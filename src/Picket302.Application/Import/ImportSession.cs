using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Import;

/// <summary>
/// Сессия мастера импорта адресной книги: чтение файла, сопоставление
/// столбцов, пошаговый предпросмотр, проверка и финальное применение
/// (ТЗ §8.12, функция 159: «Мастер импорта адресной книги»).
/// </summary>
/// <remarks>
/// <para>
/// Сессия выполняет семь шагов мастера: выбор файла и листа книги, выбор
/// категории и строки начала импорта, сопоставление столбцов с фиксированным
/// списком полей, пошаговый предпросмотр каждой записи с возможностью правки и
/// пропуска, фильтр «Все / С ошибками», финальный экран с тремя политиками
/// импорта.
/// </para>
/// <para>
/// Финальный шаг не закрывает сессию: оператор может вернуться к редактированию
/// («Вернуться к редактированию»), изменить состояния записей и повторить
/// применение.
/// </para>
/// </remarks>
public sealed class ImportSession
{
    /// <summary>
    /// Сообщение об отказе при слитых в одном столбце значениях.
    /// </summary>
    public const string СообщениеСлитыхПолей =
        "Не удалось разделить поля: значения слиты в одном столбце без разделителей. Укажите разделитель вручную.";

    /// <summary>
    /// Сообщение с предложением сохранить книгу в другом формате.
    /// </summary>
    public const string СообщениеXlsx =
        "Файл XLSX не удалось разобрать. Сохраните его как CSV или HTML и повторите импорт.";

    /// <summary>
    /// Максимальная длина текстового значения контрагента.
    /// </summary>
    public const int МаксимальнаяДлинаЗначения = ImportValidator.МаксимальнаяДлинаЗначения;

    private static readonly DelimitedTextReader Читатель = new();
    private static readonly RtfTableReader ЧитательRtf = new();
    private static readonly CsvProfileService СервисПрофилей = new();
    private static readonly ImportFieldMapper Маппер = new();

    private readonly ICounterpartyRepository? _репозиторий;
    private readonly ImportMergeService? _слияние;
    private readonly IImportMerger? _слиятель;
    private readonly ImportDeduplicator? _дедупликация;
    private readonly AddressHeuristicsService _адреса;
    private readonly bool _распознаватьАдреса;
    private MergePolicy _политикаСлияния;

    /// <summary>
    /// Создаёт сессию импорта.
    /// </summary>
    /// <param name="batch">Пакет импорта.</param>
    /// <param name="columnNames">Заголовки столбцов файла.</param>
    /// <param name="bindings">Сопоставление столбцов со слотами полей.</param>
    /// <param name="extraBindings">Сопоставление дополнительных полей.</param>
    /// <param name="warnings">Предупреждения, возникшие при чтении файла.</param>
    /// <param name="repository">Репозиторий контрагентов для финального шага.</param>
    /// <param name="groupId">Категория адресной книги, с которой начинается импорт.</param>
    /// <param name="mergePolicy">Политика слияния с существующей базой.</param>
    /// <param name="mergeService">Служба тонкого слияния.</param>
    /// <param name="merger">
    /// Слиятель пакета импорта. Задаётся в совместном режиме: слияние
    /// выполняет серверная служба одной командой в одной транзакции.
    /// </param>
    /// <param name="deduplicator">Дедупликатор импорта.</param>
    /// <param name="addressService">Служба распознавания адресов.</param>
    /// <param name="recognizeAddresses">
    /// Признак автоматического распознавания адресов при импорте.
    /// </param>
    public ImportSession(
        ImportBatch batch,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<ImportColumnBinding> bindings,
        IReadOnlyList<ImportExtraBinding> extraBindings,
        IReadOnlyList<string> warnings,
        ICounterpartyRepository? repository = null,
        long? groupId = null,
        MergePolicy mergePolicy = MergePolicy.SkipExisting,
        ImportMergeService? mergeService = null,
        IImportMerger? merger = null,
        ImportDeduplicator? deduplicator = null,
        AddressHeuristicsService? addressService = null,
        bool recognizeAddresses = true)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        ColumnNames = columnNames ?? Array.Empty<string>();
        Bindings = bindings ?? Array.Empty<ImportColumnBinding>();
        ExtraBindings = extraBindings ?? Array.Empty<ImportExtraBinding>();
        Warnings = warnings ?? Array.Empty<string>();

