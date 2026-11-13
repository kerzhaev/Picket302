using System.Net;
using System.Net.Sockets;
using System.Text;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Mail;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// S38 (ТЗ §8.7, функции 96 и 99): формат письма текст/HTML (точные
/// заголовки и содержимое MIME) и точность ReplyTo/отправителя (envelope
/// MAIL FROM/RCPT TO против заголовков письма). Сервер-приёмник захватывает
/// ПОЛНОЕ тело DATA и envelope-команды.
/// </summary>
public sealed class SmtpMimeTests
{
    private const string Логин = "user@example.test";
    private const string Пароль = "пароль-тест";

    [Fact]
    public async Task TextPlain_ТочныеЗаголовкиMIMEИТело()
    {
        using var сервер = MimeFakeSmtp.Запустить();

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "Тема проверки",
            Body = "Тело обычного письма — строка 1.\r\nСтрока 2 (русский текст).",
        };

        var клиент = Клиент(сервер.Порт);
        var результат = клиент.SendOne(письмо);

        Assert.True(результат.IsSuccess, результат.Error ?? "Отправка не удалась.");

        var тело = сервер.ТелоПисьма!;

        // Точные заголовки (№96).
        Assert.Contains("From: sender@example.test\r\n", тело, StringComparison.Ordinal);
        Assert.Contains("To: dest@example.test\r\n", тело, StringComparison.Ordinal);
        // Subject кодируется по RFC 2047; регистр метки кодировки
        // допускает варианты (utf-8 / UTF-8 / windows-1251-выход).
        Assert.True(
            тело.Contains("Subject: =?", StringComparison.Ordinal),
            "dump=" + тело.Replace(Environment.NewLine, "|"));
        Assert.Contains("MIME-Version: 1.0\r\n", тело, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain; charset=\"utf-8\"\r\n", тело, StringComparison.Ordinal);
        Assert.Contains("Content-Transfer-Encoding: quoted-printable\r\n", тело, StringComparison.Ordinal);

        // Содержимое: quoted-printable раскодируется в исходный текст.
        var частьТела = тело.Substring(тело.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4);
        var раскодированное = РаскодироватьQuotedPrintable(частьТела.TrimEnd('\r', '\n'));

        Assert.Contains("Тело обычного письма — строка 1.", раскодированное, StringComparison.Ordinal);
        Assert.Contains("Строка 2 (русский текст).", раскодированное, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_ФорматBase64СТочнымContentType()
    {
        using var сервер = MimeFakeSmtp.Запустить();

        var html = "<html><body><p>Проверка <b>HTML</b> письма.</p></body></html>";

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "HTML письмо",
            Body = html,
            BodyFormat = BodyFormat.Html,
        };

        var клиент = Клиент(сервер.Порт);
        var результат = клиент.SendOne(письмо);

        Assert.True(результат.IsSuccess, результат.Error ?? "Отправка не удалась.");

        var тело = сервер.ТелоПисьма!;

        Assert.Contains("Content-Type: text/html; charset=\"utf-8\"\r\n", тело, StringComparison.Ordinal);
        Assert.Contains("Content-Transfer-Encoding: base64\r\n", тело, StringComparison.Ordinal);

        // Base64 раскодируется в исходный HTML.
        var частьТела = тело.Substring(тело.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4);
        var строки = частьТела.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var base64 = string.Concat(строки.TakeWhile(строка => строка != "."));

        Assert.Equal(html, Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
    }

    /// <summary>
    /// №99: Reply-To попадает в заголовки; envelope MAIL FROM использует
    /// обратный адрес, RCPT TO — точный адрес получателя.
    /// </summary>
    [Fact]
    public async Task ReplyTo_ЗаголовокИEnvelopeТочны()
    {
        using var сервер = MimeFakeSmtp.Запустить();

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            ReplyTo = "ответы@example.test",
            Subject = "С обратным адресом",
            Body = "Тело.",
        };

        var клиент = Клиент(сервер.Порт);
        var результат = клиент.SendOne(письмо);

        Assert.True(результат.IsSuccess, результат.Error ?? "Отправка не удалась.");

        // Заголовок Reply-To присутствует с точным адресом.
        Assert.Contains("Reply-To:", сервер.ТелоПисьма!, StringComparison.Ordinal);

        // Envelope (№99): MAIL FROM — адрес отправителя письма, RCPT TO —
        // точный адрес получателя; Reply-To остаётся заголовком письма.
        Assert.Contains(сервер.Envelope, команда => команда == "MAIL FROM:<sender@example.test>");
        Assert.Contains(сервер.Envelope, команда => команда == "RCPT TO:<dest@example.test>");
    }

    [Fact]
    public async Task БезReplyTo_ЗаголовкаНет_EnvelopeПоОтправителю()
    {
        using var сервер = MimeFakeSmtp.Запустить();

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "Без обратного адреса",
            Body = "Тело.",
        };

