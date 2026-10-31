using Picket302.Domain.Entities;

namespace Picket302.Domain.Contracts;

/// <summary>
/// Слот поля мастера импорта — цель сопоставления столбца файла (ТЗ §8.12,
/// функция 159, шаг 3 «Сопоставление по столбцам»).
/// </summary>
/// <remarks>
/// Список слотов зафиксирован ТЗ и не расширяется: сопоставление выполняется
/// «по столбцам» — один столбец файла отображается ровно в одно поле.
/// </remarks>
public enum ImportFieldSlot
{
    /// <summary>ФИО физического лица.</summary>
    FullName = 0,

    /// <summary>Название организации.</summary>
    OrganizationName = 1,

    /// <summary>Почтовый адрес (одна строка целиком).</summary>
    PostalAddress = 2,

    /// <summary>Юридический адрес (одна строка целиком).</summary>
    LegalAddress = 3,

    /// <summary>Фактический адрес (одна строка целиком).</summary>
    ActualAddress = 4,

    /// <summary>Прочее (телефон, e-mail, примечание и т. п.).</summary>
    Other = 5,

    /// <summary>Наименование записи в адресной книге.</summary>
    Name = 6,

    /// <summary>Сокращённое наименование.</summary>
    ShortName = 7,
}

/// <summary>
/// Состояние записи импорта в пошаговом предпросмотре (ТЗ §8.12, функция 159).
/// </summary>
public enum ImportRecordStatus
{
    /// <summary>Запись не проверена оператором.</summary>
    Unverified = 0,

    /// <summary>Запись проверена оператором вручную.</summary>
    Verified = 1,

    /// <summary>Запись содержит ошибки валидации.</summary>
    HasErrors = 2,

    /// <summary>Запись пропущена оператором (знак «&lt;&lt; &gt;&gt;»).</summary>
    Skipped = 3,
}

/// <summary>
/// Политика применения импорта на финальном шаге мастера (ТЗ §8.12, функция 159).
/// </summary>
public enum ImportPolicy
{
    /// <summary>Добавить только проверенных — записей, отмеченных оператором.</summary>
    VerifiedOnly = 0,

    /// <summary>Только без ошибок — все записи, не содержащие ошибок валидации.</summary>
    WithoutErrors = 1,

    /// <summary>Все — без исключения, включая записи с ошибками.</summary>
    All = 2,
}

/// <summary>
/// Политика слияния с существующей записью (ТЗ §8.12, функция 74).
/// </summary>
public enum MergePolicy
{
    /// <summary>Пропустить существующего контрагента.</summary>
    SkipExisting = 0,

    /// <summary>Дополнить пустые поля существующей записи.</summary>
    FillEmpty = 1,

    /// <summary>Перезаписать непустые значения существующей записи.</summary>
    Overwrite = 2,
}

/// <summary>
/// Черновик контрагента, подготовленный импортом (ТЗ §8.12, функции 72–76).
/// </summary>
/// <remarks>
/// Черновик отделён от доменной сущности: он может быть неполным, содержать
/// ошибки и редактироваться оператором в предпросмотре. Преобразование в
/// доменную сущность выполняется на финальном шаге мастера.
/// </remarks>
public sealed class CounterpartyDraft
{
    /// <summary>Максимальная длина строки адреса (ТЗ §3.1.1).</summary>
    public const int MaxAddressLineLength = 255;

    /// <summary>Количество адресных строк.</summary>
    public const int AddressLineCount = 6;

    /// <summary>Тип контрагента: юридическое или физическое лицо.</summary>
    public CounterpartyType Type { get; set; } = CounterpartyType.LegalEntity;

    /// <summary>Полное наименование либо ФИО.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Сокращённое наименование.</summary>
    public string? ShortName { get; set; }

    /// <summary>ИНН (10 знаков для ЮЛ, 12 для ФЛ) либо УНП.</summary>
    public string? Inn { get; set; }

    /// <summary>КПП юридического лица.</summary>
    public string? Kpp { get; set; }

    /// <summary>ОГРН либо ОГРНИП.</summary>
    public string? Ogrn { get; set; }

    /// <summary>Телефон.</summary>
    public string? Phone { get; set; }

    /// <summary>Адрес электронной почты.</summary>
    public string? Email { get; set; }

    /// <summary>Технологическое примечание.</summary>
    public string? Note { get; set; }

    /// <summary>Почтовый индекс получателя.</summary>
    public string PostalZip { get; set; } = string.Empty;

    /// <summary>Шесть адресных строк получателя.</summary>
    public string[] PostalLines { get; set; } = new string[AddressLineCount];

    /// <summary>Почтовый индекс юридического адреса.</summary>
    public string LegalZip { get; set; } = string.Empty;

