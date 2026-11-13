using Dapper;
using Picket302.Domain.Backup;
using Picket302.Infrastructure.Backup;
using Picket302.Infrastructure.Data;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Круговой тест миграций схемы (ТЗ §3.1.18, §3.1.19, §10.2).
/// </summary>
/// <remarks>
/// <para>
/// Миграции выполняются только вперёд, поэтому «круг» проверяется полным циклом
/// обслуживания базы: создание базы и применение миграций → наполнение →
/// архивирование → восстановление в другую базу → повторное архивирование и
/// восстановление → повторное применение миграций (которое ничего не меняет).
/// </para>
/// <para>
/// Цикл замыкается: восстановленная база имеет ту же схему, те же данные и ту же
/// версию схемы, что и исходная.
/// </para>
/// </remarks>
public sealed class MigrationCircularTests : IDisposable
{
    private readonly string _каталог;

    /// <summary>Создаёт тест со временным каталогом.</summary>
    public MigrationCircularTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "Picket302Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_каталог);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Соединения BackupService/SqliteTestDatabase возвращаются в пул и
        // держат файл базы — без очистки пула каталог остаётся (замечание
        // review 2026-11-07: 2 каталога restored.db на полный прогон).
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, recursive: true);
            }

            if (Directory.Exists(_каталог))
            {
                Console.Error.WriteLine(
                    $"MigrationCircularTests: временный каталог не удалён и остался: {_каталог}");
            }
        }
        catch (IOException)
        {
            Console.Error.WriteLine(
                $"MigrationCircularTests: не удалось удалить временный каталог: {_каталог}");
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Полный круг: миграции → данные → архив → восстановление → повторный круг.
    /// </summary>
    [Fact]
    public async Task Круг_МиграцииАрхивВосстановлениеЗамыкается()
    {
        var путьБазы = Path.Combine(_каталог, "picket302.db");
        var папкаАрхивов = Path.Combine(_каталог, "backup");
        Directory.CreateDirectory(папкаАрхивов);

        var runner = new MigrationRunner();
        var версии = runner.Migrations.Select(миграция => миграция.Version).ToList();

        // 1. Создание базы и применение миграций.
        await using (var база = await SqliteTestDatabase.CreateFileAsync(путьБазы))
        {
            Assert.Equal(
                runner.Migrations.Count,
                await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM schema_version;"));
        }

        // 2. Наполнение базы.
        await using (var база = await SqliteTestDatabase.CreateFileAsync(путьБазы))
        {
            await база.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, inn, address_main_type, is_vip, is_sender, is_active, is_deleted) "
                + "VALUES (0, 'ООО «Техно»', '7707083893', 1, 0, 0, 1, 0);");

            var количество = await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM counterparties;");
            Assert.Equal(1, количество);
        }

        // 3. Архивирование.
        var служба = new BackupService(путьБазы, версии);
        var архив = служба.BackupNow(new BackupOptions(папкаАрхивов));

        Assert.True(архив.IsSuccess, архив.Error);
        Assert.NotNull(архив.ArchivePath);
        Assert.True(File.Exists(архив.ArchivePath));

        // 4. Восстановление в другую базу.
        var путьВосстановленной = Path.Combine(_каталог, "restored.db");
        File.Copy(путьБазы, путьВосстановленной, overwrite: true);

        var службаВосстановления = new BackupService(путьВосстановленной, версии);
        var восстановление = службаВосстановления.RestoreFromArchive(архив.ArchivePath!, string.Empty);

        Assert.True(восстановление.IsSuccess, восстановление.Error);
        Assert.Equal(версияСхемы(версии), восстановление.SchemaVersion);

        // 5. Восстановленная база имеет ту же схему и те же данные.
        // База открывается БЕЗ повторного применения миграций: иначе проверка
        // была бы тавтологичной — пустая база после миграций тоже содержала бы
        // нужное число строк в schema_version.
        await using (var база = await SqliteTestDatabase.CreateFileAsync(путьВосстановленной, сМиграциями: false))
        {
            Assert.Equal(
                runner.Migrations.Count,
                await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM schema_version;"));

            var таблицы = await база.Connection.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;");
            // V009 добавила incoming_mails (S32): полный набор таблиц.
            Assert.Equal(21, таблицы.Count());

            var контрагент = await база.Connection.QuerySingleAsync<string>(
                "SELECT name FROM counterparties;");

            Assert.Equal("ООО «Техно»", контрагент);
        }

        // 6. Повторное архивирование и восстановление — круг замыкается.
        var второйАрхив = службаВосстановления.BackupNow(new BackupOptions(папкаАрхивов));

        Assert.True(второйАрхив.IsSuccess, второйАрхив.Error);
        Assert.NotEqual(архив.ArchivePath, второйАрхив.ArchivePath);

        var второеВосстановление = службаВосстановления.RestoreFromArchive(второйАрхив.ArchivePath!, string.Empty);
        Assert.True(второеВосстановление.IsSuccess, второеВосстановление.Error);

        // 7. Повторное применение миграций к восстановленной базе ничего не меняет.
        await using (var база = await SqliteTestDatabase.CreateFileAsync(путьВосстановленной, сМиграциями: false))
        {
            var применённые = await runner.ApplyAsync(база.Connection);

            Assert.Empty(применённые);
            Assert.Equal(
                runner.Migrations.Count,
                await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM schema_version;"));
        }
    }

    /// <summary>
    /// Восстановленная база получает те же индексы, что и исходная: схема
    /// воспроизводится полностью.
    /// </summary>
    [Fact]
    public async Task Круг_ВосстановлениеВоспроизводитИндексы()
    {
        var путьБазы = Path.Combine(_каталог, "picket302.db");
        var папкаАрхивов = Path.Combine(_каталог, "backup");
        Directory.CreateDirectory(папкаАрхивов);

        var runner = new MigrationRunner();
        var версии = runner.Migrations.Select(миграция => миграция.Version).ToList();

        await using (var база = await SqliteTestDatabase.CreateFileAsync(путьБазы))
        {
            await база.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, address_main_type, is_vip, is_sender, is_active, is_deleted) "
                + "VALUES (0, 'ООО «Техно»', 1, 0, 0, 1, 0);");
        }

        var индексыДо = await ПрочитатьИндексы(путьБазы);

        var служба = new BackupService(путьБазы, версии);
        var архив = служба.BackupNow(new BackupOptions(папкаАрхивов));
        Assert.True(архив.IsSuccess, архив.Error);

        var путьВосстановленной = Path.Combine(_каталог, "restored.db");
        File.Copy(путьБазы, путьВосстановленной, overwrite: true);

        var восстановление = new BackupService(путьВосстановленной, версии)
            .RestoreFromArchive(архив.ArchivePath!, string.Empty);

        Assert.True(восстановление.IsSuccess, восстановление.Error);

        var индексыПосле = await ПрочитатьИндексы(путьВосстановленной);

        Assert.Equal(индексыДо, индексыПосле);
        Assert.Contains("idx_counterparties_name", индексыПосле);
        Assert.Contains("ux_groups_single_default", индексыПосле);
        Assert.Contains("idx_mails_folder_id", индексыПосле);
    }

    /// <summary>
    /// Возвращает последнюю версию схемы из списка известных версий.
    /// </summary>
    /// <param name="версии">Известные версии схемы.</param>
    /// <returns>Последняя версия схемы.</returns>
    private static string версияСхемы(IReadOnlyList<string> версии)
        => версии.OrderBy(версия => int.Parse(версия, System.Globalization.CultureInfo.InvariantCulture)).Last();

    /// <summary>
    /// Читает перечень индексов базы данных.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <returns>Отсортированный перечень имён индексов.</returns>
    private static async Task<IReadOnlyList<string>> ПрочитатьИндексы(string путьБазы)
    {
        await using var соединение = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = путьБазы }.ToString());

        await соединение.OpenAsync();

        var индексы = await соединение.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%' ORDER BY name;");

        return индексы.ToList();
    }
}
