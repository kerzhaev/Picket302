using System.Net;
using System.Net.Sockets;
using System.Text;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Mail;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// S40 (ТЗ §8.7, функция 100): встроенный SMTP-клиент — отчёт партии
/// (Total/Sent/Failed), журнал отправки, отмена и частичные отказы на
/// локальном стенде. Без внешних адресатов и реальной сети.
/// </summary>
public sealed class SmtpClientContractTests
{
    /// <summary>Письмо с двумя получателями.</summary>
    private static MailMessage ПисьмоДвум() => new(["ok1@example.test", "ok2@example.test"])
    {
        From = "sender@example.test",
        Subject = "Партия",
        Body = "тело",
    };

    [Fact]
    public async Task ОтчётПартии_ТочныеСчётчикиИЖурнал()
    {
        using var сервер = new MimeStand40();

        var опции = new SmtpOptions();
        var журнал = new List<(SendResult, DateTime)>();

        var клиент = new SmtpClientFacade(new SmtpSettings
        {
            Server = "127.0.0.1",
            Port = сервер.Запустить(),
            Login = "user@example.test",
            ProtectedPassword = опции.ProtectPassword("пароль"),
            SslMode = SslMode.None,
            TimeoutSeconds = 15,
        })
        {
            ЖурналОтправки = (результат, момент) => журнал.Add((результат, момент)),
        };

        var отчёт = клиент.Send(ПисьмоДвум());

        Assert.Equal(2, отчёт.TotalCount);
        Assert.Equal(2, отчёт.SentCount);
        Assert.Equal(0, отчёт.FailedCount);
        Assert.Null(отчёт.Error);

        // Журнал отправки (функция 100) — запись на каждого получателя.
        Assert.Equal(2, журнал.Count);
        Assert.All(журнал, запись => Assert.True(запись.Item1.IsSuccess));
        Assert.All(журнал, запись => Assert.True(запись.Item2 <= DateTime.UtcNow));
    }

    [Fact]
    public async Task ОтменаПартии_Токеном_ОтчётСПризнаком()
    {
        using var сервер = new MimeStand40();

        var опции = new SmtpOptions();

        var клиент = new SmtpClientFacade(new SmtpSettings
        {
            Server = "127.0.0.1",
            Port = сервер.Запустить(),
            SslMode = SslMode.None,
            TimeoutSeconds = 15,
        });

        using var отмена = new CancellationTokenSource();

        var письмо = new MailMessage(["ok1@example.test", "ok2@example.test", "ok3@example.test", "ok4@example.test"])
        {
            From = "sender@example.test",
            Subject = "Отмена партии",
            Body = "тело",
        };

        // Отмена сразу: партия не должна уйти в спулер.
        отмена.Cancel();

        // Отмена на подключении пробрасывается как OperationCanceledException
        // (TaskCanceledException — подкласс): партия не отправляется.
        Assert.ThrowsAny<OperationCanceledException>(
            () => клиент.Send(письмо, отмена.Token));
    }

    /// <summary>
    /// Частичный отказ: один адрес принимает сервер, второй отклоняется на
    /// RCPT TO — счётчики Sent/Failed точны, ошибки персонализированы.
    /// </summary>
    [Fact]
    public async Task ЧастичныйОтказ_СчётчикиТочны_ОшибкаPersонализирована()
    {
        using var сервер = new MimeStand40(отказные: ["bad@example.test"]);

        var опции = new SmtpOptions();

        var клиент = new SmtpClientFacade(new SmtpSettings
        {
            Server = "127.0.0.1",
            Port = сервер.Запустить(),
            SslMode = SslMode.None,
            TimeoutSeconds = 15,
        });

        var письмо = new MailMessage(["good@example.test", "bad@example.test"])
        {
            From = "sender@example.test",
            Subject = "Частичный отказ",
            Body = "тело",
        };

        var отчёт = клиент.Send(письмо);

        Assert.Equal(2, отчёт.TotalCount);
        Assert.Equal(1, отчёт.SentCount);
        Assert.Equal(1, отчёт.FailedCount);

        var отказ = Assert.Single(отчёт.Results, результат => результат.IsFailure);
        Assert.Equal("bad@example.test", отказ.Recipient);
        Assert.NotNull(отказ.Error);
    }

    /// <summary>Локальный SMTP-приёмник с отказами на RCPT (тестовый стенд).</summary>
    private sealed class MimeStand40 : IDisposable
    {
        private readonly TcpListener _слушатель;
        private readonly IReadOnlyList<string> _отказные;

        public MimeStand40(IReadOnlyList<string>? отказные = null)
        {
            _слушатель = new TcpListener(IPAddress.Loopback, 0);
            _отказные = отказные ?? Array.Empty<string>();
        }

        /// <summary>Запускает приёмник, возвращает порт.</summary>
        public int Запустить()
        {
            _слушатель.Start();

            _ = Task.Run(async () =>
            {
                using var клиент = await _слушатель.AcceptTcpClientAsync();
                await ДиалогAsync(клиент.GetStream());
            });

            return ((IPEndPoint)_слушатель.LocalEndpoint).Port;
        }

        private async Task ДиалогAsync(NetworkStream поток)
        {
            using var читатель = new StreamReader(поток, Encoding.UTF8, false, 4096, leaveOpen: true);
            await using var писатель = new StreamWriter(поток, new ASCIIEncoding(), 4096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n",
            };

            await писатель.WriteLineAsync("220 stand40 ESMTP ready").ConfigureAwait(false);

            var состояние = "старт";

            while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } команда)
            {
                switch (состояние)
                {
                    case "старт" when команда.StartsWith("EHLO", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case "старт" when команда.StartsWith("MAIL FROM", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;

                    case var _ when состояние is "старт" or "авторизован"
                        && команда.StartsWith("RCPT TO", StringComparison.Ordinal):
                        var адрес = команда.Contains("bad@", StringComparison.Ordinal);

                        await писатель.WriteLineAsync(адрес ? "550 No such user" : "250 OK").ConfigureAwait(false);
                        continue;

                    case var _ when состояние is "старт" or "авторизован"
                        && команда.Equals("DATA", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("354 End data").ConfigureAwait(false);

                        while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } строка && строка != ".")
                        {
                        }

                        await писатель.WriteLineAsync("250 OK queued").ConfigureAwait(false);
                        continue;

                    case var _ when команда.Equals("QUIT", StringComparison.Ordinal):
                        await писатель.WriteLineAsync("221 Bye").ConfigureAwait(false);
                        return;

                    default:
                        await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                        continue;
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
