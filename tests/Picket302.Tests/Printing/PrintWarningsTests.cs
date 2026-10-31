using System.Drawing;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Printing;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;
using Picket302.Printing.Validation;
using Picket302.Printing.Warnings;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Проверка предупреждений печати (ТЗ §8.6, функция 85; §8.5, функция 147).
/// </summary>
public sealed class PrintWarningsTests
{
    private sealed class ПараметрыВПамяти : IParameterRepository
    {
        private readonly Dictionary<string, Parameter> _записи = new(StringComparer.Ordinal);

        public Task<long> AddAsync(Parameter parameter, CancellationToken cancellationToken = default)
        {
            _записи[parameter.Name] = parameter;
            return Task.FromResult((long)_записи.Count);
        }

        public Task<Parameter?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_записи.TryGetValue(name, out var запись) ? запись : null);

        public Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_записи.TryGetValue(name, out var запись) ? запись.Value : null);

        public Task<IReadOnlyList<Parameter>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Parameter>>(_записи.Values.ToList());

        public Task<int> UpdateAsync(Parameter parameter, CancellationToken cancellationToken = default)
        {
            _записи[parameter.Name] = parameter;
            return Task.FromResult(1);
        }

        public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_записи.Remove(name));
    }

    [Fact]
    public void BuildMessage_СодержитРазмерыИДоступныеФорматы()
    {
        var предупреждение = new OversizeWarning(availablePaperSizes: new[] { "A4", "DL" });

        var текст = предупреждение.BuildMessage(new SizeF(324f, 229f), new SizeF(220f, 110f));

        Assert.Contains("Конверт слишком большой для выбранного принтера", текст);
        Assert.Contains("324", текст);
        Assert.Contains("229", текст);
        Assert.Contains("220", текст);
        Assert.Contains("110", текст);
        Assert.Contains("A4", текст);
        Assert.Contains("DL", текст);
    }

    [Fact]
    public async Task ShouldShowAsync_БезФлага_ПредупреждениеПоказывается()
    {
        var подсказка = new FeedOrientationHint(new ПараметрыВПамяти());

        Assert.True(await подсказка.ShouldShowAsync());
    }

    [Fact]
    public async Task SetSuppressedAsync_ПослеПодавления_ПредупреждениеНеПоказывается()
    {
        var подсказка = new FeedOrientationHint(new ПараметрыВПамяти());

        await подсказка.SetSuppressedAsync(true);

        Assert.False(await подсказка.ShouldShowAsync());
    }

    [Fact]
    public void WarningText_СовпадаетСФормулировкойСтандарта()
    {
        Assert.Equal(
            "Для корректной печати конверты следует загружать в принтер узкой стороной.",
            FeedOrientationHint.WarningText);

        Assert.Contains("Больше не показывать", FeedOrientationHint.SuppressCaption);
        Assert.Contains("скотчем", FeedOrientationHint.HelpText);
    }

    private static PrintJobContext Контекст(bool поддерживаетДуплекс = false) =>
        new()
        {
            Blank = new BlankMetrics { TemplateName = "C5.lbk", WidthMm = 162m, HeightMm = 229m },
            Orientation = TemplateOrientation.Portrait,
            Printer = new PrinterDeviceInfo
            {
                PrinterName = "Тестовый принтер",
                SupportsDuplex = поддерживаетДуплекс,
            },
            Fields = Array.Empty<BlankField>(),
        };

    [Fact]
    public void ApplyFixA4_ВыдаётПредупреждениеТз_ОПринудительнойПечатиА4()
    {
        var результат = new FixA4Mode().ApplyFixA4(Контекст());

        Assert.Contains(FixA4Mode.FixA4Warning, результат.Warnings);
        Assert.Contains(
            результат.Warnings,
            текст => текст.Contains("Внимание: включена принудительная печать конверта в габаритах листа А4"));
        Assert.Contains(
            результат.Warnings,
            текст => текст.Contains("Убедитесь в правильности физической ориентации конверта в лотке"));
    }

    [Fact]
    public void ApplyFixA4_БланкБольшеА4_ПредупреждениеОНепомещенииИСписокОбрезанныхПолей()
    {
        var контекст = Контекст() with
        {
            Blank = new BlankMetrics { TemplateName = "B4.lbk", WidthMm = 250m, HeightMm = 353m },
            Fields = new[]
            {
                new BlankField { Name = "Адрес", XMm = 240m, YMm = 10m, LengthMm = 30m, HeightMm = 10m },
            },
        };

        var результат = new FixA4Mode().ApplyFixA4(контекст);

        Assert.Contains(результат.Warnings, текст => текст.Contains("всё равно не поместится"));
        Assert.Contains(FixA4Mode.FixA4Warning, результат.Warnings);
        Assert.Contains("Адрес", результат.CroppedFieldNames);
    }

    [Fact]
    public async Task PreprintedMode_БезПодавления_ПредупреждениеДобавляется()
    {
        var режим = new PreprintedMode(new ПараметрыВПамяти());

        var результат = режим.StripBackground(Контекст());

        Assert.Contains(PreprintedMode.Warning, результат.Warnings);
        Assert.True(await режим.ShouldShowAsync());
    }

    [Fact]
    public async Task PreprintedMode_ПослеПодавления_ПредупреждениеНеДобавляется()
    {
        var параметры = new ПараметрыВПамяти();
        var режим = new PreprintedMode(параметры);

        await режим.SetSuppressedAsync(true);

        Assert.False(await режим.ShouldShowAsync());
        Assert.Equal("1", await параметры.GetValueAsync(PreprintedMode.SuppressionKey));

        var результат = режим.StripBackground(Контекст());

        Assert.DoesNotContain(PreprintedMode.Warning, результат.Warnings);
    }

    [Fact]
    public async Task PreprintedMode_ИсключаетФоновыеПоляИОставляетАдресныеДанные()
    {
        var режим = new PreprintedMode(new ПараметрыВПамяти());

        var контекст = Контекст() with
        {
            Fields = new[]
            {
                new BlankField { Name = "Рамка", Kind = BlankFieldKind.Picture },
                new BlankField { Name = "Адрес", Kind = BlankFieldKind.Text },
                new BlankField { Name = "Индекс", Kind = BlankFieldKind.PostIndex },
            },
        };

        var результат = await режим.StripBackgroundAsync(контекст);

        Assert.Equal(2, результат.Fields.Count);
        Assert.Contains(результат.Fields, поле => поле.Name == "Адрес");
        Assert.Contains(результат.Fields, поле => поле.Name == "Индекс");
        Assert.DoesNotContain(результат.Fields, поле => поле.Name == "Рамка");
        Assert.True(результат.Preprinted);
    }

    [Fact]
    public void PreprintedMode_ЧекбоксПодавления_СовпадаетСФормулировкой()
    {
        Assert.Equal("Больше не показывать это сообщение", PreprintedMode.SuppressCaption);
        Assert.Contains("ГОСТ Р 51506-99", new PreprintedMode().WarningText);
    }

    [Fact]
    public void ConfigureDuplex_ПринтерБезДуплекса_ВключаетсяДвухпроходнаяПечать()
    {
        var решение = new DuplexController().ConfigureDuplex(Контекст(), DuplexMode.Duplex);

        Assert.Equal(DuplexMode.ManualTwoPass, решение.ЭффективныйРежим);
        Assert.Equal(DevModeConstants.DMDUP_SIMPLEX, решение.DevModeDuplex);
        Assert.True(решение.ТребуетсяИнструкция);
        Assert.Contains("двухпроходная печать", решение.Инструкция!);
        Assert.Contains("переверните стопку", решение.Инструкция!);
    }

    [Fact]
    public void ConfigureDuplex_ПринтерСДуплексом_АппаратныйДуплекс()
    {
        var решение = new DuplexController().ConfigureDuplex(Контекст(поддерживаетДуплекс: true), DuplexMode.Duplex);

        Assert.Equal(DuplexMode.Duplex, решение.ЭффективныйРежим);
        Assert.Equal(DevModeConstants.DMDUP_VERTICAL, решение.DevModeDuplex);
        Assert.False(решение.ТребуетсяИнструкция);
        Assert.Null(решение.Инструкция);
    }

    [Fact]
    public void ConfigureDuplex_ОдносторонняяПечать_ИнструкцияНеТребуется()
    {
        var решение = new DuplexController().ConfigureDuplex(Контекст(поддерживаетДуплекс: true), DuplexMode.Simplex);

        Assert.Equal(DuplexMode.Simplex, решение.ЭффективныйРежим);
        Assert.Equal(DevModeConstants.DMDUP_SIMPLEX, решение.DevModeDuplex);
        Assert.False(решение.ТребуетсяИнструкция);
    }

    [Fact]
    public void ConfigureDuplex_ДвухпроходнаяПечать_ЗадаётЧётностьПроходов()
    {
        var решение = new DuplexController().ConfigureDuplex(Контекст(), DuplexMode.Duplex);

        Assert.Equal(PageParity.OddOnly, решение.ЧётностьПервогоПрохода);
        Assert.Equal(PageParity.EvenOnly, решение.ЧётностьВторогоПрохода);
    }

    [Fact]
    public void ConfigureDuplex_РучнойРежим_ЗадаётЧётностьПроходов()
    {
        var решение = new DuplexController().ConfigureDuplex(Контекст(поддерживаетДуплекс: true), DuplexMode.ManualTwoPass);

        Assert.Equal(DuplexMode.ManualTwoPass, решение.ЭффективныйРежим);
        Assert.Equal(PageParity.OddOnly, решение.ЧётностьПервогоПрохода);
        Assert.Equal(PageParity.EvenOnly, решение.ЧётностьВторогоПрохода);
    }

    [Fact]
    public void FilterPageNumbers_ОтборПоЧётности_НумерацияСЕдиницы()
    {
        var фильтр = new PageParityFilter();

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, фильтр.FilterPageNumbers(5, PageParity.All));
        Assert.Equal(new[] { 1, 3, 5 }, фильтр.FilterPageNumbers(5, PageParity.OddOnly));
        Assert.Equal(new[] { 2, 4 }, фильтр.FilterPageNumbers(5, PageParity.EvenOnly));
    }

    [Fact]
    public void FilterPageNumbers_ОднаСтраница_НечётнаяВозвращаетсяЧётнаяНет()
    {
        var фильтр = new PageParityFilter();

        Assert.Equal(new[] { 1 }, фильтр.FilterPageNumbers(1, PageParity.OddOnly));
        Assert.Empty(фильтр.FilterPageNumbers(1, PageParity.EvenOnly));
        Assert.Empty(фильтр.FilterPageNumbers(0, PageParity.All));
    }

    [Fact]
    public void CalculateOrigin_ПоЦентру_СмещениеВЦентрЛиста()
    {
        var калькулятор = new FeedAlignmentCalculator();

        var начало = калькулятор.CalculateOrigin(FeedAlignment.Center, new SizeF(210f, 297f), new SizeF(110f, 220f));

        Assert.Equal(50f, начало.X, 3);
        Assert.Equal(38.5f, начало.Y, 3);
    }

    [Fact]
    public void CalculateOrigin_ПоКраям_СмещениеКЛевомуИПравомуКраю()
    {
        var калькулятор = new FeedAlignmentCalculator();
        var лист = new SizeF(210f, 297f);
        var конверт = new SizeF(110f, 220f);

        var слева = калькулятор.CalculateOrigin(FeedAlignment.Left, лист, конверт);
        var справа = калькулятор.CalculateOrigin(FeedAlignment.Right, лист, конверт);

        Assert.Equal(0f, слева.X, 3);
        Assert.Equal(100f, справа.X, 3);
        Assert.Equal(38.5f, слева.Y, 3);
        Assert.Equal(38.5f, справа.Y, 3);
    }

    [Fact]
    public void CalculateOrigin_ПодачаУзкойСтороной_ГабаритыЛистаМеняютсяМестами()
    {
        var калькулятор = new FeedAlignmentCalculator();
        var лист = new SizeF(297f, 210f);
        var конверт = new SizeF(110f, 110f);

        var широкой = калькулятор.CalculateOrigin(FeedAlignment.Center, лист, конверт, поУзкойСтороне: false);
        var узкой = калькулятор.CalculateOrigin(FeedAlignment.Center, лист, конверт, поУзкойСтороне: true);

        // При подаче широкой стороной результат совпадает с обычным расчётом.
        Assert.Equal(калькулятор.CalculateOrigin(FeedAlignment.Center, лист, конверт), широкой);

        // При подаче узкой стороной лист повёрнут: ширина и высота меняются местами.
        Assert.Equal(93.5f, широкой.X, 3);
        Assert.Equal(50f, широкой.Y, 3);
        Assert.Equal(50f, узкой.X, 3);
        Assert.Equal(93.5f, узкой.Y, 3);
    }

    [Fact]
    public void LayoutGridOnSheet_МеткиОбрезаОстаютсяВнутриЛиста()
    {
        var контекст = Контекст() with
        {
            Blank = new BlankMetrics { TemplateName = "A4.lbk", WidthMm = 100m, HeightMm = 50m },
            LabelWidthMm = 40m,
            LabelHeightMm = 20m,
        };

        var результат = new SheetLayoutImposition().LayoutGridOnSheet(контекст, 2, 2, Margins.Default, SheetSize.A4);

        Assert.Null(результат.Ошибка);
        Assert.NotEmpty(результат.МеткиОбреза);

        foreach (var метка in результат.МеткиОбреза)
        {
            Assert.True(метка.Left >= 0f, $"Метка {метка} выходит за левый край листа.");
            Assert.True(метка.Top >= 0f, $"Метка {метка} выходит за верхний край листа.");
            Assert.True(метка.Right <= 210f, $"Метка {метка} выходит за правый край листа.");
            Assert.True(метка.Bottom <= 297f, $"Метка {метка} выходит за нижний край листа.");
        }
    }

    [Fact]
    public void LayoutGridOnSheet_НаклейкаБольшеЛиста_ОшибкаРазбивкиПоГабаритамЛиста()
    {
        var контекст = Контекст() with
        {
            Blank = new BlankMetrics { TemplateName = "A4.lbk", WidthMm = 250m, HeightMm = 353m },
            LabelWidthMm = 250m,
            LabelHeightMm = 353m,
        };

        var результат = new SheetLayoutImposition().LayoutGridOnSheet(контекст, 2, 2, Margins.Default, SheetSize.A4);

        Assert.NotNull(результат.Ошибка);
        Assert.Contains("габариты листа", результат.Ошибка!);
        Assert.Contains("A4", результат.Ошибка!);
        Assert.Empty(результат.Позиции);
    }

    [Fact]
    public void LayoutGridOnSheet_НаклейкаНеПомещаетсяВЯчейку_ОшибкаРазбивки()
    {
        var контекст = Контекст() with
        {
            Blank = new BlankMetrics { TemplateName = "A4.lbk", WidthMm = 100m, HeightMm = 150m },
            LabelWidthMm = 100m,
            LabelHeightMm = 150m,
        };

        var результат = new SheetLayoutImposition().LayoutGridOnSheet(контекст, 2, 3, Margins.Default, SheetSize.A4);

        Assert.NotNull(результат.Ошибка);
        Assert.Contains("ячейку сетки", результат.Ошибка!);
        Assert.Contains("A4", результат.Ошибка!);
        Assert.Empty(результат.Позиции);
    }
}
