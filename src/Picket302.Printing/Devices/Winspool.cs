using System.Runtime.InteropServices;

namespace Picket302.Printing.Devices;

/// <summary>
/// Вызовы системного диспетчера печати (ТЗ §6.2, §8.6, функция 78).
/// </summary>
/// <remarks>
/// Собственный драйвер принтера не создаётся: используются штатные вызовы
/// <c>winspool.drv</c> — <c>OpenPrinterW</c>, <c>DocumentPropertiesW</c>,
/// <c>DeviceCapabilitiesW</c> (ТЗ §10.1).
/// </remarks>
internal static class Winspool
{
    /// <summary>Режим <c>DM_OUT_BUFFER</c> для <c>DocumentPropertiesW</c>.</summary>
    public const int DM_OUT_BUFFER = 2;

    /// <summary>
    /// Режим <c>DM_IN_BUFFER</c> для <c>DocumentPropertiesW</c>: структура
    /// DEVMODE передаётся в драйвер (сохранение настроек принтера).
    /// </summary>
    public const int DM_IN_BUFFER = 1;

    /// <summary>
    /// Режим <c>DM_PROMPT</c> для <c>DocumentPropertiesW</c>: вывод диалога
    /// свойств принтера (кнопка «Настройки принтера», ТЗ §8.6, функция 78).
    /// </summary>
    public const int DM_PROMPT = 1;

    /// <summary>Флаги перечисления принтеров: все локальные и сетевые.</summary>
    public const uint PRINTER_ENUM_LOCAL = 0x00000002;

    /// <summary>Флаги перечисления принтеров: подключённые к порту.</summary>
    public const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;

    /// <summary>Уровень сведений о принтере.</summary>
    public const uint PRINTER_LEVEL_2 = 2;

    /// <summary>Признак принтера по умолчанию (winspool.h).</summary>
    public const uint PRINTER_ATTRIBUTE_DEFAULT = 0x00000004;

    /// <summary>Печать приостановлена (winspool.h).</summary>
    public const uint PRINTER_STATUS_PAUSED = 0x00000001;

    /// <summary>Принтер в состоянии ошибки (winspool.h).</summary>
    public const uint PRINTER_STATUS_ERROR = 0x00000002;

    /// <summary>Нет бумаги (winspool.h).</summary>
    public const uint PRINTER_STATUS_PAPEROUT = 0x00000010;

    /// <summary>Принтер вне сети (winspool.h).</summary>
    public const uint PRINTER_STATUS_OFFLINE = 0x00000080;

    /// <summary>Принтер недоступен (winspool.h).</summary>
    public const uint PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;

    /// <summary>Нет тонера (winspool.h).</summary>
    public const uint PRINTER_STATUS_NO_TONER = 0x00040000;

    /// <summary>
    /// Открывает принтер в спулере.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="handle">Дескриптор принтера.</param>
    /// <returns><see langword="true"/>, если принтер открыт.</returns>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool OpenPrinterW(string printerName, out IntPtr handle, IntPtr defaultDevMode);

    /// <summary>
    /// Закрывает дескриптор принтера.
    /// </summary>
    /// <param name="handle">Дескриптор принтера.</param>
    /// <returns><see langword="true"/>, если дескриптор закрыт.</returns>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr handle);

    /// <summary>
    /// Возвращает свойства и параметры принтера (системный диалог настройки).
    /// </summary>
    /// <param name="window">Дескриптор окна (может быть нулевым).</param>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="devModeOutput">Буфер с параметрами принтера.</param>
    /// <param name="devModeInput">Буфер с текущими параметрами (может быть нулевым).</param>
    /// <param name="mode">Режим вызова.</param>
    /// <returns>Код результата.</returns>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int DocumentPropertiesW(
        IntPtr window,
        IntPtr printer,
        string printerName,
        IntPtr devModeOutput,
        IntPtr devModeInput,
        int mode);

    /// <summary>
    /// Запрашивает возможности принтера (в том числе <c>DC_PAPERS</c>,
    /// <c>DC_PAPERSIZE</c> и <c>DC_DUPLEX</c>).
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="portName">Имя порта.</param>
    /// <param name="capability">Запрашиваемая возможность.</param>
    /// <param name="output">Буфер результата.</param>
    /// <param name="devMode">Буфер параметров принтера (может быть нулевым).</param>
    /// <returns>Число элементов в результате либо <c>-1</c> при ошибке.</returns>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int DeviceCapabilitiesW(
        string printerName,
        string? portName,
        int capability,
        IntPtr output,
        IntPtr devMode);

    /// <summary>
    /// Прерывает текущее задание печати (ТЗ §6.1: «с поддержкой прерывания
    /// AbortDoc»).
    /// </summary>
    /// <param name="printer">Дескриптор принтера.</param>
    /// <returns><see langword="true"/>, если задание прервано.</returns>
    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool AbortDoc(IntPtr printer);

    /// <summary>
    /// Перечисляет принтеры, установленные в системе.
    /// </summary>
    /// <param name="flags">Флаги перечисления.</param>
    /// <param name="name">Имя или описание (может быть пустым).</param>
    /// <param name="level">Уровень сведений.</param>
    /// <param name="printerEnum">Буфер со сведениями о принтерах.</param>
    /// <param name="bytesNeeded">Требуемый размер буфера.</param>
    /// <param name="count">Число возвращённых принтеров.</param>
    /// <returns><see langword="true"/>, если перечисление выполнено.</returns>
    /// <remarks>
    /// Сигнатура должна в точности соответствовать
    /// <c>BOOL EnumPrintersW(DWORD, LPWSTR, DWORD, LPBYTE, DWORD, LPDWORD, LPDWORD)</c>:
    /// при меньшем числе параметров вызов приводит к нарушению доступа к памяти.
    /// </remarks>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool EnumPrintersW(
        uint flags,
        IntPtr name,
        uint level,
        IntPtr printerEnum,
        uint bytesNeeded,
        ref uint count,
        ref uint printersCount);
}
