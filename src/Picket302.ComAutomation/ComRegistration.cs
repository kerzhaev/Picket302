using Microsoft.Win32;

namespace Picket302.ComAutomation;

/// <summary>
/// Регистрация COM-сервера автоматизации в ветке <c>HKCU\Software\Classes</c>
/// (ТЗ §9.3, функция 114).
/// </summary>
/// <remarks>
/// <para>
/// Регистрация выполняется в ветке пользователя и потому не требует прав
/// администратора. Создаются ключи:
/// </para>
/// <list type="bullet">
/// <item><c>HKCU\Software\Classes\PEPro.Application</c> — ProgID: описание и ссылка на CLSID;</item>
/// <item><c>HKCU\Software\Classes\CLSID\{5715286E-F98F-4C1C-AC86-AB943AFAE004}</c> — класс:
/// описание, AppID и <c>LocalServer32</c> с путём к исполняемому файлу;</item>
/// <item><c>HKCU\Software\Classes\AppID\{5715286E-F98F-4C1C-AC86-AB943AFAE004}</c> — идентификатор
/// приложения;</item>
/// <item><c>HKCU\Software\Classes\Interface\{3A98FA89-27EE-4DDF-8F5D-ABC47EA942E6}</c> — интерфейс
/// <c>IPEProApplication</c> с прокси-стабом <c>oleaut32.dll</c>;</item>
/// <item><c>HKCU\Software\Classes\TypeLib\{22BC298D-DF0E-3C29-B589-7A50D3A8B871}\2.2</c> — библиотека
/// типов версии 2.2 (Includes-версия).</item>
/// </list>
/// <para>
/// Корневой путь ветки классов задаётся конструктором, поэтому регистрацию
/// можно выполнить в тестовой ветке (например,
/// <c>HKCU\Software\Picket302.Tests\Classes</c>), не затрагивая рабочую
/// регистрацию.
/// </para>
/// </remarks>
public sealed class ComRegistration
{
    /// <summary>Корневой путь ветки классов по умолчанию.</summary>
    public const string DefaultClassesRoot = @"Software\Classes";

    /// <summary>
    /// CLSID универсального маршалера автоматизации oleaut32.dll
    /// ({00020424-0000-0000-C000-000000000046}) для ветки ProxyStubClsid32
    /// интерфейса (F-E19).
    /// </summary>
    public const string ProxyStubClsid32 = "{00020424-0000-0000-C000-000000000046}";

    /// <summary>
    /// Число методов интерфейса IPEProApplication по IDL (F-E19).
    /// </summary>
    public const string NumMethodsIPEProApplication = "11";

    /// <summary>Идентификатор приложения (AppID) сервера автоматизации.</summary>
    public const string AppId = ComAutomationIds.ClassId;

    /// <summary>Описание сервера автоматизации.</summary>
    public const string ServerDescription = "Picket302 — COM-сервер автоматизации";

    /// <summary>
    /// Имя файла библиотеки типов, который копируется в каталог сборки
    /// COM-сервера и регистрируется в ветке классов.
    /// </summary>
    public const string TypeLibraryFileName = "PrintEnvelopePro.tlb";

    /// <summary>
    /// Имя исполняемого файла COM-сервера автоматизации.
    /// </summary>
    public const string ServerFileName = "Picket302.ComAutomation.exe";

    private readonly string _classesRoot;
    private readonly string _serverPath;
    private readonly string _typeLibraryPath;

    /// <summary>
    /// Создаёт регистрацию COM-сервера.
    /// </summary>
    /// <param name="classesRoot">
    /// Путь к ветке классов относительно HKCU; по умолчанию —
    /// <c>Software\Classes</c>.
    /// </param>
    /// <param name="serverPath">
    /// Путь к исполняемому файлу сервера; по умолчанию — исполняемый файл
    /// текущего процесса.
    /// </param>
    /// <param name="typeLibraryPath">
    /// Путь к файлу библиотеки типов; по умолчанию —
    /// <c>Picket302.ComAutomation.tlb</c> рядом с исполняемым файлом.
    /// </param>
    public ComRegistration(
        string? classesRoot = null,
        string? serverPath = null,
        string? typeLibraryPath = null)
    {
        _classesRoot = string.IsNullOrWhiteSpace(classesRoot)
            ? DefaultClassesRoot
            : classesRoot.TrimEnd('\\');

        _serverPath = string.IsNullOrWhiteSpace(serverPath)
            ? ResolveServerPath()
            : Path.GetFullPath(serverPath);

        _typeLibraryPath = string.IsNullOrWhiteSpace(typeLibraryPath)
            ? Path.Combine(AppContext.BaseDirectory, TypeLibraryFileName)
            : Path.GetFullPath(typeLibraryPath);
    }

