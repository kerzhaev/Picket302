using Microsoft.Win32;
using Picket302.ComAutomation;
using Xunit;

namespace Picket302.Tests.ComAutomation;

/// <summary>
/// Тесты регистрации COM-сервера в ветке классов (ТЗ §9.3, функция 114).
/// </summary>
/// <remarks>
/// Регистрация проверяется в изолированной ветке
/// <c>HKCU\Software\Picket302.Tests\Classes</c>: рабочая регистрация
/// <c>HKCU\Software\Classes</c> тесты не затрагивают. Права администратора не
/// требуются — регистрация выполняется в ветке пользователя.
/// </remarks>
public sealed class ComRegistrationTests : IDisposable
{
    private const string ТестовыйКорень = @"Software\Picket302.Tests\Classes";
    private const string ТестовыйКореньПродукта = @"Software\Picket302.Tests";

    private readonly ComRegistration _регистрация;

    /// <summary>
    /// Создаёт тест и подготовленную регистрацию с тестовыми путями.
    /// </summary>
    public ComRegistrationTests()
    {
        _регистрация = new ComRegistration(
            ТестовыйКорень,
            Path.Combine(Path.GetTempPath(), "Picket302.ComAutomation.exe"),
            Path.Combine(Path.GetTempPath(), "Picket302.ComAutomation.tlb"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _регистрация.Unregister();

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ТестовыйКореньПродукта, throwOnMissingSubKey: false);
        }
        catch (Exception)
        {
            // Очистка тестовой ветки не влияет на результат проверки.
        }
    }

