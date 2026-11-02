using System.Drawing;
using Picket302.Domain.Entities;
using Picket302.Printing;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;
using Picket302.Printing.Preview;
using Picket302.Printing.Rendering;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Регрессия F-A7: невидимые поля (<c>IsVisible=false</c>) не выводятся
/// на печать и не попадают в предпросмотр. Прежде проверялась только
/// <c>IsPrintable</c>, и скрытое поле печаталось.
/// </summary>
public sealed class FieldVisibilityTests
{
    [Fact]
    public void Рендер_НевидимоеПоле_НеВыводится()
    {
        var фиксатор = new ФиксаторСтрок();
        var рендерер = new EnvelopeRenderer(new Измеритель());

        var предупреждения = рендерер.Render(фиксатор, Контекст(), Изделие());

        Assert.Empty(предупреждения);

        // Видимое поле отрисовано, скрытое — нет.
        Assert.Contains("Видимый текст", фиксатор.Строки);
        Assert.DoesNotContain("Скрытый текст", фиксатор.Строки);
    }

    [Fact]
    public void Предпросмотр_НевидимоеПоле_ИсключаетсяИзПолей()
    {
        using var документ = new PrintPreviewEngine(new EnvelopeRenderer(new Измеритель()))
            .RenderPreview(Контекст());

        var имена = документ.Fields.Select(поле => поле.Name).ToList();

        Assert.Contains("[поле_видимое]", имена);
        Assert.DoesNotContain("[поле_скрытое]", имена);
    }

    /// <summary>
    /// Контекст с двумя печатаемыми текстовыми полями: видимое и скрытое.
    /// </summary>
    private static PrintJobContext Контекст() => new()
    {
        TemplateName = "Конверт C6.lbk",
        Blank = new BlankMetrics
        {
            TemplateName = "Конверт C6.lbk",
            WidthMm = 114m,
            HeightMm = 162m,
            Orientation = TemplateOrientation.Portrait,
            PaperSizeName = "C6",
        },
        Printer = new PrinterDeviceInfo
        {
            PrinterName = "Picket302 Мок-Принтер",
            PaperSizeName = "C6",
            PaperWidthMm = 114m,
            PaperHeightMm = 162m,
            PrintableWidthMm = 110m,
            PrintableHeightMm = 158m,
        },
        Orientation = TemplateOrientation.Portrait,
        PaperSizeName = "C6",
        PaperWidthMm = 114m,
        PaperHeightMm = 162m,
        Items = [Изделие()],
        Fields =
        [
            new BlankField
            {
                Name = "[поле_видимое]",
                Kind = BlankFieldKind.Text,
                XMm = 8m,
                YMm = 20m,
                LengthMm = 60m,
                HeightMm = 12m,
                FontHeightPt = 10m,
                FontHeightMinPt = 6m,
                FontHeightMaxPt = 14m,
            },
            new BlankField
            {
                Name = "[поле_скрытое]",
                Kind = BlankFieldKind.Text,
                IsVisible = false,
                XMm = 8m,
                YMm = 40m,
                LengthMm = 60m,
                HeightMm = 12m,
                FontHeightPt = 10m,
                FontHeightMinPt = 6m,
                FontHeightMaxPt = 14m,
            },
        ],
    };

    private static PrintableItem Изделие() => new()
    {
        Id = 1,
        Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["[поле_видимое]"] = "Видимый текст",
            ["[поле_скрытое]"] = "Скрытый текст",
        },
    };

    /// <summary>Детерминированный измеритель текста (§11.2).</summary>
    private sealed class Измеритель : ITextMeasurer
    {
        public double MeasureWidth(string text, string fontName, decimal fontSizePt) =>
            text.Length * (double)fontSizePt * 0.5;

        public double MeasureLineHeight(string fontName, decimal fontSizePt) =>
            (double)fontSizePt * 1.2;
    }

    /// <summary>Фиксатор отрисованных строк.</summary>
    private sealed class ФиксаторСтрок : IGraphicsContext
    {
        public List<string> Строки { get; } = [];

        public Color Color { get; set; } = Color.Black;

        public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawStringMm(
            string text,
            string fontName,
            float widthMm,
            float heightMm,
            float xMm,
            float yMm) => Строки.Add(text);

        public void DrawStringMm(
            string text,
            string fontName,
            float sizePt,
            bool bold,
            float xMm,
            float yMm) => Строки.Add(text);

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }
    }
}