    /// <summary>Корневой путь ветки классов относительно HKCU.</summary>
    public string ClassesRoot => _classesRoot;

    /// <summary>Путь к исполняемому файлу сервера.</summary>
    public string ServerPath => _serverPath;

    /// <summary>Путь к файлу библиотеки типов.</summary>
    public string TypeLibraryPath => _typeLibraryPath;

    /// <summary>
    /// Регистрирует COM-сервер в ветке классов.
    /// </summary>
    public void Register()
    {
        using (var progId = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\{ComAutomationIds.ProgId}", writable: true))
        {
            progId?.SetValue(string.Empty, ServerDescription, RegistryValueKind.String);
            // The canonical ProgID -> CLSID link is a named REG_SZ value. A
            // previous build also wrote a CLSID subkey; the compatibility
            // form is retained below while this canonical link is restored.
            progId?.SetValue("CLSID", FormatGuid(ComAutomationIds.ClassId), RegistryValueKind.String);
        }

        // CLSIDFromProgID on the Windows builds used by the application
        // resolves the traditional default value of the CLSID subkey. Keep
        // that compatibility form alongside the documented named value.
        using (var progIdClsid = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\{ComAutomationIds.ProgId}\CLSID",
                   writable: true))
        {
            progIdClsid?.SetValue(string.Empty, FormatGuid(ComAutomationIds.ClassId), RegistryValueKind.String);
        }

        using (var clsid = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\CLSID\{FormatGuid(ComAutomationIds.ClassId)}", writable: true))
        {
            clsid?.SetValue(string.Empty, ServerDescription, RegistryValueKind.String);
            clsid?.SetValue("AppID", FormatGuid(AppId), RegistryValueKind.String);
            // Both reverse links are named REG_SZ values on the CLSID key.
            // Write the canonical values even when an older installation
            // already contains the compatibility subkeys below.
            clsid?.SetValue("ProgID", ComAutomationIds.ProgId, RegistryValueKind.String);
            clsid?.SetValue("VersionIndependentProgID", ComAutomationIds.ProgId, RegistryValueKind.String);
        }

        // Keep the old default-value subkeys too. They are harmless and make
        // the registration readable by legacy COM tooling that expects the
        // reverse links in subkey form.
        using (var clsidProgId = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\CLSID\{FormatGuid(ComAutomationIds.ClassId)}\ProgID",
                   writable: true))
        {
            clsidProgId?.SetValue(string.Empty, ComAutomationIds.ProgId, RegistryValueKind.String);
        }

        using (var clsidVersionIndependentProgId = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\CLSID\{FormatGuid(ComAutomationIds.ClassId)}\VersionIndependentProgID",
                   writable: true))
        {
            clsidVersionIndependentProgId?.SetValue(string.Empty, ComAutomationIds.ProgId, RegistryValueKind.String);
        }

