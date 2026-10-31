using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Infrastructure.Data;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка схемы базы данных и миграций (ТЗ §3.1.1–3.1.18, §3.1.19, §10.2).
/// </summary>
public sealed class MigrationTests
{
    /// <summary>
    /// Все таблицы модели данных ТЗ §3.1.1–3.1.18.
    /// </summary>
    public static TheoryData<string> Таблицы => new()
    {
        "counterparties",
        "groups",
        "mails",
        "print_templates",
        "blank_fields",
        "print_jobs",
        "print_history",
        "qr_cache",
        "counterparty_fields",
        "distributions",
        "folders",
        "inventories",
        "inventory_items",
        "post_registers",
        "document_movements",
        "mail_documents",
        "printer_profiles",
        "parameters",
        "schema_version",
    };

    /// <summary>
    /// Обязательные индексы ТЗ §3.1.19.
    /// </summary>
    public static TheoryData<string> ОбязательныеИндексы => new()
    {
        "idx_counterparties_name",
        "idx_counterparties_inn",
        "idx_counterparties_group_id",
        "idx_mails_counterparty_id",
        "idx_mails_sender_id",
        "idx_mails_status",
        "idx_mails_date_created",
        "idx_mails_post_register_id",
        "idx_print_history_printed_at",
        "idx_print_history_mail_id",
        "idx_inventory_items_inventory_id",
        "idx_counterparty_fields_counterparty_id",
    };

    [Theory]
    [MemberData(nameof(Таблицы))]
    public async Task Миграции_СоздаютВсеТаблицыМодели(string таблица)
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var существует = await база.Connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @Name;",
            new { Name = таблица });

        Assert.True(существует == 1, $"Таблица {таблица} не создана миграциями.");
    }

    [Theory]
    [MemberData(nameof(ОбязательныеИндексы))]
    public async Task Миграции_СоздаютОбязательныеИндексы(string индекс)
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var существует = await база.Connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = @Name;",
            new { Name = индекс });

        Assert.True(существует == 1, $"Индекс {индекс} не создан миграциями.");
    }

    [Fact]
    public async Task Миграции_ФиксируютсяВSchemaVersion()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var версии = await база.Connection.QueryAsync<(string Version, string? Description)>(
            "SELECT version, description FROM schema_version ORDER BY id;");

        var список = версии.ToList();

        Assert.NotEmpty(список);
        Assert.All(список, запись => Assert.False(string.IsNullOrWhiteSpace(запись.Version)));
        Assert.All(список, запись => Assert.False(string.IsNullOrWhiteSpace(запись.Description)));

        // Версии возрастают и не повторяются.
        var номера = список.Select(запись => int.Parse(запись.Version, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(номера.OrderBy(номер => номер), номера);
        Assert.Equal(номера.Count, номера.Distinct().Count());
    }

    /// <summary>
    /// Миграции применяются только вперёд: повторный запуск на уже мигрированной
    /// базе не изменяет схему и не добавляет строк в <c>schema_version</c>.
    /// </summary>
    [Fact]
    public async Task Миграции_ПовторныйЗапускНеИзменяетСхему()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var до = await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM schema_version;");
        var таблицыДо = await база.Connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';");

        var применённые = await new MigrationRunner().ApplyAsync(база.Connection);

        Assert.Empty(применённые);
        Assert.Equal(до, await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM schema_version;"));
        Assert.Equal(таблицыДо, await база.Connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';"));
    }

    /// <summary>
    /// Версия схемы новее известной программе приводит к отказу миграции
    /// (ТЗ §8.1, функция 104: «версия схемы новее программы — отказ с указанием
    /// требуемой версии»).
    /// </summary>
    [Fact]
    public async Task Миграции_ВерсияСхемыНовееПрограммы_Отказ()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        await база.Connection.ExecuteAsync(
            "INSERT INTO schema_version (version, description) VALUES ('999', 'Версия из будущего');");

        var ошибка = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new MigrationRunner().ApplyAsync(база.Connection));

        Assert.Contains("новее", ошибка.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("999", ошибка.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Миграции поставляются как встроенные ресурсы сборки в формате
    /// <c>V&lt;версия&gt;__&lt;описание&gt;.sql</c> (ТЗ §3.1.19).
    /// </summary>
    [Fact]
    public void Миграции_ПоставляютсяВстроеннымиРесурсами()
    {
        var миграции = new MigrationRunner().Migrations;

        Assert.NotEmpty(миграции);
        Assert.All(миграции, миграция => Assert.Matches(@"^\d+$", миграция.Version));
        Assert.All(миграции, миграция => Assert.False(string.IsNullOrWhiteSpace(миграция.Name)));
        Assert.All(миграции, миграция => Assert.False(string.IsNullOrWhiteSpace(миграция.Description)));
        Assert.All(миграции, миграция => Assert.False(string.IsNullOrWhiteSpace(миграция.Sql)));

        // Первая миграция создаёт схему, последующие — её наполняют либо
        // изменяют (добавление колонок и ограничений).
        Assert.Contains("CREATE TABLE counterparties", миграции[0].Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE schema_version", миграции[0].Sql, StringComparison.OrdinalIgnoreCase);
        Assert.All(миграции.Skip(1), миграция =>
        {
            Assert.True(
                миграция.Sql.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                    || миграция.Sql.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase),
                $"Миграция {миграция.Version} не содержит ни наполнения, ни изменения схемы.");
        });
    }

    /// <summary>
    /// Миграция выполняется атомарно: применение миграции и запись строки в
    /// <c>schema_version</c> образуют одну транзакцию.
    /// </summary>
    [Fact]
    public async Task Миграции_ПрименяютсяАтомарно()
    {
        await using var база = await SqliteTestDatabase.CreateAsync(сМиграциями: false);

        var runner = new MigrationRunner();
        var применённые = await runner.ApplyAsync(база.Connection);

        Assert.Equal(runner.Migrations.Count, применённые.Count);
        Assert.Equal(
            runner.Migrations.Count,
            await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM schema_version;"));
    }

    /// <summary>
    /// Неудачная миграция не оставляет следа: выполнение скрипта и запись в
    /// <c>schema_version</c> откатываются целиком (ТЗ §3.1.19: миграции
    /// выполняются атомарно).
    /// </summary>
    [Fact]
    public async Task Миграции_НеудачнаяМиграцияОткатываетсяЦеликом()
    {
        await using var база = await SqliteTestDatabase.CreateAsync(сМиграциями: false);

        // Объект с именем, которое создаёт первая миграция, — её выполнение
        // прервётся на середине скрипта.
        await база.Connection.ExecuteAsync("CREATE TABLE counterparties (id INTEGER PRIMARY KEY);");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new MigrationRunner().ApplyAsync(база.Connection));

        // Ни одна таблица миграции не создана, ни одна версия не зафиксирована.
        var таблицСхемы = await база.Connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_version';");

        Assert.Equal(0, таблицСхемы);

        var посторонних = await база.Connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT IN ('counterparties', 'sqlite_sequence');");

        Assert.Equal(0, посторонних);
    }
}