    /// <summary>Шесть адресных строк юридического адреса.</summary>
    public string[] LegalLines { get; set; } = new string[AddressLineCount];

    /// <summary>Почтовый индекс фактического адреса.</summary>
    public string ActualZip { get; set; } = string.Empty;

    /// <summary>Шесть адресных строк фактического адреса.</summary>
    public string[] ActualLines { get; set; } = new string[AddressLineCount];

    /// <summary>Категория (группа) адресной книги.</summary>
    public long? GroupId { get; set; }

    /// <summary>Признак «Избранный адрес».</summary>
    public bool IsVip { get; set; }

    /// <summary>
    /// Источник записи: «Импорт &lt;формат&gt; от &lt;дата&gt; из &lt;файл&gt;»
    /// (ТЗ §8.12, функция 75).
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Признак необходимости проверки адреса оператором: выставляется, когда
    /// почтовый индекс не найден в исходной строке (ТЗ §8.1, функция 5).
    /// </summary>
    public bool NeedsVerification { get; set; }

    /// <summary>Создаёт независимую копию черновика.</summary>
    /// <returns>Копия со всеми перенесёнными значениями.</returns>
    public CounterpartyDraft Clone() => new()
    {
        Type = Type,
        Name = Name,
        ShortName = ShortName,
        Inn = Inn,
        Kpp = Kpp,
        Ogrn = Ogrn,
        Phone = Phone,
        Email = Email,
        Note = Note,
        PostalZip = PostalZip,
        PostalLines = (string[])PostalLines.Clone(),
        LegalZip = LegalZip,
        LegalLines = (string[])LegalLines.Clone(),
        ActualZip = ActualZip,
        ActualLines = (string[])ActualLines.Clone(),
        GroupId = GroupId,
        IsVip = IsVip,
        Source = Source,
        NeedsVerification = NeedsVerification,
    };
}

/// <summary>
/// Запись импорта в пошаговом предпросмотре (ТЗ §8.12, функция 159, шаг 4).
/// </summary>
public sealed class ImportRecord
{
    /// <summary>Номер строки исходного файла (нумерация с единицы).</summary>
    public int RowNumber { get; init; }

    /// <summary>Черновик контрагента, подготовленный из строки файла.</summary>
    public CounterpartyDraft Draft { get; set; } = new();

    /// <summary>Текущее состояние записи.</summary>
    public ImportRecordStatus Status { get; set; } = ImportRecordStatus.Unverified;

    /// <summary>Ошибки валидации: не пустое значение переводит запись в «С ошибками».</summary>
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    /// <summary>Предупреждения (усечение строк, отсутствующий индекс и т. п.).</summary>
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    /// <summary>Признак пропуска записи оператором.</summary>
    public bool IsSkipped => Status == ImportRecordStatus.Skipped;

    /// <summary>Признак проверки записи оператором.</summary>
    public bool IsVerified => Status == ImportRecordStatus.Verified;

    /// <summary>Признак наличия ошибок валидации.</summary>
    public bool HasErrors => Status == ImportRecordStatus.HasErrors || Errors.Count > 0;
}

/// <summary>
/// Пакет импорта — результат чтения файла и основание для мастера
/// (ТЗ §8.12, функция 159).
/// </summary>
public sealed class ImportBatch
{
    /// <summary>Наименование формата источника («CSV», «XLSX», «1С 8.x» и т. п.).</summary>
    public string SourceFormat { get; init; } = string.Empty;

    /// <summary>Имя исходного файла.</summary>
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>Момент чтения файла.</summary>
    public DateTime ImportedAt { get; init; } = DateTime.Now;

    /// <summary>Записи пакета.</summary>
    public List<ImportRecord> Records { get; init; } = [];

    /// <summary>
    /// Описание источника для поля «Источник» записи контрагента:
    /// «Импорт &lt;формат&gt; от &lt;дата&gt; из &lt;файл&gt;» (ТЗ §8.12, функция 75).
    /// </summary>
    public string SourceDescription =>
        $"Импорт {SourceFormat} от {ImportedAt:dd.MM.yyyy} из {SourceFile}";
}

/// <summary>
/// Сводка проверки пакета импорта (ТЗ §8.12, функция 159, шаги 5–6).
/// </summary>
/// <param name="Records">Записи пакета.</param>
public sealed record ValidationSummary(IReadOnlyList<ImportRecord> Records)
{
    /// <summary>Общее число записей.</summary>
    public int TotalCount => Records.Count;

    /// <summary>Число проверенных оператором записей.</summary>
    public int VerifiedCount => Records.Count(запись => запись.IsVerified);

