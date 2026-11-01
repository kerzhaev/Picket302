using Picket302.Domain.Queries;
using Picket302.Infrastructure.Queries;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Слой запросов на SQLite текущей поставки (ТЗ §4.1–4.2: восемь запросов
/// с точными именами колонок; PostgreSQL-вариант хранится для совместимости).
/// </summary>
public sealed class ReportQueriesTests : IAsyncLifetime
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
    public void Каталог_ВосемьЗапросов_ИОбаДиалекта()
    {
        Assert.Equal(8, BuiltinReportQueryCatalog.QueryIds.Count);

        foreach (var идентификатор in BuiltinReportQueryCatalog.QueryIds)
        {
            var определение = BuiltinReportQueryCatalog.Get(идентификатор);

            Assert.Equal(идентификатор, определение.Id);
            Assert.NotNull(определение.GetCommand("Sqlite").Sql);
            Assert.NotNull(определение.GetCommand("PostgreSql").Sql);
            Assert.Contains(
                определение.ValidationRules,
                правило => правило.Type == QueryRuleTypes.SqlInjectionGuard
                    && правило.Mode == SqlGuardModes.StrictParametric);
        }

        foreach (var идентификатор in BuiltinReportQueryCatalog.AuxiliaryQueryIds)
        {
            Assert.NotNull(BuiltinReportQueryCatalog.Get(идентификатор).GetCommand("Sqlite").Sql);
        }
    }

    [Fact]
    public async Task Envelopes_ТочныеИменаКолонок()
    {
        var значения = new QueryParameterValues();
        значения.Set("SelectedMailIds", new List<long> { 1, 2 });

        var строки = await _поставщик.RunQueryAsync("qrReport_Envelopes", значения);

        Assert.Equal(2, строки.Count);

        var ожидаемые = new[]
        {
            "mail_id", "ind_to", "str_to1", "str_to2", "str_to3", "str_to4", "str_to5", "str_to6",
            "ind_from", "str_from1", "str_from2", "str_from3", "str_from4", "str_from5", "str_from6",
            "prim", "copyes", "vip", "date_print",
        };

        Assert.Equal(ожидаемые, строки[0].ColumnNames);
        Assert.Equal(1L, строки[0].GetValue("mail_id"));
        Assert.Equal("100000", строки[0].GetValue("ind_to"));
        Assert.Equal("101000", строки[0].GetValue("ind_from"));
        Assert.Equal("г. Москва", строки[0].GetValue("str_from1"));
        Assert.Equal("ул. Первая", строки[0].GetValue("str_from2"));
        Assert.Equal("примечание 1", строки[0].GetValue("prim"));
    }

    [Fact]
    public async Task QrFetch_ВозвращаетРастр()
    {
        var значения = new QueryParameterValues();
        значения.Set("PayloadHash", "6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b");

        var строки = await _поставщик.RunQueryAsync("qrQRcode_Fetch", значения);

        Assert.Single(строки);
        Assert.Equal("img_qrcode", Assert.Single(строки[0].ColumnNames));
        Assert.NotNull(строки[0].GetValue("img_qrcode"));
    }

    [Fact]
    public async Task Inventory107_КолонкиИОкноCountKonv()
    {
        var значения = new QueryParameterValues();
        значения.Set("InventoryId", 1L);

        var строки = await _поставщик.RunQueryAsync("qrReport_Inventory107", значения);

        Assert.Equal(2, строки.Count);

        Assert.Equal(new[] { "inv_name", "komu", "kuda", "det_name", "kol", "cost", "total_item_cost", "count_konv" }, строки[0].ColumnNames);
        Assert.Equal("Опись документов", строки[0].GetValue("inv_name"));
        Assert.Equal("Иванов Иван Иванович", строки[0].GetValue("komu"));
        Assert.Equal(2L, строки[0].GetValue("count_konv"));
        Assert.Equal(2L, строки[0].GetValue("kol"));
        Assert.Equal(301.00m, Число(строки[0].GetValue("total_item_cost")));
    }

    [Fact]
    public async Task IncomingJournal_КолонкиИСортировкаПоДате()
    {
        await using var команда = _база.Connection.CreateCommand();
        команда.CommandText = "UPDATE mails SET incoming_number = 'вх-' || id WHERE id <= 2;";
        await команда.ExecuteNonQueryAsync();

        var значения = new QueryParameterValues();
        значения.Set("DateFrom", DateTime.UtcNow.Date.AddDays(-1));
        значения.Set("DateTo", DateTime.UtcNow.Date.AddDays(1));

        var строки = await _поставщик.RunQueryAsync("qrReport_IncomingJournal", значения);

        Assert.Equal(2, строки.Count);
        Assert.Equal(
            new[] { "inc_num", "reg_date", "sender_name", "sender_inn", "subject", "execution_mark", "doc_state" },
            строки[0].ColumnNames);
    }

    [Fact]
    public async Task MailingStats_КолонкиИГруппировка()
    {
        var значения = new QueryParameterValues();
        значения.Set("DateFrom", DateTime.UtcNow.Date.AddDays(-1));
        значения.Set("DateTo", DateTime.UtcNow.Date.AddDays(1));

        var строки = await _поставщик.RunQueryAsync("qrReport_MailingStats", значения);

        Assert.Single(строки);
        Assert.Equal(new[] { "gr_name", "count_konv", "total_fee", "total_val", "date_stat" }, строки[0].ColumnNames);
        Assert.Equal("Клиенты", строки[0].GetValue("gr_name"));
        Assert.Equal(3L, строки[0].GetValue("count_konv"));
        Assert.Equal(195.00m, Число(строки[0].GetValue("total_fee")));
    }

    [Fact]
    public async Task GroupEnvelopes_АктивныеИНеудаленные()
    {
        var значения = new QueryParameterValues();
        значения.Set("GroupId", 1L);

        var строки = await _поставщик.RunQueryAsync("qrReport_GroupEnvelopes", значения);

        Assert.Equal(4, строки.Count);
        Assert.Equal(
            new[] { "contr_id", "ind_to", "str_to1", "str_to2", "str_to3", "str_to4", "str_to5", "str_to6", "prim", "group_name" },
            строки[0].ColumnNames);
        Assert.Equal("Клиенты", строки[0].GetValue("group_name"));
    }

    [Fact]
    public async Task PrintHistory_КолонкиИсторическогоСреза()
    {
        var значения = new QueryParameterValues();
        значения.Set("DateFrom", DateTime.UtcNow.Date.AddDays(-1));
        значения.Set("DateTo", DateTime.UtcNow.Date.AddDays(1));

        var строки = await _поставщик.RunQueryAsync("qrReport_PrintHistoryReport", значения);

        Assert.Single(строки);
        Assert.Equal(
            new[] { "printed_at", "template_description", "recipient_address_snapshot", "sender_address_snapshot", "copy_count", "printer_name", "paper_size_name", "status" },
            строки[0].ColumnNames);
        Assert.Equal("Конверт C5 (2012)", строки[0].GetValue("template_description"));
    }

    [Fact]
    public async Task MoneyTransfer112ep_РеквизитыПеревода()
    {
        var значения = new QueryParameterValues();
        значения.Set("MailId", 1L);

        var строки = await _поставщик.RunQueryAsync("qrReport_MoneyTransfer112ep", значения);

        Assert.Single(строки);
        Assert.Equal(
            new[] { "id", "transfer_amount", "recipient_fio", "pass_ser", "pass_num", "pass_whowhen", "bik", "rs", "ks", "bank_name", "sender_fio", "sender_inn" },
            строки[0].ColumnNames);
        Assert.Equal("Иванов Иван Иванович", строки[0].GetValue("recipient_fio"));
        Assert.Equal("1111", строки[0].GetValue("pass_ser"));
        Assert.Equal("ООО Отправитель", строки[0].GetValue("sender_fio"));
    }

    [Fact]
    public async Task ДиапазонДат91день_Отклонение()
    {
        var значения = new QueryParameterValues();
        значения.Set("DateFrom", new DateTime(2024, 1, 1));
        значения.Set("DateTo", new DateTime(2024, 4, 1));

        await Assert.ThrowsAsync<QueryValidationException>(
            () => _поставщик.RunQueryAsync("qrReport_IncomingJournal", значения));
    }

    [Fact]
    public async Task ИнъекцияВСтроке_StrictParametricОтклоняет()
    {
        var значения = new QueryParameterValues();
        значения.Set("PayloadHash", "abc'; DROP TABLE qr_cache;--");

        await Assert.ThrowsAsync<QueryValidationException>(
            () => _поставщик.RunQueryAsync("qrQRcode_Fetch", значения));

        // Таблица не пострадала: инъекция не исполнялась.
        var чистый = new QueryParameterValues();
        чистый.Set("PayloadHash", "6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b");

        var строки = await _поставщик.RunQueryAsync("qrQRcode_Fetch", чистый);

        Assert.Single(строки);
    }

    [Fact]
    public async Task ПустойСписокИдентификаторов_ПустаяВыборка()
    {
        var значения = new QueryParameterValues();
        значения.Set("SelectedMailIds", new List<long>());

        var строки = await _поставщик.RunQueryAsync("qrReport_Envelopes", значения);

        Assert.Empty(строки);
    }

    private static decimal Число(object? значение) => значение switch
    {
        null => 0m,
        decimal число => число,
        long число => число,
        double число => (decimal)число,
        string текст when decimal.TryParse(текст, out var разобрано) => разобрано,
        _ => 0m,
    };
}
