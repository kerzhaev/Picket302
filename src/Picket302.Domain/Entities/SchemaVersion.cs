namespace Picket302.Domain.Entities;

/// <summary>
/// Версия применённой миграции схемы (ТЗ §3.1.18, таблица <c>schema_version</c>).
/// </summary>
/// <remarks>
/// Каждая миграция атомарна и пишет строку в эту таблицу. Миграции выполняются
/// только вперед (forward-only); откат схемы вручную не поддерживается.
/// </remarks>
public sealed class SchemaVersion
{
    /// <summary>Идентификатор записи (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Номер версии схемы.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Описание миграции.</summary>
    public string? Description { get; set; }

    /// <summary>Момент применения миграции (UTC).</summary>
    public DateTime AppliedAt { get; set; }
}
