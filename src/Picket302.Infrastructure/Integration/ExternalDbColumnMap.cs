using System.Text;

namespace Picket302.Infrastructure.Integration;

/// <summary>
/// Поле черновика контрагента, которому сопоставляется столбец внешней таблицы
/// (ТЗ §8.5, функция 76: «читает таблицу контрагентов по настраиваемому
/// соответствию колонок»).
/// </summary>
public enum ExternalDbField
{
    /// <summary>Наименование контрагента.</summary>
    Name = 0,

    /// <summary>Сокращённое наименование контрагента.</summary>
    ShortName = 1,

    /// <summary>ИНН контрагента.</summary>
    Inn = 2,

    /// <summary>КПП контрагента.</summary>
    Kpp = 3,

    /// <summary>ОГРН (ОГРНИП) контрагента.</summary>
    Ogrn = 4,

    /// <summary>Телефон контрагента.</summary>
    Phone = 5,

    /// <summary>Адрес электронной почты контрагента.</summary>
    Email = 6,

    /// <summary>Примечание к контрагенту.</summary>
    Note = 7,

    /// <summary>Почтовый индекс (строка 1–6 адреса).</summary>
    PostalZip = 8,

    /// <summary>Почтовый адрес (строки 1–6 адреса).</summary>
    PostalAddress = 9,

    /// <summary>Почтовый индекс юридического адреса.</summary>
    LegalZip = 10,

    /// <summary>Юридический адрес (строки 1–6 адреса).</summary>
    LegalAddress = 11,

    /// <summary>Почтовый индекс фактического адреса.</summary>
    ActualZip = 12,

    /// <summary>Фактический адрес (строки 1–6 адреса).</summary>
    ActualAddress = 13,
}

/// <summary>
/// Настраиваемое соответствие между столбцами внешней таблицы и полями черновика
/// контрагента (ТЗ §8.5, функция 76: «Импорт контрагентов из отдельной внешней БД»).
/// </summary>
/// <remarks>
/// <para>
/// Соответствие формируется автоматически по именам столбцов
/// (<see cref="CreateAuto"/>) и может быть уточнено оператором вручную
/// (<see cref="Set"/>), например: «Наименование» → <see cref="ExternalDbField.Name"/>,
/// «ИНН» → <see cref="ExternalDbField.Inn"/>, «КПП» → <see cref="ExternalDbField.Kpp"/>,
/// «Телефон» → <see cref="ExternalDbField.Phone"/>, «E-mail»/«Эл. почта» →
/// <see cref="ExternalDbField.Email"/>, «Адрес» → <see cref="ExternalDbField.PostalAddress"/>,
/// «Юридический адрес» → <see cref="ExternalDbField.LegalAddress"/>,
/// «Фактический адрес» → <see cref="ExternalDbField.ActualAddress"/>,
/// «Примечание» → <see cref="ExternalDbField.Note"/>.
/// </para>
/// <para>
/// Одному столбцу внешней таблицы соответствует не более одного поля черновика,
/// поэтому при автоматическом подборе столбцы не могут быть использованы дважды.
/// </para>
/// </remarks>
public sealed class ExternalDbColumnMap
{
    private readonly Dictionary<ExternalDbField, string> _columns = [];

    /// <summary>
    /// Фразы-признаки полей в порядке убывания специфичности: сначала проверяются
    /// более точные признаки, затем более общие (например, «Юридический адрес»
    /// проверяется раньше, чем «Адрес»).
    /// </summary>
    private static readonly (ExternalDbField Field, string[] Phrases)[] _phraseTable =
    [
        (ExternalDbField.LegalAddress,
        [
            "юридический адрес", "юрадрес", "адрес юридический", "адрес регистрации",
            "зарегистрированный адрес", "юридический адрес контрагента", "legaladdress", "legal address",
        ]),
        (ExternalDbField.ActualAddress,
        [
            "фактический адрес", "фактадрес", "адрес фактический", "адрес деятельности",
            "адрес исполнения", "actualaddress", "actual address",
        ]),
        (ExternalDbField.PostalAddress,
        [
            "почтовый адрес", "адрес получателя", "адрес контрагента", "адрес организации",
            "адрес местонахождения", "местонахождение", "адрес", "address",
        ]),
        (ExternalDbField.ShortName,
        [
            "сокращенное наименование", "сокращенное название", "сокращенно",
            "краткое наименование", "краткое название", "сокращение", "shortname", "short name",
        ]),
        (ExternalDbField.Name,
        [
            "полное наименование", "наименование", "название", "контрагент",
            "организация", "компания", "фирма", "name",
        ]),
        (ExternalDbField.Inn, ["инн", "inn", "унп", "unp"]),
        (ExternalDbField.Kpp, ["кпп", "kpp"]),
        (ExternalDbField.Ogrn, ["огрн", "огрнип", "ogrn"]),
        (ExternalDbField.Phone, ["телефон", "тел", "phone"]),
        (ExternalDbField.Email, ["e-mail", "e mail", "email", "эл почта", "электронная почта", "почта"]),
        (ExternalDbField.Note, ["примечание", "комментарий", "дополнительная информация", "note", "remark"]),
        (ExternalDbField.LegalZip,
        [
            "индекс юридического адреса", "юридический индекс", "индекс юрадреса",
        ]),
        (ExternalDbField.ActualZip,
        [
            "индекс фактического адреса", "фактический индекс", "индекс фактадреса",
        ]),
        (ExternalDbField.PostalZip,
        [
            "почтовый индекс", "индекс получателя", "индекс контрагента",
            "индекс организации", "индекс", "zip",
        ]),
    ];

