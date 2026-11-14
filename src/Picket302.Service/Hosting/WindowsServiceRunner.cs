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
/// <b>Жизненный цикл.</b> SCM ограничивает время ожидания запуска службы
/// примерно тридцатью секундами, поэтому состояние «запускается» не может
/// длиться дольше этого срока, а состояние «работает» наступает сразу после
/// того, как хост поднят. Состояние «остановлена» наступает после того, как
/// хост действительно остановился, а не сразу после команды остановки.
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

    /// <summary>Предельное время ожидания запуска, миллисекунды.</summary>
    private const uint StartTimeoutMilliseconds = 30000;

    /// <summary>
    /// Делегат обработчика состояния службы: SCM вызывает его при командах
    /// управления.
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

    private static Action? _старт;
    private static Func<Task>? _ожиданиеОстановки;
    private static CancellationTokenSource? _отмена;
    private static IntPtr _дескрипторСостояния = IntPtr.Zero;

    /// <summary>Имя службы, под которым она зарегистрирована в SCM.</summary>
    public const string ИмяСлужбы = "Picket302Service";

    // Делегаты P/Invoke должны оставаться живыми всё время работы службы:
    // сборщик мусора не должен собрать их между вызовом SCM и обработкой
    // команды управления.
    private static readonly ОбработчикУправления Обработчик = ОбработчикУправленияСлужбы;

    private static readonly Action ОбработчикГлавный = ГлавныйОбработчик;

    /// <summary>
    /// Пытается передать управление диспетчеру служб Windows.
    /// </summary>
    /// <remarks>
    /// Возвращает <see langword="false"/>, если процесс запущен не под
    /// управлением SCM: в этом случае служба работает как обычное консольное
    /// приложение (используется при проверках и запуске вручную).
    /// </remarks>
    /// <param name="имяСлужбы">Системное имя службы.</param>
    /// <param name="старт">
    /// Запускает хост и возвращает управление, когда хост поднят. Токен
    /// отменяется по команде остановки.
    /// </param>
    /// <param name="ожиданиеОстановки">
    /// Ожидает фактической остановки хоста.
    /// </param>
    /// <returns><see langword="true"/>, если служба запущена через SCM.</returns>
    public static bool ЗапуститьКакСлужбы(
        string имяСлужбы,
        Func<CancellationToken, Task> старт,
        Func<Task> ожиданиеОстановки)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяСлужбы);
        ArgumentNullException.ThrowIfNull(старт);
        ArgumentNullException.ThrowIfNull(ожиданиеОстановки);

        _отмена = new CancellationTokenSource();
        _старт = () => старт(_отмена!.Token).GetAwaiter().GetResult();
        _ожиданиеОстановки = ожиданиеОстановки;

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
    /// Обработчик состояния службы: SCM вызывает его при командах управления.
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

                // Отмена токена инициирует остановку хоста; состояние
                // «остановлена» наступает после того, как хост действительно
                // остановился.
                _отмена?.Cancel();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Точка входа службы для SCM.
    /// </summary>
    private static void ГлавныйОбработчик()
    {
        _дескрипторСостояния = RegisterServiceCtrlHandlerExW(
            ИмяСлужбы,
            Marshal.GetFunctionPointerForDelegate(Обработчик),
            IntPtr.Zero);

        УстановитьСостояние(SERVICE_START_PENDING, 0, StartTimeoutMilliseconds);

        try
        {
            // Хост поднимается и возвращает управление: после этого служба
            // считается работающей, а ожидание остановки идёт отдельным шагом.
            _старт?.Invoke();

            УстановитьСостояние(SERVICE_RUNNING, SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN, 0);

            _ожиданиеОстановки?.Invoke()?.GetAwaiter().GetResult();

            УстановитьСостояние(SERVICE_STOPPED, 0, 0);
        }
        catch (Exception)
        {
            УстановитьСостояние(SERVICE_STOPPED, 0, 0);
            throw;
        }
    }

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
    }
}
