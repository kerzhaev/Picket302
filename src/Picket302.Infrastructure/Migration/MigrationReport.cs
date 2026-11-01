using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Счётчики переноса одной таблицы исходной базы (ТЗ §8.9, функции 117 и 118).
/// </summary>
/// <remarks>
/// <para>
/// Счётчики ведутся по таблицам <em>предшественника</em>: одна строка отчёта
/// соответствует одной исходной таблице, даже если её строки попадают сразу в
/// несколько целевых таблиц (например, <c>addrto</c> исходной MySQL-схемы даёт
/// и запись адресной книги, и отправление).
/// </para>
/// <para>
/// Значения счётчиков:
/// <list type="bullet">
/// <item><description><see cref="Read"/> — прочитано строк исходной таблицы;</description></item>
/// <item><description><see cref="Transferred"/> — перенесено записей (одна исходная
/// строка учитывается один раз, даже если она дала несколько целевых строк);</description></item>
/// <item><description><see cref="Skipped"/> — строк пропущено: обязательные данные
/// отсутствуют либо запись слита с существующей по политике «пропустить»;</description></item>
/// <item><description><see cref="Errors"/> — строк, перенос которых завершился ошибкой;</description></item>
/// <item><description><see cref="NeedsVerification"/> — перенесённых записей с
/// недопустимыми значениями (например, почтовый индекс не из шести цифр): такие
/// записи помечаются как требующие проверки, но не отбрасываются.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class MigrationTableCounters
{
    /// <summary>Создаёт счётчики переноса таблицы.</summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="targetTable">Основная целевая таблица.</param>
    /// <param name="note">Пояснение к переносу таблицы.</param>
    public MigrationTableCounters(string legacyTable, string targetTable, string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);

        LegacyTable = legacyTable;
        TargetTable = targetTable;
        Note = note;
    }

    /// <summary>Имя таблицы исходной базы (предшественника).</summary>
    public string LegacyTable { get; }

    /// <summary>Основная целевая таблица, в которую переносятся строки.</summary>
    public string TargetTable { get; }

    /// <summary>Пояснение к переносу таблицы (необязательно).</summary>
    public string? Note { get; }

    /// <summary>Прочитано строк исходной таблицы.</summary>
    public int Read { get; internal set; }

    /// <summary>Перенесено записей.</summary>
    public int Transferred { get; internal set; }

    /// <summary>Пропущено записей.</summary>
    public int Skipped { get; internal set; }

    /// <summary>Записей, перенос которых завершился ошибкой.</summary>
    public int Errors { get; internal set; }

    /// <summary>Перенесённых записей, требующих проверки.</summary>
    public int NeedsVerification { get; internal set; }

    /// <inheritdoc />
    public override string ToString()
        => $"{LegacyTable} → {TargetTable}: прочитано {Read}, перенесено {Transferred}, "
            + $"пропущено {Skipped}, ошибок {Errors}, требует проверки {NeedsVerification}";
}

/// <summary>
/// Соответствие одного столбца исходной базы полю целевой модели
/// (ТЗ §8.9, функции 117 и 118: «отчет соответствия»).
/// </summary>
/// <param name="LegacyTable">Имя таблицы исходной базы.</param>
/// <param name="LegacyColumn">Имя столбца исходной базы.</param>
/// <param name="TargetTable">Имя целевой таблицы.</param>
/// <param name="TargetField">
/// Имя поля целевой таблицы; <see langword="null"/> — столбец не переносится
/// (например, учётные данные или дублирующее значение).
/// </param>
/// <param name="Note">Пояснение к соответствию (необязательно).</param>
public sealed record MigrationFieldMapping(
    string LegacyTable,
    string LegacyColumn,
    string TargetTable,
    string? TargetField,
    string? Note = null)
{
    /// <summary>Признак того, что столбец переносится в целевую модель.</summary>
    public bool IsTransferred => !string.IsNullOrEmpty(TargetField);

    /// <inheritdoc />
    public override string ToString()
        => IsTransferred
            ? $"{LegacyTable}.{LegacyColumn} → {TargetTable}.{TargetField}"
            : $"{LegacyTable}.{LegacyColumn} → не переносится";
}

/// <summary>
/// Счётчик переноса в одну целевую таблицу — сводка отчёта миграции
/// (ТЗ §8.9, функции 117 и 118: «сводка»).
/// </summary>
/// <param name="TargetTable">Имя целевой таблицы.</param>
/// <param name="Transferred">Количество перенесённых записей.</param>
public sealed record MigrationTargetCount(string TargetTable, int Transferred);

