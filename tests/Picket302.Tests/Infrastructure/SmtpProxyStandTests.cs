using System.Net;
using System.Net.Sockets;
using System.Text;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Mail;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// S37 (ТЗ §8.7, функция 95): SMTP через HTTP CONNECT-прокси на локальном
/// стенде — маршрут (CONNECT ведёт к SMTP-серверу, F-B2), авторизация
/// (Basic с DPAPI-паролем), отказ/таймаут; секреты не попадают в логи и
/// сообщения об ошибках. Реальная сеть не используется.
/// </summary>
public sealed class SmtpProxyStandTests
{
    private const string Логин = "user@example.test";
    private const string ПарольSmtp = "пароль-тест";
    private const string ПарольПрокси = "пароль-прокси";

    /// <summary>Отправляет письмо через стенд «прокси → SMTP».</summary>
    private static (bool Успех, string? Ошибка, SmtpProxyStand Прокси) Отправить(
        SmtpProxyStand прокси,
        bool сАвторизациейНаПрокси = false,
        int timeoutSeconds = 15)
    {
        var опции = new SmtpOptions();
        var защищённыйПарольПрокси = опции.ProtectPassword(ПарольПрокси);

        var клиент = new SmtpClientFacade(
            new SmtpSettings
            {
                Server = "smtp.example.test",
                Port = 25,
                Login = Логин,
                ProtectedPassword = опции.ProtectPassword(ПарольSmtp),
                SslMode = SslMode.None,
                TimeoutSeconds = timeoutSeconds,
            },
            new ProxySettings
            {
                Enabled = true,
                Server = "127.0.0.1",
                Port = прокси.Порт,
                Login = сАвторизациейНаПрокси ? "proxy-user" : null,
                ProtectedPassword = сАвторизациейНаПрокси ? защищённыйПарольПрокси : null,
            });

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "Проверка прокси",
            Body = "тело",
        };

        var результат = клиент.SendOne(письмо);

