using Picket302.Domain.Contracts;
using Picket302.Domain.Dictionaries;

namespace Picket302.Application.Import;

/// <summary>
/// Дополнительные поля импорта, распознаваемые по имени столбца (ТЗ §8.12,
/// функция 159, шаг 3 «Сопоставление по столбцам»).
/// </summary>
/// <remarks>
/// Фиксированный список слотов ТЗ не содержит реквизитов, поэтому они
/// распознаются отдельно: ИНН участвует в ключе слияния, индекс — в адресе,
/// телефон и электронная почта — в карточке контрагента.
/// </remarks>
public enum ImportExtraField
{
    /// <summary>ИНН (либо УНП).</summary>
    Inn = 0,

    /// <summary>Почтовый индекс.</summary>
    PostalZip = 1,

    /// <summary>Телефон.</summary>
    Phone = 2,

    /// <summary>Электронная почта.</summary>
    Email = 3,
}

/// <summary>
/// Сопоставление дополнительного поля импорта со столбцом файла (ТЗ §8.12,
/// функция 159).
/// </summary>
/// <param name="Field">Распознанное поле.</param>
/// <param name="ColumnIndex">Номер столбца файла (нумерация с нуля).</param>
/// <param name="ColumnName">Заголовок столбца файла.</param>
public sealed record ImportExtraBinding(ImportExtraField Field, int ColumnIndex, string ColumnName);

/// <summary>
/// Сопоставление столбцов файла со слотами полей мастера импорта (ТЗ §8.12,
/// функция 159, шаг 3 «Сопоставление по столбцам»).
/// </summary>
/// <remarks>
/// <para>
/// Сопоставление выполняется по столбцам: один столбец файла отображается
/// ровно в одно поле из фиксированного списка ТЗ (ФИО / Название организации /
/// Почтовый адрес / Юридический адрес / Фактический адрес / Прочее / Название /
/// Сокращенное).
/// </para>
/// <para>
/// Маппер умеет подобрать слот по имени столбца и принять ручное сопоставление
/// оператора. Результат сопоставления сохраняется в профиле
/// (<see cref="CsvProfile"/>) и может быть сохранён в файл <c>*.csvprof</c>.
/// </para>
/// </remarks>
public sealed class ImportFieldMapper
{
    /// <summary>
    /// Все слоты полей мастера импорта в порядке, зафиксированном ТЗ.
    /// </summary>
    public static IReadOnlyList<ImportFieldSlot> Slots => ImportFieldSlotCatalog.All;

    /// <summary>
    /// Подбирает сопоставление столбцов со слотами по именам столбцов.
    /// </summary>
    /// <param name="columnNames">Заголовки столбцов файла.</param>
    /// <returns>Сопоставление: по одному столбцу на слот.</returns>
    public IReadOnlyList<ImportColumnBinding> Suggest(IReadOnlyList<string> columnNames)
    {
        ArgumentNullException.ThrowIfNull(columnNames);

        var привязки = new List<ImportColumnBinding>();
        var занятыеСлоты = new HashSet<ImportFieldSlot>();

        for (var индекс = 0; индекс < columnNames.Count; индекс++)
        {
            var заголовок = columnNames[индекс] ?? string.Empty;
            var слот = GuessSlot(заголовок);

            if (слот is null || !занятыеСлоты.Add(слот.Value))
            {
                continue;
            }

            привязки.Add(new ImportColumnBinding(индекс, слот, заголовок));
        }

        return привязки;
    }

    /// <summary>
    /// Принимает ручное сопоставление оператора: проверяет индексы столбцов и
    /// убирает повторное использование одного слота (для слота сохраняется
    /// первое сопоставление).
    /// </summary>
    /// <param name="bindings">Сопоставление оператора.</param>
    /// <returns>Нормализованное сопоставление.</returns>
    public IReadOnlyList<ImportColumnBinding> Apply(IEnumerable<ImportColumnBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var привязки = new List<ImportColumnBinding>();
        var занятыеСлоты = new HashSet<ImportFieldSlot>();

        foreach (var привязка in bindings)
        {
            if (привязка.ColumnIndex < 0)
            {
                continue;
            }

            if (привязка.Slot is null)
            {
                continue;
            }

            if (!занятыеСлоты.Add(привязка.Slot.Value))
            {
                continue;
            }

            привязки.Add(привязка);
        }

        return привязки;
    }

