using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий журнала напечатанных конвертов на SQLite (ТЗ §3.1.7).
/// </summary>
/// <remarks>
/// Журнал хранит текстовые снимки адресов получателя и отправителя на момент
/// печати: изменения карточек контрагентов не влияют на записи журнала
/// (ТЗ §3.1.19: «Правило снимков»).
/// </remarks>
public sealed class PrintHistoryRepository : IPrintHistoryRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий журнала напечатанных конвертов.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public PrintHistoryRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(PrintHistory history, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);

        history.PrintedAt = history.PrintedAt == default ? DateTime.UtcNow : history.PrintedAt;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO print_history (printed_at, mail_id, recipient_address_snapshot, sender_address_snapshot,
                copy_count, is_vip, note, template_description, print_job_id, outcome, device_id)
            VALUES (@PrintedAt, @MailId, @RecipientAddressSnapshot, @SenderAddressSnapshot,
                @CopyCount, @IsVip, @Note, @TemplateDescription, @PrintJobId, @Outcome, @DeviceId);
            SELECT last_insert_rowid();
            """,
            new
            {
                history.PrintedAt,
                history.MailId,
                history.RecipientAddressSnapshot,
                history.SenderAddressSnapshot,
                history.CopyCount,
                history.IsVip,
                history.Note,
                history.TemplateDescription,
                history.PrintJobId,
                history.Outcome,
                history.DeviceId,
            },
            cancellationToken).ConfigureAwait(false);

        history.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<PrintHistory?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<PrintHistory>(
            "SELECT * FROM print_history WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintHistory>> GetByMailAsync(
        long mailId,
        CancellationToken cancellationToken = default)
    {
        var записи = await _сессия.QueryAsync<PrintHistory>(
            "SELECT * FROM print_history WHERE mail_id = @MailId ORDER BY printed_at, id;",
            new { MailId = mailId },
            cancellationToken).ConfigureAwait(false);

        return записи.ToList();
    }

    /// <inheritdoc />
    public async Task<Page<PrintHistory>> GetPagedAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var нормализованный = request.Normalized();

        var всего = await _сессия.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM print_history;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (всего == 0)
        {
            return Page<PrintHistory>.Empty(нормализованный);
        }

        var записи = await _сессия.QueryAsync<PrintHistory>(
            "SELECT * FROM print_history ORDER BY printed_at, id LIMIT @Limit OFFSET @Offset;",
            new { Limit = нормализованный.Limit, Offset = нормализованный.Offset },
            cancellationToken).ConfigureAwait(false);

        return new Page<PrintHistory>(
            записи.ToList(),
            нормализованный.Offset,
            нормализованный.Limit,
            всего);
    }
}