    /// <summary>Число непроверенных записей (без ошибок и без отметки проверки).</summary>
    public int UnverifiedCount => Records.Count(запись =>
        !запись.IsVerified && !запись.HasErrors && !запись.IsSkipped);

    /// <summary>Число записей с ошибками.</summary>
    public int ErrorCount => Records.Count(запись => запись.HasErrors);

    /// <summary>Число пропущенных записей.</summary>
    public int SkippedCount => Records.Count(запись => запись.IsSkipped);
}

/// <summary>
/// Применённая политика импорта (ТЗ §8.12, функция 159, шаг 7).
/// </summary>
public sealed class ImportResult
{
    /// <summary>Применённая политика.</summary>
    public ImportPolicy Policy { get; init; }

    /// <summary>Число созданных записей.</summary>
    public int Created { get; init; }

    /// <summary>Число обновлённых записей.</summary>
    public int Updated { get; init; }

    /// <summary>Число пропущенных записей.</summary>
    public int Skipped { get; init; }

    /// <summary>Число конфликтов (существующие записи по ключу слияния).</summary>
    public int Conflicts { get; init; }

    /// <summary>Число записей с ошибками, попавших в импорт.</summary>
    public int Errors { get; init; }

    /// <summary>Число непроверенных записей, попавших в импорт.</summary>
    public int Unverified { get; init; }

    /// <summary>Число записей, отобранных политикой к применению.</summary>
    public int Selected => Created + Updated;
}

/// <summary>
/// Отчёт слияния импорта с базой (ТЗ §8.12, функция 74).
/// </summary>
public sealed class MergeReport
{
    /// <summary>Число созданных записей.</summary>
    public int Created { get; init; }

    /// <summary>Число обновлённых записей.</summary>
    public int Updated { get; init; }

    /// <summary>Число пропущенных записей (существующие записи при политике «пропустить»).</summary>
    public int Skipped { get; init; }

    /// <summary>Число конфликтов: существующая запись найдена по ключу слияния.</summary>
    public int Conflicts { get; init; }

    /// <summary>Описание конфликтов: ключ и имя существующей записи.</summary>
    public IReadOnlyList<string> ConflictDetails { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Результат дедупликации пакета импорта (ТЗ §8.12, функция 75).
/// </summary>
public sealed class DeduplicationResult
{
    /// <summary>Записи, прошедшие дедупликацию.</summary>
    public IReadOnlyList<CounterpartyDraft> Accepted { get; init; } = Array.Empty<CounterpartyDraft>();

    /// <summary>Записи-дубликаты, исключённые из пакета.</summary>
    public IReadOnlyList<CounterpartyDraft> SkippedDuplicates { get; init; } = Array.Empty<CounterpartyDraft>();

    /// <summary>Число исключённых дубликатов.</summary>
    public int SkippedCount => SkippedDuplicates.Count;

    /// <summary>Число обновлённых записей (совпадения дополнены данными файла).</summary>
    public int UpdatedCount { get; init; }
}

/// <summary>
/// Сопоставление столбца файла импорта со слотом поля (ТЗ §8.12, функция 73).
/// </summary>
/// <param name="ColumnIndex">Номер столбца файла (нумерация с нуля).</param>
/// <param name="Slot">Слот поля мастера импорта.</param>
/// <param name="ColumnName">
/// Колонка БД из словаря полей (ТЗ §3.2), если столбец отображён в поле словаря.
/// </param>
public sealed record ImportColumnBinding(int ColumnIndex, ImportFieldSlot? Slot, string? ColumnName = null);

/// <summary>
/// Профиль сопоставления CSV/текстового файла (ТЗ §8.12, функция 73).
/// </summary>
/// <remarks>
/// Профиль хранится в файле с расширением <c>.csvprof</c> и позволяет повторять
/// импорт без повторного сопоставления столбцов.
/// </remarks>
public sealed class CsvProfile
{
    /// <summary>Расширение файла профиля.</summary>
    public const string FileExtension = ".csvprof";

    /// <summary>Имя профиля.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Имя кодировки (например, «utf-8», «windows-1251»).</summary>
    public string EncodingName { get; set; } = "utf-8";

    /// <summary>Разделитель столбцов.</summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>Символ обрамления значений.</summary>
    public char QuoteChar { get; set; } = '"';

    /// <summary>Признак наличия строки заголовков.</summary>
    public bool HasHeaderRow { get; set; } = true;

    /// <summary>Номер первой строки данных (нумерация с единицы).</summary>
    public int FirstDataRow { get; set; } = 1;

    /// <summary>Сопоставление столбцов файла со слотами полей.</summary>
    public List<ImportColumnBinding> Bindings { get; set; } = [];

    /// <summary>Путь к файлу профиля.</summary>
    public string? FilePath { get; set; }
}
