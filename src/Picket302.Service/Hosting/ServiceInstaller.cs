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

    private const uint SERVICE_AUTO_START = 2;
    private const uint SERVICE_ERROR_NORMAL = 1;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;
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
    private static extern bool ChangeServiceConfig2W(IntPtr hService, uint dwInfoLevel, IntPtr lpInfo);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

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
                var ошибка = new Win32Exception(Marshal.GetLastWin32Error()).Message;

                return ошибка;
            }

            try
            {
                // Описание службы: SCM отображает его в консоли управления.
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

    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;

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
            var служба = OpenServiceW(менеджер, WindowsServiceRunner.ИмяСлужбы, DELETE);

            if (служба == IntPtr.Zero)
            {
                return new Win32Exception(Marshal.GetLastWin32Error()).Message;
            }

            try
            {
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
}
