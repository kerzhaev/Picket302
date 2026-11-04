using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Picket302.ComAutomation;

/// <summary>
/// Управление передним планом окна: вспомогательное немодальное окно и
/// возврат фокуса вызывающему процессу (ТЗ §9.2, методы 10–11).
/// </summary>
public interface IForegroundWindowService
{
    /// <summary>
    /// Открывает вспомогательное окно и перехватывает фокус переднего плана.
    /// </summary>
    /// <returns>Заголовок окна, находившегося на переднем плане до перехвата.</returns>
    string OpenDummyFormToSetAsForeground();

    /// <summary>
    /// Закрывает вспомогательное окно и возвращает фокус вызывающему процессу.
    /// </summary>
    /// <param name="process">Имя процесса или его идентификатор.</param>
    void CloseDummyFormAndSetAsForeground(string process);
}

/// <summary>
/// Служба управления передним планом: вспомогательное окно создаётся как
/// собственное Win32-окно (<c>RegisterClassExW</c> + <c>CreateWindowExW</c>) в
/// отдельном STA-потоке с собственным циклом сообщений, поэтому перехват
/// фокуса работает и при вызове из потока COM (ТЗ §9.2).
/// </summary>
/// <remarks>
/// <para>
/// Исторически окно создавалось как WinForms <c>Form</c> с пользовательским
/// <c>CreateParams.ClassName</c>; на .NET 8 регистрация такого класса внутри
/// WinForms падала с <c>Win32Exception</c> 1411 «Class does not exist», окно
/// не создавалось, а ошибка ранее глоталась. Теперь класс и окно
/// регистрируются и создаются напрямую через Win32, ошибка инициализации
/// доступна вызывающему коду через <see cref="ОшибкаИнициализации"/>.
/// </para>
/// <para>
/// Два экземпляра службы в одном процессе (хост и фабрика классов) создают
/// два независимых окна одного класса; <c>FindWindow</c> возвращает первое —
/// это корректно для поиска «любого работающего экземпляра сервера».
/// </para>
/// </remarks>
public sealed class ForegroundWindowService : IForegroundWindowService, IDisposable
{
    /// <summary>Класс вспомогательного окна — по нему находится работающий экземпляр сервера.</summary>
    public const string HelperWindowClassName = "Picket302ComAutomationHelper";

    /// <summary>Зарегистрированное сообщение активации работающего экземпляра.</summary>
    public const string ActivationMessageName = "PICKET302_COM_ACTIVATION";

    /// <summary>Заголовок вспомогательного окна.</summary>
    public const string HelperWindowTitle = "Picket302 — COM-автоматизация";

    private const uint WmClose = 0x0010;
    private const uint WmQuit = 0x0012;
    private const uint WsOverlapped = 0x00000000;
    private const uint WsExToolWindow = 0x00000080;
    private const int SwShow = 5;
    private const int SwHide = 0;
    private const int GwlpUserdata = -21;
    private const int IdcArrow = 32512;
    private const int ErrorClassAlreadyExists = 1410;
    private const uint QsAllInput = 0x00FF;
    private const uint MwmoInputAvailable = 0x0001;
    private const uint WaitObject0 = 0;
    private const uint PmRemove = 0x0001;

    /// <summary>
    /// Оконная процедура: статический делегат — корень для сборщика мусора,
    /// чтобы Win32 не получил «повисший» указатель вызова.
    /// </summary>
    private static readonly WndProc ОконнаяПроцедура = ОконнаяПроцедураImpl;

    private static readonly object СинхронизацияКласса = new();
    private static bool КлассЗарегистрирован;

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _завершить = new(false);
    private Thread? _thread;
    private volatile IntPtr _hwnd;
    private Exception? _ошибкаИнициализации;
    private GCHandle _ссылкаНаСлужбу;
    private int _activationMessage;
    private int _tid;
    private bool _освобождено;

