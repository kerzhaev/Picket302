using System.Drawing;
using System.Drawing.Text;

namespace Picket302.Printing.Engine;

/// <summary>
/// Измеритель текста на базе GDI+ (система <c>System.Drawing</c>, §10.1).
/// </summary>
/// <remarks>
/// Измерения выполняются в миллиметрах: графический контекст переводится в
/// единицы <see cref="GraphicsUnit.Millimeter"/>, поэтому результат не зависит
/// от разрешения экрана.
/// </remarks>
public sealed class GdiTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly Bitmap _поверхность;
    private readonly Graphics _графика;
    private readonly StringFormat _формат = StringFormat.GenericTypographic;

    /// <summary>
    /// Создаёт измеритель текста.
    /// </summary>
    public GdiTextMeasurer()
    {
        _поверхность = new Bitmap(8, 8);
        _графика = Graphics.FromImage(_поверхность);
        _графика.PageUnit = GraphicsUnit.Millimeter;
        _графика.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    /// <inheritdoc />
    public double MeasureWidth(string text, string fontName, decimal fontSizePt)
    {
        using var шрифт = СоздатьШрифт(fontName, fontSizePt);

        var размер = _графика.MeasureString(text ?? string.Empty, шрифт, int.MaxValue, _формат);
        return размер.Width;
    }

    /// <inheritdoc />
    public double MeasureLineHeight(string fontName, decimal fontSizePt)
    {
        using var шрифт = СоздатьШрифт(fontName, fontSizePt);

        return шрифт.GetHeight(_графика);
    }

    /// <summary>Освобождает графический контекст.</summary>
    public void Dispose()
    {
        _графика.Dispose();
        _поверхность.Dispose();
        _формат.Dispose();
    }

    private static Font СоздатьШрифт(string fontName, decimal fontSizePt) =>
        new(
            string.IsNullOrWhiteSpace(fontName) ? "Tahoma" : fontName,
            (float)fontSizePt,
            FontStyle.Regular,
            GraphicsUnit.Point);
}
