using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Shared.Commands;
using Picket302.Shared.Protocol;

namespace Picket302.App.Shared;

/// <summary>
/// Запись печатных событий в общий журнал через серверную службу.
/// </summary>
/// <remarks>
/// Событие пишется как операция с защитой от дубля: потерянный ответ не
/// приводит ко второй записи журнала печати.
/// </remarks>
public sealed class RemotePrintHistoryWriter : IPrintHistoryWriter
{
    private readonly SharedTransport _транспорт;
    private readonly PendingOperationsStore _незавершённые;

    /// <summary>
    /// Создаёт запись печатных событий поверх серверной службы.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    /// <param name="незавершённые">Хранилище незавершённых операций.</param>
    public RemotePrintHistoryWriter(SharedTransport транспорт, PendingOperationsStore незавершённые)
    {
        ArgumentNullException.ThrowIfNull(транспорт);
        ArgumentNullException.ThrowIfNull(незавершённые);

        _транспорт = транспорт;
        _незавершённые = незавершённые;
    }

    /// <inheritdoc />
    public async Task<Result<PrintHistory>> RegisterPrintAsync(
        long mailId,
        long? printJobId,
        string templateDescription,
        CancellationToken cancellationToken = default,
        PrintOutcome? outcome = null,
        string? deviceId = null,
        string? operationId = null)
    {
        var исход = outcome ?? PrintOutcome.Spooled;

        // Идентификатор операции клиент не передаёт: служба знает его по
        // заголовку запроса и записывает в строку журнала сама. Параметр
        // оставлен для реализаций, которые ведут журнал на своей стороне.
        _ = operationId;

        var тело = System.Text.Json.JsonSerializer.Serialize(
            new RecordPrintEventRequest(
                new[] { mailId },
                templateDescription,
                КонтрактныйИсход(исход),
                null,
                null,
                1,
                "selected",
                null),
            SharedJson.Options);

        var ответ = await _незавершённые
            .ВыполнитьAsync<PrintEventResponse>(_транспорт, "print.record-event", тело, cancellationToken)
            .ConfigureAwait(false);

        return Result<PrintHistory>.Success(new PrintHistory
        {
            Id = 0,
            PrintedAt = DateTime.UtcNow,
            MailId = mailId,
            RecipientAddressSnapshot = string.Empty,
            SenderAddressSnapshot = string.Empty,
            CopyCount = 1,
            IsVip = false,
            Note = null,
            TemplateDescription = templateDescription,
            PrintJobId = ответ.PrintJobId,
            Outcome = исход.ToString(),
            DeviceId = deviceId,
        });
    }

    /// <summary>
    /// Переводит доменный исход печати в контрактный исход совместного
    /// режима.
    /// </summary>
    /// <param name="исход">Доменный исход.</param>
    /// <returns>Контрактный исход.</returns>
    /// <remarks>
    /// Перечисления различают четыре состояния, и смешивать их нельзя:
    /// подтверждение спулера не равно физически напечатанному конверту.
    /// Соответствие явное, а не приведением типа: значения перечислений
    /// совместного режима — часть контракта и могут быть расширены.
    /// </remarks>
    private static PrintEventOutcome КонтрактныйИсход(PrintOutcome исход) => исход switch
    {
        PrintOutcome.Prepared => PrintEventOutcome.Prepared,
        PrintOutcome.Spooled => PrintEventOutcome.Spooled,
        PrintOutcome.Error => PrintEventOutcome.Error,
        PrintOutcome.Unknown => PrintEventOutcome.Unknown,
        _ => PrintEventOutcome.Unknown,
    };
}

/// <summary>
/// Кэш QR-кодов в памяти рабочего места.
/// </summary>
/// <remarks>
/// <para>
/// QR-код — производная величина: он вычисляется из содержимого бланка и
/// зоны отправления, а не хранится как общие данные. В совместном режиме
/// кэш держится в памяти компьютера, который печатает: локальной копии общей
/// базы при этом не появляется.
/// </para>
/// </remarks>
public sealed class InMemoryQrCacheRepository : IQrCacheRepository
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, QrCache> _кэш = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<long> AddAsync(QrCache cache, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);

        cache.Id = _кэш.Count == 0 ? 1 : _кэш.Values.Max(з => з.Id) + 1;

        _кэш[cache.PayloadHash] = cache;

        return Task.FromResult(cache.Id);
    }

    /// <inheritdoc />
    public Task<QrCache?> GetByPayloadHashAsync(string payloadHash, CancellationToken cancellationToken = default)
        => Task.FromResult(_кэш.TryGetValue(payloadHash, out var найденный) ? найденный : null);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var удаляемые = _кэш.Where(пара => пара.Value.Id == id).Select(пара => пара.Key).ToArray();

        foreach (var ключ in удаляемые)
        {
            _кэш.TryRemove(ключ, out _);
        }

        return Task.FromResult(удаляемые.Length > 0);
    }
}
