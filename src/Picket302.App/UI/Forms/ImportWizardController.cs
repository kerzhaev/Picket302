using System.IO.Compression;
using System.Xml.Linq;
using Picket302.Application.Import;
using Picket302.Application.Services;
using Picket302.App.Shared;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Результат применения правки записи в пошаговом предпросмотре
/// (ТЗ §8.12, функция 159, шаг 4).
/// </summary>
public enum ImportEditResult
{
    /// <summary>Правка применена, запись перепроверена.</summary>
    Edited = 0,

    /// <summary>В правке стоит знак «&lt;&lt; &gt;&gt;» — запись пропущена.</summary>
    SkippedByMarker = 1,

    /// <summary>Правка не применена: нет выбранной записи.</summary>
    NoRecord = 2,
}

/// <summary>
/// Логика шагов мастера импорта адресной книги (ТЗ §8.12, функция 159):
/// сессия импорта, текущий шаг, ответы оператора и переходы между шагами.
/// </summary>
/// <remarks>
/// <para>
/// Контроллер не зависит от окон: он проверяется модульными тестами без
/// создания формы, а окно мастера только отображает его состояние.
/// </para>
/// <para>
/// Отказ до начала импорта (ТЗ §8.12, функция 159: «файл поля “слит” без
/// разделителей — предупреждение о невозможности корректной загрузки до
/// начала процесса») выражается в <see cref="ErrorMessage"/>: сессия не
/// создаётся, шаг остаётся первым.
/// </para>
/// </remarks>
public sealed class ImportWizardController
{
    /// <summary>Знак пропуска записи оператором (ТЗ §8.12, функция 159, шаг 4).</summary>
    public const string SkipMarker = "<<";

    /// <summary>Знак пропуска записи, альтернативный вариант.</summary>
    public const string SkipMarkerAlternative = ">>";

    /// <summary>Имя каталога файлов профилей внутри каталога приложения.</summary>
    public const string ProfilesDirectoryName = "profiles";

    private readonly ICounterpartyRepository? _repository;
    private readonly IGroupRepository? _groups;
    private readonly IImportMerger? _слиятель;
    private readonly IDataGateway? _шлюз;
    private readonly ICsvProfileStore _profileStore;

    private readonly Dictionary<int, ImportFieldSlot?> _overrides = [];
    private readonly Dictionary<int, ImportRecordStatus> _decisions = [];

    private ImportSession? _session;
    private string _filePath = string.Empty;
    private string? _sheetName;
    private int? _sheetIndex;
    private int _sheetCount;
    private int _startRow = 1;
    private bool _hasHeaderRow = true;
    private string? _encodingName;
    private char? _delimiter;
    private long? _groupId;
    private bool _needsReload;
    private bool _mappingSet;
    private int _currentRecord;

    /// <summary>
    /// Создаёт контроллер мастера импорта.
    /// </summary>
    /// <param name="repository">
    /// Репозиторий контрагентов для финального шага импорта; при отсутствии
    /// импорт не может быть применён, а мастер сообщает об этом.
    /// </param>
    /// <param name="groups">
    /// Репозиторий групп для выбора категории адресной книги на шаге 2.
    /// </param>
    /// <param name="profileStore">
    /// Хранилище файлов профилей сопоставления (*.csvprof); при отсутствии
    /// используется хранилище по умолчанию.
    /// </param>
    /// <param name="слиятель">
    /// Слиятель пакета импорта; задаётся в совместном режиме — слияние
    /// выполняет серверная служба.
    /// </param>
    /// <param name="шлюз">
    /// Шлюз данных рабочего места; задаётся в совместном режиме — список и
    /// создание категорий адресной книги идут через общую базу.
    /// </param>
    public ImportWizardController(
        ICounterpartyRepository? repository = null,
        IGroupRepository? groups = null,
        ICsvProfileStore? profileStore = null,
        IImportMerger? слиятель = null,
        IDataGateway? шлюз = null)
    {
        _repository = repository;
        _groups = groups;
        _profileStore = profileStore ?? CreateProfileStore();
        _слиятель = слиятель;
        _шлюз = шлюз;
    }

    /// <summary>Текущий шаг мастера.</summary>
    public ImportWizardStep CurrentStep { get; private set; } = ImportWizardStep.File;

    /// <summary>Номер текущего шага (нумерация с единицы).</summary>
    public int StepNumber => (int)CurrentStep;

