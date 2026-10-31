namespace Picket302.Domain.Entities;

/// <summary>
/// Позиция описи вложений (ТЗ §3.1.12, таблица <c>inventory_items</c>).
/// </summary>
/// <remarks>
/// Количество позиции строго положительно — ограничение проверяется схемой БД
/// (<c>CHECK (quantity &gt; 0)</c>).
/// </remarks>
public sealed class InventoryItem
{
    /// <summary>Идентификатор позиции (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Опись, которой принадлежит позиция.</summary>
    public long InventoryId { get; set; }

    /// <summary>Наименование вложения.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Количество; строго больше нуля.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Стоимость вложения.</summary>
    public decimal Cost { get; set; }

    /// <summary>Пояснение к позиции.</summary>
    public string? Note { get; set; }

    /// <summary>Момент создания позиции (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
