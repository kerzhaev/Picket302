using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Picket302.Service.Hosting;

/// <summary>
/// Регистрация серверной службы в диспетчере служб Windows.
/// </summary>
/// <remarks>
/// <para>
/// Служба устанавливается под учётной записью с минимальными правами
/// <c>NT AUTHORITY\NetworkService</c>: ей нужны только чтение и запись
/// каталога данных и прослушивание порта локальной сети. <c>LocalSystem</c>
/// не используется.
/// </para>
/// <para>
/// Служба запускается автоматически при старте Windows и переживает выход
/// сотрудника из учётной записи.
/// </para>
/// <para>
/// Повторная регистрация (обновление поверх установленной копии) не считается
/// ошибкой: уже зарегистрированная служба перенастраивается на новый путь к
/// исполняемому файлу и на автозапуск. Данные и учётная запись при этом не
/// меняются.
/// </para>
/// </remarks>
public static class ServiceInstaller
{
    /// <summary>Отображаемое имя службы.</summary>
    public const string ОтображаемоеИмя = "Picket302 — серверная служба общей базы";

    /// <summary>Описание службы.</summary>
    public const string Описание =
        "Общая база адресов, отправлений, рассылок и шаблонов для 1–5 рабочих мест Picket302.";

    /// <summary>Учётная запись службы с минимальными правами.</summary>
    public const string УчётнаяЗапись = "NT AUTHORITY\\NetworkService";

    /// <summary>
    /// Код ошибки Windows «служба уже зарегистрирована».
    /// </summary>
    public const int ErrorServiceExists = 1073;

    /// <summary>
    /// Код ошибки Windows «служба не зарегистрирована».
    /// </summary>
    public const int ErrorServiceDoesNotExist = 1060;

    /// <summary>
    /// Код ошибки Windows «служба остановлена» (ожидаемое состояние при
    /// остановке уже остановленной службы).
    /// </summary>
    public const int ErrorServiceNotActive = 1062;

    private const uint SERVICE_AUTO_START = 2;
    private const uint SERVICE_ERROR_NORMAL = 1;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
    private const uint SERVICE_CHANGE_CONFIG = 0x0002;
    private const uint SERVICE_STOP = 0x0020;
    private const uint DELETE = 0x00010000;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateServiceW(
        IntPtr hSCManager,
        string lpServiceName,
        string lpDisplayName,
        uint dwDesiredAccess,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string lpBinaryPath,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfigW(
        IntPtr hService,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string? lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword,
        string? lpDisplayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig2W(IntPtr hService, uint dwInfoLevel, IntPtr lpInfo);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_DESCRIPTIONW
    {
        public string? lpDescription;
    }

    private const uint SERVICE_CONFIG_DESCRIPTION = 1;

    /// <summary>
    /// Регистрирует службу в диспетчере служб.
    /// </summary>
    /// <param name="путьКИсполняемому">Полный путь к исполняемому файлу службы.</param>
    /// <returns>Пояснение результата; <see langword="null"/> — установлено.</returns>
    public static string? Установить(string путьКИсполняемому)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьКИсполняемому);

        var менеджер = OpenSCManagerW(null, null, SC_MANAGER_CREATE_SERVICE);

        if (менеджер == IntPtr.Zero)
        {
            return new Win32Exception(Marshal.GetLastWin32Error()).Message
                + " Установка службы требует прав администратора.";
        }

        try
        {
            var служба = CreateServiceW(
                менеджер,
                WindowsServiceRunner.ИмяСлужбы,
                ОтображаемоеИмя,
                SERVICE_ALL_ACCESS,
                SERVICE_WIN32_OWN_PROCESS,
                SERVICE_AUTO_START,
                SERVICE_ERROR_NORMAL,
                $"\"{Path.GetFullPath(путьКИсполняемому)}\"",
                null,
                IntPtr.Zero,
                null,
                УчётнаяЗапись,
                null);

            if (служба == IntPtr.Zero)
            {
                var код = Marshal.GetLastWin32Error();

                if (УжеЗарегистрирована(код))
                {
                    // Обновление поверх установленной копии: перенастраивается
                    // уже зарегистрированная служба, а не создаётся вторая.
                    return Перенастроить(менеджер, путьКИсполняемому);
                }

                return new Win32Exception(код).Message;
            }

            try
            {
                // Описание службы: SCM отображает его в консоли управления.
                УстановитьОписание(служба);

                return null;
            }
            finally
            {
                CloseServiceHandle(служба);
            }
        }
        finally
        {
            CloseServiceHandle(менеджер);
        }
    }

    /// <summary>
    /// Определяет, означает ли код ошибки Windows, что служба уже
    /// зарегистрирована.
    /// </summary>
    /// <param name="кодОшибки">Код ошибки Windows.</param>
    /// <returns><see langword="true"/> — служба уже зарегистрирована.</returns>
    public static bool УжеЗарегистрирована(int кодОшибки) => кодОшибки == ErrorServiceExists;