    /// <summary>
    /// Создаёт службу и запускает поток вспомогательного окна: готовность
    /// фиксируется только после создания HWND либо после ошибки, переданной
    /// в <see cref="ОшибкаИнициализации"/>.
    /// </summary>
    public ForegroundWindowService()
    {
        _thread = new Thread(RunHelperThread)
        {
            IsBackground = true,
            Name = "Picket302.ComAutomation.Foreground",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        AutomationServerLog.Write(
            $"Helper: поток стартован: managedId={_thread.ManagedThreadId}, " +
            $"состояние={_thread.ThreadState}, pid={Environment.ProcessId}.");

        _ready.Wait(TimeSpan.FromSeconds(5));

        AutomationServerLog.Write(
            $"Helper: ожидание готовности завершено: состояние={_thread.ThreadState}, " +
            $"hwnd={HelperWindowHandle}, ошибка={(_ошибкаИнициализации?.Message ?? "нет")}, " +
            $"pid={Environment.ProcessId}.");
    }

    /// <summary>Ошибка запуска потока окна; <see langword="null"/> — окно создано.</summary>
    public Exception? ОшибкаИнициализации => _ошибкаИнициализации;

    /// <summary>
    /// Дескриптор вспомогательного окна; нулевой — окно не создано или уже
    /// уничтожено (проверка <c>IsWindow</c> выполняется на каждом чтении,
    /// поэтому повисший HWND наружу не отдаётся).
    /// </summary>
    public IntPtr HelperWindowHandle => IsWindow(_hwnd) ? _hwnd : IntPtr.Zero;

    /// <inheritdoc />
    public string OpenDummyFormToSetAsForeground()
    {
        var заголовок = ReadForegroundTitle();

        ПоказатьВспомогательноеОкно();

        return заголовок;
    }

    /// <inheritdoc />
    public void CloseDummyFormAndSetAsForeground(string process)
    {
        var окно = FindProcessWindow(process);

        if (окно != IntPtr.Zero)
        {
            SetForegroundWindow(окно);
        }

        СкрытьВспомогательноеОкно();
    }

    /// <summary>
    /// Освобождает ресурсы службы: взводит событие завершения, дожидается
    /// потока (HWND уничтожается в самом потоке) и освобождает примитивы.
    /// Не зависит от потока вызова и не зависает дольше 3 секунд.
    /// </summary>
    public void Dispose()
    {
        if (_освобождено)
        {
            return;
        }

        _освобождено = true;

        _завершить.Set();
        _thread?.Join(TimeSpan.FromSeconds(3));

        _ready.Dispose();
        _завершить.Dispose();
    }

    private void RunHelperThread()
    {
        try
        {
            _tid = Environment.CurrentManagedThreadId;

            AutomationServerLog.Write(
                $"Helper: поток запущен: tid={_tid}, pid={Environment.ProcessId}.");

            ЗарегистрироватьКлассОкна();

            AutomationServerLog.Write(
                $"Helper: класс зарегистрирован: tid={_tid}.");

            _activationMessage = RegisterWindowMessage(ActivationMessageName);

            _ссылкаНаСлужбу = GCHandle.Alloc(this);

            // Окно создаётся невидимым (без WS_VISIBLE), 1×1, за пределами
            // экрана, с WS_EX_TOOLWINDOW — без панели задач и без диалогов.
            var hwnd = CreateWindowExW(
                WsExToolWindow,
                HelperWindowClassName,
                HelperWindowTitle,
                WsOverlapped,
                -32000,
                -32000,
                1,
                1,
                IntPtr.Zero,
                IntPtr.Zero,
                Marshal.GetHINSTANCE(typeof(ForegroundWindowService).Module),
                IntPtr.Zero);

            if (hwnd == IntPtr.Zero)
            {
                var код = Marshal.GetLastWin32Error();

                throw new System.ComponentModel.Win32Exception(
                    код,
                    $"CreateWindowExW для класса «{HelperWindowClassName}» не удалось: Win32Error={код}.");
            }

            SetWindowLongPtr(hwnd, GwlpUserdata, GCHandle.ToIntPtr(_ссылкаНаСлужбу));

            _hwnd = hwnd;

            AutomationServerLog.Write(
                $"Helper: hwnd={hwnd}, tid={_tid}, pid={Environment.ProcessId}, " +
                $"класс={HelperWindowClassName} создан.");

            // Готовность объявляется только после существующего HWND.
            _ready.Set();

            ЦиклСообщений();
        }
        catch (Exception ошибка)
        {
            // Ошибка инициализации фиксируется и отдаётся вызывающему коду:
            // мнимая готовность службы недопустима.
            _ошибкаИнициализации = ошибка;

            AutomationServerLog.Write(
                "Поток вспомогательного окна завершился с ошибкой: " + ошибка);

            _ready.Set();
        }
        finally
        {
            if (_ссылкаНаСлужбу.IsAllocated)
            {
                _ссылкаНаСлужбу.Free();
            }
        }
    }

    /// <summary>
    /// Регистрирует оконный класс <see cref="HelperWindowClassName"/> один раз
    /// на процесс; повторная регистрация существующего класса (1410)
    /// игнорируется, иная — приводит к контролируемому отказу.
    /// </summary>
    private static void ЗарегистрироватьКлассОкна()
    {
        lock (СинхронизацияКласса)
        {
            if (КлассЗарегистрирован)
            {
                return;
            }

            var класс = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(ОконнаяПроцедура),
                hInstance = Marshal.GetHINSTANCE(typeof(ForegroundWindowService).Module),
                hCursor = LoadCursorW(IntPtr.Zero, new IntPtr(IdcArrow)),
                lpszClassName = HelperWindowClassName,
            };

            if (RegisterClassExW(ref класс) == 0)
            {
                var код = Marshal.GetLastWin32Error();

                if (код != ErrorClassAlreadyExists)
                {
                    throw new System.ComponentModel.Win32Exception(
                        код,
                        $"RegisterClassExW для класса «{HelperWindowClassName}» не удалось: Win32Error={код}.");
                }
            }

            КлассЗарегистрирован = true;
        }
    }

