using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Picket302.Printing.Metrics;

namespace Picket302.Printing.Devices;

/// <summary>
/// Состояние принтера для индикации в списке выбора (ТЗ §8.6, функция 78).
/// </summary>
public enum PrinterState
{
    /// <summary>Принтер готов к печати.</summary>
    Ready = 0,

    /// <summary>Принтер в состоянии ошибки.</summary>
    Error = 1,

    /// <summary>Принтер вне сети.</summary>
    Offline = 2,

    /// <summary>Принтер недоступен (нет бумаги, тонера, приостановлен).</summary>
    NotReady = 3,

    /// <summary>Принтер не установлен в системе.</summary>
    NotInstalled = 4,
}

/// <summary>
/// Сведения о принтере для диалога печати (ТЗ §8.6, функция 78).
/// </summary>
/// <param name="Name">Имя принтера.</param>
/// <param name="PortName">Имя порта.</param>
/// <param name="State">Состояние принтера.</param>
/// <param name="IsDefault">Признак принтера по умолчанию.</param>
public sealed record PrinterDescriptor(
    string Name,
    string PortName,
    PrinterState State,
    bool IsDefault);

/// <summary>
/// Выбор и настройка принтера (ТЗ §8.6, функция 78:
/// <c>Printing.Devices.PrinterManager</c>).
/// </summary>
public class PrinterManager
{
    /// <summary>Сообщение об отсутствии принтера по умолчанию.</summary>
    public const string NoDefaultPrinterMessage = "Принтер по умолчанию не установлен";

    /// <summary>Сообщение о недоступном принтере.</summary>
    public const string PrinterUnavailableMessage = "Принтер недоступен или отключен";

    /// <summary>Множитель перевода сотых долей дюйма в миллиметры.</summary>
    private const decimal ДюймВМиллиметры = 25.4m / 100m;

    /// <summary>
    /// Перечисляет принтеры, установленные в системе.
    /// </summary>
    /// <returns>Список принтеров с указанием состояния.</returns>
    public IReadOnlyList<PrinterDescriptor> EnumeratePrinters()
    {
        var флаги = Winspool.PRINTER_ENUM_LOCAL | Winspool.PRINTER_ENUM_CONNECTIONS;
        var нужныйРазмер = 0u;
        var количество = 0u;

        // Первый вызов — уточняющий: драйвер сообщает требуемый размер буфера.
        // Возвращаемое значение FALSE здесь штатно (ERROR_INSUFFICIENT_BUFFER).
        Winspool.EnumPrintersW(флаги, IntPtr.Zero, Winspool.PRINTER_LEVEL_2, IntPtr.Zero, 0u, ref нужныйРазмер, ref количество);

        if (нужныйРазмер == 0u)
        {
            return Array.Empty<PrinterDescriptor>();
        }

        var буфер = Marshal.AllocHGlobal((int)нужныйРазмер);

        try
        {
            if (!Winspool.EnumPrintersW(
                    флаги,
                    IntPtr.Zero,
                    Winspool.PRINTER_LEVEL_2,
                    буфер,
                    нужныйРазмер,
                    ref нужныйРазмер,
                    ref количество))
            {
                return Array.Empty<PrinterDescriptor>();
            }

            var принтеры = new List<PrinterDescriptor>((int)количество);
            var размер = Marshal.SizeOf<PrinterInfo2>();

            for (var индекс = 0; индекс < количество; индекс++)
            {
                var адрес = new IntPtr(буфер.ToInt64() + (индекс * размер));
                var сведения = Marshal.PtrToStructure<PrinterInfo2>(адрес);

                принтеры.Add(new PrinterDescriptor(
                    ПрочитатьСтроку(сведения.PrinterName),
                    ПрочитатьСтроку(сведения.PortName),
                    ОпределитьСостояние(сведения.Status),
                    (сведения.Attributes & Winspool.PRINTER_ATTRIBUTE_DEFAULT) != 0));
            }

            return принтеры;
        }
        finally
        {
            Marshal.FreeHGlobal(буфер);
        }
    }

