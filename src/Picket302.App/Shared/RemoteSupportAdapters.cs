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
public sealed class RemotePrintHistoryWriter : IPrintHistoryWriter
{
    private readonly SharedTransport _транспорт;

    /// <summary>
    /// Создаёт запись печатных событий поверх серверной службы.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    public RemotePrintHistoryWriter(SharedTransport транспорт)
    {
        ArgumentNullException.ThrowIfNull(транспорт);

        _транспорт = транспорт;
    }

    /// <inheritdoc />
    public async Task<Result<PrintHistory>> RegisterPrintAsync(
        long mailId,
        long? printJobId,
        string templateDescription,
        CancellationToken cancellationToken = default,
        string? outcome = null,
        string? deviceId = null)
    {
        var ответ = await _транспорт
            .ВыполнитьКомандуAsync<RecordPrintEventRequest, PrintEventResponse>(
                "print.record-event",
                new RecordPrintEventRequest(
                    new[] { mailId },
                    templateDescription,
                    outcome is null ? PrintEventOutcome.Spooled : Enum.Parse<PrintEventOutcome>(outcome),
                    null,
                    null,
                    1,
                    "selected",
                    null),
                cancellationToken: cancellationToken)
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
            Outcome = outcome,
            DeviceId = deviceId,
        });
    }
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