    /// <summary>
    /// Цикл сообщений потока окна: обслуживает очередь и одновременно ждёт
    /// взведения события завершения (<c>MsgWaitForMultipleObjectsEx</c>),
    /// поэтому <see cref="Dispose"/> не зависит от доставки WM_QUIT.
    /// </summary>
    private void ЦиклСообщений()
    {
        var дескрипторы = new[] { _завершить.WaitHandle.SafeWaitHandle.DangerousGetHandle() };

        while (true)
        {
            var ожидание = MsgWaitForMultipleObjectsEx(
                дескрипторы.Length,
                дескрипторы,
                500,
                QsAllInput,
                MwmoInputAvailable);

            if (ожидание == WaitObject0)
            {
                return;
            }

            while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
            {
                if (message.Message == (int)WmQuit)
                {
                    return;
                }

                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
    }

    /// <summary>
    /// Оконная процедура: маршрутизация к экземпляру службы через
    /// <c>GWLP_USERDATA</c>; <c>WM_CLOSE</c> уничтожает окно в его собственном
    /// потоке, зарегистрированное сообщение активации перехватывает фокус.
    /// </summary>
    private static IntPtr ОконнаяПроцедураImpl(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmClose)
        {
            DestroyWindow(hwnd);

            return IntPtr.Zero;
        }

        ForegroundWindowService? служба = null;

        var ссылка = GetWindowLongPtrW(hwnd, GwlpUserdata);

        if (ссылка != IntPtr.Zero && GCHandle.FromIntPtr(ссылка).Target is ForegroundWindowService найденная)
        {
            служба = найденная;
        }

        if (служба is not null
            && служба._activationMessage != 0
            && message == (uint)служба._activationMessage)
        {
            служба.ПерехватитьФокус(hwnd);

            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ПерехватитьФокус(IntPtr hwnd)
    {
        if (IsWindow(hwnd))
        {
            SetForegroundWindow(hwnd);
        }
    }

    private void ПоказатьВспомогательноеОкно()
    {
        var hwnd = HelperWindowHandle;

        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SwShow);
            SetForegroundWindow(hwnd);
        }
    }

    private void СкрытьВспомогательноеОкно()
    {
        var hwnd = HelperWindowHandle;

        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SwHide);
        }
    }

    private static string ReadForegroundTitle()
    {
        var окно = GetForegroundWindow();

        if (окно == IntPtr.Zero)
        {
            return string.Empty;
        }

        var буфер = new System.Text.StringBuilder(512);

        var длина = GetWindowText(окно, буфер, буфер.Capacity);

        return длина > 0 ? буфер.ToString(0, длина) : string.Empty;
    }

    private static IntPtr FindProcessWindow(string process)
    {
        if (string.IsNullOrWhiteSpace(process))
        {
            return IntPtr.Zero;
        }

        var идентификаторы = new HashSet<int>();

        if (int.TryParse(process.Trim(), out var номер))
        {
            идентификаторы.Add(номер);
        }
        else
        {
            var имя = process.Trim();

            if (имя.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                имя = имя[..^4];
            }

            Process[] процессы;

            try
            {
                процессы = Process.GetProcessesByName(имя);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }

            foreach (var экземпляр in процессы)
            {
                using (экземпляр)
                {
                    идентификаторы.Add(экземпляр.Id);
                }
            }
        }

        if (идентификаторы.Count == 0)
        {
            return IntPtr.Zero;
        }

        var найдено = IntPtr.Zero;

        EnumWindows(
            (окно, _) =>
            {
                GetWindowThreadProcessId(окно, out var идентификатор);

                if (идентификаторы.Contains((int)идентификатор))
                {
                    найдено = окно;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);

        return найдено;
    }

    /// <summary>Делегат оконной процедуры.</summary>
    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>Делегат обхода окон.</summary>
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLongW(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLongW(IntPtr window, int index, int value);

    /// <summary>Чтение длинного значения окна с учётом разрядности процесса.</summary>
    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtrW(window, index)
            : new IntPtr(GetWindowLongW(window, index));

    /// <summary>Запись длинного значения окна с учётом разрядности процесса.</summary>
    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtrW(window, index, value)
            : new IntPtr(SetWindowLongW(window, index, (int)value));

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr module, IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MsgWaitForMultipleObjectsEx(
        int count,
        IntPtr[] handles,
        uint timeout,
        uint wakeMask,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PeekMessageW(
        out NativeMessage message,
        IntPtr window,
        uint filterMin,
        uint filterMax,
        uint remove);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref NativeMessage message);

    /// <summary>Сообщение Windows в собственном представлении.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        /// <summary>Дескриптор окна.</summary>
        public IntPtr Handle;

        /// <summary>Код сообщения.</summary>
        public int Message;

        /// <summary>Параметр WParam.</summary>
        public IntPtr WParam;

        /// <summary>Параметр LParam.</summary>
        public IntPtr LParam;

        /// <summary>Время публикации сообщения.</summary>
        public uint Time;

        /// <summary>Позиция курсора.</summary>
        public Point Cursor;
    }

    /// <summary>Позиция точки в собственном представлении.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        /// <summary>Координата X.</summary>
        public int X;

        /// <summary>Координата Y.</summary>
        public int Y;
    }

    /// <summary>Структура WNDCLASSEX для регистрации класса окна.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        /// <summary>Размер структуры.</summary>
        public uint cbSize;

        /// <summary>Стили класса.</summary>
        public uint style;

        /// <summary>Процедура окна.</summary>
        public IntPtr lpfnWndProc;

        /// <summary>Дополнительные байты класса.</summary>
        public int cbClsExtra;

        /// <summary>Дополнительные байты окна.</summary>
        public int cbWndExtra;

        /// <summary>Дескриптор модуля.</summary>
        public IntPtr hInstance;

        /// <summary>Значок.</summary>
        public IntPtr hIcon;

        /// <summary>Курсор.</summary>
        public IntPtr hCursor;

        /// <summary>Кисть фона.</summary>
        public IntPtr hbrBackground;

        /// <summary>Имя меню.</summary>
        public string? lpszMenuName;

        /// <summary>Имя класса.</summary>
        public string lpszClassName;

        /// <summary>Малый значок.</summary>
        public IntPtr hIconSm;
    }

    /// <summary>
    /// Включает режим осведомлённости о DPI процесса: per-monitor aware
    /// (Windows 10 1703+), иначе — системный DPI aware (ТЗ §9.6).
    /// </summary>
    /// <returns><see langword="true"/> — режим включён.</returns>
    public static bool EnableDpiAwareness()
    {
        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
        if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
        {
            return true;
        }

        return SetProcessDPIAware();
    }
}

