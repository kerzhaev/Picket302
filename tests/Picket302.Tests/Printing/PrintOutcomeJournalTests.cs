using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Printing;
using Picket302.Printing.Audit;
using Picket302.Printing.Devices;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;
using Picket302.Printing.Rendering;
using Picket302.Printing.Validation;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Исходы печатной операции в журнале совместного режима: «подготовлено»,
/// «передано локальному спулеру», «ошибка» и «неизвестный исход после
/// обрыва».
/// </summary>
/// <remarks>
/// Ключевое требование: неизвестный сетевой исход не превращается в ложное
/// «не напечатано» и не приводит к автоматической повторной физической
/// печати.
/// </remarks>
public sealed class PrintOutcomeJournalTests : IDisposable
{
    private readonly string _каталог = Path.Combine(
        Path.GetTempPath(),
        "picket302-исходы-" + Guid.NewGuid().ToString("N"));

    public PrintOutcomeJournalTests() => Directory.CreateDirectory(_каталог);

    public void Dispose()
    {
        if (Directory.Exists(_каталог))
        {
            Directory.Delete(_каталог, true);
        }
    }

    [Fact]
    public async Task УспешнаяПечать_ИсходВЖурнале_Spooled()
    {
        var журнал = new ЖурналЗаглушка();
        var спулер = new СпулерЗаглушка();
        var контроллер = СобратьКонтроллер(журнал, спулер, new РеестрЗаглушка());

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.True(итог.IsSuccess, итог.ErrorMessage);
        Assert.Equal(1, спулер.Вызовов);
        Assert.Equal(PrintOutcome.Spooled, Assert.Single(журнал.Исходы));
    }

