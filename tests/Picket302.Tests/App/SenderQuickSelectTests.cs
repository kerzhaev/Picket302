using Picket302.App.UI.Components;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Выбор отправителя по первым буквам наименования
/// (ТЗ §8.1, функция 16: «Текущий отправитель» как глобальная сущность;
/// выбор отправителя по первым буквам).
/// </summary>
public sealed class SenderQuickSelectTests
{
    private static readonly (long Id, string Name)[] Отправители =
    [
        (1, "ООО «Ромашка»"),
        (2, "АО «Ромашка-Центр»"),
        (3, "ПАО «Северный Ветер»"),
    ];

    [Fact]
    public void FindByPrefix_НаходитОтправителяПоПервымБуквам()
    {
        var найденный = SenderQuickSelect.FindByPrefix(Отправители, "ООО");

        Assert.Equal(1, найденный);
    }

    [Fact]
    public void FindByPrefix_НечувствителенКРегистру()
    {
        var найденный = SenderQuickSelect.FindByPrefix(Отправители, "пао");

        Assert.Equal(3, найденный);
    }

    [Fact]
    public void FindByPrefix_ВозвращаетПервоеСовпадение()
    {
        // «ООО «Ромашка»» идёт раньше «ООО «Ромашка-Центр»» в справочнике.
        var найденный = SenderQuickSelect.FindByPrefix(Отправители, "ООО «Ромашка");

        Assert.Equal(1, найденный);
    }

    [Fact]
    public void FindByPrefix_СовпадениеПоНачалуНаименования_НеСовпадаетСовпадениемВнутри()
    {
        // Совпадение внутри наименования («Ромашка» в «ООО «Ромашка»») не считается.
        var найденный = SenderQuickSelect.FindByPrefix(Отправители, "Ромашка");

        Assert.Null(найденный);
    }

    [Fact]
    public void FindByPrefix_БезСовпадений_ВозвращаетNull()
    {
        var найденный = SenderQuickSelect.FindByPrefix(Отправители, "Почта");

        Assert.Null(найденный);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindByPrefix_ПустойПрефикс_ВозвращаетNull(string? префикс)
    {
        var найденный = SenderQuickSelect.FindByPrefix(Отправители, префикс);

        Assert.Null(найденный);
    }
}