    /// <summary>
    /// Определяет, означает ли код ошибки Windows, что служба не
    /// зарегистрирована.
    /// </summary>
    /// <param name="кодОшибки">Код ошибки Windows.</param>
    /// <returns><see langword="true"/> — службы нет.</returns>
    public static bool НеЗарегистрирована(int кодОшибки) => кодОшибки == ErrorServiceDoesNotExist;

    /// <summary>
    /// Перенастраивает уже зарегистрированную службу: путь к исполняемому
    /// файлу, автозапуск и описание.
    /// </summary>
    /// <param name="менеджер">Дескриптор диспетчера служб.</param>
    /// <param name="путьКИсполняемому">Полный путь к исполняемому файлу службы.</param>
    /// <returns>Пояснение результата; <see langword="null"/> — перенастроено.</returns>
    private static string? Перенастроить(IntPtr менеджер, string путьКИсполняемому)
    {
        var служба = OpenServiceW(менеджер, WindowsServiceRunner.ИмяСлужбы, SERVICE_CHANGE_CONFIG);

        if (служба == IntPtr.Zero)
        {
            return new Win32Exception(Marshal.GetLastWin32Error()).Message
                + " Обновление регистрации службы требует прав администратора.";
        }

        try
        {
            var изменено = ChangeServiceConfigW(
                служба,
                SERVICE_WIN32_OWN_PROCESS,
                SERVICE_AUTO_START,
                SERVICE_ERROR_NORMAL,
                $"\"{Path.GetFullPath(путьКИсполняемому)}\"",
                null,
                IntPtr.Zero,
                null,
                УчётнаяЗапись,
                null,
                null);

            if (!изменено)
            {
                return new Win32Exception(Marshal.GetLastWin32Error()).Message;
            }

            УстановитьОписание(служба);

            return null;
        }
        finally
        {
            CloseServiceHandle(служба);
        }
    }

    /// <summary>
    /// Записывает описание службы, видимое в консоли управления.
    /// </summary>
    /// <param name="служба">Дескриптор службы.</param>
    private static void УстановитьОписание(IntPtr служба)
    {
        var описание = new SERVICE_DESCRIPTIONW { lpDescription = Описание };
        var размер = Marshal.SizeOf<SERVICE_DESCRIPTIONW>();
        var указатель = Marshal.AllocHGlobal(размер);

        try
        {
            Marshal.StructureToPtr(описание, указатель, false);

            ChangeServiceConfig2W(служба, SERVICE_CONFIG_DESCRIPTION, указатель);
        }
        finally
        {
            Marshal.FreeHGlobal(указатель);
        }
    }

    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;

    private const uint SERVICE_CONTROL_STOP = 0x00000001;

    /// <summary>
    /// Снимает регистрацию службы.
    /// </summary>
    /// <returns>Пояснение результата; <see langword="null"/> — удалено.</returns>
    public static string? Удалить()
    {
        var менеджер = OpenSCManagerW(null, null, SC_MANAGER_CREATE_SERVICE);

        if (менеджер == IntPtr.Zero)
        {
            return new Win32Exception(Marshal.GetLastWin32Error()).Message
                + " Удаление службы требует прав администратора.";
        }

        try
        {
            var служба = OpenServiceW(менеджер, WindowsServiceRunner.ИмяСлужбы, DELETE | SERVICE_STOP);

            if (служба == IntPtr.Zero)
            {
                var код = Marshal.GetLastWin32Error();

                if (НеЗарегистрирована(код))
                {
                    // Службы нет — снимать нечего. Снятие регистрации
                    // считается выполненным: повторное удаление не должно
                    // считаться ошибкой.
                    return null;
                }

                return new Win32Exception(код).Message;
            }

            try
            {
                Остановить(служба);

                return DeleteService(служба)
                    ? null
                    : new Win32Exception(Marshal.GetLastWin32Error()).Message;
            }
            finally
            {
                CloseServiceHandle(служба);
            }
        }
        finally
        {
            CloseServiceHandle(менеджер);
        }
    }

    /// <summary>
    /// Просит диспетчер служб остановить службу.
    /// </summary>
    /// <param name="служба">Дескриптор службы.</param>
    /// <remarks>
    /// Уже остановленная служба — ожидаемое состояние, а не ошибка. Служба,
    /// остановленная не сразу, удаляется диспетчером после остановки: снятие
    /// регистрации помечает её на удаление.
    /// </remarks>
    private static void Остановить(IntPtr служба)
    {
        var статус = new SERVICE_STATUS();

        if (ControlService(служба, SERVICE_CONTROL_STOP, ref статус))
        {
            return;
        }

        var код = Marshal.GetLastWin32Error();

        if (код != ErrorServiceNotActive)
        {
            // Остановка не удалась по иной причине: снятие регистрации всё
            // равно выполняется, а служба будет удалена диспетчером после
            // остановки.
            return;
        }
    }
}
