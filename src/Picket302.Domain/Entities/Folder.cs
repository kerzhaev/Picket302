namespace Picket302.Domain.Entities;

/// <summary>
/// Папка писем (ТЗ §3.1.11, таблица <c>folders</c>).
/// </summary>
/// <remarks>
/// Иерархия папок задаётся ссылкой <see cref="ParentId"/>. Циклические ссылки
/// (папка внутри самой себя либо внутри собственного потомка) пресекаются
/// валидатором домена (ТЗ §3.1.11).
/// </remarks>
public sealed class Folder
{
    /// <summary>Идентификатор папки (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Наименование папки.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Родительская папка; <see langword="null"/> — корень.</summary>
    public long? ParentId { get; set; }

    /// <summary>Пояснение к папке.</summary>
    public string? Note { get; set; }

    /// <summary>Порядок сортировки.</summary>
    public int SortOrder { get; set; }
}
