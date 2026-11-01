using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;

namespace Picket302.Infrastructure.Integration;

/// <summary>
/// Результат импорта контрагентов из внешней базы данных (ТЗ §8.5, функция 76:
/// «Импорт контрагентов из отдельной внешней БД»).
/// </summary>
/// <remarks>
/// Контракт повторяет <c>Picket302.Application.Import.ImportBatchResult</c> один в
/// один: слой инфраструктуры не ссылается на слой приложения (ТЗ §2.1), поэтому
/// тип пакета и предупреждений объявлен здесь заново. Пакет передаётся в общий
/// конвейер проверки и слияния импорта без дополнительной обработки.
/// </remarks>
public sealed class ImportBatchResult
{
    /// <summary>
    /// Создаёт результат импорта.
    /// </summary>
    /// <param name="batch">Прочитанный пакет импорта.</param>
    /// <param name="warnings">Предупреждения, возникшие при чтении внешней базы.</param>
    public ImportBatchResult(ImportBatch batch, IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(batch);

        Batch = batch;
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>Прочитанный пакет импорта.</summary>
    public ImportBatch Batch { get; }

    /// <summary>Предупреждения, возникшие при чтении внешней базы.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Записи пакета импорта.</summary>
    public IReadOnlyList<ImportRecord> Records => Batch.Records;

    /// <summary>Количество прочитанных записей.</summary>
    public int RecordCount => Batch.Records.Count;

    /// <summary>Все предупреждения пакета: общие и построчные.</summary>
    public IReadOnlyList<string> AllWarnings =>
        Batch.Records.SelectMany(record => record.Warnings).Concat(Warnings).ToList();
}

/// <summary>
/// Ошибка импорта из внешней базы данных (ТЗ §8.5, функция 76).
/// </summary>
public class ExternalDbException : Exception
{
    /// <summary>
    /// Создаёт исключение с текстом сообщения.
    /// </summary>
    /// <param name="message">Текст сообщения на русском языке.</param>
    public ExternalDbException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Создаёт исключение с текстом сообщения и исходной причиной.
    /// </summary>
    /// <param name="message">Текст сообщения на русском языке.</param>
    /// <param name="innerException">Исходная причина.</param>
    public ExternalDbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Не удалось подключиться к внешней базе данных (ТЗ §8.5, функция 76:
/// «Недоступность сервера → "Не удалось подключиться к внешней базе данных" с
/// детализацией исключения подключения»).
/// </summary>
public sealed class ExternalDbConnectionException : ExternalDbException
{
    /// <summary>
    /// Создаёт исключение о невозможности подключения.
    /// </summary>
    /// <param name="message">Текст сообщения на русском языке.</param>
    /// <param name="innerException">Исходное исключение подключения.</param>
    public ExternalDbConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Для выбранного вида внешней базы данных отсутствует драйвер (ТЗ §8.5, функция 76).
/// </summary>
public sealed class ExternalDbDriverException : ExternalDbException
{
    /// <summary>
    /// Создаёт исключение об отсутствующем драйвере.
    /// </summary>
    /// <param name="message">Текст сообщения на русском языке.</param>
    public ExternalDbDriverException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Импорт контрагентов из отдельной внешней реляционной базы данных
/// (ТЗ §8.5, функция 76: «Импорт контрагентов из отдельной внешней БД»).
/// </summary>
/// <remarks>
/// <para>
/// Импортер читает таблицу контрагентов из внешней базы по настраиваемому
/// соответствию колонок и формирует пакет импорта, который передаётся в общий
/// конвейер проверки и слияния (<c>ImportSession</c> / <c>ImportMergeService</c>).
/// </para>
/// <para>
/// Строка подключения вводится оператором, а пароль хранится в защищённом виде
/// (DPAPI, см. <see cref="DpapiSecretStore"/>). Ни строка подключения, ни пароль
/// не попадают в текст ошибок и не пишутся в журнал: перед выводом пароль
/// маскируется, а в сообщении остаётся только детализация исключения.
/// </para>
/// <para>
/// Драйверы подключаются без изменения списка пакетов решения: SQLite использует
/// <c>Microsoft.Data.Sqlite</c>, MySQL — <c>MySqlConnector</c> (поиск класса
/// фабрики по отражению), SQL Server — <c>Microsoft.Data.SqlClient</c> (также по
/// отражению). Если драйвер отсутствует, выдаётся понятная ошибка на русском
/// языке.
/// </para>
/// </remarks>
public sealed class ExternalDbImporter
{
    /// <summary>
    /// Сообщение о невозможности подключения к внешней базе данных
    /// (ТЗ §8.5, функция 76).
    /// </summary>
    public const string ConnectionFailedMessage = "Не удалось подключиться к внешней базе данных";

    /// <summary>
    /// Наименование источника данных в пакете импорта — «Внешняя БД»
    /// (ТЗ §8.5, функция 76: «источник "Внешняя БД"»).
    /// </summary>
    public const string SourceFormat = "Внешняя БД";

    /// <summary>
    /// Сообщение об отсутствии драйвера MySQL.
    /// </summary>
    public const string MySqlDriverMissingMessage =
        ConnectionFailedMessage + ": не найден драйвер MySQL (MySqlConnector).";

    /// <summary>
    /// Сообщение об отсутствии драйвера SQL Server.
    /// </summary>
    public const string SqlServerDriverMissingMessage =
        ConnectionFailedMessage + ": не найден драйвер SQL Server (Microsoft.Data.SqlClient).";

    private const int DefaultCommandTimeoutSeconds = 30;

    private const string MySqlFactoryTypeName = "MySqlConnector.MySqlClientFactory, MySqlConnector";
    private const string SqlServerFactoryTypeName =
        "Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient";

    private static readonly Regex _postalIndexPattern = new(@"(?<!\d)(\d{6})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex _passwordFragmentPattern =
        new(@"(?i)\b(password|pwd)\s*=\s*[^;\s]*", RegexOptions.Compiled);

    private static readonly string[] _counterpartiesTableMarkers =
    [
        "counterpart",
        "counterparty",
        "contragent",
        "контрагент",
        "клиент",
        "client",
        "партнер",
        "partner",
        "получател",
        "addrto",
    ];

    /// <summary>
    /// Импортирует контрагентов из внешней базы данных, выбирая таблицу и
    /// соответствие колонок автоматически (ТЗ §8.5, функция 76).
    /// </summary>
    /// <param name="connectionString">Строка подключения к внешней базе данных.</param>
    /// <param name="kind">Вид внешней базы данных.</param>
    /// <returns>
    /// Пакет импорта с прочитанными записями; источник записей —
    /// «Импорт Внешняя БД от &lt;дата&gt; из &lt;таблица&gt;».
    /// </returns>
    /// <exception cref="ExternalDbConnectionException">
    /// Подключение к внешней базе данных не удалось (сообщение содержит детализацию
    /// исключения подключения).
    /// </exception>
    /// <exception cref="ExternalDbDriverException">
    /// Для выбранного вида базы данных отсутствует драйвер.
    /// </exception>
    public ImportBatchResult ImportFromExternalDb(string connectionString, DbKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var tables = ListTablesAsync(connectionString, kind);
        if (tables.IsFailure)
        {
            throw new ExternalDbException(tables.Error!);
        }

        var tableNames = tables.Value!;
        if (tableNames.Count == 0)
        {
            throw new ExternalDbException("Внешняя база данных не содержит таблиц.");
        }

        var tableName = ChooseCounterpartiesTable(tableNames);
        var table = ReadTable(connectionString, kind, tableName);
        var map = ExternalDbColumnMap.CreateAuto(table.Columns);
        var batch = BuildBatch(table, map, tableName);

        return new ImportBatchResult(batch);
    }

    /// <summary>
    /// Импортирует контрагентов из выбранной таблицы внешней базы данных по
    /// заданному соответствию колонок (ТЗ §8.5, функция 76).
    /// </summary>
    /// <param name="connectionString">Строка подключения к внешней базе данных.</param>
    /// <param name="kind">Вид внешней базы данных.</param>
    /// <param name="tableName">Имя таблицы с контрагентами.</param>
    /// <param name="map">Соответствие колонок внешней таблицы и полей черновика.</param>
    /// <returns>
    /// Пакет импорта либо сообщение об ошибке подключения/чтения на русском языке.
    /// </returns>
    public Result<ImportBatch> ImportFromExternalDb(
        string connectionString,
        DbKind kind,
        string tableName,
        ExternalDbColumnMap map)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(map);

        try
        {
            var table = ReadTable(connectionString, kind, tableName);
            return Result<ImportBatch>.Success(BuildBatch(table, map, tableName));
        }
        catch (ExternalDbException ex)
        {
            return Result<ImportBatch>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Возвращает перечень таблиц внешней базы данных для диалога
    /// «Импорт из внешней БД» (ТЗ §8.5, функция 76: «выбор таблицы»).
    /// </summary>
    /// <remarks>
    /// Название метода сохранено по требованию ТЗ; перечень таблиц читается
    /// синхронно, поэтому при вызове из интерфейса операцию следует выполнять
    /// в отдельном потоке.
    /// </remarks>
    /// <param name="connectionString">Строка подключения к внешней базе данных.</param>
    /// <param name="kind">Вид внешней базы данных.</param>
    /// <returns>Список имён таблиц либо сообщение об ошибке подключения.</returns>
    public Result<IReadOnlyList<string>> ListTablesAsync(
        string connectionString,
        DbKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        try
        {
            var factory = ResolveFactory(kind);
            using var connection = CreateConnection(factory, connectionString);
            connection.Open();
            var tables = ReadTableNames(connection, kind);
            return Result<IReadOnlyList<string>>.Success(tables);
        }
        catch (ExternalDbException ex)
        {
            return Result<IReadOnlyList<string>>.Failure(ex.Message);
        }
#pragma warning disable CA1031 // причина подключения приходит от внешнего драйвера
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Result<IReadOnlyList<string>>.Failure(BuildConnectionErrorMessage(connectionString, ex));
        }
    }

    /// <summary>
    /// Возвращает перечень колонок выбранной таблицы внешней базы данных —
    /// для построения соответствия колонок и полей в диалоге
    /// «Импорт из внешней БД» (ТЗ §8.5, функция 76: «запуск мастера
    /// соответствия полей»).
    /// </summary>
    /// <param name="connectionString">Строка подключения к внешней базе данных.</param>
    /// <param name="kind">Вид внешней базы данных.</param>
    /// <param name="tableName">Имя таблицы.</param>
    /// <returns>Список имён колонок либо сообщение об ошибке подключения.</returns>
    public Result<IReadOnlyList<string>> ListColumnsAsync(
        string connectionString,
        DbKind kind,
        string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        try
        {
            var table = ReadTable(connectionString, kind, tableName);
            return Result<IReadOnlyList<string>>.Success(table.Columns);
        }
        catch (ExternalDbException ex)
        {
            return Result<IReadOnlyList<string>>.Failure(ex.Message);
        }
#pragma warning disable CA1031 // причина подключения приходит от внешнего драйвера
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Result<IReadOnlyList<string>>.Failure(BuildConnectionErrorMessage(connectionString, ex));
        }
    }

    /// <summary>
    /// Проверяет возможность подключения к внешней базе данных — кнопка
    /// «Проверить соединение» диалога «Импорт из внешней БД»
    /// (ТЗ §8.5, функция 76).
    /// </summary>
    /// <remarks>
    /// Название метода сохранено по требованию ТЗ; проверка выполняется
    /// синхронно, поэтому при вызове из интерфейса операцию следует выполнять
    /// в отдельном потоке.
    /// </remarks>
    /// <param name="connectionString">Строка подключения к внешней базе данных.</param>
    /// <param name="kind">Вид внешней базы данных.</param>
    /// <returns>
    /// <see langword="true"/>, если подключение установлено; иначе —
    /// <see langword="false"/> с сообщением об ошибке на русском языке.
    /// </returns>
    public Result<bool> TestConnectionAsync(string connectionString, DbKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        try
        {
            var factory = ResolveFactory(kind);
            using var connection = CreateConnection(factory, connectionString);
            connection.Open();
            return Result<bool>.Success(true);
        }
        catch (ExternalDbException ex)
        {
            return Result<bool>.Failure(ex.Message);
        }
#pragma warning disable CA1031 // причина подключения приходит от внешнего драйвера
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Result<bool>.Failure(BuildConnectionErrorMessage(connectionString, ex));
        }
    }

    /// <summary>
    /// Маскирует пароль в строке подключения — используется перед выводом строки
    /// подключения в интерфейс или в журнал (ТЗ §8.5, функция 76: «пароль не
    /// пишется в лог»).
    /// </summary>
    /// <param name="connectionString">Строка подключения.</param>
    /// <returns>Строка подключения с замаскированным паролем.</returns>
    public static string RedactConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(connectionString.Length);
        var position = 0;

        while (position < connectionString.Length)
        {
            var separator = connectionString.IndexOf(';', position);
            var segmentEnd = separator < 0 ? connectionString.Length : separator;
            var segment = connectionString[position..segmentEnd];

            builder.Append(MaskPasswordSegment(segment));

            if (separator < 0)
            {
                break;
            }

            builder.Append(';');
            position = separator + 1;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Формирует пакет импорта по прочитанным строкам внешней таблицы.
    /// </summary>
    private static ImportBatch BuildBatch(
        ExternalTable table,
        ExternalDbColumnMap map,
        string tableName)
    {
        var batch = new ImportBatch
        {
            SourceFormat = SourceFormat,
            SourceFile = tableName,
            ImportedAt = DateTime.Now,
        };

        var source = batch.SourceDescription;
        var rowNumber = 0;

        foreach (var row in table.Rows)
        {
            rowNumber++;
            batch.Records.Add(new ImportRecord
            {
                RowNumber = rowNumber,
                Draft = BuildDraft(row, map, source),
            });
        }

        return batch;
    }

    /// <summary>
    /// Формирует черновик контрагента по строке внешней таблицы.
    /// </summary>
    private static CounterpartyDraft BuildDraft(
        IReadOnlyDictionary<string, string?> row,
        ExternalDbColumnMap map,
        string source)
    {
        var draft = new CounterpartyDraft
        {
            Name = ReadValue(map, ExternalDbField.Name, row) ?? string.Empty,
            ShortName = ReadValue(map, ExternalDbField.ShortName, row),
            Inn = ReadValue(map, ExternalDbField.Inn, row),
            Kpp = ReadValue(map, ExternalDbField.Kpp, row),
            Ogrn = ReadValue(map, ExternalDbField.Ogrn, row),
            Phone = ReadValue(map, ExternalDbField.Phone, row),
            Email = ReadValue(map, ExternalDbField.Email, row),
            Note = ReadValue(map, ExternalDbField.Note, row),
            Source = source,
        };

        var postal = BuildAddress(row, map, ExternalDbField.PostalZip, ExternalDbField.PostalAddress);
        draft.PostalZip = postal.Zip;
        draft.PostalLines = postal.Lines;

        var legal = BuildAddress(row, map, ExternalDbField.LegalZip, ExternalDbField.LegalAddress);
        draft.LegalZip = legal.Zip;
        draft.LegalLines = legal.Lines;

        var actual = BuildAddress(row, map, ExternalDbField.ActualZip, ExternalDbField.ActualAddress);
        draft.ActualZip = actual.Zip;
        draft.ActualLines = actual.Lines;

        draft.NeedsVerification = postal.Zip.Length == 0
            && postal.Lines.Any(line => line.Length > 0);

        return draft;
    }

    /// <summary>
    /// Формирует почтовый индекс и строки адреса из соответствующих колонок.
    /// </summary>
    private static (string Zip, string[] Lines) BuildAddress(
        IReadOnlyDictionary<string, string?> row,
        ExternalDbColumnMap map,
        ExternalDbField zipField,
        ExternalDbField addressField)
    {
        var zip = ReadValue(map, zipField, row) ?? string.Empty;
        var text = ReadValue(map, addressField, row) ?? string.Empty;

        var (extractedZip, remainder) = SplitPostalIndex(text);
        if (zip.Length == 0)
        {
            zip = extractedZip;
        }

        return (zip, SplitAddressLines(remainder));
    }

    /// <summary>
    /// Читает значение поля по назначенному столбцу внешней таблицы.
    /// </summary>
    private static string? ReadValue(
        ExternalDbColumnMap map,
        ExternalDbField field,
        IReadOnlyDictionary<string, string?> row)
    {
        var column = map.GetColumn(field);
        if (column is null || !row.TryGetValue(column, out var value) || value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Выделяет шестизначный почтовый индекс из текста адреса.
    /// </summary>
    private static (string Zip, string Text) SplitPostalIndex(string text)
    {
        if (text.Length == 0)
        {
            return (string.Empty, text);
        }

        var match = _postalIndexPattern.Match(text);
        if (!match.Success)
        {
            return (string.Empty, text);
        }

        var zip = match.Groups[1].Value;
        var remainder = string.Concat(
            text.AsSpan(0, match.Index),
            " ",
            text.AsSpan(match.Index + match.Length)).Trim();

        return (zip, remainder);
    }

    /// <summary>
    /// Разбивает текст адреса на строки адреса получателя (ТЗ §3.1.1: шесть строк).
    /// </summary>
    private static string[] SplitAddressLines(string text)
    {
        var lines = new string[CounterpartyDraft.AddressLineCount];
        if (string.IsNullOrWhiteSpace(text))
        {
            return lines;
        }

        var parts = text.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();

        if (parts.Count == 0)
        {
            return lines;
        }

        var settlement = parts.FindIndex(IsSettlement);
        var street = parts.FindIndex(IsStreet);
        if (settlement > 0 && street >= 0 && settlement > street)
        {
            (parts[settlement], parts[street]) = (parts[street], parts[settlement]);
        }

        var region = parts.FindIndex(IsRegion);
        if (region >= 0 && region < parts.Count - 1)
        {
            var regionPart = parts[region];
            parts.RemoveAt(region);
            parts.Add(regionPart);
        }

        for (var index = 0; index < parts.Count && index < lines.Length; index++)
        {
            lines[index] = parts[index];
        }

        return lines;
    }

    /// <summary>
    /// Определяет, что часть адреса указывает на населённый пункт.
    /// </summary>
    private static bool IsSettlement(string part) =>
        StartsWithKeyword(part, "г.")
        || StartsWithKeyword(part, "г ")
        || StartsWithKeyword(part, "город")
        || StartsWithKeyword(part, "с.")
        || StartsWithKeyword(part, "село")
        || StartsWithKeyword(part, "пос.")
        || StartsWithKeyword(part, "поселок")
        || StartsWithKeyword(part, "пгт")
        || StartsWithKeyword(part, "ст.")
        || StartsWithKeyword(part, "станица")
        || StartsWithKeyword(part, "хутор")
        || StartsWithKeyword(part, "деревня");

    /// <summary>
    /// Определяет, что часть адреса указывает на улицу.
    /// </summary>
    private static bool IsStreet(string part) =>
        StartsWithKeyword(part, "ул.")
        || StartsWithKeyword(part, "улица")
        || StartsWithKeyword(part, "просп.")
        || StartsWithKeyword(part, "проспект")
        || StartsWithKeyword(part, "пр-т")
        || StartsWithKeyword(part, "ш.")
        || StartsWithKeyword(part, "шоссе")
        || StartsWithKeyword(part, "пер.")
        || StartsWithKeyword(part, "переулок")
        || StartsWithKeyword(part, "бульвар")
        || StartsWithKeyword(part, "бул.")
        || StartsWithKeyword(part, "наб.")
        || StartsWithKeyword(part, "набережная")
        || StartsWithKeyword(part, "проезд")
        || StartsWithKeyword(part, "тупик")
        || StartsWithKeyword(part, "аллея")
        || StartsWithKeyword(part, "тракт")
        || StartsWithKeyword(part, "линия")
        || StartsWithKeyword(part, "микрорайон")
        || StartsWithKeyword(part, "мкр")
        || StartsWithKeyword(part, "тер.");

    /// <summary>
    /// Определяет, что часть адреса указывает на регион.
    /// </summary>
    private static bool IsRegion(string part) =>
        StartsWithKeyword(part, "область")
        || StartsWithKeyword(part, "обл.")
        || StartsWithKeyword(part, "край")
        || StartsWithKeyword(part, "респ.")
        || StartsWithKeyword(part, "республика")
        || StartsWithKeyword(part, "район")
        || StartsWithKeyword(part, "р-н")
        || EndsWithKeyword(part, "область")
        || EndsWithKeyword(part, "обл.")
        || EndsWithKeyword(part, "край")
        || EndsWithKeyword(part, "респ.")
        || EndsWithKeyword(part, "республика")
        || EndsWithKeyword(part, "район")
        || EndsWithKeyword(part, "р-н");

    /// <summary>
    /// Проверяет, что часть адреса заканчивается указанным сокращением.
    /// </summary>
    private static bool EndsWithKeyword(string part, string keyword) =>
        part.EndsWith(keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Проверяет, что часть адреса начинается с указанного сокращения.
    /// </summary>
    private static bool StartsWithKeyword(string part, string keyword)
    {
        if (!part.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (part.Length == keyword.Length)
        {
            return true;
        }

        var next = part[keyword.Length];
        return next == ' ' || next == '.' || next == '-' || char.IsDigit(next);
    }

    /// <summary>
    /// Выбирает таблицу с контрагентами: по названию, при отсутствии совпадений —
    /// первую таблицу базы.
    /// </summary>
    private static string ChooseCounterpartiesTable(IReadOnlyList<string> tableNames)
    {
        foreach (var tableName in tableNames)
        {
            foreach (var marker in _counterpartiesTableMarkers)
            {
                if (tableName.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return tableName;
                }
            }
        }

        return tableNames[0];
    }

    /// <summary>
    /// Создаёт соединение с внешней базой данных.
    /// </summary>
    private static DbConnection CreateConnection(DbProviderFactory factory, string connectionString)
    {
        var connection = factory.CreateConnection()
            ?? throw new ExternalDbDriverException(
                ConnectionFailedMessage + ": не удалось создать подключение к внешней базе данных.");

        connection.ConnectionString = connectionString;
        return connection;
    }

    /// <summary>
    /// Возвращает фабрику провайдера для выбранного вида базы данных. Драйверы MySQL
    /// и SQL Server ищутся по отражению, поэтому список пакетов решения не меняется.
    /// </summary>
    private static DbProviderFactory ResolveFactory(DbKind kind) => kind switch
    {
        DbKind.Sqlite => SqliteFactory.Instance,
        DbKind.MySql => ResolveFactoryByReflection(MySqlFactoryTypeName, MySqlDriverMissingMessage),
        DbKind.SqlServer => ResolveFactoryByReflection(SqlServerFactoryTypeName, SqlServerDriverMissingMessage),
        _ => throw new ExternalDbDriverException(
            ConnectionFailedMessage + ": неподдерживаемый вид внешней базы данных."),
    };

    /// <summary>
    /// Ищет фабрику провайдера по полному имени типа.
    /// </summary>
    private static DbProviderFactory ResolveFactoryByReflection(string typeName, string missingMessage)
    {
        var type = Type.GetType(typeName, throwOnError: false);

        if (type is null)
        {
            var parts = typeName.Split(',', StringSplitOptions.TrimEntries);

            try
            {
                var assembly = Assembly.Load(new AssemblyName(parts[^1].Trim()));
                type = assembly.GetType(parts[0].Trim());
            }
#pragma warning disable CA1031 // драйвер может отсутствовать по любой причине
            catch (Exception ex) when (ex is FileNotFoundException
                or FileLoadException
                or BadImageFormatException
                or TypeLoadException)
#pragma warning restore CA1031
            {
                throw new ExternalDbDriverException(missingMessage);
            }
        }

        if (type is null)
        {
            throw new ExternalDbDriverException(missingMessage);
        }

        var instance = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (instance is null)
        {
            instance = Activator.CreateInstance(type);
        }

        return instance as DbProviderFactory
            ?? throw new ExternalDbDriverException(missingMessage);
    }

    /// <summary>
    /// Читает имена таблиц внешней базы данных.
    /// </summary>
    private static IReadOnlyList<string> ReadTableNames(
        DbConnection connection,
        DbKind kind)
    {
        var sql = kind switch
        {
            DbKind.Sqlite =>
                "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') "
                + "AND name NOT LIKE 'sqlite_%' ORDER BY name",
            DbKind.MySql =>
                "SELECT table_name FROM information_schema.tables "
                + "WHERE table_schema = DATABASE() ORDER BY table_name",
            DbKind.SqlServer =>
                "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES "
                + "WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW') ORDER BY TABLE_NAME",
            _ => throw new ExternalDbDriverException(
                ConnectionFailedMessage + ": неподдерживаемый вид внешней базы данных."),
        };

        var names = new List<string>();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = DefaultCommandTimeoutSeconds;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        return names;
    }

    /// <summary>
    /// Читает содержимое таблицы внешней базы данных.
    /// </summary>
    private static ExternalTable ReadTable(
        string connectionString,
        DbKind kind,
        string tableName)
    {
        var factory = ResolveFactory(kind);

        try
        {
            using var connection = CreateConnection(factory, connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {QuoteIdentifier(kind, tableName)}";
            command.CommandTimeout = DefaultCommandTimeoutSeconds;

            var columns = new List<string>();
            var rows = new List<IReadOnlyDictionary<string, string?>>();

            using var reader = command.ExecuteReader();

            for (var index = 0; index < reader.FieldCount; index++)
            {
                columns.Add(reader.GetName(index));
            }

            while (reader.Read())
            {
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                for (var index = 0; index < reader.FieldCount; index++)
                {
                    row[columns[index]] = reader.IsDBNull(index)
                        ? null
                        : ConvertToText(reader.GetValue(index));
                }

                rows.Add(row);
            }

            return new ExternalTable
            {
                Columns = columns,
                Rows = rows,
            };
        }
#pragma warning disable CA1031 // причина подключения приходит от внешнего драйвера
        catch (Exception ex) when (ex is not ExternalDbException)
#pragma warning restore CA1031
        {
            throw new ExternalDbConnectionException(
                BuildConnectionErrorMessage(connectionString, ex),
                ex);
        }
    }

    /// <summary>
    /// Экранирует имя таблицы для выбранного вида базы данных.
    /// </summary>
    private static string QuoteIdentifier(DbKind kind, string tableName) => kind switch
    {
        DbKind.MySql => "`" + tableName.Replace("`", "``", StringComparison.Ordinal) + "`",
        _ => "\"" + tableName.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"",
    };

    /// <summary>
    /// Приводит значение ячейки к тексту.
    /// </summary>
    private static string? ConvertToText(object? value) => value switch
    {
        null or DBNull => null,
        string text => text,
        byte[] bytes => Convert.ToBase64String(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>
    /// Формирует сообщение об ошибке подключения с детализацией исклюнения.
    /// Пароль из строки подключения маскируется (ТЗ §8.5, функция 76).
    /// </summary>
    private static string BuildConnectionErrorMessage(string? connectionString, Exception exception)
    {
        var details = RedactText(exception.Message, connectionString);
        if (string.IsNullOrWhiteSpace(details))
        {
            details = exception.GetType().Name;
        }

        return $"{ConnectionFailedMessage}: {details}";
    }

    /// <summary>
    /// Удаляет пароль из произвольного текста (сообщение исключения, журнал).
    /// </summary>
    private static string RedactText(string text, string? connectionString)
    {
        var result = text;

        var password = ExtractPassword(connectionString);
        if (!string.IsNullOrEmpty(password))
        {
            result = result.Replace(password, "***", StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(connectionString))
        {
            var redacted = RedactConnectionString(connectionString);
            if (!string.Equals(redacted, connectionString, StringComparison.Ordinal))
            {
                result = result.Replace(connectionString, redacted, StringComparison.Ordinal);
            }
        }

        return _passwordFragmentPattern.Replace(result, "$1=***");
    }

    /// <summary>
    /// Извлекает значение пароля из строки подключения.
    /// </summary>
    private static string? ExtractPassword(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return null;
        }

        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsPasswordSegment(segment))
            {
                continue;
            }

            var equals = segment.IndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            var value = segment[(equals + 1)..].Trim().Trim('"').Trim('\'');
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    /// <summary>
    /// Маскирует значение пароля в отдельном фрагменте строки подключения.
    /// </summary>
    private static string MaskPasswordSegment(string segment)
    {
        if (!IsPasswordSegment(segment))
        {
            return segment;
        }

        var equals = segment.IndexOf('=');
        return equals < 0
            ? segment
            : string.Concat(segment.AsSpan(0, equals + 1), "***");
    }

    /// <summary>
    /// Определяет, что фрагмент строки подключения содержит пароль.
    /// </summary>
    private static bool IsPasswordSegment(string segment)
    {
        var trimmed = segment.TrimStart();
        var equals = trimmed.IndexOf('=');
        var name = (equals < 0 ? trimmed : trimmed[..equals]).Trim().Trim('"');

        return name.Equals("password", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pwd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Прочитанная таблица внешней базы данных.
    /// </summary>
    private sealed class ExternalTable
    {
        /// <summary>Имена столбцов таблицы.</summary>
        public IReadOnlyList<string> Columns { get; init; } = [];

        /// <summary>Строки таблицы.</summary>
        public IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows { get; init; } = [];
    }
}