/// <summary>
/// Фабрика классов COM (интерфейс <c>IClassFactory</c>, IID
/// <c>00000001-0000-0000-C000-000000000046</c>).
/// </summary>
/// <remarks>
/// Интерфейс объявлен в проекте явно: в .NET 8 тип
/// <c>System.Runtime.InteropServices.ComTypes.IClassFactory</c> отсутствует.
/// </remarks>
[ComVisible(true)]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    /// <summary>
    /// Создаёт экземпляр класса.
    /// </summary>
    /// <param name="outer">Агрегирующий объект; не поддерживается.</param>
    /// <param name="classId">Идентификатор запрашиваемого интерфейса.</param>
    /// <param name="instance">Созданный экземпляр.</param>
    void CreateInstance(
        IntPtr outer,
        ref Guid classId,
        [MarshalAs(UnmanagedType.Interface)] out object instance);

    /// <summary>
    /// Удерживает сервер от выгрузки.
    /// </summary>
    /// <param name="fLock">Признак блокировки сервера.</param>
    void LockServer(bool fLock);
}

/// <summary>
/// Фабрика классов локального COM-сервера (ТЗ §9.1, функция 113).
/// </summary>
/// <remarks>
/// Служба переднего плана создаётся хостом до запуска сервера
/// (<c>ИнициализироватьОбщуюСлужбу</c>), а не в статическом инициализаторе:
/// запуск STA-потока вспомогательного окна из контекста cctor приводил к
/// зависанию <c>Thread.Start</c> (доказано журналом: строки
/// «Helper: поток стартован» не появлялись).
/// </remarks>
[ComVisible(true)]
public sealed class ComClassFactory : IClassFactory
{
    /// <summary>Единственная служба переднего плана на процесс сервера (F-E24):
    /// каждый экземпляр PEProApplication получает один и тот же STA-поток
    /// вспомогательного окна, а не собственный.</summary>
    internal static ForegroundWindowService ОбщаяСлужбаПереднегоПлана { get; private set; }
        = null!;

