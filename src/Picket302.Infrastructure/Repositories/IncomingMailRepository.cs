using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий журнала входящей корреспонденции на SQLite (ТЗ §8.2, функция 29).
/// </summary>
public sealed class IncomingMailRepository : IIncomingMailRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий входящей корреспонденции.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public IncomingMailRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Регистрационный номер уже занят (нарушение UNIQUE — драйверное
    /// исключение транслируется в доменный контракт, D-S32-01).
    /// </exception>
    public async Task<long> AddAsync(IncomingMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentException.ThrowIfNullOrWhiteSpace(mail.RegNumber);

        long идентификатор;

        try
        {
            идентификатор = await ВставитьAsync(mail, cancellationToken).ConfigureAwait(false);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ошибка)
            when (ошибка.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Регистрационный номер «{mail.RegNumber}» уже занят.",
                ошибка);
        }

        mail.Id = идентификатор;

        return идентификатор;
    }

    /// <summary>Выполняет вставку записи журнала.</summary>
    private async Task<long> ВставитьAsync(IncomingMail mail, CancellationToken cancellationToken)
    {
        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO incoming_mails (reg_number, reg_date, sender, subject, doc_type, scan_path, created_at)
            VALUES (@RegNumber, @RegDate, @Sender, @Subject, @DocType, @ScanPath, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                mail.RegNumber,
                RegDate = ФорматДаты(mail.RegDate),
                mail.Sender,
                mail.Subject,
                DocType = (long)mail.DocType,
                mail.ScanPath,
                CreatedAt = ФорматДаты(mail.CreatedAt),
            },
            cancellationToken).ConfigureAwait(false);

        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<IncomingMail?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var записи = await _сессия.QueryAsync<IncomingMail>(
            "SELECT * FROM incoming_mails WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return записи.FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncomingMail>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            var все = await _сессия.QueryAsync<IncomingMail>(
                "SELECT * FROM incoming_mails ORDER BY reg_date DESC, id DESC;",
                cancellationToken).ConfigureAwait(false);

            return все.ToList();
        }

        var образец = $"%{query.Trim()}%";

        var найденные = await _сессия.QueryAsync<IncomingMail>(
            """
            SELECT * FROM incoming_mails
            WHERE reg_number LIKE @Образец
               OR sender LIKE @Образец
               OR subject LIKE @Образец
            ORDER BY reg_date DESC, id DESC;
            """,
            new { Образец = образец },
            cancellationToken).ConfigureAwait(false);

        return найденные.ToList();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM incoming_mails WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <summary>Формат даты для хранения: сортируемая строка UTC.</summary>
    private static string ФорматДаты(DateTime момент) => момент
        .ToUniversalTime()
        .ToString(SqliteDateTimeHandler.Формат, System.Globalization.CultureInfo.InvariantCulture);
}
