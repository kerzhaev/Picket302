namespace Picket302.Domain.Entities;

/// <summary>
/// Группа (категория) адресной книги (ТЗ §3.1.2, таблица <c>groups</c>).
/// </summary>
/// <remarks>
/// В системе ровно одна группа отмечена как системная группа по умолчанию
/// («Неразобранные»). Поле <see cref="IsDefault"/> не может быть выставлено более
/// чем у одной группы — правило проверяется доменной политикой
/// <see cref="DefaultGroupPolicy"/> и частичным уникальным индексом в схеме БД.
/// </remarks>
public sealed class Group
{
    /// <summary>Наименование системной группы по умолчанию (ТЗ §3.1.2).</summary>
    public const string DefaultGroupName = "Неразобранные";

    /// <summary>Идентификатор группы (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Наименование группы; уникально в пределах справочника.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Пояснение к группе.</summary>
    public string? Note { get; set; }

    /// <summary>Признак системной группы по умолчанию.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Блокировка добавления новых записей в группу.</summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// Родительская категория; <see langword="null"/> — категория верхнего уровня
    /// (ТЗ §8.12, функция 154: «Дерево категорий адресатов»).
    /// </summary>
    /// <remarks>
    /// Ссылка на родителя хранится в колонке <c>groups.parent_id</c>. Циклические
    /// ссылки пресекаются валидатором домена и триггером схемы БД.
    /// </remarks>
    public long? ParentId { get; set; }

    /// <summary>Порядок сортировки в справочнике.</summary>
    public int SortOrder { get; set; }

    /// <summary>Создаёт группу по умолчанию «Неразобранные».</summary>
    /// <returns>Новая группа, отмеченная как системная группа по умолчанию.</returns>
    public static Group CreateDefault() => new()
    {
        Name = DefaultGroupName,
        IsDefault = true,
    };

    /// <summary>
    /// Маркер версии записи: значение, предъявляемое при изменении, чтобы
    /// служба отклонила правку устаревшей копии.
    /// </summary>
    /// <remarks>
    /// <para>
    /// В совместном режиме версию ведёт служба в отдельной таблице
    /// <c>shared_row_versions</c>; в DTO записи она передаётся клиенту и
    /// обратно. В одиночном режиме версия не ведётся и остаётся равной нулю.
    /// </para>
    /// <para>
    /// Само поле в таблицах базы отсутствует: существующие репозитории и
    /// одиночный режим остаются неизменными.
    /// </para>
    /// </remarks>
    public long Version { get; set; }

}