    /// <summary>
    /// Назначает общую службу переднего плана (вызывается хостом до Run).
    /// </summary>
    /// <param name="служба">Служба переднего плана.</param>
    internal static void ИнициализироватьОбщуюСлужбу(ForegroundWindowService служба)
    {
        ОбщаяСлужбаПереднегоПлана = служба
            ?? throw new ArgumentNullException(nameof(служба));
    }

    /// <inheritdoc />
    public void CreateInstance(
        IntPtr outer,
        ref Guid classId,
        [MarshalAs(UnmanagedType.Interface)] out object instance)
    {
        if (outer != IntPtr.Zero)
        {
            throw new COMException(
                "Агрегация COM-объектов не поддерживается.",
                unchecked((int)0x80040110));
        }

        instance = new PEProApplication(ОбщаяСлужбаПереднегоПлана);
    }

    /// <inheritdoc />
    public void LockServer(bool fLock)
    {
        // Сервер не выгружается при освобождении последнего объекта: он живёт,
        // пока работает цикл сообщений (поведение локального сервера).
    }
}

/// <summary>
/// Хост локального COM-сервера автоматизации: регистрация фабрики классов,
/// цикл сообщений, единственный экземпляр и осведомлённость о DPI
/// (ТЗ §9.1, функция 113; ТЗ §9.6).
/// </summary>
public sealed class ComServerHost
{
    /// <summary>Имя именованного мьютекса единственного экземпляра.</summary>
    /// <remarks>
    /// Мьютекс создаётся в сессии пользователя (<c>Local\</c>): локальный
    /// COM-сервер работает в сессии вызывающего, а глобальный объект требует
    /// привилегии «создание глобальных объектов», недоступной обычному
    /// пользователю.
    /// </remarks>
    public const string SingleInstanceMutexName = @"Local\Picket302.ComAutomation.SingleInstance";

