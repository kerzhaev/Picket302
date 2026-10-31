using Microsoft.Data.Sqlite;
using Picket302.Infrastructure.Data;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Тестовая база данных SQLite в памяти (ТЗ §11.2: «БД-мок: SQLite in-memory
/// с сеяными данными демо-базы»; §11.1: интеграционный тест на встроенной
/// SQLite в памяти).
/// </summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteUnitOfWork _единицаРаботы;

    private SqliteTestDatabase(string connectionString, SqliteUnitOfWork единицаРаботы)
    {
        ConnectionString = connectionString;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>Строка подключения к тестовой базе.</summary>
    public string ConnectionString { get; }

    /// <summary>Единица работы над тестовой базой.</summary>
    public SqliteUnitOfWork UnitOfWork => _единицаРаботы;

    /// <summary>Сессия доступа к тестовой базе.</summary>
    public ISqliteSession Session => _единицаРаботы;

    /// <summary>Соединение с тестовой базой.</summary>
    public SqliteConnection Connection => _единицаРаботы.Connection;

    /// <summary>
    /// Создаёт пустую базу данных в памяти с применёнными миграциями.
    /// </summary>
    /// <param name="сМиграциями">Применять ли миграции схемы.</param>
    /// <returns>Готовая тестовая база.</returns>
    public static async Task<SqliteTestDatabase> CreateAsync(bool сМиграциями = true)
    {
        var имя = "picket302_test_" + Guid.NewGuid().ToString("N");
        var строка = new SqliteConnectionStringBuilder
        {
            DataSource = имя,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        var база = new SqliteTestDatabase(строка, new SqliteUnitOfWork(строка));

        if (сМиграциями)
        {
            await new MigrationRunner().ApplyAsync(база.Connection).ConfigureAwait(false);
        }

        return база;
    }

    /// <summary>
    /// Создаёт базу данных в файле с применёнными миграциями — используется для
    /// проверки архивирования и восстановления.
    /// </summary>
    /// <param name="путь">Путь к файлу базы данных.</param>
    /// <param name="сМиграциями">
    /// Применять ли миграции схемы. Значение <see langword="false"/> открывает
    /// существующий файл «как есть» — так проверяется, что восстановленная база
    /// действительно содержит схему и данные, а не пустая оболочка.
    /// </param>
    /// <returns>Готовая тестовая база.</returns>
    public static async Task<SqliteTestDatabase> CreateFileAsync(string путь, bool сМиграциями = true)
    {
        var строка = new SqliteConnectionStringBuilder
        {
            DataSource = путь,
        }.ToString();

        var база = new SqliteTestDatabase(строка, new SqliteUnitOfWork(строка));

        if (сМиграциями)
        {
            await new MigrationRunner().ApplyAsync(база.Connection).ConfigureAwait(false);
        }

        return база;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _единицаРаботы.DisposeAsync().ConfigureAwait(false);
    }
}