        _репозиторий = repository;
        GroupId = groupId;
        _политикаСлияния = mergePolicy;
        _слияние = mergeService ?? (repository is null ? null : new ImportMergeService(repository));
        _слиятель = merger;
        _дедупликация = deduplicator ?? new ImportDeduplicator(repository);
        _адреса = addressService ?? new AddressHeuristicsService();
        _распознаватьАдреса = recognizeAddresses;
    }

    /// <summary>Пакет импорта: прочитанные записи.</summary>
    public ImportBatch Batch { get; }

    /// <summary>Заголовки столбцов файла.</summary>
    public IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Предложенное сопоставление столбцов со слотами полей.</summary>
    public IReadOnlyList<ImportColumnBinding> Bindings { get; }

    /// <summary>Предложенное сопоставление дополнительных полей.</summary>
    public IReadOnlyList<ImportExtraBinding> ExtraBindings { get; }

    /// <summary>Предупреждения, возникшие при чтении файла.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Категория (группа) адресной книги, с которой начинается импорт.</summary>
    public long? GroupId { get; private set; }

    /// <summary>Описание источника данных вида «Импорт &lt;формат&gt; от &lt;дата&gt; из &lt;файл&gt;».</summary>
    public string SourceDescription => Batch.SourceDescription;

    /// <summary>Записи пакета импорта.</summary>
    public IReadOnlyList<ImportRecord> Records => Batch.Records;

    /// <summary>Политика слияния с существующей базой, применяемая на финальном шаге.</summary>
    public MergePolicy MergePolicy => _политикаСлияния;

    /// <summary>
    /// Читает файл, определяет формат, применяет сопоставление столбцов и
    /// создаёт сессию мастера импорта с параметрами по умолчанию
    /// (ТЗ §8.12, функция 159: «public ImportSession Start(string filePath)»).
    /// </summary>
    /// <param name="filePath">Путь к импортируемому файлу.</param>
    /// <returns>
    /// Сессия импорта либо текст ошибки: файл не найден, формат не поддерживается,
    /// значения слиты в одном столбце без разделителей.
    /// </returns>
    public static Result<ImportSession> Start(string filePath) =>
        Start(filePath, new ImportStartOptions { FilePath = filePath });

    /// <summary>
    /// Читает файл, определяет формат, применяет сопоставление столбцов и
    /// создаёт сессию мастера импорта (ТЗ §8.12, функция 159, шаги 1–3).
    /// </summary>
    /// <param name="filePath">Путь к импортируемому файлу.</param>
    /// <param name="options">Параметры запуска мастера импорта.</param>
    /// <returns>
    /// Сессия импорта либо текст ошибки: файл не найден, формат не поддерживается,
    /// значения слиты в одном столбце без разделителей.
    /// </returns>
    public static Result<ImportSession> Start(string filePath, ImportStartOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result<ImportSession>.Failure("Файл импорта не указан.");
        }

        if (!File.Exists(filePath))
        {
            return Result<ImportSession>.Failure($"Файл импорта не найден: {filePath}");
        }

        var имяФайла = Path.GetFileName(filePath);
        var предупреждения = new List<string>();
        var формат = ОпределитьФормат(filePath);

        if (формат == "XML")
        {
            return Result<ImportSession>.Failure(
                "Формат XML (выгрузка 1С) обрабатывается отдельным модулем импорта данных 1С.");
        }

        if (string.IsNullOrEmpty(формат))
        {
            return Result<ImportSession>.Failure(
                $"Формат файла «{Path.GetExtension(filePath)}» не поддерживается. Используйте CSV, XLSX, HTML или RTF.");
        }

        DelimitedTextTable таблица;

        switch (формат)
        {
            case "XLSX":
                var результатXlsx = ПрочитатьКнигу(filePath, options);

                if (!результатXlsx.IsSuccess)
                {
                    return Result<ImportSession>.Failure(результатXlsx.Error!);
                }

                таблица = результатXlsx.Value!;
                break;

            case "HTML":
                var результатHtml = ПрочитатьТаблицуHtml(filePath, options);

                if (!результатHtml.IsSuccess)
                {
                    return Result<ImportSession>.Failure(результатHtml.Error!);
                }

                таблица = результатHtml.Value!;
                break;

            case "RTF":
                var результатRtf = ПрочитатьДокументRtf(filePath, options);

                if (!результатRtf.IsSuccess)
                {
                    return Result<ImportSession>.Failure(результатRtf.Error!);
                }

                таблица = результатRtf.Value!;
                break;

            default:
                var результатCsv = ПрочитатьРазделители(filePath, options, предупреждения);

                if (!результатCsv.IsSuccess)
                {
                    return Result<ImportSession>.Failure(результатCsv.Error!);
                }

                таблица = результатCsv.Value!;
                break;
        }

        if (ПохожеНаСлитыеПоля(таблица))
        {
            return Result<ImportSession>.Failure(СообщениеСлитыхПолей);
        }

        if (таблица.HasErrors)
        {
            предупреждения.Add(
                $"В файле {таблица.ErrorCount} строк(и) с числом столбцов, отличным от ожидаемого — проверьте их в предпросмотре.");
        }

        var заголовки = таблица.Headers.Count > 0
            ? таблица.Headers.ToList()
            : Enumerable.Range(1, МаксимальноеЧислоСтолбцов(таблица)).Select(номер => $"Столбец {номер}").ToList();

        if (таблица.Headers.Count == 0)
        {
            предупреждения.Add("В файле нет строки заголовков — сопоставление столбцов необходимо задать вручную.");
        }

        var привязки = options.Bindings is { Count: > 0 }
            ? Маппер.Apply(options.Bindings)
            : Маппер.Suggest(заголовки);

        if (options.Bindings is { Count: > 0 })
        {
            предупреждения.Add("Применено сопоставление столбцов, заданное оператором.");
        }

        var дополнительные = Маппер.SuggestExtras(заголовки);
        var моментИмпорта = DateTime.Now;
        var адреса = new AddressHeuristicsService();

        var записи = new List<ImportRecord>();

        foreach (var строка in таблица.Rows)
        {
            var черновик = ПостроитьЧерновик(строка, привязки, дополнительные, options, адреса);

            // Группа из файла имеет приоритет над категорией, выбранной оператором.
            черновик.GroupId ??= options.GroupId;
            черновик.Source = $"Импорт {формат} от {моментИмпорта:dd.MM.yyyy} из {имяФайла}";

            var запись = new ImportRecord
            {
                RowNumber = строка.RowNumber,
                Draft = черновик,
                Status = ImportRecordStatus.Unverified,
            };

            if (строка.HasError)
            {
                запись.Errors = [строка.Error ?? $"Строка {строка.RowNumber}: ошибка чтения файла."];
                запись.Status = ImportRecordStatus.HasErrors;
            }

            записи.Add(запись);
        }

        var пакет = new ImportBatch
        {
            SourceFormat = формат,
            SourceFile = имяФайла,
            ImportedAt = моментИмпорта,
            Records = записи,
        };

        var сессия = new ImportSession(
            пакет,
            заголовки,
            привязки,
            дополнительные,
            предупреждения,
            options.Repository,
            options.GroupId,
            options.MergePolicy,
            merger: options.Merger,
            addressService: адреса,
            recognizeAddresses: options.RecognizeAddresses);

        return Result<ImportSession>.Success(сессия);
    }

    /// <summary>
    /// Проверяет все записи пакета: обязательные поля, ИНН, почтовый индекс,
    /// телефон и электронная почта; значения длиннее 255 символов усекаются с
    /// предупреждением (ТЗ §8.12, функция 159, шаг 4).
    /// </summary>
    /// <returns>Сводка проверки пакета.</returns>
    public ValidationSummary ValidateAll() => new ImportValidator(Batch).ValidateAll();

    /// <summary>
    /// Применяет выбранную политику импорта и возвращает итог (ТЗ §8.12,
    /// функция 159, шаги 6–7).
    /// </summary>
    /// <param name="policy">Политика импорта.</param>
    /// <returns>Итог импорта.</returns>
    public ImportResult Commit(ImportPolicy policy) => Commit(policy, _политикаСлияния);

    /// <summary>
    /// Применяет выбранную политику импорта и возвращает итог.
    /// </summary>
    /// <param name="policy">Политика импорта.</param>
    /// <param name="mergePolicy">Политика слияния с существующей базой.</param>
    /// <returns>Итог импорта.</returns>
    public ImportResult Commit(ImportPolicy policy, MergePolicy mergePolicy)
        => CommitAsync(policy, mergePolicy).GetAwaiter().GetResult();

    /// <summary>
    /// Применяет выбранную политику импорта и возвращает итог.
    /// </summary>
    /// <param name="policy">Политика импорта.</param>
    /// <param name="mergePolicy">Политика слияния с существующей базой.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Итог импорта.</returns>
    public async Task<ImportResult> CommitAsync(
        ImportPolicy policy,
        MergePolicy mergePolicy,
        CancellationToken cancellationToken = default)
    {
        if (_слияние is null && _слиятель is null)
        {
            throw new InvalidOperationException(
                "Не задан слиятель импорта: импорт не может быть применён.");
        }

        var отобранные = new List<ImportRecord>();
        var отброшеноПолитикой = 0;

        foreach (var запись in Batch.Records)
        {
            if (запись.Status == ImportRecordStatus.Skipped)
            {
                continue;
            }

            if (!ПодходитПоПолитике(запись, policy))
            {
                отброшеноПолитикой++;
                continue;
            }

            отобранные.Add(запись);
        }

        // Дедупликация внутри пакета: повторы по ключу слияния исключаются.
        var дедупликация = _дедупликация!.FilterDuplicates(
            отобранные.Select(запись => запись.Draft),
            mergePolicy,
            SourceDescription,
            cancellationToken,
            проверятьБазу: false);

        var принятые = new HashSet<CounterpartyDraft>(дедупликация.Accepted);

        var пакет = new ImportBatch
        {
            SourceFormat = Batch.SourceFormat,
            SourceFile = Batch.SourceFile,
            ImportedAt = Batch.ImportedAt,
            Records = отобранные.Where(запись => принятые.Contains(запись.Draft)).ToList(),
        };

        if (_слиятель is not null)
        {
            // Слияние выполняет служба: пакет применяется одной командой в
            // одной транзакции. Повтор команды с прежним идентификатором
            // операции возвращает прежний результат и не создаёт дублей.
            var итог = await _слиятель
                .СлитьAsync(пакет, mergePolicy, cancellationToken)
                .ConfigureAwait(false);

            return new ImportResult
            {
                Policy = policy,
                Created = итог.Created,
                Updated = итог.Updated,
                Skipped = итог.Skipped
                    + отброшеноПолитикой
                    + дедупликация.SkippedDuplicates.Count,
                Conflicts = итог.Conflicts,
                Errors = итог.Errors,
                Unverified = итог.Unverified,
            };
        }

        var результаты = await _слияние!
            .MergeImportedDetailed(пакет, mergePolicy, cancellationToken)
            .ConfigureAwait(false);

        var записанные = результаты
            .Where(результат => результат.Action != ImportMergeAction.Skipped)
            .ToList();

        var ошибки = записанные.Count(результат => результат.Record.Status == ImportRecordStatus.HasErrors);
        var непроверенные = записанные.Count(результат => результат.Record.Status == ImportRecordStatus.Unverified);

        var создано = результаты.Count(результат => результат.Action == ImportMergeAction.Created);
        var обновлено = результаты.Count(результат => результат.Action == ImportMergeAction.Updated);
        var конфликтов = результаты.Count(результат => результат.HasConflict);

        var пропущено = отброшеноПолитикой
            + дедупликация.SkippedDuplicates.Count
            + результаты.Count(результат => результат.Action == ImportMergeAction.Skipped);

        return new ImportResult
        {
            Policy = policy,
            Created = создано,
            Updated = обновлено,
            Skipped = пропущено,
            Conflicts = конфликтов,
            Errors = ошибки,
            Unverified = непроверенные,
        };
    }

    /// <summary>
    /// Отмечает запись как проверенную оператором.
    /// </summary>
    /// <param name="record">Запись предпросмотра.</param>
    public void MarkVerified(ImportRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.Errors = Array.Empty<string>();
        record.Status = ImportRecordStatus.Verified;
    }

    /// <summary>
    /// Отмечает запись как пропущенную оператором (знак «&lt;&lt; &gt;&gt;»).
    /// </summary>
    /// <param name="record">Запись предпросмотра.</param>
    public void MarkSkipped(ImportRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.Status = ImportRecordStatus.Skipped;
    }

    /// <summary>
    /// Возвращает запись в состояние «не проверено» (снятие отметки проверки
    /// либо пропуска).
    /// </summary>
    /// <param name="record">Запись предпросмотра.</param>
    public void MarkUnverified(ImportRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        record.Status = record.Errors.Count > 0
            ? ImportRecordStatus.HasErrors
            : ImportRecordStatus.Unverified;
    }

    /// <summary>
    /// Применяет правку оператора к записи предпросмотра и перепроверяет её.
    /// </summary>
    /// <param name="record">Запись предпросмотра с исправленными значениями.</param>
    public void UpdateRecord(ImportRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        new ImportValidator(Batch).ValidateRecord(record);
    }

    /// <summary>
    /// Возвращает записи пакета для пошагового предпросмотра с фильтром
    /// «Все / С ошибками» (ТЗ §8.12, функция 159, шаг 5).
    /// </summary>
    /// <param name="толькоСОшибками">Показывать только записи с ошибками.</param>
    /// <returns>Отобранные записи предпросмотра.</returns>
    public IReadOnlyList<ImportRecord> GetRecords(bool толькоСОшибками = false)
        => толькоСОшибками
            ? Batch.Records.Where(запись => запись.HasErrors).ToList()
            : Batch.Records;

    /// <summary>
    /// Возвращает запись пакета по номеру строки исходного файла.
    /// </summary>
    /// <param name="rowNumber">Номер строки файла (нумерация с единицы).</param>
    /// <returns>Запись пакета либо <see langword="null"/>.</returns>
    public ImportRecord? GetRecord(int rowNumber)
        => Batch.Records.FirstOrDefault(запись => запись.RowNumber == rowNumber);

    /// <summary>
    /// Меняет категорию адресной книги, с которой выполняется импорт.
    /// </summary>
    /// <param name="groupId">Идентификатор группы либо <see langword="null"/>.</param>
    public void SetGroupId(long? groupId)
    {
        GroupId = groupId;

        foreach (var запись in Batch.Records)
        {
            запись.Draft.GroupId = groupId;
        }
    }

    /// <summary>
    /// Меняет политику слияния с существующей базой.
    /// </summary>
    /// <param name="mergePolicy">Политика слияния.</param>
    public void SetMergePolicy(MergePolicy mergePolicy) => _политикаСлияния = mergePolicy;

    /// <summary>
    /// Проверяет, отбирается ли запись по политике импорта.
    /// </summary>
    /// <param name="record">Запись предпросмотра.</param>
    /// <param name="policy">Политика импорта.</param>
    /// <returns><see langword="true"/>, если запись подлежит импорту.</returns>
    private static bool ПодходитПоПолитике(ImportRecord record, ImportPolicy policy) => policy switch
    {
        ImportPolicy.VerifiedOnly => record.Status == ImportRecordStatus.Verified,
        ImportPolicy.WithoutErrors => record.Status != ImportRecordStatus.HasErrors && record.Errors.Count == 0,
        _ => true,
    };

    /// <summary>
    /// Определяет формат файла по расширению и содержимому.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>Имя формата («CSV», «XLSX», «HTML», «RTF», «XML») либо пустая строка.</returns>
    private static string ОпределитьФормат(string filePath)
    {
        var расширение = Path.GetExtension(filePath).ToLowerInvariant();

        switch (расширение)
        {
            case ".xlsx":
            case ".xlsm":
                return "XLSX";
            case ".csv":
            case ".txt":
            case ".tsv":
                return "CSV";
            case ".htm":
            case ".html":
                return "HTML";
            case ".rtf":
                return "RTF";
            case ".xml":
                return "XML";
        }

        var начало = ReadНачалоФайла(filePath);

        if (начало.StartsWith("PK", StringComparison.Ordinal))
        {
            return "XLSX";
        }

        if (начало.StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase))
        {
            return "RTF";
        }

        if (начало.Contains("<table", StringComparison.OrdinalIgnoreCase)
            || начало.Contains("<html", StringComparison.OrdinalIgnoreCase))
        {
            return "HTML";
        }

        return "CSV";
    }

    /// <summary>
    /// Читает начало файла для определения формата.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>Первые символы файла.</returns>
    private static string ReadНачалоФайла(string filePath)
    {
        try
        {
            var байты = File.ReadAllBytes(filePath);
            var длина = Math.Min(2048, байты.Length);

            return Encoding.UTF8.GetString(байты, 0, длина);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Читает файл с разделителями, определяя кодировку и разделитель столбцов.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="options">Параметры запуска мастера импорта.</param>
    /// <param name="предупреждения">Накопитель предупреждений.</param>
    /// <returns>Разобранная таблица либо текст ошибки.</returns>
    private static Result<DelimitedTextTable> ПрочитатьРазделители(
        string filePath,
        ImportStartOptions options,
        List<string> предупреждения)
    {
        byte[] байты;

        try
        {
            байты = File.ReadAllBytes(filePath);
        }
        catch (IOException ex)
        {
            return Result<DelimitedTextTable>.Failure($"Файл импорта не удалось прочитать: {ex.Message}");
        }

        var определение = СервисПрофилей.DetectEncoding(байты);
        var заданнаяКодировка = TextEncodingDetector.ПоИмени(options.EncodingName);
        var текст = (заданнаяКодировка ?? определение.Encoding).GetString(байты);

        if (текст.Length > 0 && текст[0] == '\uFEFF')
        {
            текст = текст[1..];
        }

        var разделитель = options.Delimiter
            ?? СервисПрофилей.DetectDelimiter(текст);

        if (options.Delimiter is null && !текст.Contains(разделитель, StringComparison.Ordinal))
        {
            предупреждения.Add(
                $"Разделитель столбцов «{разделитель}» в файле не найден — все значения считаются одним столбцом.");
        }

        var профиль = new CsvProfile
        {
            Name = Path.GetFileName(filePath),
            EncodingName = string.IsNullOrWhiteSpace(options.EncodingName) ? определение.EncodingName : options.EncodingName,
            Delimiter = разделитель,
            QuoteChar = CsvProfileService.СимволКавычкиПоУмолчанию,
            HasHeaderRow = options.HasHeaderRow,
            FirstDataRow = Math.Max(1, options.StartRow),
        };

        var таблица = Читатель.Read(текст, профиль);

        return Result<DelimitedTextTable>.Success(таблица);
    }

    /// <summary>
    /// Читает книгу XLSX: лист, строки и значения ячеек.
    /// </summary>
    /// <param name="filePath">Путь к книге.</param>
    /// <param name="options">Параметры запуска мастера импорта.</param>
    /// <returns>Разобранная таблица либо текст ошибки с предложением сохранить файл иначе.</returns>
    private static Result<DelimitedTextTable> ПрочитатьКнигу(string filePath, ImportStartOptions options)
    {
        try
        {
            var таблица = КнигаXlsx.Прочитать(
                filePath,
                options.SheetName,
                options.SheetIndex,
                Math.Max(1, options.StartRow),
                options.HasHeaderRow);

            if (таблица is null)
            {
                return Result<DelimitedTextTable>.Failure(СообщениеXlsx);
            }

            return Result<DelimitedTextTable>.Success(таблица);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException)
        {
            return Result<DelimitedTextTable>.Failure(СообщениеXlsx);
        }
    }

    /// <summary>
    /// Читает HTML-таблицу как источник данных.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="options">Параметры запуска мастера импорта.</param>
    /// <returns>Разобранная таблица либо текст ошибки.</returns>
    private static Result<DelimitedTextTable> ПрочитатьТаблицуHtml(string filePath, ImportStartOptions options)
    {
        string текст;

        try
        {
            var байты = File.ReadAllBytes(filePath);
            var определение = СервисПрофилей.DetectEncoding(байты);
            текст = определение.Encoding.GetString(байты);
        }
        catch (IOException ex)
        {
            return Result<DelimitedTextTable>.Failure($"Файл импорта не удалось прочитать: {ex.Message}");
        }

        var таблица = HtmlTableReader.Прочитать(текст, options.HasHeaderRow, Math.Max(1, options.StartRow));

        if (таблица is null)
        {
            return Result<DelimitedTextTable>.Failure(
                "Файл HTML не удалось разобрать: в файле не найдено ни одной таблицы. Сохраните данные как CSV и повторите импорт.");
        }

        return Result<DelimitedTextTable>.Success(таблица);
    }

    /// <summary>
    /// Читает документ RTF как источник данных: таблица RTF либо строки из
    /// абзацев документа, если таблицы в нём нет (ТЗ §8.5, функция 71).
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="options">Параметры запуска мастера импорта.</param>
    /// <returns>Разобранная таблица либо текст ошибки.</returns>
    private static Result<DelimitedTextTable> ПрочитатьДокументRtf(string filePath, ImportStartOptions options)
    {
        try
        {
            using var поток = File.OpenRead(filePath);

            var таблица = ЧитательRtf.Read(
                поток,
                options.HasHeaderRow,
                Math.Max(1, options.StartRow));

            if (таблица.Rows.Count == 0 && таблица.Headers.Count == 0)
            {
                return Result<DelimitedTextTable>.Failure(
                    "Файл RTF не удалось разобрать: в файле не найдено ни таблицы, ни абзацев с текстом. Сохраните данные как CSV и повторите импорт.");
            }

            return Result<DelimitedTextTable>.Success(ПроверитьДлинуЗначений(таблица));
        }
        catch (IOException ex)
        {
            return Result<DelimitedTextTable>.Failure($"Файл импорта не удалось прочитать: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверяет предельную длину значения в строках файла RTF: значение
    /// длиннее 255 символов помечает строку как ошибочную, чтобы усечение
    /// не осталось молчаливым (ТЗ §9.7, п. 4).
    /// </summary>
    /// <param name="таблица">Разобранная таблица файла.</param>
    /// <returns>Таблица с проверенными строками.</returns>
    private static DelimitedTextTable ПроверитьДлинуЗначений(DelimitedTextTable таблица)
    {
        var строки = new List<DelimitedTextRow>();

        foreach (var строка in таблица.Rows)
        {
            var превышение = -1;

            for (var индекс = 0; индекс < строка.Cells.Count; индекс++)
            {
                var значение = строка.Cells[индекс];

                if (значение is not null && значение.Length > МаксимальнаяДлинаЗначения)
                {
                    превышение = индекс;
                    break;
                }
            }

            if (превышение < 0)
            {
                строки.Add(строка);
                continue;
            }

            var сообщение =
                $"Строка {строка.RowNumber}: значение столбца {превышение + 1} длиннее {МаксимальнаяДлинаЗначения} символов.";

            var ошибка = строка.Error is null ? сообщение : $"{строка.Error} {сообщение}";

            строки.Add(строка with { HasError = true, Error = ошибка });
        }

        return new DelimitedTextTable(таблица.Headers, строки);
    }

    /// <summary>
    /// Определяет, содержит ли файл значения, слитые в одном столбце без
    /// разделителей (ТЗ §8.12, функция 159: «поля, слитые без разделителей,
    /// должны отклоняться до начала импорта»).
    /// </summary>
    /// <param name="таблица">Разобранная таблица файла.</param>
    /// <returns><see langword="true"/>, если значения слиты в одном столбце.</returns>
    private static bool ПохожеНаСлитыеПоля(DelimitedTextTable таблица)
    {
        if (таблица.Headers.Count > 1)
        {
            return false;
        }

        foreach (var строка in таблица.Rows)
        {
            if (строка.Cells.Count > 1)
            {
                return false;
            }
        }

        foreach (var строка in таблица.Rows)
        {
            if (строка.Cells.Count == 0)
            {
                continue;
            }

            var значение = строка.Cells[0];

            if (значение.Contains(',', StringComparison.Ordinal))
            {
                continue;
            }

            var слова = значение.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (слова.Length < 4)
            {
                continue;
            }

            var маркеров = слова.Count(слово => ЯвляетсяМаркеромЗначения(слово));

            if (маркеров >= 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Определяет, является ли слово маркером отдельного значения адреса или
    /// реквизита.
    /// </summary>
    /// <param name="слово">Проверяемое слово.</param>
    /// <returns><see langword="true"/>, если слово — маркер значения.</returns>
    private static bool ЯвляетсяМаркеромЗначения(string слово)
    {
        var нормализованное = ToponymDictionary.Normalize(слово);
        var словарь = ToponymDictionary.Default;

        return словарь.IsSettlementType(нормализованное)
            || словарь.IsStreetType(нормализованное)
            || словарь.IsHouseType(нормализованное)
            || словарь.IsFlatType(нормализованное);
    }

    /// <summary>
    /// Определяет максимальное число столбцов в таблице.
    /// </summary>
    /// <param name="таблица">Разобранная таблица файла.</param>
    /// <returns>Число столбцов.</returns>
    private static int МаксимальноеЧислоСтолбцов(DelimitedTextTable таблица)
        => таблица.Rows.Count == 0 ? 0 : таблица.Rows.Max(строка => строка.Cells.Count);

    /// <summary>
    /// Строит черновик контрагента по строке файла.
    /// </summary>
    /// <param name="строка">Строка файла.</param>
    /// <param name="привязки">Сопоставление столбцов со слотами полей.</param>
    /// <param name="дополнительные">Сопоставление дополнительных полей.</param>
    /// <param name="options">Параметры запуска мастера импорта.</param>
    /// <returns>Черновик контрагента.</returns>
    private static CounterpartyDraft ПостроитьЧерновик(
        DelimitedTextRow строка,
        IReadOnlyList<ImportColumnBinding> привязки,
        IReadOnlyList<ImportExtraBinding> дополнительные,
        ImportStartOptions options,
        AddressHeuristicsService адреса)
    {
        var значения = new Dictionary<ImportFieldSlot, string>();

        foreach (var привязка in привязки)
        {
            if (привязка.Slot is null)
            {
                continue;
            }

            var значение = ЗначениеЯчейки(строка, привязка.ColumnIndex);

            if (string.IsNullOrWhiteSpace(значение))
            {
                continue;
            }

            if (!значения.TryGetValue(привязка.Slot.Value, out var прежнее)
                || string.IsNullOrWhiteSpace(прежнее))
            {
                значения[привязка.Slot.Value] = значение.Trim();
            }
        }

        var черновик = new CounterpartyDraft();

        if (значения.TryGetValue(ImportFieldSlot.OrganizationName, out var организация)
            && !string.IsNullOrWhiteSpace(организация))
        {
            черновик.Name = организация;
            черновик.Type = CounterpartyType.LegalEntity;
        }
        else if (значения.TryGetValue(ImportFieldSlot.FullName, out var фио)
            && !string.IsNullOrWhiteSpace(фио))
        {
            черновик.Name = фио;
            черновик.Type = CounterpartyType.NaturalPerson;
        }
        else if (значения.TryGetValue(ImportFieldSlot.Name, out var имя)
            && !string.IsNullOrWhiteSpace(имя))
        {
            черновик.Name = имя;
        }

        if (значения.TryGetValue(ImportFieldSlot.ShortName, out var сокращённое))
        {
            черновик.ShortName = сокращённое;
        }

        var почтовыйАдрес = РазобратьАдрес(значения, ImportFieldSlot.PostalAddress, options.RecognizeAddresses, адреса);

        if (почтовыйАдрес is not null)
        {
            черновик.PostalZip = почтовыйАдрес.Zip;
            черновик.PostalLines = почтовыйАдрес.Lines.ToArray();
            черновик.NeedsVerification |= почтовыйАдрес.NeedsVerification;

            if (string.IsNullOrWhiteSpace(черновик.Name) && почтовыйАдрес.FullName.Length > 0)
            {
                черновик.Name = почтовыйАдрес.FullName;
                черновик.Type = CounterpartyType.NaturalPerson;
            }
        }

        var юридическийАдрес = РазобратьАдрес(значения, ImportFieldSlot.LegalAddress, options.RecognizeAddresses, адреса);

        if (юридическийАдрес is not null)
        {
            черновик.LegalZip = юридическийАдрес.Zip;
            черновик.LegalLines = юридическийАдрес.Lines.ToArray();
            черновик.NeedsVerification |= юридическийАдрес.NeedsVerification;
        }

        var фактическийАдрес = РазобратьАдрес(значения, ImportFieldSlot.ActualAddress, options.RecognizeAddresses, адреса);

        if (фактическийАдрес is not null)
        {
            черновик.ActualZip = фактическийАдрес.Zip;
            черновик.ActualLines = фактическийАдрес.Lines.ToArray();
            черновик.NeedsVerification |= фактическийАдрес.NeedsVerification;
        }

        if (значения.TryGetValue(ImportFieldSlot.Other, out var прочее))
        {
            var разобранное = РазобратьПрочее(прочее);

            черновик.Phone = разобранное.Телефон;
            черновик.Email = разобранное.Почта;
            черновик.Note = разобранное.Примечание;
        }

        foreach (var дополнительное in дополнительные)
        {
            var значение = ЗначениеЯчейки(строка, дополнительное.ColumnIndex);

            if (string.IsNullOrWhiteSpace(значение))
            {
                continue;
            }

            switch (дополнительное.Field)
            {
                case ImportExtraField.Inn:
                    черновик.Inn = значение.Trim();
                    break;

                case ImportExtraField.PostalZip:
                    if (string.IsNullOrWhiteSpace(черновик.PostalZip))
                    {
                        черновик.PostalZip = значение.Trim();
                    }

                    break;

                case ImportExtraField.Phone:
                    if (string.IsNullOrWhiteSpace(черновик.Phone))
                    {
                        черновик.Phone = значение.Trim();
                    }

                    break;

                case ImportExtraField.Email:
                    if (string.IsNullOrWhiteSpace(черновик.Email))
                    {
                        черновик.Email = значение.Trim();
                    }

                    break;

                case ImportExtraField.Kpp:
                    if (string.IsNullOrWhiteSpace(черновик.Kpp))
                    {
                        черновик.Kpp = значение.Trim();
                    }

                    break;

                case ImportExtraField.Ogrn:
                    if (string.IsNullOrWhiteSpace(черновик.Ogrn))
                    {
                        черновик.Ogrn = значение.Trim();
                    }

                    break;

                case ImportExtraField.IsVip:
                    черновик.IsVip |= РазобратьПризнак(значение);
                    break;

                case ImportExtraField.Group:
                    if (черновик.GroupId is null
                        && long.TryParse(значение.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var группа))
                    {
                        черновик.GroupId = группа;
                    }

                    break;
            }
        }

        if (options.RecognizeAddresses && черновик.PostalZip.Length == 0 && почтовыйАдрес is not null)
        {
            черновик.NeedsVerification = true;
        }

        return черновик;
    }

    /// <summary>
    /// Разбирает адрес из значения слота адреса.
    /// </summary>
    /// <param name="значения">Значения слотов строки.</param>
    /// <param name="слот">Слот адреса.</param>
    /// <returns>Результат распознавания адреса либо <see langword="null"/>.</returns>
    private static AddressRecognitionResult? РазобратьАдрес(
        IReadOnlyDictionary<ImportFieldSlot, string> значения,
        ImportFieldSlot слот,
        bool распознавать,
        AddressHeuristicsService адреса)
    {
        if (!значения.TryGetValue(слот, out var текст) || string.IsNullOrWhiteSpace(текст))
        {
            return null;
        }

        if (!распознавать)
        {
            var строки = new string[6];
            строки[0] = текст.Trim();

            return new AddressRecognitionResult(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                строки,
                true);
        }

        // Текстовый снимок адреса (индекс и шесть строк, разделённых переводом
        // строки) восстанавливается без эвристик: так импорт собственной
        // выгрузки сохраняет строки адреса (ТЗ §3.1.19: «поле *_raw служит
        // только для round-trip импорта»).
        var снимок = РазобратьСнимокАдреса(текст);

        if (снимок is not null)
        {
            return снимок;
        }

        return адреса.RecognizeDetailed(текст);
    }

    /// <summary>
    /// Восстанавливает адрес из текстового снимка: индекс и шесть строк,
    /// разделённых переводом строки.
    /// </summary>
    /// <param name="текст">Исходное значение столбца адреса.</param>
    /// <returns>
    /// Результат восстановления адреса либо <see langword="null"/>, если
    /// значение не является снимком адреса.
    /// </returns>
    private static AddressRecognitionResult? РазобратьСнимокАдреса(string текст)
    {
        if (текст.Split('\n').Length != 7)
        {
            return null;
        }

        var разбор = Address.Parse(текст);

        if (!разбор.IsSuccess)
        {
            return null;
        }

        var адрес = разбор.Value;

        return new AddressRecognitionResult(
            адрес.Zip,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            адрес.Lines.ToArray(),
            false);
    }

    /// <summary>
    /// Разбирает свободный текст слота «Прочее» на телефон, почту и примечание.
    /// </summary>
    /// <param name="текст">Исходный текст.</param>
    /// <returns>Телефон, почта и примечание.</returns>
    private static (string? Телефон, string? Почта, string? Примечание) РазобратьПрочее(string текст)
    {
        string? телефон = null;
        string? почта = null;
        var остаток = текст;

        var совпадениеПочты = Regex.Match(остаток, @"[\w.\-+]+@[\w\-]+(\.[\w\-]+)+", RegexOptions.CultureInvariant);

        if (совпадениеПочты.Success)
        {
            почта = совпадениеПочты.Value;
            остаток = остаток.Remove(совпадениеПочты.Index, совпадениеПочты.Length);
        }

        var совпадениеТелефона = Regex.Match(
            остаток,
            @"(?<!\w)(?<phone>\+?\d[\d\s\-()]{4,}\d)(?!\w)",
            RegexOptions.CultureInvariant);

        if (совпадениеТелефона.Success)
        {
            телефон = совпадениеТелефона.Groups["phone"].Value.Trim();
            остаток = остаток.Remove(совпадениеТелефона.Index, совпадениеТелефона.Length);
        }

        var примечание = остаток.Replace(';', ' ').Trim();

        if (примечание.Length == 0)
        {
            примечание = null;
        }

        return (телефон, почта, примечание);
    }

    /// <summary>
    /// Разбирает признак «избранный адрес» из значения столбца.
    /// </summary>
    /// <param name="значение">Исходное значение столбца.</param>
    /// <returns><see langword="true"/>, если признак установлен.</returns>
    private static bool РазобратьПризнак(string значение)
    {
        var текст = значение.Trim().ToLowerInvariant();

        return текст is "да" or "да*" or "истина" or "true" or "1" or "vip" or "избранное";
    }

    /// <summary>
    /// Возвращает значение ячейки строки по номеру столбца.
    /// </summary>
    /// <param name="строка">Строка файла.</param>
    /// <param name="индекс">Номер столбца (нумерация с нуля).</param>
    /// <returns>Значение ячейки либо пустая строка.</returns>
    private static string ЗначениеЯчейки(DelimitedTextRow строка, int индекс)
        => индекс >= 0 && индекс < строка.Cells.Count ? строка.Cells[индекс] ?? string.Empty : string.Empty;
}

/// <summary>
/// Чтение книги XLSX без внешних зависимостей: книга распаковывается как
/// архив, лист выбирается по имени либо номеру, значения ячеек читаются из
/// разметки таблицы (ТЗ §8.12, функция 159: «XLSX → CSV/HTML»).
/// </summary>
internal static class КнигаXlsx
{
    /// <summary>
    /// Читает лист книги и возвращает его как таблицу строк.
    /// </summary>
    /// <param name="путь">Путь к книге.</param>
    /// <param name="имяЛиста">Имя листа; пустое значение — первый лист.</param>
    /// <param name="номерЛиста">Номер листа (нумерация с единицы).</param>
    /// <param name="начало">Номер первой строки данных (нумерация с единицы).</param>
    /// <param name="естьЗаголовки">Признак наличия строки заголовков.</param>
    /// <returns>Таблица строк либо <see langword="null"/>, если лист не найден.</returns>
    public static DelimitedTextTable? Прочитать(
        string путь,
        string? имяЛиста,
        int? номерЛиста,
        int начало = 1,
        bool естьЗаголовки = false)
    {
        using var архив = ZipFile.OpenRead(путь);

        var общиеСтроки = ПрочитатьОбщиеСтроки(архив);
        var путьЛиста = НайтиЛист(архив, имяЛиста, номерЛиста);

        if (путьЛиста is null)
        {
            return null;
        }

        var запись = архив.GetEntry(путьЛиста);

        if (запись is null)
        {
            return null;
        }

        using var поток = запись.Open();
        var документ = XDocument.Load(поток);

        var строки = new List<DelimitedTextRow>();
        var порядковыйНомер = 0;

        foreach (var строка in документ.Descendants().Where(элемент => элемент.Name.LocalName == "row"))
        {
            порядковыйНомер++;

            var номер = int.TryParse(строка.Attribute("r")?.Value, out var значениеНомера)
                ? значениеНомера
                : порядковыйНомер;

            var ячейки = new List<string>();

            foreach (var ячейка in строка.Elements().Where(элемент => элемент.Name.LocalName == "c"))
            {
                var индекс = ИндексКолонки(ячейка.Attribute("r")?.Value);
                var тип = ячейка.Attribute("t")?.Value;
                var значение = string.Empty;

                if (string.Equals(тип, "s", StringComparison.Ordinal))
                {
                    var сырое = ячейка.Elements().FirstOrDefault(элемент => элемент.Name.LocalName == "v")?.Value;

                    if (int.TryParse(сырое, out var номерСтроки)
                        && номерСтроки >= 0
                        && номерСтроки < общиеСтроки.Count)
                    {
                        значение = общиеСтроки[номерСтроки];
                    }
                }
                else if (string.Equals(тип, "inlineStr", StringComparison.Ordinal))
                {
                    значение = string.Concat(ячейка.Descendants()
                        .Where(элемент => элемент.Name.LocalName == "t")
                        .Select(элемент => элемент.Value));
                }
                else
                {
                    значение = ячейка.Elements().FirstOrDefault(элемент => элемент.Name.LocalName == "v")?.Value
                        ?? string.Empty;
                }

                while (ячейки.Count <= индекс)
                {
                    ячейки.Add(string.Empty);
                }

                ячейки[индекс] = значение;
            }

            строки.Add(new DelimitedTextRow(номер, ячейки));
        }

        // Отбор строк данных с учётом параметра «Начать импорт с» и строки
        // заголовков — так же, как при чтении CSV и HTML.
        var началоДанных = Math.Max(1, начало);
        var заголовки = Array.Empty<string>();
        var данные = строки.Where(строка => строка.RowNumber >= началоДанных).ToList();

        if (естьЗаголовки && строки.Count > 0)
        {
            // Строка заголовков — первая строка листа: «Начать импорт с» задаёт
            // первую строку данных, а не заголовка.
            var строкаЗаголовка = строки[0];

            заголовки = строкаЗаголовка.Cells.ToArray();

            if (данные.Count > 0 && данные[0].RowNumber <= строкаЗаголовка.RowNumber)
            {
                данные.RemoveAt(0);
            }
        }

        return new DelimitedTextTable(заголовки, данные);
    }

    /// <summary>
    /// Читает общие строки книги.
    /// </summary>
    /// <param name="архив">Архив книги.</param>
    /// <returns>Список общих строк.</returns>
    private static List<string> ПрочитатьОбщиеСтроки(ZipArchive архив)
    {
        var строки = new List<string>();
        var запись = архив.GetEntry("xl/sharedStrings.xml");

        if (запись is null)
        {
            return строки;
        }

        using var поток = запись.Open();
        var документ = XDocument.Load(поток);

        foreach (var элемент in документ.Descendants().Where(элемент => элемент.Name.LocalName == "si"))
        {
            строки.Add(string.Concat(элемент.Descendants()
                .Where(вложенный => вложенный.Name.LocalName == "t")
                .Select(вложенный => вложенный.Value)));
        }

        return строки;
    }

    /// <summary>
    /// Определяет путь к листу книги по имени или номеру.
    /// </summary>
    /// <param name="архив">Архив книги.</param>
    /// <param name="имяЛиста">Имя листа.</param>
    /// <param name="номерЛиста">Номер листа (нумерация с единицы).</param>
    /// <returns>Путь к листу внутри архива либо <see langword="null"/>.</returns>
    private static string? НайтиЛист(ZipArchive архив, string? имяЛиста, int? номерЛиста)
    {
        var записьКниги = архив.GetEntry("xl/workbook.xml");

        if (записьКниги is null)
        {
            return null;
        }

        List<XElement> листы;

        using (var поток = записьКниги.Open())
        {
            var документ = XDocument.Load(поток);
            листы = документ.Descendants().Where(элемент => элемент.Name.LocalName == "sheet").ToList();
        }

        if (листы.Count == 0)
        {
            return null;
        }

        XElement? выбранный = null;

        if (!string.IsNullOrWhiteSpace(имяЛиста))
        {
            выбранный = листы.FirstOrDefault(лист =>
                string.Equals(лист.Attribute("name")?.Value, имяЛиста.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        else if (номерЛиста is { } номер && номер > 0 && номер <= листы.Count)
        {
            выбранный = листы[номер - 1];
        }
        else
        {
            выбранный = листы[0];
        }

        if (выбранный is null)
        {
            return null;
        }

        var идентификатор = выбранный.Attribute("id")?.Value
            ?? выбранный.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;

        var целевойПуть = НайтиЦелевойПуть(архив, идентификатор);

        return целевойПуть ?? НайтиПервыйЛист(архив);
    }

    /// <summary>
    /// Ищет путь к листу по идентификатору связи книги.
    /// </summary>
    /// <param name="архив">Архив книги.</param>
    /// <param name="идентификатор">Идентификатор связи.</param>
    /// <returns>Путь к листу либо <see langword="null"/>.</returns>
    private static string? НайтиЦелевойПуть(ZipArchive архив, string? идентификатор)
    {
        if (string.IsNullOrWhiteSpace(идентификатор))
        {
            return null;
        }

        var запись = архив.GetEntry("xl/_rels/workbook.xml.rels");

        if (запись is null)
        {
            return null;
        }

        using var поток = запись.Open();
        var документ = XDocument.Load(поток);

        var связь = документ.Descendants()
            .FirstOrDefault(элемент => элемент.Name.LocalName == "Relationship"
                && string.Equals(элемент.Attribute("Id")?.Value, идентификатор, StringComparison.Ordinal));

        var цель = связь?.Attribute("Target")?.Value;

        if (string.IsNullOrWhiteSpace(цель))
        {
            return null;
        }

        return цель.StartsWith('/') ? цель.TrimStart('/') : $"xl/{цель}";
    }

    /// <summary>
    /// Ищет первый лист книги по имени файла.
    /// </summary>
    /// <param name="архив">Архив книги.</param>
    /// <returns>Путь к листу либо <see langword="null"/>.</returns>
    private static string? НайтиПервыйЛист(ZipArchive архив)
        => архив.Entries
            .Where(запись => запись.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)
                && запись.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(запись => запись.FullName, StringComparer.Ordinal)
            .Select(запись => запись.FullName)
            .FirstOrDefault();

    /// <summary>
    /// Вычисляет номер столбца по ссылке ячейки («B3» → 1).
    /// </summary>
    /// <param name="ссылка">Ссылка на ячейку.</param>
    /// <returns>Номер столбца (нумерация с нуля).</returns>
    private static int ИндексКолонки(string? ссылка)
    {
        if (string.IsNullOrWhiteSpace(ссылка))
        {
            return 0;
        }

        var индекс = 0;

        foreach (var символ in ссылка)
        {
            if (!char.IsAsciiLetter(символ))
            {
                break;
            }

            индекс = (индекс * 26) + (char.ToUpperInvariant(символ) - 'A' + 1);
        }

        return Math.Max(0, индекс - 1);
    }
}

/// <summary>
/// Чтение таблицы из HTML-файла — формат, в который предлагается сохранить
/// книгу XLSX, если её не удалось разобрать (ТЗ §8.12, функция 159:
/// «XLSX → CSV/HTML»).
/// </summary>
internal static class HtmlTableReader
{
    /// <summary>
    /// Читает первую таблицу HTML-файла.
    /// </summary>
    /// <param name="html">Содержимое файла.</param>
    /// <param name="естьЗаголовки">Признак наличия строки заголовков.</param>
    /// <param name="начало">Номер первой строки данных (нумерация с единицы).</param>
    /// <returns>Таблица строк либо <see langword="null"/>, если таблица не найдена.</returns>
    public static DelimitedTextTable? Прочитать(string html, bool естьЗаголовки, int начало)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var совпадениеТаблицы = Regex.Match(
            html,
            @"<table[^>]*>(.*?)</table>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!совпадениеТаблицы.Success)
        {
            return null;
        }

        var строкиHtml = Regex.Matches(
            совпадениеТаблицы.Groups[1].Value,
            @"<tr[^>]*>(.*?)</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var строки = new List<DelimitedTextRow>();
        var номер = 0;

        foreach (Match строкаHtml in строкиHtml)
        {
            номер++;

            var ячейкиHtml = Regex.Matches(
                строкаHtml.Groups[1].Value,
                @"<(td|th)[^>]*>(.*?)</\1>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var ячейки = new List<string>();

            foreach (Match ячейкаHtml in ячейкиHtml)
            {
                var текст = Regex.Replace(ячейкаHtml.Groups[2].Value, @"<[^>]+>", " ");
                ячейки.Add(РаскодироватьСущности(СхлопнутьПробелы(текст)));
            }

            строки.Add(new DelimitedTextRow(номер, ячейки));
        }

        if (строки.Count == 0)
        {
            return null;
        }

        var началоДанных = Math.Max(1, начало);
        var заголовки = Array.Empty<string>();
        var данные = строки.Where(строка => строка.RowNumber >= началоДанных).ToList();

        if (естьЗаголовки && строки.Count > 0)
        {
            // Строка заголовков — первая строка таблицы: «Начать импорт с»
            // задаёт первую строку данных, а не заголовка.
            var строкаЗаголовка = строки[0];

            заголовки = строкаЗаголовка.Cells.ToArray();

            if (данные.Count > 0 && данные[0].RowNumber <= строкаЗаголовка.RowNumber)
            {
                данные.RemoveAt(0);
            }
        }

        return new DelimitedTextTable(заголовки, данные);
    }

    /// <summary>
    /// Схлопывает повторяющиеся пробелы и обрезает края текста.
    /// </summary>
    /// <param name="текст">Исходный текст.</param>
    /// <returns>Нормализованный текст.</returns>
    private static string СхлопнутьПробелы(string текст)
    {
        var части = текст.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', части).Trim();
    }

    /// <summary>
    /// Раскодирует HTML-сущности текста; «&amp;amp;» — последним (F-D13):
    /// прежде раннее декодирование «&amp;amp;» порождало новые сущности
    /// («&amp;amp;lt;» → «&amp;lt;»), которые оставались в тексте.
    /// </summary>
    /// <param name="текст">Исходный текст.</param>
    /// <returns>Текст с раскодированными сущностями.</returns>
    private static string РаскодироватьСущности(string текст) => текст
        .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
        .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
        .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
        .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
        .Replace("&#39;", "'", StringComparison.OrdinalIgnoreCase)
        .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);
}
