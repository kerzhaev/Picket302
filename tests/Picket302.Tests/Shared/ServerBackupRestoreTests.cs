using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Domain.Backup;
using Picket302.Infrastructure.Backup;
using Picket302.Service.Configuration;
using Picket302.Service.Data;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки резервного копирования общей базы серверной службы: копия
/// создается механизмом VACUUM INTO, восстанавливается в отдельный контур,
/// данные и пакеты шаблонов совпадают.
/// </summary>
public sealed class ServerBackupRestoreTests : IDisposable
{
    private readonly string _каталог;

    /// <summary>Создаёт тест с временным каталогом.</summary>
    public ServerBackupRestoreTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "Backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_каталог);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, recursive: true);
            }
        }
        catch (IOException)
        {
            // Каталог остаётся: временные файлы проверки не критичны.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Копия_Общей_Базы_Восстанавливается_С_Данными_И_Пакетами()
    {
        // 1. База службы с данными и пакетом шаблонов.
        var каталогСлужбы = Path.Combine(_каталог, "service");
        Directory.CreateDirectory(каталогСлужбы);

        var параметры = new ServiceOptions { DataDirectory = каталогСлужбы };
        var базаСлужбы = new ServerDatabase(параметры.DatabasePath);

        await базаСлужбы.ПодготовитьAsync();

        await using (var сессия = базаСлужбы.СоздатьСессию())
        {
            await сессия.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, is_active, legal_zip, legal_str1, address_main_type) " +
                "VALUES (0, 'ООО «Общая»', 1, '101000', 'ООО «Общая»', 0);");
            await сессия.Connection.ExecuteAsync(
                "INSERT INTO shared_template_packages (package_id, name, version, content_hash, size_bytes, " +
                "file_name, is_builtin, updated_at) VALUES ('pkg-1', 'Бланк', 1, 'hash', 10, 'v1.lbk', 0, '2026-11-15');");
        }

        // 2. Копирование: тот же механизм VACUUM INTO, что и в одиночном режиме.
        var каталогКопий = Path.Combine(_каталог, "backups");
        Directory.CreateDirectory(каталогКопий);

        var службаКопий = new BackupService(параметры.DatabasePath);

        var итог = службаКопий.BackupNow(new BackupOptions(каталогКопий));

        Assert.True(итог.IsSuccess, итог.Error);
        Assert.True(File.Exists(итог.ArchivePath));

        // 3. После копирования в базу вносятся новые данные: восстановленный
        // снимок обязан их НЕ содержать (копия — согласованный снимок на момент
        // копирования).
        await using (var сессия = базаСлужбы.СоздатьСессию())
        {
            await сессия.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, is_active, legal_zip, legal_str1, address_main_type) " +
                "VALUES (0, 'ООО «После копии»', 1, '101000', 'ООО «После копии»', 0);");
        }

        // 4. Восстановление в ОТДЕЛЬНЫЙ контур (отдельный файл базы).
        var восстановленная = Path.Combine(_каталог, "restored", "picket302.db");
        Directory.CreateDirectory(Path.GetDirectoryName(восстановленная)!);

        var службаВосстановления = new BackupService(восстановленная);

        var исходВосстановления = службаВосстановления.RestoreFromArchive(
            итог.ArchivePath!, password: string.Empty);

        Assert.True(исходВосстановления.IsSuccess, исходВосстановления.Error);

        // 5. Сравнение данных и пакетов шаблонов.
        await using var соединение = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = восстановленная }.ToString());

        await соединение.OpenAsync();

        var контрагентов = await соединение.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM counterparties;");

        Assert.Equal(1, контрагентов);

        var наименование = await соединение.ExecuteScalarAsync<string>(
            "SELECT name FROM counterparties LIMIT 1;");

        Assert.Equal("ООО «Общая»", наименование);

        var пакетов = await соединение.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM shared_template_packages;");

        Assert.Equal(1, пакетов);

        var имяПакета = await соединение.ExecuteScalarAsync<string>(
            "SELECT name FROM shared_template_packages LIMIT 1;");

        Assert.Equal("Бланк", имяПакета);
    }

    [Fact]
    public async Task Планировщик_Создаёт_Копию_И_Не_Дублирует_За_День()
    {
        var каталогСлужбы = Path.Combine(_каталог, "service2");
        Directory.CreateDirectory(каталогСлужбы);

        var параметры = new ServiceOptions { DataDirectory = каталогСлужбы };
        var базаСлужбы = new ServerDatabase(параметры.DatabasePath);

        await базаСлужбы.ПодготовитьAsync();

        var записи = new List<string>();

        using var планировщик = new Picket302.Service.Hosting.ServerBackupScheduler(
            параметры.DatabasePath,
            параметры.BackupsDirectory,
            записи.Add,
            сразу: true);

        var копииДо = Directory.GetFiles(параметры.BackupsDirectory, "*.zip");

        Assert.NotEmpty(копииДо);

        // Повторный запуск в тот же день не создаёт вторую копию.
        планировщик.ВыполнитьПоРасписаниюСинхронно();

        var копииПосле = Directory.GetFiles(параметры.BackupsDirectory, "*.zip");

        Assert.Equal(копииДо.Length, копииПосле.Length);
    }
}
