using Picket302.Printing;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Функция №33 (ТЗ §8.4, S21): реверс порядка адресных строк
/// AddressOrderSmallToLarge при формировании печатного текста —
/// сохраняя перенос по словам/буквам (меняется только порядок готовых
/// строк, не их содержимое).
/// </summary>
public sealed class ApplyAddressOrderTests
{
    private static PrintableItem Элемент(params (string Ключ, string Значение)[] поля)
    {
        var значения = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (ключ, значение) in поля)
        {
            значения[ключ] = значение;
        }

        return new PrintableItem { Id = 1, Values = значения };
    }

    [Fact]
    public void SmallToLarge_РеверсНепустыхСтрокПолучателя()
    {
        var элемент = Элемент(
            ("to_str1", "г. Москва"),
            ("to_str2", "ул. Тверская, 12"),
            ("to_str3", "каб. 5"),
            ("to_str6", "РФ"));

        var результат = PrintSessionController.ApplyAddressOrder(new[] { элемент }, smallToLarge: true)[0];

        // Чистый реверс порядка непустых строк: последняя сохранённая
        // («РФ») печатается первой — «от частного к общему» относительно
        // сохранённого порядка «от общего к частному». NULL-слоты
        // (to_str4/5) и хвост заполняются пустыми строками.
        Assert.Equal("РФ", результат.Values["to_str1"]);
        Assert.Equal("каб. 5", результат.Values["to_str2"]);
        Assert.Equal("ул. Тверская, 12", результат.Values["to_str3"]);
        Assert.Equal("г. Москва", результат.Values["to_str4"]);
        Assert.Equal(string.Empty, результат.Values["to_str5"]);
        Assert.Equal(string.Empty, результат.Values["to_str6"]);
    }

    [Fact]
    public void SmallToLarge_РеверсИСтрокОтправителя()
    {
        var элемент = Элемент(
            ("from_str1", "ООО «Ромашка»"),
            ("from_str2", "г. Тверь"),
            ("from_str3", "ул. Ленина, 1"));

        var результат = PrintSessionController.ApplyAddressOrder(new[] { элемент }, smallToLarge: true)[0];

        Assert.Equal("ул. Ленина, 1", результат.Values["from_str1"]);
        Assert.Equal("г. Тверь", результат.Values["from_str2"]);
        Assert.Equal("ООО «Ромашка»", результат.Values["from_str3"]);
    }

    [Fact]
    public void LargeToSmall_ПорядокНеМеняется()
    {
        var элемент = Элемент(
            ("to_str1", "г. Москва"),
            ("to_str2", "ул. Тверская, 12"));

        var результат = PrintSessionController.ApplyAddressOrder(new[] { элемент }, smallToLarge: false)[0];

        Assert.Equal("г. Москва", результат.Values["to_str1"]);
        Assert.Equal("ул. Тверская, 12", результат.Values["to_str2"]);
    }

    [Fact]
    public void ОднаСтрока_РеверсНичегоНеМеняет()
    {
        var элемент = Элемент(("to_str1", "единственная строка"));

        var результат = PrintSessionController.ApplyAddressOrder(new[] { элемент }, smallToLarge: true)[0];

        Assert.Equal("единственная строка", результат.Values["to_str1"]);
    }

    [Fact]
    public void СодержимоеСтрокНеИзменяется_ТолькоПорядок()
    {
        var строки = new[] { "AAAA", "BBBB BBBB", "CCCC" };
        var элемент = Элемент(
            ("to_str1", строки[0]),
            ("to_str2", строки[1]),
            ("to_str3", строки[2]));

        var результат = PrintSessionController.ApplyAddressOrder(new[] { элемент }, smallToLarge: true)[0];

        Assert.Equal(строки[2], результат.Values["to_str1"]);
        Assert.Equal(строки[1], результат.Values["to_str2"]);
        Assert.Equal(строки[0], результат.Values["to_str3"]);
    }
}
