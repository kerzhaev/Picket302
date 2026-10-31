using Picket302.Domain.Entities;
using Picket302.Domain.Postal;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверка справочника форматов конвертов (ТЗ §8.5, функция 149: «Список форматов
/// строится из таблицы форматов стандарта (включая варианты "/О" с окном), а не
/// задается константами вручную»; §6.3, ГОСТ Р 51506-99).
/// </summary>
public sealed class EnvelopeFormatCatalogTests
{
    [Theory]
    [InlineData("C6", 114, 162)]
    [InlineData("DL", 110, 220)]
    [InlineData("C5", 162, 229)]
    [InlineData("C4", 229, 324)]
    [InlineData("C3", 324, 458)]
    [InlineData("B4", 250, 353)]
    [InlineData("E65", 110, 220)]
    [InlineData("E4", 220, 312)]
    [InlineData("K5", 145, 215)]
    [InlineData("K6", 125, 125)]
    [InlineData("K8", 150, 150)]
    [InlineData("K65", 125, 190)]
    public void Get_Формат_ВозвращаетРазмерыПоГост(string код, decimal высота, decimal длина)
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get(код, "RUS");

        Assert.Equal(высота, геометрия.HeightMm);
        Assert.Equal(длина, геометрия.LengthMm);
    }

    [Theory]
    [InlineData("C6")]
    [InlineData("DL")]
    public void Get_Формат_КлапанСверху(string код)
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get(код, "RUS");

        Assert.Equal(EnvelopeFlapType.Top, геометрия.FlapType);
    }

    [Theory]
    [InlineData("C5")]
    [InlineData("C4")]
    [InlineData("B4")]
    public void Get_Формат_КлапанБоковойИлиСверху(string код)
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get(код, "RUS");

        Assert.Equal(EnvelopeFlapType.SideOrTop, геометрия.FlapType);
    }

    [Fact]
    public void Get_ВнутреннееОтправление_ШестизначныйКодовыйШтамп()
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get("C6", "RUS");

        Assert.Equal(EnvelopeTerritory.Domestic, геометрия.Territory);
        Assert.Equal(6, геометрия.IndexDigitCount);
        Assert.Null(геометрия.InternationalIndex);
    }

    [Fact]
    public void Get_МеждународноеОтправление_ТрёхзначныйШтампИндекс555()
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get("C6", "RUS", EnvelopeTerritory.International);

        Assert.Equal(EnvelopeTerritory.International, геометрия.Territory);
        Assert.Equal(3, геометрия.IndexDigitCount);
        Assert.Equal("555", геометрия.InternationalIndex);
        Assert.True(геометрия.AddressOrderSmallToLarge);
    }

    [Theory]
    [InlineData("C6/О")]
    [InlineData("DL/О")]
    [InlineData("C5/О")]
    public void Get_ВариантСОкном_ПомеченКакСОкном(string код)
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get(код, "RUS");

        Assert.True(геометрия.HasWindow);
    }

    [Theory]
    [InlineData("C6")]
    [InlineData("DL")]
    [InlineData("C5")]
    [InlineData("C4")]
    [InlineData("B4")]
    public void Get_ФорматБезОкна_НеПомеченКакСОкном(string код)
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get(код, "RUS");

        Assert.False(геометрия.HasWindow);
    }

    [Fact]
    public void Get_НеизвестныйФормат_ВыбрасываетИсключение()
    {
        Assert.Throws<KeyNotFoundException>(
            () => EnvelopeFormatCatalog.Default.Get("A4", "RUS"));
    }

    [Fact]
    public void Get_ФорматНеДляСтраны_ВыбрасываетИсключение()
    {
        Assert.Throws<KeyNotFoundException>(
            () => EnvelopeFormatCatalog.Default.Get("K65", "UKR"));
    }

    [Fact]
    public void GetAll_Россия_СодержитВсеФорматыСпецификации()
    {
        var коды = EnvelopeFormatCatalog.Default.GetFormatCodes("RUS");

        Assert.Contains("C6", коды);
        Assert.Contains("DL", коды);
        Assert.Contains("C5", коды);
        Assert.Contains("C4", коды);
        Assert.Contains("C3", коды);
        Assert.Contains("B4", коды);
        Assert.Contains("E65", коды);
        Assert.Contains("E4", коды);
        Assert.Contains("K5", коды);
        Assert.Contains("K6", коды);
        Assert.Contains("K8", коды);
        Assert.Contains("K65", коды);
    }

    [Fact]
    public void GetAll_Украина_ТолькоУкраинскаяСерия()
    {
        var коды = EnvelopeFormatCatalog.Default.GetFormatCodes("UKR");

        Assert.Contains("C6", коды);
        Assert.Contains("DL", коды);
        Assert.Contains("C5", коды);
        Assert.Contains("C4", коды);
        Assert.Contains("E65", коды);
        Assert.DoesNotContain("K65", коды);
        Assert.DoesNotContain("B4", коды);
    }

    [Fact]
    public void Get_ПеречислениеФорматов_ВозвращаетГеометрию()
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get(EnvelopeFormat.C5, TemplateOrientation.Landscape);

        Assert.Equal("C5", геометрия.FormatCode);
        Assert.Equal(162m, геометрия.HeightMm);
        Assert.Equal(229m, геометрия.LengthMm);
    }

    [Fact]
    public void GetWidthMm_АльбомнаяОриентация_ДлиннаяСторона()
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get("C5", "RUS");

        Assert.Equal(229m, геометрия.GetWidthMm(TemplateOrientation.Landscape));
        Assert.Equal(162m, геометрия.GetHeightMm(TemplateOrientation.Landscape));
    }

    [Fact]
    public void GetWidthMm_КнижнаяОриентация_КороткаяСторона()
    {
        var геометрия = EnvelopeFormatCatalog.Default.Get("C5", "RUS");

        Assert.Equal(162m, геометрия.GetWidthMm(TemplateOrientation.Portrait));
        Assert.Equal(229m, геометрия.GetHeightMm(TemplateOrientation.Portrait));
    }
}