    /// <summary>
    /// Создаёт пустое соответствие (все столбцы назначаются оператором вручную).
    /// </summary>
    /// <returns>Пустое соответствие колонок.</returns>
    public static ExternalDbColumnMap CreateEmpty() => new();

    /// <summary>
    /// Создаёт соответствие, подобранное автоматически по именам столбцов внешней
    /// таблицы (ТЗ §8.5, функция 76: «настраиваемое соответствие колонок»).
    /// </summary>
    /// <param name="columns">Имена столбцов внешней таблицы.</param>
    /// <returns>
    /// Соответствие, в котором каждому распознанному полю назначен столбец;
    /// нераспознанные столбцы остаются неназначенными.
    /// </returns>
    public static ExternalDbColumnMap CreateAuto(IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var map = new ExternalDbColumnMap();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Первый проход — точное совпадение имени столбца с признаком поля.
        foreach (var (field, phrases) in _phraseTable)
        {
            foreach (var phrase in phrases)
            {
                var index = FindColumn(columns, used, name => string.Equals(name, phrase, StringComparison.Ordinal));
                if (index < 0)
                {
                    continue;
                }

                map.Set(field, columns[index]);
                used.Add(columns[index]);
                break;
            }
        }

        // Второй проход — совпадение по вхождению признака в имени столбца.
        foreach (var (field, phrases) in _phraseTable)
        {
            if (map._columns.ContainsKey(field))
            {
                continue;
            }

            foreach (var phrase in phrases)
            {
                var index = FindColumn(columns, used, name => name.Contains(phrase, StringComparison.Ordinal));
                if (index < 0)
                {
                    continue;
                }

                map.Set(field, columns[index]);
                used.Add(columns[index]);
                break;
            }
        }

        return map;
    }

