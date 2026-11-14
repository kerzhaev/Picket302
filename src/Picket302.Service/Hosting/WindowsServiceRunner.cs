using System.Runtime.InteropServices;

namespace Picket302.Service.Hosting;

/// <summary>
/// Запуск службы под управлением диспетчера служб Windows.
/// </summary>
/// <remarks>
/// <para>
/// Собственный диспетчер SCM написан прямо здесь, потому что пакет
/// <c>Microsoft.Extensions.Hosting.WindowsServices</c> в офлайн-окружении
/// недоступен. Используется тот же протокол, что и у штатного
/// <c>ServiceBase</c>: служба сообщает SCM о своём состоянии и получает
/// команды остановки.
/// </para>
/// <para>
/// Служба работает в отдельном процессе без интерактивного окна и
/// переживает выход сотрудника из учётной записи: её выполняет собственная
/// учётная запись службы.
/// </para>
/// </remarks>
internal static class WindowsServiceRunner
{
    /// <summary>Тип службы: собственный процесс.</summary>
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;

    /// <summary>Служба ожидает запуска.</summary>
    private const uint SERVICE_START_PENDING = 0x00000002;

    /// <summary>Служба работает.</summary>
    private const uint SERVICE_RUNNING = 0x00000004;

    /// <summary>Служба останавливается.</summary>
    private const uint SERVICE_STOP_PENDING = 0x00000003;

    /// <summary>Служба остановлена.</summary>
    private const uint SERVICE_STOPPED = 0x00000001;

    /// <summary>Служба принимает команды остановки и выключения.</summary>
    private const uint SERVICE_ACCEPT_STOP = 0x00000001;

    /// <summary>Служба принимает команду выключения.</summary>
    private const uint SERVICE_ACCEPT_SHUTDOWN = 0x00000004;

    /// <summary>Команда остановки службы.</summary>
    private const uint SERVICE_CONTROL_STOP = 0x00000001;

    /// <summary>Команда выключения компьютера.</summary>
    private const uint SERVICE_CONTROL_SHUTDOWN = 0x00000005;

    /// <summary>Код службы, снятой с выполнения.</summary>
    private const uint ERROR_SERVICE_DISABLED = 0x00000420;

    /// <summary>
    /// Делегат обработчика состояния службы: SCM вызывает его при старте и
    /// при командах управления.
    /// </summary>
    /// <param name="управление">Команда управления.</param>
    /// <param name="параметр">Параметр команды.</param>
    public delegate void ОбработчикУправления(uint управление, uint параметр);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_TABLE_ENTRY
    {
        public IntPtr lpServiceName;
        public IntPtr lpServiceProc;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartServiceCtrlDispatcherW(SERVICE_TABLE_ENTRY[] lpServiceTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterServiceCtrlHandlerExW(
        string lpServiceName,
        IntPtr lpHandlerProc,
        IntPtr lpContext);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr hServiceStatus, ref SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetLastError();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalAlloc(uint uFlags, uint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const uint LMEM_ZEROINIT = 0x00000040;

    private static Action? _запуск;
    private static Action? _останов;
    private static CancellationTokenSource? _отмена;
    private static IntPtr _дескрипторСостояния = IntPtr.Zero;
    private static uint _текущееСостояние = SERVICE_STOPPED;

    /// <summary>
    /// Пытается передать управление диспетчеру служб Windows.
    /// </summary>
    /// <remarks>
    /// Возвращает <see langword="false"/>, если процесс запущен не под
    /// управлением SCM: в этом случае служба работает как обычное консольное
    /// приложение (используется при проверках и запуске вручную).
    /// </remarks>
    /// <param name="имяСлужбы">Системное имя службы.</param>
    /// <param name="запуск">
    /// Действие запуска службы; получает отменяющий токен остановки.
    /// </param>
    /// <returns><see langword="true"/>, если служба запущена через SCM.</returns>
    public static bool ЗапуститьКакСлужбу(string имяСлужбы, Action<CancellationToken> запуск)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяСлужбы);
        ArgumentNullException.ThrowIfNull(запуск);

        _отмена = new CancellationTokenSource();
        _запуск = () => запуск(_отмена!.Token);
        _останов = () => _отмена?.Cancel();

        var имя = Marshal.StringToHGlobalUni(имяСлужбы);

        try
        {
            var таблица = new[]
            {
                new SERVICE_TABLE_ENTRY
                {
                    lpServiceName = имя,
                    lpServiceProc = Marshal.GetFunctionPointerForDelegate(ОбработчикГлавный),
                },
            };

            if (!StartServiceCtrlDispatcherW(таблица))
            {
                // Процесс запущен не под SCM либо служба отключена.
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(имя);
        }
    }

    /// <summary>
    /// Обработчик состояния службы: SCM вызывает его при старте и при
    /// командах управления.
    /// </summary>
    /// <param name="управление">Команда управления.</param>
    /// <param name="параметр">Параметр команды.</param>
    private static void ОбработчикУправленияСлужбы(uint управление, uint параметр)
    {
        switch (управление)
        {
            case SERVICE_CONTROL_STOP:
            case SERVICE_CONTROL_SHUTDOWN:
                УстановитьСостояние(SERVICE_STOP_PENDING, 0, 0);
                _останов?.Invoke();
                УстановитьСостояние(SERVICE_STOPPED, 0, 0);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Точка входа службы для SCM.
    /// </summary>
    /// <param name="аргументов">Число аргументов запуска.</param>
    /// <param name="аргументы">Аргументы запуска.</param>
    private static void ОбработчикГлавный(uint аргументов, IntPtr аргументы)
    {
        var дескриптор = RegisterServiceCtrlHandlerExW(
            ИмяСлужбы,
            Marshal.GetFunctionPointerForDelegate(Обработчик),
            IntPtr.Zero);

        _дескрипторСостояния = дескриптор;

        УстановитьСостояние(SERVICE_START_PENDING, 0, 30000);

        try
        {
            _запуск?.Invoke();

            УстановитьСостояние(SERVICE_RUNNING, SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN, 0);
        }
        catch (Exception)
        {
            УстановитьСостояние(SERVICE_STOPPED, 0, 0);
            throw;
        }
    }

    /// <summary>
    /// Имя службы, под которым она зарегистрирована в SCM.
    /// </summary>
    public const string ИмяСлужбы = "Picket302Service";

    private static readonly ОбработчикУправления Обработчик = ОбработчикУправленияСлужбы;

    /// <summary>
    /// Сообщает SCM о текущем состоянии службы.
    /// </summary>
    /// <param name="состояние">Новое состояние.</param>
    /// <param name="принимаемые">Принимаемые команды управления.</param>
    /// <param name="ожидание">Подсказка ожидания, миллисекунды.</param>
    private static void УстановитьСостояние(uint состояние, uint принимаемые, uint ожидание)
    {
        var статус = new SERVICE_STATUS
        {
            dwServiceType = SERVICE_WIN32_OWN_PROCESS,
            dwCurrentState = состояние,
            dwControlsAccepted = принимаемые,
            dwWin32ExitCode = 0,
            dwServiceSpecificExitCode = 0,
            dwCheckPoint = 0,
            dwWaitHint = ожидание,
        };

        SetServiceStatus(_дескрипторСостояния, ref статус);

        _текущееСостояние = состояние;
    }

    /// <summary>
    /// Код последней ошибки Windows.
    /// </summary>
    /// <returns>Код ошибки.</returns>
    public static uint ПоследняяОшибка() => (uint)GetLastError();
}
