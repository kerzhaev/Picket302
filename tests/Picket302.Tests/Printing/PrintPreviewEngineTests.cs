using System.Drawing;
using Picket302.Domain.Entities;
using Picket302.Printing;
using Picket302.Printing.Engine;
using Picket302.Printing.Elements;
using Picket302.Printing.Metrics;
using Picket302.Printing.Preview;
using Picket302.Printing.Rendering;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Предпросмотр печати (ТЗ §8.6, функция 80; критерий приёмки №1 заказа —
/// «Конверт C6 печатается с совпадением до ±1 мм с предпросмотром»).
/// </summary>
/// <remarks>
/// Автоматизируемая часть критерия: предпросмотр и печать строятся одним и тем
/// же конвейером рендеринга (<see cref="EnvelopeRenderer"/>), поэтому координаты
/// полей в предпросмотре должны совпадать с координатами вывода на печати.
/// Тест фиксирует вызовы графического контекста печатного пути и сопоставляет
/// их с полями документа предпросмотра в пределах допуска 1 мм.
/// Физическая сверка ±1 мм на трёх реальных принтерах (A4-офисный, лотковый
/// конвертный, термопринтер) — стендовая процедура ТЗ §11.1; она не
/// автоматизируется и оформлена в docs/ACCEPTANCE.md как фиксация.
/// </remarks>
public sealed class PrintPreviewEngineTests
{
    /// <summary>Допустимое расхождение координат, мм (критерий приёмки №1).</summary>
    private const float ДопускМм = 1f;

    /// <summary>Три профиля принтеров стендового прогона (ТЗ §11.1).</summary>
    public static TheoryData<string, decimal, decimal, decimal, decimal> ПрофилиПринтеров() =>
        new()
        {
            { "A4-офисный", 210m, 297m, 200m, 287m },
            { "Лотковый конвертный", 114m, 162m, 110m, 158m },
            { "Термопринтер", 80m, 200m, 76m, 196m },
        };

    [Theory]
    [MemberData(nameof(ПрофилиПринтеров))]
    public void Предпросмотр_ИПечать_ДаютОдинаковыеКоординатыПолей(
        string имяПринтера,
        decimal ширинаБумаги,
        decimal высотаБумаги,
        decimal печатнаяШирина,
        decimal печатнаяВысота)
    {
        var контекст = КонтекстC6(Принтер(
            имяПринтера,
            ширинаБумаги,
            высотаБумаги,
            печатнаяШирина,
            печатнаяВысота));

        var фиксатор = new ФиксаторГрафики();
        var рендерер = new EnvelopeRenderer(new Измеритель());

        рендерер.Render(фиксатор, контекст, контекст.Items[0]);

        using var документ = new PrintPreviewEngine(рендерер).RenderPreview(контекст);

        Assert.Equal(контекст.Fields.Count, документ.Fields.Count);
        Assert.Equal(контекст.Fields.Count, фиксатор.Позиции.Count);

        foreach (var поле in документ.Fields)
        {
            var ожидаемыйТекст = контекст.Items[0].Values[поле.Name];
            var выведено = Assert.Single(
                фиксатор.Позиции,
                позиция => позиция.Текст == ожидаемыйТекст);

            Assert.True(
                Math.Abs(выведено.X - поле.Bounds.X) <= ДопускМм,
                $"Поле «{поле.Name}»: расхождение по X между предпросмотром и печатью " +
                $"{Math.Abs(выведено.X - поле.Bounds.X):F2} мм превышает {ДопускМм} мм.");

            Assert.True(
                Math.Abs(выведено.Y - поле.Bounds.Y) <= ДопускМм,
                $"Поле «{поле.Name}»: расхождение по Y между предпросмотром и печатью " +
                $"{Math.Abs(выведено.Y - поле.Bounds.Y):F2} мм превышает {ДопускМм} мм.");

            var образец = контекст.Fields.Single(п => п.Name == поле.Name);
            Assert.Equal((float)образец.LengthMm, поле.Bounds.Width, 2);
        }
    }

    [Fact]
    public void Предпросмотр_КалибровочноеСмещение_ПрименяетсяИВПредпросмотреИНаПечати()
    {
        var контекст = КонтекстC6(Принтер()) with
        {
            OffsetXMm = 2.5m,
            OffsetYMm = -1.5m,
        };

        var фиксатор = new ФиксаторГрафики();
        var рендерер = new EnvelopeRenderer(new Измеритель());

        рендерер.Render(фиксатор, контекст, контекст.Items[0]);

        using var документ = new PrintPreviewEngine(рендерер).RenderPreview(контекст);

        Assert.Equal(2.5m, документ.OffsetXMm);
        Assert.Equal(-1.5m, документ.OffsetYMm);

        foreach (var поле in документ.Fields)
        {
            var образец = контекст.Fields.Single(п => п.Name == поле.Name);
            var выведено = Assert.Single(
                фиксатор.Позиции,
                позиция => позиция.Текст == контекст.Items[0].Values[поле.Name]);

            Assert.Equal((float)(образец.XMm + 2.5m), поле.Bounds.X, 2);
            Assert.Equal((float)(образец.YMm - 1.5m), поле.Bounds.Y, 2);
            Assert.Equal(выведено.X, поле.Bounds.X, 2);
            Assert.Equal(выведено.Y, поле.Bounds.Y, 2);
        }
    }

    [Fact]
    public void Предпросмотр_ПечатнаяОбласть_СовпадаетСДаннымиПринтера()
    {
        var принтер = Принтер();
        var контекст = КонтекстC6(принтер);

        using var документ = new PrintPreviewEngine(new EnvelopeRenderer(new Измеритель()))
            .RenderPreview(контекст);

        Assert.Equal((float)принтер.PrintableWidthMm, документ.PrintableAreaMm.Width, 2);
        Assert.Equal((float)принтер.PrintableHeightMm, документ.PrintableAreaMm.Height, 2);
    }

    [Fact]
    public void Предпросмотр_ФорматСтраницы_СовпадаетСФорматомБланкаC6()
    {
        var контекст = КонтекстC6(Принтер());

        using var документ = new PrintPreviewEngine(new EnvelopeRenderer(new Измеритель()))
            .RenderPreview(контекст);

        Assert.Equal(114f, документ.PageSizeMm.Width, 2);
        Assert.Equal(162f, документ.PageSizeMm.Height, 2);
    }

    [Fact]
    public void Предпросмотр_ПартияОтправлений_СтраницаЗаданияРаскладываетсяНаЭлементИСтраницуШаблона()
    {
        // ТЗ §8.6, функции 79-80: страница задания = "отправление x страница
        // шаблона". Партия из двух отправлений на двухстраничном шаблоне даёт
        // четыре страницы предпросмотра (регрессия: все элементы партии
        // рисовались поверх одной страницы шаблона).
        var базовый = КонтекстC6(Принтер());
        var контекст = базовый with
        {
            Blank = new BlankMetrics
            {
                TemplateName = базовый.Blank.TemplateName,
                WidthMm = базовый.Blank.WidthMm,
                HeightMm = базовый.Blank.HeightMm,
                Orientation = базовый.Blank.Orientation,
                PaperSizeName = базовый.Blank.PaperSizeName,
                PageCount = 2,
            },
            TotalPages = 4,
            Items = new[]
            {
                базовый.Items[0],
                new PrintableItem
                {
                    Id = 2,
                    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["[edt_str_to1]"] = "ИП Сидоров",
                        ["[edt_str_to2]"] = "Тверь",
                    },
                },
            },
        };

        using var документ = new PrintPreviewEngine(new EnvelopeRenderer(new Измеритель()))
            .RenderPreview(контекст);

        Assert.Equal(4, документ.Pages.Count);
    }

    [Fact]
    public void Предпросмотр_ТолькоЧётныеСтраницы_ФормируетСоответствующееЧислоСтраниц()
    {
        var контекст = КонтекстC6(Принтер()) with
        {
            TotalPages = 4,
            Parity = PageParity.EvenOnly,
        };

        using var документ = new PrintPreviewEngine(new EnvelopeRenderer(new Измеритель()))
            .RenderPreview(контекст);

        Assert.Equal(2, документ.Pages.Count);
    }

    [Fact]
    public void Предпросмотр_ПредупрежденияСессии_ВыносятсяВБаннер()
    {
        var контекст = КонтекстC6(Принтер()) with
        {
            Warnings = new[] { "Конверт слишком большой для выбранного принтера." },
        };

        using var документ = new PrintPreviewEngine(new EnvelopeRenderer(new Измеритель()))
            .RenderPreview(контекст);

        Assert.NotNull(документ.BannerMessage);
        Assert.Contains("Конверт слишком большой", документ.BannerMessage);
    }

    [Fact]
    public void Предпросмотр_НепечатаемоеПоле_НеПоказывается()
    {
        var контекст = КонтекстC6(Принтер());

        var поля = контекст.Fields.ToList();
        поля.Add(new BlankField
        {
            Name = "служебное",
            Kind = BlankFieldKind.Text,
            XMm = 5m,
            YMm = 5m,
            LengthMm = 20m,
            HeightMm = 5m,
            IsPrintable = false,
        });

        using var документ = new PrintPreviewEngine(new EnvelopeRenderer(new Измеритель()))
            .RenderPreview(контекст with { Fields = поля });

        Assert.DoesNotContain(документ.Fields, поле => поле.Name == "служебное");
    }

    private static PrinterDeviceInfo Принтер(
        string имя = "Picket302 Мок-Принтер",
        decimal ширина = 114m,
        decimal высота = 162m,
        decimal печатнаяШирина = 110m,
        decimal печатнаяВысота = 158m) =>
        new()
        {
            PrinterName = имя,
            PaperSizeName = "C6",
            PaperWidthMm = ширина,
            PaperHeightMm = высота,
            PrintableWidthMm = печатнаяШирина,
            PrintableHeightMm = печатнаяВысота,
        };

    private static PrintJobContext КонтекстC6(PrinterDeviceInfo принтер) =>
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
            Printer = принтер,
            Orientation = TemplateOrientation.Portrait,
            PaperSizeName = "C6",
            PaperWidthMm = 114m,
            PaperHeightMm = 162m,
            Items = new[]
            {
                new PrintableItem
                {
                    Id = 1,
                    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["[edt_str_to1]"] = "ООО Ромашка",
                        ["[edt_str_to2]"] = "Москва",
                    },
                },
            },
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
                    Name = "[edt_str_to2]",
                    Kind = BlankFieldKind.Text,
                    XMm = 8m,
                    YMm = 40m,
                    LengthMm = 60m,
                    HeightMm = 12m,
                    FontHeightPt = 10m,
                    FontHeightMinPt = 6m,
                    FontHeightMaxPt = 14m,
                },
            },
        };

    /// <summary>
    /// Детерминированный измеритель текста (§11.2): ширина строки равна половине
    /// кегля на символ, высота строки — 1,2 кегля.
    /// </summary>
    private sealed class Измеритель : ITextMeasurer
    {
        public double MeasureWidth(string text, string fontName, decimal fontSizePt) =>
            text.Length * (double)fontSizePt * 0.5;

        public double MeasureLineHeight(string fontName, decimal fontSizePt) =>
            (double)fontSizePt * 1.2;
    }

    /// <summary>
    /// Фиксатор вызовов графического контекста печатного пути: запоминает
    /// координаты вывода каждой строки текста.
    /// </summary>
    private sealed class ФиксаторГрафики : IGraphicsContext
    {
        public List<(string Текст, float X, float Y)> Позиции { get; } = new();

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
            float yMm) =>
            Позиции.Add((text, xMm, yMm));

        public void DrawStringMm(
            string text,
            string fontName,
            float sizePt,
            bool bold,
            float xMm,
            float yMm) =>
            Позиции.Add((text, xMm, yMm));

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm)
        {
        }
    }
}
