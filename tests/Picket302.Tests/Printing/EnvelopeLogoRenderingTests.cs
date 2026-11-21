using System.Drawing;
using System.Drawing.Imaging;
using Picket302.Domain.Entities;
using Picket302.Printing;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;
using Picket302.Printing.Rendering;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Наложение растрового логотипа на конверт (ТЗ §8.4, функция 47:
/// «Логотип компании на конверте (растровый файл), режим “с логотипом /
/// без логотипа”»).
/// </summary>
/// <remarks>
/// Проверяются оба режима: вывод изображения в границы поля и полное
/// исключение логотипа при выключенном режиме, а также честное предупреждение
/// при недоступном или повреждённом файле логотипа.
/// </remarks>
public sealed class EnvelopeLogoRenderingTests
{
    [Fact]
    public void Логотип_ВРежимеБезЛоготипа_НаКонвертНеВыводится()
    {
        var фиксатор = new ФиксаторГрафики();
        var рендерер = new EnvelopeRenderer(new Измеритель());

        var предупреждения = рендерер.Render(
            фиксатор,
            Контекст(сЛоготипом: false, логотип: РастровыеБайты()),
            ПечатноеИзделие(),
            logo: new LogoRenderer());

        Assert.Empty(фиксатор.Изображения);
        Assert.DoesNotContain(предупреждения, текст => текст.Contains("Логотип", StringComparison.Ordinal));
    }

    [Fact]
    public void Логотип_ВРежимеСЛоготипом_ВыводитсяВГраницыПоля()
    {
        var фиксатор = new ФиксаторГрафики();
        var рендерер = new EnvelopeRenderer(new Измеритель());

        var предупреждения = рендерер.Render(
            фиксатор,
            Контекст(сЛоготипом: true, логотип: РастровыеБайты()),
            ПечатноеИзделие(),
            logo: new LogoRenderer());

        var изображение = Assert.Single(фиксатор.Изображения);

        Assert.Equal(40f, изображение.X, 2);
        Assert.Equal(80f, изображение.Y, 2);
        Assert.True(изображение.Width > 0f);
        Assert.True(изображение.Height > 0f);

        Assert.DoesNotContain(предупреждения, текст => текст.Contains("Логотип", StringComparison.Ordinal));
    }

    [Fact]
    public void Логотип_ФайлНедоступен_ВыдаётсяПонятноеПредупреждение()
    {
        var фиксатор = new ФиксаторГрафики();
        var рендерер = new EnvelopeRenderer(new Измеритель());

        var предупреждения = рендерер.Render(
            фиксатор,
            Контекст(сЛоготипом: true, логотип: null),
            ПечатноеИзделие(),
            logo: new LogoRenderer());

        Assert.Empty(фиксатор.Изображения);
        Assert.Contains(
            предупреждения,
            текст => текст.Contains("Логотип не выведен", StringComparison.Ordinal));
    }

    [Fact]
    public void Логотип_ПовреждённыйФайл_ВыводитсяЗаглушкаСПредупреждением()
    {
        var фиксатор = new ФиксаторГрафики();
        var рендерер = new EnvelopeRenderer(new Измеритель());

        var предупреждения = рендерер.Render(
            фиксатор,
            Контекст(сЛоготипом: true, логотип: [0x01, 0x02, 0x03]),
            ПечатноеИзделие(),
            logo: new LogoRenderer());

        Assert.Equal(1, фиксатор.ЗаполненныхПрямоугольников);
        Assert.Contains(
            предупреждения,
            текст => текст.Contains("Файл логотипа повреждён", StringComparison.Ordinal));
    }

    private static PrintableItem ПечатноеИзделие() =>
        new()
        {
            Id = 1,
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["[edt_str_to1]"] = "ООО Ромашка",
            },
        };

    private static PrintJobContext Контекст(bool сЛоготипом, byte[]? логотип) =>
        new()
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
            WithLogo = сЛоготипом,
            LogoBytes = логотип,
            Items = new[] { ПечатноеИзделие() },
            Fields = new[]
            {
                new BlankField
                {
                    Name = "[edt_str_to1]",
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
                    Name = "logo",
                    Kind = BlankFieldKind.Picture,
                    XMm = 40m,
                    YMm = 80m,
                    LengthMm = 30m,
                    HeightMm = 15m,
                },
            },
        };

    /// <summary>Готовит растровые байты логотипа в формате PNG.</summary>
    private static byte[] РастровыеБайты()
    {
        using var изображение = new Bitmap(40, 20, PixelFormat.Format32bppArgb);
        using var поток = new MemoryStream();
        изображение.Save(поток, ImageFormat.Png);

        return поток.ToArray();
    }

    /// <summary>Детерминированный измеритель текста (§11.2).</summary>
    private sealed class Измеритель : ITextMeasurer
    {
        public double MeasureWidth(string text, string fontName, decimal fontSizePt) =>
            text.Length * (double)fontSizePt * 0.5;

        public double MeasureLineHeight(string fontName, decimal fontSizePt) =>
            (double)fontSizePt * 1.2;
    }

    /// <summary>
    /// Фиксатор графического контекста: запоминает выведенные изображения и
    /// заполненные прямоугольники (заглушка повреждённого логотипа).
    /// </summary>
    private sealed class ФиксаторГрафики : IGraphicsContext
    {
        public List<(float X, float Y, float Width, float Height)> Изображения { get; } = new();

        public int ЗаполненныхПрямоугольников { get; private set; }

        public Color Color { get; set; } = Color.Black;

        public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm) =>
            ЗаполненныхПрямоугольников++;

        public void DrawStringMm(
            string text,
            string fontName,
            float widthMm,
            float heightMm,
            float xMm,
            float yMm)
        {
        }

        public void DrawStringMm(
            string text,
            string fontName,
            float sizePt,
            bool bold,
            float xMm,
            float yMm)
        {
        }

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }


            /// <summary>
            /// Отрисовка заданным семейством: фикстура записывает обращение так же,
            /// как и вариант с именем гарнитуры.
            /// </summary>
            public void DrawStringMm(
                string text,
                FontFamily family,
                float widthMm,
                float heightMm,
                float xMm,
                float yMm)
            {
                DrawStringMm(text, family.Name, widthMm, heightMm, xMm, yMm);
            }

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm) =>
            Изображения.Add((xMm, yMm, widthMm, heightMm));
    }
}
