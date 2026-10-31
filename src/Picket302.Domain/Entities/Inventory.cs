namespace Picket302.Domain.Entities;

/// <summary>
/// Опись вложений (ТЗ §3.1.12, таблица <c>inventories</c>).
/// </summary>
public sealed class Inventory
{
    /// <summary>Идентификатор описи (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Наименование описи.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Контрагент, к которому относится опись.</summary>
    public long? CounterpartyId { get; set; }

    /// <summary>Назначение («Куда»).</summary>
    public string? Destination { get; set; }

    /// <summary>Пояснение к описи.</summary>
    public string? Note { get; set; }

    /// <summary>Момент создания описи (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
