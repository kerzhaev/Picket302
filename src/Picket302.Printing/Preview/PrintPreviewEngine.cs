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
/// <remarks>
/// Документ владеет GDI-ресурсами страниц (метафайлов), поэтому освобождается
/// через <see cref="Dispose"/>; иначе дескрипторы GDI накапливаются при
/// каждом открытии окна предпросмотра.
/// </remarks>
public sealed record PreviewDocument : IDisposable
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

    /// <summary>
    /// Освобождает GDI-ресурсы страниц предпросмотра.
    /// </summary>
    public void Dispose()
    {
        foreach (var страница in Pages)
        {
            страница.Dispose();
        }

        GC.SuppressFinalize(this);
    }
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

        // Предпросмотр повторяет состав печати: невидимые поля не выводятся
        // так же, как непечатаемые (F-A7).
        var поля = context.Fields
            .Where(поле => поле.IsPrintable && поле.IsVisible)
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

        // Предпросмотр повторяет разбиение задания на страницы печати
        // («отправление × страница шаблона», ТЗ §8.6, функции 79–80): страница
        // задания раскладывается на номер отправления и номер страницы шаблона.
        var страницНаЭлемент = Math.Max(context.Blank.PageCount, 1);

        try
        {
            foreach (var страница in НомераСтраниц(context))
            {
                var метафайл = СоздатьМетафайл(ширинаМм, высотаМм);

                var индексЭлемента = context.Items.Count > 0
                    ? Math.Min((страница - 1) / страницНаЭлемент, context.Items.Count - 1)
                    : -1;
                var элемент = индексЭлемента >= 0
                    ? context.Items[индексЭлемента]
                    : new PrintableItem();

                // Предпросмотр формируется тем же конвейером рендеринга, что и
                // печать; отличие — маркер переполнения поля и номер страницы
                // шаблона (CurrentPage фильтрует поля по их странице).
                var контекстСтраницы = context with
                {
                    IsPreview = true,
                    CurrentPage = (страница - 1) % страницНаЭлемент + 1,
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
        // Рамка метафайла задаётся в ПИКСЕЛЯХ и совпадает с опорным
        // растровым изображением (страница при 96 dpi). Причина: содержимое
        // записывается в пикселях опорного контекста (PageUnit=Millimeter
        // пересчитывается в device-пиксели по 96 dpi), а DrawImage при
        // воспроизведении отображает ПИКСЕЛЬНОЕ пространство метафайла на
        // целевой прямоугольник. Рамка в миллиметрах давала пиксельное
        // пространство 3.216 px/мм при записи 3.78 px/мм — содержимое
        // воспроизводилось с искажением ×1.1754 от начала координат: текст
        // полей уходил правее/ниже своих координат и в предпросмотре, и в
        // печати (runtime-доказательство 2026-11-08, изолированное
        // воспроизведение: 324 мм → 1042 px = 3.216 px/мм против записи
        // 3.78 px/мм).
        var пикселейНаМм = 96f / 25.4f;
        var ширинаПикс = Math.Max((int)Math.Ceiling(ширинаМм * пикселейНаМм), 1);
        var высотаПикс = Math.Max((int)Math.Ceiling(высотаМм * пикселейНаМм), 1);

        using var опорный = new Bitmap(ширинаПикс, высотаПикс);
        using var опорнаяГрафика = Graphics.FromImage(опорный);

        var hdc = опорнаяГрафика.GetHdc();

        try
        {
            return new Metafile(
                hdc,
                new Rectangle(0, 0, ширинаПикс, высотаПикс),
                MetafileFrameUnit.Pixel,
                EmfType.EmfOnly);
        }
        finally
        {
            опорнаяГрафика.ReleaseHdc(hdc);
        }
    }
}
