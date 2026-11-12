using System.Diagnostics;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.Postal;
using Picket302.Domain.Repositories;
using Picket302.Printing.Audit;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Picket302.Printing.Elements;
using Picket302.Printing.Metrics;
using Picket302.Printing.Rendering;
using Picket302.Printing.Validation;

namespace Picket302.Printing;

/// <summary>
/// Управление печатными сессиями: три режима F5 / F6 / F7 (ТЗ §8.6, функция 79:
/// <c>Printing.PrintSessionController</c>).
/// </summary>
/// <remarks>
/// <para>Конвейер печати выполняется строго по §6.1: данные отправлений и
/// бланк проходят препроцессинг (автоподбор кегля, формирование кодового
/// штампа), проверку геометрии, генерацию изображения, передачу в спулер и
/// запись аудита.</para>
/// <para>Печать невозможна в обход аудита: транзакция аудита открывается до
/// передачи задания в спулер, а её сбой отменяет печать (ТЗ §2.2, §8.6,
/// функция 88).</para>
/// </remarks>
public sealed class PrintSessionController
{
    private readonly IPrintTemplateRepository _шаблоны;
    private readonly IBlankFieldRepository _поля;
    private readonly IPrintJobRepository _журнал;
    private readonly IPrintDataSource _данные;
    private readonly PrintAuditWriter _аудит;
    private readonly IPrintSpooler _спулер;
    private readonly PaperSizeValidator _валидатор;
    private readonly EnvelopeRenderer _рендерер;
    private readonly PostIndexRenderer? _кодовыйШтамп;
    private readonly QrCodeRenderer? _qr;
    private readonly LogoRenderer? _логотип;
    private readonly PrinterManager? _принтеры;
    private readonly IPrinterProfileRepository? _профили;
    private readonly EnvelopeFormatCatalog? _форматы;
    private readonly IPrintHistoryRecorder? _историяПечати;
    private readonly ILicenseGate? _ворота;

    /// <summary>
    /// Создаёт контроллер печатных сессий.
    /// </summary>
    /// <param name="templates">Хранилище шаблонов.</param>
    /// <param name="fields">Хранилище полей бланка.</param>
    /// <param name="printJobs">Реестр сессий печати.</param>
    /// <param name="dataSource">Источник данных отправлений.</param>
    /// <param name="audit">Журналист аудита печати.</param>
    /// <param name="spooler">Спулер печати.</param>
    /// <param name="renderer">Генератор изображения бланка.</param>
    /// <param name="validator">Валидатор геометрии.</param>
    /// <param name="postIndex">Отрисовщик кодового штампа.</param>
    /// <param name="qrCode">Отрисовщик QR-кода.</param>
    /// <param name="logo">Отрисовщик логотипа.</param>
    /// <param name="printers">Менеджер принтеров (реальные метрики DEVMODE).</param>
    /// <param name="printerProfiles">Хранилище калибровочных профилей принтеров.</param>
    /// <param name="formats">Справочник форматов конвертов.</param>
    /// <param name="licenseGate">Ворота лицензии: ограничивают печать партий в режиме trial (ТЗ §8.10, функция 119).</param>
    public PrintSessionController(
        IPrintTemplateRepository templates,
        IBlankFieldRepository fields,
        IPrintJobRepository printJobs,
        IPrintDataSource dataSource,
        PrintAuditWriter audit,
        IPrintSpooler spooler,
        EnvelopeRenderer renderer,
        PaperSizeValidator validator,
        PostIndexRenderer? postIndex = null,
        QrCodeRenderer? qrCode = null,
        LogoRenderer? logo = null,
        PrinterManager? printers = null,
        IPrinterProfileRepository? printerProfiles = null,
        EnvelopeFormatCatalog? formats = null,
        IPrintHistoryRecorder? printHistory = null,
        ILicenseGate? licenseGate = null)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(printJobs);
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(spooler);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(validator);

