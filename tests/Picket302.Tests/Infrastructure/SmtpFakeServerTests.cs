using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Mail;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Регрессии F-B1 и F-B2: собственный SMTP-клиент против минимального
/// эмулятора сервера на <see cref="TcpListener"/>.
///
/// F-B1 — промежуточные коды 334/354 прежде считались отказом: после AUTH
/// PLAIN токен уходил в фолбэк LOGIN, ответ на DATA прерывал передачу тела.
/// F-B2 — при включённом прокси сокет подключался к SMTP-серверу напрямую,
/// и CONNECT уходил не по адресу.
/// </summary>
public sealed class SmtpFakeServerTests
{
    private const string Логин = "user@example.test";
    private const string Пароль = "пароль-тест";

    [Fact]
    public void Отправка_AuthLogin_ПромежуточныеКодыПринимаются()
    {
        using var сервер = ФейковыйSmtp.Запустить("LOGIN");

        var опции = new SmtpOptions();
        var клиент = new SmtpClientFacade(
            new SmtpSettings
            {
                Server = "127.0.0.1",
                Port = сервер.Порт,
                Login = Логин,
                ProtectedPassword = опции.ProtectPassword(Пароль),
                SslMode = SslMode.None,
            });

        var результат = клиент.SendOne(Письмо());

        Assert.True(результат.IsSuccess, результат.Error ?? "Отправка не удалась.");
        Assert.Equal(250, результат.SmtpCode);

        // Корректная последовательность команд в логе сервера.
        Assert.Contains("EHLO", сервер.Команды.First(c => c.StartsWith("EHLO", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("AUTH LOGIN", сервер.Команды);
        Assert.Equal(Логин, DecodeBase64(сервер.Команды[сервер.Команды.IndexOf("AUTH LOGIN") + 1]));
        Assert.Equal(Пароль, DecodeBase64(сервер.Команды[сервер.Команды.IndexOf("AUTH LOGIN") + 2]));
        Assert.Contains(сервер.Команды, c => c.StartsWith("MAIL FROM:<", StringComparison.Ordinal));
        Assert.Contains(сервер.Команды, c => c.StartsWith("RCPT TO:<", StringComparison.Ordinal));
        Assert.Contains("DATA", сервер.Команды);
        Assert.Contains("<тело письма>", сервер.Команды);
        Assert.Contains("QUIT", сервер.Команды);
    }

    [Fact]
    public void Отправка_AuthPlain_ТокенОтправляетсяПоОтвету334()
    {
        using var сервер = ФейковыйSmtp.Запустить("PLAIN");

        var опции = new SmtpOptions();
        var клиент = new SmtpClientFacade(
            new SmtpSettings
            {
                Server = "127.0.0.1",
                Port = сервер.Порт,
                Login = Логин,
                ProtectedPassword = опции.ProtectPassword(Пароль),
                SslMode = SslMode.None,
            });

        var результат = клиент.SendOne(Письмо());

        Assert.True(результат.IsSuccess, результат.Error ?? "Отправка не удалась.");

        // AUTH PLAIN принят по 334: токен отправлен вторым Выполнить,
        // фолбэк AUTH LOGIN не выполнялся.
        Assert.Contains("AUTH PLAIN", сервер.Команды);
        Assert.DoesNotContain("AUTH LOGIN", сервер.Команды);

        var токен = сервер.Команды[сервер.Команды.IndexOf("AUTH PLAIN") + 1];

        Assert.Equal($"\0{Логин}\0{Пароль}", DecodeBase64(токен));
        Assert.Contains("QUIT", сервер.Команды);
    }

    [Fact]
    public void Отправка_ЧерезПрокси_ConnectУходитНаПрокси()
    {
        using var сервер = ФейковыйSmtp.Запустить("LOGIN");
        using var прокси = ФейковыйПрокси.Запустить(сервер.Порт);

        var опции = new SmtpOptions();
        var клиент = new SmtpClientFacade(
            new SmtpSettings
            {
                Server = "smtp.example.test",
                Port = 25,
                Login = Логин,
                ProtectedPassword = опции.ProtectPassword(Пароль),
                SslMode = SslMode.None,
            })
        {
            Прокси = new ProxySettings
            {
                Enabled = true,
                Server = "127.0.0.1",
                Port = прокси.Порт,
            },
        };

        var результат = клиент.SendOne(Письмо());

        Assert.True(результат.IsSuccess, результат.Error ?? "Отправка через прокси не удалась.");

        // CONNECT адресован цели (SMTP-сервер), а не самому себе.
        Assert.Contains(прокси.Команды, c =>
            c.StartsWith("CONNECT smtp.example.test:25 ", StringComparison.Ordinal));
        Assert.Contains("Host: smtp.example.test:25", прокси.Команды);

        // Диалог SMTP прошёл через туннель.
        Assert.Contains("AUTH LOGIN", сервер.Команды);
        Assert.Contains("QUIT", сервер.Команды);
    }

    /// <summary>
    /// Письмо для проверок: один получатель, простое тело.
    /// </summary>
    private static MailMessage Письмо()
    {
        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "Проверка",
            Body = "Тело письма",
        };

        return письмо;
    }

    /// <summary>
    /// Декодирует команду base64 с устойчивостью к отсутствию значения.
    /// </summary>
    private static string DecodeBase64(string команда)
        => Encoding.UTF8.GetString(Convert.FromBase64String(команда));

    /// <summary>
    /// Минимальный эмулятор SMTP-сервера: один клиент, сценарий
    /// приветствие → EHLO → AUTH (LOGIN либо PLAIN, с промежуточными 334)
    /// → MAIL → RCPT → DATA (354, тело, точка) → QUIT. Ведёт лог команд;
    /// тело письма записывается одной строкой-маркером.
    /// </summary>
    private sealed class ФейковыйSmtp : IDisposable
    {
        private readonly TcpListener _слушатель;
        private Task _диалог = Task.CompletedTask;

        private ФейковыйSmtp(TcpListener слушатель, string механизм)
        {
            _слушатель = слушатель;
            Механизм = механизм;
        }

        /// <summary>Механизм авторизации: «LOGIN» либо «PLAIN».</summary>
        public string Механизм { get; }

        /// <summary>Порт прослушивания.</summary>
        public int Порт { get; private set; }

        /// <summary>Лог принятых команд; тело письма — маркер «&lt;тело письма&gt;».</summary>
        public List<string> Команды { get; } = [];

        /// <summary>Запускает эмулятор и возвращает его готовым к подключению.</summary>
        public static ФейковыйSmtp Запустить(string механизм)
        {
            var слушатель = new TcpListener(IPAddress.Loopback, 0);

            слушатель.Start();

            var сервер = new ФейковыйSmtp(слушатель, механизм)
            {
                Порт = ((IPEndPoint)слушатель.LocalEndpoint).Port,
            };

            сервер._диалог = Task.Run(async () =>
            {
                using var клиент = await слушатель.AcceptTcpClientAsync();

                await сервер.ОбслужитьAsync(клиент);
            });

            return сервер;
        }

        /// <summary>Ведёт диалог с одним клиентом.</summary>
        private async Task ОбслужитьAsync(TcpClient клиент)
        {
            var поток = клиент.GetStream();

            using var читатель = new StreamReader(поток, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var писатель = new StreamWriter(поток, new ASCIIEncoding(), 4096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n",
            };

            await писатель.WriteLineAsync("220 fake ESMTP ready").ConfigureAwait(false);

            // Состояния диалога: старт → ждётТокен/ждётЛогин → ждётПароль → диалог.
            var состояние = "старт";

            while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } команда)
            {
                Команды.Add(команда);

                switch (состояние)
                {
                    case "старт" when команда.StartsWith("EHLO", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250-fake greets you").ConfigureAwait(false);
                        await писатель.WriteLineAsync($"250 AUTH {Механизм}").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.StartsWith("AUTH PLAIN", StringComparison.Ordinal)
                        && Механизм != "PLAIN":
                        // Сервер поддерживает только заявленный механизм:
                        // отказ PLAIN продолжает диалог для фолбэка LOGIN.
                        await писатель.WriteLineAsync("504 Mechanism not supported").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.StartsWith("AUTH PLAIN", StringComparison.Ordinal):
                        состояние = "ждётТокен";
                        await писатель.WriteLineAsync("334 ").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.StartsWith("AUTH LOGIN", StringComparison.Ordinal):
                        состояние = "ждётЛогин";
                        await писатель.WriteLineAsync("334 Username:").ConfigureAwait(false);
                        continue;

                    case "ждётТокен":
                        состояние = "авторизован";
                        await писатель.WriteLineAsync("235 Authentication successful").ConfigureAwait(false);
                        continue;

                    case "ждётЛогин":
                        состояние = "ждётПароль";
                        await писатель.WriteLineAsync("334 Password:").ConfigureAwait(false);
                        continue;

                    case "ждётПароль":
                        состояние = "авторизован";
                        await писатель.WriteLineAsync("235 Authentication successful").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.StartsWith("MAIL FROM", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.StartsWith("RCPT TO", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.Equals("DATA", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>").ConfigureAwait(false);
                        await ПрочитатьТелоAsync(читатель).ConfigureAwait(false);
                        Команды.Add("<тело письма>");
                        await писатель.WriteLineAsync("250 OK queued").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.Equals("QUIT", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("221 Bye").ConfigureAwait(false);
                        return;

                    default:
                        await писатель.WriteLineAsync("500 Неизвестная команда").ConfigureAwait(false);
                        return;
                }
            }
        }

        /// <summary>
        /// Читает тело письма до строки «.» (RFC 5321) и дописывает в лог.
        /// </summary>
        private static async Task ПрочитатьТелоAsync(StreamReader читатель)
        {
            while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } строка)
            {
                if (строка == ".")
                {
                    return;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                _диалог.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Клиент мог оборвать соединение: результат диалога не важен.
            }

            _слушатель.Stop();
        }
    }

    /// <summary>
    /// Фейковый HTTP-прокси: читает CONNECT-запрос, отвечает 200 и далее
    /// перекачивает байты между клиентом и целевым сервером.
    /// </summary>
    private sealed class ФейковыйПрокси : IDisposable
    {
        private readonly TcpListener _слушатель;
        private Task _цикл = Task.CompletedTask;

        private ФейковыйПрокси(TcpListener слушатель)
        {
            _слушатель = слушатель;
        }

        /// <summary>Порт прослушивания.</summary>
        public int Порт { get; private set; }

        /// <summary>Лог строк CONNECT-запроса.</summary>
        public List<string> Команды { get; } = [];

        /// <summary>Запускает прокси, ведущий к целевому порту.</summary>
        public static ФейковыйПрокси Запустить(int целевойПорт)
        {
            var слушатель = new TcpListener(IPAddress.Loopback, 0);

            слушатель.Start();

            var прокси = new ФейковыйПрокси(слушатель)
            {
                Порт = ((IPEndPoint)слушатель.LocalEndpoint).Port,
            };

            прокси._цикл = Task.Run(async () =>
            {
                using var клиент = await слушатель.AcceptTcpClientAsync().ConfigureAwait(false);

                await прокси.ОбслужитьAsync(клиент, целевойПорт).ConfigureAwait(false);
            });

            return прокси;
        }

        /// <summary>Принимает CONNECT и открывает туннель.</summary>
        private async Task ОбслужитьAsync(TcpClient клиент, int целевойПорт)
        {
            var потокКлиента = клиент.GetStream();

            // Заголовки читаются побайтово: StreamReader мог бы забрать в свой
            // буфер первые байты SMTP-диалога, идущие сразу после пустой строки.
            var накоплено = new List<byte>();

            while (true)
            {
                var байт = потокКлиента.ReadByte();

                if (байт < 0)
                {
                    throw new IOException("Прокси: клиент закрыл соединение.");
                }

                накоплено.Add((byte)байт);

                var длина = накоплено.Count;

                if (длина >= 4
                    && накоплено[длина - 4] == 13
                    && накоплено[длина - 3] == 10
                    && накоплено[длина - 2] == 13
                    && накоплено[длина - 1] == 10)
                {
                    break;
                }

                if (длина > 8192)
                {
                    throw new IOException("Прокси: превышен размер CONNECT-запроса.");
                }
            }

            // Логируем строки CONNECT-запроса без завершающих пустых строк.
            var запрос = Encoding.ASCII.GetString(накоплено.ToArray()).TrimEnd('\r', '\n');

            foreach (var строкаЗапроса in запрос.Split("\r\n"))
            {
                if (строкаЗапроса.Length > 0)
                {
                    Команды.Add(строкаЗапроса);
                }
            }

            var байтыОтвета = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n");

            await потокКлиента.WriteAsync(байтыОтвета).ConfigureAwait(false);
            await потокКлиента.FlushAsync().ConfigureAwait(false);

            using var цель = new TcpClient { NoDelay = true };

            await цель.ConnectAsync(IPAddress.Loopback, целевойПорт).ConfigureAwait(false);

            var потокЦели = цель.GetStream();

            await Task.WhenAll(
                ПерекачатьAsync(потокКлиента, потокЦели),
                ПерекачатьAsync(потокЦели, потокКлиента)).ConfigureAwait(false);
        }

        /// <summary>Перекачивает байты между потоками до разрыва соединения.</summary>
        private static async Task ПерекачатьAsync(Stream из, Stream в)
        {
            var буфер = new byte[8192];

            int получено;

            while ((получено = await из.ReadAsync(буфер).ConfigureAwait(false)) > 0)
            {
                await в.WriteAsync(буфер.AsMemory(0, получено)).ConfigureAwait(false);
                await в.FlushAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                _цикл.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Туннель мог оборваться по завершении клиента: не важно.
            }

            _слушатель.Stop();
        }
    }
}
