namespace Picket302.Infrastructure.Integration;

/// <summary>
/// Вид внешней реляционной базы данных — источника импорта контрагентов
/// (ТЗ §8.5, функция 76: «Импорт контрагентов из отдельной внешней БД»).
/// </summary>
/// <remarks>
/// Перечень видов ограничен драйверами, доступными без установки дополнительных
/// пакетов: SQLite (<c>Microsoft.Data.Sqlite</c>) подключён постоянно, а MySQL
/// (<c>MySqlConnector</c>) и SQL Server (<c>Microsoft.Data.SqlClient</c>)
/// подключаются по требованию — драйвер ищется в загруженных сборках, и при его
/// отсутствии выдаётся понятная ошибка на русском языке.
/// </remarks>
public enum DbKind
{
    /// <summary>SQLite — файл базы данных на диске.</summary>
    Sqlite = 0,

    /// <summary>MySQL (в том числе MariaDB) — драйвер <c>MySqlConnector</c>.</summary>
    MySql = 1,

    /// <summary>
    /// Microsoft SQL Server — драйвер <c>Microsoft.Data.SqlClient</c>
    /// (подключается по требованию; без драйвера импорт невозможен).
    /// </summary>
    SqlServer = 2,
}

/// <summary>
/// Справочные сведения о видах внешней базы данных (ТЗ §8.5, функция 76:
/// диалог «Импорт из внешней БД»).
/// </summary>
public static class DbKinds
{
    /// <summary>Все поддерживаемые виды внешней базы данных.</summary>
    public static IReadOnlyList<DbKind> All { get; } =
    [
        DbKind.Sqlite,
        DbKind.MySql,
        DbKind.SqlServer,
    ];

    /// <summary>
    /// Возвращает название вида базы данных для интерфейса.
    /// </summary>
    /// <param name="kind">Вид внешней базы данных.</param>
    /// <returns>Название вида базы данных.</returns>
    public static string Title(DbKind kind) => kind switch
    {
        DbKind.Sqlite => "SQLite",
        DbKind.MySql => "MySQL",
        DbKind.SqlServer => "Microsoft SQL Server",
        _ => "Неизвестная база данных",
    };

    /// <summary>
    /// Разбирает название вида базы данных, введённое оператором.
    /// </summary>
    /// <param name="text">Название вида базы данных.</param>
    /// <param name="kind">Разобранный вид внешней базы данных.</param>
    /// <returns>
    /// <see langword="true"/>, если название распознано; иначе —
    /// <see langword="false"/>, а <paramref name="kind"/> получает значение
    /// <see cref="DbKind.Sqlite"/>.
    /// </returns>
    public static bool TryParse(string? text, out DbKind kind)
    {
        kind = DbKind.Sqlite;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();

        if (normalized.Contains("sqlite", StringComparison.Ordinal))
        {
            kind = DbKind.Sqlite;
            return true;
        }

        if (normalized.Contains("mysql", StringComparison.Ordinal)
            || normalized.Contains("mariadb", StringComparison.Ordinal))
        {
            kind = DbKind.MySql;
            return true;
        }

        if (normalized.Contains("sqlserver", StringComparison.Ordinal)
            || normalized.Contains("sql server", StringComparison.Ordinal)
            || normalized.Contains("mssql", StringComparison.Ordinal))
        {
            kind = DbKind.SqlServer;
            return true;
        }

        return false;
    }
}
