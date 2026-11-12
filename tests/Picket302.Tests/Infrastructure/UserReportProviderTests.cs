using Picket302.Domain.Queries;
using Picket302.Infrastructure.Queries;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// S27 (ТЗ §8.4, функция 56): исполнение пользовательских определений
/// запросов поверх SQLite текущей поставки — <see cref="SqliteReportDataProvider.
/// RunDefinitionAsync"/> с штатной валидацией и строгим параметрическим
/// режимом значений.
/// </summary>
public sealed class UserReportProviderTests : IAsyncLifetime
{
    private SqliteTestDatabase _база = null!;
    private SqliteReportDataProvider _поставщик = null!;

    public async Task InitializeAsync()
    {
        _база = await SqliteTestDatabase.CreateAsync();
        await ReportDataSeeder.SeedAsync(_база.Connection);
        _поставщик = new SqliteReportDataProvider(_база.Connection);
    }

    public async Task DisposeAsync()
    {
        await _база.DisposeAsync();
    }

    [Fact]
    public async Task ПользовательскийЗапрос_ВозвращаетСтрокиТекущейБазы()
    {
        var определение = new ReportQueryXmlSerializer().Deserialize(ТекстЗапросаГрупп());

        var строки = await _поставщик.RunDefinitionAsync(
            определение,
            new QueryParameterValues());

        var имена = строки.Select(строка => строка.GetValue("name")).ToList();

        Assert.NotEmpty(имена);
        Assert.Contains("Клиенты", имена);
    }

    [Fact]
    public async Task ЗапросСоСпискомГрупп_РазворачиваетПараметрIN()
    {
        var определение = new ReportQueryXmlSerializer().Deserialize(ТекстЗапросаПоГруппам());

        var значения = new QueryParameterValues();
        значения.Set("GroupIds", new List<long> { 1 });

        var строки = await _поставщик.RunDefinitionAsync(определение, значения);

        Assert.NotEmpty(строки);
    }

    [Fact]
    public async Task ПустойСписокГрупп_ГарантированноПустаяВыборка()
    {
        var определение = new ReportQueryXmlSerializer().Deserialize(ТекстЗапросаПоГруппам());

        var значения = new QueryParameterValues();
        значения.Set("GroupIds", new List<long>());

        var строки = await _поставщик.RunDefinitionAsync(определение, значения);

        Assert.Empty(строки);
    }

    [Fact]
    public async Task ДиалектБезSqliteКоманды_Отклоняется()
    {
        var текст = """
            <?xml version="1.0" encoding="utf-8"?>
            <ReportQueryDefinition id="qrUser_OnlyPg" version="1.0">
              <Name>Запрос без команды SQLite</Name>
              <Parameters />
              <ValidationRules>
                <Rule type="SqlInjectionGuard" mode="StrictParametric" />
              </ValidationRules>
              <Command dialect="PostgreSql">
                <![CDATA[SELECT name FROM groups;]]>
              </Command>
            </ReportQueryDefinition>
            """;

        var определение = new ReportQueryXmlSerializer().Deserialize(текст);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _поставщик.RunDefinitionAsync(определение, new QueryParameterValues()));
    }

    [Fact]
    public async Task НеобъявленныйПараметр_ОтклоняетсяВалидатором()
    {
        var текст = """
            <?xml version="1.0" encoding="utf-8"?>
            <ReportQueryDefinition id="qrUser_BadParam" version="1.0">
              <Name>Запрос с необъявленным параметром</Name>
              <Parameters />
              <ValidationRules>
                <Rule type="SqlInjectionGuard" mode="StrictParametric" />
              </ValidationRules>
              <Command dialect="Sqlite">
                <![CDATA[SELECT name FROM groups WHERE id = :GroupIds;]]>
              </Command>
            </ReportQueryDefinition>
            """;

        var определение = new ReportQueryXmlSerializer().Deserialize(текст);

        await Assert.ThrowsAsync<QueryValidationException>(() =>
            _поставщик.RunDefinitionAsync(определение, new QueryParameterValues()));
    }

    [Fact]
    public async Task ЗначениеСSqlКонструкцией_ОтклоняетсяСтрогимРежимом()
    {
        var определение = new ReportQueryXmlSerializer().Deserialize(ТекстЗапросаПоГруппам());

        var значения = new QueryParameterValues();
        значения.Set("GroupIds", new List<long> { 1 });
        // Строковый параметр с SQL-комментарием: строгий параметрический
        // режим проверяет входной поток до обращения к драйверу.
        значения.Set("Подпись", "группы-- служебный комментарий");

        await Assert.ThrowsAsync<QueryValidationException>(() =>
            _поставщик.RunDefinitionAsync(определение, значения));
    }

    /// <summary>Валидный пользовательский запрос перечня групп.</summary>
    private static string ТекстЗапросаГрупп() => """
        <?xml version="1.0" encoding="utf-8"?>
        <ReportQueryDefinition id="qrUser_Groups" version="1.0">
          <Name>Перечень групп пользователя</Name>
          <Parameters />
          <ValidationRules>
            <Rule type="SqlInjectionGuard" mode="StrictParametric" />
          </ValidationRules>
          <Command dialect="Sqlite">
            <![CDATA[SELECT name FROM groups ORDER BY name;]]>
          </Command>
        </ReportQueryDefinition>
        """;

    /// <summary>Валидный пользовательский запрос по списку групп.</summary>
    private static string ТекстЗапросаПоГруппам() => """
        <?xml version="1.0" encoding="utf-8"?>
        <ReportQueryDefinition id="qrUser_GroupsByIds" version="1.0">
          <Name>Группы по списку идентификаторов</Name>
          <Parameters>
            <Parameter name="GroupIds" type="Int64" required="false" />
            <Parameter name="Подпись" type="String" required="false" />
          </Parameters>
          <ValidationRules>
            <Rule type="SqlInjectionGuard" mode="StrictParametric" />
          </ValidationRules>
          <Command dialect="Sqlite">
            <![CDATA[SELECT name FROM groups WHERE id IN (:GroupIds) ORDER BY name;]]>
          </Command>
        </ReportQueryDefinition>
        """;
}
