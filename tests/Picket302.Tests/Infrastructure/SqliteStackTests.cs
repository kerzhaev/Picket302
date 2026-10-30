using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка инфраструктурного стека «SQLite + Dapper», подключённого в слое
/// инфраструктуры (ТЗ §10.1 «Стек и версии», §11.2 «Мок-объекты»: SQLite in-memory).
/// </summary>
public sealed class SqliteStackTests
{
    [Fact]
    public async Task SqliteИDapper_ПозволяютСоздатьТаблицуИПрочитатьЗапись()
    {
        await using var соединение = new SqliteConnection("Data Source=:memory:");
        await соединение.OpenAsync();

        await соединение.ExecuteAsync(
            "CREATE TABLE smoke (id INTEGER PRIMARY KEY, name TEXT NOT NULL);");
        await соединение.ExecuteAsync(
            "INSERT INTO smoke (name) VALUES (@name);",
            new { name = "Проверка" });

        var имя = await соединение.QuerySingleAsync<string>("SELECT name FROM smoke WHERE id = 1;");

        Assert.Equal("Проверка", имя);
    }
}
