using Picket302.Application.Services;
using Picket302.Domain.Email;
using Picket302.Domain.Entities;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка электронной рассылки (ТЗ §8.7, функция 92:
/// <c>public SendReport SendBatch(EmailDistribution distribution)</c>).
/// </summary>
public sealed class EmailDistributionServiceTests
{
    [Fact]
    public void SendBatch_ТридцатьПолучателейДваБезАдреса_ОтправляетДвадцатьВосемьПисем()
    {
        var получатели = СобратьПолучателей(30, безАдреса: [7, 19]);
        var рассылка = СобратьРассылку(получатели);

        var отправленные = new List<string>();
        var журнал = new ЖурналЗаглушка();

        var служба = СобратьСлужбу(
            (письмо, _) =>
            {
                отправленные.AddRange(письмо.To);

                return Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK"));
            },
            журнал);

        var отчёт = служба.SendBatch(рассылка);

        Assert.Equal(30, отчёт.TotalCount);
        Assert.Equal(28, отчёт.SentCount);
        Assert.Equal(0, отчёт.FailedCount);
        Assert.Equal(2, отчёт.SkippedCount);
        Assert.Equal(2, отчёт.SkippedRecipients.Count);
        Assert.Equal(28, отправленные.Count);
        Assert.Null(отчёт.Error);
        Assert.False(отчёт.IsCanceled);
    }

    [Fact]
    public void SendBatch_ПолучательБезАдреса_ИсключаетсяИзОтправкиИПеречисляетсяВОтчете()
    {
        var получатели = СобратьПолучателей(30, безАдреса: [7, 19]);
        var рассылка = СобратьРассылку(получатели);

        var отправленные = new List<string>();

        var служба = СобратьСлужбу(
            (письмо, _) =>
            {
                отправленные.AddRange(письмо.To);

                return Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK"));
            },
            new ЖурналЗаглушка());

        var отчёт = служба.SendBatch(рассылка);

        Assert.DoesNotContain("client7@example.ru", отправленные);
        Assert.DoesNotContain("client19@example.ru", отправленные);
        Assert.Contains("Контрагент 7", отчёт.SkippedRecipients);
        Assert.Contains("Контрагент 19", отчёт.SkippedRecipients);
        Assert.Contains("Пропущены", отчёт.BuildText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SendBatch_ОтправкаВыполняетсяПоследовательно_РезультатПишетсяВЖурнал()
    {
        var получатели = СобратьПолучателей(3, безАдреса: []);
        var рассылка = СобратьРассылку(получатели);

        var журнал = new ЖурналЗаглушка();

        var служба = СобратьСлужбу(
            (письмо, _) => Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK")),
            журнал);

        служба.SendBatch(рассылка);

        Assert.Equal(3, журнал.Записи.Count);
        Assert.All(журнал.Записи, запись => Assert.Equal(EmailLogEntry.StatusSent, запись.Status));
        Assert.All(журнал.Записи, запись => Assert.Equal(250, запись.SmtpCode));
        Assert.Equal(
            new[] { "client1@example.ru", "client2@example.ru", "client3@example.ru" },
            журнал.Записи.Select(запись => запись.Recipient));
    }

    [Fact]
    public void SendBatch_МаркерыПолей_ПодставляетЗначенияВТемуИТело()
    {
        var получатели = СобратьПолучателей(2, безАдреса: []);
        var рассылка = СобратьРассылку(получатели);
        рассылка.Subject = "Письмо для #Наименование#";
        рассылка.Body = "Организация: #Организация#";

        var отправленныеПисьма = new List<MailMessage>();

        var служба = СобратьСлужбу(
            (письмо, _) =>
            {
                отправленныеПисьма.Add(письмо);

                return Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK"));
            },
            new ЖурналЗаглушка(),
            new EmailMarkerResolver(new Dictionary<string, object>
            {
                ["Организация"] = "ООО «Техно»",
            }));

        служба.SendBatch(рассылка);

        Assert.Equal(2, отправленныеПисьма.Count);
        Assert.Equal("Письмо для Контрагент 1", отправленныеПисьма[0].Subject);
        Assert.Equal("Письмо для Контрагент 2", отправленныеПисьма[1].Subject);
        Assert.Equal("Организация: ООО «Техно»", отправленныеПисьма[0].Body);
    }

    [Fact]
    public void SendBatch_НезаполненныйМаркер_ПопадаетВПредупрежденияБезИсключения()
    {
        var получатели = СобратьПолучателей(1, безАдреса: []);
        var рассылка = СобратьРассылку(получатели);
        рассылка.Subject = "Тема #НеизвестноеПоле#";

        var служба = СобратьСлужбу(
            (письмо, _) => Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK")),
            new ЖурналЗаглушка());

        var исход = служба.SendBatchDetailed(рассылка);

        Assert.Equal(1, исход.Report.SentCount);
        Assert.Contains(исход.Warnings, предупреждение => предупреждение.Contains("#НеизвестноеПоле#", StringComparison.Ordinal));
    }

    [Fact]
    public void SendBatch_ОтказСервераПоОдномуПолучателю_ПродолжаетОтправкуОстальных()
    {
        var получатели = СобратьПолучателей(3, безАдреса: []);
        var рассылка = СобратьРассылку(получатели);

        var служба = СобратьСлужбу(
            (письмо, _) => письмо.To[0].Contains("client2", StringComparison.Ordinal)
                ? Task.FromResult(SendResult.Failure(письмо.To[0], 550, "550 No such user", "Сервер отклонил получателя"))
                : Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK")),
            new ЖурналЗаглушка());

        var отчёт = служба.SendBatch(рассылка);

        Assert.Equal(3, отчёт.TotalCount);
        Assert.Equal(2, отчёт.SentCount);
        Assert.Equal(1, отчёт.FailedCount);
        Assert.Equal(3, отчёт.Results.Count);
    }

    [Fact]
    public void SendBatch_ОтменаОперации_ПрерываетПартию()
    {
        var получатели = СобратьПолучателей(5, безАдреса: []);
        var рассылка = СобратьРассылку(получатели);

        var отправлено = 0;

        var служба = СобратьСлужбу(
            (письмо, _) =>
            {
                отправлено++;

                return Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK"));
            },
            new ЖурналЗаглушка());

        using var отмена = new CancellationTokenSource();
        отмена.Cancel();

        var отчёт = служба.SendBatch(рассылка, null, отмена.Token);

        Assert.True(отчёт.IsCanceled);
        Assert.Equal(5, отчёт.TotalCount);
        Assert.Equal(0, отчёт.SentCount);
        Assert.Equal(0, отправлено);
    }

    [Fact]
    public void SendBatch_СерверНеУказан_ВозвращаетОшибкуБезОтправки()
    {
        var получатели = СобратьПолучателей(3, безАдреса: []);
        var рассылка = СобратьРассылку(получатели);

        var отправлено = 0;

        var служба = new EmailDistributionService(
            (письмо, _) =>
            {
                отправлено++;

                return Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK"));
            },
            new ЖурналЗаглушка(),
            new EmailMarkerResolver(),
            new SmtpSettings(),
            паузаМеждуПисьмамиМс: 0);

        var отчёт = служба.SendBatch(рассылка);

        Assert.Equal(SmtpSettings.ServerNotSpecifiedMessage, отчёт.Error);
        Assert.Equal(0, отчёт.SentCount);
        Assert.Equal(0, отправлено);
    }

    [Fact]
    public void SendBatch_ЭлектроннаяРассылка_НеСоздаетЗаписейБумажныхОтправлений()
    {
        var получатели = СобратьПолучателей(2, безАдреса: []);
        var рассылка = СобратьРассылку(получатели);

        var служба = СобратьСлужбу(
            (письмо, _) => Task.FromResult(SendResult.Success(письмо.To[0], 250, "250 OK")),
            new ЖурналЗаглушка());

        служба.SendBatch(рассылка);

        // Рассылка ведётся отдельным каналом: список вложений и получателей
        // меняется только самой рассылкой, таблица mails не затрагивается.
        Assert.Equal(2, рассылка.Recipients.Count);
        Assert.Empty(рассылка.Attachments);
    }

    /// <summary>
    /// Формирует список получателей нужного размера.
    /// </summary>
    /// <param name="количество">Число получателей.</param>
    /// <param name="безАдреса">Номера получателей без адреса.</param>
    /// <returns>Список получателей.</returns>
    private static List<EmailRecipient> СобратьПолучателей(int количество, int[] безАдреса)
    {
        var получатели = new List<EmailRecipient>();

        for (var номер = 1; номер <= количество; номер++)
        {
            var адресОтсутствует = Array.IndexOf(безАдреса, номер) >= 0;

            получатели.Add(new EmailRecipient(
                номер,
                $"Контрагент {номер}",
                адресОтсутствует ? string.Empty : $"client{номер}@example.ru"));
        }

        return получатели;
    }

    /// <summary>
    /// Формирует рассылку по списку получателей.
    /// </summary>
    /// <param name="получатели">Получатели рассылки.</param>
    /// <returns>Готовая рассылка.</returns>
    private static EmailDistribution СобратьРассылку(IEnumerable<EmailRecipient> получатели)
    {
        var рассылка = new EmailDistribution
        {
            Name = "Проверка рассылки",
            Subject = "Тема письма",
            Body = "Текст письма",
            From = "sender@example.ru",
        };

        рассылка.Recipients.AddRange(получатели);

        return рассылка;
    }

    /// <summary>
    /// Формирует службу рассылки с настроенным сервером и нулевой паузой.
    /// </summary>
    /// <param name="отправить">Подстановка отправки.</param>
    /// <param name="журнал">Журнал отправки.</param>
    /// <param name="маркеры">Подстановщик маркеров.</param>
    /// <returns>Служба электронной рассылки.</returns>
    private static EmailDistributionService СобратьСлужбу(
        EmailSenderDelegate отправить,
        IEmailLogWriter журнал,
        EmailMarkerResolver? маркеры = null)
        => new(
            отправить,
            журнал,
            маркеры ?? new EmailMarkerResolver(),
            new SmtpSettings { Server = "smtp.example.ru", Port = 25 },
            паузаМеждуПисьмамиМс: 0);

    /// <summary>
    /// Журнал отправки-заглушка: накапливает записи в памяти.
    /// </summary>
    private sealed class ЖурналЗаглушка : IEmailLogWriter
    {
        /// <summary>Записанные записи журнала.</summary>
        public List<EmailLogEntry> Записи { get; } = [];

        /// <inheritdoc />
        public Task WriteAsync(EmailLogEntry entry, CancellationToken cancellationToken = default)
        {
            Записи.Add(entry);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EmailLogEntry>> ReadAsync(
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmailLogEntry>>(Записи.TakeLast(limit).ToList());
    }
}
