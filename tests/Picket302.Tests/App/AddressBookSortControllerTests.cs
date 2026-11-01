using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Application.Queries;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверки сортировки адресной книги по клику на заголовок столбца
/// (ТЗ §8.1, функция 9: «Фильтры и сортировка по любой колонке»).
/// </summary>
public sealed class AddressBookSortControllerTests
{
    /// <summary>
    /// Создаёт таблицу адресной книги с колонками каталога.
    /// </summary>
    /// <returns>Таблица.</returns>
    private static DataGridView Таблица()
    {
        var таблица = new DataGridView();

        foreach (var колонка in AddressBookColumnCatalog.Columns)
        {
            таблица.Columns.Add(колонка.GridName, колонка.Caption);
        }

        return таблица;
    }

    /// <summary>
    /// Клик по заголовку задаёт колонку сортировки и формирует критерии запроса.
    /// </summary>
    [Fact]
    public void КликПоЗаголовку_ЗадаётКолонкуСортировки()
    {
        using var таблица = Таблица();
        var контроллер = new AddressBookSortController();
        var изменена = false;
        контроллер.СортировкаИзменена += (_, _) => изменена = true;

        контроллер.Подключить(таблица);

        Assert.True(контроллер.ОбработатьКликПоЗаголовку(2));
        Assert.Equal(AddressBookColumnCatalog.КлючПолучатель, контроллер.КолонкаСортировки);
        Assert.False(контроллер.ПоУбыванию);
        Assert.True(изменена);

        var критерии = контроллер.ПостроитьКритерии();
        Assert.Equal(AddressBookColumnCatalog.КлючПолучатель, критерии.SortColumn);
        Assert.False(критерии.SortDescending);
    }

    /// <summary>
    /// Повторный клик по заголовку той же колонки меняет направление сортировки.
    /// </summary>
    [Fact]
    public void ПовторныйКликПоЗаголовку_МеняетНаправлениеСортировки()
    {
        using var таблица = Таблица();
        var контроллер = new AddressBookSortController();
        контроллер.Подключить(таблица);

        контроллер.ОбработатьКликПоЗаголовку(2);
        контроллер.ОбработатьКликПоЗаголовку(2);

        Assert.True(контроллер.ПоУбыванию);
        Assert.True(контроллер.ПостроитьКритерии().SortDescending);

        // Клик по другой колонке начинает сортировку заново — по возрастанию.
        контроллер.ОбработатьКликПоЗаголовку(3);

        Assert.Equal(AddressBookColumnCatalog.КлючГород, контроллер.КолонкаСортировки);
        Assert.False(контроллер.ПоУбыванию);
    }

    /// <summary>
    /// Вычисляемые проекции адресной книги имеют собственные ключи сортировки.
    /// </summary>
    [Fact]
    public void ВычисляемыеКолонки_ИмеютСобственныеКлючиСортировки()
    {
        using var таблица = Таблица();
        var контроллер = new AddressBookSortController();
        контроллер.Подключить(таблица);

        Assert.True(контроллер.ОбработатьКликПоЗаголовку(1));
        Assert.Equal("main_zip", контроллер.КолонкаСортировки);

        Assert.True(контроллер.ОбработатьКликПоЗаголовку(6));
        Assert.Equal("group", контроллер.КолонкаСортировки);
    }

    /// <summary>
    /// Столбец, не входящий в состав адресной книги, сортировку не задаёт.
    /// </summary>
    [Fact]
    public void СтолбецВнеАдреснойКниги_СортировкуНеЗадаёт()
    {
        using var таблица = Таблица();
        таблица.Columns.Add("Сумма", "Сумма");

        var контроллер = new AddressBookSortController();
        контроллер.Подключить(таблица);

        Assert.False(контроллер.ОбработатьКликПоЗаголовку(таблица.Columns.Count - 1));
        Assert.Null(контроллер.КолонкаСортировки);
        Assert.Null(контроллер.ПостроитьКритерии().SortColumn);
    }
}
