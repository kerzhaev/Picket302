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
/// Проверка инварианта документируемости печати (ТЗ §2.2; §8.6, функция 88):
/// передача данных в подсистему печати не выполняется в обход создания
/// транзакции аудита, а сбой записи аудита отменяет печать.
/// </summary>
public sealed class PrintSessionAuditTests : IDisposable
{
    private readonly string _каталог = Path.Combine(
        Path.GetTempPath(),
        "picket302-аудит-" + Guid.NewGuid().ToString("N"));

    public PrintSessionAuditTests() => Directory.CreateDirectory(_каталог);

    public void Dispose()
    {
        if (Directory.Exists(_каталог))
        {
            Directory.Delete(_каталог, true);
        }
    }

    [Fact]
    public async Task Печать_СбойЗаписиАудита_ЗаданиеВСпулерНеПередаётся()
    {
        // Каталог аудита заменён файлом: запись в канал аудита невозможна.
        var недоступныйПуть = Path.Combine(_каталог, "не-каталог");
        await File.WriteAllTextAsync(недоступныйПуть, "файл вместо каталога");

        var спулер = new СпулерЗаглушка();
        var контроллер = СобратьКонтроллер(
            new PrintAuditWriter(недоступныйПуть),
            спулер,
            new РеестрЗаглушка());

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.False(итог.IsSuccess);
        Assert.Equal(PrintJobStatus.Failed, итог.Status);

        // Ключевое требование инварианта §2.2: данные в спулер не передавались.
        Assert.Equal(0, спулер.Вызовов);
    }

    [Fact]
    public async Task Печать_ТранзакцияАудитаСоздаётсяДоПередачиВСпулер()
    {
        var реестр = new РеестрЗаглушка();
        var спулер = new СпулерЗаглушка { СчётчикЗаписей = () => реестр.Записи.Count };
        var контроллер = СобратьКонтроллер(new PrintAuditWriter(_каталог), спулер, реестр);

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.True(итог.IsSuccess);
        Assert.Equal(1, спулер.Вызовов);

        // Запись реестра создана до обращения к спулеру — транзакция аудита
        // предшествует передаче данных в подсистему печати (ТЗ §2.2).
        Assert.Equal(1, спулер.ЗаписейРеестраПриВызове);
        Assert.Single(реестр.Записи);
        Assert.Equal(PrintJobStatus.Success, реестр.Записи[0].Status);
    }

    [Fact]
    public async Task Печать_FixA4СРазлиновкой_ПредупрежденияБезДублей()
    {
        var контроллер = СобратьКонтроллер(
            new PrintAuditWriter(_каталог),
            new СпулерЗаглушка(),
            new РеестрЗаглушка());

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос() with { FixA4 = true, Preprinted = true },
            CancellationToken.None);

        Assert.True(итог.IsSuccess, итог.ErrorMessage);