    [Fact]
    public void Регистрация_Создаёт_ProgId_Clsid_AppId_Интерфейс_И_Библиотеку_Типов()
    {
        _регистрация.Register();

        var progId = $@"{ТестовыйКорень}\{ComAutomationIds.ProgId}";
        var clsid = $@"{ТестовыйКорень}\CLSID\{{{ComAutomationIds.ClassId}}}";
        var appId = $@"{ТестовыйКорень}\AppID\{{{ComAutomationIds.ClassId}}}";
        var интерфейс = $@"{ТестовыйКорень}\Interface\{{{ComAutomationIds.InterfaceId}}}";
        var библиотека = $@"{ТестовыйКорень}\TypeLib\{{{ComAutomationIds.TypeLibId}}}\{ComAutomationIds.TypeLibVersion}\0\win64";

        Assert.True(ComRegistration.KeyExists(progId));
        Assert.True(ComRegistration.KeyExists(clsid));
        Assert.True(ComRegistration.KeyExists($@"{clsid}\LocalServer32"));
        Assert.True(ComRegistration.KeyExists(appId));
        Assert.True(ComRegistration.KeyExists(интерфейс));
        Assert.True(ComRegistration.KeyExists(библиотека));

        Assert.Equal(
            $"{{{ComAutomationIds.ClassId}}}",
            ComRegistration.ReadValue(progId, "CLSID"));

        Assert.Equal(
            $"{{{ComAutomationIds.ClassId}}}",
            ComRegistration.ReadValue(clsid, "AppID"));

        Assert.Equal(
            $"{{{ComAutomationIds.ClassId}}}",
            ComRegistration.ReadValue(appId, "CLSID"));

        Assert.Equal(
            _регистрация.ServerPath,
            ComRegistration.ReadValue($@"{clsid}\LocalServer32"));

        Assert.Equal(
            "oleaut32.dll",
            ComRegistration.ReadValue(интерфейс, "ProxyStub32"));

        // Ссылка на библиотеку типов в ветке интерфейса указывает на
        // собранный файл PrintEnvelopePro.tlb: без неё невозможен экспорт
        // сведений о типе (ITypeInfo) для клиента.
        Assert.Equal(
            _регистрация.TypeLibraryPath,
            ComRegistration.ReadValue(интерфейс, "TypeLib"));

        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, ComRegistration.TypeLibraryFileName)),
            $"Файл библиотеки типов '{ComRegistration.TypeLibraryFileName}' не поставляется вместе с программой.");

        Assert.Equal(
            _регистрация.TypeLibraryPath,
            ComRegistration.ReadValue(библиотека));
    }

    [Fact]
    public void Регистрация_Библиотеки_Типов_Создаёт_Ветку_Версии_22_И_Ссылку_В_Интерфейсе()
    {
        _регистрация.RegisterTypeLibrary();

        var версия = $@"{ТестовыйКорень}\TypeLib\{{{ComAutomationIds.TypeLibId}}}\{ComAutomationIds.TypeLibVersion}";
        var интерфейс = $@"{ТестовыйКорень}\Interface\{{{ComAutomationIds.InterfaceId}}}";

        Assert.Equal("2.2", ComAutomationIds.TypeLibVersion);
        Assert.True(ComRegistration.KeyExists(версия));
        Assert.True(ComRegistration.KeyExists($@"{версия}\0\win64"));
        Assert.True(ComRegistration.KeyExists($@"{версия}\0\win32"));

        Assert.Equal(
            _регистрация.TypeLibraryPath,
            ComRegistration.ReadValue($@"{версия}\0\win64"));

        Assert.Equal(
            _регистрация.TypeLibraryPath,
            ComRegistration.ReadValue($@"{версия}\0\win32"));

        Assert.Equal(
            _регистрация.TypeLibraryPath,
            ComRegistration.ReadValue(интерфейс, "TypeLib"));
    }

    [Fact]
    public void Удаление_Регистрации_Библиотеки_Типов_Убирает_Ветку_И_Ссылку()
    {
        _регистрация.RegisterTypeLibrary();

        var версия = $@"{ТестовыйКорень}\TypeLib\{{{ComAutomationIds.TypeLibId}}}";
        var интерфейс = $@"{ТестовыйКорень}\Interface\{{{ComAutomationIds.InterfaceId}}}";

        Assert.True(ComRegistration.KeyExists(версия));

        _регистрация.UnregisterTypeLibrary();

        Assert.False(ComRegistration.KeyExists(версия));
        Assert.Null(ComRegistration.ReadValue(интерфейс, "TypeLib"));
    }

    [Fact]
    public void Удаление_Регистрации_Убирает_Созданные_Ключи()
    {
        _регистрация.Register();

        var progId = $@"{ТестовыйКорень}\{ComAutomationIds.ProgId}";

        Assert.True(ComRegistration.KeyExists(progId));

        _регистрация.Unregister();

        Assert.False(ComRegistration.KeyExists(progId));
        Assert.False(ComRegistration.KeyExists($@"{ТестовыйКорень}\CLSID\{{{ComAutomationIds.ClassId}}}"));
        Assert.False(ComRegistration.KeyExists($@"{ТестовыйКорень}\AppID\{{{ComAutomationIds.ClassId}}}"));
        Assert.False(ComRegistration.KeyExists($@"{ТестовыйКорень}\Interface\{{{ComAutomationIds.InterfaceId}}}"));
        Assert.False(ComRegistration.KeyExists($@"{ТестовыйКорень}\TypeLib\{{{ComAutomationIds.TypeLibId}}}"));
    }

    [Fact]
    public void Регистрация_Не_Требует_Прав_Администратора_И_Использует_Ветку_Пользователя()
    {
        _регистрация.Register();

        using var ветка = Registry.CurrentUser.OpenSubKey(ТестовыйКорень);

        Assert.NotNull(ветка);
    }

    [Fact]
    public void Повторная_Регистрация_Не_Приводит_К_Ошибке()
    {
        _регистрация.Register();
        _регистрация.Register();

        Assert.True(ComRegistration.KeyExists($@"{ТестовыйКорень}\{ComAutomationIds.ProgId}"));
    }
}
