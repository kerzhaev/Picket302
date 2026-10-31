using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий пользовательских полей контрагента на SQLite (ТЗ §3.1.9).
/// </summary>
public sealed class CounterpartyFieldRepository : ICounterpartyFieldRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий пользовательских полей контрагента.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public CounterpartyFieldRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(CounterpartyField field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);

        field.UpdatedAt = DateTime.UtcNow;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO counterparty_fields (counterparty_id, field_name, field_value, field_type, updated_at)
            VALUES (@CounterpartyId, @FieldName, @FieldValue, @FieldType, @UpdatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                field.CounterpartyId,
                field.FieldName,
                field.FieldValue,
                FieldType = (int)field.FieldType,
                field.UpdatedAt,
            },
            cancellationToken).ConfigureAwait(false);

        field.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<CounterpartyField?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<CounterpartyField>(
            "SELECT * FROM counterparty_fields WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CounterpartyField>> GetByCounterpartyAsync(
        long counterpartyId,
        CancellationToken cancellationToken = default)
    {
        var поля = await _сессия.QueryAsync<CounterpartyField>(
            "SELECT * FROM counterparty_fields WHERE counterparty_id = @CounterpartyId ORDER BY field_name;",
            new { CounterpartyId = counterpartyId },
            cancellationToken).ConfigureAwait(false);

        return поля.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(CounterpartyField field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);

        field.UpdatedAt = DateTime.UtcNow;

        return await _сессия.ExecuteAsync(
            """
            UPDATE counterparty_fields SET field_name = @FieldName, field_value = @FieldValue,
                field_type = @FieldType, updated_at = @UpdatedAt
            WHERE id = @Id;
            """,
            new
            {
                field.FieldName,
                field.FieldValue,
                FieldType = (int)field.FieldType,
                field.UpdatedAt,
                field.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM counterparty_fields WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
