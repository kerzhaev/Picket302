using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Источник данных прежней установки Picket302: чтение файла базы данных
/// SQLite из папки прежней установки (ТЗ §8.9, функция 107: «Загрузка данных
/// из предыдущей/бесплатной версии»).
/// </summary>
/// <remarks>
/// <para>
/// Файл базы прежней установки читается в режиме «только чтение»: данные
/// прежней версии не изменяются. Чтение выполняется через
/// <see cref="Microsoft.Data.Sqlite"/> — тот же драйвер, что и у текущей базы.
/// </para>
/// <para>
/// Источник реализует <see cref="ILegacyTableSource"/>, поэтому данные
/// прежней установки читаются той же нейтральной моделью таблиц и строк,
/// что и данные исходных схем предшественника.
/// </para>
/// </remarks>
public sealed class LegacyUpgradeSource : ILegacyTableSource, IDisposable
{
    private readonly string _путьФайлаБазы;
    private SqliteConnection? _соединение;
    private bool _освобождено;

    /// <summary>
    /// Создаёт источник данных прежней установки.
    /// </summary>
    /// <param name="путьФайлаБазы">Путь к файлу базы данных прежней установки.</param>
    public LegacyUpgradeSource(string путьФайлаБазы)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьФайлаБазы);

        _путьФайлаБазы = Path.GetFullPath(путьФайлаБазы);
    }

    /// <summary>Путь к файлу базы данных прежней установки.</summary>
    public string ПутьФайлаБазы => _путьФайлаБазы;

    /// <summary>
    /// Версия схемы прежней установки (значение таблицы <c>schema_version</c>);
    /// <see langword="null"/>, если версия в базе не зафиксирована.
    /// </summary>
    public string? ВерсияСхемы { get; private set; }

    /// <summary>Признак того, что источник открыт.</summary>
    public bool Открыт => _соединение is not null;

    /// <inheritdoc />
    public async Task<bool> TryOpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_освобождено, this);

        if (!File.Exists(_путьФайлаБазы))
        {
            return false;
        }

        try
        {
            var соединение = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = _путьФайлаБазы,
                    Mode = SqliteOpenMode.ReadOnly,
                }.ToString());

            await соединение.OpenAsync(cancellationToken).ConfigureAwait(false);
            _соединение = соединение;
        }
        catch (SqliteException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        ВерсияСхемы = await ПрочитатьВерсиюСхемыAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LegacyTable>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        var соединение = ТребуетсяСоединение();

        var таблицы = new List<LegacyTable>();

        using var команда = соединение.CreateCommand();
        команда.CommandText =
            """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        using var читатель = await команда.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await читатель.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var имя = читатель.GetString(0);

            таблицы.Add(new LegacyTable(имя, await ПрочитатьСтолбцыAsync(имя, cancellationToken).ConfigureAwait(false)));
        }

        return таблицы;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LegacyRow>> GetRowsAsync(
        string table,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var соединение = ТребуетсяСоединение();
        var строки = new List<LegacyRow>();

        using var команда = соединение.CreateCommand();
        команда.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";

        using var читатель = await команда.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await читатель.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var значения = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var индекс = 0; индекс < читатель.FieldCount; индекс++)
            {
                значения[читатель.GetName(индекс)] = ПрочитатьЗначение(читатель.GetValue(индекс));
            }

            строки.Add(new LegacyRow(значения));
        }

        return строки;
    }

    /// <summary>
    /// Проверяет наличие таблицы в базе прежней установки.
    /// </summary>
    /// <param name="table">Имя таблицы.</param>
    /// <returns><see langword="true"/>, если таблица присутствует.</returns>
    public bool СодержитТаблицу(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var соединение = _соединение;

        if (соединение is null)
        {
            return false;
        }

        using var команда = соединение.CreateCommand();
        команда.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        команда.Parameters.AddWithValue("$name", table);

        var количество = (long)(команда.ExecuteScalar() ?? 0L);

        return количество > 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_освобождено)
        {
            return;
        }

        _освобождено = true;
        _соединение?.Dispose();
        _соединение = null;
    }

    /// <summary>
    /// Возвращает открытое соединение с базой прежней установки.
    /// </summary>
    /// <returns>Соединение с базой.</returns>
    private SqliteConnection ТребуетсяСоединение()
        => _соединение ?? throw new InvalidOperationException(
            "Источник прежней установки не открыт: сначала вызовите TryOpenAsync.");

    /// <summary>
    /// Читает список столбцов таблицы.
    /// </summary>
    /// <param name="таблица">Имя таблицы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список имён столбцов.</returns>
    private async Task<IReadOnlyList<string>> ПрочитатьСтолбцыAsync(
        string таблица,
        CancellationToken cancellationToken)
    {
        var соединение = ТребуетсяСоединение();
        var столбцы = new List<string>();

        using var команда = соединение.CreateCommand();
        команда.CommandText = $"PRAGMA table_info(\"{таблица.Replace("\"", "\"\"", StringComparison.Ordinal)}\");";
        команда.Parameters.AddWithValue("$table", таблица);

        using var читатель = await команда.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await читатель.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            столбцы.Add(читатель.GetString(1));
        }

        return столбцы;
    }

    /// <summary>
    /// Читает версию схемы прежней установки: последнюю запись таблицы
    /// <c>schema_version</c>.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Версия схемы либо <see langword="null"/>.</returns>
    private async Task<string?> ПрочитатьВерсиюСхемыAsync(CancellationToken cancellationToken)
    {
        var соединение = ТребуетсяСоединение();

        try
        {
            using var команда = соединение.CreateCommand();
            команда.CommandText = "SELECT version FROM schema_version ORDER BY id DESC LIMIT 1;";

            var значение = await команда.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return значение is null or DBNull ? null : Convert.ToString(значение, CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// <summary>
    /// Приводит значение ячейки к текстовому представлению: двоичные данные
    /// передаются в виде Base64, числа — в инвариантном формате.
    /// </summary>
    /// <param name="значение">Значение ячейки.</param>
    /// <returns>Текстовое представление значения либо <see langword="null"/>.</returns>
    private static string? ПрочитатьЗначение(object значение)
    {
        if (значение is null or DBNull)
        {
            return null;
        }

        if (значение is byte[] двоичные)
        {
            return двоичные.Length == 0 ? null : Convert.ToBase64String(двоичные);
        }

        if (значение is bool логическое)
        {
            return логическое ? "1" : "0";
        }

        if (значение is IFormattable форматируемое)
        {
            return форматируемое.ToString(null, CultureInfo.InvariantCulture);
        }

        return значение.ToString();
    }
}