    /// <summary>
    /// Имя именованного события плавного завершения сервера: внешний
    /// процесс взводит событие, сервер получает WM_QUIT в главный поток,
    /// завершает цикл сообщений и отзывает фабрику классов через
    /// CoRevokeClassObject. Жёсткий kill без отзыва фабрики оставляет в SCM
    /// запись о сервере, который «ещё держит класс», и последующая холодная
    /// активация может отказывать без попытки запуска.
    /// </summary>
    public const string ShutdownEventName = @"Local\Picket302.ComAutomation.Shutdown";

    /// <summary>Описание сервера, записываемое в реестр.</summary>
    public const string ServerDescription = "Picket302 — COM-сервер автоматизации";

    private const uint ClsCtxLocalServer = 0x00000004;
    private const uint RegClsMultipleUse = 0x00000002;
    private const uint WmQuit = 0x0012;
    private const uint QsAllInput = 0x00FF;
    private const uint MwmoInputAvailable = 0x0001;
    private const uint WaitObject0 = 0;
    private const uint PmRemove = 0x0001;

    /// <summary>Время ожидания освобождения мьютекса предшественником.</summary>
    private static readonly TimeSpan ВремяОжиданияМьютекса = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Win32-идентификатор главного потока сервера (регистрация фабрики и
    /// цикл сообщений). Именно его ждёт PostThreadMessage; управляемый
    /// Thread.CurrentManagedThreadId для этого не годится.
    /// </summary>
    internal static volatile int MainWin32ThreadId;

    /// <summary>
    /// Создаёт хост сервера: назначает общую службу переднего плана в
    /// обычном контексте конструктора (вне статического инициализатора
    /// фабрики), поэтому helper-окно в процессе ровно одно.
    /// </summary>
    public ComServerHost()
    {
        MainWin32ThreadId = GetCurrentThreadId();

        ComClassFactory.ИнициализироватьОбщуюСлужбу(new ForegroundWindowService());
    }

    /// <summary>
    /// Запускает сервер: включает DPI-осведомлённость, проверяет единственный
    /// экземпляр, регистрирует фабрику классов и выполняет цикл сообщений.
    /// </summary>
    /// <returns>Код завершения процесса.</returns>
    public int Run()
    {
        ForegroundWindowService.EnableDpiAwareness();

        AutomationServerLog.Write(
            $"Хост: exe={Environment.ProcessPath}, base={AppContext.BaseDirectory}, " +
            $"архитектура={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, " +
            $"pid={Environment.ProcessId}.");

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var созданНовый);

        var владеем = созданНовый;
        var полученОжиданием = false;

        if (!владеем)
        {
            // Мьютекс занят предыдущим экземпляром: он может быть в момент
            // завершения — фабрика уже отозвана, а процесс ещё жив. Короткое
            // ожидание позволяет новому запуску принять эстафету вместо
            // выхода «в никуда», из-за которого чистая активация клиента
            // не находит фабрику (ТЗ §9.1).
            владеем = ОжидатьМьютекс(mutex, ВремяОжиданияМьютекса);
            полученОжиданием = владеем;
        }

        AutomationServerLog.Write(
            $"Мьютекс: созданНовый={созданНовый}, полученОжиданием={полученОжиданием}, pid={Environment.ProcessId}.");

        if (!владеем)
        {
            // Работающий экземпляр уже обслуживает запросы — активируем его
            // и завершаемся, не поднимая вторую копию сервера.
            AutomationServerLog.Write(
                $"Мьютекс занят работающим экземпляром, фабрика не регистрируется: pid={Environment.ProcessId}.");

            return ActivateRunningInstance();
        }

        var classId = new Guid(ComAutomationIds.ClassId);
        var factory = new ComClassFactory();

        var результат = CoRegisterClassObject(
            ref classId,
            factory,
            ClsCtxLocalServer,
            RegClsMultipleUse,
            out var cookie);

        AutomationServerLog.Write(
            $"CoRegisterClassObject: hresult=0x{результат:X8}, cookie={cookie}, " +
            $"pid={Environment.ProcessId}.");

        if (результат < 0)
        {
            AutomationServerLog.Write(
                $"Регистрация фабрики классов не удалась: 0x{результат:X8}, pid={Environment.ProcessId}.");

            return unchecked((int)результат);
        }

        // Канал плавного завершения: взведение именованного события будит
        // цикл сообщений, который штатно завершается с отзывом фабрики
        // классов. Жёсткий kill без отзыва фабрики оставляет в SCM запись
        // о сервере, который «ещё держит класс», и последующая холодная
        // активация может отказывать без попытки запуска.
        using var сигналЗавершения = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            ShutdownEventName);