        return (результат.IsSuccess, результат.Error, прокси);
    }

    [Fact]
    public void Маршрут_ConnectВедётКSmtpСерверу_ОтправкаУспешна()
    {
        using var прокси = SmtpProxyStand.Запустить(авторизация: false);

        var (успех, ошибка, стенд) = Отправить(прокси);

        Assert.True(успех, $"успех=False ошибка={ошибка} соединений={стенд.ConnectЗапросы.Count} " +
            $"ошибкиПрокси=[{string.Join(" || ", стенд.ОшибкиПрокси)}]");

        // F-B2: CONNECT адресует SMTP-сервер (host:port), а не сам прокси.
        var connect = Assert.Single(стенд.ConnectЗапросы);
        Assert.Contains("CONNECT smtp.example.test:25", connect, StringComparison.Ordinal);
    }

    [Fact]
    public void АвторизацияНаПрокси_BasicЗаголовокСРасшифрованнымПаролем()
    {
        using var прокси = SmtpProxyStand.Запустить(авторизация: true);

        var (успех, ошибка, стенд) = Отправить(прокси, сАвторизациейНаПрокси: true);

        Assert.True(успех, ошибка ?? "Отправка не удалась.");

        var connect = стенд.ConnectЗапросы.Single();
        Assert.Contains("Proxy-Authorization: Basic", connect, StringComparison.Ordinal);

        // Пароль в заголовке — base64(login:пароль); расшифровка DPAPI
        // выполнена (в заголовке НЕ защищённый блок, а фактический пароль).
        var строка = connect.Split("\r\n").First(строка => строка.StartsWith("Proxy-Authorization", StringComparison.Ordinal));
        var токен = строка["Proxy-Authorization: Basic ".Length..].Trim();
        var раскодировано = Encoding.UTF8.GetString(Convert.FromBase64String(токен));

        Assert.Equal($"proxy-user:{ПарольПрокси}", раскодировано);
    }

    /// <summary>
    /// D-S37-03: 403 при ПРАВИЛЬНОЙ авторизации — ветка отказа стенда
    /// достижима; текст ошибки тот же (не-200 обрабатывается одинаково).
    /// </summary>
    [Fact]
    public void ОтказПрокси_403_ПриАвторизации_ПонятнаяОшибка()
    {
        using var прокси = SmtpProxyStand.Запустить(авторизация: true, отказ: true);

        var (успех, ошибка, _) = Отправить(прокси, сАвторизациейНаПрокси: true);

        Assert.False(успех);
        Assert.Contains("Прокси-сервер отказал в туннеле", ошибка, StringComparison.Ordinal);
    }

    [Fact]
    public void ОтказПрокси_407_ПонятнаяОшибка()
    {
        using var прокси = SmtpProxyStand.Запустить(авторизация: true, отказ: true);

        var (успех, ошибка, _) = Отправить(прокси);

        Assert.False(успех);
        Assert.Contains("Прокси-сервер отказал в туннеле", ошибка, StringComparison.Ordinal);
    }

    /// <summary>
    /// Таймаут: прокси принял соединение, но не отвечает — ограниченное
    /// ожидание, понятная ошибка, секреты не утекают.
    /// </summary>
    [Fact]
    public async Task ТаймаутПрокси_ОграниченноеОжиданиеБезУтечки()
    {
        using var прокси = SmtpProxyStand.Запустить(авторизация: true, молчать: true);

        var часы = System.Diagnostics.Stopwatch.StartNew();

        var задача = Task.Run(() => Отправить(прокси, сАвторизациейНаПрокси: true, timeoutSeconds: 2));

        var завершена = await Task.WhenAny(задача, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.True(завершена == задача, "Отправка не завершилась за отведённое время.");

        часы.Stop();

        var (успех, ошибка, _) = await задача;

        Assert.False(успех);

        // D-S37-01: ожидание ОГРАНИЧЕНО таймаутом клиента — соединение
        // держится открытым, клиент реально упирается в таймаут (не в обрыв).
        Assert.True(
            часы.Elapsed.TotalSeconds >= 1.5,
            string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Ожидание завершилось слишком быстро: {часы.Elapsed.TotalSeconds:0.0} с."));

        Assert.DoesNotContain(ПарольПрокси, ошибка, StringComparison.Ordinal);
        Assert.DoesNotContain(ПарольSmtp, ошибка, StringComparison.Ordinal);
    }
}

/// <summary>
/// Локальный HTTP CONNECT-прокси + минимальный SMTP-сервер за ним:
/// прокси проверяет авторизацию, логирует CONNECT-запрос и после успеха
/// релеит поток на SMTP-сервер.
/// </summary>
public sealed class SmtpProxyStand : IDisposable
{
    private readonly TcpListener _приёмник;
    private readonly TcpListener _smtp;
    private readonly bool _авторизация;
    private readonly bool _отказ;
    private readonly bool _молчать;
    private readonly List<Task> _задачи = [];

    private SmtpProxyStand(TcpListener приёмник, TcpListener smtp, bool авторизация, bool отказ, bool молчать)
    {
        _приёмник = приёмник;
        _smtp = smtp;
        _авторизация = авторизация;
        _отказ = отказ;
        _молчать = молчать;
    }

    /// <summary>Порт приёма прокси (клиент подключается сюда).</summary>
    public int Порт { get; private set; }

    /// <summary>CONNECT-запросы (статусные строки с целевым адресом).</summary>
    public List<string> ConnectЗапросы { get; } = [];

    /// <summary>Исключения прокси (диагностика).</summary>
    public List<string> ОшибкиПрокси { get; } = [];

    /// <summary>Запускает стенд.</summary>
    public static SmtpProxyStand Запустить(bool авторизация, bool отказ = false, bool молчать = false)
    {
        var приёмник = new TcpListener(IPAddress.Loopback, 0);
        var smtp = new TcpListener(IPAddress.Loopback, 0);

        приёмник.Start();
        smtp.Start();

        var стенд = new SmtpProxyStand(приёмник, smtp, авторизация, отказ, молчать)
        {
            Порт = ((IPEndPoint)приёмник.LocalEndpoint).Port,
        };

        стенд._задачи.Add(Task.Run(async () =>
        {
            while (true)
            {
                using var клиент = await приёмник.AcceptTcpClientAsync();
                await стенд.ОбслужитьПроксиAsync(клиент);
            }
        }));

        return стенд;
    }

    /// <summary>Ведёт CONNECT-диалог и релеит поток на SMTP.</summary>
    private async Task ОбслужитьПроксиAsync(TcpClient клиент)
    {
        using var клиентСторона = клиент;

        using var кSmtp = new TcpClient();
        await кSmtp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_smtp.LocalEndpoint).Port);

        var потокКлиента = клиентСторона.GetStream();

        if (_молчать)
        {
            // Прокси принял соединение и МОЛЧИТ: читаем, пока клиент сам не
            // закроет соединение по своему таймауту (D-S37-01 — прежняя
            // версия рвала соединение после первого чтения, клиент получал
            // обрыв вместо таймаута).
            var буфер = new byte[4096];

            try
            {
                while (await потокКлиента.ReadAsync(буфер).ConfigureAwait(false) > 0)
                {
                    // Ответа нет — просто продолжаем поглощать байты.
                }
            }
            catch (IOException)
            {
                // Клиент отсоединился по таймауту.
            }

            return;
        }

        // Чтение CONNECT-запроса до пустой строки.
        var запрос = await ПрочитатьЗаголовкиAsync(потокКлиента).ConfigureAwait(false);
ConnectЗапросы.Add(запрос);

        if (_авторизация && !запрос.Contains("Proxy-Authorization", StringComparison.Ordinal))
        {
            var отказ407 = "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic\r\n\r\n";
            var байтыОтказа = Encoding.ASCII.GetBytes(отказ407);

            await потокКлиента.WriteAsync(байтыОтказа).ConfigureAwait(false);

            return;
        }

        if (_отказ)
        {
            var отказ403 = "HTTP/1.1 403 Forbidden\r\n\r\n";
            var байтыОтказа = Encoding.ASCII.GetBytes(отказ403);

            await потокКлиента.WriteAsync(байтыОтказа).ConfigureAwait(false);

            return;
        }

        var успех = "HTTP/1.1 200 Connection established\r\n\r\n";
        var байтыУспеха = Encoding.ASCII.GetBytes(успех);

        await потокКлиента.WriteAsync(байтыУспеха).ConfigureAwait(false);

        // Реле на SMTP-сервер и его диалог.
        _задачи.Add(Task.Run(async () =>
        {
            using var smtpСторона = await _smtp.AcceptTcpClientAsync();
            await ДиалогSmtpAsync(smtpСторона.GetStream());
        }));

        // Двунаправленный реле (односторонний оставлял ответы SMTP-сервера
        // недоставленными — клиент ждал приветствие и падал по таймауту).
        var клиентКСерверу = ПересылкаAsync(потокКлиента, кSmtp.GetStream());
        var серверККлиенту = ПересылкаAsync(кSmtp.GetStream(), потокКлиента);

        await Task.WhenAny(клиентКСерверу, серверККлиенту);
    }

    /// <summary>Минимальный SMTP-диалог за прокси (LOGIN, MAIL/RCPT/DATA).</summary>
    private static async Task ДиалогSmtpAsync(NetworkStream поток)
    {
        using var читатель = new StreamReader(поток, Encoding.UTF8, false, 4096, leaveOpen: true);
        await using var писатель = new StreamWriter(поток, new ASCIIEncoding(), 4096, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\r\n",
        };

        await писатель.WriteLineAsync("220 proxy-stand ESMTP ready").ConfigureAwait(false);

        var состояние = "старт";

        while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } команда)
        {
            switch (состояние)
            {
                case "старт" when команда.StartsWith("EHLO", StringComparison.Ordinal):
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

                case "авторизован" when команда.StartsWith("MAIL FROM", StringComparison.Ordinal)
                    || команда.StartsWith("RCPT TO", StringComparison.Ordinal):
                    await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                    continue;

                case "авторизован" when команда.Equals("DATA", StringComparison.Ordinal):
                    await писатель.WriteLineAsync("354 End data").ConfigureAwait(false);

                    while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } строка && строка != ".")
                    {
                    }

                    await писатель.WriteLineAsync("250 OK queued").ConfigureAwait(false);
                    continue;

                case "авторизован" when команда.Equals("QUIT", StringComparison.Ordinal):
                    await писатель.WriteLineAsync("221 Bye").ConfigureAwait(false);
                    return;

                default:
                    await писатель.WriteLineAsync("500 Unknown").ConfigureAwait(false);
                    return;
            }
        }
    }

    /// <summary>Пересылает поток в поток (одно направление).</summary>
    private static async Task ПересылкаAsync(NetworkStream источник, NetworkStream приёмник)
    {
        var буфер = new byte[8192];

        try
        {
            while (true)
            {
                var прочитано = await источник.ReadAsync(буфер).ConfigureAwait(false);

                if (прочитано == 0)
                {
                    return;
                }

                await приёмник.WriteAsync(буфер.AsMemory(0, прочитано)).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Штатный обрыв стороны.
        }
    }

    /// <summary>Читает HTTP-заголовки до пустой строки.</summary>
    private static async Task<string> ПрочитатьЗаголовкиAsync(NetworkStream поток)
    {
        var буфер = new List<byte>();
        var байт = new byte[1];

        while (true)
        {
            var получено = await поток.ReadAsync(байт).ConfigureAwait(false);

            if (получено <= 0)
            {
                break;
            }

            буфер.Add(байт[0]);

            var длина = буфер.Count;

            if (длина >= 4
                && буфер[длина - 4] == 13
                && буфер[длина - 3] == 10
                && буфер[длина - 2] == 13
                && буфер[длина - 1] == 10)
            {
                break;
            }

            if (длина > 8192)
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(буфер.ToArray());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _приёмник.Stop();
            _smtp.Stop();
        }
        catch (SocketException)
        {
            // Повторная остановка безопасна.
        }
    }
}
