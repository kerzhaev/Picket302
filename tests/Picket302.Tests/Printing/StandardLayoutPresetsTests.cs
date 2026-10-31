using Picket302.Printing.Templates;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Пресеты «стандартная разметка» по связке «формат + страна»
/// (ТЗ §8.11, функция 145; список форматов — из справочника, включая «/О»).
/// </summary>
public sealed class StandardLayoutPresetsTests
{
    private readonly StandardLayoutPresets _пресеты = new();

    [Fact]
    public void ФорматыИзСправочника_Россия_ВключаяСОкном()
    {
        var форматы = _пресеты.GetAvailableFormats("RUS");

        Assert.Contains("C6", форматы);
        Assert.Contains("DL", форматы);
        Assert.Contains("C5", форматы);
        Assert.Contains("C4", форматы);
        Assert.Contains("B4", форматы);
        Assert.Contains("C6/О", форматы);
        Assert.Contains("DL/О", форматы);
    }

    [Fact]
    public void Пресет_КаждыйФормат_ИмеетВариантСОкном()
    {
        var форматы = _пресеты.GetAvailableFormats("RUS");

        // Справочник поставляет варианты «/О» с окном (ТЗ §8.5, функция 149).
        Assert.Contains("C6/О", форматы);
        Assert.Contains("DL/О", форматы);
        Assert.Contains("C5/О", форматы);
        Assert.All(форматы.Where(StandardLayoutPresets.IsWindowVariant), формат =>
            Assert.True(формат.EndsWith("/О", StringComparison.Ordinal)));
    }

    [Fact]
    public void Пресет_C6_ЗоныРазметки()
    {
        var пресет = _пресеты.GetPreset("C6", "RUS");

        Assert.Equal(162m, пресет.WidthMm);
        Assert.Equal(114m, пресет.HeightMm);

        Assert.NotNull(пресет.SenderZone);
        Assert.NotNull(пресет.RecipientZone);
        Assert.NotNull(пресет.PostIndexZone);

        // Отправитель — в верхнем левом углу, получатель — в нижнем правом.
        Assert.True(пресет.SenderZone!.XMm < пресет.WidthMm / 2);
        Assert.True(пресет.SenderZone.YMm < пресет.HeightMm / 2);
        Assert.True(пресет_правоНиз(пресет.RecipientZone!));

        // Кодовый штамп: 6 знаков, шаг 5,4 мм + ширина знака 3,7 мм.
        Assert.Equal(5 * 5.4m + 3.7m, пресет.PostIndexZone!.WidthMm);
        Assert.Equal(5.5m, пресет.PostIndexZone.HeightMm);
    }

    [Fact]
    public void Пресет_Ukraina_СвойСписокФорматов()
    {
        var форматы = _пресеты.GetAvailableFormats("UKR");

        Assert.Contains("C6", форматы);
        Assert.Contains("DL", форматы);
        Assert.DoesNotContain("B4", форматы);

        var пресет = _пресеты.GetPreset("DL", "UKR");

        Assert.Equal("UKR", пресет.Country);
        Assert.Equal(220m, пресет.WidthMm);
        Assert.Equal(110m, пресет.HeightMm);
    }

    [Fact]
    public void РежимБезРазметки_ЗоныПусты()
    {
        var пресет = _пресеты.GetPreset("C5", "RUS", StandardLayoutMode.None);

        Assert.Equal(229m, пресет.WidthMm);
        Assert.Equal(162m, пресет.HeightMm);
        Assert.Null(пресет.SenderZone);
        Assert.Null(пресет.RecipientZone);
        Assert.Null(пресет.PostIndexZone);
    }

    [Fact]
    public void НеизвестныйФормат_Отклонение()
    {
        Assert.ThrowsAny<Exception>(() => _пресеты.GetPreset("XX9", "RUS"));
    }

    private static bool пресет_правоНиз(LayoutZone зона) =>
        зона.XMm + зона.WidthMm > 162m / 2 && зона.YMm + зона.HeightMm > 114m / 2;
}