/// <summary>
/// Сводка отчёта миграции: общие количества и количество записей по целевым таблицам.
/// </summary>
public sealed class MigrationSummary
{
    /// <summary>Всего прочитано строк исходных таблиц.</summary>
    public int TotalRead { get; init; }

    /// <summary>Всего перенесено записей.</summary>
    public int TotalTransferred { get; init; }

    /// <summary>Всего пропущено записей.</summary>
    public int TotalSkipped { get; init; }

    /// <summary>Всего записей с ошибками переноса.</summary>
    public int TotalErrors { get; init; }

    /// <summary>Всего записей, перенесённых с пометкой «требует проверки».</summary>
    public int NeedsVerification { get; init; }

    /// <summary>Количество перенесённых записей по целевым таблицам.</summary>
    public IReadOnlyList<MigrationTargetCount> Targets { get; init; } = Array.Empty<MigrationTargetCount>();

    /// <inheritdoc />
    public override string ToString()
        => $"прочитано {TotalRead}, перенесено {TotalTransferred}, пропущено {TotalSkipped}, "
            + $"ошибок {TotalErrors}, требует проверки {NeedsVerification}";
}

/// <summary>
/// Отчёт о миграции базы предшественника: соответствие полей, счётчики переноса
/// и сводка (ТЗ §8.9, функции 117 «Импорт базы из исходной MySQL-схемы» и
/// 118 «Импорт базы из исходной Access-схемы»).
/// </summary>
/// <remarks>
/// Отчёт сериализуется в текст и в JSON — мастер миграции показывает его
/// пользователю (ТЗ §8.9: «UI: Мастер миграции: параметры подключения, отчет
/// соответствия, сводка»).
/// </remarks>
public sealed class MigrationReport
{
    /// <summary>Параметры сериализации отчёта в JSON.</summary>
    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Наименование источника данных («MySQL», «Access»).</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Счётчики переноса по таблицам исходной базы.</summary>
    public IReadOnlyList<MigrationTableCounters> Tables { get; init; } = Array.Empty<MigrationTableCounters>();

    /// <summary>Предупреждения миграции (отсутствующая таблица, недопустимое значение).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Соответствие столбцов исходной базы полям целевой модели.</summary>
    public IReadOnlyList<MigrationFieldMapping> FieldMappings { get; init; } = Array.Empty<MigrationFieldMapping>();

    /// <summary>Сводка переноса.</summary>
    public MigrationSummary Summary { get; init; } = new();

    /// <summary>Признак наличия предупреждений.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>
    /// Возвращает текстовое представление отчёта для показа в мастере миграции.
    /// </summary>
    /// <returns>Текст отчёта.</returns>
    public string ToText()
    {
        var текст = new StringBuilder();

        текст.AppendLine(FormattableString.Invariant($"Отчёт миграции: {Source}"));
        текст.AppendLine(FormattableString.Invariant($"Сводка: {Summary}"));
        текст.AppendLine();
        текст.AppendLine("Таблицы исходной базы:");

        foreach (var счётчик in Tables)
        {
            текст.AppendLine(FormattableString.Invariant(
                $"  {счётчик.LegacyTable} → {счётчик.TargetTable}: прочитано {счётчик.Read}, перенесено {счётчик.Transferred}, пропущено {счётчик.Skipped}, ошибок {счётчик.Errors}, требует проверки {счётчик.NeedsVerification}"));
        }

        текст.AppendLine();
        текст.AppendLine("Целевые таблицы:");

        foreach (var цель in Summary.Targets)
        {
            текст.AppendLine(FormattableString.Invariant($"  {цель.TargetTable}: {цель.Transferred}"));
        }

        текст.AppendLine();
        текст.AppendLine(FormattableString.Invariant($"Предупреждения ({Warnings.Count}):"));

        for (var индекс = 0; индекс < Warnings.Count; индекс++)
        {
            текст.AppendLine(FormattableString.Invariant($"  {индекс + 1}. {Warnings[индекс]}"));
        }

        текст.AppendLine();
        текст.AppendLine(FormattableString.Invariant($"Соответствие полей ({FieldMappings.Count}):"));

        foreach (var соответствие in FieldMappings)
        {
            if (соответствие.IsTransferred)
            {
                текст.AppendLine(FormattableString.Invariant(
                    $"  {соответствие.LegacyTable}.{соответствие.LegacyColumn} → {соответствие.TargetTable}.{соответствие.TargetField}"));
            }
            else
            {
                текст.AppendLine(FormattableString.Invariant(
                    $"  {соответствие.LegacyTable}.{соответствие.LegacyColumn} → не переносится{(соответствие.Note is null ? string.Empty : $" ({соответствие.Note})")}"));
            }
        }

        return текст.ToString();
    }

