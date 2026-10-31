namespace Picket302.Domain.Dictionaries;

/// <summary>
/// Словарь полей: единая схема «БД ↔ Шаблоны ↔ Импорт» (ТЗ §3.2).
/// </summary>
/// <remarks>
/// <para>
/// ТЗ §3.2: «Во избежание расхождений и повреждения данных при миграциях и
/// сопоставлениях закрепляется строгая матрица имен полей. Никакие другие имена
/// не допускаются к использованию в выражениях бланков и маппере импорта».
/// </para>
/// <para>
/// Словарь — единственный источник имён: колонка БД, тег привязки в шаблоне и
/// псевдоним маппера импорта описаны в одном месте. Пользовательские поля
/// контрагента дополняют словарь тегами вида <c>[Custom_&lt;имя&gt;]</c>
/// (ТЗ §3.1.9).
/// </para>
/// </remarks>
public static class FieldDictionary
{
    /// <summary>Колонка внутреннего идентификатора записи.</summary>
    public const string ColumnId = "counterparties.id";

    /// <summary>Колонка почтового индекса получателя.</summary>
    public const string ColumnToZip = "to_zip";

    /// <summary>Колонка адресной строки получателя 1.</summary>
    public const string ColumnToStr1 = "to_str1";

    /// <summary>Колонка адресной строки получателя 2.</summary>
    public const string ColumnToStr2 = "to_str2";

    /// <summary>Колонка адресной строки получателя 3.</summary>
    public const string ColumnToStr3 = "to_str3";

    /// <summary>Колонка адресной строки получателя 4.</summary>
    public const string ColumnToStr4 = "to_str4";

    /// <summary>Колонка адресной строки получателя 5.</summary>
    public const string ColumnToStr5 = "to_str5";

    /// <summary>Колонка адресной строки получателя 6.</summary>
    public const string ColumnToStr6 = "to_str6";

    /// <summary>Колонка почтового индекса отправителя.</summary>
    public const string ColumnFromZip = "from_zip";

    /// <summary>Колонка адресной строки отправителя 1.</summary>
    public const string ColumnFromStr1 = "from_str1";

    /// <summary>Колонка адресной строки отправителя 2.</summary>
    public const string ColumnFromStr2 = "from_str2";

    /// <summary>Колонка адресной строки отправителя 3.</summary>
    public const string ColumnFromStr3 = "from_str3";

    /// <summary>Колонка адресной строки отправителя 4.</summary>
    public const string ColumnFromStr4 = "from_str4";

    /// <summary>Колонка адресной строки отправителя 5.</summary>
    public const string ColumnFromStr5 = "from_str5";

    /// <summary>Колонка адресной строки отправителя 6.</summary>
    public const string ColumnFromStr6 = "from_str6";

    /// <summary>Колонка количества экземпляров.</summary>
    public const string ColumnCopyCount = "copy_count";

    /// <summary>Колонка признака приоритетного адресата.</summary>
    public const string ColumnIsVip = "is_vip";

    /// <summary>Колонка технологического примечания.</summary>
    public const string ColumnNote = "note";

    /// <summary>Колонка суммы объявленной ценности.</summary>
    public const string ColumnDeclaredValue = "declared_value";

    /// <summary>Колонка платы за пересылку.</summary>
    public const string ColumnFee = "fee";

    /// <summary>Колонка веса отправления.</summary>
    public const string ColumnWeight = "weight";

    /// <summary>Колонка машинночитаемого штрих-кода индекса.</summary>
    public const string ColumnPostIndexMachine = "post_index_machine";

    /// <summary>Тег внутреннего идентификатора записи.</summary>
    public const string TagId = "[id]";

    /// <summary>Тег почтового индекса получателя.</summary>
    public const string TagToZip = "[edtIndeks_to]";

    /// <summary>Тег почтового индекса отправителя.</summary>
    public const string TagFromZip = "[edtIndeks_from]";

    /// <summary>Тег количества экземпляров.</summary>
    public const string TagCopyCount = "[copyes]";

    /// <summary>Тег признака приоритетного адресата.</summary>
    public const string TagIsVip = "[vip]";

    /// <summary>Тег технологического примечания.</summary>
    public const string TagNote = "[edt_str_prim]";

    /// <summary>Тег суммы объявленной ценности.</summary>
    public const string TagDeclaredValue = "[declared_value]";

