using Picket302.Application.Import;
using Picket302.Application.Services;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверки автоматического распознавания адресов при импорте (ТЗ §8.12,
/// функция 148: «Автоматическое распознавание адресов при импорте»).
/// </summary>
public sealed class AddressHeuristicsServiceTests
{
    private const string ОбразецАдреса =
        "350000, г. Краснодар, ул. Красная, д. 35, оф. 4, Иванов Иван Иванович";

    /// <summary>
    /// Словари топонимов поставляются вместе с программой: без них разбор
    /// адреса не учитывает пользовательские и встроенные понятия
    /// (ТЗ §8.12, функция 148: «Словари топонимов и сокращений поставляются
    /// файлами, расширяемы»).
    /// </summary>
    [Theory]
    [InlineData("cities.txt")]
    [InlineData("regions.txt")]
    [InlineData("street-types.txt")]
    [InlineData("settlements.txt")]
    [InlineData("house-types.txt")]
    [InlineData("flat-types.txt")]
    public void СловариТопонимов_ПоставляютсяВКаталогеСборки(string имяФайла)
    {
        var каталог = Path.Combine(AppContext.BaseDirectory, "Resources", "Toponyms");

        Assert.True(
            Directory.Exists(каталог),
            $"Каталог словарей топонимов не найден рядом с программой: {каталог}");

        var путь = Path.Combine(каталог, имяФайла);

        Assert.True(File.Exists(путь), $"Файл словаря топонимов не найден: {путь}");
        Assert.Contains(File.ReadAllLines(путь), строка => !строка.StartsWith("#", StringComparison.Ordinal));
    }

    [Fact]
    public void СловариТопонимов_ЧитаютсяСлужбой()
    {
        var служба = new AddressHeuristicsService();

        var результат = служба.RecognizeDetailed(ОбразецАдреса);

        // Город распознан по встроенному словарю городов, а сокращения
        // «ул.», «д.», «оф.» — по словарю сокращений. населённый пункт
        // возвращается вместе с обозначением типа («г. Краснодар»).
        Assert.Equal("г. Краснодар", результат.City);
        Assert.Equal("ул. Красная", результат.Street);
        Assert.Equal("35", результат.House);
        Assert.Equal("4", результат.Flat);
    }

    [Fact]
    public void РаспознаваниеАдреса_РазбираетИндексГородУлицуДомКвартируИФИО()
    {
        var служба = new AddressHeuristicsService();

        var результат = служба.RecognizeDetailed(ОбразецАдреса);

        Assert.Equal("350000", результат.Zip);
        Assert.Contains("Краснодар", результат.City);
        Assert.Equal("ул. Красная", результат.Street);
        Assert.Equal("35", результат.House);
        Assert.Equal("4", результат.Flat);
        Assert.Equal("Иванов Иван Иванович", результат.FullName);
        Assert.False(результат.NeedsVerification);
    }

    [Fact]
    public void РаспознаваниеАдреса_ЗаполняетАдресныеСтрокиПоОбластиМодели()
    {
        var служба = new AddressHeuristicsService();

        var результат = служба.RecognizeDetailed(ОбразецАдреса);

        Assert.Equal(6, результат.Lines.Count);
        Assert.Equal("Иванов Иван Иванович", результат.Lines[0]);
        Assert.Equal("ул. Красная", результат.Lines[1]);
        Assert.Equal("д. 35", результат.Lines[2]);
        Assert.Equal("оф. 4", результат.Lines[3]);
        Assert.Contains("Краснодар", результат.Lines[4]);
    }

    [Fact]
    public void ФИОНеОстаётсяВСтрокеУлицы()
    {
        var служба = new AddressHeuristicsService();

        var результат = служба.RecognizeDetailed(ОбразецАдреса);

        Assert.DoesNotContain("Иванов", результат.Lines[1]);
        Assert.Contains("Иванов Иван Иванович", результат.StreetHouseFlat + результат.FullName);
    }

    [Fact]
    public void НеРаспознанныйФрагмент_ПопадаетВПервуюСвободнуюСтрокуИТребуетПроверки()
    {
        var служба = new AddressHeuristicsService();

        var результат = служба.RecognizeDetailed("350000, г. Краснодар, ул. Красная, д. 35, непонятный фрагмент");

        Assert.True(результат.NeedsVerification);

        // ФИО в адресе отсутствует, поэтому первой свободной строкой является строка 1.
        Assert.Equal("непонятный фрагмент", результат.Lines[0]);

        // Разобранные части адреса остаются на своих местах.
        Assert.Equal("ул. Красная", результат.Lines[1]);
        Assert.Equal("д. 35", результат.Lines[2]);
        Assert.Contains("Краснодар", результат.Lines[4]);
    }

    [Fact]
    public void ПустойАдрес_ТребуетПроверки()
    {
        var служба = new AddressHeuristicsService();

        var результат = служба.RecognizeDetailed(string.Empty);

        Assert.True(результат.NeedsVerification);
        Assert.Equal(string.Empty, результат.Zip);
        Assert.All(результат.Lines, строка => Assert.Equal(string.Empty, строка));
    }

    [Fact]
    public void РегионИГородРаспознаютсяПоСловарям()
    {
        var служба = new AddressHeuristicsService();

        var результат = служба.RecognizeDetailed("Краснодарский край, г. Краснодар, ул. Красная");

        Assert.Equal("Краснодарский край", результат.Region);
        Assert.Contains("Краснодар", результат.City);
        Assert.Equal("ул. Красная", результат.Street);
    }

    [Fact]
    public void СловариТопонимов_ЗагружаютсяИзПользовательскогоКаталога()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "picket302_dict_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        try
        {
            File.WriteAllText(
                Path.Combine(каталог, "cities.txt"),
                "ТестовыйГород\nДругойГород|Другое Название\n",
                new System.Text.UTF8Encoding(false));

            var словарь = ToponymDictionary.Load(каталог);

            Assert.True(словарь.IsCity("ТестовыйГород"));
            Assert.True(словарь.IsCity("Другое Название"));
            Assert.True(словарь.IsRegion("Краснодарский край"));
            Assert.True(словарь.IsStreetType("ул."));
            Assert.True(словарь.IsHouseType("д."));
            Assert.True(словарь.IsFlatType("оф."));
        }
        finally
        {
            Directory.Delete(каталог, true);
        }
    }
}