    /// <summary>Общее число шагов мастера.</summary>
    public int TotalSteps => ImportWizardSteps.TotalSteps;

    /// <summary>
    /// Текст последней ошибки: отказ при чтении файла, невозможность
    /// применить импорт и ошибки профиля сопоставления.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Путь к импортируемому файлу.</summary>
    public string FilePath => _filePath;

    /// <summary>Имя выбранного листа книги (для файлов XLSX).</summary>
    public string? SheetName => _sheetName;

    /// <summary>Номер выбранного листа книги (нумерация с единицы).</summary>
    public int? SheetIndex => _sheetIndex;

    /// <summary>
    /// Число листов книги XLSX (для прочих форматов — ноль)
    /// (ТЗ §8.12, функция 159, шаг 1: «выбор файла и листа (показ
    /// “Количество листов/записей/столбцов”)»).
    /// </summary>
    public int SheetCount => _sheetCount;

    /// <summary>Номер строки файла, с которой начинается импорт.</summary>
    public int StartRow => _startRow;

    /// <summary>Признак наличия строки заголовков в файле.</summary>
    public bool HasHeaderRow => _hasHeaderRow;

    /// <summary>Идентификатор категории адресной книги, в которую выполняется импорт.</summary>
    public long? GroupId => _groupId;

    /// <summary>Политика слияния импортируемых записей с существующей базой.</summary>
    public MergePolicy MergePolicy { get; set; } = MergePolicy.SkipExisting;

    /// <summary>Политика применения импорта, выбранная на финальном шаге.</summary>
    public ImportPolicy ImportPolicy { get; set; } = ImportPolicy.VerifiedOnly;

    /// <summary>Фильтр пошагового предпросмотра.</summary>
    public ImportRecordFilter Filter { get; private set; } = ImportRecordFilter.All;

    /// <summary>Сессия импорта; <see langword="null"/> — файл ещё не прочитан.</summary>
    public ImportSession? Session => _session;

    /// <summary>Признак наличия прочитанной сессии импорта.</summary>
    public bool HasSession => _session is not null;

    /// <summary>Записи пакета импорта.</summary>
    public IReadOnlyList<ImportRecord> Records => _session?.Records ?? [];

    /// <summary>Заголовки столбцов файла.</summary>
    public IReadOnlyList<string> ColumnNames => _session?.ColumnNames ?? [];

    /// <summary>Предупреждения, возникшие при чтении файла.</summary>
    public IReadOnlyList<string> FileWarnings => _session?.Warnings ?? [];

    /// <summary>Число записей в прочитанном файле.</summary>
    public int RecordCount => Records.Count;

    /// <summary>Число столбцов в прочитанном файле.</summary>
    public int ColumnCount => ColumnNames.Count;

    /// <summary>
    /// Записи, отображаемые в пошаговом предпросмотре с учётом фильтра
    /// (ТЗ §8.12, функция 159, шаг 5).
    /// </summary>
    public IReadOnlyList<ImportRecord> FilteredRecords
        => _session?.GetRecords(Filter == ImportRecordFilter.WithErrors) ?? [];

    /// <summary>Номер текущей записи предпросмотра (нумерация с единицы).</summary>
    public int CurrentIndex => _currentRecord + 1;

    /// <summary>Текущая запись предпросмотра.</summary>
    public ImportRecord? CurrentRecord
    {
        get
        {
            var records = FilteredRecords;

            if (_currentRecord < 0 || _currentRecord >= records.Count)
            {
                return null;
            }

            return records[_currentRecord];
        }
    }

    /// <summary>
    /// Сводка проверки пакета: числа проверенных, непроверенных, ошибочных
    /// и пропущенных записей (ТЗ §8.12, функция 159, шаг 6).
    /// </summary>
    public ValidationSummary Summary => new(Records);

    /// <summary>Число непроверенных записей.</summary>
    public int UnverifiedCount => Summary.UnverifiedCount;

    /// <summary>Число записей с ошибками проверки.</summary>
    public int ErrorCount => Summary.ErrorCount;

    /// <summary>Число проверенных записей.</summary>
    public int VerifiedCount => Summary.VerifiedCount;

    /// <summary>Число пропущенных записей.</summary>
    public int SkippedCount => Summary.SkippedCount;

    /// <summary>Итог последнего применения импорта.</summary>
    public ImportResult? Result { get; private set; }

