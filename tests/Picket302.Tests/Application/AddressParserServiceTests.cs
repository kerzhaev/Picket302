using Picket302.Application.Services;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка разбора почтового индекса из строки адреса (ТЗ §8.1, функция 5:
/// «Разбор индекса из строки адреса (<c>parseZipIndexFromAddress</c>)»).
/// </summary>
public sealed class AddressParserServiceTests
{
    private readonly AddressParserService _служба = new();

    [Fact]
    public void ParseRawString_ИндексВНачалеСтроки_Вырезается()
    {
        var результат = _служба.ParseRawString("101000, г. Москва, ул. Мясницкая, д. 1", extractZip: true);

        Assert.Equal("101000", результат.Zip);
        Assert.False(результат.NeedsVerification);
    }

    [Fact]
    public void ParseRawString_ИндексВКонцеСтроки_Вырезается()
    {
        var результат = _служба.ParseRawString("г. Москва, ул. Тверская, д. 1, 125009", extractZip: true);

        Assert.Equal("125009", результат.Zip);
    }

    [Fact]
    public void ParseRawString_ИндексОтсутствует_ПолеИндексаПустоеИТребуетсяПроверка()
    {
        var результат = _служба.ParseRawString("г. Москва, ул. Тверская, д. 1", extractZip: true);

        Assert.Equal(string.Empty, результат.Zip);
        Assert.True(результат.NeedsVerification);
    }

    [Fact]
    public void ParseRawString_ФлагВыключен_ИндексНеВырезается()
    {
        var результат = _служба.ParseRawString("101000, г. Москва", extractZip: false);

        Assert.Equal(string.Empty, результат.Zip);
        Assert.True(результат.NeedsVerification);
        Assert.Contains(результат.Lines, строка => строка.Contains("101000", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseRawString_СемизначноеЧисло_ИндексомНеСчитается()
    {
        var результат = _служба.ParseRawString("1234567, г. Москва", extractZip: true);

        Assert.Equal(string.Empty, результат.Zip);
    }

    [Fact]
    public void ParseRawString_ПятизначноеЧисло_ИндексомНеСчитается()
    {
        var результат = _служба.ParseRawString("10100, г. Москва", extractZip: true);

        Assert.Equal(string.Empty, результат.Zip);
    }

    [Fact]
    public void ParseRawString_ОстатокРазбиваетсяПоЗапятымНаСтроки()
    {
        var результат = _служба.ParseRawString("101000, г. Москва, ул. Мясницкая, д. 1", extractZip: true);

        Assert.Equal(6, результат.Lines.Count);
        Assert.Equal("г. Москва", результат.Lines[0]);
        Assert.Equal("ул. Мясницкая", результат.Lines[1]);
        Assert.Equal("д. 1", результат.Lines[2]);
    }

    [Fact]
    public void ParseRawString_БолееШестиЧастей_ЛишниеОтбрасываются()
    {
        var результат = _служба.ParseRawString(
            "101000, а, б, в, г, д, е, ж, з",
            extractZip: true);

        Assert.Equal(6, результат.Lines.Count);
        Assert.Equal("а", результат.Lines[0]);
        Assert.Equal("д", результат.Lines[4]);
        Assert.Equal("е", результат.Lines[5]);
    }

    [Fact]
    public void ParseRawString_ПустаяСтрока_ИндексПустойИТребуетсяПроверка()
    {
        var результат = _служба.ParseRawString(string.Empty, extractZip: true);

        Assert.Equal(string.Empty, результат.Zip);
        Assert.True(результат.NeedsVerification);
        Assert.Equal(6, результат.Lines.Count);
    }

    [Fact]
    public void ИмяПараметраИнтеграции_СоответствуетСпецификации()
    {
        Assert.Equal("parseZipIndexFromAddress", AddressParserService.ParseZipIndexFromAddressOption);
    }
}
