namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Источник данных исходной базы предшественника — абстракция чтения таблиц
/// (ТЗ §8.9, функции 117 «Импорт базы из исходной MySQL-схемы» и 118
/// «Импорт базы из исходной Access-схемы»).
/// </summary>
/// <remarks>
/// <para>
/// Абстракция позволяет проверять перенос без реального сервера: конкретные
/// реализации — <see cref="MySqlTableSource"/> и
/// <see cref="AccessOdbcTableSource"/> — являются тонкими адаптерами над
/// драйверами, а вся логика сопоставления работает с нейтральной моделью
/// таблиц и строк.
/// </para>
/// <para>
/// Отсутствующая таблица не является ошибкой источника: она просто не попадает
/// в список таблиц, и перенос её пропускает с предупреждением в отчёте.
/// </para>
/// </remarks>
public interface ILegacyTableSource
{
    /// <summary>
    /// Пытается открыть источник данных.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// <see langword="true"/>, если источник доступен; <see langword="false"/> —
    /// если подключение не удалось (подробности — в тексте ошибки источника).
    /// </returns>
    Task<bool> TryOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает список доступных таблиц источника.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список таблиц; таблицы, отсутствующие в источнике, в списке нет.</returns>
    Task<IReadOnlyList<LegacyTable>> GetTablesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает строки указанной таблицы.
    /// </summary>
    /// <param name="table">Имя таблицы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Строки таблицы; для отсутствующей таблицы — пустой список.</returns>
    Task<IReadOnlyList<LegacyRow>> GetRowsAsync(string table, CancellationToken cancellationToken = default);
}

/// <summary>
/// Таблица исходной базы предшественника: имя, список столбцов и число строк
/// (ТЗ §8.9, функции 117 и 118).
/// </summary>
public sealed class LegacyTable
{
    /// <summary>Создаёт описание таблицы.</summary>
    /// <param name="name">Имя таблицы.</param>
    /// <param name="columns">Список столбцов таблицы.</param>
    /// <param name="rowCount">Примерное число строк (необязательно).</param>
    public LegacyTable(string name, IReadOnlyList<string> columns, long rowCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(columns);

        Name = name;
        Columns = columns;
        RowCount = rowCount;
    }

    /// <summary>Имя таблицы.</summary>
    public string Name { get; }

    /// <summary>Список столбцов таблицы.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>Примерное число строк таблицы.</summary>
    public long RowCount { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Columns.Count} столбцов)";
}

/// <summary>
/// Строка исходной базы предшественника: значения столбцов в виде строк
/// (ТЗ §8.9, функции 117 и 118).
/// </summary>
/// <remarks>
/// <para>
/// Нейтральная модель хранит значения текстом: так одна и та же модель
/// описывает данные, прочитанные драйвером MySQL, и данные, прочитанные
/// драйвером ODBC. Двоичные значения (логотипы, изображения кодов) передаются
/// в виде текста Base64 и читаются через <see cref="GetBytes"/>.
/// </para>
/// <para>
/// Имена столбцов сопоставляются без учёта регистра.
/// </para>
/// </remarks>
public sealed class LegacyRow
{
    private readonly Dictionary<string, string?> _значения;