    /// <summary>
    /// Возвращает JSON-представление отчёта для показа в мастере миграции
    /// и сохранения в файл отчёта.
    /// </summary>
    /// <returns>JSON отчёта.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, ПараметрыJson);

    /// <inheritdoc />
    public override string ToString() => $"Отчёт миграции: {Source}. {Summary}";
}

/// <summary>
/// Сборщик отчёта миграции: счётчики переноса, предупреждения и соответствия полей
/// (ТЗ §8.9, функции 117 и 118).
/// </summary>
/// <remarks>
/// Сборщик наполняется по ходу переноса: картограф читает исходные таблицы и
/// увеличивает счётчик «прочитано», а писатель переноса — «перенесено»,
/// «пропущено», «ошибок» и «требует проверки».
/// </remarks>
public sealed class MigrationReportBuilder
{
    private readonly List<MigrationTableCounters> _счётчики = [];
    private readonly Dictionary<string, MigrationTableCounters> _поТаблицам =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _предупреждения = [];
    private readonly List<MigrationFieldMapping> _соответствия = [];
    private readonly Dictionary<string, int> _поЦелевым = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Создаёт сборщик отчёта.</summary>
    /// <param name="source">Наименование источника данных.</param>
    public MigrationReportBuilder(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Source = source;
    }

    /// <summary>Наименование источника данных.</summary>
    public string Source { get; }

    /// <summary>
    /// Возвращает счётчики таблицы, создавая их при первом обращении.
    /// </summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="targetTable">Основная целевая таблица.</param>
    /// <param name="note">Пояснение к переносу таблицы.</param>
    /// <returns>Счётчики переноса таблицы.</returns>
    public MigrationTableCounters Счётчик(string legacyTable, string targetTable, string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);

        if (_поТаблицам.TryGetValue(legacyTable, out var существующий))
        {
            return существующий;
        }

        var счётчик = new MigrationTableCounters(legacyTable, targetTable, note);
        _поТаблицам[legacyTable] = счётчик;
        _счётчики.Add(счётчик);

