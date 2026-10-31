using Picket302.Domain.Entities;
using Picket302.Printing.Devices;
using Picket302.Printing.Metrics;
using Picket302.Printing.Validation;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Проверка двусторонней сверки метрик (ТЗ §6.2; §8.6, функция 83:
/// <c>Printing.Validation.PaperSizeValidator.Validate(BlankMetrics, PrinterDeviceInfo)</c>).
/// </summary>
public sealed class PaperSizeValidatorTests
{
    private static BlankMetrics Бланк(decimal ширина, decimal высота, string формат = "DL") =>
        new()
        {
            TemplateName = "Envelope_C5_Custom.lbk",
            WidthMm = ширина,
            HeightMm = высота,
            Orientation = TemplateOrientation.Landscape,
            PaperSizeName = формат,
        };

    private static PrinterDeviceInfo Принтер(
        decimal ширинаНосителя,
        decimal высотаНосителя,
        decimal ширинаПечати,
        decimal высотаПечати,
        string формат = "DL") =>
        new()
        {
            PrinterName = "HP LaserJet Pro M402dn",
            PaperSizeName = формат,
            PaperWidthMm = ширинаНосителя,
            PaperHeightMm = высотаНосителя,
            PrintableWidthMm = ширинаПечати,
            PrintableHeightMm = высотаПечати,
        };

    [Fact]
    public void Validate_КонвертБольшеНосителя_БлокируетПечать()
    {
        var результат = new PaperSizeValidator().Validate(
            Бланк(324, 229, "C4"),
            Принтер(220, 110, 220, 110, "DL"));

        Assert.True(результат.IsBlocked);
        Assert.Equal(PrintJobStatus.BlockedSize, результат.Status);
        Assert.Contains(PaperSizeValidator.BlockedSizeMessage, результат.ErrorMessage!);
    }

    [Fact]
    public void Validate_ПревышениеРовноНаОдинМиллиметр_НеБлокирует()
    {
        var результат = new PaperSizeValidator().Validate(
            Бланк(220, 110),
            Принтер(219, 110, 219, 110));

        Assert.False(результат.IsBlocked);
    }

    [Fact]
    public void Validate_ПревышениеНа1_01Миллиметра_Блокирует()
    {
        var результат = new PaperSizeValidator().Validate(
            Бланк(220, 110),
            Принтер(218.99m, 110, 218.99m, 110));

        Assert.True(результат.IsBlocked);
    }

    [Fact]
    public void Validate_КонвертМеньшеНосителя_ОшибокНет()
    {
        var результат = new PaperSizeValidator().Validate(
            Бланк(220, 110),
            Принтер(297, 210, 297, 210, "A4"));

        Assert.False(результат.IsBlocked);
        Assert.Null(результат.ErrorMessage);
    }

    [Fact]
    public void Validate_ДрайверСообщаетИнойФормат_ПредлагаетЗафиксироватьA4()
    {
        var результат = new PaperSizeValidator().Validate(
            Бланк(220, 110, "DL"),
            Принтер(297, 210, 297, 210, "A4"));

        Assert.False(результат.IsBlocked);
        Assert.True(результат.SuggestsFixA4);
        Assert.Contains(результат.Warnings, предупреждение =>
            предупреждение.Contains("Зафиксировать A4", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ФорматСовпадает_БезПредупреждений()
    {
        var результат = new PaperSizeValidator().Validate(
            Бланк(220, 110, "DL"),
            Принтер(220, 110, 220, 110, "DL"));

        Assert.False(результат.IsBlocked);
        Assert.False(результат.SuggestsFixA4);
        Assert.Empty(результат.Warnings);
    }

    [Fact]
    public void Validate_РезультатСодержитДельтуРазмеров()
    {
        var результат = new PaperSizeValidator().Validate(
            Бланк(324, 229, "C4"),
            Принтер(220, 110, 220, 110, "DL"));

        Assert.Equal(104m, результат.DeltaWidthMm);
        Assert.Equal(119m, результат.DeltaHeightMm);
    }

    [Fact]
    public void FromDevMode_МокDevMode_ДаётРазмерыНосителя()
    {
        var мок = new DevMode
        {
            DeviceName = "HP LaserJet Pro M402dn",
            SpecVersion = 0x0401,
            DriverVersion = 0x0600,
            Size = (short)System.Runtime.InteropServices.Marshal.SizeOf<DevMode>(),
            PaperSize = DevModeConstants.DMPAPER_A4,
            PaperWidth = 2970,
            PaperLength = 2100,
            Orientation = DevModeConstants.DMORIENT_LANDSCAPE,
            Duplex = DevModeConstants.DMDUP_SIMPLEX,
        };

        var устройство = PrinterDeviceInfo.FromDevMode(мок, printableWidthMm: 287, printableHeightMm: 200);

        Assert.Equal("HP LaserJet Pro M402dn", устройство.PrinterName);
        Assert.Equal(297m, устройство.PaperWidthMm);
        Assert.Equal(210m, устройство.PaperHeightMm);
        Assert.Equal(287m, устройство.PrintableWidthMm);
        Assert.Equal(200m, устройство.PrintableHeightMm);
        Assert.Equal(DevModeConstants.DMPAPER_A4, устройство.DmPaperSize);
    }

    [Fact]
    public void FromDevMode_МокDevMode_ОпределяетПоддержкуДуплекса()
    {
        var мок = new DevMode
        {
            DeviceName = "HP LaserJet Pro M402dn",
            Size = (short)System.Runtime.InteropServices.Marshal.SizeOf<DevMode>(),
            Duplex = DevModeConstants.DMDUP_VERTICAL,
        };

        var устройство = PrinterDeviceInfo.FromDevMode(мок, printableWidthMm: 287, printableHeightMm: 200);

        Assert.True(устройство.SupportsDuplex);
    }
}
