using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Неизменяемый снимок печатной сессии (ТЗ §8.4, функция 64:
/// <c>PrintHistoryRecord</c>). Все свойства — только для чтения: запись
/// журнала после создания не изменяется.
/// </summary>
/// <param name="PrintedAt">Момент печати (UTC).</param>
/// <param name="MailId">Отправление; <see langword="null"/> — пробная печать.</param>
/// <param name="RecipientAddressSnapshot">Снимок адреса получателя на момент печати.</param>
/// <param name="SenderAddressSnapshot">Снимок адреса отправителя на момент печати.</param>
/// <param name="CopyCount">Число напечатанных экземпляров.</param>
/// <param name="TemplateDescription">Наименование бланка.</param>
/// <param name="PrintJobId">Сессия печати.</param>
/// <param name="IsVip">Признак приоритетного адресата на момент печати.</param>
/// <param name="Note">Технологическое примечание.</param>
public sealed record PrintHistoryRecord(
    DateTime PrintedAt,
    long? MailId,
    string RecipientAddressSnapshot,
    string SenderAddressSnapshot,
    int CopyCount,
    string TemplateDescription,
    long? PrintJobId = null,
    bool IsVip = false,
    string? Note = null);

/// <summary>
/// Неизменяемая история печати конвертов (ТЗ §8.4, функция 64:
/// <c>Application.Services.HistoryTracker.RecordPrintEvent</c>).
/// </summary>
/// <remarks>
/// <para>При каждом физическом выводе конверта на принтер фиксирует
/// неизменяемый снимок адресных строк получателя и отправителя, имя бланка
/// и дату (таблица print_history). Последующее изменение адреса контрагента
/// в карточке не изменяет историческую запись — правило снимков (ТЗ §3.1.19).</para>
/// <para>Сбой записи отменяет транзакцию печати: исключение пробрасывается
/// вызывающему конвейеру печати (ТЗ §6.1, §8.4).</para>
/// </remarks>
public sealed class HistoryTracker
{
    private readonly IPrintHistoryRepository _история;

    /// <summary>Создаёт трекер истории печати.</summary>
    /// <param name="historyRepository">Репозиторий журнала печати.</param>
    public HistoryTracker(IPrintHistoryRepository historyRepository)
    {
        ArgumentNullException.ThrowIfNull(historyRepository);

        _история = historyRepository;
    }

    /// <summary>
    /// Фиксирует неизменяемый снимок печатного события (ТЗ §8.4, функция 64).
    /// </summary>
    /// <param name="record">Снимок.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор записи журнала.</returns>
    /// <remarks>Сбой записи пробрасывается вызывающему процессу: транзакция
    /// печати отменяется.</remarks>
    public async Task<long> RecordPrintEvent(
        PrintHistoryRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var запись = new PrintHistory
        {
            PrintedAt = DateTime.SpecifyKind(record.PrintedAt, DateTimeKind.Utc),
            MailId = record.MailId,
            RecipientAddressSnapshot = record.RecipientAddressSnapshot,
            SenderAddressSnapshot = record.SenderAddressSnapshot,
            CopyCount = record.CopyCount,
            IsVip = record.IsVip,
            Note = record.Note,
            TemplateDescription = record.TemplateDescription,
            PrintJobId = record.PrintJobId,
        };

        // Репозиторий print_history не предоставляет операций изменения:
        // снимок записывается однократно.
        return await _история.AddAsync(запись, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Возвращает снимки печати отправления; записи никогда не изменяются.
    /// </summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список снимков.</returns>
    public Task<IReadOnlyList<PrintHistory>> GetMailHistoryAsync(
        long mailId,
        CancellationToken cancellationToken = default) =>
        _история.GetByMailAsync(mailId, cancellationToken);
}
