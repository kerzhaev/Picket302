using Dapper;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using ПочтовоеОтправление = Picket302.Domain.Entities.Mail;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Data.Rows;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий почтовых отправлений на SQLite (ТЗ §3.1.3).
/// </summary>
public sealed class MailRepository : IMailRepository
{
    private const string КолонкиВставки = """
        counterparty_id, sender_id, distribution_id, envelope_type, notice_type, copy_count, is_vip, note,
        declared_value, weight, fee, status, need_notification, need_return_address, mail_category,
        date_created, date_printed, date_dispatched, date_confirmation, date_canceled,
        incoming_number, outgoing_number, post_register_id, russian_post_id, russian_post_batch, inventory_id,
        to_zip, to_str1, to_str2, to_str3, to_str4, to_str5, to_str6, folder_id
        """;

    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий почтовых отправлений.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public MailRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(ПочтовоеОтправление mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);

        mail.DateCreated = mail.DateCreated == default ? DateTime.UtcNow : mail.DateCreated;

        var sql = $"""
            INSERT INTO mails ({КолонкиВставки})
            VALUES (
                @CounterpartyId, @SenderId, @DistributionId, @EnvelopeType, @NoticeType, @CopyCount, @IsVip, @Note,
                @DeclaredValue, @Weight, @Fee, @Status, @NeedNotification, @NeedReturnAddress, @MailCategory,
                @DateCreated, @DatePrinted, @DateDispatched, @DateConfirmation, @DateCanceled,
                @IncomingNumber, @OutgoingNumber, @PostRegisterId, @RussianPostId, @RussianPostBatch, @InventoryId,
                @ToZip, @ToStr1, @ToStr2, @ToStr3, @ToStr4, @ToStr5, @ToStr6, @FolderId);
            SELECT last_insert_rowid();
            """;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            sql,
            Параметры(mail),
            cancellationToken).ConfigureAwait(false);

        mail.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<ПочтовоеОтправление?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var строка = await _сессия.QueryFirstOrDefaultAsync<MailRow>(
            "SELECT * FROM mails WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return строка?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(ПочтовоеОтправление mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);

        var sql = $"""
            UPDATE mails SET
                counterparty_id = @CounterpartyId, sender_id = @SenderId, distribution_id = @DistributionId,
                envelope_type = @EnvelopeType, notice_type = @NoticeType, copy_count = @CopyCount,
                is_vip = @IsVip, note = @Note, declared_value = @DeclaredValue, weight = @Weight, fee = @Fee,
                status = @Status, need_notification = @NeedNotification, need_return_address = @NeedReturnAddress,
                mail_category = @MailCategory, date_created = @DateCreated, date_printed = @DatePrinted,
                date_dispatched = @DateDispatched, date_confirmation = @DateConfirmation, date_canceled = @DateCanceled,
                incoming_number = @IncomingNumber, outgoing_number = @OutgoingNumber,
                post_register_id = @PostRegisterId, russian_post_id = @RussianPostId,
                russian_post_batch = @RussianPostBatch, inventory_id = @InventoryId,
                to_zip = @ToZip, to_str1 = @ToStr1, to_str2 = @ToStr2, to_str3 = @ToStr3,
                to_str4 = @ToStr4, to_str5 = @ToStr5, to_str6 = @ToStr6, folder_id = @FolderId
            WHERE id = @Id;
            """;

        var параметры = Параметры(mail);
        параметры.Add("Id", mail.Id);

        return await _сессия.ExecuteAsync(sql, параметры, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM mails WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <inheritdoc />
    public async Task<Page<ПочтовоеОтправление>> GetPagedAsync(
        PageRequest request,
        MailFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var нормализованный = request.Normalized();
        var условия = new List<string>();
        var параметры = new DynamicParameters();

        if (filter is not null)
        {
            if (filter.Status is not null)
            {
                условия.Add("status = @Status");
                параметры.Add("Status", (int)filter.Status.Value);
            }

            if (filter.CounterpartyId is not null)
            {
                условия.Add("counterparty_id = @CounterpartyId");
                параметры.Add("CounterpartyId", filter.CounterpartyId.Value);
            }

            if (filter.SenderId is not null)
            {
                условия.Add("sender_id = @SenderId");
                параметры.Add("SenderId", filter.SenderId.Value);
            }

            if (filter.DistributionId is not null)
            {
                условия.Add("distribution_id = @DistributionId");
                параметры.Add("DistributionId", filter.DistributionId.Value);
            }

            if (filter.PostRegisterId is not null)
            {
                условия.Add("post_register_id = @PostRegisterId");
                параметры.Add("PostRegisterId", filter.PostRegisterId.Value);
            }

            if (filter.DateFrom is not null)
            {
                условия.Add("date_created >= @DateFrom");
                параметры.Add("DateFrom", filter.DateFrom.Value);
            }

            if (filter.DateTo is not null)
            {
                условия.Add("date_created <= @DateTo");
                параметры.Add("DateTo", filter.DateTo.Value);
            }
        }

        var где = условия.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", условия);

        var всего = await _сессия.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM mails{где};",
            параметры,
            cancellationToken).ConfigureAwait(false);

        if (всего == 0)
        {
            return Page<ПочтовоеОтправление>.Empty(нормализованный);
        }

        параметры.Add("Limit", нормализованный.Limit);
        параметры.Add("Offset", нормализованный.Offset);

        var строки = await _сессия.QueryAsync<MailRow>(
            $"SELECT * FROM mails{где} ORDER BY id LIMIT @Limit OFFSET @Offset;",
            параметры,
            cancellationToken).ConfigureAwait(false);

        var элементы = строки.Select(строка => строка.ToEntity()).ToList();

        return new Page<ПочтовоеОтправление>(элементы, нормализованный.Offset, нормализованный.Limit, всего);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ПочтовоеОтправление>> GetByCounterpartyAsync(
        long counterpartyId,
        CancellationToken cancellationToken = default)
    {
        var строки = await _сессия.QueryAsync<MailRow>(
            "SELECT * FROM mails WHERE counterparty_id = @CounterpartyId ORDER BY id;",
            new { CounterpartyId = counterpartyId },
            cancellationToken).ConfigureAwait(false);

        return строки.Select(строка => строка.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ПочтовоеОтправление>> GetByDistributionAsync(
        long distributionId,
        CancellationToken cancellationToken = default)
    {
        var строки = await _сессия.QueryAsync<MailRow>(
            "SELECT * FROM mails WHERE distribution_id = @DistributionId ORDER BY id;",
            new { DistributionId = distributionId },
            cancellationToken).ConfigureAwait(false);

        return строки.Select(строка => строка.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateDraftRecipientAddressAsync(
        long counterpartyId,
        Address address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        // Обновляются только отправления в статусе «Черновик»: напечатанные
        // письма хранят снимок адреса на момент печати и не изменяются
        // (ТЗ §8.1, функция 15).
        return await _сессия.ExecuteAsync(
            """
            UPDATE mails SET
                to_zip = @ToZip,
                to_str1 = @ToStr1, to_str2 = @ToStr2, to_str3 = @ToStr3,
                to_str4 = @ToStr4, to_str5 = @ToStr5, to_str6 = @ToStr6
            WHERE counterparty_id = @CounterpartyId AND status = 0;
            """,
            new
            {
                ToZip = address.Zip,
                ToStr1 = address.Line1,
                ToStr2 = address.Line2,
                ToStr3 = address.Line3,
                ToStr4 = address.Line4,
                ToStr5 = address.Line5,
                ToStr6 = address.Line6,
                CounterpartyId = counterpartyId,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, int>> ПолучитьСчётчикиПоПапкамAsync(
        CancellationToken cancellationToken = default)
    {
        // Агрегатный запрос (F-E21): счётчики точны при любом числе писем.
        var строки = await _сессия.QueryAsync<(long FolderId, int Число)>(
            """
            SELECT folder_id AS FolderId, COUNT(*) AS Число
            FROM mails
            WHERE folder_id IS NOT NULL
            GROUP BY folder_id;
            """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return строки.ToDictionary(строка => строка.FolderId, строка => строка.Число);
    }

    /// <summary>
    /// Формирует параметры команды записи отправления.
    /// </summary>
    /// <param name="mail">Сохраняемое отправление.</param>
    /// <returns>Набор параметров команды.</returns>
    private static DynamicParameters Параметры(ПочтовоеОтправление mail)
    {
        var адрес = mail.RecipientAddress;

        var параметры = new DynamicParameters();
        параметры.Add("CounterpartyId", mail.CounterpartyId);
        параметры.Add("SenderId", mail.SenderId);
        параметры.Add("DistributionId", mail.DistributionId);
        параметры.Add("EnvelopeType", mail.EnvelopeType);
        параметры.Add("NoticeType", mail.NoticeType);
        параметры.Add("CopyCount", mail.CopyCount);
        параметры.Add("IsVip", mail.IsVip);
        параметры.Add("Note", mail.Note);
        параметры.Add("DeclaredValue", mail.DeclaredValue);
        параметры.Add("Weight", mail.Weight);
        параметры.Add("Fee", mail.Fee);
        параметры.Add("Status", (int)mail.Status);
        параметры.Add("NeedNotification", mail.NeedNotification);
        параметры.Add("NeedReturnAddress", mail.NeedReturnAddress);
        параметры.Add("MailCategory", (int)mail.MailCategory);
        параметры.Add("DateCreated", mail.DateCreated);
        параметры.Add("DatePrinted", mail.DatePrinted);
        параметры.Add("DateDispatched", mail.DateDispatched);
        параметры.Add("DateConfirmation", mail.DateConfirmation);
        параметры.Add("DateCanceled", mail.DateCanceled);
        параметры.Add("IncomingNumber", mail.IncomingNumber);
        параметры.Add("OutgoingNumber", mail.OutgoingNumber);
        параметры.Add("PostRegisterId", mail.PostRegisterId);
        параметры.Add("RussianPostId", mail.RussianPostId);
        параметры.Add("RussianPostBatch", mail.RussianPostBatch);
        параметры.Add("InventoryId", mail.InventoryId);
        параметры.Add("ToZip", адрес?.Zip ?? string.Empty);
        параметры.Add("ToStr1", адрес?.Line1);
        параметры.Add("ToStr2", адрес?.Line2);
        параметры.Add("ToStr3", адрес?.Line3);
        параметры.Add("ToStr4", адрес?.Line4);
        параметры.Add("ToStr5", адрес?.Line5);
        параметры.Add("ToStr6", адрес?.Line6);
        параметры.Add("FolderId", mail.FolderId);

        return параметры;
    }
}