    /// <summary>
    /// Читает файл импорта и создаёт сессию мастера.
    /// </summary>
    /// <param name="filePath">Путь к импортируемому файлу.</param>
    /// <param name="sheetName">Имя листа книги (для файлов XLSX).</param>
    /// <param name="sheetIndex">Номер листа книги (для файлов XLSX).</param>
    /// <returns>
    /// <see langword="true"/> — файл прочитан; <see langword="false"/> — чтение
    /// не удалось, текст отказа находится в <see cref="ErrorMessage"/>.
    /// </returns>
    public bool LoadFile(string filePath, string? sheetName = null, int? sheetIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _sheetName = sheetName;
        _sheetIndex = sheetIndex;
        _sheetCount = ReadSheetNames(filePath).Count;
        _overrides.Clear();
        _decisions.Clear();
        _mappingSet = false;
        _needsReload = false;
        _currentRecord = 0;

        return ReloadSession();
    }

    /// <summary>
    /// Читает выгрузку 1С и создаёт сессию мастера: сопоставление столбцов для
    /// выгрузки 1С не выполняется, поля уже приведены к виду черновика
    /// (ТЗ §8.5, функция 72).
    /// </summary>
    /// <param name="filePath">Путь к файлу выгрузки 1С.</param>
    /// <param name="version">Версия выгрузки 1С.</param>
    /// <returns>
    /// <see langword="true"/> — выгрузка прочитана; <see langword="false"/> —
    /// чтение не удалось, текст отказа находится в <see cref="ErrorMessage"/>.
    /// </returns>
    public bool LoadOneC(string filePath, OneCVersion version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        ImportBatchResult parsed;

        try
        {
            parsed = new OneCImportParser().ImportFromOneC(filePath, version);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            _session = null;
            ErrorMessage = $"Выгрузка 1С не прочитана: {error.Message}";
            return false;
        }

        _filePath = filePath;
        _sheetName = null;
        _sheetIndex = null;
        _sheetCount = 0;
        _overrides.Clear();
        _decisions.Clear();
        _mappingSet = false;
        _needsReload = false;
        _currentRecord = 0;

        _session = new ImportSession(
            parsed.Batch,
            [],
            [],
            [],
            parsed.Warnings,
            _repository,
            _groupId,
            MergePolicy);

        _session.ValidateAll();
        ErrorMessage = null;

        return true;
    }

    /// <summary>
    /// Загружает категории адресной книги для выбора на шаге 2
    /// (ТЗ §8.12, функция 159, шаг 2: «выбор категории назначения»).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список групп; пустой список, если ни репозиторий, ни шлюз не заданы.</returns>
    public async Task<IReadOnlyList<Group>> LoadCategoriesAsync(CancellationToken cancellationToken = default)
    {
        if (_шлюз is not null)
        {
            return await _шлюз.ПолучитьГруппыAsync(cancellationToken).ConfigureAwait(false);
        }

        return _groups is null
            ? []
            : await _groups.GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Создаёт новую категорию адресной книги и назначает её текущей
    /// категорией импорта (ТЗ §8.12, функция 159, шаг 2: «выбор категории
    /// назначения или создание новой»).
    /// </summary>
    /// <param name="название">Наименование создаваемой категории.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданная категория либо <see langword="null"/>, если
    /// репозиторий групп не задан либо имя пустое.</returns>
    public async Task<Group?> CreateCategoryAsync(string название, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(название))
        {
            return null;
        }

        if (_шлюз is not null)
        {
            var результат = await _шлюз
                .СоздатьКатегориюAsync(название.Trim(), null, cancellationToken)
                .ConfigureAwait(false);

            if (результат.IsFailure)
            {
                ErrorMessage = результат.Error;
                return null;
            }

            var категория = результат.Value;

            SetGroupId(категория.Id);

            return new Group
            {
                Id = категория.Id,
                Name = категория.Name,
                ParentId = категория.ParentId,
                IsDefault = категория.IsDefault,
                IsClosed = категория.IsClosed,
                SortOrder = категория.SortOrder,
            };
        }

        if (_groups is null)
        {
            return null;
        }

        var группа = new Group { Name = название.Trim() };

        var идентификатор = await _groups.AddAsync(группа, cancellationToken).ConfigureAwait(false);

        группа.Id = идентификатор;

        SetGroupId(идентификатор);

        return группа;
    }

