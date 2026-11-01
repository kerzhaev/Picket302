namespace Picket302.Application.Queries;

/// <summary>
/// Колонка таблицы адресной книги (ТЗ §8.1, функция 9: «Фильтры и сортировка
/// по любой колонке»).
/// </summary>
/// <param name="GridName">
/// Имя столбца таблицы адресной книги — по нему заголовок опознаётся при
/// клике мышью.
/// </param>
/// <param name="SortKey">
/// Ключ сортировки, передаваемый репозиторию: ключ из белого списка
/// выражений сортировки.
/// </param>
/// <param name="Caption">Подпись столбца.</param>
public sealed record AddressBookColumn(
    string GridName,
    string SortKey,
    string Caption);

/// <summary>
/// Каталог колонок адресной книги: соответствие столбцов таблицы и доменных
/// полей записи (ТЗ §8.1, функция 9).
/// </summary>
/// <remarks>
/// <para>
/// Состав колонок адресной книги: «VIP», «Индекс», «Получатель», «Город»,
/// «Улица», «Копий», «Группа».
/// </para>
/// <para>
/// Столбцы «Индекс», «Город» и «Улица» показывают основной адрес записи —
/// тот, что задан типом <c>address_main_type</c>. Их ключи сортировки
/// (<c>main_zip</c>, <c>main_city</c>, <c>main_street</c>) — вычисляемые
/// проекции основного адреса; ключи <c>zip</c>, <c>city</c> и <c>street</c>
/// относятся к почтовому адресу.
/// </para>
/// <para>
/// Количество копий относится к отправлению, а не к контрагенту, поэтому в
/// адресной книге столбец «Копий» всегда содержит единицу, а сортировка по
/// нему сохраняет порядок записей.
/// </para>
/// </remarks>
public static class AddressBookColumnCatalog
{
    /// <summary>Столбец «VIP» — признак избранного адреса.</summary>
    public const string КлючVip = "vip";

    /// <summary>Столбец «Индекс» — почтовый индекс основного адреса.</summary>
    public const string КлючИндекс = "main_zip";

    /// <summary>Столбец «Получатель» — наименование контрагента.</summary>
    public const string КлючПолучатель = "name";

    /// <summary>Столбец «Город» — населённый пункт основного адреса.</summary>
    public const string КлючГород = "main_city";

    /// <summary>Столбец «Улица» — адресная строка 2 основного адреса.</summary>
    public const string КлючУлица = "main_street";

    /// <summary>Столбец «Копий» — количество экземпляров.</summary>
    public const string КлючКопий = "copies";

    /// <summary>Столбец «Группа» — наименование группы (категории).</summary>
    public const string КлючГруппа = "group";

    /// <summary>Колонки адресной книги в порядке отображения.</summary>
    public static IReadOnlyList<AddressBookColumn> Columns { get; } =
    [
        new("VIP", КлючVip, "VIP"),
        new("Индекс", КлючИндекс, "Индекс"),
        new("Получатель", КлючПолучатель, "Получатель"),
        new("Город", КлючГород, "Город"),
        new("Улица", КлючУлица, "Улица"),
        new("Копий", КлючКопий, "Копий"),
        new("Группа", КлючГруппа, "Группа"),
    ];

    /// <summary>
    /// Находит колонку адресной книги по имени столбца таблицы.
    /// </summary>
    /// <param name="gridName">Имя столбца таблицы.</param>
    /// <returns>Колонка адресной книги либо <see langword="null"/>, если столбец не входит в состав адресной книги.</returns>
    public static AddressBookColumn? Найти(string? gridName)
    {
        if (string.IsNullOrWhiteSpace(gridName))
        {
            return null;
        }

        return Columns.FirstOrDefault(колонка => string.Equals(
            колонка.GridName,
            gridName.Trim(),
            StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Проверяет, что ключ сортировки входит в каталог колонок адресной книги.
    /// </summary>
    /// <param name="sortKey">Проверяемый ключ сортировки.</param>
    /// <returns><see langword="true"/>, если ключ поддерживается.</returns>
    public static bool ПоддерживаетсяСортировка(string? sortKey)
    {
        if (string.IsNullOrWhiteSpace(sortKey))
        {
            return false;
        }

        return Columns.Any(колонка => string.Equals(
            колонка.SortKey,
            sortKey.Trim(),
            StringComparison.OrdinalIgnoreCase));
    }
}