    /// <summary>
    /// Подбирает дополнительные поля импорта по именам столбцов.
    /// </summary>
    /// <param name="columnNames">Заголовки столбцов файла.</param>
    /// <returns>Сопоставление дополнительных полей со столбцами.</returns>
    public IReadOnlyList<ImportExtraBinding> SuggestExtras(IReadOnlyList<string> columnNames)
    {
        ArgumentNullException.ThrowIfNull(columnNames);

        var привязки = new List<ImportExtraBinding>();
        var занятыеПоля = new HashSet<ImportExtraField>();

        for (var индекс = 0; индекс < columnNames.Count; индекс++)
        {
            var заголовок = columnNames[индекс] ?? string.Empty;
            var поле = GuessExtra(заголовок);

            if (поле is null || !занятыеПоля.Add(поле.Value))
            {
                continue;
            }

            привязки.Add(new ImportExtraBinding(поле.Value, индекс, заголовок));
        }

        return привязки;
    }

    /// <summary>
    /// Подбирает слот поля по заголовку столбца.
    /// </summary>
    /// <param name="columnName">Заголовок столбца файла.</param>
    /// <returns>Слот поля либо <see langword="null"/>, если столбец не распознан.</returns>
    public static ImportFieldSlot? GuessSlot(string? columnName)
    {
        var нормализованное = Нормализовать(columnName);

        if (нормализованное.Length == 0)
        {
            return null;
        }

        if (Псевдонимы.TryGetValue(нормализованное, out var слот))
        {
            return слот;
        }

        foreach (var известный in ImportFieldSlotCatalog.All)
        {
            if (string.Equals(Нормализовать(ImportFieldSlotCatalog.Title(известный)), нормализованное, StringComparison.Ordinal))
            {
                return известный;
            }
        }

        return null;
    }

    /// <summary>
    /// Подбирает дополнительное поле импорта по заголовку столбца.
    /// </summary>
    /// <param name="columnName">Заголовок столбца файла.</param>
    /// <returns>Дополнительное поле либо <see langword="null"/>.</returns>
    public static ImportExtraField? GuessExtra(string? columnName)
    {
        var нормализованное = Нормализовать(columnName);

        if (нормализованное.Length == 0)
        {
            return null;
        }

        return ДополнительныеПсевдонимы.TryGetValue(нормализованное, out var поле)
            ? поле
            : null;
    }

    /// <summary>
    /// Формирует профиль сопоставления по найденным привязкам, чтобы его можно
    /// было сохранить в файл <c>*.csvprof</c> и использовать повторно.
    /// </summary>
    /// <param name="profile">Исходный профиль; его параметры сохраняются.</param>
    /// <param name="bindings">Сопоставление столбцов со слотами.</param>
    /// <returns>Профиль с заполненным сопоставлением.</returns>
    public CsvProfile BuildProfile(CsvProfile profile, IEnumerable<ImportColumnBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(bindings);

        profile.Bindings = Apply(bindings).ToList();

        return profile;
    }

    /// <summary>
    /// Возвращает русскую подпись слота поля.
    /// </summary>
    /// <param name="slot">Слот поля.</param>
    /// <returns>Подпись поля.</returns>
    public static string SlotTitle(ImportFieldSlot slot) => ImportFieldSlotCatalog.Title(slot);

    /// <summary>
    /// Нормализует заголовок столбца: нижний регистр, удаление точек и
    /// скобок, схлопывание пробелов.
    /// </summary>
    /// <param name="columnName">Заголовок столбца.</param>
    /// <returns>Нормализованный заголовок.</returns>
    private static string Нормализовать(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return string.Empty;
        }

        var символы = columnName
            .Where(символ => символ is not ('.' or '(' or ')' or '"' or '«' or '»'))
            .Select(char.ToLowerInvariant)
            .ToArray();

