using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Picket302.Domain.Dictionaries;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Templating;

/// <summary>
/// Описание одного маркера документа вида <c>#Имя#</c> (ТЗ §8.5, функция 66:
/// «Мастер редактирования шаблонов документов»).
/// </summary>
/// <param name="Name">Имя поля без обрамляющих решёток.</param>
/// <param name="Description">Описание поля на русском языке.</param>
/// <param name="Group">
/// Группа маркеров: «Справочник полей», «Карточка контрагента» либо
/// «Адреса контрагента».
/// </param>
/// <param name="Aliases">
/// Синонимы имени маркера, которые также допускаются в шаблоне: тег привязки
/// словаря полей, псевдоним маппера импорта, вариант имени в верхнем регистре.
/// </param>
public sealed record DocumentMarker(
    string Name,
    string Description,
    string Group,
    IReadOnlyList<string> Aliases)
{
    /// <summary>Текст маркера, вставляемый в шаблон: <c>#Имя#</c>.</summary>
    public string Text => DocumentMarkerCatalog.FormatMarker(Name);
}

/// <summary>
/// Каталог маркеров документов: перечень полей, доступных для подстановки в
/// шаблоны произвольного вида (ТЗ §8.5, функции 65 и 66).
/// </summary>
/// <remarks>
/// <para>
/// Источник маркеров — словарь полей (ТЗ §3.2) и реквизиты карточки
/// контрагента (ТЗ §8.1, функция 1). Синтаксис маркера: <c>#Имя#</c>.
/// </para>
/// <para>
/// Каталог используется в двух ролях: мастер шаблонов (функция 66) выводит
/// перечень маркеров и вставляет выбранный в редактор, а генератор документов
/// (функция 65) по тому же перечню строит набор параметров подстановки.
/// </para>
/// </remarks>
public sealed class DocumentMarkerCatalog
{
    /// <summary>Начало маркера документа.</summary>
    public const string MarkerPrefix = "#";

    /// <summary>Конец маркера документа.</summary>
    public const string MarkerSuffix = "#";

    /// <summary>Группа маркеров, построенных по словарю полей (ТЗ §3.2).</summary>
    public const string DictionaryGroup = "Справочник полей";

    /// <summary>Группа маркеров реквизитов карточки контрагента (ТЗ §8.1, функция 1).</summary>
    public const string CounterpartyGroup = "Карточка контрагента";

    /// <summary>Группа маркеров адресных срезов контрагента (ТЗ §8.1, функция 3).</summary>
    public const string AddressGroup = "Адреса контрагента";

    /// <summary>
    /// Шаблон маркера документа: <c>#Имя#</c>. Имя поля состоит из букв
    /// (латинских и кириллических), цифр, подчёркивания и точки.
    /// </summary>
    public static readonly Regex MarkerPattern = new(
        @"#(?<name>[A-Za-zА-Яа-яЁё_][A-Za-zА-Яа-яЁё0-9_\-\.]*)#",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<DocumentMarker> _markers;

    private readonly Dictionary<string, string> _canonicalByAlias;

    /// <summary>Создаёт каталог маркеров по словарю полей и карточке контрагента.</summary>
    public DocumentMarkerCatalog()
    {
        var markers = new List<DocumentMarker>();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in FieldDictionary.All)
        {
            var fieldAliases = BuildFieldAliases(field);

            markers.Add(new DocumentMarker(field.ColumnName, field.Description, DictionaryGroup, fieldAliases));
            Register(aliases, field.ColumnName, fieldAliases);
        }

        foreach (var marker in BuildCounterpartyMarkers())
        {
            markers.Add(marker);
            Register(aliases, marker.Name, marker.Aliases);
        }

        _markers = markers;
        _canonicalByAlias = aliases;
    }

    /// <summary>Общий экземпляр каталога для использования без внедрения зависимости.</summary>
    public static DocumentMarkerCatalog Shared { get; } = new();

    /// <summary>Все доступные маркеры в порядке: словарь полей, карточка, адреса.</summary>
    public IReadOnlyList<DocumentMarker> Markers => _markers;

    /// <summary>Имена всех доступных маркеров.</summary>
    public IReadOnlyList<string> MarkerNames => _markers.Select(marker => marker.Name).ToList();

