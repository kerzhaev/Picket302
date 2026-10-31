using System.Globalization;
using System.IO.Compression;
using Dapper;
using Picket302.Domain.Backup;
using Picket302.Infrastructure.Backup;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка архивирования и восстановления базы данных (ТЗ §8.1, функции
/// 103–104; §10.4, §10.5).
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly string _каталог;
    private readonly string _папкаАрхивов;

    /// <summary>Создаёт тест с временным каталогом и папкой архивов.</summary>
    public BackupServiceTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "Picket302BackupTests_" + Guid.NewGuid().ToString("N"));
        _папкаАрхивов = Path.Combine(_каталог, "backup");
        Directory.CreateDirectory(_папкаАрхивов);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временный каталог не удалён — на результат теста не влияет.
        }

        GC.SuppressFinalize(this);
    }

    private string ПутьБазы => Path.Combine(_каталог, "picket302.db");

    private static IReadOnlyList<string> ИзвестныеВерсии() =>
        new Picket302.Infrastructure.Data.MigrationRunner().Migrations
            .Select(миграция => миграция.Version)
            .ToList();

    [Fact]
    public async Task Архивирование_СоздаётZipСИменемBaseГГММДДЧЧММ()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            await база.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, address_main_type, is_vip, is_sender, is_active, is_deleted) "
                + "VALUES (0, 'ООО «Техно»', 1, 0, 0, 1, 0);");
        }

        var служба = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var результат = служба.BackupNow(new BackupOptions(_папкаАрхивов));

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.NotNull(результат.ArchivePath);

        var имя = Path.GetFileName(результат.ArchivePath!);

        Assert.StartsWith("Base_", имя, StringComparison.Ordinal);
        Assert.EndsWith(".zip", имя, StringComparison.Ordinal);

        // Имя вида Base_ГГММДДЧЧММ.zip: десять цифр после префикса.
        var цифры = имя["Base_".Length..^".zip".Length];

        Assert.Equal(10, цифры.Length);
        Assert.True(цифры.All(char.IsAsciiDigit), $"Имя архива «{имя}» не соответствует формату Base_ГГММДДЧЧММ.zip.");
    }

    [Fact]
    public async Task Архивирование_СоздаётСтандартныйZipСМанифестом()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var служба = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var результат = служба.BackupNow(new BackupOptions(_папкаАрхивов));

        Assert.True(результат.IsSuccess, результат.Error);

        using var архив = ZipFile.OpenRead(результат.ArchivePath!);

        Assert.NotNull(архив.GetEntry("manifest.json"));
        Assert.NotNull(архив.GetEntry(BackupService.DatabaseEntryName));

        // Архив читается штатным ZIP-архиватором — совместим со «Сжатыми папками».
        using var поток = архив.GetEntry(BackupService.DatabaseEntryName)!.Open();
        using var копия = new MemoryStream();
        поток.CopyTo(копия);

        Assert.True(копия.Length > 0);
    }

    [Fact]
    public async Task Восстановление_ВозвращаетДанныеБазы()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            await база.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, address_main_type, is_vip, is_sender, is_active, is_deleted) "
                + "VALUES (0, 'ООО «Техно»', 1, 0, 0, 1, 0);");
        }

        var служба = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var архив = служба.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(архив.IsSuccess, архив.Error);

        // Данные меняются после архивирования.
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            await база.Connection.ExecuteAsync("DELETE FROM counterparties;");
        }

        var восстановление = служба.RestoreFromArchive(архив.ArchivePath!, string.Empty);
        Assert.True(восстановление.IsSuccess, восстановление.Error);

        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            var имя = await база.Connection.QuerySingleAsync<string>("SELECT name FROM counterparties;");
            Assert.Equal("ООО «Техно»", имя);
        }

        // Страховочная копия текущего состояния создана.
        Assert.True(File.Exists(ПутьБазы + BackupService.SafetyCopySuffix));
    }

    [Fact]
    public async Task Восстановление_САрхивомПодПаролем_ТребуетВерныйПароль()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            await база.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, address_main_type, is_vip, is_sender, is_active, is_deleted) "
                + "VALUES (0, 'ООО «Техно»', 1, 0, 0, 1, 0);");
        }

        var служба = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var архив = служба.BackupNow(new BackupOptions(_папкаАрхивов, "секретный-пароль"));
        Assert.True(архив.IsSuccess, архив.Error);

        // Неверный пароль: текущая база не изменяется.
        var неверный = служба.RestoreFromArchive(архив.ArchivePath!, "неверный-пароль");

        Assert.True(неверный.IsFailure);
        Assert.Contains("пароль", неверный.Error!, StringComparison.OrdinalIgnoreCase);

        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            var количество = await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM counterparties;");
            Assert.Equal(1, количество);
        }

        // Верный пароль: восстановление выполняется.
        var верный = служба.RestoreFromArchive(архив.ArchivePath!, "секретный-пароль");
        Assert.True(верный.IsSuccess, верный.Error);
    }

    [Fact]
    public async Task Восстановление_ПовреждённогоАрхива_НеИзменяетБазу()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            await база.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, address_main_type, is_vip, is_sender, is_active, is_deleted) "
                + "VALUES (0, 'ООО «Техно»', 1, 0, 0, 1, 0);");
        }

        var повреждённый = Path.Combine(_папкаАрхивов, "Base_0000000000.zip");
        await File.WriteAllTextAsync(повреждённый, "это не архив");

        var служба = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var результат = служба.RestoreFromArchive(повреждённый, string.Empty);

        Assert.True(результат.IsFailure);
        Assert.NotNull(результат.Error);

        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            var количество = await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM counterparties;");
            Assert.Equal(1, количество);
        }
    }

    /// <summary>
    /// Версия схемы архива новее известной программе приводит к отказу
    /// (ТЗ §8.1, функция 104).
    /// </summary>
    [Fact]
    public async Task Восстановление_ВерсияСхемыНовееПрограммы_Отказ()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var служба = new BackupService(ПутьБазы, new[] { "001" });
        var архив = служба.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(архив.IsSuccess, архив.Error);

        var отказ = служба.RestoreFromArchive(архив.ArchivePath!, string.Empty);

        Assert.True(отказ.IsFailure);
        Assert.Contains("новее", отказ.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Архивирование_НедоступнаяПапка_ВозвращаетОшибку()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var служба = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var результат = служба.BackupNow(new BackupOptions(Path.Combine(_каталог, "нет-такой-папки")));

        Assert.True(результат.IsFailure);
        Assert.Contains("недоступна", результат.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ПеречислениеАрхивов_ВозвращаетСведенияОбАрхивах()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var служба = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var архив = служба.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(архив.IsSuccess, архив.Error);

        var список = служба.ListArchives(_папкаАрхивов);

        Assert.Single(список);
        Assert.Equal(Path.GetFileName(архив.ArchivePath!), список[0].FileName);
        Assert.True(список[0].SizeInBytes > 0);
        Assert.NotNull(список[0].SchemaVersion);
    }

    /// <summary>
    /// Прежний архив не затирается: при повторном архивировании в ту же минуту
    /// создаётся архив с новым именем (ТЗ §8.1, функция 103: «архив не затирает
    /// предыдущий при неудаче»).
    /// </summary>
    [Fact]
    public async Task Архивирование_ПовторныйАрхивНеЗатираетПрежний()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var служба = new BackupService(ПутьБазы, ИзвестныеВерсии());

        var первый = служба.BackupNow(new BackupOptions(_папкаАрхивов));
        var второй = служба.BackupNow(new BackupOptions(_папкаАрхивов));

        Assert.True(первый.IsSuccess, первый.Error);
        Assert.True(второй.IsSuccess, второй.Error);
        Assert.NotEqual(первый.ArchivePath, второй.ArchivePath);
        Assert.True(File.Exists(первый.ArchivePath!));
        Assert.True(File.Exists(второй.ArchivePath!));
    }

    /// <summary>
    /// Имя архива формируется по инвариантной культуре: в локалях с
    /// негригорианским календарём формат «ГГММДДЧЧММ» не нарушается
    /// (ТЗ §7.8: «Имя архива: Base_ГГММДДЧЧММ.zip»).
    /// </summary>
    [Fact]
    public void ИмяАрхива_НеЗависитОтКультуры()
    {
        var момент = new DateTime(2026, 10, 31, 9, 42, 0, DateTimeKind.Local);
        var исходнаяКультура = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var имя = BackupOptions.BuildArchiveFileName(момент);

            Assert.Equal("Base_2610310942.zip", имя);
        }
        finally
        {
            CultureInfo.CurrentCulture = исходнаяКультура;
        }
    }

    /// <summary>
    /// Архив со схемой старее известной программе версии восстанавливается:
    /// недостающие миграции применяются после восстановления (ТЗ §8.1,
    /// функция 104).
    /// </summary>
    [Fact]
    public async Task Восстановление_СхемаСтарееИзвестнойПрограмме_Допускается()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            // Схема архива — заведомо старая версия 001: она отсутствует в списке
            // версий, известных программе, но старее известной версии, поэтому
            // восстановление допустимо (сравнение числовое, а не проверка
            // вхождения в список).
            await база.Connection.ExecuteAsync(
                "DELETE FROM schema_version WHERE version <> '001';");
        }

        // Программа знает версии 002 и 004.
        var служба = new BackupService(ПутьБазы, new[] { "002", "004" });
        var архив = служба.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(архив.IsSuccess, архив.Error);

        var восстановление = служба.RestoreFromArchive(архив.ArchivePath!, string.Empty);

        Assert.True(восстановление.IsSuccess, восстановление.Error);
    }

    /// <summary>
    /// Служба, созданная без явного списка версий схемы, всё равно проверяет
    /// совместимость: версии берутся из встроенных миграций программы.
    /// </summary>
    [Fact]
    public async Task Восстановление_КонструкторПоУмолчанию_ПроверяетВерсиюСхемы()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            // Схема из «будущего»: версия новее любой известной программе.
            await база.Connection.ExecuteAsync(
                "INSERT INTO schema_version (version, description) VALUES ('999', 'Версия из будущего');");
        }

        var служба = new BackupService(ПутьБазы);
        var архив = служба.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(архив.IsSuccess, архив.Error);

        var отказ = служба.RestoreFromArchive(архив.ArchivePath!, string.Empty);

        Assert.True(отказ.IsFailure);
        Assert.Contains("новее", отказ.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Сообщение об отказе указывает требуемую версию схемы программы
    /// (ТЗ §8.1, функция 104: «отказ с указанием требуемой версии»).
    /// </summary>
    [Fact]
    public async Task Восстановление_ОтказУказываетТребуемуюВерсию()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            await база.Connection.ExecuteAsync(
                "INSERT INTO schema_version (version, description) VALUES ('999', 'Версия из будущего');");
        }

        var служба = new BackupService(ПутьБазы, new[] { "001", "002" });
        var архив = служба.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(архив.IsSuccess, архив.Error);

        var отказ = служба.RestoreFromArchive(архив.ArchivePath!, string.Empty);

        Assert.True(отказ.IsFailure);
        Assert.Contains("Требуемая версия", отказ.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Требуемая версия: 2", отказ.Error!, StringComparison.Ordinal);
    }
}
