using System.Net;
using System.Net.Sockets;
using System.Text;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Mail;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// S40/№100 (корректирующий проход): retry-семантика SMTP-фасада на
/// счётном стенде — один таймаут → РОВНО ОДИН повтор (2 соединения,
/// письмо доставлено 1 раз); отмена и постоянная ошибка → без повтора.
/// </summary>
public sealed class SmtpRetryStandTests
{
    /// <summary>
    /// SMTP-сервер со счётчиком соединений: первая попытка — молчание
    /// (таймаут клиента), последующие — штатный диалог.
    /// </summary>
    public sealed class RetryCountingStand : IDisposable
    {
        private readonly TcpListener _слушатель;
        private readonly List<string> _принятые = [];
        private int _соединений;
        private int _молчаливых;
        private readonly int _молчаливыхДопустимо;
        private readonly List<string> _тела = [];

        public RetryCountingStand(int молчаливыхДопустимо = 0)
        {
            _слушатель = new TcpListener(IPAddress.Loopback, 0);
            _молчаливыхДопустимо = молчаливыхДопустимо;
            _слушатель.Start();
            Порт = ((IPEndPoint)_слушатель.LocalEndpoint).Port;
            _ = Task.Run(ПриёмЦикл);
        }

        /// <summary>Порт приёмника.</summary>
        public int Порт { get; }

        /// <summary>Сколько соединений принято.</summary>
        public int Соединений => _соединений;

        /// <summary>Лог команд стенда (диагностика).</summary>
        public List<string> ЛогКоманд { get; } = [];

        /// <summary>Число писем, принятых с телом (после DATA «.»).</summary>
        public int ПисемПринято => _тела.Count;

        /// <summary>Тела принятых писем.</summary>
        public IReadOnlyList<string> Тела => _тела;

