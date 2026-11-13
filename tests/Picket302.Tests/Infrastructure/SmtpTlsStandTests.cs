using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Mail;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// S36 (ТЗ §8.7, функция 94): SSL-соединение SMTP на локальном стенде —
/// режимы None / StartTls / ImplicitTls, отказы (сервер без STARTTLS,
/// отпечаток сертификата не совпал) и доказательство «plaintext ≠ защищённая
/// отправка»: сниффер-прослойка записывает байты клиента до шифрования.
/// Сертификат стенда — самоподписанный, доверие — по отпечатку (ТЗ: решение
/// пользователя). Физический/внешний SMTP не используется.
/// </summary>
public sealed class SmtpTlsStandTests
{
    private const string Логин = "user@example.test";
    private const string Пароль = "пароль-тест";

    /// <summary>Самоподписанный сертификат стенда + отпечаток.</summary>
    private static (X509Certificate2 Сертификат, string Отпечаток) СоздатьСертификат()
    {
        using var rsa = RSA.Create(2048);
        var запрос = new CertificateRequest(
            "CN=fake-smtp",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var эфемерный = запрос.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));

        // SChannel (SslStream-сервер на Windows) не поддерживает эфемерные
        // ключи: ключ персистируется через PFX-реэкспорт.
        var pfx = эфемерный.Export(X509ContentType.Pfx);
        var сертификат = new X509Certificate2(
            pfx,
            (string?)null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);