    /// <summary>Тег платы за пересылку.</summary>
    public const string TagFee = "[fee]";

    /// <summary>Тег веса отправления.</summary>
    public const string TagWeight = "[weight]";

    /// <summary>Тег машинночитаемого штрих-кода индекса.</summary>
    public const string TagPostIndexMachine = "[POSTINDEX]";

    /// <summary>Префикс тега пользовательского поля контрагента (ТЗ §3.1.9).</summary>
    public const string CustomTagPrefix = "Custom_";

    private static readonly IReadOnlyList<FieldDescriptor> Описание =
    [
        new(
            ColumnId,
            "Внутренний ID записи",
            TagId,
            Array.Empty<string>()),
        new(
            ColumnToZip,
            "Почтовый индекс получателя",
            TagToZip,
            ["ИндексПолучателя", "ind_to"]),
        new(
            ColumnToStr1,
            "Строка получателя 1 (Организация/ФИО)",
            "[edt_str_to1]",
            ["КомуСтрока1", "str_to1"]),
        new(
            ColumnToStr2,
            "Строка получателя 2 (Подразделение/Улица)",
            "[edt_str_to2]",
            ["КомуСтрока2", "str_to2"]),
        new(
            ColumnToStr3,
            "Строка получателя 3 (Дом, корпус, строение)",
            "[edt_str_to3]",
            ["КомуСтрока3", "str_to3"]),
        new(
            ColumnToStr4,
            "Строка получателя 4 (Квартира/Офис)",
            "[edt_str_to4]",
            ["КомуСтрока4", "str_to4"]),
        new(
            ColumnToStr5,
            "Строка получателя 5 (Населенный пункт)",
            "[edt_str_to5]",
            ["КомуСтрока5", "str_to5"]),
        new(
            ColumnToStr6,
            "Строка получателя 6 (Область/Край/Регион)",
            "[edt_str_to6]",
            ["КомуСтрока6", "str_to6"]),
        new(
            ColumnFromZip,
            "Почтовый индекс отправителя",
            TagFromZip,
            ["ИндексОтправителя", "ind_from"]),
        new(
            ColumnFromStr1,
            "Адресная строка отправителя 1",
            "[edt_str_from1]",
            ["ОтКогоСтрока1", "str_from1"]),
        new(
            ColumnFromStr2,
            "Адресная строка отправителя 2",
            "[edt_str_from2]",
            ["ОтКогоСтрока2", "str_from2"]),
        new(
            ColumnFromStr3,
            "Адресная строка отправителя 3",
            "[edt_str_from3]",
            ["ОтКогоСтрока3", "str_from3"]),
        new(
            ColumnFromStr4,
            "Адресная строка отправителя 4",
            "[edt_str_from4]",
            ["ОтКогоСтрока4", "str_from4"]),
        new(
            ColumnFromStr5,
            "Адресная строка отправителя 5",
            "[edt_str_from5]",
            ["ОтКогоСтрока5", "str_from5"]),
        new(
            ColumnFromStr6,
            "Адресная строка отправителя 6",
            "[edt_str_from6]",
            ["ОтКогоСтрока6", "str_from6"]),
        new(
            ColumnCopyCount,
            "Количество экземпляров",
            TagCopyCount,
            ["КоличествоКопий", "copyes"]),
        new(
            ColumnIsVip,
            "Признак приоритетного адресата",
            TagIsVip,
            ["Избранное", "vip"]),
        new(
            ColumnNote,
            "Технологическое примечание",
            TagNote,
            ["Примечание", "prim"]),
        new(
            ColumnDeclaredValue,
            "Сумма объявленной ценности",
            TagDeclaredValue,
            ["Ценность", "declared_value"]),
        new(
            ColumnFee,
            "Плата за пересылку",
            TagFee,
            ["Тариф", "fee"]),
        new(
            ColumnWeight,
            "Вес отправления (кг)",
            TagWeight,
            ["Вес", "weight"]),
        new(
            ColumnPostIndexMachine,
            "Машиночитаемый штрих-код индекса",
            TagPostIndexMachine,
            Array.Empty<string>(),
            Computed: true),
    ];

