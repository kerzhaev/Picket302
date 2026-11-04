using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
/// Служба управления передним планом: вспомогательное окно создаётся в
/// отдельном STA-потоке с собственным циклом сообщений, поэтому перехват
/// фокуса работает и при вызове из потока COM (ТЗ §9.2).
/// </summary>
public sealed class ForegroundWindowService : IForegroundWindowService, IDisposable
{
    /// <summary>Класс вспомогательного окна — по нему находится работающий экземпляр сервера.</summary>
    public const string HelperWindowClassName = "Picket302ComAutomationHelper";

    /// <summary>Зарегистрированное сообщение активации работающего экземпляра.</summary>
    public const string ActivationMessageName = "PICKET302_COM_ACTIVATION";

    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _sync = new();
    private Thread? _thread;
    private Form? _form;
    private int _activationMessage;

    /// <summary>
    /// Создаёт службу и запускает поток вспомогательного окна.
    /// </summary>
    public ForegroundWindowService()
    {
        _activationMessage = RegisterWindowMessage(ActivationMessageName);

        _thread = new Thread(RunHelperThread)
        {
            IsBackground = true,
            Name = "Picket302.ComAutomation.Foreground",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Дескриптор вспомогательного окна; нулевой — окно не создано.</summary>
    public IntPtr HelperWindowHandle
    {
        get
        {
            var форма = _form;

            if (форма is null || !форма.IsHandleCreated)
            {
                return IntPtr.Zero;
            }

            return форма.Handle;
        }
    }

    /// <inheritdoc />
    public string OpenDummyFormToSetAsForeground()
    {
        var заголовок = ReadForegroundTitle();

        var форма = _form;

        if (форма is null)
        {
            return заголовок;
        }

        if (форма.InvokeRequired)
        {
            форма.Invoke(() => ShowHelper(форма));
        }
        else
        {
            ShowHelper(форма);
        }

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

        var форма = _form;

        if (форма is not null && форма.InvokeRequired)
        {
            форма.Invoke(() => форма.Hide());
        }
        else
        {
            форма?.Hide();
        }
    }

    /// <summary>
    /// Освобождает ресурсы службы.
    /// </summary>
    public void Dispose()
    {
        var форма = _form;

        if (форма is not null && форма.InvokeRequired)
        {
            try
            {
                форма.Invoke(() => форма.Close());
            }
            catch (Exception)
            {
                // Поток вспомогательного окна уже завершён — закрывать нечего.
            }
        }
        else
        {
            форма?.Close();
        }

        _ready.Dispose();
    }

    private void RunHelperThread()
    {
        try
        {
            var форма = new HelperForm(this);

            _form = форма;
            _ready.Set();

            System.Windows.Forms.Application.Run(форма);
        }
        catch (Exception ошибка)
        {
            // Ошибка создания вспомогательного окна не должна оставаться
            // невидимой: без окна не работают ни активация работающего
            // экземпляра, ни плавное завершение сервера.
            AutomationServerLog.Write(
                "Поток вспомогательного окна завершился с ошибкой: " + ошибка);

            _ready.Set();
        }
    }

    /// <summary>
    /// Вспомогательное окно сервера: невидимое, без рамки и без задачи в
    /// списке задач; перехватывает сообщение активации работающего экземпляра.
    /// </summary>
    private sealed class HelperForm : Form
    {
        private readonly ForegroundWindowService _служба;

        /// <summary>
        /// Создаёт вспомогательное окно.
        /// </summary>
        /// <param name="служба">Служба управления передним планом.</param>
        public HelperForm(ForegroundWindowService служба)
        {
            _служба = служба;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Opacity = 0d;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Text = "Picket302 — COM-автоматизация";

            CreateParams.ClassName = HelperWindowClassName;
        }

        /// <inheritdoc />
        protected override void WndProc(ref Message message)
        {
            _служба.HandleActivation(ref message);

            base.WndProc(ref message);
        }
    }

    private void ShowHelper(Form форма)
    {
        if (!форма.Visible)
        {
            форма.Show();
        }

        форма.Activate();
        форма.BringToFront();

        SetForegroundWindow(форма.Handle);
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

    /// <summary>
    /// Обрабатывает сообщение активации: возвращает фокус вспомогательному окну.
    /// </summary>
    /// <param name="message">Сообщение Windows.</param>
    internal void HandleActivation(ref Message message)
    {
        if (_activationMessage == 0 || message.Msg != _activationMessage)
        {
            return;
        }

        var форма = _form;

        if (форма is null || !форма.IsHandleCreated)
        {
            return;
        }

        if (форма.InvokeRequired)
        {
            форма.BeginInvoke(() => SetForegroundWindow(форма.Handle));
        }
        else
        {
            SetForegroundWindow(форма.Handle);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(int threadId, uint message, IntPtr wParam, IntPtr lParam);

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
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    /// <summary>Делегат обхода окон.</summary>
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

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
[ComVisible(true)]
public sealed class ComClassFactory : IClassFactory
{
    /// <summary>Единственная служба переднего плана на процесс сервера (F-E24):
    /// каждый экземпляр PEProApplication получает один и тот же STA-поток
    /// вспомогательного окна, а не собственный.</summary>
    private static readonly ForegroundWindowService ОбщаяСлужбаПереднегоПлана = new();

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
    /// Идентификатор главного потока сервера (регистрация фабрики и цикл
    /// сообщений). Служба переднего плана доставляет на него WM_QUIT при
    /// плавном завершении.
    /// </summary>
    internal static volatile int MainThreadId;

    private readonly ForegroundWindowService _foreground;

    /// <summary>
    /// Создаёт хост сервера.
    /// </summary>
    public ComServerHost()
    {
        MainThreadId = Environment.CurrentManagedThreadId;

        _foreground = new ForegroundWindowService();
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
        var окно = FindWindow(ForegroundWindowService.HelperWindowClassName, null);

        if (окно == IntPtr.Zero)
        {
            return 0;
        }

        var сообщение = RegisterWindowMessage(ForegroundWindowService.ActivationMessageName);

        if (сообщение == 0)
        {
            return 0;
        }

        PostMessage(окно, сообщение, IntPtr.Zero, IntPtr.Zero);

        return 0;
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
        var дескрипторы = new[] { сигналЗавершения.SafeWaitHandle.DangerousGetHandle() };

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
                // Сигнал плавного завершения получен.
                return;
            }

            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, PmRemove))
            {
                if (message.Message == WmQuit)
                {
                    return;
                }

                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MsgWaitForMultipleObjectsEx(
        int count,
        IntPtr[] handles,
        uint timeout,
        uint wakeMask,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr window,
        uint filterMin,
        uint filterMax,
        uint remove);

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