    /// <summary>
    /// Задаёт номер строки файла, с которой начинается импорт
    /// (ТЗ §8.12, функция 159, шаг 2: «“Начать импорт с” N-й строки»).
    /// </summary>
    /// <param name="row">Номер строки (нумерация с единицы).</param>
    public void SetStartRow(int row)
    {
        var value = Math.Max(1, row);

        if (_startRow == value)
        {
            return;
        }

        _startRow = value;
        _needsReload = true;
    }

    /// <summary>
    /// Задаёт наличие строки заголовков в файле.
    /// </summary>
    /// <param name="value">Признак наличия строки заголовков.</param>
    public void SetHasHeaderRow(bool value)
    {
        if (_hasHeaderRow == value)
        {
            return;
        }

        _hasHeaderRow = value;
        _needsReload = true;
    }

    /// <summary>
    /// Задаёт кодировку файла импорта; пустое значение означает автоопределение.
    /// </summary>
    /// <param name="encodingName">Имя кодировки.</param>
    public void SetEncoding(string? encodingName)
    {
        if (string.Equals(encodingName, _encodingName, StringComparison.Ordinal))
        {
            return;
        }

        _encodingName = encodingName;
        _needsReload = true;
    }

    /// <summary>
    /// Задаёт разделитель столбцов; пустое значение означает автоопределение.
    /// </summary>
    /// <param name="delimiter">Разделитель столбцов.</param>
    public void SetDelimiter(char? delimiter)
    {
        if (_delimiter == delimiter)
        {
            return;
        }

        _delimiter = delimiter;
        _needsReload = true;
    }

    /// <summary>
    /// Задаёт категорию адресной книги, в которую выполняется импорт
    /// (ТЗ §8.12, функция 159, шаг 2).
    /// </summary>
    /// <param name="groupId">Идентификатор группы либо <see langword="null"/>.</param>
    public void SetGroupId(long? groupId)
    {
        if (_groupId == groupId)
        {
            return;
        }

        _groupId = groupId;
        _session?.SetGroupId(groupId);
    }

    /// <summary>
    /// Возвращает слот поля, назначенный столбцу файла
    /// (ТЗ §8.12, функция 159, шаг 3: сопоставление по столбцам).
    /// </summary>
    /// <param name="columnIndex">Номер столбца файла (нумерация с нуля).</param>
    /// <returns>Слот поля либо <see langword="null"/>, если столбец не сопоставлен.</returns>
    public ImportFieldSlot? GetSlot(int columnIndex)
    {
        if (_overrides.TryGetValue(columnIndex, out var slot))
        {
            return slot;
        }

        return AutoSlot(columnIndex);
    }

    /// <summary>
    /// Назначает столбцу файла слот поля; слот, назначенный другому столбцу,
    /// освобождается, иначе поле осталось бы сопоставлено дважды.
    /// </summary>
    /// <param name="columnIndex">Номер столбца файла (нумерация с нуля).</param>
    /// <param name="slot">Слот поля либо <see langword="null"/> — столбец не сопоставлен.</param>
    public void SetSlot(int columnIndex, ImportFieldSlot? slot)
    {
        if (slot is not null)
        {
            for (var index = 0; index < ColumnNames.Count; index++)
            {
                if (index != columnIndex && GetSlot(index) == slot)
                {
                    _overrides[index] = null;
                }
            }
        }

        _overrides[columnIndex] = slot;
        _mappingSet = true;
        _needsReload = true;
    }

    /// <summary>
    /// Возвращает сопоставление столбцов файла со слотами полей
    /// (ТЗ §8.12, функция 159, шаг 3).
    /// </summary>
    public IReadOnlyList<ImportColumnBinding> GetBindings()
    {
        var bindings = new List<ImportColumnBinding>();

        for (var index = 0; index < ColumnNames.Count; index++)
        {
            bindings.Add(new ImportColumnBinding(index, GetSlot(index), ColumnNames[index]));
        }

        return bindings;
    }

    /// <summary>
    /// Формирует профиль сопоставления для сохранения в файл *.csvprof
    /// (ТЗ §8.12, функция 73).
    /// </summary>
    /// <returns>Профиль сопоставления.</returns>
    public CsvProfile BuildProfile() => new()
    {
        Name = string.IsNullOrWhiteSpace(_filePath)
            ? "Профиль импорта"
            : Path.GetFileNameWithoutExtension(_filePath),
        EncodingName = string.IsNullOrWhiteSpace(_encodingName) ? "utf-8" : _encodingName,
        Delimiter = _delimiter ?? ',',
        QuoteChar = CsvProfileService.СимволКавычкиПоУмолчанию,
        HasHeaderRow = _hasHeaderRow,
        FirstDataRow = _startRow,
        Bindings = [.. GetBindings()],
    };