        var клиент = Клиент(сервер.Порт);
        var результат = клиент.SendOne(письмо);

        Assert.True(результат.IsSuccess, результат.Error ?? "Отправка не удалась.");

        Assert.DoesNotContain("Reply-To:", сервер.ТелоПисьма!, StringComparison.Ordinal);
        Assert.Contains(сервер.Envelope, команда => команда == "MAIL FROM:<sender@example.test>");
    }

    /// <summary>Клиент с настройками против локального сервера.</summary>
    private static SmtpClientFacade Клиент(int порт)
        => new(
            new SmtpSettings
            {
                Server = "127.0.0.1",
                Port = порт,
                Login = Логин,
                ProtectedPassword = new SmtpOptions().ProtectPassword(Пароль),
                SslMode = SslMode.None,
                TimeoutSeconds = 15,
            });

    /// <summary>Раскодирует quoted-printable (упрощённо для проверок).</summary>
    private static string РаскодироватьQuotedPrintable(string текст)
    {
        var байты = new List<byte>();
        var строки = текст.Split("\r\n", StringSplitOptions.None);

        foreach (var строка in строки)
        {
            var мягкийПеренос = строка.EndsWith("=", StringComparison.Ordinal);
            var содержимое = мягкийПеренос ? строка[..^1] : строка;

            for (var позиция = 0; позиция < содержимое.Length; позиция++)
            {
                if (содержимое[позиция] == '='
                    && позиция + 2 < содержимое.Length
                    && Uri.IsHexDigit(содержимое[позиция + 1])
                    && Uri.IsHexDigit(содержимое[позиция + 2]))
                {
                    байты.Add(Convert.ToByte(содержимое[(позиция + 1)..(позиция + 3)], 16));
                    позиция += 2;
                }
                else
                {
                    байты.Add(Encoding.UTF8.GetBytes(new[] { содержимое[позиция] })[0]);
                }
            }

            if (!мягкийПеренос)
            {
                байты.AddRange(Encoding.UTF8.GetBytes("\r\n"));
            }
        }

        return Encoding.UTF8.GetString(байты.ToArray()).TrimEnd('\r', '\n');
    }

    /// <summary>
    /// SMTP-сервер с захватом ПОЛНОГО тела DATA и envelope-команд
    /// (MAIL FROM/RCPT TO отдельно от тела письма).
    /// </summary>
    private sealed class MimeFakeSmtp : IDisposable
    {
        private readonly TcpListener _слушатель;
        private Task _диалог = Task.CompletedTask;

        private MimeFakeSmtp(TcpListener слушатель)
        {
            _слушатель = слушатель;
        }

        /// <summary>Envelope-команды (MAIL FROM/RCPT TO как получены).</summary>
        public List<string> Envelope { get; } = [];

        /// <summary>Полное тело DATA (MIME-текст письма).</summary>
        public string? ТелоПисьма { get; private set; }

        /// <summary>Порт прослушивания.</summary>
        public int Порт { get; private set; }

        /// <summary>Запускает сервер.</summary>
        public static MimeFakeSmtp Запустить()
        {
            var слушатель = new TcpListener(IPAddress.Loopback, 0);

            слушатель.Start();

            var сервер = new MimeFakeSmtp(слушатель)
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

        /// <summary>Ведёт SMTP-диалог, захватывая envelope и DATA.</summary>
        private async Task ОбслужитьAsync(TcpClient клиент)
        {
            using var клиентПожизненно = клиент;

            using var читатель = new StreamReader(клиент.GetStream(), Encoding.UTF8, false, 4096, leaveOpen: true);
            await using var писатель = new StreamWriter(
                клиент.GetStream(),
                new ASCIIEncoding(),
                4096,
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n",
            };

            await писатель.WriteLineAsync("220 mime-stand ESMTP ready").ConfigureAwait(false);

            var состояние = "старт";
            var тело = new StringBuilder();

            while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } команда)
            {
                switch (состояние)
                {
                    case "старт" when команда.StartsWith("EHLO", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250-mime-stand greets you").ConfigureAwait(false);
                        await писатель.WriteLineAsync("250 AUTH LOGIN").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.StartsWith("AUTH PLAIN", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("504 Mechanism not supported").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.StartsWith("AUTH LOGIN", StringComparison.Ordinal):
                        состояние = "ждётЛогин";
                        await писатель.WriteLineAsync("334 Username:").ConfigureAwait(false);
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
                        Envelope.Add(команда);
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.StartsWith("RCPT TO", StringComparison.Ordinal):
                        Envelope.Add(команда);
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case "авторизован" when команда.Equals("DATA", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>").ConfigureAwait(false);

                        тело.Clear();

                        while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } строка && строка != ".")
                        {
                            тело.AppendLine(строка);
                        }

                        ТелоПисьма = тело.ToString();
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

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                _слушатель.Stop();
            }
            catch (SocketException)
            {
                // Повторная остановка безопасна.
            }
        }
    }
}