        // Предупреждение FixA4 фиксируется один раз, а не дважды (F-A9):
        // прежде StripBackground возвращал накопленный список, и AddRange
        // дублировал предупреждение принудительной печати A4.
        Assert.Equal(1, итог.Warnings.Count(текст => текст == FixA4Mode.FixA4Warning));
    }

    [Fact]
    public async Task Печать_ПрерваннаяСессия_ВРеестреЕстьЗаписьСоСтатусомAborted()
    {
        var реестр = new РеестрЗаглушка();
        var спулер = new СпулерЗаглушка { Прервать = true };
        var контроллер = СобратьКонтроллер(new PrintAuditWriter(_каталог), спулер, реестр);

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.False(итог.IsSuccess);
        Assert.Equal(PrintJobStatus.Aborted, итог.Status);

        // Итог прерванной сессии зафиксирован в реестре (ТЗ §8.6, функция 88).
        Assert.Contains(реестр.Записи, запись => запись.Status == PrintJobStatus.Aborted);
    }

    [Fact]
    public async Task Печать_УспешнаяСессия_КаналАудитаСодержитЗаписьФормата65()
    {
        var контроллер = СобратьКонтроллер(
            new PrintAuditWriter(_каталог),
            new СпулерЗаглушка(),
            new РеестрЗаглушка());

        await контроллер.PrintSelectionDirectAsync(new[] { 1L }, Запрос(), CancellationToken.None);

        var текст = await File.ReadAllTextAsync(Path.Combine(_каталог, PrintAuditWriter.FileName));

        Assert.StartsWith("------------- Вывод на принтер:", текст);
        Assert.Contains("------------- Параметры принтера: paper=", текст);
        Assert.Contains("Шаблон: ", текст);
        Assert.Contains("Имя шрифта: ", текст);
        Assert.Contains("Смещение: X=", текст);
        Assert.Contains("Объектов: ", текст);
        Assert.Contains("Статус: УСПЕШНО, Затрачено времени:", текст);
        Assert.EndsWith("-------------\n", текст);
    }

    [Fact]
    public async Task Печать_БлокировкаПоРазмеру_ЗаданиеВСпулерНеПередаётся()
    {
        var спулер = new СпулерЗаглушка();
        var реестр = new РеестрЗаглушка();

        // Печатная область принтера меньше бланка более чем на 1 мм.
        var принтеры = new ПринтерЗаглушка();

        var контроллер = СобратьКонтроллер(new PrintAuditWriter(_каталог), спулер, реестр, принтеры);

        var итог = await контроллер.PrintSelectionDirectAsync(
            new[] { 1L },
            Запрос(),
            CancellationToken.None);

        Assert.False(итог.IsSuccess);
        Assert.Equal(PrintJobStatus.BlockedSize, итог.Status);
        Assert.Equal(0, спулер.Вызовов);
        Assert.Single(реестр.Записи);
        Assert.Contains(PaperSizeValidator.BlockedSizeMessage, итог.ErrorMessage ?? string.Empty);
    }

    private sealed class ПринтерЗаглушка : PrinterManager
    {
        public override PrinterDeviceInfo GetDeviceInfo(
            string printerName,
            short paperSize = DevModeConstants.DMPAPER_USER) =>
            new()
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

    private static PrintRequest Запрос() => new()
    {
        TemplateId = 1,
        PrinterName = "HP LaserJet Pro M402dn",
        PrintMode = PrintModes.BatchDirect,
        Copies = 1,
    };

    private static PrintSessionController СобратьКонтроллер(
        PrintAuditWriter аудит,
        IPrintSpooler спулер,
        IPrintJobRepository реестр,
        PrinterManager? принтеры = null) =>
        new(
            new ШаблоныЗаглушка(),
            new ПоляЗаглушка(),
            реестр,
            new ДанныеЗаглушка(),
            аудит,
            спулер,
            new EnvelopeRenderer(new ИзмерительЗаглушка()),
            new PaperSizeValidator(),
            printerProfiles: null,
            printers: принтеры);

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

    private sealed class РеестрЗаглушка : IPrintJobRepository
    {
        private readonly List<PrintJob> _записи = new();

        public List<PrintJob> Записи => _записи;

        public Task<long> AddAsync(PrintJob printJob, CancellationToken cancellationToken = default)
        {
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

        public bool Прервать { get; init; }

        /// <summary>Число записей реестра на момент обращения к спулеру.</summary>
        public int ЗаписейРеестраПриВызове { get; private set; }

        public Func<int>? СчётчикЗаписей { get; init; }

        public Task PrintAsync(
            PrintJobContext context,
            Action<IGraphicsContext> render,
            CancellationToken cancellationToken)
        {
            Вызовов++;
            ЗаписейРеестраПриВызове = СчётчикЗаписей?.Invoke() ?? 0;

            if (Прервать)
            {
                throw new OperationCanceledException(cancellationToken);
            }

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