    /// <summary>
    /// Сохраняет профиль сопоставления в файл *.csvprof
    /// (ТЗ §8.12, функция 73: «Данные: Файлы конфигурации *.csvprof»).
    /// </summary>
    /// <param name="filePath">Путь к файлу профиля.</param>
    /// <returns><see langword="true"/> — профиль сохранён.</returns>
    public bool SaveProfile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            _profileStore.Save(BuildProfile(), filePath);
            ErrorMessage = null;
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            ErrorMessage = $"Профиль сопоставления не сохранён: {error.Message}";
            return false;
        }
    }

    /// <summary>
    /// Загружает профиль сопоставления из файла *.csvprof и применяет его
    /// параметры: разделитель, кодировку, начало данных и сопоставление столбцов.
    /// </summary>
    /// <param name="filePath">Путь к файлу профиля.</param>
    /// <returns><see langword="true"/> — профиль загружен.</returns>
    public bool LoadProfile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        CsvProfile profile;

        try
        {
            profile = _profileStore.Load(filePath);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            ErrorMessage = $"Профиль сопоставления не загружен: {error.Message}";
            return false;
        }

        _encodingName = profile.EncodingName;
        _delimiter = profile.Delimiter;
        _hasHeaderRow = profile.HasHeaderRow;
        _startRow = Math.Max(1, profile.FirstDataRow);

        _overrides.Clear();

        foreach (var binding in profile.Bindings)
        {
            if (binding.ColumnIndex < 0)
            {
                continue;
            }

            _overrides[binding.ColumnIndex] = binding.Slot;
        }

        _mappingSet = true;
        _needsReload = true;
        ErrorMessage = null;

        return true;
    }

    /// <summary>
    /// Задаёт фильтр пошагового предпросмотра
    /// (ТЗ §8.12, функция 159, шаг 5).
    /// </summary>
    /// <param name="filter">Фильтр предпросмотра.</param>
    public void SetFilter(ImportRecordFilter filter)
    {
        Filter = filter;
        _currentRecord = 0;
    }

    /// <summary>
    /// Переходит к следующей записи предпросмотра.
    /// </summary>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool MoveNextRecord()
    {
        if (_currentRecord + 1 >= FilteredRecords.Count)
        {
            return false;
        }

        _currentRecord++;
        return true;
    }

    /// <summary>
    /// Переходит к предыдущей записи предпросмотра.
    /// </summary>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool MovePreviousRecord()
    {
        if (_currentRecord <= 0)
        {
            return false;
        }

        _currentRecord--;
        return true;
    }

    /// <summary>
    /// Отмечает текущую запись как проверенную оператором
    /// (ТЗ §8.12, функция 159, шаг 4).
    /// </summary>
    /// <returns><see langword="true"/> — запись отмечена.</returns>
    public bool MarkCurrentVerified()
    {
        var record = CurrentRecord;

        if (record is null || _session is null)
        {
            return false;
        }

        _session.MarkVerified(record);
        _decisions[record.RowNumber] = ImportRecordStatus.Verified;
        return true;
    }

    /// <summary>
    /// Пропускает текущую запись знаком «&lt;&lt; &gt;&gt;»
    /// (ТЗ §8.12, функция 159, шаг 4).
    /// </summary>
    /// <returns><see langword="true"/> — запись пропущена.</returns>
    public bool MarkCurrentSkipped()
    {
        var record = CurrentRecord;

        if (record is null || _session is null)
        {
            return false;
        }

        _session.MarkSkipped(record);
        _decisions[record.RowNumber] = ImportRecordStatus.Skipped;
        return true;
    }

    /// <summary>
    /// Снимает отметку проверки или пропуска текущей записи.
    /// </summary>
    /// <returns><see langword="true"/> — отметка снята.</returns>
    public bool MarkCurrentUnverified()
    {
        var record = CurrentRecord;

        if (record is null || _session is null)
        {
            return false;
        }

        _session.MarkUnverified(record);
        _decisions.Remove(record.RowNumber);
        return true;
    }

    /// <summary>
    /// Применяет правку оператора к текущей записи предпросмотра и перепроверяет
    /// её; правка со знаком «&lt;&lt; &gt;&gt;» пропускает запись.
    /// </summary>
    /// <param name="draft">Исправленные значения записи.</param>
    /// <returns>Результат применения правки.</returns>
    public ImportEditResult ApplyCurrentEdit(CounterpartyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var record = CurrentRecord;

        if (record is null || _session is null)
        {
            return ImportEditResult.NoRecord;
        }

        if (ContainsSkipMarker(draft))
        {
            _session.MarkSkipped(record);
            _decisions[record.RowNumber] = ImportRecordStatus.Skipped;
            return ImportEditResult.SkippedByMarker;
        }

        record.Draft = draft;
        _session.UpdateRecord(record);
        _decisions.Remove(record.RowNumber);

        return ImportEditResult.Edited;
    }

    /// <summary>
    /// Проверяет, содержит ли черновик знак пропуска «&lt;&lt; &gt;&gt;».
    /// </summary>
    /// <param name="draft">Проверяемый черновик.</param>
    /// <returns><see langword="true"/> — знак пропуска присутствует.</returns>
    public static bool ContainsSkipMarker(CounterpartyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return IsSkipMarker(draft.Name)
            || IsSkipMarker(draft.ShortName)
            || IsSkipMarker(draft.Note)
            || IsSkipMarker(draft.Phone)
            || IsSkipMarker(draft.Email)
            || IsSkipMarker(draft.Inn)
            || IsSkipMarker(draft.PostalZip)
            || draft.PostalLines.Any(IsSkipMarker);
    }

    /// <summary>
    /// Проверяет, является ли значение знаком пропуска «&lt;&lt; &gt;&gt;».
    /// </summary>
    /// <param name="value">Проверяемое значение.</param>
    /// <returns><see langword="true"/> — значение является знаком пропуска.</returns>
    public static bool IsSkipMarker(string? value)
    {
        var text = value?.Trim();

        return string.Equals(text, SkipMarker, StringComparison.Ordinal)
            || string.Equals(text, SkipMarkerAlternative, StringComparison.Ordinal);
    }

    /// <summary>
    /// Возвращает на шаг предпросмотра, сохраняя открытую сессию импорта
    /// (ТЗ §8.12, функция 159, шаг 6: «четвёртый выход “Вернуться к
    /// редактированию”»).
    /// </summary>
    /// <returns><see langword="true"/> — возврат выполнен.</returns>
    public bool ReturnToEditing()
    {
        if (_session is null)
        {
            ErrorMessage = "Возврат к редактированию невозможен: файл импорта не прочитан.";
            return false;
        }

        CurrentStep = ImportWizardStep.RecordPreview;
        _currentRecord = 0;
        return true;
    }

    /// <summary>
    /// Применяет импорт по выбранной политике и возвращает итоговые счётчики
    /// (ТЗ §8.12, функция 159, шаг 7).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Итог импорта либо <see langword="null"/> — импорт не применён.</returns>
    /// <remarks>
    /// Применение асинхронное: в совместном режиме слияние выполняет серверная
    /// служба, и сетевой запрос не должен выполняться блокирующим вызовом из
    /// потока интерфейса.
    /// </remarks>
    public async Task<ImportResult?> CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            ErrorMessage = "Импорт не применён: файл импорта не прочитан.";
            return null;
        }

        try
        {
            _session.SetMergePolicy(MergePolicy);
            Result = await _session
                .CommitAsync(ImportPolicy, MergePolicy, cancellationToken)
                .ConfigureAwait(true);
            ErrorMessage = null;
            return Result;
        }
        catch (InvalidOperationException error)
        {
            ErrorMessage = error.Message;
            return null;
        }
    }

    /// <summary>
    /// Переходит к следующему шагу мастера; при переходе применяются ответы
    /// оператора (номер строки начала импорта и сопоставление столбцов).
    /// </summary>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool GoNext()
    {
        if (CurrentStep == ImportWizardStep.Commit)
        {
            return false;
        }

        if (CurrentStep == ImportWizardStep.File && _session is null)
        {
            ErrorMessage = "Выберите файл импорта: без него переход к следующему шагу невозможен.";
            return false;
        }

        if (!ReloadSessionIfNeeded())
        {
            return false;
        }

        CurrentStep = ImportWizardSteps.Next(CurrentStep);
        return true;
    }

    /// <summary>
    /// Возвращается к предыдущему шагу мастера.
    /// </summary>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool GoBack()
    {
        if (CurrentStep == ImportWizardStep.File)
        {
            return false;
        }

        CurrentStep = ImportWizardSteps.Previous(CurrentStep);
        return true;
    }

    /// <summary>
    /// Переходит к указанному шагу мастера назад, без потери сессии импорта.
    /// </summary>
    /// <param name="step">Шаг мастера.</param>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool GoToStep(ImportWizardStep step)
    {
        if (step == ImportWizardStep.File && _session is null)
        {
            ErrorMessage = "Файл импорта не прочитан.";
            return false;
        }

        CurrentStep = step;
        return true;
    }

    /// <summary>
    /// Возвращает имена листов книги XLSX в порядке книги.
    /// </summary>
    /// <param name="filePath">Путь к книге.</param>
    /// <returns>Имена листов; пустой список, если книга не читается.</returns>
    public static IReadOnlyList<string> ReadSheetNames(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var entry = archive.GetEntry("xl/workbook.xml");

            if (entry is null)
            {
                return [];
            }

            using var stream = entry.Open();
            var document = XDocument.Load(stream);

            return [.. document
                .Descendants()
                .Where(element => element.Name.LocalName == "sheet")
                .Select(element => (string?)element.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)];
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Xml.XmlException)
        {
            return [];
        }
    }

    /// <summary>
    /// Возвращает каталог, в котором хранятся файлы профилей сопоставления.
    /// </summary>
    public static string ProfilesDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Picket302",
        ProfilesDirectoryName);

    /// <summary>
    /// Создаёт хранилище файлов профилей сопоставления по умолчанию.
    /// </summary>
    /// <returns>Хранилище профилей (*.csvprof).</returns>
    public static ICsvProfileStore CreateProfileStore()
    {
        var implementation = new Picket302.Infrastructure.Import.CsvProfileStore();

        return new DelegateCsvProfileStore(
            implementation.Save,
            implementation.Load,
            implementation.ListProfiles);
    }

    private ImportFieldSlot? AutoSlot(int columnIndex)
    {
        if (_session is null)
        {
            return null;
        }

        foreach (var binding in _session.Bindings)
        {
            if (binding.ColumnIndex == columnIndex)
            {
                return binding.Slot;
            }
        }

        return null;
    }

    private bool ReloadSessionIfNeeded() => !_needsReload || ReloadSession();

    private bool ReloadSession()
    {
        _needsReload = false;

        if (string.IsNullOrWhiteSpace(_filePath))
        {
            ErrorMessage = "Файл импорта не выбран.";
            return false;
        }

        var options = new ImportStartOptions
        {
            FilePath = _filePath,
            SheetName = _sheetName,
            SheetIndex = _sheetIndex,
            GroupId = _groupId,
            StartRow = _startRow,
            HasHeaderRow = _hasHeaderRow,
            EncodingName = _encodingName,
            Delimiter = _delimiter,
            MergePolicy = MergePolicy,
            RecognizeAddresses = true,
            Repository = _repository,
            Merger = _слиятель,
        };

        if (_mappingSet)
        {
            options.Bindings = [.. GetBindings()];
        }

        var result = ImportSession.Start(_filePath, options);

        if (!result.IsSuccess)
        {
            _session = null;
            _currentRecord = 0;
            ErrorMessage = result.Error;
            return false;
        }

        _session = result.Value!;
        _session.ValidateAll();
        RestoreOperatorDecisions();
        _currentRecord = 0;
        ErrorMessage = null;

        return true;
    }

    /// <summary>
    /// Восстанавливает отметки оператора (проверена, пропущена) после повторного
    /// чтения файла: файл не менялся, поэтому решения относятся к тем же строкам.
    /// </summary>
    private void RestoreOperatorDecisions()
    {
        if (_session is null || _decisions.Count == 0)
        {
            return;
        }

        foreach (var (rowNumber, status) in _decisions)
        {
            var record = _session.GetRecord(rowNumber);

            if (record is null)
            {
                continue;
            }

            switch (status)
            {
                case ImportRecordStatus.Skipped:
                    _session.MarkSkipped(record);
                    break;

                case ImportRecordStatus.Verified when record.Errors.Count == 0:
                    _session.MarkVerified(record);
                    break;
            }
        }
    }
}