    /// <summary>
    /// Определяет поле черновика, которому по умолчанию соответствует столбец
    /// внешней таблицы (используется мастером сопоставления колонок).
    /// </summary>
    /// <param name="columnName">Имя столбца внешней таблицы.</param>
    /// <param name="field">Распознанное поле черновика.</param>
    /// <returns>
    /// <see langword="true"/>, если поле распознано; иначе —
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryGuessField(string? columnName, out ExternalDbField field)
    {
        field = ExternalDbField.Name;

        if (string.IsNullOrWhiteSpace(columnName))
        {
            return false;
        }

        var normalized = Normalize(columnName);

        foreach (var (candidate, phrases) in _phraseTable)
        {
            foreach (var phrase in phrases)
            {
                if (normalized.Contains(phrase, StringComparison.Ordinal))
                {
                    field = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Назначает столбец внешней таблицы полю черновика (ручное сопоставление).
    /// </summary>
    /// <param name="field">Поле черновика.</param>
    /// <param name="column">
    /// Имя столбца внешней таблицы; пустое значение снимает назначение.
    /// </param>
    /// <returns>Текущее соответствие (для цепочки вызовов).</returns>
    public ExternalDbColumnMap Set(ExternalDbField field, string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            _columns.Remove(field);
            return this;
        }

        _columns[field] = column.Trim();
        return this;
    }

    /// <summary>
    /// Возвращает назначенный столбец внешней таблицы для поля черновика.
    /// </summary>
    /// <param name="field">Поле черновика.</param>
    /// <returns>Имя столбца либо <see langword="null"/>, если столбец не назначен.</returns>
    public string? GetColumn(ExternalDbField field) =>
        _columns.TryGetValue(field, out var column) ? column : null;

    /// <summary>Признак того, что соответствие пустое.</summary>
    public bool IsEmpty => _columns.Count == 0;

    /// <summary>Все выполненные назначения «поле → столбец».</summary>
    public IReadOnlyDictionary<ExternalDbField, string> Mappings => _columns;

    /// <summary>Столбец с наименованием контрагента.</summary>
    public string? NameColumn
    {
        get => GetColumn(ExternalDbField.Name);
        set => Set(ExternalDbField.Name, value);
    }

    /// <summary>Столбец с сокращённым наименованием контрагента.</summary>
    public string? ShortNameColumn
    {
        get => GetColumn(ExternalDbField.ShortName);
        set => Set(ExternalDbField.ShortName, value);
    }

    /// <summary>Столбец с ИНН контрагента.</summary>
    public string? InnColumn
    {
        get => GetColumn(ExternalDbField.Inn);
        set => Set(ExternalDbField.Inn, value);
    }

    /// <summary>Столбец с КПП контрагента.</summary>
    public string? KppColumn
    {
        get => GetColumn(ExternalDbField.Kpp);
        set => Set(ExternalDbField.Kpp, value);
    }

    /// <summary>Столбец с ОГРН контрагента.</summary>
    public string? OgrnColumn
    {
        get => GetColumn(ExternalDbField.Ogrn);
        set => Set(ExternalDbField.Ogrn, value);
    }

    /// <summary>Столбец с телефоном контрагента.</summary>
    public string? PhoneColumn
    {
        get => GetColumn(ExternalDbField.Phone);
        set => Set(ExternalDbField.Phone, value);
    }

    /// <summary>Столбец с адресом электронной почты контрагента.</summary>
    public string? EmailColumn
    {
        get => GetColumn(ExternalDbField.Email);
        set => Set(ExternalDbField.Email, value);
    }

    /// <summary>Столбец с примечанием к контрагенту.</summary>
    public string? NoteColumn
    {
        get => GetColumn(ExternalDbField.Note);
        set => Set(ExternalDbField.Note, value);
    }

    /// <summary>Столбец с почтовым индексом.</summary>
    public string? PostalZipColumn
    {
        get => GetColumn(ExternalDbField.PostalZip);
        set => Set(ExternalDbField.PostalZip, value);
    }

    /// <summary>Столбец с почтовым адресом.</summary>
    public string? PostalAddressColumn
    {
        get => GetColumn(ExternalDbField.PostalAddress);
        set => Set(ExternalDbField.PostalAddress, value);
    }

    /// <summary>Столбец с почтовым индексом юридического адреса.</summary>
    public string? LegalZipColumn
    {
        get => GetColumn(ExternalDbField.LegalZip);
        set => Set(ExternalDbField.LegalZip, value);
    }

    /// <summary>Столбец с юридическим адресом.</summary>
    public string? LegalAddressColumn
    {
        get => GetColumn(ExternalDbField.LegalAddress);
        set => Set(ExternalDbField.LegalAddress, value);
    }

    /// <summary>Столбец с почтовым индексом фактического адреса.</summary>
    public string? ActualZipColumn
    {
        get => GetColumn(ExternalDbField.ActualZip);
        set => Set(ExternalDbField.ActualZip, value);
    }

    /// <summary>Столбец с фактическим адресом.</summary>
    public string? ActualAddressColumn
    {
        get => GetColumn(ExternalDbField.ActualAddress);
        set => Set(ExternalDbField.ActualAddress, value);
    }

    /// <summary>
    /// Нормализует имя столбца: нижний регистр, замена «ё» на «е», удаление точек,
    /// скобок и кавычек, замена подчёркиваний на пробелы.
    /// </summary>
    private static string Normalize(string columnName)
    {
        var builder = new StringBuilder(columnName.Length);
        var lastWasSpace = false;

        foreach (var rawSymbol in columnName.Trim().ToLowerInvariant())
        {
            var symbol = rawSymbol == 'ё' ? 'е' : rawSymbol;

            if (char.IsLetterOrDigit(symbol) || symbol == '-')
            {
                builder.Append(symbol);
                lastWasSpace = false;
                continue;
            }

            if (!lastWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Ищет первый не использованный столбец, удовлетворяющий условию отбора.
    /// </summary>
    private static int FindColumn(
        IReadOnlyList<string> columns,
        HashSet<string> used,
        Func<string, bool> predicate)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            if (used.Contains(column))
            {
                continue;
            }

            if (predicate(Normalize(column)))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Возвращает нормализованное имя столбца для диагностических сообщений.
    /// </summary>
    /// <param name="columnName">Имя столбца.</param>
    /// <returns>Нормализованное имя столбца.</returns>
    public override string ToString() =>
        string.Join(", ", _columns.Select(pair => $"{pair.Key}={pair.Value}"));
}
