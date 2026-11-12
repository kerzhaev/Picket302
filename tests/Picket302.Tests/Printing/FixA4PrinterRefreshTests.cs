using Picket302.Domain.Entities;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;
using Picket302.Printing.Validation;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// D-S17-01 (приёмка S17): после «Зафиксировать A4» сведения принтера
/// обязаны соответствовать листу A4 — иначе повторная валидация
/// бланков зацикливается на драйверах без конвертных форматов
/// (наблюдалось: бесконечный цикл «Конверт слишком большой» →
/// «Проверка размера бумаги» → «Зафиксировать A4»).
/// </summary>
public sealed class FixA4PrinterRefreshTests
{
    [Theory]
    [InlineData(TemplateOrientation.Portrait, 210, 297)]
    [InlineData(TemplateOrientation.Landscape, 297, 210)]
    public void ОбновитьПринтерПодФорматA4_СтавитГабаритыИОбластьA4(
        TemplateOrientation ориентация,
        decimal ожидаемаяШирина,
        decimal decimalHeight)
    {
        // Сведения принтера, полученные под конвертный код (например,
        // драйвер без конвертных форматов сообщает нулевую область).
        var принтер = new PrinterDeviceInfo
        {
            PrinterName = "Virtual PDF",
            PaperSizeName = "C6",
            PaperWidthMm = 162m,
            PaperHeightMm = 114m,
            PrintableWidthMm = 0m,
            PrintableHeightMm = 0m,
        };

        var обновлён = FixA4Mode.ОбновитьПринтерПодФорматA4(принтер, ориентация);

        Assert.Equal("A4", обновлён.PaperSizeName);
        Assert.Equal(ожидаемаяШирина, обновлён.PaperWidthMm);
        Assert.Equal(decimalHeight, обновлён.PaperHeightMm);
        Assert.Equal(ожидаемаяШирина, обновлён.PrintableWidthMm);
        Assert.Equal(decimalHeight, обновлён.PrintableHeightMm);
        Assert.Equal(DevModeConstants.DMPAPER_A4, обновлён.DmPaperSize);
    }

    [Fact]
    public void ОбновитьПринтерПодФорматA4_ВалидацияБланкаA4Проходит()
    {
        // Бланк после FixA4 имеет габариты A4; валидатор обязан пропустить
        // его против обновлённых сведений принтера (регрессия цикла).
        var принтер = FixA4Mode.ОбновитьПринтерПодФорматA4(
            new PrinterDeviceInfo
            {
                PrinterName = "Virtual PDF",
                PaperSizeName = "C6",
                PrintableWidthMm = 0m,
                PrintableHeightMm = 0m,
            },
            TemplateOrientation.Portrait);

        var бланк = new BlankMetrics
        {
            TemplateName = "Конверт C6 (печать, S17)",
            PaperSizeName = "A4",
            WidthMm = 210m,
            HeightMm = 297m,
        };

        var результат = new PaperSizeValidator().Validate(бланк, принтер);

        Assert.False(результат.IsBlocked);
    }
}