        using (var localServer = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\CLSID\{FormatGuid(ComAutomationIds.ClassId)}\LocalServer32",
                   writable: true))
        {
            localServer?.SetValue(string.Empty, Quote(_serverPath), RegistryValueKind.String);
            // ServerExecutable is passed as lpApplicationName to
            // CreateProcess, removing any ambiguity caused by spaces or
            // arguments in LocalServer32's command line.
            localServer?.SetValue("ServerExecutable", _serverPath, RegistryValueKind.String);
            localServer?.Flush();
        }

        using (var appId = Registry.CurrentUser.CreateSubKey($@"{_classesRoot}\AppID\{FormatGuid(AppId)}", writable: true))
        {
            appId?.SetValue(string.Empty, ServerDescription, RegistryValueKind.String);
            appId?.SetValue("CLSID", FormatGuid(ComAutomationIds.ClassId), RegistryValueKind.String);
        }

        using (var интерфейс = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\Interface\{FormatGuid(ComAutomationIds.InterfaceId)}",
                   writable: true))
        {
            интерфейс?.SetValue(string.Empty, "IPEProApplication", RegistryValueKind.String);

            // Универсальный маршалер автоматизации (F-E19): ветка
            // ProxyStubClsid32 с CLSID стандартного прокси oleaut32.dll и
            // число методов интерфейса по IDL. Значение «ProxyStub32 =
            // oleaut32.dll» не читается системой и маршалера не назначает.
            интерфейс?.SetValue("ProxyStubClsid32", "{00020424-0000-0000-C000-000000000046}", RegistryValueKind.String);
            интерфейс?.SetValue("NumMethods", NumMethodsIPEProApplication, RegistryValueKind.String);

            // Ссылка на библиотеку типов: без неё невозможен экспорт сведений
            // о типе (ITypeInfo) для интерфейса, а значит и работа клиента
            // через IDispatch с ранним связыванием. Значение — идентификатор
            // библиотеки типов (GUID), а не путь к файлу: путь хранится в
            // ветке TypeLib.
            интерфейс?.SetValue("TypeLib", FormatGuid(ComAutomationIds.TypeLibId), RegistryValueKind.String);
        }

        using (var класс = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\CLSID\{FormatGuid(ComAutomationIds.ClassId)}",
                   writable: true))
        {
            // Библиотека типов класса: нужна клиенту для получения сведений
            // о типах (IProvideClassInfo) и для позднего связывания.
            класс?.SetValue("TypeLib", FormatGuid(ComAutomationIds.TypeLibId), RegistryValueKind.String);
        }

        using (var версия = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\TypeLib\{FormatGuid(ComAutomationIds.TypeLibId)}\{ComAutomationIds.TypeLibVersion}",
                   writable: true))
        {
            версия?.SetValue(string.Empty, ServerDescription, RegistryValueKind.String);
        }

        RegisterTypeLibrary();
    }

    /// <summary>
    /// Регистрирует библиотеку типов версии 2.2 (Includes-версия) в ветке
    /// классов пользователя (ТЗ §9.3, функция 114).
    /// </summary>
    /// <remarks>
    /// Регистрируются ключи
    /// <c>TypeLib\{22BC298D-DF0E-3C29-B589-7A50D3A8B871}\2.2</c> с путём к
    /// собранному файлу <c>PrintEnvelopePro.tlb</c> для 32- и 64-разрядных
    /// клиентов, а также ссылка «TypeLib» в ветке интерфейса.
    /// </remarks>
    public void RegisterTypeLibrary()
    {
        using (var версия = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\TypeLib\{FormatGuid(ComAutomationIds.TypeLibId)}\{ComAutomationIds.TypeLibVersion}",
                   writable: true))
        {
            версия?.SetValue(string.Empty, ServerDescription, RegistryValueKind.String);
        }

        using (var платформа = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\TypeLib\{FormatGuid(ComAutomationIds.TypeLibId)}\{ComAutomationIds.TypeLibVersion}\0",
                   writable: true))
        {
            платформа?.SetValue(string.Empty, string.Empty, RegistryValueKind.String);
        }

        using (var win64 = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\TypeLib\{FormatGuid(ComAutomationIds.TypeLibId)}\{ComAutomationIds.TypeLibVersion}\0\win64",
                   writable: true))
        {
            win64?.SetValue(string.Empty, _typeLibraryPath, RegistryValueKind.String);
        }

        using (var win32 = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\TypeLib\{FormatGuid(ComAutomationIds.TypeLibId)}\{ComAutomationIds.TypeLibVersion}\0\win32",
                   writable: true))
        {
            win32?.SetValue(string.Empty, _typeLibraryPath, RegistryValueKind.String);
        }

        using (var интерфейс = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\Interface\{FormatGuid(ComAutomationIds.InterfaceId)}",
                   writable: true))
        {
            // Значение TypeLib — идентификатор библиотеки типов; путь к
            // файлу хранится в ветке TypeLib\{GUID}\{версия}\0\win64.
            интерфейс?.SetValue("TypeLib", FormatGuid(ComAutomationIds.TypeLibId), RegistryValueKind.String);
        }
    }

    /// <summary>
    /// Удаляет регистрацию библиотеки типов версии 2.2 из ветки классов.
    /// </summary>
    public void UnregisterTypeLibrary()
    {
        DeleteSubKeyTree($@"{_classesRoot}\TypeLib\{FormatGuid(ComAutomationIds.TypeLibId)}");

        using (var интерфейс = Registry.CurrentUser.CreateSubKey(
                   $@"{_classesRoot}\Interface\{FormatGuid(ComAutomationIds.InterfaceId)}",
                   writable: true))
        {
            интерфейс?.DeleteValue("TypeLib", throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Удаляет регистрацию COM-сервера из ветки классов.
    /// </summary>
    public void Unregister()
    {
        DeleteSubKeyTree($@"{_classesRoot}\{ComAutomationIds.ProgId}");
        DeleteSubKeyTree($@"{_classesRoot}\CLSID\{FormatGuid(ComAutomationIds.ClassId)}");
        DeleteSubKeyTree($@"{_classesRoot}\AppID\{FormatGuid(AppId)}");
        DeleteSubKeyTree($@"{_classesRoot}\Interface\{FormatGuid(ComAutomationIds.InterfaceId)}");
        DeleteSubKeyTree($@"{_classesRoot}\TypeLib\{FormatGuid(ComAutomationIds.TypeLibId)}");
    }

    /// <summary>
    /// Возвращает значение параметра ключа или <see langword="null"/>, если
    /// ключа либо параметра нет.
    /// </summary>
    /// <param name="keyPath">Путь к ключу относительно HKCU.</param>
    /// <param name="valueName">Имя параметра; пусто — значение по умолчанию.</param>
    /// <returns>Значение параметра либо <see langword="null"/>.</returns>
    public static string? ReadValue(string keyPath, string valueName = "")
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);

            if (key is null)
            {
                return null;
            }

            var значение = key.GetValue(valueName);

            return значение is null
                ? null
                : Convert.ToString(значение, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Проверяет наличие ключа в ветке классов.
    /// </summary>
    /// <param name="keyPath">Путь к ключу относительно HKCU.</param>
    /// <returns><see langword="true"/> — ключ существует.</returns>
    public static bool KeyExists(string keyPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);

            return key is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void DeleteSubKeyTree(string keyPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        catch (Exception)
        {
            // Отсутствующий ключ удаления не требует: операция идемпотентна.
        }
    }

    private static string FormatGuid(string value) => $"{{{value}}}";

    /// <summary>
    /// Заключает путь LocalServer32 в кавычки всегда: значение ветки читает
    /// SCM при запуске сервера, и кавычки делают разбор пути независимым от
    /// пробелов в каталоге установки (запуск из каталога без пробелов тоже
    /// остаётся корректным).
    /// </summary>
    private static string Quote(string path) => $"\"{path}\"";

    /// <summary>
    /// Определяет путь к исполняемому файлу COM-сервера.
    /// </summary>
    /// <remarks>
    /// Сервер регистрируется как отдельный out-of-proc процесс, поэтому в
    /// ветку <c>LocalServer32</c> должен попасть именно
    /// <c>Picket302.ComAutomation.exe</c>, а не исполняемый файл оболочки, из
    /// которой вызвана регистрация. Файл сервера поставляется рядом с
    /// оболочкой (ТЗ §8.9, функция 114).
    /// </remarks>
    /// <returns>Полный путь к исполняемому файлу сервера.</returns>
    private static string ResolveServerPath()
    {
        var собственный = Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(собственный)
            && string.Equals(
                Path.GetFileName(собственный),
                ServerFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(собственный);
        }

        var рядом = Path.Combine(AppContext.BaseDirectory, ServerFileName);

        if (File.Exists(рядом))
        {
            return Path.GetFullPath(рядом);
        }

        if (!string.IsNullOrWhiteSpace(собственный))
        {
            return Path.GetFullPath(собственный);
        }

        var модуль = System.Reflection.Assembly.GetEntryAssembly()?.Location;

        return string.IsNullOrWhiteSpace(модуль)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(модуль);
    }
}