    [Fact]
    public async Task СбойЗаписиВЖурнал_ПечатьНеПревращаетсяВНеНапечатанную()
    {
        // Журнал общего режима недоступен: запись исхода падает. Конверты к
        // этому моменту уже приняты локальным спулером, поэтому состоявшаяся
        // печать не должна превращаться в «не напечатано» — иначе оператор
        // напечатает конверты повторно.
        var журнал = new ЖурналЗаглушка { ОшибкаЗаписи = true };
        var спулер = new СпулерЗаглушка();
        var контроллер = СобратьКонтроллер(журнал, спулер, new РеестрЗаглушка());

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.True(итог.IsSuccess, итог.ErrorMessage);
        Assert.Null(итог.ErrorMessage);
        Assert.Equal(1, спулер.Вызовов);

        // Оператору сообщается: запись не подтверждена, повторяется только
        // запись, повторная печать не требуется.
        Assert.Contains(
            итог.Warnings,
            текст => текст.Contains("повторная печать не требуется", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ОшибкаСпулера_ИсходВЖурнале_Error()
    {
        var журнал = new ЖурналЗаглушка();
        var спулер = new СпулерЗаглушка { Ошибка = true };
        var контроллер = СобратьКонтроллер(журнал, спулер, new РеестрЗаглушка());

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.False(итог.IsSuccess);
        Assert.Equal(PrintOutcome.Error, Assert.Single(журнал.Исходы));
    }

    [Fact]
    public async Task НеизвестныйИсходЗаписиСессии_ПечатьНеВыполнялась_ИсходUnknown()
    {
        // Ответ сервера на запись сессии потерян: исход записи неизвестен.
        // Задание в спулер не передавалось, поэтому конверты не напечатаны,
        // и повторная печать не выполняется.
        var журнал = new ЖурналЗаглушка();
        var спулер = new СпулерЗаглушка();
        var контроллер = СобратьКонтроллер(
            журнал,
            спулер,
            new РеестрЗаглушка { НеизвестныйИсход = true });

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.False(итог.IsSuccess);
        Assert.Equal(0, спулер.Вызовов);
        Assert.Contains("Печать не выполнялась", итог.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(PrintOutcome.Unknown, Assert.Single(журнал.Исходы));
    }

    [Fact]
    public async Task БлокировкаПоРазмеру_ИсходВЖурнале_Prepared()
    {
        var журнал = new ЖурналЗаглушка();
        var спулер = new СпулерЗаглушка();
        var контроллер = СобратьКонтроллер(
            журнал,
            спулер,
            new РеестрЗаглушка(),
            new ПринтерЗаглушка(маленький: true));

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.False(итог.IsSuccess);
        Assert.Equal(PrintJobStatus.BlockedSize, итог.Status);
        Assert.Equal(0, спулер.Вызовов);
        Assert.Equal(PrintOutcome.Prepared, Assert.Single(журнал.Исходы));
    }

    private static PrintRequest Запрос() => new()
    {
        TemplateId = 1,
        PrinterName = "HP LaserJet Pro M402dn",
        PrintMode = PrintModes.BatchDirect,
        Copies = 1,
    };

    private PrintSessionController СобратьКонтроллер(
        IPrintHistoryRecorder журнал,
        IPrintSpooler спулер,
        IPrintJobRepository реестр,
        PrinterManager? принтеры = null) =>
        new(
            new ШаблоныЗаглушка(),
            new ПоляЗаглушка(),
            реестр,
            new ДанныеЗаглушка(),
            new PrintAuditWriter(_каталог),
            спулер,
            new EnvelopeRenderer(new ИзмерительЗаглушка()),
            new PaperSizeValidator(),
            printerProfiles: null,
            printers: принтеры,
            printHistory: журнал);

    private sealed class ПринтерЗаглушка : PrinterManager
    {
        private readonly bool _маленький;

        public ПринтерЗаглушка(bool маленький = false) => _маленький = маленький;

        public override PrinterDeviceInfo GetDeviceInfo(
            string printerName,
            short paperSize = DevModeConstants.DMPAPER_USER) =>
            _маленький
                ? new()
                {
                    PrinterName = printerName,
                    PaperSizeName = "USER",
                    PaperWidthMm = 100m,
                    PaperHeightMm = 150m,
                    PrintableWidthMm = 90m,
                    PrintableHeightMm = 140m,
                    DmPaperSize = paperSize,
                    AvailablePaperSizes = new[] { "USER" },
                }
                : new()
                {
                    PrinterName = printerName,
                    PaperSizeName = "C5",
                    PaperWidthMm = 162m,
                    PaperHeightMm = 229m,
                    PrintableWidthMm = 150m,
                    PrintableHeightMm = 229m,
                    DmPaperSize = paperSize,
                    AvailablePaperSizes = new[] { "C5", "A4" },
                };
    }

    private sealed class ШаблоныЗаглушка : IPrintTemplateRepository
    {
        public Task<long> AddAsync(PrintTemplate template, CancellationToken cancellationToken = default) =>
            Task.FromResult(1L);

        public Task<PrintTemplate?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PrintTemplate?>(new PrintTemplate
            {
                Id = id,
                Name = "Envelope_C5_Custom",
                FilePath = "Envelope_C5_Custom.lbk",
                Format = "C5",
                WidthMm = 229m,
                HeightMm = 162m,
                Orientation = TemplateOrientation.Landscape,
                Country = "RUS",
            });

        public Task<IReadOnlyList<PrintTemplate>> GetAllAsync(
            string? kind = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrintTemplate>>(Array.Empty<PrintTemplate>());

        public Task<int> UpdateAsync(PrintTemplate template, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class ПоляЗаглушка : IBlankFieldRepository
    {
        public Task<long> AddAsync(BlankField field, CancellationToken cancellationToken = default) =>
            Task.FromResult(1L);

        public Task<IReadOnlyList<BlankField>> GetByTemplateAsync(
            long templateId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BlankField>>(new List<BlankField>
            {
                new() { Name = "Кому 1", Kind = BlankFieldKind.Text, XMm = 10, YMm = 20, LengthMm = 40, HeightMm = 10 },
            });

        public Task<int> UpdateAsync(BlankField field, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class ДанныеЗаглушка : IPrintDataSource
    {
        public Task<IReadOnlyList<PrintableItem>> GetItemsAsync(
            IReadOnlyList<long> mailIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrintableItem>>(new List<PrintableItem>
            {
                new()
                {
                    Id = 1,
                    Values = new Dictionary<string, string>
                    {
                        ["Кому 1"] = "Иванов Иван Иванович",
                    },
                },
            });
    }

    /// <summary>Реестр сессий печати: может сообщить о неизвестном исходе.</summary>
    private sealed class РеестрЗаглушка : IPrintJobRepository
    {
        private readonly List<PrintJob> _записи = new();

        public List<PrintJob> Записи => _записи;

        /// <summary>Исход записи сессии неизвестен (ответ сервера потерян).</summary>
        public bool НеизвестныйИсход { get; init; }

        public Task<long> AddAsync(PrintJob printJob, CancellationToken cancellationToken = default)
        {
            if (НеизвестныйИсход)
            {
                throw new OutcomeUnknownException("Ответ сервера потерян.");
            }

            _записи.Add(printJob);

            return Task.FromResult((long)_записи.Count);
        }

        public Task<PrintJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PrintJob?>(_записи.FirstOrDefault(запись => запись.Id == id));

        public Task<Page<PrintJob>> GetPagedAsync(
            PageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new Page<PrintJob>(_записи, 1, _записи.Count, _записи.Count));
    }

    private sealed class СпулерЗаглушка : IPrintSpooler
    {
        public int Вызовов { get; private set; }

        /// <summary>Спулер отвергает задание.</summary>
        public bool Ошибка { get; init; }

        public Task PrintAsync(
            PrintJobContext context,
            Action<IGraphicsContext> render,
            CancellationToken cancellationToken)
        {
            Вызовов++;

            if (Ошибка)
            {
                throw new InvalidOperationException("Принтер недоступен.");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Журнал печати: фиксирует исход и умеет его потерять.</summary>
    private sealed class ЖурналЗаглушка : IPrintHistoryRecorder
    {
        private readonly List<PrintOutcome> _исходы = new();

        public IReadOnlyList<PrintOutcome> Исходы => _исходы;

        /// <summary>Запись исхода не удаётся (обрыв связи с общей базой).</summary>
        public bool ОшибкаЗаписи { get; init; }

        public Task RecordAsync(
            IReadOnlyList<long> mailIds,
            long? printJobId,
            string templateDescription,
            CancellationToken cancellationToken = default,
            PrintOutcome outcome = PrintOutcome.Spooled)
        {
            if (ОшибкаЗаписи)
            {
                throw new InvalidOperationException("Общая база недоступна.");
            }

            _исходы.Add(outcome);

            return Task.CompletedTask;
        }
    }

    private sealed class ИзмерительЗаглушка : ITextMeasurer
    {
        public double MeasureWidth(string text, string fontName, decimal fontSizePt) =>
            text.Length * (double)fontSizePt * 0.5;

        public double MeasureLineHeight(string fontName, decimal fontSizePt) => (double)fontSizePt * 1.2;
    }
}
