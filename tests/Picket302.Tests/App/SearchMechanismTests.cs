using System.Windows.Forms;
using Picket302.App.UI.Components;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверка трёх механизмов поиска и подбора значений поля (ТЗ §7.7: «Три
/// механизма поиска (все обязательны)»; §8.1, функция 133).
/// </summary>
public sealed class SearchMechanismTests
{
    private static readonly string[] Записи =
    [
        "ООО «Техно»",
        "ООО «Торг»",
        "ПАО «МосБанк»",
        "ИП Смирнов",
    ];

    [Fact]
    public void НайтиДалее_СовпадениеВпереди_ВозвращаетСледующуюЗапись()
    {
        var индекс = SearchMechanism.НайтиДалее(Записи, запись => запись, "ООО", -1);

        Assert.Equal(0, индекс);
    }

    [Fact]
    public void НайтиДалее_ПоискСТекущейЗаписи_ВозвращаетСледующееСовпадение()
    {
        var индекс = SearchMechanism.НайтиДалее(Записи, запись => запись, "ООО", 0);

        Assert.Equal(1, индекс);
    }

    [Fact]
    public void НайтиДалее_СовпадениеВКонце_ПоискОборачивается()
    {
        var индекс = SearchMechanism.НайтиДалее(Записи, запись => запись, "ИП", 3);

        Assert.Equal(3, индекс);
    }

    [Fact]
    public void НайтиДалее_СовпаденийНет_ВозвращаетМинусЕдиница()
    {
        Assert.Equal(-1, SearchMechanism.НайтиДалее(Записи, запись => запись, "ООО «Несуществующая»", -1));
    }

    [Fact]
    public void НайтиДалее_ПустойЗапрос_СовпаденийНет()
    {
        Assert.Equal(-1, SearchMechanism.НайтиДалее(Записи, запись => запись, string.Empty, -1));
    }

    [Theory]
    [InlineData(РежимСовпадения.Содержит, 0)]
    [InlineData(РежимСовпадения.НачинаетсяС, 0)]
    [InlineData(РежимСовпадения.Полное, -1)]
    public void НайтиДалее_РежимСовпадения_Применяется(
        РежимСовпадения режим,
        int ожидаемыйИндекс)
    {
        var индекс = SearchMechanism.НайтиДалее(
            Записи,
            запись => запись,
            "ООО",
            -1,
            режим);

        Assert.Equal(ожидаемыйИндекс, индекс);
    }

    [Fact]
    public void НайтиДалее_УчетРегистра_ИФормата_НеОслабляетКонтракт()
    {
        Assert.Equal(
            0,
            SearchMechanism.НайтиДалее(
                ["  ООО «Техно»"],
                запись => запись,
                "  ООО",
                -1,
                РежимСовпадения.НачинаетсяС,
                учитыватьРегистр: true,
                учитыватьФормат: true));

        Assert.Equal(
            -1,
            SearchMechanism.НайтиДалее(
                ["ООО «Техно»"],
                запись => запись,
                "ооо",
                -1,
                РежимСовпадения.НачинаетсяС,
                учитыватьРегистр: true));
    }

    [Fact]
    public void Совпадения_ПрямойНабор_ОтбираетПоНачалуСтроки()
    {
        var индексы = SearchMechanism.Совпадения(Записи, запись => запись, "ООО");

        Assert.Equal([0, 1], индексы);
    }

    [Fact]
    public void Совпадения_ПустойНабор_ВозвращаетПустойСписок()
    {
        Assert.Empty(SearchMechanism.Совпадения(Записи, запись => запись, string.Empty));
    }

    [Fact]
    public void SearchSession_СбросПоEsc_СнимаетУсловияПоОдному()
    {
        var сессия = new SearchSession();

        сессия.ДобавитьВПрямойНабор('О');
        сессия.ЗадатьФормуПоиска("Техно");
        сессия.ЗадатьФильтрИзбранного(true);
        сессия.ЗадатьФильтрГрупп(5);

        Assert.Equal(СбросПоиска.СброшенПрямойНабор, сессия.СброситьПоEsc());
        Assert.Equal(СбросПоиска.СброшенаФормаПоиска, сессия.СброситьПоEsc());
        Assert.Equal(СбросПоиска.СброшенФильтрИзбранного, сессия.СброситьПоEsc());
        Assert.Equal(СбросПоиска.СброшенФильтрГрупп, сессия.СброситьПоEsc());
        Assert.Equal(СбросПоиска.НичегоНеСброшено, сессия.СброситьПоEsc());

        Assert.False(сессия.ЕстьУсловия);
    }

    [Fact]
    public void SearchSession_ДвойноеНажатиеEsc_СбрасываетНаборИФормуПоиска()
    {
        var сессия = new SearchSession();

        сессия.ДобавитьВПрямойНабор('О');
        сессия.ЗадатьФормуПоиска("Техно");

        Assert.Equal(СбросПоиска.СброшенПрямойНабор, сессия.СброситьПоEsc());
        Assert.Equal(СбросПоиска.СброшенаФормаПоиска, сессия.СброситьПоEsc());
    }

    [Fact]
    public void SearchSession_ФильтрыРаздельны_ИзбранноеИГруппаНеСливаются()
    {
        var сессия = new SearchSession();

        сессия.ЗадатьФильтрИзбранного(true);
        сессия.ЗадатьФильтрГрупп(7);

        Assert.True(сессия.ТолькоИзбранное);
        Assert.Equal(7, сессия.Группа);

        сессия.ЗадатьФильтрИзбранного(false);

        Assert.False(сессия.ТолькоИзбранное);
        Assert.Equal(7, сессия.Группа);
    }

    [Fact]
    public void SearchSession_УдалениеИзПрямогоНабора_СнимаетПоследнийСимвол()
    {
        var сессия = new SearchSession();

        сессия.ДобавитьВПрямойНабор('О');
        сессия.ДобавитьВПрямойНабор('О');
        сессия.УдалитьИзПрямогоНабора();

        Assert.Equal("О", сессия.ПрямойНабор);
    }

    [Fact]
    public void FieldValueAutoComplete_ПустоеПолеИBackSpace_ПодборПредлагается()
    {
        Assert.True(FieldValueAutoComplete.ТребуетсяПодбор(полеПустое: true, Keys.Back));
    }

    [Theory]
    [InlineData(true, Keys.A)]
    [InlineData(false, Keys.Back)]
    public void FieldValueAutoComplete_ИныеСлучаи_ПодборНеПредлагается(bool полеПустое, Keys клавиша)
    {
        Assert.False(FieldValueAutoComplete.ТребуетсяПодбор(полеПустое, клавиша));
    }

    [Fact]
    public async Task FieldValueAutoComplete_ПолучитьЗначения_ВозвращаетЗначенияПоля()
    {
        var подбор = new FieldValueAutoComplete((поле, _) =>
            Task.FromResult<IReadOnlyList<string>>(
                поле == "inn" ? ["7707083893", "7740000005"] : []));

        var значения = await подбор.ПолучитьЗначенияAsync("inn");

        Assert.Equal(["7707083893", "7740000005"], значения);
    }

    [Fact]
    public void FieldValueAutoComplete_Подобрать_НаходитЗначениеПоПрефиксу()
    {
        var значения = new[] { "ООО «Техно»", "ООО «Торг»" };

        Assert.Equal("ООО «Торг»", FieldValueAutoComplete.Подобрать(значения, "ООО «Тор"));
        Assert.Null(FieldValueAutoComplete.Подобрать(значения, "ПАО"));
    }

    [Fact]
    public void FieldValueAutoComplete_ПодобратьБезПрефикса_ВозвращаетПервоеЗначение()
    {
        Assert.Equal(
            "ООО «Техно»",
            FieldValueAutoComplete.Подобрать(["ООО «Техно»", "ООО «Торг»"], null));
    }
}
