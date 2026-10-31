using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий позиций осей вложений на SQLite (ТЗ §3.1.12).
/// </summary>
public sealed class InventoryItemRepository : IInventoryItemRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий позиций осей вложений.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public InventoryItemRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.CreatedAt = item.CreatedAt == default ? DateTime.UtcNow : item.CreatedAt;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO inventory_items (inventory_id, name, quantity, cost, note, created_at)
            VALUES (@InventoryId, @Name, @Quantity, @Cost, @Note, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                item.InventoryId,
                item.Name,
                item.Quantity,
                item.Cost,
                item.Note,
                item.CreatedAt,
            },
            cancellationToken).ConfigureAwait(false);

        item.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<InventoryItem?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<InventoryItem>(
            "SELECT * FROM inventory_items WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryItem>> GetByInventoryAsync(
        long inventoryId,
        CancellationToken cancellationToken = default)
    {
        var позиции = await _сессия.QueryAsync<InventoryItem>(
            "SELECT * FROM inventory_items WHERE inventory_id = @InventoryId ORDER BY id;",
            new { InventoryId = inventoryId },
            cancellationToken).ConfigureAwait(false);

        return позиции.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return await _сессия.ExecuteAsync(
            """
            UPDATE inventory_items SET name = @Name, quantity = @Quantity, cost = @Cost, note = @Note
            WHERE id = @Id;
            """,
            new
            {
                item.Name,
                item.Quantity,
                item.Cost,
                item.Note,
                item.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM inventory_items WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
