using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Picket302.ComAutomation;
using Picket302.Domain.Contracts;
using Picket302.Domain.Exchange;
using Picket302.Infrastructure.Templates;
using Xunit;

namespace Picket302.Tests.ComAutomation;

/// <summary>
/// Тесты контракта COM-сервера автоматизации (ТЗ §9.1, функция 113; ТЗ §9.2,
/// таблица 9.2).
/// </summary>
public sealed class ComAutomationContractTests
{
    /// <summary>Корректный XML-контракт EnvelopesData с одним конвертом.</summary>
    private const string КорректныйXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <EnvelopesData>
          <EnvelopeData>
            <From>
              <Name>Иванов Иван Иванович</Name>
              <Address>г. Москва, ул. Тверская, д. 1</Address>
              <ZipCode>125009</ZipCode>
              <Phone>+7 495 000-00-01</Phone>
            </From>
            <To>
              <Name>ООО «Ромашка»</Name>
              <Address>г. Москва, ул. Пушкина, д. 5</Address>
              <ZipCode>101000</ZipCode>
              <Phone>+7 495 000-00-02</Phone>
            </To>
            <Number>17</Number>
            <Note>Тестовый конверт</Note>
          </EnvelopeData>
        </EnvelopesData>
        """;

    [Fact]
    public void Интерфейс_Объявляет_Ровно_23_Метода_С_Последовательными_Dispid()
    {
        var методы = typeof(IPEProApplication).GetMethods();

        Assert.Equal(ComAutomationIds.MethodCount, методы.Length);

        var ожидаемые = new (string Имя, int Dispid)[]
        {
            ("PrintFromFileEnvelopes", 0x60020000),
            ("PrintFromFileNotifications", 0x60020001),
            ("PrintFromFileInventories", 0x60020002),
            ("PrintFromFileRegisters", 0x60020003),
            ("PrintFromXmlEnvelopes", 0x60020004),
            ("PrintFromXmlNotifications", 0x60020005),
            ("PrintFromXmlInventories", 0x60020006),
            ("PrintFromXmlRegisters", 0x60020007),
            ("PrintEnvelope", 0x60020008),
            ("OpenDummyFormToSetAsForeground", 0x60020009),
            ("CloseDummyFormAndSetAsForeground", 0x6002000A),
            ("DatabaseIsLoaded", 0x6002000B),
            ("SafeLoadDatabase", 0x6002000C),
            ("GetFlagParseZipIndexFromAddress", 0x6002000D),
            ("SetFlagParseZipIndexFromAddress", 0x6002000E),
            ("GetFlagShowFormsAsModal", 0x6002000F),
            ("SetFlagShowFormsAsModal", 0x60020010),
            ("GetProductVersion", 0x60020011),
            ("GetAPIVersion", 0x60020012),
            ("GetRegisterTemplateNames", 0x60020013),
            ("PrintFromXmlRegistersWithoutForm", 0x60020014),
            ("GetWarningType", 0x60020015),
            ("GetStdRegisterTemplateNames", 0x60020016),
        };

        foreach (var (имя, dispid) in ожидаемые)
        {
            var метод = Assert.Single(методы, м => м.Name == имя);

            var атрибут = метод.GetCustomAttribute<DispIdAttribute>();

            Assert.NotNull(атрибут);
            Assert.Equal(dispid, атрибут!.Value);
        }

        // DISPID-ы идут подряд, без пропусков: порядковые номера 1..23
        // таблицы 9.1 ТЗ соответствуют значениям 0x60020000 + (номер − 1).
        var значения = методы
            .Select(м => м.GetCustomAttribute<DispIdAttribute>()!.Value)
            .OrderBy(значение => значение)
            .ToArray();

        for (var i = 1; i < значения.Length; i++)
        {
            Assert.Equal(значения[i - 1] + 1, значения[i]);
        }
    }

    [Fact]
    public void Сломанный_Xml_Возвращает_S_OK_И_Код_2()
    {
        var приложение = СоздатьПриложение(new FakeAutomationEnvironment());

        var hr = приложение.PrintFromXmlEnvelopes("<EnvelopesData><EnvelopeData><From>");

        Assert.Equal(0, hr);

        var hrКода = приложение.GetWarningType(out var код);

        Assert.Equal(0, hrКода);
        Assert.Equal(2, код);
        Assert.Equal(AutomationWarningCodes.InvalidXml, код);
    }

    [Fact]
    public void Без_Загруженной_Базы_Операции_Возвращают_Код_1()
    {
        var среда = new FakeAutomationEnvironment { IsDatabaseLoaded = false };
        var приложение = СоздатьПриложение(среда);

        Assert.Equal(0, приложение.PrintFromXmlEnvelopes(КорректныйXml));
        Assert.Equal(0, приложение.GetWarningType(out var кодПечати));

        Assert.Equal(0, приложение.PrintEnvelope("Иванов", "Москва", "125009", "ООО «Ромашка»", "Москва", "101000"));
        Assert.Equal(0, приложение.GetWarningType(out var кодКонверта));

        Assert.Equal(0, приложение.GetRegisterTemplateNames(out _));
        Assert.Equal(0, приложение.GetWarningType(out var кодШаблонов));

        Assert.Equal(1, кодПечати);
        Assert.Equal(1, кодКонверта);
        Assert.Equal(1, кодШаблонов);
    }

    [Fact]
    public void Отсутствующий_Параметр_Реестра_Читается_Как_Пустая_Строка()
    {
        using var область = new TestRegistryScope();

        var настройки = new PrintEnvelopeSettings(область.RootPath);

        var снимок = настройки.Read();

        Assert.Equal(string.Empty, снимок.FromName);
        Assert.Equal(string.Empty, снимок.FromAddr);
        Assert.Equal(string.Empty, снимок.FromIndex);
        Assert.Equal(string.Empty, снимок.ToName);
        Assert.Equal(string.Empty, снимок.ToAddr);
        Assert.Equal(string.Empty, снимок.ToIndex);

        Assert.Equal(string.Empty, настройки.ReadValue(PrintEnvelopeSettings.FromNameValue));
    }

    [Fact]
    public void Флаг_Модальных_Форм_По_Умолчанию_Выключен_И_Сохраняется()
    {
        using var область = new TestRegistryScope();

        var флаги = new AutomationFlagSettings(область.RootPath);

        // Слепой запуск: модальные окна по умолчанию выключены.
        Assert.False(флаги.GetShowFormsAsModal());

        var среда = new FakeAutomationEnvironment();
        var приложение = СоздатьПриложение(среда, область.RootPath);

        Assert.Equal(0, приложение.GetFlagShowFormsAsModal(out var поУмолчанию));
        Assert.False(поУмолчанию);

        Assert.Equal(0, приложение.SetFlagShowFormsAsModal(true, out var установленное));
        Assert.True(установленное);

        Assert.Equal(0, приложение.GetFlagShowFormsAsModal(out var послеУстановки));
        Assert.True(послеУстановки);

        Assert.True(флаги.GetShowFormsAsModal());

        // Значение переживает перезапуск: новый экземпляр читает тот же ключ.
        Assert.True(new AutomationFlagSettings(область.RootPath).GetShowFormsAsModal());
    }

    [Fact]
    public void Флаг_Разбора_Индекса_Читается_И_Записывается()
    {
        using var область = new TestRegistryScope();

        var флаги = new AutomationFlagSettings(область.RootPath);

        Assert.False(флаги.GetParseZipIndexFromAddress());

        флаги.SetParseZipIndexFromAddress(true);

        Assert.True(флаги.GetParseZipIndexFromAddress());

        var приложение = СоздатьПриложение(new FakeAutomationEnvironment(), область.RootPath);

        Assert.Equal(0, приложение.GetFlagParseZipIndexFromAddress(out var значение));
        Assert.True(значение);
    }

    [Fact]
    public void Версия_Продукта_И_Версия_Api_Возвращаются_В_Ожидаемом_Виде()
    {
        var приложение = СоздатьПриложение(new FakeAutomationEnvironment());

        Assert.Equal(0, приложение.GetProductVersion(out var версия));

        Assert.False(string.IsNullOrWhiteSpace(версия));
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", версия);

        Assert.Equal(0, приложение.GetAPIVersion(out var api));

        Assert.True(api > 0);
        Assert.Equal(ComAutomationIds.ApiVersion, api);
    }

    [Fact]
    public async Task Шаблоны_Реестров_Возвращаются_Из_Встроенного_Каталога()
    {
        var среда = new ComAutomationEnvironment();

        var стандартные = await среда.GetStandardRegisterTemplateNamesAsync();

        Assert.NotEmpty(стандартные);
        Assert.Contains("Реестр почтовых отправлений", стандартные);

        var каталог = new BuiltinTemplatesCatalog()
            .BuildLibrary()
            .Select(запись => запись.PackageName)
            .Where(имя => имя.Contains("реестр", StringComparison.OrdinalIgnoreCase))
            .OrderBy(имя => имя, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(каталог, стандартные.OrderBy(имя => имя, StringComparer.OrdinalIgnoreCase).ToArray());

        var приложение = СоздатьПриложение(new FakeAutomationEnvironment
        {
            IsDatabaseLoaded = true,
            UserTemplateNames = стандартные,
            StdTemplateNames = стандартные,
        });

        Assert.Equal(0, приложение.GetStdRegisterTemplateNames(out var имена));

        Assert.Equal(стандартные, имена);

        Assert.Equal(0, приложение.GetRegisterTemplateNames(out var пользовательские));

        Assert.Equal(стандартные, пользовательские);

        Assert.Equal(0, приложение.GetWarningType(out var код));

        Assert.Equal(0, код);
    }

    [Fact]
    public void Подготовка_Команды_Не_Исполняет_Операцию_А_Исполнение_Исполняет()
    {
        var среда = new FakeAutomationEnvironment { IsDatabaseLoaded = true };
        var подготовитель = new AutomationCommandPreparer();

        var команда = подготовитель.PreparePrintFromXml("Envelopes", КорректныйXml);

        Assert.IsType<PrintFromXmlCommand>(команда);
        Assert.Empty(среда.ВыполненныеОперации);

        var исполнитель = new AutomationDispatcher(среда);

        var результат = исполнителя_Результат(исполнитель, команда);

        Assert.True(результат.Succeeded);
        Assert.Equal(AutomationWarningCodes.Success, результат.WarningCode);
        Assert.NotEmpty(среда.ВыполненныеОперации);
        Assert.Contains("PrintEnvelopes", среда.ВыполненныеОперации);
    }

    [Fact]
    public void Подготовка_Команды_Печати_Из_Файла_Не_Обращается_К_Файлу()
    {
        var подготовитель = new AutomationCommandPreparer();

        var команда = подготовитель.PreparePrintFromFile("Registers", @"Z:\отсутствующий\файл.xml");

        Assert.IsType<PrintFromFileCommand>(команда);

        var исполнитель = new AutomationDispatcher(new FakeAutomationEnvironment());

        var результат = исполнителя_Результат(исполнитель, команда);

        Assert.False(результат.Succeeded);
        Assert.Equal(AutomationWarningCodes.DocumentError, результат.WarningCode);
    }

    [Fact]
    public void Способы_Подготовки_Соответствуют_Видам_Документов()
    {
        var подготовитель = new AutomationCommandPreparer();

        Assert.Equal(AutomationDocumentKind.Envelopes, AutomationCommandPreparer.ParseDocumentKind("Envelopes"));
        Assert.Equal(AutomationDocumentKind.Notifications, AutomationCommandPreparer.ParseDocumentKind("notifications"));
        Assert.Equal(AutomationDocumentKind.Inventories, AutomationCommandPreparer.ParseDocumentKind("Inventories"));
        Assert.Equal(AutomationDocumentKind.Registers, AutomationCommandPreparer.ParseDocumentKind("Registers"));

        Assert.Throws<ArgumentException>(() => AutomationCommandPreparer.ParseDocumentKind("Неизвестно"));

        Assert.IsType<PrintEnvelopeCommand>(подготовитель.PreparePrintEnvelope("а", "б", "в", "г", "д", "е"));
        Assert.IsType<GetRegisterTemplateNamesCommand>(подготовитель.PrepareGetRegisterTemplateNames());
        Assert.IsType<GetStdRegisterTemplateNamesCommand>(подготовитель.PrepareGetStdRegisterTemplateNames());
        Assert.IsType<SafeLoadDatabaseCommand>(подготовитель.PrepareSafeLoadDatabase());
        Assert.IsType<PrintRegisterWithoutFormCommand>(подготовитель.PreparePrintRegisterWithoutForm("xml", "шаблон", "путь"));
    }

    private static AutomationExecutionResult исполнителя_Результат(
        AutomationDispatcher исполнитель,
        AutomationCommand команда) =>
        исполнитель.Execute(команда);

    private static PEProApplication СоздатьПриложение(
        IAutomationEnvironment среда,
        string? кореньРеестра = null)
    {
        var флаги = new AutomationFlagSettings(кореньРеестра);

        return new PEProApplication(среда, флаги, new FakeForegroundWindowService());
    }
}

/// <summary>
/// Тестовая среда исполнения: состояние базы данных и перечень выполненных
/// операций проверяются без обращения к реальной базе и принтеру.
/// </summary>
internal sealed class FakeAutomationEnvironment : IAutomationEnvironment
{
    /// <summary>Признак загруженной базы данных.</summary>
    public bool IsDatabaseLoaded { get; set; }

    /// <summary>Показывать ли окна печати модально.</summary>
    public bool ShowFormsAsModal { get; set; }

    /// <summary>Результат безопасной загрузки базы данных.</summary>
    public bool LoadResult { get; set; } = true;

    /// <summary>Имена пользовательских шаблонов реестров.</summary>
    public IReadOnlyList<string> UserTemplateNames { get; set; } = Array.Empty<string>();

    /// <summary>Имена стандартных шаблонов реестров.</summary>
    public IReadOnlyList<string> StdTemplateNames { get; set; } = Array.Empty<string>();

    /// <summary>Перечень выполненных операций.</summary>
    public List<string> ВыполненныеОперации { get; } = new();

    /// <inheritdoc />
    public Task<bool> SafeLoadDatabaseAsync(CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add(nameof(SafeLoadDatabaseAsync));

        IsDatabaseLoaded = LoadResult;

        return Task.FromResult(LoadResult);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetUserRegisterTemplateNamesAsync(
        CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add(nameof(GetUserRegisterTemplateNamesAsync));

        return Task.FromResult(UserTemplateNames);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetStandardRegisterTemplateNamesAsync(
        CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add(nameof(GetStandardRegisterTemplateNamesAsync));

        return Task.FromResult(StdTemplateNames);
    }

    /// <inheritdoc />
    public Task<AutomationLettersResult> CreateLettersAsync(
        IReadOnlyList<PrintableEnvelope> envelopes,
        bool print,
        CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add(print ? "PrintEnvelopes" : "CreateLetters");

        var ids = envelopes.Select((_, индекс) => (long)(индекс + 1)).ToArray();

        return Task.FromResult(
            AutomationLettersResult.Ok(ids.Length, $"Создано писем: {ids.Length}.", ids));
    }

    /// <inheritdoc />
    public Task<AutomationLettersResult> ImportCounterpartiesAsync(
        IReadOnlyList<CounterpartyDraft> drafts,
        bool updateExisting,
        bool writeNote,
        CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add(nameof(ImportCounterpartiesAsync));

        return Task.FromResult(
            AutomationLettersResult.Ok(drafts.Count, $"Импортировано контрагентов: {drafts.Count}."));
    }

    /// <inheritdoc />
    public Task<AutomationPrintOutcome> PrintEnvelopesAsync(
        IReadOnlyList<PrintableEnvelope> envelopes,
        bool notification,
        CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add("PrintEnvelopes");

        return Task.FromResult(AutomationPrintOutcome.Ok($"Напечатано конвертов: {envelopes.Count}."));
    }

    /// <inheritdoc />
    public Task<AutomationPrintOutcome> PrintEnvelopeAsync(
        PrintableEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add("PrintEnvelope");

        return Task.FromResult(AutomationPrintOutcome.Ok("Конверт напечатан."));
    }

    /// <inheritdoc />
    public Task<AutomationPrintOutcome> PrintRegistersAsync(
        IReadOnlyList<RegisterData> registers,
        string templateName,
        string resultFullPath,
        CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add("PrintRegisters");

        return Task.FromResult(AutomationPrintOutcome.Ok($"Напечатано реестров: {registers.Count}."));
    }

    /// <inheritdoc />
    public Task<AutomationPrintOutcome> PrintInventoriesAsync(
        IReadOnlyList<InventoryData> inventories,
        CancellationToken cancellationToken = default)
    {
        ВыполненныеОперации.Add("PrintInventories");

        return Task.FromResult(AutomationPrintOutcome.Ok($"Напечатано описей: {inventories.Count}."));
    }
}

/// <summary>
/// Тестовая служба управления передним планом: окно не создаётся, вызовы
/// фиксируются.
/// </summary>
internal sealed class FakeForegroundWindowService : IForegroundWindowService
{
    /// <summary>Заголовок окна, возвращаемый при перехвате фокуса.</summary>
    public string ОткрытоеОкно { get; private set; } = string.Empty;

    /// <summary>Процесс, которому возвращён фокус.</summary>
    public string ЗакрытыйПроцесс { get; private set; } = string.Empty;

    /// <inheritdoc />
    public string OpenDummyFormToSetAsForeground()
    {
        ОткрытоеОкно = "Окно вызывающего процесса";

        return ОткрытоеОкно;
    }

    /// <inheritdoc />
    public void CloseDummyFormAndSetAsForeground(string process)
    {
        ЗакрытыйПроцесс = process;
    }
}

/// <summary>
/// Изолированная ветка реестра для тестов: создаётся под
/// <c>HKCU\Software\Picket302.Tests</c> и удаляется после проверки.
/// </summary>
internal sealed class TestRegistryScope : IDisposable
{
    /// <summary>Путь к тестовой ветке реестра относительно HKCU.</summary>
    public string RootPath { get; }

    /// <summary>Создаёт тестовую ветку реестра.</summary>
    public TestRegistryScope()
    {
        RootPath = $@"Software\Picket302.Tests\{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RootPath, throwOnMissingSubKey: false);
        }
        catch (Exception)
        {
            // Ветка теста удаляется «по возможности»: сбой очистки не должен
            // влиять на результат проверки.
        }
    }
}

