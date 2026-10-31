using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий осей вложений на SQLite (ТЗ §3.1.12).
/// </summary>
public sealed class InventoryRepository : IInventoryRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий осей вложений.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public InventoryRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        inventory.CreatedAt = inventory.CreatedAt == default ? DateTime.UtcNow : inventory.CreatedAt;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO inventories (name, counterparty_id, destination, note, created_at)
            VALUES (@Name, @CounterpartyId, @Destination, @Note, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                inventory.Name,
                inventory.CounterpartyId,
                inventory.Destination,
                inventory.Note,
                inventory.CreatedAt,
            },
            cancellationToken).ConfigureAwait(false);

        inventory.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<Inventory?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<Inventory>(
            "SELECT * FROM inventories WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Inventory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var описи = await _сессия.QueryAsync<Inventory>(
            "SELECT * FROM inventories ORDER BY created_at, id;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return описи.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return await _сессия.ExecuteAsync(
            """
            UPDATE inventories SET name = @Name, counterparty_id = @CounterpartyId,
                destination = @Destination, note = @Note
            WHERE id = @Id;
            """,
            new
            {
                inventory.Name,
                inventory.CounterpartyId,
                inventory.Destination,
                inventory.Note,
                inventory.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM inventories WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
