using Picket302.Domain.ValueObjects;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверка доменного правила «почтовый индекс + 6 адресных строк»
/// (ТЗ §3.1.1 и §8.1, функция 4 «Структура адреса: индекс + 6 строк»).
/// </summary>
public sealed class AddressTests
{
    private static readonly string[] ШестьСтрок =
    {
        "ООО «Техно»",
        "Управление делами",
        "ул. Тверская, 12",
        "корп. 2, комн. 4",
        "г. Москва",
        string.Empty,
    };

    [Fact]
    public void Create_ИндексИзШестиЦифр_СоздаетАдресИзШестиСтрок()
    {
        var результат = Address.Create("101000", ШестьСтрок);

        Assert.True(результат.IsSuccess);
        Assert.False(результат.IsFailure);
        Assert.Equal("101000", результат.Value.Zip);
        Assert.Equal(6, результат.Value.Lines.Count);
        Assert.Equal("ООО «Техно»", результат.Value.Line1);
        Assert.Equal("Управление делами", результат.Value.Line2);
        Assert.Equal("ул. Тверская, 12", результат.Value.Line3);
        Assert.Equal("корп. 2, комн. 4", результат.Value.Line4);
        Assert.Equal("г. Москва", результат.Value.Line5);
        Assert.Equal(string.Empty, результат.Value.Line6);
        Assert.Empty(результат.Warnings);
    }

    [Theory]
    [InlineData("10100")]
    [InlineData("1010000")]
    [InlineData("10100A")]
    [InlineData("")]
    public void Create_ИндексНеИзШестиЦифр_ВозвращаетОшибку(string индекс)
    {
        var результат = Address.Create(индекс, ШестьСтрок);

        Assert.True(результат.IsFailure);
        Assert.False(результат.IsSuccess);
        Assert.NotNull(результат.Error);
        Assert.False(результат.TryGetValue(out _));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(7)]
    public void Create_НеШестьАдресныхСтрок_ВозвращаетОшибку(int количествоСтрок)
    {
        var строки = Enumerable.Range(1, количествоСтрок).Select(номер => $"строка {номер}").ToArray();

        var результат = Address.Create("101000", строки);

        Assert.True(результат.IsFailure);
        Assert.NotNull(результат.Error);
    }

    [Fact]
    public void Create_СтрокаДлиннее255Знаков_ОбрезаетсяСПредупреждением()
    {
        var строки = (string[])ШестьСтрок.Clone();
        строки[2] = new string('А', 300);

        var результат = Address.Create("101000", строки);

        Assert.True(результат.IsSuccess);
        Assert.Equal(Address.MaxLineLength, результат.Value.Line3.Length);
        Assert.Single(результат.Warnings);
        Assert.Contains("3", результат.Warnings[0]);
    }

    [Fact]
    public void Create_НезаполненнаяАдреснаяСтрока_ОстаетсяПустой()
    {
        var строки = new string[Address.LineCount];
        строки[0] = "ООО «Техно»";

        var результат = Address.Create("101000", строки);

        Assert.True(результат.IsSuccess);
        Assert.Equal(string.Empty, результат.Value.Line2);
        Assert.Equal(6, результат.Value.Lines.Count);
    }
}