        _шаблоны = templates;
        _поля = fields;
        _журнал = printJobs;
        _данные = dataSource;
        _аудит = audit;
        _спулер = spooler;
        _рендерер = renderer;
        _валидатор = validator;
        _кодовыйШтамп = postIndex;
        _qr = qrCode;
        _логотип = logo;
        _принтеры = printers;
        _профили = printerProfiles;
        _форматы = formats;
        _историяПечати = printHistory;
        _ворота = licenseGate;
    }

    /// <summary>Текст ошибки при пустом выделении (ТЗ §8.6, функция 79).</summary>
    public const string EmptySelectionMessage = "Не выбрано ни одной записи";

    /// <summary>
    /// Печать одного выбранного конверта (F5).
    /// </summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="request">Параметры печати.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="progress">Сообщения о ходе печати.</param>
    /// <returns>Итог печатной сессии.</returns>
    public Task<PrintResult> PrintSelectedAsync(
        long mailId,
        PrintRequest request,
        CancellationToken cancellationToken = default,
        IProgress<PrintProgress>? progress = null) =>
        ВыполнитьAsync(
            request with { MailIds = new[] { mailId }, PrintMode = PrintModes.Selected },
            cancellationToken,
            progress);

    /// <summary>
    /// Печать выделенных записей с интерактивным предпросмотром (F6).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="request">Параметры печати.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="progress">Сообщения о ходе печати.</param>
    /// <returns>Итог печатной сессии.</returns>
    public Task<PrintResult> PrintSelectionPreviewedAsync(
        IReadOnlyList<long> mailIds,
        PrintRequest request,
        CancellationToken cancellationToken = default,
        IProgress<PrintProgress>? progress = null) =>
        ВыполнитьAsync(
            request with { MailIds = mailIds, PrintMode = PrintModes.BatchPreview, ShowPreview = true },
            cancellationToken,
            progress);

    /// <summary>
    /// Прямая печать выделенных записей без предпросмотра (F7).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="request">Параметры печати.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="progress">Сообщения о ходе печати.</param>
    /// <returns>Итог печатной сессии.</returns>
    public Task<PrintResult> PrintSelectionDirectAsync(
        IReadOnlyList<long> mailIds,
        PrintRequest request,
        CancellationToken cancellationToken = default,
        IProgress<PrintProgress>? progress = null) =>
        ВыполнитьAsync(
            request with { MailIds = mailIds, PrintMode = PrintModes.BatchDirect, ShowPreview = false },
            cancellationToken,
            progress);

    /// <summary>
    /// Пробная печать одного экземпляра (ТЗ §8.1, функция 143): записи в
    /// <c>mails</c> не создаются, режим записи — <c>test</c>.
    /// </summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="request">Параметры печати.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="progress">Сообщения о ходе печати.</param>
    /// <returns>Итог печатной сессии.</returns>
    public Task<PrintResult> PrintTestAsync(
        long mailId,
        PrintRequest request,
        CancellationToken cancellationToken = default,
        IProgress<PrintProgress>? progress = null) =>
        ВыполнитьAsync(
            request with
            {
                MailIds = new[] { mailId },
                PrintMode = PrintModes.Test,
                IsTest = true,
                Copies = 1,
            },
            cancellationToken,
            progress);

    private async Task<PrintResult> ВыполнитьAsync(
        PrintRequest request,
        CancellationToken cancellationToken,
        IProgress<PrintProgress>? progress)
    {
        if (request.MailIds.Count == 0)
        {
            return new PrintResult
            {
                IsSuccess = false,
                Status = PrintJobStatus.Failed,
                ErrorMessage = EmptySelectionMessage,
            };
        }

        // Печать партий в режиме trial ограничивается лимитами лицензии
        // (ТЗ §8.10, функция 119): при истёкшем сроке или исчерпанном лимите
        // объёма партия не печатается, оператор получает понятное сообщение
        // с числом оставшихся дней. Одиночная печать (F5) и пробная печать
        // остаются доступными.
        if (request.PrintMode is PrintModes.BatchPreview or PrintModes.BatchDirect)
        {
            var разрешение = _ворота?.Проверить(LicenseOperation.ПечатьПартий);

            if (разрешение is { Разрешено: false })
            {
                return new PrintResult
                {
                    IsSuccess = false,
                    Status = PrintJobStatus.Failed,
                    ErrorMessage = разрешение.Сообщение,
                };
            }
        }

        progress?.Report(new PrintProgress(
            PrintProgressPhase.Preparing,
            0,
            request.MailIds.Count,
            PrintProgress.PreparingMessage));

        var секундомер = Stopwatch.StartNew();
        var предупреждения = new List<string>();

        try
        {
            var шаблон = request.TemplateId is null
                ? null
                : await _шаблоны.GetByIdAsync(request.TemplateId.Value, cancellationToken).ConfigureAwait(false);

            if (шаблон is null)
            {
                return new PrintResult
                {
                    IsSuccess = false,
                    Status = PrintJobStatus.Failed,
                    ErrorMessage = "Шаблон не выбран.",
                };
            }

            var поля = await _поля
                .GetByTemplateAsync(шаблон.Id, cancellationToken)
                .ConfigureAwait(false);

            var элементы = await _данные
                .GetItemsAsync(request.MailIds, cancellationToken)
                .ConfigureAwait(false);

            var имяШаблона = string.IsNullOrWhiteSpace(шаблон.FilePath)
                ? шаблон.Name
                : Path.GetFileName(шаблон.FilePath);

            var метрики = new BlankMetrics
            {
                TemplateName = имяШаблона,
                WidthMm = шаблон.WidthMm,
                HeightMm = шаблон.HeightMm,
                Orientation = шаблон.Orientation,
                PaperSizeName = шаблон.Format,
                PageCount = поля.Count > 0 ? поля.Max(поле => поле.Page) : 1,
            };

            var контекст = new PrintJobContext
            {
                TemplateId = шаблон.Id,
                TemplateName = имяШаблона,
                Blank = метрики,
                Fields = поля,
                Items = элементы,
                Printer = ПолучитьСведенияОПринтере(request, шаблон),
                Orientation = шаблон.Orientation,
                PaperSizeName = шаблон.Format,
                PaperWidthMm = шаблон.WidthMm,
                PaperHeightMm = шаблон.HeightMm,
                DmPaperSize = PrinterDeviceInfo.PaperSizeCodeByFormat(шаблон.Format),
                FontName = "Tahoma",
                FontSizePt = 10m,
                IndexFontName = PostIndexSpec.DefaultFontName,
                IndexLineWidthMm = PostIndexSpec.DefaultLineWidthMm,
                OffsetXMm = request.OffsetXMm,
                OffsetYMm = request.OffsetYMm,
                Duplex = request.Duplex,
                Parity = request.Parity,
                PrintMode = request.IsTest ? PrintModes.Test : request.PrintMode,
                Copies = request.Copies,
                FixA4 = request.FixA4,
                Preprinted = request.Preprinted,
                WithLogo = request.WithLogo,
                // Страница задания = «отправление × страница шаблона» (ТЗ §8.6,
                // функции 79–80): каждый элемент партии выводится на своём
                // физическом листе; общее число страниц задания — произведение
                // числа отправлений на число страниц шаблона.
                TotalPages = Math.Max(метрики.PageCount, 1) * Math.Max(элементы.Count, 1),
            };

            // Территория отправления и параметры кодового штампа определяются
            // справочником форматов (ТЗ §6.3; ГОСТ Р 51506-99): внутреннее
            // отправление («Вн») — шестизначный индекс, международное («Мн») —
            // трёхзначный индекс «555».
            контекст = ПрименитьТерриторию(контекст, шаблон);

            // Порядок адресных строк формата (ТЗ §8.4, функция 33):
            // «от частного к общему» для форматов с AddressOrderSmallToLarge.
            элементы = ApplyAddressOrder(элементы, контекст.AddressOrderSmallToLarge);
            контекст = контекст with { Items = элементы };
            // Калибровочные смещения принтера для пары «принтер + формат»
            // (ТЗ §8.6, функция 45): применяются, если задание не содержит
            // явных смещений.
            if (request.OffsetXMm == 0m && request.OffsetYMm == 0m)
            {
                var смещение = await ПолучитьСмещениеПринтера(request.PrinterName, шаблон.Format, cancellationToken)
                    .ConfigureAwait(false);

                if (смещение is not null)
                {
                    контекст = контекст with
                    {
                        OffsetXMm = смещение.Value.X,
                        OffsetYMm = смещение.Value.Y,
                    };
                }
            }

            // Двусторонняя печать: при отсутствии поддержки у принтера
            // применяется двухпроходный режим с ручным переворотом стопки
            // (ТЗ §8.6, функция 86).
            var duplex = new DuplexController().ConfigureDuplex(контекст, request.Duplex);
            контекст = контекст with
            {
                Duplex = duplex.ЭффективныйРежим,
                Printer = контекст.Printer with { DmDuplex = duplex.DevModeDuplex },
            };

            if (duplex.ТребуетсяИнструкция && !string.IsNullOrWhiteSpace(duplex.Инструкция))
            {
                предупреждения.Add(duplex.Инструкция);
            }

            if (request.FixA4)
            {
                контекст = new FixA4Mode().ApplyFixA4(контекст);

                // D-S17-01: сведения принтера запрашивались под код формата
                // бланка; после перевода задания в A4 они обязаны
                // соответствовать A4, иначе повторная валидация блока
                // «Зафиксировать A4» зацикливается на драйверах без
                // конвертных форматов.
                контекст = контекст with
                {
                    Printer = FixA4Mode.ОбновитьПринтерПодФорматA4(контекст.Printer, контекст.Orientation),
                };

                предупреждения.AddRange(контекст.Warnings);
            }

            if (request.Preprinted)
            {
                контекст = new PreprintedMode().StripBackground(контекст);

                // Режимы накапливают предупреждения в контексте: повторный
                // AddRange дублировал предупреждения FixA4 (F-A9) —
                // добавляются только новые.
                предупреждения.AddRange(контекст.Warnings.Except(предупреждения));
            }

            // Проверка параметров кодового штампа: отсутствие шрифта ZIPcode
            // блокирует печать конверта (ТЗ §6.3).
            if (_кодовыйШтамп is not null)
            {
                var проверка = _кодовыйШтамп.Validate(new PostIndexSpec
                {
                    LineWidthMm = контекст.IndexLineWidthMm,
                    FontName = контекст.IndexFontName,
                });

                предупреждения.AddRange(проверка.Warnings);

                if (проверка.IsBlocked)
                {
                    return await ЗаписатьИтогAsync(
                        контекст,
                        request,
                        PrintJobStatus.BlockedSize,
                        проверка.BlockReason,
                        предупреждения,
                        секундомер,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            // Проверка метрик: превышение размеров блокирует задание.
            var проверкаМетрик = _валидатор.Validate(метрики, контекст.Printer);
            предупреждения.AddRange(проверкаМетрик.Warnings);

            if (проверкаМетрик.IsBlocked)
            {
                return await ЗаписатьИтогAsync(
                    контекст,
                    request,
                    PrintJobStatus.BlockedSize,
                    проверкаМетрик.ErrorMessage,
                    предупреждения,
                    секундомер,
                    cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new PrintProgress(
                PrintProgressPhase.Printing,
                0,
                элементы.Count,
                PrintProgress.PrintingMessage));

            // Транзакция аудита открывается ДО передачи задания в спулер
            // (ТЗ §2.2): недоступность канала аудита или сбой записи в реестр
            // сессий печати отменяют печать — данные в подсистему печати не
            // передаются.
            _аудит.УбедитьсяВДоступностиКанала();

            // Прогрев кэша QR-кодов: ранее сформированные коды берутся из
            // таблицы qr_cache по SHA-256 ключу строки данных (ТЗ §6.4).
            if (_qr is not null)
            {
                await _qr
                    .ПрогретьКэшAsync(_рендерер.ПолучитьПолезныеНагрузкиQr(контекст), cancellationToken)
                    .ConfigureAwait(false);
            }

            var плановаяЗапись = СобратьЗапись(контекст, PrintJobStatus.Success, секундомер);
            var плановыйИдентификатор = await _журнал
                .AddAsync(СобратьЗаписьРеестра(контекст, PrintJobStatus.Success, null, DateTime.UtcNow), cancellationToken)
                .ConfigureAwait(false);

            var статус = PrintJobStatus.Success;
            string? ошибка = null;
            List<string> предупрежденияРендеринга = new();

            try
            {
                if (duplex.ТребуетсяИнструкция)
                {
                    // Двухпроходная печать без поддержки двусторонней у принтера
                    // (ТЗ §8.6, функция 86): первый проход — лицевые (нечётные)
                    // страницы, затем инструкция оператору, второй проход —
                    // оборотные (чётные) страницы.
                    var первыйПроход = контекст with { Parity = duplex.ЧётностьПервогоПрохода };

                    await _спулер
                        .PrintAsync(
                            первыйПроход,
                            графика => Отрисовать(графика, первыйПроход, предупрежденияРендеринга),
                            cancellationToken)
                        .ConfigureAwait(false);

                    progress?.Report(new PrintProgress(
                        PrintProgressPhase.DuplexFlip,
                        элементы.Count,
                        элементы.Count,
                        PrintProgress.DuplexFlipMessage));

                    var второйПроход = контекст with { Parity = duplex.ЧётностьВторогоПрохода };

                    await _спулер
                        .PrintAsync(
                            второйПроход,
                            графика => Отрисовать(графика, второйПроход, предупрежденияРендеринга),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _спулер
                        .PrintAsync(
                            контекст,
                            графика => Отрисовать(графика, контекст, предупрежденияРендеринга),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                статус = PrintJobStatus.Aborted;
                ошибка = "Печать прервана оператором.";
            }
#pragma warning disable CA1031 // Любая ошибка спулера фиксируется в журнале сессии печати.
            catch (Exception исключение)
#pragma warning restore CA1031
            {
                статус = PrintJobStatus.Failed;
                ошибка = исключение.Message;
            }

            предупреждения.AddRange(предупрежденияРендеринга);

            // Сохранение сформированных в сессии кодов в таблицу qr_cache.
            if (_qr is not null)
            {
                await _qr.СохранитьКэшAsync(cancellationToken).ConfigureAwait(false);
            }

            // История печати конвертов (ТЗ §3.1.7, §8.4, функция 64): снимок
            // адресов получателя и отправителя фиксируется для каждого
            // физически выведенного конверта. Пробная печать (print_mode = test)
            // учётной записи не создаёт (ТЗ §8.10, функция 143).
            if (статус == PrintJobStatus.Success && !request.IsTest && _историяПечати is not null)
            {
                try
                {
                    await _историяПечати
                        .RecordAsync(
                            request.MailIds,
                            плановыйИдентификатор,
                            контекст.TemplateName,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // Сбой записи истории печати фиксируется в журнале сессии печати.
                catch (Exception исключение)
#pragma warning restore CA1031
                {
                    статус = PrintJobStatus.Failed;
                    ошибка = $"Не удалось записать историю печати: {исключение.Message}";
                }
            }

            return await ЗаписатьИтогAsync(
                контекст,
                request,
                статус,
                ошибка,
                предупреждения,
                секундомер,
                cancellationToken,
                плановыйИдентификатор,
                плановаяЗапись).ConfigureAwait(false);
        }
        catch (PrintAuditException исключение)
        {
            return new PrintResult
            {
                IsSuccess = false,
                Status = PrintJobStatus.Failed,
                ErrorMessage = исключение.Message,
                Warnings = предупреждения,
            };
        }
    }

    private void Отрисовать(IGraphicsContext графика, PrintJobContext контекст, List<string> предупреждения)
    {
        if (контекст.Items.Count == 0)
        {
            return;
        }

        // Страница задания раскладывается на пару «отправление × страница
        // шаблона» (ТЗ §8.6, функции 79–80): спулер выставляет CurrentPage =
        // номер страницы задания, здесь она переводится в номер отправления и
        // номер страницы шаблона, и на лист выводится ровно одно отправление.
        var страницНаЭлемент = Math.Max(контекст.Blank.PageCount, 1);
        var номерСтраницыЗадания = Math.Max(контекст.CurrentPage, 1);
        var индексЭлемента = Math.Min(
            (номерСтраницыЗадания - 1) / страницНаЭлемент,
            контекст.Items.Count - 1);
        var элемент = контекст.Items[индексЭлемента];

        контекст.CurrentPage = (номерСтраницыЗадания - 1) % страницНаЭлемент + 1;

        // Предупреждения генератора (переполнение поля, превышение лимита
        // логотипа и др.) фиксируются в предупреждениях сессии печати
        // (ТЗ §8.6, функция 91).
        предупреждения.AddRange(_рендерер.Render(
            графика,
            контекст,
            элемент,
            _кодовыйШтамп,
            _qr,
            _логотип));
    }

    private async Task<PrintResult> ЗаписатьИтогAsync(
        PrintJobContext контекст,
        PrintRequest request,
        string статус,
        string? ошибка,
        List<string> предупреждения,
        Stopwatch секундомер,
        CancellationToken cancellationToken,
        long? плановыйИдентификатор = null,
        PrintJobRecord? плановыйЗапись = null)
    {
        var запись = плановыйЗапись is not null && статус == PrintJobStatus.Success
            ? плановыйЗапись with { Duration = секундомер.Elapsed, Status = статус }
            : СобратьЗапись(контекст, статус, секундомер);

        // Аудит: запись в канал аудита и в реестр сессий печати. Сбой записи
        // аудита отменяет печатную транзакцию (ТЗ §2.2, §8.6, функция 88).
        _аудит.WriteSessionRecord(запись);

        var job = СобратьЗаписьРеестра(контекст, статус, ошибка, DateTime.UtcNow);

        var идентификатор = плановыйИдентификатор ?? await _журнал
            .AddAsync(job, cancellationToken)
            .ConfigureAwait(false);

        if (плановыйИдентификатор is not null && статус != PrintJobStatus.Success)
        {
            // Реестр сессий печати неизменяем (ТЗ §3.1.6): фактический итог
            // сессии, отличающийся от планового, фиксируется уточняющей
            // записью с тем же временем создания.
            идентификатор = await _журнал
                .AddAsync(
                    СобратьЗаписьРеестра(
                        контекст,
                        статус,
                        $"Уточнение статуса сессии. {ошибка}".Trim(),
                        job.CreatedAt),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        _ = request;

        return new PrintResult
        {
            IsSuccess = статус == PrintJobStatus.Success,
            Status = статус,
            ErrorMessage = ошибка,
            Copies = контекст.Copies,
            Warnings = предупреждения,
            PrintJobId = идентификатор,
            AvailablePaperSizes = контекст.Printer.AvailablePaperSizes,
            PrinterName = контекст.Printer.PrinterName,
        };
    }

    /// <summary>
    /// Собирает запись аудита сессии печати по формату §6.5.
    /// </summary>
    /// <param name="контекст">Контекст печатной сессии.</param>
    /// <param name="статус">Итог сессии.</param>
    /// <param name="секундомер">Замер длительности сессии.</param>
    /// <returns>Запись аудита.</returns>
    private static PrintJobRecord СобратьЗапись(PrintJobContext контекст, string статус, Stopwatch секундомер) =>
        new()
        {
            PrinterName = контекст.Printer.PrinterName,
            PaperWidthMm = контекст.PaperWidthMm,
            PaperHeightMm = контекст.PaperHeightMm,
            PaperSizeName = контекст.PaperSizeName,
            PaperSource = контекст.Printer.PaperSource,
            Duplex = контекст.Duplex == DuplexMode.Duplex ? "Vertical" : "Simplex",
            TemplateName = контекст.TemplateName,
            Orientation = контекст.Orientation,
            FontName = контекст.FontName,
            FontSizePt = контекст.FontSizePt,
            IndexFontName = контекст.IndexFontName,
            IndexLineWidthMm = контекст.IndexLineWidthMm,
            OffsetXMm = контекст.OffsetXMm,
            OffsetYMm = контекст.OffsetYMm,
            ObjectCount = контекст.Items.Count,
            PageCount = контекст.TotalPages,
            CopyCount = контекст.Copies,
            Status = статус,
            Duration = секундомер.Elapsed,
        };

    /// <summary>
    /// Собирает запись реестра сессий печати (<c>print_jobs</c>).
    /// </summary>
    /// <param name="контекст">Контекст печатной сессии.</param>
    /// <param name="статус">Итог сессии.</param>
    /// <param name="ошибка">Текст ошибки.</param>
    /// <param name="создана">Момент создания записи.</param>
    /// <returns>Запись реестра.</returns>
    private static PrintJob СобратьЗаписьРеестра(
        PrintJobContext контекст,
        string статус,
        string? ошибка,
        DateTime создана) =>
        new(
            0,
            создана,
            контекст.TemplateId,
            контекст.Printer.PrinterName,
            контекст.PaperSizeName,
            контекст.PaperWidthMm,
            контекст.PaperHeightMm,
            $"{контекст.FontName}; {контекст.IndexFontName}",
            контекст.Copies,
            контекст.PrintMode,
            статус,
            ошибка,
            null);

    /// <summary>
    /// Получает реальные сведения о принтере из драйвера (DEVMODE и
    /// <c>DeviceCapabilitiesW</c>) — ТЗ §6.2.
    /// </summary>
    /// <param name="request">Параметры печати.</param>
    /// <param name="шаблон">Макет бланка.</param>
    /// <returns>Сведения о принтере.</returns>
    /// <summary>
    /// Применяет порядок адресных строк формата конверта (ТЗ §8.4,
    /// функция 33): при smallToLarge = true печатный порядок строк —
    /// «от частного к общему» (дом → город → страна): непустые строки
    /// адреса перенумеровываются в обратном порядке относительно хранения.
    /// Пустые строки не создаются; перенос по словам/буквам не меняется
    /// (меняется только порядок готовых строк).
    /// </summary>
    /// <param name="items">Элементы печати со значениями полей бланка.</param>
    /// <param name="smallToLarge">Признак порядка «от частного к общему».</param>
    /// <returns>Элементы с переписанными to_strN/from_strN при необходимости.</returns>
    public static IReadOnlyList<PrintableItem> ApplyAddressOrder(
        IReadOnlyList<PrintableItem> items,
        bool smallToLarge)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!smallToLarge)
        {
            return items;
        }

        static IReadOnlyDictionary<string, string> Переписать(
            IReadOnlyDictionary<string, string> значения,
            string префикс)
        {
            var результат = new Dictionary<string, string>(значения, StringComparer.OrdinalIgnoreCase);
            const int строк = 6;
            var непустые = new List<string>();

            for (var номер = 1; номер <= строк; номер++)
            {
                if (результат.TryGetValue(префикс + номер, out var значение)
                    && !string.IsNullOrWhiteSpace(значение))
                {
                    непустые.Add(значение);
                }
            }

            if (непустые.Count < 2)
            {
                return значения;
            }

            непустые.Reverse();

            for (var номер = 1; номер <= строк; номер++)
            {
                результат[префикс + номер] = номер <= непустые.Count
                    ? непустые[номер - 1]
                    : string.Empty;
            }

            return результат;
        }

        var изменённые = new List<PrintableItem>(items.Count);

        foreach (var элемент in items)
        {
            var значения = элемент.Values;
            значения = Переписать(значения, "to_str");
            значения = Переписать(значения, "from_str");
            изменённые.Add(элемент with { Values = значения });
        }

        return изменённые;
    }

    private PrinterDeviceInfo ПолучитьСведенияОПринтере(PrintRequest request, PrintTemplate шаблон)
    {
        var кодФормата = PrinterDeviceInfo.PaperSizeCodeByFormat(шаблон.Format);

        if (_принтеры is null || string.IsNullOrWhiteSpace(request.PrinterName))
        {
            // Драйвер недоступен (например, в тестовом контуре): размеры
            // печатной области принимаются равными размеру бланка — проверка
            // метрик не блокирует печать.
            return new PrinterDeviceInfo
            {
                PrinterName = request.PrinterName,
                PaperSizeName = шаблон.Format,
                PaperWidthMm = шаблон.WidthMm,
                PaperHeightMm = шаблон.HeightMm,
                PrintableWidthMm = шаблон.WidthMm,
                PrintableHeightMm = шаблон.HeightMm,
                DmPaperSize = кодФормата,
            };
        }

        try
        {
            return _принтеры.GetDeviceInfo(request.PrinterName, кодФормата);
        }
#pragma warning disable CA1031 // Сбой опроса драйвера не должен прерывать сессию печати.
        catch (Exception)
#pragma warning restore CA1031
        {
            return new PrinterDeviceInfo
            {
                PrinterName = request.PrinterName,
                PaperSizeName = шаблон.Format,
                PaperWidthMm = шаблон.WidthMm,
                PaperHeightMm = шаблон.HeightMm,
                PrintableWidthMm = шаблон.WidthMm,
                PrintableHeightMm = шаблон.HeightMm,
                DmPaperSize = кодФормата,
            };
        }
    }

    /// <summary>
    /// Определяет территорию отправления и параметры кодового штампа по
    /// справочнику форматов (ТЗ §6.3; ГОСТ Р 51506-99): внутреннее
    /// отправление («Вн») — шестизначный индекс, международное («Мн») —
    /// трёхзначный индекс «555».
    /// </summary>
    /// <param name="контекст">Контекст печатной сессии.</param>
    /// <param name="шаблон">Макет бланка.</param>
    private PrintJobContext ПрименитьТерриторию(PrintJobContext контекст, PrintTemplate шаблон)
    {
        if (_форматы is null)
        {
            return контекст;
        }

        var геометрия = _форматы.Get(шаблон.Format, шаблон.Country);

        if (геометрия is null)
        {
            return контекст;
        }

        return контекст with
        {
            IndexDigitCount = геометрия.IndexDigitCount,
            InternationalIndex = геометрия.InternationalIndex,
            Territory = геометрия.Territory,
            AddressOrderSmallToLarge = геометрия.AddressOrderSmallToLarge,
        };
    }

    /// <summary>
    /// Возвращает калибровочное смещение принтера для пары «принтер + формат»
    /// (ТЗ §8.6, функция 45).
    /// </summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="format">Формат бланка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Смещение, мм, либо <see langword="null"/>.</returns>
    private async Task<(decimal X, decimal Y)?> ПолучитьСмещениеПринтера(
        string? printerName,
        string? format,
        CancellationToken cancellationToken)
    {
        if (_профили is null || string.IsNullOrWhiteSpace(printerName) || string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        try
        {
            var профиль = await _профили
                .GetByPrinterAndFormatAsync(printerName, format, cancellationToken)
                .ConfigureAwait(false);

            return профиль is null ? null : (профиль.OffsetXMm, профиль.OffsetYMm);
        }
#pragma warning disable CA1031 // Отсутствие профиля не должно прерывать печать.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}