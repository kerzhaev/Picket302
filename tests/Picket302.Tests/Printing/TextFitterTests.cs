using Picket302.Domain.Entities;
using Picket302.Printing.Engine;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Проверка автоподбора кегля (ТЗ §8.6, функция 91:
/// <c>Printing.Engine.TextFitter.FitField(BlankField, string)</c>).
/// </summary>
public sealed class TextFitterTests
{
    /// <summary>
    /// Измеритель текста с заданной шириной знака: ширина строки пропорциональна
    /// числу знаков и кеглю, высота строки — кеглю.
    /// </summary>
    private sealed class УсловныйИзмеритель : ITextMeasurer
    {
        public double ШиринаЗнакаEm { get; init; } = 0.5;

        public double MeasureWidth(string text, string fontName, decimal fontSizePt) =>
            text.Length * ШиринаЗнакаEm * (double)fontSizePt * 25.4 / 72.0;

        public double MeasureLineHeight(string fontName, decimal fontSizePt) =>
            1.2 * (double)fontSizePt * 25.4 / 72.0;
    }

    private static BlankField Поле(decimal длина = 40m, decimal высота = 15m, decimal мин = 6m, decimal макс = 10m) =>
        new()
        {
            Name = "Кому 1",
            LengthMm = длина,
            HeightMm = высота,
            FontHeightMinPt = мин,
            FontHeightMaxPt = макс,
        };

    [Fact]
    public void FitField_КороткийТекст_ОставляетМаксимальныйКегль()
    {
        var результат = new TextFitter(new УсловныйИзмеритель()).FitField(Поле(), "Иванов");

        Assert.Equal(10m, результат.FontSizePt);
        Assert.False(результат.Overflow);
    }

    [Fact]
    public void FitField_ДлинныйТекст_УменьшаетКегльШагом0_5()
    {
        var текст = string.Join(' ', Enumerable.Repeat("Иванов Иван Иванович", 6));

        var результат = new TextFitter(new УсловныйИзмеритель()).FitField(Поле(), текст);

        Assert.True(результат.FontSizePt < 10m);
        Assert.True(результат.FontSizePt >= 6m);
        Assert.Equal(0m, (10m - результат.FontSizePt) % 0.5m);
        Assert.False(результат.Overflow);
    }

    [Fact]
    public void FitField_ТекстНеВлезаетДажеПриМинимальномКегле_ДаётМаркерПереполнения()
    {
        var текст = string.Join(' ', Enumerable.Repeat("Иванов Иван Иванович", 60));

        var результат = new TextFitter(new УсловныйИзмеритель()).FitField(Поле(), текст);

        Assert.True(результат.Overflow);
        Assert.Equal(6m, результат.FontSizePt);
        Assert.NotNull(результат.Warning);
        Assert.Contains("Кому 1", результат.Warning!);
    }

    [Fact]
    public void FitField_КегльПодбираетсяСШагомНеКрупнее0_5()
    {
        var поле = Поле(длина: 20m, высота: 30m, мин: 6m, макс: 10m);

        var результат = new TextFitter(new УсловныйИзмеритель()).FitField(поле, "Иванов Иван");

        Assert.Equal(0m, (10m - результат.FontSizePt) % 0.5m);
    }

    [Fact]
    public void FitField_ПустойТекст_НеДаётМаркераПереполнения()
    {
        var результат = new TextFitter(new УсловныйИзмеритель()).FitField(Поле(), string.Empty);

        Assert.False(результат.Overflow);
        Assert.Equal(10m, результат.FontSizePt);
        Assert.Empty(результат.Lines);
    }

    [Fact]
    public void FitField_ПревышениеЛимитаСимволов_ОбрезаетТекстИПредупреждает()
    {
        var поле = Поле();
        поле.MaxChars = 20;

        var результат = new TextFitter(new УсловныйИзмеритель())
            .FitField(поле, string.Join(' ', Enumerable.Repeat("Иван", 20)));

        Assert.True(результат.Truncated);
        Assert.NotNull(результат.Warning);
        Assert.Contains("20", результат.Warning!);
        Assert.True(
            результат.Lines.Sum(строка => строка.Length) <= поле.MaxChars);
    }
}