    /// <summary>Создаёт строку исходной таблицы.</summary>
    /// <param name="values">Значения столбцов: имя столбца — значение.</param>
    public LegacyRow(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _значения = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (столбец, значение) in values)
        {
            _значения[столбец] = значение;
        }
    }

    /// <summary>Значения столбцов строки.</summary>
    public IReadOnlyDictionary<string, string?> Values => _значения;

    /// <summary>
    /// Возвращает значение столбца без обрезки пробелов.
    /// </summary>
    /// <param name="column">Имя столбца.</param>
    /// <returns>Значение столбца либо <see langword="null"/>.</returns>
    public string? Get(string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        return _значения.TryGetValue(column, out var значение) ? значение : null;
    }

    /// <summary>
    /// Возвращает обрезанное значение столбца; пустое значение приводится
    /// к <see langword="null"/>.
    /// </summary>
    /// <param name="column">Имя столбца.</param>
    /// <returns>Значение столбца либо <see langword="null"/>.</returns>
    public string? GetString(string column)
    {
        var значение = Get(column);

        return string.IsNullOrWhiteSpace(значение) ? null : значение!.Trim();
    }

    /// <summary>
    /// Возвращает значение столбца как логический признак.
    /// </summary>
    /// <param name="column">Имя столбца.</param>
    /// <returns><see langword="true"/>, если значение истинно.</returns>
    public bool GetBool(string column)
    {
        var значение = GetString(column);

        if (значение is null)
        {
            return false;
        }

        return значение switch
        {
            "1" => true,
            "0" => false,
            "true" => true,
            "false" => false,
            "True" => true,
            "False" => false,
            "да" => true,
            "нет" => false,
            _ => decimal.TryParse(
                значение.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var число) && число != 0,
        };
    }

    /// <summary>
    /// Возвращает значение столбца как целое число.
    /// </summary>
    /// <param name="column">Имя столбца.</param>
    /// <returns>Число либо <see langword="null"/>, если значение не является числом.</returns>
    public int? GetInt(string column)
    {
        var значение = GetString(column);

        if (значение is null)
        {
            return null;
        }

        if (int.TryParse(
                значение,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var число))
        {
            return число;
        }

        return decimal.TryParse(
            значение.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var дробное) ? (int)decimal.Truncate(дробное) : null;
    }

    /// <summary>
    /// Возвращает значение столбца как десятичное число.
    /// </summary>
    /// <param name="column">Имя столбца.</param>
    /// <returns>Число либо <see langword="null"/>, если значение не является числом.</returns>
    public decimal? GetDecimal(string column)
    {
        var значение = GetString(column);

        if (значение is null)
        {
            return null;
        }

        return decimal.TryParse(
            значение.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var число) ? число : null;
    }

    /// <summary>
    /// Возвращает значение столбца как дату.
    /// </summary>
    /// <param name="column">Имя столбца.</param>
    /// <returns>Дата либо <see langword="null"/>, если значение не является датой.</returns>
    public DateTime? GetDateTime(string column)
    {
        var значение = GetString(column);

        if (значение is null)
        {
            return null;
        }

        if (DateTime.TryParse(
                значение,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var дата))
        {
            return дата;
        }

        return DateTime.TryParse(
            значение,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces,
            out дата) ? дата : null;
    }

    /// <summary>
    /// Возвращает значение столбца как двоичные данные.
    /// </summary>
    /// <param name="column">Имя столбца.</param>
    /// <returns>
    /// Байты значения: для данных, переданных в виде Base64, — декодированные
    /// байты; для текста — его байты в UTF-8.
    /// </returns>
    public byte[] GetBytes(string column)
    {
        var значение = GetString(column);

        if (значение is null)
        {
            return Array.Empty<byte>();
        }

        if (значение.Length > 0 && значение.Length % 4 == 0)
        {
            var буфер = new byte[значение.Length / 4];

            if (Convert.TryFromBase64String(значение, буфер, out var скопировано))
            {
                if (скопировано == буфер.Length)
                {
                    return буфер;
                }

                var точный = new byte[скопировано];
                Array.Copy(буфер, точный, скопировано);

                return точный;
            }
        }

        return System.Text.Encoding.UTF8.GetBytes(значение);
    }
}

/// <summary>
/// Набор таблиц исходной базы, прочитанных в память (ТЗ §8.9, функции 117 и 118).
/// </summary>
/// <remarks>
/// Набор используется картографом переноса: он позволяет работать с исходными
/// данными без обращения к источнику и без реального сервера — проверка переноса
/// выполняется на наборе в памяти.
/// </remarks>
public sealed class LegacyDataSet
{
    private readonly Dictionary<string, IReadOnlyList<LegacyRow>> _строки;

    /// <summary>Создаёт набор таблиц исходной базы.</summary>
    /// <param name="tables">Список таблиц.</param>
    /// <param name="rows">Строки таблиц: имя таблицы — список строк.</param>
    public LegacyDataSet(
        IEnumerable<LegacyTable> tables,
        IDictionary<string, IReadOnlyList<LegacyRow>> rows)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(rows);

        Tables = tables.ToArray();
        _строки = new Dictionary<string, IReadOnlyList<LegacyRow>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (таблица, строки) in rows)
        {
            _строки[таблица] = строки;
        }
    }

    /// <summary>Список таблиц набора.</summary>
    public IReadOnlyList<LegacyTable> Tables { get; }

    /// <summary>
    /// Проверяет наличие таблицы в наборе.
    /// </summary>
    /// <param name="table">Имя таблицы.</param>
    /// <returns><see langword="true"/>, если таблица присутствует.</returns>
    public bool HasTable(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        return _строки.ContainsKey(table);
    }

    /// <summary>
    /// Возвращает строки таблицы; для отсутствующей таблицы — пустой список.
    /// </summary>
    /// <param name="table">Имя таблицы.</param>
    /// <returns>Строки таблицы.</returns>
    public IReadOnlyList<LegacyRow> Rows(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        return _строки.TryGetValue(table, out var строки) ? строки : Array.Empty<LegacyRow>();
    }

    /// <summary>
    /// Читает все доступные таблицы источника в набор данных.
    /// </summary>
    /// <param name="source">Источник данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Набор прочитанных таблиц.</returns>
    public static async Task<LegacyDataSet> ЗагрузитьAsync(
        ILegacyTableSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var таблицы = await source.GetTablesAsync(cancellationToken).ConfigureAwait(false);
        var строки = new Dictionary<string, IReadOnlyList<LegacyRow>>(StringComparer.OrdinalIgnoreCase);

        foreach (var таблица in таблицы)
        {
            cancellationToken.ThrowIfCancellationRequested();

            строки[таблица.Name] = await source.GetRowsAsync(таблица.Name, cancellationToken).ConfigureAwait(false);
        }

        return new LegacyDataSet(таблицы, строки);
    }
}