        return счётчик;
    }

    /// <summary>Увеличивает счётчик прочитанных строк таблицы.</summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="targetTable">Основная целевая таблица.</param>
    /// <param name="count">Количество строк.</param>
    public void Прочитано(string legacyTable, string targetTable, int count = 1)
        => Счётчик(legacyTable, targetTable).Read += count;

    /// <summary>Увеличивает счётчик перенесённых записей таблицы.</summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="targetTable">Основная целевая таблица.</param>
    /// <param name="count">Количество записей.</param>
    public void Перенесено(string legacyTable, string targetTable, int count = 1)
        => Счётчик(legacyTable, targetTable).Transferred += count;

    /// <summary>
    /// Увеличивает счётчик перенесённых записей таблицы, целевая таблица которой
    /// уже известна (задана при первом обращении к счётчику).
    /// </summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="count">Количество записей.</param>
    public void ПеренесеноЗаписи(string legacyTable, int count = 1)
    {
        if (_поТаблицам.TryGetValue(legacyTable, out var счётчик))
        {
            счётчик.Transferred += count;
        }
    }

    /// <summary>
    /// Увеличивает счётчик пропущенных записей таблицы, целевая таблица которой
    /// уже известна.
    /// </summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="count">Количество записей.</param>
    public void ПропущеноЗаписи(string legacyTable, int count = 1)
    {
        if (_поТаблицам.TryGetValue(legacyTable, out var счётчик))
        {
            счётчик.Skipped += count;
        }
    }

    /// <summary>
    /// Увеличивает счётчик записей с ошибками переноса таблицы, целевая таблица
    /// которой уже известна.
    /// </summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="count">Количество записей.</param>
    public void ОшибокЗаписи(string legacyTable, int count = 1)
    {
        if (_поТаблицам.TryGetValue(legacyTable, out var счётчик))
        {
            счётчик.Errors += count;
        }
    }

    /// <summary>Увеличивает счётчик пропущенных записей таблицы.</summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="targetTable">Основная целевая таблица.</param>
    /// <param name="count">Количество записей.</param>
    public void Пропущено(string legacyTable, string targetTable, int count = 1)
        => Счётчик(legacyTable, targetTable).Skipped += count;

    /// <summary>Увеличивает счётчик записей с ошибками переноса.</summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="targetTable">Основная целевая таблица.</param>
    /// <param name="count">Количество записей.</param>
    public void Ошибок(string legacyTable, string targetTable, int count = 1)
        => Счётчик(legacyTable, targetTable).Errors += count;

    /// <summary>Увеличивает счётчик записей, требующих проверки.</summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="targetTable">Основная целевая таблица.</param>
    /// <param name="count">Количество записей.</param>
    public void ТребуетПроверки(string legacyTable, string targetTable, int count = 1)
        => Счётчик(legacyTable, targetTable).NeedsVerification += count;

    /// <summary>Добавляет предупреждение в отчёт.</summary>
    /// <param name="text">Текст предупреждения на русском языке.</param>
    public void Предупреждение(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _предупреждения.Add(text);
    }

    /// <summary>
    /// Добавляет соответствие столбца исходной базы полю целевой модели.
    /// </summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="legacyColumn">Имя столбца исходной базы.</param>
    /// <param name="targetTable">Имя целевой таблицы.</param>
    /// <param name="targetField">Имя поля целевой таблицы; <see langword="null"/> — не переносится.</param>
    /// <param name="note">Пояснение к соответствию.</param>
    public void Соответствие(
        string legacyTable,
        string legacyColumn,
        string targetTable,
        string? targetField,
        string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyTable);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);

        _соответствия.Add(new MigrationFieldMapping(legacyTable, legacyColumn, targetTable, targetField, note));
    }

    /// <summary>
    /// Добавляет несколько соответствий столбцов одной исходной таблицы.
    /// </summary>
    /// <param name="legacyTable">Имя таблицы исходной базы.</param>
    /// <param name="targetTable">Имя целевой таблицы.</param>
    /// <param name="соответствия">
    /// Пары «столбец исходной таблицы — поле целевой»; пустое поле означает,
    /// что столбец не переносится.
    /// </param>
    public void Соответствия(
        string legacyTable,
        string targetTable,
        IEnumerable<(string Столбец, string? Поле, string? Примечание)> соответствия)
    {
        ArgumentNullException.ThrowIfNull(соответствия);

        foreach (var (столбец, поле, примечание) in соответствия)
        {
            Соответствие(legacyTable, столбец, targetTable, поле, примечание);
        }
    }

    /// <summary>Учитывает запись, перенесённую в целевую таблицу.</summary>
    /// <param name="targetTable">Имя целевой таблицы.</param>
    /// <param name="count">Количество записей.</param>
    public void ПеренесеноВЦель(string targetTable, int count = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);
        _поЦелевым[targetTable] = _поЦелевым.TryGetValue(targetTable, out var текущее) ? текущее + count : count;
    }

    /// <summary>
    /// Формирует отчёт миграции.
    /// </summary>
    /// <returns>Готовый отчёт.</returns>
    public MigrationReport Build()
    {
        var сводка = new MigrationSummary
        {
            TotalRead = _счётчики.Sum(счётчик => счётчик.Read),
            TotalTransferred = _счётчики.Sum(счётчик => счётчик.Transferred),
            TotalSkipped = _счётчики.Sum(счётчик => счётчик.Skipped),
            TotalErrors = _счётчики.Sum(счётчик => счётчик.Errors),
            NeedsVerification = _счётчики.Sum(счётчик => счётчик.NeedsVerification),
            Targets = _поЦелевым
                .OrderBy(пара => пара.Key, StringComparer.Ordinal)
                .Select(пара => new MigrationTargetCount(пара.Key, пара.Value))
                .ToArray(),
        };

        return new MigrationReport
        {
            Source = Source,
            Tables = _счётчики.ToArray(),
            Warnings = _предупреждения.ToArray(),
            FieldMappings = _соответствия.ToArray(),
            Summary = сводка,
        };
    }
}

/// <summary>
/// Ошибка миграции базы предшественника (ТЗ §8.9, функции 117 и 118).
/// </summary>
/// <remarks>
/// Ошибка возникает, когда перенос невозможен в принципе: источник недоступен
/// либо не установлен требуемый драйвер. Отсутствие отдельной таблицы ошибкой
/// не является — такая таблица пропускается с предупреждением в отчёте.
/// </remarks>
public sealed class LegacyMigrationException : Exception
{
    /// <summary>Создаёт ошибку миграции.</summary>
    /// <param name="message">Описание ошибки на русском языке.</param>
    public LegacyMigrationException(string message)
        : base(message)
    {
    }

    /// <summary>Создаёт ошибку миграции с указанием причины.</summary>
    /// <param name="message">Описание ошибки на русском языке.</param>
    /// <param name="внутренняяОшибка">Исходная ошибка источника данных.</param>
    public LegacyMigrationException(string message, Exception внутренняяОшибка)
        : base(message, внутренняяОшибка)
    {
    }
}