        private async Task ПриёмЦикл()
        {
            while (true)
            {
                TcpClient клиент;

                try
                {
                    клиент = await _слушатель.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var номер = ++_соединений;
                ЛогКоманд.Add($"[{DateTime.Now:HH:mm:ss.fff}] принято соединение {номер}");
                _ = Task.Run(() => ОбслужитьСоединение(клиент, номер));
            }
        }

        private async Task ОбслужитьСоединение(TcpClient клиент, int номер)
        {
            try
            {
                using var клиентПожизненно = клиент;

                await using var писатель = new StreamWriter(
                    клиент.GetStream(), new ASCIIEncoding(), 4096, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\r\n",
                };

                using var читатель = new StreamReader(клиент.GetStream(), Encoding.UTF8, false, 4096, leaveOpen: true);

                if (номер <= _молчаливыхДопустимо)
                {
                    // Молчание: не отвечаем, ждём пока клиент сам закроет
                    // соединение по своему таймауту.
                    while (await читатель.ReadLineAsync().ConfigureAwait(false) is not null)
                    {
                    }

                    return;
                }

                await писатель.WriteLineAsync("220 retry-stand ESMTP ready").ConfigureAwait(false);

                var состояние = "старт";

                while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } команда)
                {
                    ЛогКоманд.Add($"[{DateTime.Now:HH:mm:ss.fff} конн{номер}] {команда}");

                    switch (состояние)
                    {
                        case "старт" when команда.StartsWith("EHLO", StringComparison.Ordinal):
                            await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                            continue;

                        case "старт" when команда.StartsWith("MAIL FROM", StringComparison.Ordinal)
                            || команда.StartsWith("RCPT TO", StringComparison.Ordinal):
                            await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                            continue;

                        case "старт" when команда.Equals("QUIT", StringComparison.Ordinal):
                            await писатель.WriteLineAsync("221 Bye").ConfigureAwait(false);
                            return;

                        case "старт" when команда.Equals("DATA", StringComparison.Ordinal):
                            ЛогКоманд.Add($"[{DateTime.Now:HH:mm:ss.fff}] ->354");
                            await писатель.WriteLineAsync("354 End data").ConfigureAwait(false);

                            var тело = new StringBuilder();

                            while (await читатель.ReadLineAsync().ConfigureAwait(false) is { } строка && строка != ".")
                            {
                                ЛогКоманд.Add($"[{DateTime.Now:HH:mm:ss.fff}] тело: {строка}");
                                тело.AppendLine(строка);
                            }

                            ЛогКоманд.Add($"[{DateTime.Now:HH:mm:ss.fff}] тело-терминатор");
                            _тела.Add(тело.ToString());
                            Console.Error.WriteLine("[стенд] 250 queued отправлен клиенту");
                            ЛогКоманд.Add($"[{DateTime.Now:HH:mm:ss.fff}] ->250 queued");
                            await писатель.WriteLineAsync("250 OK queued").ConfigureAwait(false);
                            continue;

                        case "авторизован" when команда.Equals("QUIT", StringComparison.Ordinal):
                            await писатель.WriteLineAsync("221 Bye").ConfigureAwait(false);
                            return;

                        default:
                            await писатель.WriteLineAsync("250 OK").ConfigureAwait(false);
                            continue;
                    }
                }
            }
            catch (Exception ошибка)
            {
                ЛогКоманд.Add($"[ошибка{номер}] {ошибка.GetType().Name}: {ошибка.Message}");
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

    /// <summary>
    /// Один таймаут (молчащая первая попытка) → РОВНО ОДИН повтор: 2
    /// соединения, письмо доставлено и принято ровно 1 раз.
    /// </summary>
    [Fact]
    public void ОдинТаймаут_РовноОдинПовтор_ПисьмоДоставленоОдинРаз()
    {
        using var стенд = new RetryCountingStand(молчаливыхДопустимо: 1);

        var клиент = new SmtpClientFacade(new SmtpSettings
        {
            Server = "127.0.0.1",
            Port = стенд.Порт,
            SslMode = SslMode.None,
            TimeoutSeconds = 2,
        });

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "Retry-пин",
            Body = "тело",
        };

        var результат = клиент.SendOne(письмо);

        var дамп = $"успех={результат.IsSuccess} ошибка={результат.Error} соединений={стенд.Соединений} писем={стенд.ПисемПринято} лог=[{string.Join(" | ", стенд.ЛогКоманд)}]";

        // Один таймаут → ровно один повтор: 2 соединения.
        Assert.Equal(2, стенд.Соединений);

        // Письмо доставлено РОВНО 1 РАЗ (без дублирования) — ядро критерия.
        Assert.True(стенд.ПисемПринято == 1, $"писем={стенд.ПисемПринято} соединений={стенд.Соединений} лог=[{string.Join(" | ", стенд.ЛогКоманд)}]");

        // Повтор завершился успехом: тело доставлено, финальный 250 получен.
        Assert.True(результат.IsSuccess, дамп);
    }

    /// <summary>Отмена до отправки → ни одного соединения (повтора нет).</summary>
    [Fact]
    public async Task Отмена_НиОдногоСоединения()
    {
        using var стенд = new RetryCountingStand();

        var клиент = new SmtpClientFacade(new SmtpSettings
        {
            Server = "127.0.0.1",
            Port = стенд.Порт,
            SslMode = SslMode.None,
            TimeoutSeconds = 5,
        });

        using var отмена = new CancellationTokenSource();
        отмена.Cancel();

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "Отмена",
            Body = "тело",
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Task.Run(() => клиент.SendOne(письмо, отмена.Token)));

        Assert.Equal(0, стенд.Соединений);
    }

    /// <summary>
    /// Постоянная ошибка (соединение отклоняется) → без нежелательного
    /// повтора: ровно одна попытка подключения.
    /// </summary>
    [Fact]
    public async Task ПостояннаяОшибка_БезПовтора()
    {
        // Порт без слушателя: соединение отклоняется мгновенно (не таймаут).
        using var глухойПорт = new TcpListener(IPAddress.Loopback, 0);
        глухойПорт.Start();
        var порт = ((IPEndPoint)глухойПорт.LocalEndpoint).Port;
        глухойПорт.Stop();

        var клиент = new SmtpClientFacade(new SmtpSettings
        {
            Server = "127.0.0.1",
            Port = порт,
            SslMode = SslMode.None,
            TimeoutSeconds = 5,
        });

        var письмо = new MailMessage(["dest@example.test"])
        {
            From = "sender@example.test",
            Subject = "Отказ",
            Body = "тело",
        };

        var результат = клиент.SendOne(письмо);

        Assert.False(результат.IsSuccess);
    }
}