        try
        {
            AutomationServerLog.Write($"Вход в цикл сообщений: pid={Environment.ProcessId}.");

            RunMessageLoop(сигналЗавершения);

            AutomationServerLog.Write($"Выход из цикла сообщений: pid={Environment.ProcessId}.");
        }
        finally
        {
            CoRevokeClassObject(cookie);

            AutomationServerLog.Write(
                $"CoRevokeClassObject выполнен: cookie={cookie}, pid={Environment.ProcessId}.");
        }

        return 0;
    }

    /// <summary>
    /// Ждёт освобождения мьютекса единственного экземпляра: предыдущий
    /// процесс может быть в момент завершения.
    /// </summary>
    /// <param name="mutex">Мьютекс единственного экземпляра.</param>
    /// <param name="ожидание">Полное время ожидания.</param>
    /// <returns><see langword="true"/> — владение получено.</returns>
    private static bool ОжидатьМьютекс(Mutex mutex, TimeSpan ожидание)
    {
        var дедлайн = DateTime.UtcNow + ожидание;

        while (true)
        {
            try
            {
                if (mutex.WaitOne(TimeSpan.FromMilliseconds(250)))
                {
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                // Предыдущий владелец завершился аварийно: владение перешло
                // к текущему процессу.
                return true;
            }

            if (DateTime.UtcNow >= дедлайн)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Активирует уже работающий экземпляр сервера: находит вспомогательное
    /// окно и отправляет ему зарегистрированное сообщение активации.
    /// </summary>
    /// <returns>Код завершения процесса.</returns>
    private int ActivateRunningInstance()
    {
        // Поиск окна чужого экземпляра: собственное helper-окно исключается
        // по PID, иначе второй запуск отправил бы активацию самому себе.
        var чужоеОкно = НайтиЧужоеОкноПомощника();

        if (чужоеОкно == IntPtr.Zero)
        {
            AutomationServerLog.Write(
                $"Работающий экземпляр не найден, активировать нечего: pid={Environment.ProcessId}.");

            return 0;
        }

        var сообщение = RegisterWindowMessage(ForegroundWindowService.ActivationMessageName);

        if (сообщение == 0)
        {
            return 0;
        }

        var доставлено = PostMessage(чужоеОкно, сообщение, IntPtr.Zero, IntPtr.Zero);

        AutomationServerLog.Write(
            $"Активация работающего экземпляра: hwnd={чужоеОкно}, доставлено={доставлено}, pid={Environment.ProcessId}.");

        return 0;
    }

    /// <summary>
    /// Находит helper-окно другого процесса сервера: класс
    /// <c>HelperWindowClassName</c> и PID, отличный от текущего.
    /// </summary>
    /// <returns>Дескриптор окна либо нулевой.</returns>
    private static IntPtr НайтиЧужоеОкноПомощника()
    {
        var мойПроцесс = Environment.ProcessId;
        var найдено = IntPtr.Zero;

        EnumWindows(
            (окно, _) =>
            {
                var буфер = new System.Text.StringBuilder(ForegroundWindowService.HelperWindowClassName.Length + 1);

                if (GetClassName(окно, буфер, буфер.Capacity)
                    && string.Equals(буфер.ToString(), ForegroundWindowService.HelperWindowClassName, StringComparison.Ordinal))
                {
                    GetWindowThreadProcessId(окно, out var процесс);

                    if (процесс != (uint)мойПроцесс)
                    {
                        найдено = окно;
                        return false;
                    }
                }

                return true;
            },
            IntPtr.Zero);

        return найдено;
    }

    /// <summary>
    /// Цикл сообщений сервера: обслуживает очередь сообщений и одновременно
    /// ждёт взведения события плавного завершения (<c>MsgWaitForMultipleObjectsEx</c>
    /// будит цикл и на сообщениях, и на событии, поэтому завершение не
    /// зависит от доставки WM_QUIT в STA-поток с COM-насосом).
    /// </summary>
    /// <param name="сигналЗавершения">Событие плавного завершения.</param>
    private static void RunMessageLoop(WaitHandle сигналЗавершения)
    {
        // Сторож: взведение события завершения доставляет WM_QUIT в очередь
        // главного STA-потока (канонический механизм остановки локального
        // COM-сервера; MsgWaitForMultipleObjectsEx на STA-потоке с
        // зарегистрированной фабрикой классов событие не видит — проверено
        // журналированием).
        var ожиданиеСигнала = ThreadPool.RegisterWaitForSingleObject(
            сигналЗавершения,
            (state, истекло) =>
            {
                if (истекло)
                {
                    return;
                }

                var доставлено = PostThreadMessage(MainWin32ThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

                AutomationServerLog.Write(
                    $"Сигнал завершения: PostThreadMessage(tid={MainWin32ThreadId}, WmQuit)={доставлено}, " +
                    $"pid={Environment.ProcessId}.");

                if (!доставлено)
                {
                    // Очередь главного потока ещё не создана: повторяем до
                    // доставки, чтобы завершение не потерялось.
                    var попыток = 0;

                    while (!PostThreadMessage(MainWin32ThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero)
                        && попыток < 40)
                    {
                        Thread.Sleep(250);
                        попыток++;

                        доставлено = PostThreadMessage(MainWin32ThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

                        if (доставлено)
                        {
                            AutomationServerLog.Write(
                                $"Сигнал завершения доставлен повтором {попыток}: pid={Environment.ProcessId}.");

                            break;
                        }
                    }
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        try
        {
            while (true)
            {
                // GetMessage возвращает 0 при WM_QUIT и -1 при ошибке.
                var результат = GetMessage(out var message, IntPtr.Zero, 0, 0);

                if (результат <= 0)
                {
                    break;
                }

                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            ожиданиеСигнала.Unregister(null);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        ref Guid classId,
        [MarshalAs(UnmanagedType.IUnknown)] object classObject,
        uint context,
        uint flags,
        out uint classCookie);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint classCookie);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(int threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetClassName(IntPtr window, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    /// <summary>Делегат обхода окон.</summary>
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    /// <summary>Сообщение Windows в собственном представлении.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        /// <summary>Дескриптор окна.</summary>
        public IntPtr Handle;

        /// <summary>Код сообщения.</summary>
        public int Message;

        /// <summary>Параметр WParam.</summary>
        public IntPtr WParam;

        /// <summary>Параметр LParam.</summary>
        public IntPtr LParam;

        /// <summary>Время публикации сообщения.</summary>
        public uint Time;

        /// <summary>Позиция курсора мыши.</summary>
        public Point Cursor;
    }
}
