using System.Drawing;
using System.Drawing.Imaging;
using Picket302.Domain.Entities;
using Picket302.Printing.Elements;
using Picket302.Printing.Rendering;

namespace Picket302.Printing.Preview;

/// <summary>
/// Поле бланка, показанное в предпросмотре (ТЗ §8.6, функция 80).
/// </summary>
/// <param name="Name">Имя поля.</param>
/// <param name="Kind">Вид поля.</param>
/// <param name="Bounds">Границы поля с учётом калибровочных смещений, мм.</param>
/// <param name="Text">Подставленное значение.</param>
public sealed record PreviewField(string Name, string Kind, RectangleF Bounds, string Text);

/// <summary>
/// Документ предпросмотра печати (ТЗ §8.6, функция 80:
/// <c>Printing.Preview.PrintPreviewEngine.RenderPreview</c>).
/// </summary>
public sealed record PreviewDocument
{
    /// <summary>Страницы предпросмотра в векторном виде (метафайлы).</summary>
    public IReadOnlyList<Metafile> Pages { get; init; } = Array.Empty<Metafile>();

    /// <summary>Размер страницы, мм.</summary>
    public SizeF PageSizeMm { get; init; }

    /// <summary>Границы печатной области принтера, мм.</summary>
    public RectangleF PrintableAreaMm { get; init; }

    /// <summary>Поля бланка с координатами.</summary>
    public IReadOnlyList<PreviewField> Fields { get; init; } = Array.Empty<PreviewField>();

    /// <summary>Калибровочное смещение по горизонтали, мм.</summary>
    public decimal OffsetXMm { get; init; }

    /// <summary>Калибровочное смещение по вертикали, мм.</summary>
    public decimal OffsetYMm { get; init; }

    /// <summary>Текст баннера о расхождении метрик.</summary>
    public string? BannerMessage { get; init; }

    /// <summary>Признак блокирующего несоответствия: предпросмотр недоступен.</summary>
    public bool IsBlocked { get; init; }
}

/// <summary>
/// Предпросмотр печати (ТЗ §8.6, функция 80).
/// </summary>
/// <remarks>
/// Предпросмотр формируется тем же конвейером рендеринга, что и физическая
/// печать, и показывает поля и их координаты, калибровочные смещения, границы
/// печатной области принтера и рамки зон индекса. Масштабирование предпросмотра
/// не влияет на координаты печати.
/// </remarks>
public sealed class PrintPreviewEngine
{
    private readonly EnvelopeRenderer _рендерер;
    private readonly PostIndexRenderer? _кодовыйШтамп;
    private readonly QrCodeRenderer? _qr;
    private readonly LogoRenderer? _логотип;

    /// <summary>
    /// Создаёт движок предпросмотра.
    /// </summary>
    /// <param name="renderer">Генератор изображения бланка.</param>
    /// <param name="postIndex">Отрисовщик кодового штампа.</param>
    /// <param name="qrCode">Отрисовщик QR-кода.</param>
    /// <param name="logo">Отрисовщик логотипа.</param>
    public PrintPreviewEngine(
        EnvelopeRenderer renderer,
        PostIndexRenderer? postIndex = null,
        QrCodeRenderer? qrCode = null,
        LogoRenderer? logo = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        _рендерер = renderer;
        _кодовыйШтамп = postIndex;
        _qr = qrCode;
        _логотип = logo;
    }

    /// <summary>
    /// Формирует документ предпросмотра.
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <returns>Документ предпросмотра.</returns>
    public PreviewDocument RenderPreview(PrintJobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ширинаМм = (float)(context.PaperWidthMm > 0 ? context.PaperWidthMm : context.Blank.WidthMm);
        var высотаМм = (float)(context.PaperHeightMm > 0 ? context.PaperHeightMm : context.Blank.HeightMm);

        var печатнаяОбласть = new RectangleF(
            0f,
            0f,
            (float)context.Printer.PrintableWidthMm,
            (float)context.Printer.PrintableHeightMm);

        var поля = context.Fields
            .Where(поле => поле.IsPrintable)
            .Select(поле => new PreviewField(
                поле.Name,
                поле.Kind,
                new RectangleF(
                    (float)(поле.XMm + context.OffsetXMm),
                    (float)(поле.YMm + context.OffsetYMm),
                    (float)поле.LengthMm,
                    (float)поле.HeightMm),
                string.Empty))
            .ToList();

        var страницы = new List<Metafile>();
        var элемент = context.Items.Count > 0 ? context.Items[0] : new PrintableItem();

        try
        {
            foreach (var страница in НомераСтраниц(context))
            {
                var метафайл = СоздатьМетафайл(ширинаМм, высотаМм);

                // Предпросмотр формируется тем же конвейером рендеринга, что и
                // печать; отличие — маркер переполнения поля и номер страницы.
                var контекстСтраницы = context with
                {
                    IsPreview = true,
                    CurrentPage = страница,
                };

                using (var графика = Graphics.FromImage(метафайл))
                {
                    var графическийКонтекст = new GdiGraphicsContext(графика);
                    _рендерер.Render(графическийКонтекст, контекстСтраницы, элемент, _кодовыйШтамп, _qr, _логотип);
                }

                страницы.Add(метафайл);
            }
        }
        catch
        {
            foreach (var страница in страницы)
            {
                страница.Dispose();
            }

            throw;
        }

        return new PreviewDocument
        {
            Pages = страницы,
            PageSizeMm = new SizeF(ширинаМм, высотаМм),
            PrintableAreaMm = печатнаяОбласть,
            Fields = поля,
            OffsetXMm = context.OffsetXMm,
            OffsetYMm = context.OffsetYMm,
            BannerMessage = context.Warnings.Count > 0 ? string.Join(" ", context.Warnings) : null,
            IsBlocked = false,
        };
    }

    private static IEnumerable<int> НомераСтраниц(PrintJobContext context)
    {
        var фильтр = new Engine.PageParityFilter();
        var номера = фильтр.FilterPageNumbers(Math.Max(context.TotalPages, 1), context.Parity).ToList();

        return номера.Count > 0 ? номера : new List<int> { 1 };
    }

    private static Metafile СоздатьМетафайл(float ширинаМм, float высотаМм)
    {
        using var опорный = new Bitmap(1, 1);
        using var опорнаяГрафика = Graphics.FromImage(опорный);

        var hdc = опорнаяГрафика.GetHdc();

        try
        {
            return new Metafile(
                hdc,
                new RectangleF(0f, 0f, ширинаМм, высотаМм),
                MetafileFrameUnit.Millimeter,
                EmfType.EmfOnly);
        }
        finally
        {
            опорнаяГрафика.ReleaseHdc(hdc);
        }
    }
}