        var части = new string(символы).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', части);
    }

    /// <summary>
    /// Псевдонимы заголовков столбцов, сопоставляемых со слотами полей.
    /// </summary>
    /// <remarks>
    /// Кроме русских заголовков в словарь входят псевдонимы единого словаря
    /// полей (ТЗ §3.2): адресные строки получателя <c>to_str1..to_str6</c>,
    /// <c>str_to1..str_to6</c>, <c>КомуСтрока1..КомуСтрока6</c> сопоставляются
    /// с почтовым адресом, почтовый индекс получателя <c>to_zip</c> /
    /// <c>ind_to</c> — с дополнительным полем «Индекс получателя».
    /// </remarks>
    private static readonly Dictionary<string, ImportFieldSlot> Псевдонимы = СоздатьПсевдонимы();

    /// <summary>
    /// Создаёт словарь псевдонимов заголовков столбцов.
    /// </summary>
    /// <returns>Словарь псевдонимов.</returns>
    private static Dictionary<string, ImportFieldSlot> СоздатьПсевдонимы()
    {
        var псевдонимы = new Dictionary<string, ImportFieldSlot>(StringComparer.Ordinal)
        {
            ["фио"] = ImportFieldSlot.FullName,
            ["фио контрагента"] = ImportFieldSlot.FullName,
            ["фамилия имя отчество"] = ImportFieldSlot.FullName,
            ["название организации"] = ImportFieldSlot.OrganizationName,
            ["наименование организации"] = ImportFieldSlot.OrganizationName,
            ["организация"] = ImportFieldSlot.OrganizationName,
            ["название юридического лица"] = ImportFieldSlot.OrganizationName,
            ["компания"] = ImportFieldSlot.OrganizationName,
            ["почтовый адрес"] = ImportFieldSlot.PostalAddress,
            ["адрес"] = ImportFieldSlot.PostalAddress,
            ["адрес получателя"] = ImportFieldSlot.PostalAddress,
            ["почтовый адрес получателя"] = ImportFieldSlot.PostalAddress,
            ["юридический адрес"] = ImportFieldSlot.LegalAddress,
            ["юрадрес"] = ImportFieldSlot.LegalAddress,
            ["адрес регистрации"] = ImportFieldSlot.LegalAddress,
            ["фактический адрес"] = ImportFieldSlot.ActualAddress,
            ["фактадрес"] = ImportFieldSlot.ActualAddress,
            ["адрес деятельности"] = ImportFieldSlot.ActualAddress,
            ["прочее"] = ImportFieldSlot.Other,
            ["дополнительно"] = ImportFieldSlot.Other,
            ["дополнительная информация"] = ImportFieldSlot.Other,
            ["комментарий"] = ImportFieldSlot.Other,
            ["примечание"] = ImportFieldSlot.Other,
            ["название"] = ImportFieldSlot.Name,
            ["наименование"] = ImportFieldSlot.Name,
            ["контрагент"] = ImportFieldSlot.Name,
            ["полное наименование"] = ImportFieldSlot.Name,
            ["сокращенное"] = ImportFieldSlot.ShortName,
            ["сокращенное название"] = ImportFieldSlot.ShortName,
            ["сокращенное наименование"] = ImportFieldSlot.ShortName,
            ["краткое наименование"] = ImportFieldSlot.ShortName,
            ["краткое название"] = ImportFieldSlot.ShortName,
        };

        // Адресные строки получателя единого словаря полей (ТЗ §3.2).
        for (var строка = 1; строка <= 6; строка++)
        {
            псевдонимы[$"to_str{строка}"] = ImportFieldSlot.PostalAddress;
            псевдонимы[$"str_to{строка}"] = ImportFieldSlot.PostalAddress;
            псевдонимы[$"комустрока{строка}"] = ImportFieldSlot.PostalAddress;
        }

        return псевдонимы;
    }

    /// <summary>
    /// Псевдонимы заголовков столбцов, распознаваемых как дополнительные поля.
    /// </summary>
    private static readonly Dictionary<string, ImportExtraField> ДополнительныеПсевдонимы = new(StringComparer.Ordinal)
    {
        ["инн"] = ImportExtraField.Inn,
        ["инн юл"] = ImportExtraField.Inn,
        ["инн ип"] = ImportExtraField.Inn,
        ["инн/унп"] = ImportExtraField.Inn,
        ["унп"] = ImportExtraField.Inn,
        ["индекс"] = ImportExtraField.PostalZip,
        ["почтовый индекс"] = ImportExtraField.PostalZip,
        ["индекс получателя"] = ImportExtraField.PostalZip,
        ["индексполучателя"] = ImportExtraField.PostalZip,
        ["to_zip"] = ImportExtraField.PostalZip,
        ["ind_to"] = ImportExtraField.PostalZip,
        ["телефон"] = ImportExtraField.Phone,
        ["тел"] = ImportExtraField.Phone,
        ["телефон получателя"] = ImportExtraField.Phone,
        ["эл почта"] = ImportExtraField.Email,
        ["электронная почта"] = ImportExtraField.Email,
        ["email"] = ImportExtraField.Email,
        ["e mail"] = ImportExtraField.Email,
        ["почта"] = ImportExtraField.Email,
    };
}