    /// <summary>
    /// Формирует текст маркера по имени поля.
    /// </summary>
    /// <param name="name">Имя поля без решёток.</param>
    /// <returns>Текст маркера вида <c>#Имя#</c>.</returns>
    public static string FormatMarker(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return MarkerPrefix + name + MarkerSuffix;
    }

    /// <summary>
    /// Возвращает маркер по имени поля либо по одному из его синонимов.
    /// </summary>
    /// <param name="name">Имя поля, тег шаблона либо псевдоним импорта.</param>
    /// <returns>Маркер либо <see langword="null"/>, если имя не зафиксировано.</returns>
    public DocumentMarker? Find(string? name)
    {
        if (!TryResolve(name, out var canonical))
        {
            return null;
        }

        return _markers.FirstOrDefault(marker =>
            string.Equals(marker.Name, canonical, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Проверяет, что имя поля зафиксировано в каталоге.
    /// </summary>
    /// <param name="name">Имя поля.</param>
    /// <returns><see langword="true"/>, если маркер известен.</returns>
    public bool IsKnown(string? name) => TryResolve(name, out _);

    /// <summary>
    /// Приводит имя поля (либо его синоним) к каноническому имени маркера.
    /// </summary>
    /// <param name="name">Имя поля, тег шаблона либо псевдоним импорта.</param>
    /// <param name="canonicalName">Каноническое имя маркера.</param>
    /// <returns><see langword="true"/>, если имя известно каталогу.</returns>
    public bool TryResolve(string? name, out string canonicalName)
    {
        canonicalName = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = name.Trim().Trim('[', ']');

        return _canonicalByAlias.TryGetValue(key, out canonicalName!);
    }

    /// <summary>
    /// Отбирает маркеры по подстроке имени, описания или группы.
    /// </summary>
    /// <param name="filter">Фильтр; пустое значение либо <see langword="null"/> — все маркеры.</param>
    /// <returns>Отобранные маркеры.</returns>
    public IReadOnlyList<DocumentMarker> Search(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return _markers;
        }

        var sample = filter.Trim();

        return _markers
            .Where(marker =>
                marker.Name.Contains(sample, StringComparison.OrdinalIgnoreCase)
                || marker.Description.Contains(sample, StringComparison.OrdinalIgnoreCase)
                || marker.Group.Contains(sample, StringComparison.OrdinalIgnoreCase)
                || marker.Aliases.Any(alias => alias.Contains(sample, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// Ищет значение маркера в наборе параметров подстановки.
    /// </summary>
    /// <param name="name">Имя поля либо его синоним.</param>
    /// <param name="parameters">Набор параметров подстановки.</param>
    /// <param name="value">Текстовое значение маркера.</param>
    /// <returns><see langword="true"/>, если значение найдено.</returns>
    public bool TryGetParameterValue(
        string name,
        IReadOnlyDictionary<string, object> parameters,
        out string value)
    {
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(name) || parameters is null)
        {
            return false;
        }

        if (TryGetRawValue(name, parameters, out var raw))
        {
            value = FormatValue(raw);
            return true;
        }

        if (TryResolve(name, out var canonical) && TryGetRawValue(canonical, parameters, out raw))
        {
            value = FormatValue(raw);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Приводит значение параметра к текстовому виду для подстановки в шаблон.
    /// </summary>
    /// <param name="value">Значение параметра.</param>
    /// <returns>Текстовое представление значения.</returns>
    public static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "Да" : "Нет",
        DateTime date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.CurrentCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty,
    };

    /// <summary>
    /// Строит набор параметров подстановки по карточке контрагента.
    /// </summary>
    /// <param name="counterparty">Карточка контрагента.</param>
    /// <param name="customValues">
    /// Значения пользовательских полей контрагента; имя поля образует маркер
    /// вида <c>#Custom_имя#</c> (ТЗ §3.1.9).
    /// </param>
    /// <param name="sender">
    /// Отправитель; используется для маркеров адресных строк отправителя
    /// (<c>from_*</c>). Без отправителя эти маркеры в набор не попадают.
    /// </param>
    /// <returns>
    /// Набор параметров подстановки. В набор входят только фактически
    /// заполненные значения: маркер незаполненного реквизита считается
    /// отсутствующим и при пакетной генерации приводит к пропуску записи.
    /// </returns>
    public Dictionary<string, object> BuildParameters(
        Counterparty counterparty,
        IReadOnlyDictionary<string, string>? customValues = null,
        Counterparty? sender = null)
    {
        ArgumentNullException.ThrowIfNull(counterparty);

        var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        Add(parameters, "Counterparty_Id", counterparty.Id);
        Add(
            parameters,
            "Counterparty_Type",
            counterparty.Type == CounterpartyType.LegalEntity ? "Юридическое лицо" : "Физическое лицо");
        Add(parameters, "Counterparty_Name", counterparty.Name);
        Add(parameters, "Counterparty_ShortName", counterparty.ShortName);
        Add(parameters, "Counterparty_Inn", counterparty.Inn);
        Add(parameters, "Counterparty_Kpp", counterparty.Kpp);
        Add(parameters, "Counterparty_Ogrn", counterparty.Ogrn);
        Add(parameters, "Counterparty_Okfs", counterparty.Okfs);
        Add(parameters, "Counterparty_Okpo", counterparty.Okpo);
        Add(parameters, "Counterparty_Okved", counterparty.Okved);
        Add(parameters, "Counterparty_Bik", counterparty.Bik);
        Add(parameters, "Counterparty_Ks", counterparty.Ks);
        Add(parameters, "Counterparty_Rs", counterparty.Rs);
        Add(parameters, "Counterparty_Rkc", counterparty.Rkc);
        Add(parameters, "Counterparty_BankName", counterparty.BankName);
        Add(parameters, "Counterparty_PassportSeries", counterparty.PassportSeries);
        Add(parameters, "Counterparty_PassportNumber", counterparty.PassportNumber);
        Add(parameters, "Counterparty_PassportIssuedBy", counterparty.PassportIssuedBy);
        Add(parameters, "Counterparty_PassportIssuedDate", counterparty.PassportIssuedDate);
        Add(parameters, "Counterparty_BirthDate", counterparty.BirthDate);
        Add(parameters, "Counterparty_BirthPlace", counterparty.BirthPlace);
        Add(parameters, "Counterparty_Position", counterparty.Position);
        Add(parameters, "Counterparty_DirectorFio", counterparty.DirectorFio);
        Add(parameters, "Counterparty_Email", counterparty.Email);
        Add(parameters, "Counterparty_Phone", counterparty.Phone);
        Add(parameters, "Counterparty_SpecialNotes", counterparty.SpecialNotes);
        Add(parameters, "Counterparty_GroupId", counterparty.GroupId);
        Add(parameters, "Counterparty_IsVip", counterparty.IsVip);
        Add(parameters, "Counterparty_IsActive", counterparty.IsActive);

        var mainAddress = GetMainAddress(counterparty);

        Add(parameters, "Counterparty_Address_Main", FormatAddress(mainAddress));
        Add(parameters, "Counterparty_Address_Legal", FormatAddress(counterparty.GetAddress(AddressMainType.Legal)));
        Add(parameters, "Counterparty_Address_Actual", FormatAddress(counterparty.GetAddress(AddressMainType.Actual)));
        Add(parameters, "Counterparty_Address_Postal", FormatAddress(counterparty.GetAddress(AddressMainType.Postal)));

        Add(parameters, "to_zip", mainAddress?.Zip);
        Add(parameters, "to_str1", mainAddress?.Line1);
        Add(parameters, "to_str2", mainAddress?.Line2);
        Add(parameters, "to_str3", mainAddress?.Line3);
        Add(parameters, "to_str4", mainAddress?.Line4);
        Add(parameters, "to_str5", mainAddress?.Line5);
        Add(parameters, "to_str6", mainAddress?.Line6);

        Add(parameters, "copy_count", 1);
        Add(parameters, "is_vip", counterparty.IsVip);
        Add(parameters, "note", counterparty.SpecialNotes);

        if (sender is not null)
        {
            var senderAddress = GetMainAddress(sender);

            Add(parameters, "from_zip", senderAddress?.Zip);
            Add(parameters, "from_str1", senderAddress?.Line1);
            Add(parameters, "from_str2", senderAddress?.Line2);
            Add(parameters, "from_str3", senderAddress?.Line3);
            Add(parameters, "from_str4", senderAddress?.Line4);
            Add(parameters, "from_str5", senderAddress?.Line5);
            Add(parameters, "from_str6", senderAddress?.Line6);
        }

        if (customValues is not null)
        {
            foreach (var pair in customValues)
            {
                Add(parameters, BuildCustomMarkerName(pair.Key), pair.Value);
            }
        }

        return parameters;
    }

    /// <summary>
    /// Формирует имя маркера пользовательского поля контрагента
    /// (ТЗ §3.1.9: тег вида <c>[Custom_имя]</c>).
    /// </summary>
    /// <param name="fieldName">Имя пользовательского поля.</param>
    /// <returns>Имя маркера вида <c>Custom_имя</c>.</returns>
    public static string BuildCustomMarkerName(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        return FieldDictionary.CustomTagPrefix + fieldName.Trim();
    }

    /// <summary>
    /// Возвращает основной адресный срез контрагента; при отсутствии основного
    /// среза используется почтовый.
    /// </summary>
    /// <param name="counterparty">Карточка контрагента.</param>
    /// <returns>Адрес либо <see langword="null"/>, если адрес не заведён.</returns>
    private static Address? GetMainAddress(Counterparty counterparty)
        => counterparty.GetAddress(counterparty.AddressMainType) ?? counterparty.GetAddress(AddressMainType.Postal);

    /// <summary>
    /// Формирует текстовое представление адреса: индекс и непустые строки.
    /// </summary>
    /// <param name="address">Адрес.</param>
    /// <returns>Текст адреса либо пустая строка.</returns>
    private static string FormatAddress(Address? address)
    {
        if (address is null || address.IsEmpty)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(address.Zip))
        {
            parts.Add(address.Zip);
        }

        parts.AddRange(address.Lines.Where(line => !string.IsNullOrWhiteSpace(line)));

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Добавляет значение в набор параметров, если значение задано.
    /// </summary>
    /// <param name="parameters">Набор параметров.</param>
    /// <param name="name">Имя параметра.</param>
    /// <param name="value">Значение параметра.</param>
    private static void Add(Dictionary<string, object> parameters, string name, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        parameters[name] = value;
    }

    /// <summary>
    /// Ищет значение параметра без учёта регистра имени.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="parameters">Набор параметров.</param>
    /// <param name="value">Найденное значение.</param>
    /// <returns><see langword="true"/>, если значение найдено.</returns>
    private static bool TryGetRawValue(
        string name,
        IReadOnlyDictionary<string, object> parameters,
        out object? value)
    {
        if (parameters.TryGetValue(name, out value))
        {
            return true;
        }

        foreach (var pair in parameters)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Регистрирует маркер и его синонимы в справочнике разрешения имён.
    /// </summary>
    /// <param name="aliases">Справочник «синоним → каноническое имя».</param>
    /// <param name="canonical">Каноническое имя маркера.</param>
    /// <param name="aliasList">Синонимы имени маркера.</param>
    private static void Register(
        Dictionary<string, string> aliases,
        string canonical,
        IEnumerable<string> aliasList)
    {
        aliases[canonical] = canonical;

        foreach (var alias in aliasList)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                aliases[alias] = canonical;
            }
        }
    }

    /// <summary>
    /// Формирует синонимы имени маркера по описанию поля словаря.
    /// </summary>
    /// <param name="field">Описание поля словаря полей.</param>
    /// <returns>Перечень синонимов.</returns>
    private static IReadOnlyList<string> BuildFieldAliases(FieldDescriptor field)
    {
        var aliases = new List<string> { field.ColumnName, ToPascalCase(field.ColumnName) };

        var tag = field.TemplateTag.Trim('[', ']');

        if (tag.Length > 0)
        {
            aliases.Add(tag);
        }

        aliases.AddRange(field.ImportAliases);

        return aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Приводит имя колонки к виду <c>Слово_Слово</c> (<c>to_zip</c> → <c>To_Zip</c>).
    /// </summary>
    /// <param name="name">Имя колонки.</param>
    /// <returns>Имя в смешанном регистре.</returns>
    private static string ToPascalCase(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var part in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(char.ToUpperInvariant(part[0]));
            builder.Append(part[1..]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Формирует перечень маркеров реквизитов карточки контрагента.
    /// </summary>
    /// <returns>Перечень маркеров карточки и адресных срезов.</returns>
    private static IReadOnlyList<DocumentMarker> BuildCounterpartyMarkers()
    {
        var markers = new List<DocumentMarker>
        {
            new("Counterparty_Id", "Внутренний идентификатор записи", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Id")),
            new("Counterparty_Type", "Тип контрагента: юридическое или физическое лицо", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Type")),
            new("Counterparty_Name", "Полное наименование организации или ФИО", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Name")),
            new("Counterparty_ShortName", "Сокращённое наименование", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_ShortName")),
            new("Counterparty_Inn", "ИНН или УНП", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Inn")),
            new("Counterparty_Kpp", "КПП юридического лица", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Kpp")),
            new("Counterparty_Ogrn", "ОГРН или ОГРНИП", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Ogrn")),
            new("Counterparty_Okfs", "ОКФС", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Okfs")),
            new("Counterparty_Okpo", "ОКПО", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Okpo")),
            new("Counterparty_Okved", "ОКВЭД", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Okved")),
            new("Counterparty_Bik", "БИК банка", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Bik")),
            new("Counterparty_Ks", "Корреспондентский счёт банка", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Ks")),
            new("Counterparty_Rs", "Расчётный счёт контрагента", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Rs")),
            new("Counterparty_Rkc", "Наименование расчётно-кассового центра", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Rkc")),
            new("Counterparty_BankName", "Наименование обслуживающего банка", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_BankName")),
            new("Counterparty_PassportSeries", "Серия паспорта", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_PassportSeries")),
            new("Counterparty_PassportNumber", "Номер паспорта", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_PassportNumber")),
            new("Counterparty_PassportIssuedBy", "Кем выдан паспорт", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_PassportIssuedBy")),
            new("Counterparty_PassportIssuedDate", "Дата выдачи паспорта", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_PassportIssuedDate")),
            new("Counterparty_BirthDate", "Дата рождения", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_BirthDate")),
            new("Counterparty_BirthPlace", "Место рождения", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_BirthPlace")),
            new("Counterparty_Position", "Должность руководителя или контактного лица", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Position")),
            new("Counterparty_DirectorFio", "ФИО первого лица", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_DirectorFio")),
            new("Counterparty_Email", "Адрес электронной почты", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Email")),
            new("Counterparty_Phone", "Телефон", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_Phone")),
            new("Counterparty_SpecialNotes", "Особые технологические отметки", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_SpecialNotes")),
            new("Counterparty_GroupId", "Идентификатор группы адресной книги", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_GroupId")),
            new("Counterparty_IsVip", "Признак «Избранный адрес»", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_IsVip")),
            new("Counterparty_IsActive", "Признак активности записи", CounterpartyGroup, BuildCounterpartyAliases("Counterparty_IsActive")),
            new("Counterparty_Address_Main", "Адрес основного среза", AddressGroup, BuildCounterpartyAliases("Counterparty_Address_Main")),
            new("Counterparty_Address_Legal", "Юридический адрес", AddressGroup, BuildCounterpartyAliases("Counterparty_Address_Legal")),
            new("Counterparty_Address_Actual", "Фактический адрес", AddressGroup, BuildCounterpartyAliases("Counterparty_Address_Actual")),
            new("Counterparty_Address_Postal", "Почтовый адрес", AddressGroup, BuildCounterpartyAliases("Counterparty_Address_Postal")),
        };

        return markers;
    }

    /// <summary>
    /// Формирует синонимы имени маркера карточки: имя без подчёркивания и
    /// имя в нижнем регистре.
    /// </summary>
    /// <param name="name">Каноническое имя маркера.</param>
    /// <returns>Перечень синонимов.</returns>
    private static IReadOnlyList<string> BuildCounterpartyAliases(string name)
    {
        var aliases = new List<string>
        {
            name,
            name.Replace("_", string.Empty, StringComparison.Ordinal),
            name.ToLowerInvariant(),
        };

        return aliases;
    }
}