/// <summary>
/// Имена таблиц исходных схем предшественника (ТЗ §8.9, функции 117 и 118;
/// EVIDENCE/schema_reference.sql, EVIDENCE/database_schema_notes.txt).
/// </summary>
public static class LegacyTableNames
{
    /// <summary>Отправители исходной MySQL-схемы (8 таблиц).</summary>
    public const string MySqlSenders = "addrfrom";

    /// <summary>Получатели исходной MySQL-схемы.</summary>
    public const string MySqlRecipients = "addrto";

    /// <summary>История печати исходной MySQL-схемы.</summary>
    public const string MySqlPrintHistory = "addrto_history";

    /// <summary>Кэш QR-кодов исходной MySQL-схемы.</summary>
    public const string MySqlQrCache = "addrblob";

    /// <summary>Группы исходной MySQL-схемы.</summary>
    public const string MySqlGroups = "groups";

    /// <summary>Описи вложений исходной MySQL-схемы.</summary>
    public const string MySqlInventory = "inventory";

    /// <summary>Позиции описей исходной MySQL-схемы.</summary>
    public const string MySqlInventoryDetail = "inventory_detail";

    /// <summary>Параметры исходной MySQL-схемы.</summary>
    public const string MySqlParams = "params";

    /// <summary>Адреса исходной Access-схемы (14 таблиц).</summary>
    public const string AccessAddresses = "Address";

    /// <summary>Бланки исходной Access-схемы.</summary>
    public const string AccessBlanks = "Blank";

    /// <summary>Пользовательские поля исходной Access-схемы.</summary>
    public const string AccessCustomFields = "CustomFields";

    /// <summary>Служебная таблица версии схемы исходной Access-схемы.</summary>
    public const string AccessDbScheme = "DB_scheme";

    /// <summary>Рассылки исходной Access-схемы.</summary>
    public const string AccessDistributions = "Distribution";

    /// <summary>Документы исходной Access-схемы.</summary>
    public const string AccessDocuments = "Document";

    /// <summary>Связи документов и движений исходной Access-схемы.</summary>
    public const string AccessDocumentMovements = "DocumentMovement";

    /// <summary>Справочник типов документов исходной Access-схемы.</summary>
    public const string AccessDocumentTypes = "DocumentType";

    /// <summary>Контрагенты исходной Access-схемы (53 столбца).</summary>
    public const string AccessCounterparties = "EconomicMember";

    /// <summary>Входящие исходной Access-схемы.</summary>
    public const string AccessIncoming = "Incoming";

    /// <summary>Письма исходной Access-схемы (39 столбцов).</summary>
    public const string AccessLetters = "Letter";

    /// <summary>Движения документов исходной Access-схемы.</summary>
    public const string AccessMovements = "Movement";

    /// <summary>Почтовые реестры исходной Access-схемы.</summary>
    public const string AccessPostRegisters = "PostRegister";

    /// <summary>Настройки исходной Access-схемы (39 столбцов).</summary>
    public const string AccessSettings = "Settings";

    /// <summary>Все таблицы исходной MySQL-схемы.</summary>
    public static IReadOnlyList<string> MySql { get; } =
    [
        MySqlSenders,
        MySqlRecipients,
        MySqlPrintHistory,
        MySqlQrCache,
        MySqlGroups,
        MySqlInventory,
        MySqlInventoryDetail,
        MySqlParams,
    ];

    /// <summary>Все таблицы исходной Access-схемы.</summary>
    public static IReadOnlyList<string> Access { get; } =
    [
        AccessAddresses,
        AccessBlanks,
        AccessCustomFields,
        AccessDbScheme,
        AccessDistributions,
        AccessDocuments,
        AccessDocumentMovements,
        AccessDocumentTypes,
        AccessCounterparties,
        AccessIncoming,
        AccessLetters,
        AccessMovements,
        AccessPostRegisters,
        AccessSettings,
    ];
}
