using System.Drawing;
using Picket302.Domain.Entities;
using Picket302.Printing;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Проверка элементов печатного конвейера (ТЗ §8.6, функции 84–87; §8.4,
/// функции 43–46).
/// </summary>
public sealed class PrintEngineTests
{
    private static PrintJobContext Контекст(
        bool дуплексПоддерживается = true,
        decimal ширина = 229m,
        decimal высота = 162m,
        decimal ширинаНаклейки = 40m,
        decimal высотаНаклейки = 28m) =>
        new()
        {
            TemplateName = "Envelope_C5_Custom.lbk",
            Blank = new BlankMetrics
            {
                TemplateName = "Envelope_C5_Custom.lbk",
                WidthMm = ширина,
                HeightMm = высота,
                Orientation = TemplateOrientation.Landscape,
                PaperSizeName = "C5",
            },
            Printer = new PrinterDeviceInfo
            {
                PrinterName = "HP LaserJet Pro M402dn",
                PaperSizeName = "C5",
                PaperWidthMm = 229,
                PaperHeightMm = 162,
                PrintableWidthMm = 229,
                PrintableHeightMm = 162,
                SupportsDuplex = дуплексПоддерживается,
            },
            PaperWidthMm = ширина,
            PaperHeightMm = высота,
            Orientation = TemplateOrientation.Landscape,
            LabelWidthMm = ширинаНаклейки,
            LabelHeightMm = высотаНаклейки,
        };

    [Theory]
    [InlineData(PageParity.All, new[] { 1, 2, 3, 4 })]
    [InlineData(PageParity.OddOnly, new[] { 1, 3 })]
    [InlineData(PageParity.EvenOnly, new[] { 2, 4 })]
    public void FilterPageNumbers_Паритет_ОтбираетНужныеСтраницы(PageParity паритет, int[] ожидается)
    {
        var страницы = new PageParityFilter().FilterPageNumbers(4, паритет);

        Assert.Equal(ожидается, страницы);
    }

    [Fact]
    public void FilterPageNumbers_НольСтраниц_ПустойРезультат()
    {
        Assert.Empty(new PageParityFilter().FilterPageNumbers(0, PageParity.All));
    }

    [Fact]
    public void ConfigureDuplex_ПринтерПоддерживает_ВключаетДуплексВDevmode()
    {
        var контекст = Контекст();

        var решение = new DuplexController().ConfigureDuplex(контекст, DuplexMode.Duplex);

        Assert.Equal(DuplexMode.Duplex, решение.ЭффективныйРежим);
        Assert.False(решение.ТребуетсяИнструкция);
        Assert.Equal(
            DevModeConstants.DMDUP_VERTICAL,
            решение.DevModeDuplex);
    }

    [Fact]
    public void ConfigureDuplex_ПринтерНеПоддерживает_ФолбэкНаДвухпроходнуюПечать()
    {
        var контекст = Контекст(дуплексПоддерживается: false);

        var решение = new DuplexController().ConfigureDuplex(контекст, DuplexMode.Duplex);

        Assert.Equal(DuplexMode.ManualTwoPass, решение.ЭффективныйРежим);
        Assert.True(решение.ТребуетсяИнструкция);
        Assert.Contains("переверните", решение.Инструкция);
    }

    [Fact]
    public void ConfigureDuplex_ОдносторонняяПечать_ДуплексНеВключается()
    {
        var решение = new DuplexController().ConfigureDuplex(Контекст(), DuplexMode.Simplex);

        Assert.Equal(DuplexMode.Simplex, решение.ЭффективныйРежим);
        Assert.Equal(DevModeConstants.DMDUP_SIMPLEX, решение.DevModeDuplex);
    }

    [Fact]
    public void CalculateOrigin_Центр_СмещаетПоОсиX()
    {
        var начало = new FeedAlignmentCalculator()
            .CalculateOrigin(FeedAlignment.Center, new SizeF(297, 210), new SizeF(114, 162));

        Assert.Equal(91.5f, начало.X, 2);
        Assert.Equal(24f, начало.Y, 2);
    }

    [Fact]
    public void CalculateOrigin_ПоЛевомуКраю_БезСмещенияПоX()
    {
        var начало = new FeedAlignmentCalculator()
            .CalculateOrigin(FeedAlignment.Left, new SizeF(297, 210), new SizeF(114, 162));

        Assert.Equal(0f, начало.X, 2);
    }

    [Fact]
    public void CalculateOrigin_ПоПравомуКраю_СмещениеКПравойГранице()
    {
        var начало = new FeedAlignmentCalculator()
            .CalculateOrigin(FeedAlignment.Right, new SizeF(297, 210), new SizeF(114, 162));

        Assert.Equal(183f, начало.X, 2);
    }

    [Fact]
    public void LayoutGridOnSheet_Сетка3х7_Формирует21Наклейку()
    {
        var раскладка = new SheetLayoutImposition()
            .LayoutGridOnSheet(Контекст(), 3, 7, new Margins(5, 5, 5, 5), SheetSize.A4);

        Assert.Equal(21, раскладка.НаклеекНаЛисте);
        Assert.Equal(21, раскладка.Позиции.Count);
        Assert.Null(раскладка.Ошибка);
    }

    [Fact]
    public void LayoutGridOnSheet_НаклейкаБольшеЛиста_ОшибкаРазбивки()
    {
        var раскладка = new SheetLayoutImposition()
            .LayoutGridOnSheet(
                Контекст(ширинаНаклейки: 0, высотаНаклейки: 0),
                1,
                1,
                new Margins(5, 5, 5, 5),
                SheetSize.A5);

        Assert.NotNull(раскладка.Ошибка);
        Assert.Contains("Габарит", раскладка.Ошибка);
    }

    [Fact]
    public void LayoutGridOnSheet_ВылетыИМетки_Заданы()
    {
        var раскладка = new SheetLayoutImposition()
            .LayoutGridOnSheet(Контекст(), 2, 3, new Margins(5, 5, 5, 5), SheetSize.A4);

        Assert.True(раскладка.ВылетМм > 0);
        Assert.True(раскладка.МеткиОбреза.Count > 0);
    }

    [Fact]
    public void InvertAddresses_МеняетЗоныОтправителяИПолучателя()
    {
        var поля = new List<BlankField>
        {
            new() { Name = "Кому 1", ZoneRole = "BottomRecipient", XMm = 10, YMm = 100, LengthMm = 40, HeightMm = 10 },
            new() { Name = "От кого 1", ZoneRole = "TopSender", XMm = 10, YMm = 10, LengthMm = 40, HeightMm = 10 },
        };

        new EnvelopeInverter().InvertAddresses(поля);

        Assert.Equal(10m, поля[0].XMm);
        Assert.Equal(10m, поля[0].YMm);
        Assert.Equal(100m, поля[1].YMm);
    }

    [Fact]
    public void StripBackground_ИсключаетФоновыеИзображения()
    {
        var контекст = Контекст() with
        {
            Fields = new List<BlankField>
            {
                new() { Name = "Кому 1", Kind = BlankFieldKind.Text },
                new() { Name = "Фон", Kind = BlankFieldKind.Picture },
                new() { Name = "Индекс", Kind = BlankFieldKind.PostIndex },
            },
        };

        var результат = new PreprintedMode().StripBackground(контекст);

        Assert.Equal(2, результат.Fields.Count);
        Assert.DoesNotContain(результат.Fields, поле => поле.Kind == BlankFieldKind.Picture);
    }

    [Fact]
    public void StripBackground_ПредупреждениеОГост()
    {
        var предупреждение = new PreprintedMode().WarningText;

        Assert.Contains("ГОСТ Р 51506-99", предупреждение);
    }

    [Fact]
    public void ApplyFixA4_УстанавливаетФорматA4ИМасштабируетПоля()
    {
        var контекст = Контекст() with
        {
            Fields = new List<BlankField>
            {
                new() { Name = "Кому 1", XMm = 10, YMm = 20, LengthMm = 40, HeightMm = 10 },
            },
        };

        var результат = new FixA4Mode().ApplyFixA4(контекст);

        Assert.Equal("A4", результат.PaperSizeName);
        Assert.Equal(DevModeConstants.DMPAPER_A4, результат.DmPaperSize);

        var масштаб = 210m / 162m;
        Assert.Equal(10m * масштаб, результат.Fields[0].XMm, 2);
        Assert.Equal(20m * масштаб, результат.Fields[0].YMm, 2);
    }

    [Fact]
    public void ApplyFixA4_БланкБольшеA4_ЧестноеПредупреждениеОНеПомещении()
    {
        var контекст = Контекст(ширина: 250, высота: 353) with
        {
            Fields = new List<BlankField>
            {
                new() { Name = "Кому 1", XMm = 10, YMm = 20, LengthMm = 40, HeightMm = 10 },
            },
        };

        var результат = new FixA4Mode().ApplyFixA4(контекст);

        Assert.Contains(результат.Warnings, текст => текст.Contains("не поместится"));
        Assert.Contains(результат.Warnings, текст => текст.Contains("143"));
    }
}
