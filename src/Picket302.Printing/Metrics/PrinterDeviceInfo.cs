using Picket302.Printing.Devices;

namespace Picket302.Printing.Metrics;

/// <summary>
/// Сведения о принтере и его носителе, полученные от драйвера (ТЗ §6.2;
/// §8.6, функция 83).
/// </summary>
public sealed record PrinterDeviceInfo
{
    /// <summary>Имя принтера.</summary>
    public required string PrinterName { get; init; }

    /// <summary>Имя формата носителя в терминах драйвера.</summary>
    public string PaperSizeName { get; init; } = string.Empty;

    /// <summary>Ширина носителя, мм (DEVMODE.dmPaperWidth).</summary>
    public decimal PaperWidthMm { get; init; }

    /// <summary>Высота носителя, мм (DEVMODE.dmPaperLength).</summary>
    public decimal PaperHeightMm { get; init; }

    /// <summary>Ширина печатной области, мм (DC_PAPERSIZE).</summary>
    public decimal PrintableWidthMm { get; init; }

    /// <summary>Высота печатной области, мм (DC_PAPERSIZE).</summary>
    public decimal PrintableHeightMm { get; init; }

    /// <summary>Признак аппаратной поддержки двусторонней печати (DC_DUPLEX).</summary>
    public bool SupportsDuplex { get; init; }

    /// <summary>Лоток подачи по умолчанию.</summary>
    public string PaperSource { get; init; } = string.Empty;

    /// <summary>Код формата бумаги DEVMODE.dmPaperSize.</summary>
    public short DmPaperSize { get; init; }

    /// <summary>Ориентация DEVMODE.dmOrientation.</summary>
    public short DmOrientation { get; init; }

    /// <summary>Режим дуплекса DEVMODE.dmDuplex.</summary>
    public short DmDuplex { get; init; }

    /// <summary>Признак доступности принтера.</summary>
    public bool IsAvailable { get; init; } = true;

    /// <summary>Форматы носителя, поддерживаемые принтером.</summary>
    public IReadOnlyList<string> AvailablePaperSizes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Строит сведения о принтере по структуре DEVMODE и размерам печатной области.
    /// </summary>
    /// <param name="devMode">Структура DEVMODE драйвера.</param>
    /// <param name="printableWidthMm">Ширина печатной области, мм.</param>
    /// <param name="printableHeightMm">Высота печатной области, мм.</param>
    /// <returns>Сведения о принтере.</returns>
    public static PrinterDeviceInfo FromDevMode(
        DevMode devMode,
        decimal printableWidthMm,
        decimal printableHeightMm)
    {
        var ширина = devMode.PaperWidth > 0 ? devMode.PaperWidth / 10m : 0m;
        var высота = devMode.PaperLength > 0 ? devMode.PaperLength / 10m : 0m;

        var имяФормы = string.IsNullOrWhiteSpace(devMode.FormName)
            ? PaperSizeNameByCode(devMode.PaperSize)
            : devMode.FormName.Trim();

        return new PrinterDeviceInfo
        {
            PrinterName = devMode.DeviceName ?? string.Empty,
            PaperSizeName = имяФормы,
            PaperWidthMm = ширина,
            PaperHeightMm = высота,
            PrintableWidthMm = printableWidthMm,
            PrintableHeightMm = printableHeightMm,
            SupportsDuplex = devMode.Duplex is DevModeConstants.DMDUP_VERTICAL
                or DevModeConstants.DMDUP_HORIZONTAL,
            DmPaperSize = devMode.PaperSize,
            DmOrientation = devMode.Orientation,
            DmDuplex = devMode.Duplex,
        };
    }

    /// <summary>
    /// Возвращает имя формата по коду DEVMODE.dmPaperSize.
    /// </summary>
    /// <param name="paperSize">Код формата.</param>
    /// <returns>Имя формата либо <c>User</c> для пользовательского формата.</returns>
    public static string PaperSizeNameByCode(short paperSize) => paperSize switch
    {
        DevModeConstants.DMPAPER_LETTER => "Letter",
        DevModeConstants.DMPAPER_LEGAL => "Legal",
        DevModeConstants.DMPAPER_EXECUTIVE => "Executive",
        DevModeConstants.DMPAPER_A3 => "A3",
        DevModeConstants.DMPAPER_A4 => "A4",
        DevModeConstants.DMPAPER_A4SMALL => "A4Small",
        DevModeConstants.DMPAPER_A5 => "A5",
        DevModeConstants.DMPAPER_B4 => "B4",
        DevModeConstants.DMPAPER_B5 => "B5",
        DevModeConstants.DMPAPER_FOLIO => "Folio",
        DevModeConstants.DMPAPER_QUARTO => "Quarto",
        DevModeConstants.DMPAPER_ENV_INVITE => "EnvelopeInvite",
        DevModeConstants.DMPAPER_ENV_10 => "Envelope10",
        DevModeConstants.DMPAPER_ENV_11 => "Envelope11",
        DevModeConstants.DMPAPER_ENV_12 => "Envelope12",
        DevModeConstants.DMPAPER_ENV_9 => "Envelope9",
        DevModeConstants.DMPAPER_ENV_C5 => "C5",
        DevModeConstants.DMPAPER_ENV_C3 => "C3",
        DevModeConstants.DMPAPER_ENV_C4 => "C4",
        DevModeConstants.DMPAPER_ENV_C6 => "C6",
        DevModeConstants.DMPAPER_ENV_C65 => "C65",
        DevModeConstants.DMPAPER_USER => "User",
        _ => $"Paper{paperSize}",
    };
}
