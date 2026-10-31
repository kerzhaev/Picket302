using Picket302.Application.Formatting;
using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Queries;
using Picket302.Printing.Reports;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;
/// <summary>
/// Реестры, описи, отчеты и служба истории (ТЗ §8.4, функции 50–64).
/// </summary>
public sealed class RegistryReportsTests : IAsyncLifetime
{
    private SqliteTestDatabase _база = null!;
    private SqliteReportDataProvider _поставщик = null!;

    public async Task InitializeAsync()
    {
        _база = await SqliteTestDatabase.CreateAsync().ConfigureAwait(false);
        await ReportDataSeeder.SeedAsync(_база.Connection).ConfigureAwait(false);
        _поставщик = new SqliteReportDataProvider(_база.Connection);
    }

    public async Task DisposeAsync()
    {
        await _база.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task Ф103_ИтогиСходятся()
    {
        var отчёт = new Form103Report(_поставщик);

        var документ = await отчёт.GenerateF103(new[] { 1L, 2L }, ReportDataSeeder.SenderId).ConfigureAwait(false);

        Assert.Equal(2, документ.Main.Rows.Count);

        // Итоговая строка платы равна сумме строк табличной части.
        Assert.True(Form103Report.TotalsMatch(документ));
        Assert.Equal(130.00m, документ.Main.TotalValues["Плата"]);
        Assert.Equal(2m, документ.Main.TotalValues["Отправлений"]);
    }

    [Fact]
    public async Task Ф103_СмешениеОтправителей_Запрещено()
    {
        var отчёт = new Form103Report(_поставщик);

        var ошибка = await Assert.ThrowsAsync<ReportValidationException>(
            () => отчёт.GenerateF103(new long[] { 1L, 3L }, ReportDataSeeder.SenderId)).ConfigureAwait(false);

        Assert.Equal(Form103Report.MixedSendersError, ошибка.Message);
    }

    [Fact]
    public async Task Ф103_ПустаяВыборка_Ошибка()
    {
        var отчёт = new Form103Report(_поставщик);

        await Assert.ThrowsAsync<ReportValidationException>(
            () => отчёт.GenerateF103(Array.Empty<long>(), ReportDataSeeder.SenderId)).ConfigureAwait(false);
    }

    [Fact]
    public async Task Ф107_ДваЭкземпляра_ИтогиСходятся()
    {
        var отчёт = new InventoryF107Report(_поставщик);

        var документ = await отчёт.GenerateF107(1).ConfigureAwait(false);

        Assert.Equal(2, документ.Blocks.Count);
        Assert.Equal(InventoryF107Report.SenderCopyCaption, документ.Blocks[0].Caption);
        Assert.Equal(InventoryF107Report.PostCopyCaption, документ.Blocks[1].Caption);

        foreach (var блок in документ.Blocks)
        {
            Assert.Equal(2, блок.Rows.Count);
            Assert.Equal(350.50m, блок.TotalValues["Итого"]);
        }

        // Оба экземпляра печатаются на одном листе.
        Assert.Equal(210m, документ.PageWidthMm);
        Assert.Equal(297m, документ.PageHeightMm);
    }

    [Fact]
    public async Task Ф107_ОписьБезПозиций_НеФормируется()
    {
        await using var команда = _база.Connection.CreateCommand();
        команда.CommandText = "INSERT INTO inventories (id, name, counterparty_id) VALUES (99, 'Пустая опись', 3);";
        await команда.ExecuteNonQueryAsync().ConfigureAwait(false);

        var отчёт = new InventoryF107Report(_поставщик);

        var ошибка = await Assert.ThrowsAsync<ReportValidationException>(
            () => отчёт.GenerateF107(99)).ConfigureAwait(false);

        Assert.Equal(InventoryF107Report.NoItemsError, ошибка.Message);
    }

    [Fact]
    public async Task РасширенныйРеестр_Примечания()
    {
        var отчёт = new ExtendedRegistryReport(_поставщик);
        var фильтр = new ReportFilter(MailIds: new List<long> { 1, 2 });

        var документ = await отчёт.GenerateExtendedRegistry(фильтр).ConfigureAwait(false);

        Assert.Equal(2, документ.Main.Rows.Count);
        Assert.Contains("примечание 1", документ.Main.Rows[0].Cells);
    }

    [Fact]
    public async Task ПростойРеестр_ОднаСтраницаДо35Писем()
    {
        var отчёт = new SimpleRegistryReport(_поставщик);

        var документ = await отчёт.GenerateSimpleRegistry(new[] { 1L, 2L }).ConfigureAwait(false);

        Assert.Equal(2, документ.Main.Rows.Count);
        Assert.True(документ.Main.Rows.Count <= SimpleRegistryReport.RowsPerPage);
        Assert.Equal(4, документ.Main.Columns.Count);
    }

    [Fact]
    public async Task ЖурналВходящих_Структура()
    {
        var отчёт = new IncomingJournalReport(_поставщик);

        var документ = await отчёт.BuildIncomingJournal(
            DateTime.UtcNow.Date.AddDays(-1),
            DateTime.UtcNow.Date.AddDays(1)).ConfigureAwait(false);

        Assert.Equal(6, документ.Main.Columns.Count);
        Assert.Equal("Журнал входящей корреспонденции", документ.Title);
    }

    [Fact]
    public async Task Реестр1С_СтруктураКолонок()
    {
        await using var команда = _база.Connection.CreateCommand();
        команда.CommandText = "UPDATE mails SET incoming_number = 'вх-1' WHERE id = 1;";
        await команда.ExecuteNonQueryAsync().ConfigureAwait(false);

        var отчёт = new OneCIncomingReport(_поставщик);

        var документ = await отчёт.BuildOneCCompatibleRegistry(DateTime.UtcNow.Date).ConfigureAwait(false);

        Assert.Single(документ.Main.Rows);
        Assert.Equal("вх-1", документ.Main.Rows[0].Cells[0]);
        Assert.Equal("РегНомер", документ.Main.Columns[0].Name);
    }

    [Fact]
    public async Task ДополнительныеФормы_5и6()
    {
        var отчёт = new AuxiliaryPostReports(_поставщик);

        var форма5 = await отчёт.GenerateAuxForm(5, new[] { 1L }).ConfigureAwait(false);
        var форма6 = await отчёт.GenerateAuxForm(6, new[] { 1L }).ConfigureAwait(false);

        Assert.Contains("ФОРМА 5", форма5.Main.HeaderLines);
        Assert.Contains("ФОРМА 6", форма6.Main.HeaderLines);
        Assert.Equal(65.00m, форма5.Main.TotalValues["Плата"]);
    }

    [Fact]
    public async Task ДополнительныеФормы_Номер7_Отклонение()
    {
        var отчёт = new AuxiliaryPostReports(_поставщик);

        await Assert.ThrowsAsync<ReportValidationException>(
            () => отчёт.GenerateAuxForm(7, new long[] { 1L })).ConfigureAwait(false);
    }

    [Fact]
    public async Task СтатистикаИГруппы_СоставИПустаяГруппа()
    {
        var отчёт = new StatsAndGroupReports(_поставщик);

        var состав = await отчёт.GenerateGroupReport(1).ConfigureAwait(false);

        Assert.Equal(4, состав.Main.Rows.Count);
        Assert.Equal(4m, состав.Main.TotalValues["Контрагентов"]);

        // Пустая группа: отчет с уведомлением «Нет данных».
        var пустая = await отчёт.GenerateGroupReport(777).ConfigureAwait(false);
        Assert.Empty(пустая.Main.Rows);
        Assert.Equal(StatsAndGroupReports.NoDataNotice, пустая.Main.Totals[0]);
    }

    [Fact]
    public async Task СтатистикаРассылок()
    {
        var отчёт = new StatsAndGroupReports(_поставщик);

        var документ = await отчёт.GenerateMailingStats(
            DateTime.UtcNow.Date.AddDays(-1),
            DateTime.UtcNow.Date.AddDays(1)).ConfigureAwait(false);

        Assert.Single(документ.Main.Rows);
        Assert.Equal(3m, документ.Main.TotalValues["Конвертов"]);
    }

    [Fact]
    public void Сортировка_ПоАлфавиту_АбрамовВышеБорисова()
    {
        var сортировщик = new RegistrySorter();

        var абрамов = new Mail { Id = 2, CounterpartyId = 11, DateCreated = DateTime.UtcNow };
        var борисов = new Mail { Id = 1, CounterpartyId = 12, DateCreated = DateTime.UtcNow.AddMinutes(-5) };

        var список = new[] { борисов, абрамов };
        var имена = new Dictionary<long, string> { [11] = "Абрамов А.А.", [12] = "Борисов Б.Б." };

        var отсортировано = сортировщик
            .SortMails(список, RegistrySortMode.ByRecipientName, идентификатор => имена[идентификатор])
            .ToList();

        Assert.Equal(2, отсортировано[0].Id); // Абрамов выше независимо от даты.
        Assert.Equal(1, отсортировано[1].Id);
    }

    [Fact]
    public void Сортировка_ПоНомеру_Естественная()
    {
        var сортировщик = new RegistrySorter();

        var письма = new[]
        {
            new Mail { Id = 1, OutgoingNumber = "№12" },
            new Mail { Id = 2, OutgoingNumber = "№9" },
            new Mail { Id = 3, OutgoingNumber = "№15" },
        };

        var отсортировано = сортировщик.SortMails(письма, RegistrySortMode.ByOutgoingNumber).ToList();

        Assert.Equal(new long[] { 2, 1, 3 }, отсортировано.Select(письмо => письмо.Id));
    }

    [Fact]
    public void Сортировка_ПоИндексу()
    {
        var сортировщик = new RegistrySorter();

        var адрес1 = Address.Create("190000", new[] { "", "", "", "", "", "" }).Value!;
        var адрес2 = Address.Create("100000", new[] { "", "", "", "", "", "" }).Value!;

        var письма = new[]
        {
            new Mail { Id = 1, RecipientAddress = адрес1 },
            new Mail { Id = 2, RecipientAddress = адрес2 },
        };

        var отсортировано = сортировщик.SortMails(письма, RegistrySortMode.ByDestinationZip).ToList();

        Assert.Equal(2, отсортировано[0].Id);
    }

    [Fact]
    public void НомераДокументов_ЧерезЗапятую()
    {
        var агрегатор = new DocumentNumbersAggregator();

        var строка = агрегатор.AggregateDocNumbers(new[] { "№12", "№15", "№18" });

        Assert.Equal("№12, №15, №18", строка);
        Assert.False(агрегатор.Truncated);
    }

    [Fact]
    public void НомераДокументов_КонтрольДлиныЯчейки()
    {
        var агрегатор = new DocumentNumbersAggregator();

        var строка = агрегатор.AggregateDocNumbers(
            Enumerable.Range(1, 100).Select(i => "№" + i),
            maxCellLength: 60);

        Assert.True(агрегатор.Truncated);
        Assert.True(строка.Length <= 60);
        Assert.EndsWith("…", строка);
    }

    [Fact]
    public void ИсторияПечати_НеизменяемыйСнимок()
    {
        var репозиторий = new СнимковыйРепозиторий();
        var трекер = new HistoryTracker(репозиторий);

        var момент = DateTime.UtcNow;
        var идентификатор = трекер.RecordPrintEvent(new PrintHistoryRecord(
            момент,
            MailId: 1,
            RecipientAddressSnapshot: "100000, г. Москва, ул. Тестовая, д. 10; Иванов Иван Иванович",
            SenderAddressSnapshot: "101000, г. Москва, ул. Первая, д. 1; ООО Отправитель",
            CopyCount: 2,
            TemplateDescription: "Конверт C5 (2012)")).GetAwaiter().GetResult();

        Assert.Equal(1, идентификатор);

        // Последующее изменение адреса контрагента не меняет историческую запись.
        var снимок = репозиторий.GetByMailAsync(1).GetAwaiter().GetResult().Single();

        Assert.Equal("100000, г. Москва, ул. Тестовая, д. 10; Иванов Иван Иванович", снимок.RecipientAddressSnapshot);
        Assert.Equal("101000, г. Москва, ул. Первая, д. 1; ООО Отправитель", снимок.SenderAddressSnapshot);
        Assert.False(репозиторий.Изменён);
    }

    [Fact]
    public async Task ТихаяПечать_БезGui_КодОшибкиВызывающему()
    {
        var канал = new ФейковыйКанал(ошибка: null);
        var служба = new SilentPrintService(_поставщик, канал);

        var результат = await служба.PrintRegistrySilently(
            new[] { 1L, 2L },
            "Реестр ф.103",
            "HP LaserJet Pro M402dn").ConfigureAwait(false);

        Assert.True(результат.Success);
        Assert.Equal(0, (int)результат.ErrorCode);
        Assert.NotNull(канал.ПоследнийДокумент);
        Assert.Equal(1, канал.Вызовов);
    }

    [Fact]
    public async Task ТихаяПечать_ОшибкаSpooler_КодВозврата()
    {
        var канал = new ФейковыйКанал(ошибка: "Принтер недоступен или отключен.");
        var служба = new SilentPrintService(_поставщик, канал);

        var результат = await служба.PrintRegistrySilently(
            new[] { 1L }, "Реестр", "Нет такого принтера").ConfigureAwait(false);

        Assert.False(результат.Success);
        Assert.Equal(SilentPrintErrorCode.SpoolerFailure, результат.ErrorCode);
        Assert.Contains("Принтер", результат.Message);
    }

    [Fact]
    public async Task ТихаяПечать_СмешениеОтправителей_КодОшибки()
    {
        var служба = new SilentPrintService(_поставщик, new ФейковыйКанал(null));

        var результат = await служба.PrintRegistrySilently(
            new[] { 1L, 3L }, "Реестр", "Принтер").ConfigureAwait(false);

        Assert.Equal(SilentPrintErrorCode.MixedSenders, результат.ErrorCode);
    }

    [Fact]
    public async Task ТихаяПечать_ПустаяВыборка_КодОшибки()
    {
        var служба = new SilentPrintService(_поставщик, new ФейковыйКанал(null));

        var результат = await служба.PrintRegistrySilently(
            Array.Empty<long>(), "Реестр", "Принтер").ConfigureAwait(false);

        Assert.Equal(SilentPrintErrorCode.EmptySelection, результат.ErrorCode);
    }

    private static decimal Число(object? значение) => значение switch
    {
        decimal число => число,
        long число => число,
        _ => 0m,
    };

    /// <summary>Репозиторий истории без операций изменения.</summary>
    private sealed class СнимковыйРепозиторий : IPrintHistoryRepository
    {
        private readonly List<PrintHistory> _записи = new();

        public bool Изменён { get; private set; }

        public Task<long> AddAsync(PrintHistory history, CancellationToken cancellationToken = default)
        {
            _записи.Add(history);

            return Task.FromResult((long)_записи.Count);
        }

        public Task<PrintHistory?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PrintHistory?>(_записи[(int)id - 1]);

        public Task<IReadOnlyList<PrintHistory>> GetByMailAsync(long mailId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrintHistory>>(_записи.Where(запись => запись.MailId == mailId).ToList());

        public Task<Page<PrintHistory>> GetPagedAsync(PageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>Единственная мутация хранилища — имитация изменения карточки
        /// контрагента в БД; снимки в print_history от этого не зависят.</summary>
        public void ИзменитьСнимки() => Изменён = true;
    }

    /// <summary>Канал спулера, записывающий вызовы.</summary>
    private sealed class ФейковыйКанал : ISilentPrintChannel
    {
        private readonly string? _ошибка;

        public ФейковыйКанал(string? ошибка) => _ошибка = ошибка;

        public int Вызовов { get; private set; }

        public ReportDocument? ПоследнийДокумент { get; private set; }

        public string? SendToSpooler(ReportDocument document, string printerName, int copies)
        {
            Вызовов++;
            ПоследнийДокумент = document;

            return _ошибка;
        }
    }
}
