using System.Drawing;
using System.Drawing.Drawing2D;
using Picket302.Printing.Elements;

namespace Picket302.Printing.Rendering;

/// <summary>
/// Графический контекст GDI+ с физическими единицами измерения (ТЗ §10.1).
/// </summary>
/// <remarks>
/// Контекст работает в миллиметрах и не сглаживает штрихи: для машиночитаемого
/// индекса и QR-кода это обязательное условие (ТЗ §6.3, §6.4).
/// </remarks>
public sealed class GdiGraphicsContext : IGraphicsContext
{
    private readonly Graphics _графика;

    /// <summary>
    /// Создаёт контекст поверх графического объекта GDI+.
    /// </summary>
    /// <param name="graphics">Графический объект.</param>
    public GdiGraphicsContext(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _графика = graphics;
        _графика.PageUnit = GraphicsUnit.Millimeter;
        _графика.InterpolationMode = InterpolationMode.NearestNeighbor;
        _графика.SmoothingMode = SmoothingMode.None;
        _графика.PixelOffsetMode = PixelOffsetMode.Half;
    }

    /// <inheritdoc />
    public Color Color { get; set; } = Color.Black;

    /// <inheritdoc />
    public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
    {
        using var кисть = new SolidBrush(Color);
        _графика.FillRectangle(кисть, xMm, yMm, widthMm, heightMm);
    }

    /// <inheritdoc />
    public void DrawStringMm(
        string text,
        string fontName,
        float widthMm,
        float heightMm,
        float xMm,
        float yMm)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using var шрифт = new Font(
            string.IsNullOrWhiteSpace(fontName) ? "Tahoma" : fontName,
            100f,
            FontStyle.Regular,
            GraphicsUnit.Point);

        Отрисовать(text, шрифт, widthMm, heightMm, xMm, yMm);
    }

    /// <inheritdoc />
    public void DrawStringMm(
        string text,
        FontFamily family,
        float widthMm,
        float heightMm,
        float xMm,
        float yMm)
    {
        ArgumentNullException.ThrowIfNull(family);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using var шрифт = new Font(family, 100f, FontStyle.Regular, GraphicsUnit.Point);

        Отрисовать(text, шрифт, widthMm, heightMm, xMm, yMm);
    }

    /// <summary>
    /// Рисует строку готовым шрифтом в заданный габарит (мм).
    /// </summary>
    private void Отрисовать(
        string text,
        Font шрифт,
        float widthMm,
        float heightMm,
        float xMm,
        float yMm)
    {
        var формат = StringFormat.GenericTypographic;
        var естественный = _графика.MeasureString(text, шрифт, int.MaxValue, формат);

        if (естественный.Width <= 0 || естественный.Height <= 0)
        {
            return;
        }

        var масштабX = widthMm / естественный.Width;
        var масштабY = heightMm / естественный.Height;

        var состояние = _графика.Save();

        // Сначала перенос начала координат в точку поля, затем сжатие
        // 100pt-кегля в размеры поля (F-A2): при обратном порядке перенесённая
        // точка умножалась на масштаб, и текст уходил со своего поля.
        _графика.TranslateTransform(xMm, yMm);
        _графика.ScaleTransform(масштабX, масштабY);

        using var кисть = new SolidBrush(Color);
        _графика.DrawString(text, шрифт, кисть, 0f, 0f, формат);

        _графика.Restore(состояние);
    }

    /// <inheritdoc />
    public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm)
    {
        ArgumentNullException.ThrowIfNull(image);

        _графика.DrawImage(image, new RectangleF(xMm, yMm, widthMm, heightMm));
    }

    /// <inheritdoc />
    public void DrawStringMm(string text, string fontName, float sizePt, bool bold, float xMm, float yMm)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using var шрифт = new Font(
            string.IsNullOrWhiteSpace(fontName) ? "Tahoma" : fontName,
            sizePt,
            bold ? FontStyle.Bold : FontStyle.Regular,
            GraphicsUnit.Point);

        using var кисть = new SolidBrush(Color);
        _графика.DrawString(text, шрифт, кисть, xMm, yMm, StringFormat.GenericTypographic);
    }

    /// <inheritdoc />
    public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
    {
        using var перо = new Pen(Color, 0.2f);
        _графика.DrawRectangle(перо, xMm, yMm, widthMm, heightMm);
    }
}
