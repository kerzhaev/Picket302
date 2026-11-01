using System.Globalization;

using Dapper;
using Microsoft.Data.Sqlite;

using Picket302.App.UI.Forms;
using Picket302.Domain.Backup;
using Picket302.Infrastructure.Backup;

using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверки автоматического архивирования по расписанию, политики хранения
/// архивов и восстановления базы данных из архива
/// (ТЗ §8.1, функции 103–104; §8.11, функции 128–129; §8.12, функция 161;
/// §10.4, §10.5).
/// </summary>
/// <remarks>
/// Проверки двух режимов хранения и безопасного отказа включены в этот же
/// файл: по заданию допускается один файл тестов из двух указанных.
/// </remarks>
public sealed class ScheduledBackupTests : IDisposable
{
    private readonly string _каталог;
    private readonly string _папкаАрхивов;
    private readonly string _путьНастроек;

    /// <summary>Создаёт тест с временным каталогом, папкой архивов и файлом настроек.</summary>
    public ScheduledBackupTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "Picket302ScheduleTests_" + Guid.NewGuid().ToString("N"));
        _папкаАрхивов = Path.Combine(_каталог, "backup");
        _путьНастроек = Path.Combine(_каталог, "schedule.json");

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

    /// <summary>
    /// Точки расписания по умолчанию: 09:00, 13:05, 16:05
    /// (ТЗ §8.1, функция 103; §10.4).
    /// </summary>
    [Fact]
    public void Расписание_ПоУмолчанию_Девять_Тринадцать_Шестнадцать()
    {
        Assert.Equal(3, ScheduledBackupService.ТочкиПоУмолчанию.Length);

        Assert.Equal(
            new TimeSpan(9, 0, 0),
            ScheduledBackupService.ТочкиПоУмолчанию[0]);

        Assert.Equal(
            new TimeSpan(13, 5, 0),
            ScheduledBackupService.ТочкиПоУмолчанию[1]);

        Assert.Equal(
            new TimeSpan(16, 5, 0),
            ScheduledBackupService.ТочкиПоУмолчанию[2]);
    }

    /// <summary>
    /// Служба без файла настроек использует точки расписания по умолчанию.
    /// </summary>
    [Fact]
    public void Служба_БезФайлаНастроек_БерётТочкиПоУмолчанию()
    {
        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        Assert.Equal(3, служба.Schedule.Точки.Count);

        for (var индекс = 0; индекс < ScheduledBackupService.ТочкиПоУмолчанию.Length; индекс++)
        {
            Assert.Equal(ScheduledBackupService.ТочкиПоУмолчанию[индекс], служба.Schedule.Точки[индекс]);
        }
    }

    /// <summary>
    /// Настроенное расписание сохраняется в файле настроек и читается обратно
    /// (ТЗ §8.11, функция 128: «Три настраиваемые точки архивации в сутки»).
    /// </summary>
    [Fact]
    public void ConfigureSchedule_СохраняетРасписание_ВФайлНастроек()
    {
        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        служба.ConfigureSchedule(new BackupSchedule(
        [
            new TimeSpan(7, 30, 0),
            new TimeSpan(12, 0, 0),
            new TimeSpan(18, 45, 0),
        ]));

        Assert.True(File.Exists(_путьНастроек));

        var чтение = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        Assert.Equal(
            new[]
            {
                new TimeSpan(7, 30, 0),
                new TimeSpan(12, 0, 0),
                new TimeSpan(18, 45, 0),
            },
            чтение.Schedule.Точки);
    }

    /// <summary>
    /// Более трёх точек расписания не принимается (ТЗ §10.4: «до трех точек в сутки»).
    /// </summary>
    [Fact]
    public void ConfigureSchedule_БолееТрёхТочек_Отказ()
    {
        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        Assert.Throws<ArgumentException>(() => служба.ConfigureSchedule(new BackupSchedule(
        [
            new TimeSpan(7, 0, 0),
            new TimeSpan(9, 0, 0),
            new TimeSpan(13, 0, 0),
            new TimeSpan(17, 0, 0),
        ])));
    }

    /// <summary>
    /// Запуск вне точек расписания архив не создаёт
    /// (ТЗ §8.1, функция 103: архивирование «в назначенные 09:00/13:05/16:05»).
    /// </summary>
    [Fact]
    public async Task RunScheduledBackup_ВнеРасписания_НеСоздаётАрхив()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        var момент = new DateTime(2026, 3, 10, 11, 17, 0, DateTimeKind.Local);
        var результат = служба.RunScheduledBackup(момент);

        Assert.True(результат.IsFailure);
        Assert.Equal(ScheduledBackupService.СообщениеВнеРасписания, результат.Error);
        Assert.Empty(Directory.EnumerateFiles(_папкаАрхивов, "Base_*.zip"));
    }

    /// <summary>
    /// В назначенную точку расписания архив создаётся (ТЗ §8.1, функция 103).
    /// </summary>
    [Fact]
    public async Task RunScheduledBackup_ВТочкеРасписания_СоздаётАрхив()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        var момент = new DateTime(2026, 3, 10, 13, 5, 0, DateTimeKind.Local);
        var результат = служба.RunScheduledBackup(момент);

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.NotNull(результат.ArchivePath);
        Assert.True(File.Exists(результат.ArchivePath!));
    }

    /// <summary>
    /// Имя создаваемого архива соответствует формату Base_ГГММДДЧЧММ.zip
    /// (ТЗ §7.8, §10.4).
    /// </summary>
    [Fact]
    public async Task Архив_Создаётся_СИменемBaseГГММДДЧЧММ()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        var результат = служба.RunNow();

        Assert.True(результат.IsSuccess, результат.Error);

        var имя = Path.GetFileName(результат.ArchivePath!);

        Assert.StartsWith(BackupOptions.ArchiveNamePrefix, имя, StringComparison.Ordinal);
        Assert.EndsWith(BackupOptions.ArchiveExtension, имя, StringComparison.Ordinal);

        var метка = имя[
            BackupOptions.ArchiveNamePrefix.Length..^BackupOptions.ArchiveExtension.Length];

        Assert.Equal(10, метка.Length);
        Assert.True(метка.All(char.IsAsciiDigit), $"Имя архива «{имя}» не соответствует формату Base_ГГММДДЧЧММ.zip.");

        // Имя совпадает с построенным по правилам доменного типа.
        var ожидаемое = BackupOptions.BuildArchiveFileName(
            new DateTime(
                int.Parse(метка[..2], CultureInfo.InvariantCulture),
                int.Parse(метка[2..4], CultureInfo.InvariantCulture),
                int.Parse(метка[4..6], CultureInfo.InvariantCulture),
                int.Parse(метка[6..8], CultureInfo.InvariantCulture),
                int.Parse(метка[8..10], CultureInfo.InvariantCulture),
                0,
                DateTimeKind.Local));

        Assert.Equal(ожидаемое, имя);
    }

    /// <summary>
    /// Недоступная целевая папка приводит к понятной ошибке и повтору через
    /// 10 минут (ТЗ §8.1, функция 103: «Недоступность целевой папки — повтор
    /// через 10 минут»).
    /// </summary>
    [Fact]
    public async Task НедоступнаяПапка_ОшибкаИПовторЧерезДесятьМинут()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var недоступнаяПапка = Path.Combine(_каталог, "нет-такой-папки");

        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, недоступнаяПапка, null),
            _путьНастроек);

        var момент = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Local);
        var результат = служба.RunScheduledBackup(момент);

        Assert.True(результат.IsFailure);
        Assert.NotNull(результат.Error);
        Assert.Contains("недоступна", результат.Error!, StringComparison.OrdinalIgnoreCase);

        // Повтор назначен через 10 минут после неудачной попытки.
        Assert.NotNull(служба.NextRetryAt);
        Assert.Equal(момент.AddMinutes(10), служба.NextRetryAt!.Value);
    }

    /// <summary>
    /// Повтор разрешён не ранее чем через 10 минут с последней неудачной попытки
    /// (ТЗ §8.1, функция 103).
    /// </summary>
    [Fact]
    public void ShouldRetry_СрабатываетЧерезДесятьМинут()
    {
        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        var последняяПопытка = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

        Assert.False(служба.ShouldRetry(последняяПопытка.AddMinutes(9), последняяПопытка));
        Assert.True(служба.ShouldRetry(последняяПопытка.AddMinutes(10), последняяПопытка));
        Assert.True(служба.ShouldRetry(последняяПопытка.AddMinutes(25), последняяПопытка));
    }

    /// <summary>
    /// Состояние повтора сохраняется в файле настроек (ТЗ §8.11, функция 128:
    /// «Данные: parameters (расписание, папка, срок)»).
    /// </summary>
    [Fact]
    public void ScheduleRetry_СохраняетМоментПовтора_ВФайлНастроек()
    {
        var служба = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        var момент = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
        служба.ScheduleRetry(момент);

        Assert.Equal(момент.AddMinutes(10), служба.NextRetryAt);

        var чтение = new ScheduledBackupService(
            ПутьБазы,
            new BackupService(ПутьБазы, ИзвестныеВерсии()),
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        Assert.NotNull(чтение.NextRetryAt);
        Assert.Equal(момент.AddMinutes(10), чтение.NextRetryAt!.Value.ToUniversalTime());
    }

    /// <summary>
    /// Восстановление на чистой базе: архив, созданный на другой машине,
    /// разворачивается на чистой установке без ручных правок
    /// (ТЗ §8.1, функция 104: «Архив, созданный на другой машине, разворачивается
    /// на чистой установке и открывается без ручных правок»; §10.5).
    /// </summary>
    [Fact]
    public async Task Восстановление_НаЧистойБазе_СоздаётФайлИВозвращаетДанные()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
            await база.Connection.ExecuteAsync(
                "INSERT INTO counterparties (type, name, address_main_type, is_vip, is_sender, is_active, is_deleted) "
                + "VALUES (0, 'ООО «Техно»', 1, 0, 0, 1, 0);");
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var архив = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));

        Assert.True(архив.IsSuccess, архив.Error);

        // Чистая установка: файла базы нет.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(ПутьБазы);
        Assert.False(File.Exists(ПутьБазы));

        var восстановитель = new RestoreService(ПутьБазы, службаАрхива, ИзвестныеВерсии());
        var итог = восстановитель.Restore(архив.ArchivePath!, null);

        Assert.True(итог.IsSuccess, итог.Error);
        Assert.True(File.Exists(ПутьБазы));

        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы, сМиграциями: false))
        {
            var имя = await база.Connection.QuerySingleAsync<string>("SELECT name FROM counterparties;");
            Assert.Equal("ООО «Техно»", имя);
        }
    }

    /// <summary>
    /// Более новая версия схемы архива приводит к отказу восстановления
    /// (ТЗ §8.1, функция 104: «версия схемы новее программы — отказ»).
    /// </summary>
    [Fact]
    public async Task Восстановление_СхемаНовееПрограммы_Отказ()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var архив = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));

        Assert.True(архив.IsSuccess, архив.Error);

        // Программе не известна ни одна версия схемы: архив с любой версией
        // схемы считается более новым.
        var восстановитель = new RestoreService(ПутьБазы, службаАрхива, Array.Empty<string>());
        var отказ = восстановитель.Restore(архив.ArchivePath!, null);

        Assert.True(отказ.IsFailure);
        Assert.NotNull(отказ.Error);
        Assert.Contains(
            "Версия схемы архива новее версии программы",
            отказ.Error!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Предпросмотр архива возвращает дату, версию схемы и объём
    /// (ТЗ §10.5: «предпросмотр (дата, версия схемы, объем)»).
    /// </summary>
    [Fact]
    public async Task Предпросмотр_ВозвращаетДатуВерсиюСхемыИОбъём()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var архив = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));

        Assert.True(архив.IsSuccess, архив.Error);

        var восстановитель = new RestoreService(ПутьБазы, службаАрхива, ИзвестныеВерсии());
        var сведения = восстановитель.Preview(архив.ArchivePath!);

        Assert.NotNull(сведения);
        Assert.Equal(Path.GetFileName(архив.ArchivePath!), сведения!.FileName);
        Assert.Equal(архив.ArchivePath, сведения.FullPath);
        Assert.True(сведения.SizeInBytes > 0);
        Assert.False(сведения.IsPasswordProtected);
        Assert.NotNull(сведения.CreatedAtLocal);
        Assert.NotEqual(default, сведения.CreatedAtUtc);
    }

    /// <summary>
    /// Архив под паролем помечается в предпросмотре как защищённый, а
    /// неверный пароль не изменяет текущую базу (ТЗ §10.5).
    /// </summary>
    [Fact]
    public async Task Предпросмотр_АрхиваПодПаролем_ПомечаетЗащищённым()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var архив = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов, "секретный-пароль"));

        Assert.True(архив.IsSuccess, архив.Error);

        var восстановитель = new RestoreService(ПутьБазы, службаАрхива, ИзвестныеВерсии());
        var сведения = восстановитель.Preview(архив.ArchivePath!);

        Assert.NotNull(сведения);
        Assert.True(сведения!.IsPasswordProtected);

        var неверныйПароль = восстановитель.Restore(архив.ArchivePath!, "неверный-пароль");

        Assert.True(неверныйПароль.IsFailure);
        Assert.NotNull(неверныйПароль.Error);
    }

    /// <summary>
    /// Повреждённый архив не изменяет текущую базу (ТЗ §8.1, функция 104).
    /// </summary>
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

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var восстановитель = new RestoreService(ПутьБазы, службаАрхива, ИзвестныеВерсии());
        var итог = восстановитель.Restore(повреждённый, null);

        Assert.True(итог.IsFailure);
        Assert.NotNull(итог.Error);

        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы, сМиграциями: false))
        {
            var количество = await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM counterparties;");
            Assert.Equal(1, количество);
        }
    }

    /// <summary>
    /// Восстановление предлагает перезапуск программы (ТЗ §10.5: «предложение
    /// перезапуска»).
    /// </summary>
    [Fact]
    public async Task Восстановление_ПредлагаетПерезапуск()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());
        var архив = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));

        Assert.True(архив.IsSuccess, архив.Error);

        var восстановитель = new RestoreService(ПутьБазы, службаАрхива, ИзвестныеВерсии());

        Assert.True(восстановитель.RestartRequired);

        var итог = восстановитель.Restore(архив.ArchivePath!, null);

        Assert.True(итог.IsSuccess, итог.Error);
        Assert.True(итог.RestartRequired);
    }

    /// <summary>
    /// Режим «за каждый день» удаляет копии старше срока хранения и сохраняет
    /// копии в пределах срока (ТЗ §8.12, функция 161; §10.4).
    /// </summary>
    [Fact]
    public async Task Политика_РежимЗаКаждыйДень_УдаляетКопииСтаршеСрока()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());

        var старый = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(старый.IsSuccess, старый.Error);

        var свежий = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(свежий.IsSuccess, свежий.Error);

        // Момент создания прежней копии отнесён назад за пределы срока хранения.
        var список = службаАрхива.ListArchives(_папкаАрхивов);
        var прежний = список.Single(архив => архив.FullPath == старый.ArchivePath);

        var соСдвигом = список
            .Select(архив => архив.FullPath == старый.ArchivePath
                ? архив with { CreatedAt = DateTime.UtcNow.AddDays(-40) }
                : архив)
            .ToList();

        var политика = new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null);
        var службаПолитики = new BackupPolicyService();

        var итог = службаПолитики.Apply(политика, соСдвигом, свежий.ArchivePath);

        Assert.True(итог.IsSuccess, итог.Error);
        Assert.False(File.Exists(прежний.FullPath));
        Assert.True(File.Exists(свежий.ArchivePath!));
        Assert.Contains(прежний.FullPath, итог.DeletedFiles);
    }

    /// <summary>
    /// Режим «только последняя копия» удаляет предыдущую копию только после
    /// успешного создания новой (ТЗ §8.12, функция 161).
    /// </summary>
    [Fact]
    public async Task Политика_РежимТолькоПоследняяКопия_УдаляетПредыдущуюПослеУспеха()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());

        var предыдущий = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(предыдущий.IsSuccess, предыдущий.Error);

        var новый = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(новый.IsSuccess, новый.Error);

        var политика = new BackupPolicy(BackupRetentionMode.LastOnly, 30, _папкаАрхивов, null);
        var службаПолитики = new BackupPolicyService();

        var список = службаАрхива.ListArchives(_папкаАрхивов);
        var итог = службаПолитики.Apply(политика, список, новый.ArchivePath);

        Assert.True(итог.IsSuccess, итог.Error);
        Assert.False(File.Exists(предыдущий.ArchivePath!));
        Assert.True(File.Exists(новый.ArchivePath!));
        Assert.Contains(предыдущий.ArchivePath!, итог.DeletedFiles);
    }

    /// <summary>
    /// Безопасный отказ: сбой создания новой копии не удаляет предыдущую
    /// (ТЗ §8.12, функция 161: «Ошибка создания новой копии в режиме LastOnly
    /// не удаляет предыдущую»).
    /// </summary>
    [Fact]
    public async Task Политика_РежимТолькоПоследняяКопия_ПриСбоеСохраняетПредыдущую()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());

        var предыдущий = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(предыдущий.IsSuccess, предыдущий.Error);

        var политика = new BackupPolicy(BackupRetentionMode.LastOnly, 30, _папкаАрхивов, null);
        var службаПолитики = new BackupPolicyService();

        var список = службаАрхива.ListArchives(_папкаАрхивов);

        // Новый архив не создан: очистка не выполняется.
        var итог = службаПолитики.Apply(политика, список, новыйАрхив: null);

        Assert.True(итог.IsFailure);
        Assert.NotNull(итог.Error);
        Assert.Empty(итог.DeletedFiles);
        Assert.True(File.Exists(предыдущий.ArchivePath!));
    }

    /// <summary>
    /// Безопасный отказ действует и в режиме «за каждый день»: без новой копии
    /// прежние копии не удаляются (ТЗ §10.4: «предыдущая удаляется только после
    /// успешного создания новой»).
    /// </summary>
    [Fact]
    public async Task Политика_РежимЗаКаждыйДень_ПриСбоеНеУдаляетКопии()
    {
        await using (var база = await SqliteTestDatabase.CreateFileAsync(ПутьБазы))
        {
        }

        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());

        var прежний = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));
        Assert.True(прежний.IsSuccess, прежний.Error);

        var список = службаАрхива.ListArchives(_папкаАрхивов);
        var соСдвигом = список
            .Select(архив => архив with { CreatedAt = DateTime.UtcNow.AddDays(-40) })
            .ToList();

        var политика = new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null);
        var службаПолитики = new BackupPolicyService();

        var итог = службаПолитики.Apply(политика, соСдвигом, новыйАрхив: null);
        Assert.True(итог.IsFailure);
        Assert.Empty(итог.DeletedFiles);
        Assert.True(File.Exists(прежний.ArchivePath!));
    }

    /// <summary>
    /// Названия режимов хранения — русские, как в ТЗ (§8.12, функция 161:
    /// «Хранить данные за каждый день» и «Хранить только последнюю сохранённую
    /// базу»).
    /// </summary>
    [Fact]
    public void РежимыХранения_ИмеютРусскиеНазвания()
    {
        Assert.Equal(
            "за каждый день",
            BackupPolicyService.DescribeMode(BackupRetentionMode.DailyHistory));

        Assert.Equal(
            "только последняя копия",
            BackupPolicyService.DescribeMode(BackupRetentionMode.LastOnly));

        Assert.Equal(
            BackupRetentionMode.DailyHistory,
            BackupPolicyService.ParseMode("за каждый день"));

        Assert.Equal(
            BackupRetentionMode.LastOnly,
            BackupPolicyService.ParseMode("только последняя копия"));

        Assert.Throws<ArgumentException>(() => BackupPolicyService.ParseMode("неизвестный режим"));
    }

    /// <summary>
    /// Диалог архивирования собирается без обращения к базе данных и
    /// переиспользует переданные зависимости — поэтому его можно создать в
    /// тесте (ТЗ §10.4: «бэкап и восстановление — одна форма»).
    /// </summary>
    [Fact]
    public void ФормаАрхивации_СоздаётсяБезОбращенияКБазе()
    {
        var службаАрхива = new BackupService(ПутьБазы, ИзвестныеВерсии());

        var планировщик = new ScheduledBackupService(
            ПутьБазы,
            службаАрхива,
            new BackupPolicy(BackupRetentionMode.DailyHistory, 30, _папкаАрхивов, null),
            _путьНастроек);

        using var форма = new FormBackupRestore(
            ПутьБазы,
            службаАрхива,
            планировщик,
            new RestoreService(ПутьБазы, службаАрхива, ИзвестныеВерсии()));

        Assert.Equal(Path.GetFullPath(ПутьБазы), форма.ПутьБазы);
        Assert.Same(планировщик, форма.Планировщик);
    }

    /// <summary>
    /// Кнопка «глаз» переключает показ пароля архива: пароль маскируется
    /// системным символом и показывается текстом по требованию оператора
    /// (ТЗ §10.4: «Пароль архива — опционально, с кнопкой показа»).
    /// </summary>
    [Fact]
    public void ФормаАрхивации_КнопкаГлазПереключаетПоказПароля()
    {
        using var форма = new FormBackupRestore(ПутьБазы);

        Assert.False(форма.ПарольВидим);

        форма.ПереключитьПоказПароля();
        Assert.True(форма.ПарольВидим);

        форма.ПереключитьПоказПароля();
        Assert.False(форма.ПарольВидим);
    }
}
