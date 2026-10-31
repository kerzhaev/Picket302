using Picket302.Domain.Dictionaries;
using Xunit;

namespace Picket302.Tests.Domain;

/// <summary>
/// Проверка словаря полей — единой схемы «БД ↔ Шаблоны ↔ Импорт» (ТЗ §3.2).
/// </summary>
/// <remarks>
/// ТЗ §3.2: «Во избежание расхождений и повреждения данных при миграциях и
/// сопоставлениях закрепляется строгая матрица имен полей. Никакие другие имена
/// не допускаются к использованию в выражениях бланков и маппере импорта».
/// </remarks>
public sealed class FieldDictionaryTests
{
    /// <summary>
    /// Матрица ТЗ §3.2 в исходном виде: колонка БД, тег шаблона, псевдонимы импорта.
    /// </summary>
    public static TheoryData<string, string, string[]> Матрица => new()
    {
        { "counterparties.id", "[id]", Array.Empty<string>() },
        { "to_zip", "[edtIndeks_to]", new[] { "ИндексПолучателя", "ind_to" } },
        { "to_str1", "[edt_str_to1]", new[] { "КомуСтрока1", "str_to1" } },
        { "to_str2", "[edt_str_to2]", new[] { "КомуСтрока2", "str_to2" } },
        { "to_str3", "[edt_str_to3]", new[] { "КомуСтрока3", "str_to3" } },
        { "to_str4", "[edt_str_to4]", new[] { "КомуСтрока4", "str_to4" } },
        { "to_str5", "[edt_str_to5]", new[] { "КомуСтрока5", "str_to5" } },
        { "to_str6", "[edt_str_to6]", new[] { "КомуСтрока6", "str_to6" } },
        { "from_zip", "[edtIndeks_from]", new[] { "ИндексОтправителя", "ind_from" } },
        { "from_str1", "[edt_str_from1]", new[] { "ОтКогоСтрока1", "str_from1" } },
        { "from_str2", "[edt_str_from2]", new[] { "ОтКогоСтрока2", "str_from2" } },
        { "from_str3", "[edt_str_from3]", new[] { "ОтКогоСтрока3", "str_from3" } },
        { "from_str4", "[edt_str_from4]", new[] { "ОтКогоСтрока4", "str_from4" } },
        { "from_str5", "[edt_str_from5]", new[] { "ОтКогоСтрока5", "str_from5" } },
        { "from_str6", "[edt_str_from6]", new[] { "ОтКогоСтрока6", "str_from6" } },
        { "copy_count", "[copyes]", new[] { "КоличествоКопий", "copyes" } },
        { "is_vip", "[vip]", new[] { "Избранное", "vip" } },
        { "note", "[edt_str_prim]", new[] { "Примечание", "prim" } },
        { "declared_value", "[declared_value]", new[] { "Ценность", "declared_value" } },
        { "fee", "[fee]", new[] { "Тариф", "fee" } },
        { "weight", "[weight]", new[] { "Вес", "weight" } },
        { "post_index_machine", "[POSTINDEX]", Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(Матрица))]
    public void Словарь_СодержитПолеМатрицыТЗ(
        string колонка,
        string тег,
        string[] псевдонимы)
    {
        var описание = FieldDictionary.FindByColumn(колонка);

        Assert.NotNull(описание);
        Assert.Equal(тег, описание!.TemplateTag);
        Assert.Equal(псевдонимы, описание.ImportAliases);
        Assert.Equal(колонка, описание.ColumnName);
    }

    /// <summary>
    /// Теги шаблонов уникальны: тег определяет ровно одно поле.
    /// </summary>
    [Fact]
    public void Словарь_ТегиШаблоновУникальны()
    {
        var теги = FieldDictionary.All.Select(поле => поле.TemplateTag).ToList();

        Assert.Equal(теги.Count, теги.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Псевдонимы импорта уникальны: псевдоним определяет ровно одно поле.
    /// </summary>
    [Fact]
    public void Словарь_ПсевдонимыИмпортаУникальны()
    {
        var псевдонимы = FieldDictionary.All
            .SelectMany(поле => поле.ImportAliases)
            .ToList();

        Assert.Equal(псевдонимы.Count, псевдонимы.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Поиск по тегу шаблона и по псевдониму импорта возвращает то же поле,
    /// что и поиск по колонке БД.
    /// </summary>
    [Fact]
    public void Словарь_ПоискПоЛюбомуИмениВозвращаетТоЖеПоле()
    {
        foreach (var описание in FieldDictionary.All)
        {
            Assert.Equal(описание.ColumnName, FieldDictionary.FindByTemplateTag(описание.TemplateTag)?.ColumnName);

            foreach (var псевдоним in описание.ImportAliases)
            {
                Assert.Equal(описание.ColumnName, FieldDictionary.FindByImportAlias(псевдоним)?.ColumnName);
            }
        }
    }

    /// <summary>
    /// Имена, не зафиксированные в ТЗ §3.2, не допускаются.
    /// </summary>
    [Theory]
    [InlineData("to_str7")]
    [InlineData("неизвестное_поле")]
    [InlineData("")]
    public void Словарь_НеизвестнаяКолонкаНеДопускается(string колонка)
    {
        Assert.False(FieldDictionary.IsKnownColumn(колонка));
        Assert.Null(FieldDictionary.FindByColumn(колонка));
    }

    [Theory]
    [InlineData("[edt_str_to7]")]
    [InlineData("[неизвестный_тег]")]
    public void Словарь_НеизвестныйТегШаблонаНеДопускается(string тег)
    {
        Assert.False(FieldDictionary.IsAllowedTemplateTag(тег));
    }

    /// <summary>
    /// Пользовательское поле контрагента доступно как тег
    /// <c>[Custom_&lt;имя&gt;]</c> (ТЗ §3.1.9).
    /// </summary>
    [Fact]
    public void Словарь_ТегПользовательскогоПоляДопускается()
    {
        var тег = FieldDictionary.BuildCustomTag("Договор");

        Assert.Equal("[Custom_Договор]", тег);
        Assert.True(FieldDictionary.IsCustomTemplateTag(тег));
        Assert.True(FieldDictionary.IsAllowedTemplateTag(тег));
    }

    /// <summary>
    /// Вычисляемое поле не импортируется: машинночитаемый штрих-код индекса
    /// формируется вычислением (ТЗ §3.2).
    /// </summary>
    [Fact]
    public void Словарь_ВычисляемоеПолеНеИмпортируется()
    {
        var описание = FieldDictionary.FindByColumn(FieldDictionary.ColumnPostIndexMachine);

        Assert.NotNull(описание);
        Assert.True(описание!.Computed);
        Assert.False(описание.IsImportable);
        Assert.Empty(описание.ImportAliases);
    }
}