    private static readonly IReadOnlyDictionary<string, FieldDescriptor> ПоКолонке =
        Описание.ToDictionary(поле => поле.ColumnName, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, FieldDescriptor> ПоТегу =
        Описание.ToDictionary(поле => поле.TemplateTag, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, FieldDescriptor> ПоПсевдониму =
        Описание
            .SelectMany(поле => поле.ImportAliases.Select(псевдоним => (Псевдоним: псевдоним, Поле: поле)))
            .ToDictionary(
                пара => пара.Псевдоним,
                пара => пара.Поле,
                StringComparer.OrdinalIgnoreCase);

    /// <summary>Все поля словаря в порядке матрицы ТЗ §3.2.</summary>
    public static IReadOnlyList<FieldDescriptor> All => Описание;

    /// <summary>Поля, доступные мапперу импорта.</summary>
    public static IReadOnlyList<FieldDescriptor> Importable =>
        Описание.Where(поле => поле.IsImportable).ToList();

    /// <summary>Имена колонок БД, участвующих в словаре.</summary>
    public static IReadOnlyList<string> ColumnNames =>
        Описание.Select(поле => поле.ColumnName).ToList();

    /// <summary>Теги шаблонов, участвующие в словаре.</summary>
    public static IReadOnlyList<string> TemplateTags =>
        Описание.Select(поле => поле.TemplateTag).ToList();

    /// <summary>
    /// Возвращает описание поля по имени колонки БД.
    /// </summary>
    /// <param name="columnName">Имя колонки.</param>
    /// <returns>Описание поля либо <see langword="null"/>, если имя не зафиксировано в ТЗ §3.2.</returns>
    public static FieldDescriptor? FindByColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return null;
        }

        return ПоКолонке.GetValueOrDefault(columnName);
    }

    /// <summary>
    /// Возвращает описание поля по тегу привязки в шаблоне.
    /// </summary>
    /// <param name="templateTag">Тег шаблона.</param>
    /// <returns>Описание поля либо <see langword="null"/>, если тег не зафиксирован в ТЗ §3.2.</returns>
    public static FieldDescriptor? FindByTemplateTag(string templateTag)
    {
        if (string.IsNullOrWhiteSpace(templateTag))
        {
            return null;
        }

        return ПоТегу.GetValueOrDefault(templateTag);
    }

    /// <summary>
    /// Возвращает описание поля по псевдониму маппера импорта.
    /// </summary>
    /// <param name="alias">Псевдоним колонки файла импорта.</param>
    /// <returns>Описание поля либо <see langword="null"/>, если псевдоним не зафиксирован в ТЗ §3.2.</returns>
    public static FieldDescriptor? FindByImportAlias(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        return ПоПсевдониму.GetValueOrDefault(alias);
    }

    /// <summary>
    /// Проверяет, что имя колонки зафиксировано в словаре.
    /// </summary>
    /// <param name="columnName">Имя колонки.</param>
    /// <returns><see langword="true"/>, если имя колонки допустимо.</returns>
    public static bool IsKnownColumn(string columnName) => FindByColumn(columnName) is not null;

    /// <summary>
    /// Проверяет, что тег шаблона зафиксирован в словаре либо является тегом
    /// пользовательского поля контрагента <c>[Custom_&lt;имя&gt;]</c>.
    /// </summary>
    /// <param name="templateTag">Тег шаблона.</param>
    /// <returns><see langword="true"/>, если тег допустим.</returns>
    public static bool IsAllowedTemplateTag(string templateTag)
    {
        if (string.IsNullOrWhiteSpace(templateTag))
        {
            return false;
        }

        return FindByTemplateTag(templateTag) is not null || IsCustomTemplateTag(templateTag);
    }

    /// <summary>
    /// Проверяет, что тег является тегом пользовательского поля контрагента.
    /// </summary>
    /// <param name="templateTag">Тег шаблона.</param>
    /// <returns><see langword="true"/>, если тег имеет форму <c>[Custom_имя]</c>.</returns>
    public static bool IsCustomTemplateTag(string templateTag)
    {
        if (string.IsNullOrWhiteSpace(templateTag))
        {
            return false;
        }

        return templateTag.StartsWith('[')
            && templateTag.EndsWith(']')
            && templateTag.Length > CustomTagPrefix.Length + 2
            && templateTag.AsSpan(1).StartsWith(CustomTagPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Формирует тег шаблона для пользовательского поля контрагента.
    /// </summary>
    /// <param name="fieldName">Имя пользовательского поля.</param>
    /// <returns>Тег вида <c>[Custom_имя]</c>.</returns>
    public static string BuildCustomTag(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return $"[{CustomTagPrefix}{fieldName}]";
    }
}