    /// <summary>
    /// Возвращает состояние принтера.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Состояние принтера.</returns>
    public PrinterState QueryState(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        foreach (var принтер in EnumeratePrinters())
        {
            if (string.Equals(принтер.Name, printerName, StringComparison.OrdinalIgnoreCase))
            {
                return принтер.State;
            }
        }

        return PrinterState.NotInstalled;
    }

    /// <summary>
    /// Возвращает принтер по умолчанию.
    /// </summary>
    /// <returns>Принтер по умолчанию либо <see langword="null"/>.</returns>
    /// <remarks>
    /// Бит <c>PRINTER_ATTRIBUTE_DEFAULT</c> в перечислении
    /// <c>EnumPrintersW</c> сообщается не во всех контекстах сеанса
    /// (перенаправленные принтеры RDP), поэтому при отсутствии помеченного
    /// принтера имя по умолчанию запрашивается штатным
    /// <c>GetDefaultPrinterW</c> и сопоставляется с перечислением.
    /// </remarks>
    public PrinterDescriptor? GetDefaultPrinter()
    {
        var принтеры = EnumeratePrinters();

        foreach (var принтер in принтеры)
        {
            if (принтер.IsDefault)
            {
                return принтер;
            }
        }

        var имяПоУмолчанию = ПрочитатьИмяПринтераПоУмолчанию();

        if (имяПоУмолчанию is null)
        {
            return null;
        }

        return принтеры.FirstOrDefault(принтер => string.Equals(
            принтер.Name,
            имяПоУмолчанию,
            StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Читает имя принтера по умолчанию через <c>GetDefaultPrinterW</c>:
    /// первый вызов запрашивает размер, второй — имя.
    /// </summary>
    /// <returns>Имя принтера по умолчанию либо <see langword="null"/>.</returns>
    private static string? ПрочитатьИмяПринтераПоУмолчанию()
    {
        var размер = 0;

        // Первый вызов штатно возвращает FALSE с требуемым размером буфера.
        Winspool.GetDefaultPrinterW(null, ref размер);

        if (размер <= 0)
        {
            return null;
        }

        var имя = new System.Text.StringBuilder(размер);

        if (!Winspool.GetDefaultPrinterW(имя, ref размер))
        {
            return null;
        }

        return имя.ToString();
    }

    /// <summary>
    /// Читает DEVMODE принтера через <c>DocumentPropertiesW</c>.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Структура DEVMODE драйвера.</returns>
    public DevMode ReadDevMode(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        if (!Winspool.OpenPrinterW(printerName, out var дескриптор, IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"Не удалось открыть принтер «{printerName}»: {PrinterUnavailableMessage}.");
        }

        try
        {
            var размер = Marshal.SizeOf<DevMode>();
            var буфер = Marshal.AllocHGlobal(размер);

            try
            {
                var результат = Winspool.DocumentPropertiesW(
                    IntPtr.Zero,
                    дескриптор,
                    printerName,
                    буфер,
                    IntPtr.Zero,
                    Winspool.DM_OUT_BUFFER);

                if (результат < 0)
                {
                    throw new InvalidOperationException(
                        $"Не удалось получить параметры принтера «{printerName}».");
                }

                return Marshal.PtrToStructure<DevMode>(буфер);
            }
            finally
            {
                Marshal.FreeHGlobal(буфер);
            }
        }
        finally
        {
            Winspool.ClosePrinter(дескриптор);
        }
    }

    /// <summary>
    /// Открывает штатный диалог свойств принтера (<c>DocumentPropertiesW</c> в
    /// режиме <c>DM_PROMPT</c>) — кнопка «Настройки принтера»
    /// (ТЗ §8.6, функция 78).
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns><see langword="true"/>, если пользователь подтвердил изменение настроек.</returns>
    public bool ShowPrinterProperties(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        if (!Winspool.OpenPrinterW(printerName, out var дескриптор, IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"Не удалось открыть принтер «{printerName}»: {PrinterUnavailableMessage}.");
        }

        try
        {
            var результат = Winspool.DocumentPropertiesW(
                IntPtr.Zero,
                дескриптор,
                printerName,
                IntPtr.Zero,
                IntPtr.Zero,
                Winspool.DM_PROMPT);

            return результат >= 0;
        }
        finally
        {
            Winspool.ClosePrinter(дескриптор);
        }
    }

    /// <summary>
    /// Запрашивает у драйвера перечень поддерживаемых форматов бумаги.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Коды форматов DMPAPER, поддерживаемых драйвером.</returns>
    public IReadOnlyList<short> GetSupportedPaperSizes(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        var количество = Winspool.DeviceCapabilitiesW(
            printerName,
            null,
            DevModeConstants.DC_PAPERS,
            IntPtr.Zero,
            IntPtr.Zero);

        if (количество <= 0)
        {
            return Array.Empty<short>();
        }

        var буфер = Marshal.AllocHGlobal(количество * sizeof(short));

        try
        {
            var фактически = Winspool.DeviceCapabilitiesW(
                printerName,
                null,
                DevModeConstants.DC_PAPERS,
                буфер,
                IntPtr.Zero);

            if (фактически <= 0)
            {
                return Array.Empty<short>();
            }

            var форматы = new List<short>(фактически);

            for (var индекс = 0; индекс < фактически; индекс++)
            {
                форматы.Add(Marshal.ReadInt16(буфер, индекс * sizeof(short)));
            }

            return форматы;
        }
        finally
        {
            Marshal.FreeHGlobal(буфер);
        }
    }

    /// <summary>
    /// Возвращает наименования форматов бумаги, поддерживаемых принтером.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Список наименований форматов.</returns>
    public IReadOnlyList<string> GetSupportedPaperSizeNames(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        try
        {
            var настройки = new PrinterSettings { PrinterName = printerName };

            if (!настройки.IsValid)
            {
                return Array.Empty<string>();
            }

            var наименования = new List<string>();

            foreach (var элемент in настройки.PaperSizes)
            {
                var формат = (PaperSize)элемент;

                наименования.Add(string.IsNullOrWhiteSpace(формат.PaperName)
                    ? PrinterDeviceInfo.PaperSizeNameByCode((short)формат.RawKind)
                    : формат.PaperName);
            }

            return наименования;
        }
#pragma warning disable CA1031 // Ошибка опроса драйвера не должна прерывать печать.
        catch (Exception)
#pragma warning restore CA1031
        {
            var коды = GetSupportedPaperSizes(printerName);
            var наименования = new List<string>(коды.Count);

            foreach (var код in коды)
            {
                наименования.Add(PrinterDeviceInfo.PaperSizeNameByCode(код));
            }

            return наименования;
        }
    }

    /// <summary>
    /// Возвращает размер печатной области носителя, мм.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Ширина и высота печатной области, мм.</returns>
    /// <remarks>
    /// Размер печатной области определяется по контексту устройства драйвера
    /// (<c>GetDeviceCaps(HORZRES, VERTRES)</c> с пересчётом через
    /// <c>LOGPIXELSX/LOGPIXELSY</c>); при недоступности контекста
    /// используются настройки принтера .NET и резервный запрос
    /// <c>DeviceCapabilitiesW(DC_PAPERSIZE)</c>.
    /// </remarks>
    public (decimal Width, decimal Height) GetPrintableArea(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        var изКонтекста = PrintableAreaFromDeviceContext(printerName);
        if (изКонтекста.Width > 0m && изКонтекста.Height > 0m)
        {
            return изКонтекста;
        }

        return PaperSizeFromDriver(printerName);
    }

    /// <summary>
    /// Возвращает размер носителя текущего формата, мм.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Ширина и высота носителя, мм.</returns>
    /// <remarks>
    /// <para>Размер берётся из настроек принтера .NET: <c>PaperSize.Width</c> и
    /// <c>PaperSize.Height</c> заданы в <b>сотых долях дюйма</b>, поэтому
    /// перевод выполняется множителем <see cref="ДюймВМиллиметры"/>.</para>
    /// <para>Резервный запрос к драйверу — <c>DeviceCapabilitiesW(DC_PAPERSIZE)</c> —
    /// возвращает массив структур <c>POINT</c> с размерами в <b>десятых долях
    /// миллиметра</b>.</para>
    /// </remarks>
    public (decimal Width, decimal Height) PaperSizeFromDriver(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        var изНастроек = PaperSizeFromPrinterSettings(printerName);

        if (изНастроек.Width > 0m && изНастроек.Height > 0m)
        {
            return изНастроек;
        }

        return PaperSizeFromDeviceCapabilities(printerName);
    }

    /// <summary>
    /// Читает текущий формат носителя из настроек принтера .NET.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Размер носителя, мм; нули, если принтер недоступен.</returns>
    private static (decimal Width, decimal Height) PaperSizeFromPrinterSettings(string printerName)
    {
        try
        {
            var настройки = new PrinterSettings { PrinterName = printerName };

            if (!настройки.IsValid)
            {
                return (0m, 0m);
            }

            var формат = настройки.DefaultPageSettings.PaperSize;

            if (формат.Width <= 0 || формат.Height <= 0)
            {
                return (0m, 0m);
            }

            return (
                формат.Width * ДюймВМиллиметры,
                формат.Height * ДюймВМиллиметры);
        }
        catch (Exception ошибка) when (ошибка is System.Drawing.Printing.InvalidPrinterException
            or ArgumentException
            or System.ComponentModel.Win32Exception)
        {
            // Принтер мог быть удалён между перечислением и запросом.
            return (0m, 0m);
        }
    }

    /// <summary>
    /// Запрашивает у драйвера размеры форматов носителя
    /// (<c>DeviceCapabilitiesW(DC_PAPERSIZE)</c>).
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Размер носителя, мм; нули, если драйвер не ответил.</returns>
    /// <remarks>
    /// Драйвер возвращает массив структур <c>POINT</c>; размеры заданы в
    /// десятых долях миллиметра. Берётся первая точка массива.
    /// </remarks>
    private static (decimal Width, decimal Height) PaperSizeFromDeviceCapabilities(string printerName)
    {
        // Двухфазная схема DeviceCapabilitiesW (как в GetSupportedPaperSizes):
        // первый вызов с нулевым буфером возвращает число элементов массива
        // POINT, второй — заполняет буфер. Без первой фазы драйвер пишет массив
        // по числу ВСЕХ поддерживаемых форматов в буфер на одну POINT —
        // запись за границей выделенной памяти.
        var количество = Winspool.DeviceCapabilitiesW(
            printerName,
            null,
            DevModeConstants.DC_PAPERSIZE,
            IntPtr.Zero,
            IntPtr.Zero);

        if (количество <= 0)
        {
            return (0m, 0m);
        }

        var буфер = Marshal.AllocHGlobal(количество * sizeof(int) * 2);

        try
        {
            var фактически = Winspool.DeviceCapabilitiesW(
                printerName,
                null,
                DevModeConstants.DC_PAPERSIZE,
                буфер,
                IntPtr.Zero);

            if (фактически <= 0)
            {
                return (0m, 0m);
            }

            // POINT: два 32-разрядных значения — ширина и высота.
            var ширина = PrintUnits.TenthsOfMmToMm(Marshal.ReadInt32(буфер, 0));
            var высота = PrintUnits.TenthsOfMmToMm(Marshal.ReadInt32(буфер, sizeof(int)));

            if (ширина <= 0m || высота <= 0m)
            {
                return (0m, 0m);
            }

            return (ширина, высота);
        }
        catch (Exception ошибка) when (ошибка is DllNotFoundException
            or EntryPointNotFoundException
            or System.ComponentModel.Win32Exception)
        {
            return (0m, 0m);
        }
        finally
        {
            Marshal.FreeHGlobal(буфер);
        }
    }

    /// <summary>
    /// Запрашивает у драйвера признак поддержки двусторонней печати.
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns><see langword="true"/>, если двусторонняя печать поддерживается.</returns>
    /// <remarks>
    /// <c>DC_DUPLEX</c> возвращает число поддерживаемых режимов
    /// <c>DMDUP_*</c>; дополнительно учитывается признак
    /// <see cref="PrinterSettings.DuplexSupported"/>.
    /// </remarks>
    public bool SupportsDuplex(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        var буфер = Marshal.AllocHGlobal(sizeof(short));

        try
        {
            var фактически = Winspool.DeviceCapabilitiesW(
                printerName,
                null,
                DevModeConstants.DC_DUPLEX,
                буфер,
                IntPtr.Zero);

            if (фактически > 0)
            {
                return true;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(буфер);
        }

        try
        {
            var настройки = new PrinterSettings { PrinterName = printerName };
            return настройки.IsValid && настройки.Duplex != Duplex.Simplex;
        }
#pragma warning disable CA1031 // Ошибка опроса драйвера не должна прерывать печать.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>
    /// Собирает сведения о принтере и его носителе из реальных данных драйвера
    /// (ТЗ §6.2: DEVMODE и <c>DeviceCapabilitiesW</c>).
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="paperSize">Требуемый код формата бумаги DMPAPER.</param>
    /// <returns>Сведения о принтере.</returns>
    public virtual PrinterDeviceInfo GetDeviceInfo(string printerName, short paperSize = DevModeConstants.DMPAPER_USER)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        var devMode = ReadDevMode(printerName);
        var печатнаяОбласть = GetPrintableArea(printerName);

        var сведения = PrinterDeviceInfo.FromDevMode(devMode, печатнаяОбласть.Width, печатнаяОбласть.Height);

        return сведения with
        {
            PrinterName = printerName,
            PaperSizeName = string.IsNullOrWhiteSpace(сведения.PaperSizeName)
                ? PrinterDeviceInfo.PaperSizeNameByCode(paperSize)
                : сведения.PaperSizeName,
            DmPaperSize = devMode.PaperSize != 0 ? devMode.PaperSize : paperSize,
            SupportsDuplex = SupportsDuplex(printerName),
            PaperSource = GetPaperSource(devMode),
            AvailablePaperSizes = GetSupportedPaperSizeNames(printerName),
        };
    }

    /// <summary>
    /// Возвращает имя лотка подачи по умолчанию из DEVMODE.
    /// </summary>
    /// <param name="devMode">Структура DEVMODE.</param>
    /// <returns>Имя лотка либо пустая строка.</returns>
    private static string GetPaperSource(DevMode devMode) => devMode.DefaultSource switch
    {
        1 => "Upper Bin",
        2 => "Lower Bin",
        3 => "Upper Bin 1",
        4 => "Upper Bin 2",
        5 => "Upper Bin 3",
        6 => "Upper Bin 4",
        7 => "Upper Bin 5",
        8 => "Upper Bin 6",
        9 => "Lower Bin 1",
        10 => "Lower Bin 2",
        11 => "Lower Bin 3",
        12 => "Lower Bin 4",
        13 => "Lower Bin 5",
        14 => "Lower Bin 6",
        15 => "Side 1",
        16 => "Side 2",
        17 => "Side 3",
        18 => "Side 4",
        19 => "Side 5",
        20 => "Side 6",
        21 => "Manual Feed",
        _ => string.Empty,
    };

    /// <summary>
    /// Читает печатную область из контекста устройства драйвера (мм).
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <returns>Ширина и высота печатной области, мм.</returns>
    private static (decimal Width, decimal Height) PrintableAreaFromDeviceContext(string printerName)
    {
        var контекст = Gdi32.CreateDCW("WINSPOOL", printerName, null, IntPtr.Zero);

        if (контекст == IntPtr.Zero)
        {
            return (0m, 0m);
        }

        try
        {
            var горизонталь = Gdi32.GetDeviceCaps(контекст, Gdi32.HORZRES);
            var вертикаль = Gdi32.GetDeviceCaps(контекст, Gdi32.VERTRES);
            var точекНаДюймX = Gdi32.GetDeviceCaps(контекст, Gdi32.LOGPIXELSX);
            var точекНаДюймY = Gdi32.GetDeviceCaps(контекст, Gdi32.LOGPIXELSY);

            if (горизонталь <= 0 || вертикаль <= 0 || точекНаДюймX <= 0 || точекНаДюймY <= 0)
            {
                return (0m, 0m);
            }

            return (
                горизонталь * 25.4m / точекНаДюймX,
                вертикаль * 25.4m / точекНаДюймY);
        }
        finally
        {
            Gdi32.DeleteDC(контекст);
        }
    }

    private static string ПрочитатьСтроку(IntPtr указатель) =>
        указатель == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(указатель) ?? string.Empty;

    private static PrinterState ОпределитьСостояние(uint статус)
    {
        if (статус == 0)
        {
            return PrinterState.Ready;
        }

        if ((статус & (Winspool.PRINTER_STATUS_OFFLINE | Winspool.PRINTER_STATUS_NOT_AVAILABLE)) != 0)
        {
            return PrinterState.Offline;
        }

        if ((статус & Winspool.PRINTER_STATUS_ERROR) != 0)
        {
            return PrinterState.Error;
        }

        return PrinterState.NotReady;
    }

    /// <summary>
    /// Сведения о принтере уровня 2 (<c>PRINTER_INFO_2</c>).
    /// </summary>
    /// <remarks>
    /// Разметка повторяет <c>PRINTER_INFO_2W</c>: 13 указателей на строки и
    /// дескриптор безопасности, затем 8 значений <c>DWORD</c> (136 байт на
    /// x64). Прежде в структуре отсутствовали <c>Parameters</c> и
    /// <c>SecurityDescriptor</c>, и все поля после <c>Datatype</c> читались
    /// со смещением. Строки читаются по указателям вручную — автоматическая
    /// маршализация строк сместила бы поля структуры.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        /// <summary>Имя сервера.</summary>
        public IntPtr ServerName;

        /// <summary>Имя принтера.</summary>
        public IntPtr PrinterName;

        /// <summary>Имя общего ресурса.</summary>
        public IntPtr ShareName;

        /// <summary>Имя порта.</summary>
        public IntPtr PortName;

        /// <summary>Имя драйвера.</summary>
        public IntPtr DriverName;

        /// <summary>Комментарий.</summary>
        public IntPtr Comments;

        /// <summary>Расположение.</summary>
        public IntPtr Location;

        /// <summary>Указатель на DEVMODE.</summary>
        public IntPtr DevMode;

        /// <summary>Разделитель.</summary>
        public IntPtr Separator;

        /// <summary>Процессор печати.</summary>
        public IntPtr PrintProcessor;

        /// <summary>Тип данных.</summary>
        public IntPtr Datatype;

        /// <summary>Параметры драйвера печати.</summary>
        public IntPtr Parameters;

        /// <summary>Дескриптор безопасности.</summary>
        public IntPtr SecurityDescriptor;

        /// <summary>Атрибуты принтера.</summary>
        public uint Attributes;

        /// <summary>Приоритет.</summary>
        public uint Priority;

        /// <summary>Приоритет по умолчанию.</summary>
        public uint DefaultPriority;

        /// <summary>Время начала работы.</summary>
        public uint StartTime;

        /// <summary>Время окончания работы.</summary>
        public uint UntilTime;

        /// <summary>Состояние принтера.</summary>
        public uint Status;

        /// <summary>Число заданий в очереди.</summary>
        public uint CJobs;

        /// <summary>Число выполняемых заданий.</summary>
        public uint AJobs;
    }
}
