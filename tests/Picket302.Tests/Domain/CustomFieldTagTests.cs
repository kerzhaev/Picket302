using Picket302.Domain.Contracts;
using Picket302.Domain.Rules;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверка тегов пользовательских полей контрагента (ТЗ §8.1, функция 7:
/// «Пользовательские поля контрагента»; §8.1, функция 4: «Переход на теги
/// [Custom_*]»).
/// </summary>
public sealed class CustomFieldTagTests
{
    [Fact]
    public void Format_ОбычноеИмяПоля_ДаётТегВидаCustom()
    {
        Assert.Equal("[Custom_КодКлиента]", CustomFieldTag.Format("КодКлиента"));
    }

    [Fact]
    public void Format_ИмяСПробелами_ПробелыЗаменяютсяПодчёркиванием()
    {
        Assert.Equal("[Custom_Код_Клиента]", CustomFieldTag.Format("Код Клиента"));
    }

    [Fact]
    public void Format_ПустоеИмя_ВыбрасываетИсключение()
    {
        Assert.Throws<ArgumentException>(() => CustomFieldTag.Format("  "));
    }

    [Fact]
    public void Format_ИмяСоСкобками_ВыбрасываетИсключение()
    {
        Assert.Throws<ArgumentException>(() => CustomFieldTag.Format("Код[Клиента]"));
    }

    [Fact]
    public void IsTag_КорректныйТег_ПризнаётсяТегом()
    {
        Assert.True(CustomFieldTag.IsTag("[Custom_КодКлиента]"));
        Assert.True(CustomFieldTag.IsTag("[Custom_Договор]"));
    }

    [Theory]
    [InlineData("Custom_КодКлиента")]
    [InlineData("[КодКлиента]")]
    [InlineData("[Custom_]")]
    [InlineData("[Другое_Поле]")]
    [InlineData("")]
    public void IsTag_НекорректнаяСтрока_НеПризнаётсяТегом(string значение)
    {
        Assert.False(CustomFieldTag.IsTag(значение));
    }

    [Fact]
    public void ParseName_КорректныйТег_ВозвращаетИмяПоля()
    {
        Assert.Equal("КодКлиента", CustomFieldTag.ParseName("[Custom_КодКлиента]"));
    }

    [Fact]
    public void ParseName_НекорректныйТег_ВозвращаетNull()
    {
        Assert.Null(CustomFieldTag.ParseName("КодКлиента"));
    }

    [Fact]
    public void FormatАЗатемParseName_ВозвращаютИсходноеИмя()
    {
        var тег = CustomFieldTag.Format("Номер договора");

        Assert.Equal("Номер_договора", CustomFieldTag.ParseName(тег));
    }
}

/// <summary>
/// Проверка значений по умолчанию для оформления таблиц (ТЗ §7.10: «Три
/// независимых настраиваемых цвета: фокус, выделение, подсказка; значения по
/// умолчанию: 4227327, 6205183, 3723516»; §8.11, функция 131).
/// </summary>
public sealed class GridAppearanceTests
{
    [Fact]
    public void Default_ЦветаСоответствуютСпецификации()
    {
        var оформление = GridAppearance.Default;

        Assert.Equal(4227327, оформление.FocusColor);
        Assert.Equal(6205183, оформление.SelectedColor);
        Assert.Equal(3723516, оформление.HintColor);
    }

    [Fact]
    public void Default_ПлотностьСоответствуетСпецификации()
    {
        var оформление = GridAppearance.Default;

        Assert.Equal(8f, оформление.FontSize);
        Assert.Equal(17, оформление.RowHeight);
    }

    [Fact]
    public void Default_ЗебраВыключена()
    {
        Assert.False(GridAppearance.Default.ZebraEnabled);
    }

    [Fact]
    public void Цвета_НезависимыДругОтДруга()
    {
        var оформление = GridAppearance.Default with { FocusColor = 1 };

        Assert.Equal(1, оформление.FocusColor);
        Assert.Equal(6205183, оформление.SelectedColor);
        Assert.Equal(3723516, оформление.HintColor);
    }
}
