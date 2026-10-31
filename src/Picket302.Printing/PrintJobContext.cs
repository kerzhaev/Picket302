using Picket302.Domain.Entities;
using Picket302.Domain.Postal;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;

namespace Picket302.Printing;

/// <summary>
/// Отправление, подготовленное к печати: подстановленные значения полей бланка
/// (ТЗ §6.1: «[Данные Mail] + [Бланк .lbk]»).
/// </summary>
public sealed record PrintableItem
{
    /// <summary>Идентификатор отправления.</summary>
    public long Id { get; init; }

    /// <summary>
    /// Логотип получателя (BLOB) для вывода на конверте; при отсутствии
    /// используется логотип сессии печати.
    /// </summary>
    public byte[]? LogoBytes { get; init; }

    /// <summary>Значения полей бланка по именам полей.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Контекст печатной сессии: бланк, данные, принтер и параметры печати
/// (ТЗ §6.1; §8.6, функции 79, 84, 86, 90).
/// </summary>
public sealed record PrintJobContext
{
    /// <summary>Идентификатор шаблона.</summary>
    public long? TemplateId { get; init; }

    /// <summary>Имя файла бланка.</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Метрики бланка.</summary>
    public BlankMetrics Blank { get; init; } = new()
    {
        TemplateName = string.Empty,
        WidthMm = 0,
        HeightMm = 0,
    };

    /// <summary>Поля бланка.</summary>
    public IReadOnlyList<BlankField> Fields { get; init; } = Array.Empty<BlankField>();

    /// <summary>Отправления к печати.</summary>
    public IReadOnlyList<PrintableItem> Items { get; init; } = Array.Empty<PrintableItem>();

    /// <summary>Сведения о принтере.</summary>
    public PrinterDeviceInfo Printer { get; init; } = new() { PrinterName = string.Empty };

    /// <summary>Ориентация макета.</summary>
    public TemplateOrientation Orientation { get; init; } = TemplateOrientation.Landscape;

    /// <summary>Имя формата носителя в терминах драйвера.</summary>
    public string PaperSizeName { get; init; } = string.Empty;

    /// <summary>Ширина носителя, мм.</summary>
    public decimal PaperWidthMm { get; init; }

    /// <summary>Высота носителя, мм.</summary>
    public decimal PaperHeightMm { get; init; }

    /// <summary>Код формата бумаги DEVMODE.dmPaperSize.</summary>
    public short DmPaperSize { get; init; }

    /// <summary>Гарнитура шрифта бланка.</summary>
    public string FontName { get; init; } = "Tahoma";

    /// <summary>Кегль шрифта бланка, пунктов.</summary>
    public decimal FontSizePt { get; init; } = 10m;

    /// <summary>Гарнитура шрифта кодового штампа.</summary>
    public string IndexFontName { get; init; } = "ZIPcode";

    /// <summary>Толщина линии элементов цифр кодового штампа, мм.</summary>
    public decimal IndexLineWidthMm { get; init; } = 0.45m;

    /// <summary>
    /// Число знаков кодового штампа: 6 для внутреннего отправления («Вн»),
    /// 3 для международного («Мн») — ТЗ §6.3, ГОСТ Р 51506-99.
    /// </summary>
    public int IndexDigitCount { get; init; } = PostIndexSpec.DefaultDigitCount;

    /// <summary>
    /// Индекс трёхзначного штампа международного отправления («555»).
    /// </summary>
    public string? InternationalIndex { get; init; }

    /// <summary>
    /// Территория отправления: внутреннее («Вн») или международное («Мн») —
    /// ТЗ §6.3, ГОСТ Р 51506-99.
    /// </summary>
    public EnvelopeTerritory Territory { get; init; } = EnvelopeTerritory.Domestic;

    /// <summary>
    /// Признак порядка адреса от мелкого к крупному для международных
    /// отправлений (ГОСТ Р 51506-99).
    /// </summary>
    public bool AddressOrderSmallToLarge { get; init; }

    /// <summary>
    /// Разрешение вывода QR-кода, DPI (300 или 600) — ТЗ §6.4.
    /// </summary>
    public int PrintDpi { get; init; } = 300;

    /// <summary>Калибровочное смещение по горизонтали, мм.</summary>
    public decimal OffsetXMm { get; init; }

    /// <summary>Калибровочное смещение по вертикали, мм.</summary>
    public decimal OffsetYMm { get; init; }

    /// <summary>Режим двусторонней печати.</summary>
    public DuplexMode Duplex { get; init; } = DuplexMode.Simplex;

    /// <summary>Фильтр чётных и нечётных страниц.</summary>
    public PageParity Parity { get; init; } = PageParity.All;

    /// <summary>Режим печати: <c>selected</c>, <c>batch_preview</c>, <c>batch_direct</c>, <c>test</c>.</summary>
    public string PrintMode { get; init; } = PrintModes.Selected;

    /// <summary>Число копий.</summary>
    public int Copies { get; init; } = 1;

    /// <summary>Признак режима «Зафиксировать размер A4».</summary>
    public bool FixA4 { get; init; }

    /// <summary>Признак режима «Размеченный типографский конверт».</summary>
    public bool Preprinted { get; init; }

    /// <summary>Признак печати с логотипом.</summary>
    public bool WithLogo { get; init; }

    /// <summary>Растровый логотип (BLOB).</summary>
    public byte[]? LogoBytes { get; init; }

    /// <summary>Ширина наклейки при спуске на лист, мм.</summary>
    public decimal LabelWidthMm { get; init; }

    /// <summary>Высота наклейки при спуске на лист, мм.</summary>
    public decimal LabelHeightMm { get; init; }

    /// <summary>Общее число страниц в задании.</summary>
    public int TotalPages { get; init; } = 1;

    /// <summary>Предупреждения сессии печати.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Номер выводимой страницы бланка. Заполняется спулером перед выводом
    /// страницы; генератор изображения выводит поля этой страницы.
    /// </summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>
    /// Признак формирования предпросмотра: в предпросмотре выводится маркер
    /// переполнения поля (ТЗ §8.6, функция 91), на бланке маркер не печатается.
    /// </summary>
    public bool IsPreview { get; init; }

    /// <summary>Имена полей, попадающих в обрезаемую зону.</summary>
    public IReadOnlyList<string> CroppedFieldNames { get; init; } = Array.Empty<string>();
}
