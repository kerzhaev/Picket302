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
        catch (Exception)
        {
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
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

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

        instance = new PEProApplication();
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

    /// <summary>Описание сервера, записываемое в реестр.</summary>
    public const string ServerDescription = "Picket302 — COM-сервер автоматизации";

    private const uint ClsCtxLocalServer = 0x00000004;
    private const uint RegClsMultipleUse = 0x00000002;

    private readonly ForegroundWindowService _foreground;

    /// <summary>
    /// Создаёт хост сервера.
    /// </summary>
    public ComServerHost()
    {
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

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var созданНовый);

        if (!созданНовый)
        {
            // Работающий экземпляр уже обслуживает запросы — активируем его
            // и завершаемся, не поднимая вторую копию сервера.
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

        if (результат < 0)
        {
            return unchecked((int)результат);
        }

        try
        {
            RunMessageLoop();
        }
        finally
        {
            CoRevokeClassObject(cookie);
        }

        return 0;
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

    private static void RunMessageLoop()
    {
        while (true)
        {
            var результат = GetMessage(out var message, IntPtr.Zero, 0, 0);

            if (результат <= 0)
            {
                break;
            }

            TranslateMessage(ref message);
            DispatchMessage(ref message);
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
