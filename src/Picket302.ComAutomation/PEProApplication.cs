using System.Reflection;
using System.Runtime.InteropServices;

namespace Picket302.ComAutomation;

/// <summary>
/// Реализация COM-класса <c>PEProApplication</c> — точка входа автоматизации
/// Picket302 (ТЗ §9.1, функция 113).
/// </summary>
/// <remarks>
/// <para>
/// Класс реализует публичный контракт <see cref="IPEProApplication"/>: каждый
/// COM-метод лишь готовит команду (<c>Prepare*</c>) и передаёт её внутреннему
/// исполнителю <see cref="AutomationDispatcher"/>. Исключения не выходят за
/// границу COM-метода: ошибка кодируется значением
/// <see cref="GetWarningType"/>, HRESULT остаётся S_OK (ТЗ §9.2).
/// </para>
/// <para>
/// Класс создаётся фабрикой классов локального сервера
/// (<see cref="ComServerHost"/>) и регистрируется в
/// <c>HKCU\Software\Classes</c> под ProgID <c>PEPro.Application</c>
/// (ТЗ §9.3, функция 114).
/// </para>
/// </remarks>
[ComVisible(true)]
[Guid(ComAutomationIds.ClassId)]
[ProgId(ComAutomationIds.ProgId)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PEProApplication : IPEProApplication
{
    /// <summary>Значение HRESULT S_OK.</summary>
    private const int SOk = 0;

    private readonly AutomationCommandPreparer _preparer = new();
    private readonly AutomationDispatcher _dispatcher;
    private readonly IAutomationEnvironment _environment;
    private readonly AutomationFlagSettings _flags;
    private readonly IForegroundWindowService _foreground;
    private readonly object _sync = new();
    private int _warningType = AutomationWarningCodes.Success;

    /// <summary>
    /// Создаёт экземпляр с настройками по умолчанию: локальная база данных
    /// профиля пользователя, флаги из реестра, управление передним планом
    /// через вспомогательное окно.
    /// </summary>
    public PEProApplication()
        : this(
            new ComAutomationEnvironment(),
            new AutomationFlagSettings(),
            new ForegroundWindowService())
    {
    }

    /// <summary>
    /// Создаёт экземпляр с заданными зависимостями.
    /// </summary>
    /// <param name="environment">Среда исполнения (база данных, печать, шаблоны).</param>
    /// <param name="flags">Хранилище флагов автоматизации.</param>
    /// <param name="foreground">Управление передним планом окна.</param>
    public PEProApplication(
        IAutomationEnvironment environment,
        AutomationFlagSettings flags,
        IForegroundWindowService foreground)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(foreground);

        _dispatcher = new AutomationDispatcher(environment, flags.GetParseZipIndexFromAddress);
        _environment = environment;
        _flags = flags;
        _foreground = foreground;

        // Слепой запуск: окна печати по умолчанию не показываются, поэтому
        // вызывающий поток не блокируется (ТЗ §9.2).
        _environment.ShowFormsAsModal = flags.GetShowFormsAsModal();
    }

    /// <summary>
    /// Создаёт экземпляр с указанной службой управления передним планом и
    /// настройками по умолчанию.
    /// </summary>
    /// <param name="foreground">Управление передним планом окна.</param>
    public PEProApplication(IForegroundWindowService foreground)
        : this(new ComAutomationEnvironment(), new AutomationFlagSettings(), foreground)
    {
    }

    /// <inheritdoc />
    public int PrintFromFileEnvelopes(string filename) =>
        Execute(_preparer.PreparePrintFromFile(AutomationDocumentKind.Envelopes, filename));

    /// <inheritdoc />
    public int PrintFromFileNotifications(string filename) =>
        Execute(_preparer.PreparePrintFromFile(AutomationDocumentKind.Notifications, filename));

    /// <inheritdoc />
    public int PrintFromFileInventories(string filename) =>
        Execute(_preparer.PreparePrintFromFile(AutomationDocumentKind.Inventories, filename));

    /// <inheritdoc />
    public int PrintFromFileRegisters(string filename) =>
        Execute(_preparer.PreparePrintFromFile(AutomationDocumentKind.Registers, filename));

    /// <inheritdoc />
    public int PrintFromXmlEnvelopes(string xmlText) =>
        Execute(_preparer.PreparePrintFromXml(AutomationDocumentKind.Envelopes, xmlText));

    /// <inheritdoc />
    public int PrintFromXmlNotifications(string xmlText) =>
        Execute(_preparer.PreparePrintFromXml(AutomationDocumentKind.Notifications, xmlText));

    /// <inheritdoc />
    public int PrintFromXmlInventories(string xmlText) =>
        Execute(_preparer.PreparePrintFromXml(AutomationDocumentKind.Inventories, xmlText));

    /// <inheritdoc />
    public int PrintFromXmlRegisters(string xmlText) =>
        Execute(_preparer.PreparePrintFromXml(AutomationDocumentKind.Registers, xmlText));

    /// <inheritdoc />
    public int PrintEnvelope(
        string from,
        string fromAddress,
        string fromZipCode,
        string to,
        string toAddress,
        string toZipCode) =>
        Execute(_preparer.PreparePrintEnvelope(from, fromAddress, fromZipCode, to, toAddress, toZipCode));

    /// <inheritdoc />
    public int OpenDummyFormToSetAsForeground(out string pRetVal)
    {
        pRetVal = string.Empty;

        try
        {
            pRetVal = _foreground.OpenDummyFormToSetAsForeground();
        }
        catch (Exception error)
        {
            SetWarning(AutomationWarningCodes.DocumentError, error.Message);
        }

        return SOk;
    }

    /// <inheritdoc />
    public int CloseDummyFormAndSetAsForeground(string process)
    {
        try
        {
            _foreground.CloseDummyFormAndSetAsForeground(process ?? string.Empty);
        }
        catch (Exception error)
        {
            SetWarning(AutomationWarningCodes.DocumentError, error.Message);
        }

        return SOk;
    }

    /// <inheritdoc />
    public int DatabaseIsLoaded(out bool pRetVal)
    {
        pRetVal = _dispatcher.IsDatabaseLoaded;

        return SOk;
    }

    /// <inheritdoc />
    public int SafeLoadDatabase(out bool pRetVal)
    {
        var результат = _dispatcher.Execute(_preparer.PrepareSafeLoadDatabase());

        SetWarning(результат);

        pRetVal = результат.Succeeded;

        return SOk;
    }

    /// <inheritdoc />
    public int GetFlagParseZipIndexFromAddress(out bool pRetVal)
    {
        pRetVal = _flags.GetParseZipIndexFromAddress();

        return SOk;
    }

    /// <inheritdoc />
    public int SetFlagParseZipIndexFromAddress(bool value, out bool pRetVal)
    {
        _flags.SetParseZipIndexFromAddress(value);

        pRetVal = _flags.GetParseZipIndexFromAddress();

        return SOk;
    }

    /// <inheritdoc />
    public int GetFlagShowFormsAsModal(out bool pRetVal)
    {
        pRetVal = _flags.GetShowFormsAsModal();

        return SOk;
    }

    /// <inheritdoc />
    public int SetFlagShowFormsAsModal(bool value, out bool pRetVal)
    {
        _flags.SetShowFormsAsModal(value);

        // Флаг влияет на поведение окон печати: по умолчанию (false) печать
        // выполняется вслепую и вызывающий поток не блокируется.
        _environment.ShowFormsAsModal = value;

        pRetVal = _flags.GetShowFormsAsModal();

        return SOk;
    }

    /// <inheritdoc />
    public int GetProductVersion(out string pRetVal)
    {
        pRetVal = ProductVersion.Value;

        return SOk;
    }

    /// <inheritdoc />
    public int GetAPIVersion(out int pRetVal)
    {
        pRetVal = ComAutomationIds.ApiVersion;

        return SOk;
    }

    /// <inheritdoc />
    public int GetRegisterTemplateNames(out string[] pRetVal)
    {
        pRetVal = Array.Empty<string>();

        var результат = _dispatcher.Execute(_preparer.PrepareGetRegisterTemplateNames());

        SetWarning(результат);

        pRetVal = результат.ResultValues.ToArray();

        return SOk;
    }

    /// <inheritdoc />
    public int PrintFromXmlRegistersWithoutForm(
        string xmlText,
        string nameTemplate,
        string resultFullPath) =>
        Execute(_preparer.PreparePrintRegisterWithoutForm(xmlText, nameTemplate, resultFullPath));

    /// <inheritdoc />
    public int GetWarningType(out int pRetVal)
    {
        lock (_sync)
        {
            pRetVal = _warningType;
        }

        return SOk;
    }

    /// <inheritdoc />
    public int GetStdRegisterTemplateNames(out string[] pRetVal)
    {
        pRetVal = Array.Empty<string>();

        var результат = _dispatcher.Execute(_preparer.PrepareGetStdRegisterTemplateNames());

        SetWarning(результат);

        pRetVal = результат.ResultValues.ToArray();

        return SOk;
    }

    /// <summary>
    /// Исполняет подготовленную команду и запоминает код результата.
    /// </summary>
    /// <param name="command">Подготовленная команда.</param>
    /// <returns>S_OK: ошибка кодируется в <see cref="GetWarningType"/>.</returns>
    private int Execute(AutomationCommand command)
    {
        var результат = _dispatcher.Execute(command);

        SetWarning(результат);

        return SOk;
    }

    private void SetWarning(AutomationExecutionResult результат)
    {
        lock (_sync)
        {
            _warningType = результат.WarningCode;
        }
    }

    private void SetWarning(int warningCode, string message)
    {
        lock (_sync)
        {
            _warningType = warningCode;
        }
    }

    /// <summary>
    /// Версия продукта строкой вида «x.y.z.w» (ТЗ §9.1, метод 18).
    /// </summary>
    private static readonly Lazy<string> ProductVersion = new(ResolveProductVersion);

    private static string ResolveProductVersion()
    {
        var сборка = typeof(PEProApplication).Assembly;

        var информационная = сборка
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var кандидат = string.IsNullOrWhiteSpace(информационная)
            ? сборка.GetName().Version?.ToString()
            : информационная;

        return NormalizeVersion(кандидат);
    }

    private static string NormalizeVersion(string? значение)
    {
        var основа = (значение ?? string.Empty).Split('+')[0].Split('-')[0].Trim();

        var части = основа.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var числа = new List<int>(4);

        foreach (var часть in части)
        {
            числа.Add(int.TryParse(часть, out var число) ? число : 0);
        }

        while (числа.Count < 4)
        {
            числа.Add(0);
        }

        return string.Join(".", числа.Take(4));
    }
}
