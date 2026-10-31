namespace Picket302.Domain.Contracts;

/// <summary>
/// Категория адресатов — узел дерева категорий
/// (ТЗ §8.12, функция 154: «Категории адресатов: дерево и операции»).
/// </summary>
/// <param name="Id">Идентификатор категории.</param>
/// <param name="Name">Наименование категории.</param>
/// <param name="ParentId">Родительская категория; <see langword="null"/> — корень.</param>
/// <param name="IsDefault">Признак группы по умолчанию («Неразобранные»).</param>
/// <param name="IsClosed">Признак блокировки добавления новых записей.</param>
/// <param name="SortOrder">Порядок сортировки в справочнике.</param>
/// <param name="CounterpartyCount">Число контрагентов в категории.</param>
public sealed record Category(
    long Id,
    string Name,
    long? ParentId,
    bool IsDefault,
    bool IsClosed,
    int SortOrder,
    int CounterpartyCount)
{
    /// <summary>Наименование системной категории по умолчанию (ТЗ §8.12, функция 138).</summary>
    public const string UnparsedName = "Неразобранные";
}