        return (сертификат, сертификат.Thumbprint);
    }

    /// <summary>
    /// ImplicitTls: успешная отправка через TLS с доверием по отпечатку;
    /// сниффер не видит ни MAIL FROM, ни AUTH-данных; сервер после
    /// рукопожатия видит команды расшифрованными.
    /// </summary>
    [Fact]
    public async Task ImplicitTls_ЗащищённаяОтправка_PlaintextНеВиден()
    {
        var (сертификат, отпечаток) = СоздатьСертификат();

        using var стенд = await TlsFakeSmtp.ЗапуститьAsync(
            TlsFakeSmtp.Режим.ImplicitTls,
            сертификат,
            рекламироватьStartTls: false);

        var клиент = Клиент(стенд.ПортСниффера, отпечаток);

        var результат = клиент.SendOne(Письмо());

        var дамп = $"успех={результат.IsSuccess} ошибка={результат.Error} ошибкиДиалога={string.Join(" || ", стенд.ОшибкиДиалога)}";

        Assert.True(результат.IsSuccess, дамп);
        Assert.Equal(250, результат.SmtpCode);

        // Сервер (после расшифровки) видит полный диалог.
        Assert.Contains(стенд.Команды, команда => команда.Contains("MAIL FROM", StringComparison.Ordinal));
        Assert.Contains("<тело письма>", стенд.Команды);

        // Сниффер видит только шифрованный поток: ни команды отправки, ни
        // учётных данных в открытом тексте.
        Assert.DoesNotContain("MAIL FROM", стенд.ОткрытыйТекст, StringComparison.Ordinal);
        Assert.DoesNotContain(Логин, стенд.ОткрытыйТекст, StringComparison.Ordinal);
        Assert.DoesNotContain("<тело письма>", стенд.ОткрытыйТекст, StringComparison.Ordinal);
    }

    /// <summary>
    /// StartTls: согласование через EHLO, рукопожатие после 220, отправка
    /// защищена — сниффер не видит команд.
    /// </summary>
    [Fact]
    public async Task StartTls_СогласованиеИЗащита()
    {
        var (сертификат, отпечаток) = СоздатьСертификат();

        using var стенд = await TlsFakeSmtp.ЗапуститьAsync(
            TlsFakeSmtp.Режим.StartTls,
            сертификат,
            рекламироватьStartTls: true);

        var клиент = Клиент(стенд.ПортСниффера, отпечаток, режим: SslMode.StartTls);

        var результат = клиент.SendOne(Письмо());

        var дамп = $"успех={результат.IsSuccess} ошибка={результат.Error} " +
            $"команды=[{string.Join(" | ", стенд.Команды)}] ошибкиДиалога=[{string.Join(" || ", стенд.ОшибкиДиалога)}]";

        Assert.True(результат.IsSuccess, дамп);

        // Клиент согласовал STARTTLS (сервер видел команду до шифрования —
        // это нормально: согласование защиты идёт в открытом виде).
        Assert.Contains(стенд.Команды, команда => команда.Contains("STARTTLS", StringComparison.Ordinal));

        // Защищённая часть: MAIL FROM в сниффере отсутствует.
        Assert.DoesNotContain("MAIL FROM", стенд.ОткрытыйТекст, StringComparison.Ordinal);
        Assert.DoesNotContain("<тело письма>", стенд.ОткрытыйТекст, StringComparison.Ordinal);
    }

    /// <summary>
    /// Отказ: сервер не рекламирует STARTTLS — клиент не отправляет AUTH
    /// (учётные данные не утекают в открытом тексте) и возвращает понятную
    /// ошибку.
    /// </summary>
    [Fact]
    public async Task StartTls_СерверБезПоддержки_ОтказБезУтечкиУчётныхДанных()
    {
        var (сертификат, _) = СоздатьСертификат();

        using var стенд = await TlsFakeSmtp.ЗапуститьAsync(
            TlsFakeSmtp.Режим.БезTls,
            сертификат,
            рекламироватьStartTls: false);

        var клиент = Клиент(
            стенд.ПортСниффера,
            отпечатокДоверенного: null,
            доверятьСамоподписанным: false,
            режим: SslMode.StartTls);

        var результат = клиент.SendOne(Письмо());

        Assert.False(результат.IsSuccess);
        Assert.Contains("STARTTLS", результат.Error, StringComparison.Ordinal);

        // Утечки нет: AUTH в перехваченном открытом тексте отсутствует.
        Assert.DoesNotContain("AUTH", стенд.ОткрытыйТекст, StringComparison.Ordinal);
    }

    /// <summary>
    /// Отказ по отпечатку: доверие включено, но отпечаток не совпал —
    /// отправка прерывается, фактический отпечаток сохраняется для решения
    /// пользователя (продолжение F-B9).
    /// </summary>
    [Fact]
    public async Task ОтпечатокНеСовпал_ОтказСФиксациейОтпечатка()
    {
        var (сертификат, _) = СоздатьСертификат();

        using var стенд = await TlsFakeSmtp.ЗапуститьAsync(
            TlsFakeSmtp.Режим.ImplicitTls,
            сертификат,
            рекламироватьStartTls: false);

        var чужойОтпечаток = new string('A', 40);

        var клиент = Клиент(стенд.ПортСниффера, чужойОтпечаток);

        var результат = клиент.SendOne(Письмо());

        Assert.False(результат.IsSuccess);

        // Отклонённый отпечаток зафиксирован для диалога доверия.
        Assert.Equal(сертификат.Thumbprint, клиент.ОтклонённыйОтпечаток);
    }

    /// <summary>
    /// Контраст: режим None — те же команды видны снифферу в открытом тексте
    /// (доказательство различия plaintext ≠ защищённая отправка).
    /// </summary>
    [Fact]
    public async Task None_ОткрытыйТекстВиденСнифферу()
    {
        using var стенд = await TlsFakeSmtp.ЗапуститьAsync(
            TlsFakeSmtp.Режим.БезTls,
            сертификат: null,
            рекламироватьStartTls: false);

        var клиент = Клиент(
            стенд.ПортСниффера,
            отпечатокДоверенного: null,
            доверятьСамоподписанным: false,
            режим: SslMode.None);

        var результат = клиент.SendOne(Письмо());

        var дамп = $"успех={результат.IsSuccess} ошибка={результат.Error} " +
            $"байт={стенд.ОткрытыеБайты.Count} head={Convert.ToHexString(стенд.ОткрытыеБайты.Take(60).ToArray())} " +
            $"текст={стенд.ОткрытыйТекст[..Math.Min(120, стенд.ОткрытыйТекст.Length)]} " +
            $"команды={string.Join("|", стенд.Команды.Take(6))}";

        Assert.True(результат.IsSuccess, дамп);

        // Открытый текст содержит и команду отправки, и base64 логина.
        Assert.Contains("MAIL FROM", стенд.ОткрытыйТекст, StringComparison.Ordinal);
        Assert.Contains("AUTH", стенд.ОткрытыйТекст, StringComparison.Ordinal);
    }

    /// <summary>Клиент с указанным режимом защиты и доверием по отпечатку.</summary>
    private static SmtpClientFacade Клиент(
        int порт,
        string? отпечатокДоверенного,
        bool доверятьСамоподписанным = true,
        SslMode режим = SslMode.ImplicitTls)
        => new(
            new SmtpSettings
            {
                Server = "127.0.0.1",
                Port = порт,
                Login = Логин,
                ProtectedPassword = new SmtpOptions().ProtectPassword(Пароль),
                SslMode = режим,
                TrustSelfSignedCertificate = доверятьСамоподписанным,
                TrustedCertificateFingerprint = отпечатокДоверенного,
                TimeoutSeconds = 15,
            });

    /// <summary>Письмо с маркером тела для лога сервера.</summary>
    private static MailMessage Письмо() => new(["dest@example.test"])
    {
        From = "sender@example.test",
        Subject = "Проверка TLS",
        Body = "<тело письма>",
    };

    /// <summary>
    /// TLS-версия эмулятора SMTP: None / StartTls / ImplicitTls + сниффер
    /// открытого текста (pass-through listener записывает байты клиента).
    /// </summary>
    private sealed class TlsFakeSmtp : IDisposable
    {
        /// <summary>Режим защиты стенда.</summary>
        public enum Режим
        {
            /// <summary>Без TLS.</summary>
            БезTls = 0,

            /// <summary>STARTTLS по согласовании.</summary>
            StartTls = 1,

            /// <summary>TLS с первого байта (порт 465-стиль).</summary>
            ImplicitTls = 2,
        }

        private readonly TcpListener _сниффер;
        private readonly TcpListener _сервер;
        private readonly X509Certificate2? _сертификат;
        private readonly Режим _режим;
        private readonly bool _рекламироватьStartTls;
        private Task _транзит = Task.CompletedTask;
        private Task _диалог = Task.CompletedTask;

        private TlsFakeSmtp(
            TcpListener сниффер,
            TcpListener сервер,
            X509Certificate2? сертификат,
            Режим режим,
            bool рекламироватьStartTls)
        {
            _сниффер = сниффер;
            _сервер = сервер;
            _сертификат = сертификат;
            _режим = режим;
            _рекламироватьStartTls = рекламироватьStartTls;
        }

        /// <summary>Порт сниффера — адрес, к которому подключается клиент.</summary>
        public int ПортСниффера { get; private set; }

        /// <summary>Команды, видимые СЕРВЕРУ (после расшифровки, где применимо).</summary>
        public List<string> Команды { get; } = [];

        /// <summary>Открытый текст, перехваченный сниффером (байты клиента).</summary>
        public string ОткрытыйТекст => Encoding.UTF8.GetString(ОткрытыеБайты.ToArray());

        /// <summary>Перехваченные байты клиента (до шифрования и после).</summary>
        public List<byte> ОткрытыеБайты { get; } = [];

        /// <summary>Запускает стенд: сниффер + настоящий сервер.</summary>
        public static async Task<TlsFakeSmtp> ЗапуститьAsync(
            Режим режим,
            X509Certificate2? сертификат,
            bool рекламироватьStartTls)
        {
            var сниффер = new TcpListener(IPAddress.Loopback, 0);
            var сервер = new TcpListener(IPAddress.Loopback, 0);

            сниффер.Start();
            сервер.Start();

            var стенд = new TlsFakeSmtp(сниффер, сервер, сертификат, режим, рекламироватьStartTls)
            {
                ПортСниффера = ((IPEndPoint)сниффер.LocalEndpoint).Port,
            };

            стенд._транзит = Task.Run(стенд.ТранзитAsync);
            стенд._диалог = Task.Run(async () =>
            {
                using var клиент = await сервер.AcceptTcpClientAsync();
                Stream поток = клиент.GetStream();
                var tlsСогласован = false;
                if (стенд._режим == TlsFakeSmtp.Режим.ImplicitTls)
                {
                    try
                    {
                        поток = await стенд.ЗавернутьTlsAsync(поток);
                        tlsСогласован = true;
                    }
                    catch (Exception ошибка)
                    {
                        стенд.ОшибкиДиалога.Add(ошибка.ToString());
                        return;
                    }
                }
                await стенд.ДиалогAsync(поток, tlsСогласован);
            });

            await Task.Delay(50);

            return стенд;
        }

        /// <summary>
        /// Транзит: клиент → сниффер (запись байтов) → настоящий сервер и
        /// обратно. Байты записываются как есть — до шифрования.
        /// </summary>
        private async Task ТранзитAsync()
        {
            using var клиентСторона = await _сниффер.AcceptTcpClientAsync();

            using var кСерверу = new TcpClient();
            await кСерверу.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_сервер.LocalEndpoint).Port);

            var потокКлиента = клиентСторона.GetStream();
            var потокСервера = кСерверу.GetStream();

            var клиентКСерверу = ПересылкаAsync(потокКлиента, потокСервера, ОткрытыеБайты);
            var серверККлиенту = ПересылкаAsync(потокСервера, потокКлиента, null);

            await Task.WhenAny(клиентКСерверу, серверККлиенту);
        }

        /// <summary>Пересылает поток в поток, опционально записывая байты.</summary>
        private static async Task ПересылкаAsync(
            NetworkStream источник,
            NetworkStream приёмник,
            List<byte>? журнал)
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

                    журнал?.AddRange(буфер[..прочитано]);

                    await приёмник.WriteAsync(буфер.AsMemory(0, прочитано)).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                // Обрыв стороны — штатное завершение транзита.
            }
            catch (ObjectDisposedException)
            {
                // Стенд утилизирован.
            }
        }

        /// <summary>Исключения диалога (диагностика стенда).</summary>
        public List<string> ОшибкиДиалога { get; } = [];

        /// <summary>Ведёт SMTP-диалог с клиентом (под сетевым потоком).</summary>
        /// <summary>
        /// Ведёт SMTP-диалог над заданным потоком. Используется и для
        /// открытого текста, и после TLS-рукопожатия — ОДНА реализация
        /// конечного автомата (повторное использование исключает рассогласование
        /// копий: после STARTTLS диалог перезапускается поверх TLS-потока).
        /// </summary>
        /// <param name="поток">Сетевой поток диалога.</param>
        /// <param name="tlsСогласован">Защита уже согласована (ImplicitTls/после STARTTLS).</param>
        private async Task ДиалогAsync(Stream поток, bool tlsСогласован, bool приветствовать = true)
        {
            using var читатель = new StreamReader(поток, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var писатель = new StreamWriter(поток, new ASCIIEncoding(), 4096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n",
            };

            if (приветствовать)
            {
                await писатель.WriteLineAsync("220 fake-tls ESMTP ready").ConfigureAwait(false);
            }

            var состояние = "старт";

            while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } команда)
            {
                Команды.Add($"[{состояние}] {команда}");

                switch (состояние)
                {
                    case "старт" when команда.StartsWith("EHLO", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250-fake-tls greets you").ConfigureAwait(false);

                        if (_режим == Режим.StartTls && _рекламироватьStartTls && !tlsСогласован)
                        {
                            await писатель.WriteLineAsync("250 STARTTLS").ConfigureAwait(false);
                        }
                        else
                        {
                            await писатель.WriteLineAsync("250 AUTH LOGIN").ConfigureAwait(false);
                        }

                        continue;

                    case "старт" when команда.StartsWith("AUTH PLAIN", StringComparison.Ordinal):
                        // Сервер поддерживает только LOGIN: отказ PLAIN даёт
                        // клиенту штатный фолбэк (F-B1).
                        await писатель.WriteLineAsync("504 Mechanism not supported").ConfigureAwait(false);
                        continue;

                    case "старт"
                        when команда.Equals("STARTTLS", StringComparison.Ordinal) && !tlsСогласован:
                        await писатель.WriteLineAsync("220 Ready to start TLS").ConfigureAwait(false);

                        try
                        {
                            поток = await ЗавернутьTlsAsync(поток).ConfigureAwait(false);
                        }
                        catch (Exception ошибка)
                        {
                            ОшибкиДиалога.Add(ошибка.ToString());

                            return;
                        }

                        // Диалог продолжается поверх TLS тем же автоматом.
                        await ДиалогAsync(поток, tlsСогласован: true, приветствовать: false).ConfigureAwait(false);

                        return;

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

        /// <summary>Заворачивает поток в SslStream с сертификатом стенда.</summary>
        private async Task<Stream> ЗавернутьTlsAsync(Stream поток)
        {
            var ssl = new SslStream(поток);

            await ssl.AuthenticateAsServerAsync(
                _сертификат ?? throw new InvalidOperationException("Для TLS-режима нужен сертификат."));

            return ssl;
        }

        /// <summary>Читает тело письма до строки «.» и дописывает в лог.</summary>
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
                _сниффер.Stop();
                _сервер.Stop();
            }
            catch (SocketException)
            {
                // Повторная остановка безопасна.
            }
        }
    }
}
