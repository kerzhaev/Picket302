using Picket302.Infrastructure.Logging;
using Picket302.Infrastructure.Localization;
using Picket302.Infrastructure.Update;
using Picket302.Infrastructure.Integration;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверки журнала приложения: независимые каналы и деградация в память
/// (ТЗ §10.3; §8.11, функция 111).
/// </summary>
public sealed class LogHubTests : IDisposable
{
    private readonly string _временнаяПапка =
        Path.Combine(Path.GetTempPath(), "Picket302LogHub_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Создаёт тестовый каталог журналов.
    /// </summary>
    public LogHubTests()
    {
        Directory.CreateDirectory(_временнаяПапка);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_временнаяПапка))
        {
            Directory.Delete(_временнаяПапка, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void КаналыЖурналаРаздельны()
    {
        using var журнал = new LogHub(_временнаяПапка);

        журнал.LogError(new InvalidOperationException("ошибка приложения"), "сбой");
        журнал.LogPrintSession(new PrintSessionLogRecord(
            "HP LaserJet Pro M402dn",
            "229x162mm",
            "Tray 1",
            "Simplex",
            "Envelope_C5_Custom.lbk",
            "229x162",
            "Landscape",
            "Tahoma",
            9.0,
            "ZIPcode",
            0.45,
            0.0,
            1.50,
            12,
            1,
            150,
            PrintSessionLogRecord.УспешныйСтатус,
            4.2));

        var ошибки = журнал.ReadRecent(LogChannel.App);
        var печать = журнал.ReadRecent(LogChannel.Print);

        Assert.Contains(ошибки, запись => запись.Contains("ошибка приложения", StringComparison.Ordinal));
        Assert.DoesNotContain(ошибки, запись => запись.Contains("Вывод на принтер", StringComparison.Ordinal));

        Assert.Contains(печать, запись => запись.Contains("Вывод на принтер: HP LaserJet Pro M402dn", StringComparison.Ordinal));
        Assert.Contains(печать, запись => запись.Contains("paper=229x162mm, source=Tray 1, duplex=Simplex", StringComparison.Ordinal));
        Assert.Contains(печать, запись => запись.Contains("Шрифт индекса: ZIPcode (0.45mm)", StringComparison.Ordinal));
        Assert.Contains(печать, запись => запись.Contains("Объектов: 12, страниц: 1, копий: 150", StringComparison.Ordinal));
        Assert.Contains(печать, запись => запись.Contains("Статус: УСПЕШНО, Затрачено времени: 4.2с", StringComparison.Ordinal));

        Assert.DoesNotContain(печать, запись => запись.Contains("ошибка приложения", StringComparison.Ordinal));
    }

    [Fact]
    public void ИменаФайловЖурналовСоответствуютСпецификации()
    {
        Assert.Equal("app-.log", LogHub.ИмяФайла(LogChannel.App));
        Assert.Equal("print-.log", LogHub.ИмяФайла(LogChannel.Print));
        Assert.Equal("audit-.log", LogHub.ИмяФайла(LogChannel.Audit));
        Assert.Equal("email-.log", LogHub.ИмяФайла(LogChannel.Email));
    }

    [Fact]
    public void НедоступнаяПапкаЖурналов_ДеградацияВПамятьБезИсключения()
    {
        var недоступныйПуть = Path.Combine(_временнаяПапка, "под_файл", "logs");

        File.WriteAllText(Path.Combine(_временнаяПапка, "под_файл"), "файл вместо папки");

        using var журнал = new LogHub(недоступныйПуть);

        Assert.True(журнал.IsDegraded);

        журнал.LogError(new InvalidOperationException("сбой"), "тест");

        var записи = журнал.ReadRecent(LogChannel.App);

        Assert.Contains(записи, запись => запись.Contains("сбой", StringComparison.Ordinal));
    }

    [Fact]
    public void ТекстДляПоддержкиСодержитСведенияОПрограмме()
    {
        using var журнал = new LogHub(_временнаяПапка);

        журнал.LogAudit("Изменение справочника", "Группы", "оператор");

        var текст = журнал.BuildSupportText(LogChannel.Audit);

        Assert.Contains("Журнал: Аудит", текст, StringComparison.Ordinal);
        Assert.Contains("Picket302", текст, StringComparison.Ordinal);
        Assert.Contains("Изменение справочника", текст, StringComparison.Ordinal);
    }
}

/// <summary>
/// Проверки менеджера локализации: переключение языка без перезапуска,
/// откат на русский, отклонение неполного словаря (ТЗ §8.11, функции 112
/// и 151).
/// </summary>
public sealed class LanguageManagerTests : IDisposable
{
    private readonly string _временнаяПапка =
        Path.Combine(Path.GetTempPath(), "Picket302Lang_" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_временнаяПапка))
        {
            Directory.Delete(_временнаяПапка, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ПереключениеЯзыкаМеняетСтрокиИНеТребуетПерезапуска()
    {
        ПодготовитьЯзыки();

        var менеджер = new LanguageManager(_временнаяПапка);

        Assert.Equal("ru", менеджер.CurrentCode);
        Assert.Equal("Файл", менеджер.GetString("Menu.File"));

        var событие = 0;

        менеджер.LanguageChanged += (_, _) => событие++;

        менеджер.SwitchLanguage("en");

        Assert.Equal(1, событие);
        Assert.Equal("en", менеджер.CurrentCode);
        Assert.Equal("File", менеджер.GetString("Menu.File"));
    }

    [Fact]
    public void ОтсутствующийКлючВозвращаетсяСвоимИменемИПишетсяВЖурнал()
    {
        ПодготовитьЯзыки();

        var записи = new List<string>();

        var менеджер = new LanguageManager(_временнаяПапка, записи.Add);

        Assert.Equal("Нет.Такого.Ключа", менеджер.GetString("Нет.Такого.Ключа"));
        Assert.Contains(записи, запись => запись.Contains("Нет.Такого.Ключа", StringComparison.Ordinal));
    }

    [Fact]
    public void ОтсутствующийФайлЯзыкаДаётОткатНаРусскийСПредупреждением()
    {
        ПодготовитьЯзыки();

        var записи = new List<string>();

        var менеджер = new LanguageManager(_временнаяПапка, записи.Add);

        менеджер.SwitchLanguage("de");

        Assert.Equal("ru", менеджер.CurrentCode);
        Assert.Contains(записи, запись => запись.Contains("de", StringComparison.Ordinal));
    }

    [Fact]
    public void НеполныйСловарьВалютыОтклоняетсяСПеречнемНедостающихКлючей()
    {
        var папкаВалют = Path.Combine(_временнаяПапка, "lng");

        Directory.CreateDirectory(папкаВалют);

        var путь = Path.Combine(папкаВалют, "Test.lng");

        File.WriteAllText(путь, "One=один;два;три\n");

        var менеджер = new LanguageManager(_временнаяПапка);

        var ошибка = Assert.Throws<InvalidDataException>(() => менеджер.LoadPack(путь));

        Assert.Contains("отклонён", ошибка.Message, StringComparison.Ordinal);
        Assert.Contains("Ten", ошибка.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void НовыйЯзыкСоздаётсяКопированиемШаблона()
    {
        ПодготовитьЯзыки();

        var менеджер = new LanguageManager(_временнаяПапка);

        var путь = менеджер.CreateLanguageFromTemplate("de", "Deutsch");

        Assert.True(File.Exists(путь));

        var набор = менеджер.LoadLanguage("de");

        Assert.NotNull(набор);
        Assert.Equal("de", набор!.Code);
        Assert.Equal("Deutsch", набор.Name);
    }

    [Fact]
    public void ПоставкаСодержитРусскийИАнглийскийЯзыки()
    {
        var каталог = Path.Combine(AppContext.BaseDirectory, LanguageManager.LanguageFolderName);

        Assert.True(Directory.Exists(каталог), "Каталог языковых файлов отсутствует рядом с программой.");

        Assert.True(File.Exists(Path.Combine(каталог, "ru.xml")), "Файл русского языка не поставляется.");
        Assert.True(File.Exists(Path.Combine(каталог, "en.xml")), "Файл английского языка не поставляется.");
    }

    [Fact]
    public void ПоставкаСодержитСловариВалют()
    {
        var каталог = Path.Combine(AppContext.BaseDirectory, LanguageManager.CurrencyFolderName);

        Assert.True(Directory.Exists(каталог), "Каталог словарей валют отсутствует рядом с программой.");

        foreach (var код in new[] { "Rub", "Byn", "Byr", "Uah", "usd", "Ron", "Kzt" })
        {
            Assert.True(
                File.Exists(Path.Combine(каталог, код + ".lng")),
                $"Словарь валюты {код}.lng не поставляется.");
        }
    }

    /// <summary>
    /// Готовит временный набор языковых файлов.
    /// </summary>
    private void ПодготовитьЯзыки()
    {
        var папка = Path.Combine(_временнаяПапка, LanguageManager.LanguageFolderName);

        Directory.CreateDirectory(папка);

        File.WriteAllText(
            Path.Combine(папка, "ru.xml"),
            """<?xml version="1.0" encoding="utf-8"?><language code="ru" name="Русский"><string name="Menu.File">Файл</string></language>""");

        File.WriteAllText(
            Path.Combine(папка, "en.xml"),
            """<?xml version="1.0" encoding="utf-8"?><language code="en" name="English"><string name="Menu.File">File</string></language>""");

        File.WriteAllText(
            Path.Combine(папка, "template.xml"),
            """<?xml version="1.0" encoding="utf-8"?><language code="template" name="Шаблон"><string name="Menu.File">Файл</string></language>""");
    }
}

/// <summary>
/// Проверки проверки обновлений (ТЗ §8.12, функция 163).
/// </summary>
public sealed class UpdateCheckerTests
{
    [Fact]
    public void РазборФайлаОбновлений_ЧитаетКлючи()
    {
        var параметры = UpdateChecker.Parse("version = 2.1.0\nurl=https://example.local/updates/setup.exe\nnotes=Исправления");

        Assert.Equal("2.1.0", параметры[UpdateChecker.VersionKey]);
        Assert.Equal("https://example.local/updates/setup.exe", параметры[UpdateChecker.UrlKey]);
        Assert.Equal("Исправления", параметры[UpdateChecker.NotesKey]);
    }

    [Fact]
    public void СравнениеВерсий_ОпределяетБолееНовую()
    {
        Assert.True(UpdateChecker.СравнитьВерсии("2.1.0", "2.0.9"));
        Assert.False(UpdateChecker.СравнитьВерсии("2.0.9", "2.1.0"));
        Assert.False(UpdateChecker.СравнитьВерсии("2.1.0", "2.1.0"));
    }

    [Fact]
    public async Task НедоступныйСерверОбновлений_ТихаяДеградация()
    {
        var записи = new List<string>();

        var проверка = new UpdateChecker(
            "https://updates.example.local/updates.txt",
            "2.0.0",
            (_, _) => throw new HttpRequestException("сервер недоступен"),
            записи.Add);

        var результат = await проверка.CheckForUpdatesAsync();

        Assert.False(результат.IsUpdateAvailable);
        Assert.Contains(записи, запись => запись.Contains("Сервер обновлений недоступен", StringComparison.Ordinal));
    }

    [Fact]
    public async Task НоваяВерсия_ВозвращаетсяКакДоступноеОбновление()
    {
        var проверка = new UpdateChecker(
            "https://updates.example.local/updates.txt",
            "2.0.0",
            (_, _) => Task.FromResult("version=2.1.0\nurl=https://example.local/setup.exe\nnotes=Изменения"));

        var результат = await проверка.CheckForUpdatesAsync();

        Assert.True(результат.IsUpdateAvailable);
        Assert.Equal("2.1.0", результат.Version);
        Assert.Equal("https://example.local/setup.exe", результат.DownloadUrl);
    }
}

/// <summary>
/// Проверки трекера отправлений Почты России (ТЗ §8.8, функция 110).
/// </summary>
public sealed class RussianPostTrackerTests
{
    private const string Партия = "П-0001";

    [Fact]
    public void РегистрацияПартии_ПроверяетИдентификаторыШпи()
    {
        var трекер = СоздатьТрекер();

        var исключение = Assert.Throws<ArgumentException>(() =>
            трекер.RegisterBatch(new RegisterBatchRequest(
                Партия,
                new DateTime(2026, 11, 1),
                "Краснодар",
                ["123"])));

        Assert.Contains("14 знаков", исключение.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void РегистрацияПартии_ПринимаетКорректныеИдентификаторы()
    {
        var трекер = СоздатьТрекер();

        var партия = трекер.RegisterBatch(new RegisterBatchRequest(
            Партия,
            new DateTime(2026, 11, 1),
            "Краснодар",
            ["RU1234567890AB", "RU1234567891AB"]));

        Assert.Equal(Партия, партия.BatchNumber);
        Assert.Equal(2, партия.TrackingIds.Count);
    }

    [Fact]
    public async Task ОпросСтатусов_ОтправленоПревращаетсяВПодтверждено()
    {
        var трекер = СоздатьТрекер(
            """{"items":[{"trackingId":"RU1234567890AB","status":"Отправлено","delivered":false}]}""");

        var статусы = await трекер.QueryStatusesAsync(Партия);

        var статус = Assert.Single(статусы);

        Assert.Equal("Отправлено", статус.Status);
        Assert.False(статус.Delivered);
        Assert.False(RussianPostTracker.ПодтвержденоВручение(статус));
    }

    [Fact]
    public async Task ОпросСтатусов_ВручениеПодтверждается()
    {
        var трекер = СоздатьТрекер(
            """{"items":[{"trackingId":"RU1234567890AB","status":"Вручено","delivered":true,"deliveredAt":"2026-11-02T08:00:00Z"}]}""");

        var статусы = await трекер.QueryStatusesAsync(Партия);

        var статус = Assert.Single(статусы);

        Assert.True(статус.Delivered);
        Assert.True(RussianPostTracker.ПодтвержденоВручение(статус));
        Assert.NotNull(статус.DeliveredAtUtc);
    }

    [Fact]
    public async Task ИсчерпаниеПопыток_ПишетВЖурнал()
    {
        var записи = new List<string>();

        var трекер = new RussianPostTracker(
            "https://api.example.local",
            new RussianPostCredentials("operator", null, null),
            (_, _, _) => throw new HttpRequestException("сервис недоступен"),
            записи.Add);

        var результат = await трекер.QueryStatusesWithRetriesAsync(Партия, попыток: 2);

        Assert.True(результат.IsFailure);
        Assert.Contains("Исчерпаны попытки", результат.Error, StringComparison.Ordinal);
        Assert.Contains(записи, запись => запись.Contains("Исчерпаны попытки", StringComparison.Ordinal));
    }

    [Fact]
    public void НормализацияИдентификатора_УбираетПробелыИДефисы()
    {
        Assert.Equal("RU1234567890AB", RussianPostTracker.НормализоватьИдентификатор("ru 1234-567890ab"));
    }

    /// <summary>
    /// Создаёт трекер с подставным ответом сервиса.
    /// </summary>
    private static RussianPostTracker СоздатьТрекер(string? ответ = null)
        => new(
            "https://api.example.local",
            new RussianPostCredentials("operator", null, null),
            (_, _, _) => Task.FromResult(ответ ?? "{\"items\":[]}"));
}
