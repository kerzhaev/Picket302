namespace Picket302.App.UI.Forms;

/// <summary>
/// Шаг мастера импорта адресной книги (ТЗ §8.12, функция 159:
/// «Мастер импорта адресной книги (7 экранов)»).
/// </summary>
public enum ImportWizardStep
{
    /// <summary>Шаг 1 — выбор файла и листа книги, предпросмотр первых строк.</summary>
    File = 1,

    /// <summary>Шаг 2 — «Начать импорт с» и категория адресной книги.</summary>
    StartRowAndGroup = 2,

    /// <summary>Шаг 3 — сопоставление столбцов файла с полями.</summary>
    ColumnMapping = 3,

    /// <summary>Шаг 4 — пошаговый предпросмотр записей.</summary>
    RecordPreview = 4,

    /// <summary>Шаг 5 — фильтр «Все записи / Записи с ошибками».</summary>
    ErrorFilter = 5,

    /// <summary>Шаг 6 — итоги проверки и политики импорта.</summary>
    Summary = 6,

    /// <summary>Шаг 7 — применение импорта и итоговые счётчики.</summary>
    Commit = 7,
}

/// <summary>
/// Фильтр пошагового предпросмотра (ТЗ §8.12, функция 159, шаг 5:
/// «фильтр “Показывать: Все записи / Записи с ошибками”»).
/// </summary>
public enum ImportRecordFilter
{
    /// <summary>Показывать все записи пакета.</summary>
    All = 0,

    /// <summary>Показывать только записи с ошибками проверки.</summary>
    WithErrors = 1,
}

/// <summary>
/// Описание шагов мастера импорта: заголовки, подсказки и переходы
/// (ТЗ §8.12, функция 159).
/// </summary>
public static class ImportWizardSteps
{
    /// <summary>Число шагов мастера импорта.</summary>
    public const int TotalSteps = 7;

    /// <summary>
    /// Возвращает русский заголовок шага для индикатора мастера.
    /// </summary>
    /// <param name="шаг">Шаг мастера.</param>
    /// <returns>Заголовок шага.</returns>
    public static string Title(ImportWizardStep шаг) => шаг switch
    {
        ImportWizardStep.File => "Файл импорта",
        ImportWizardStep.StartRowAndGroup => "Начало импорта и категория",
        ImportWizardStep.ColumnMapping => "Сопоставление столбцов",
        ImportWizardStep.RecordPreview => "Предпросмотр записей",
        ImportWizardStep.ErrorFilter => "Фильтр записей",
        ImportWizardStep.Summary => "Итоги и политики импорта",
        ImportWizardStep.Commit => "Применение импорта",
        _ => "Мастер импорта",
    };

    /// <summary>
    /// Возвращает пояснение к шагу мастера.
    /// </summary>
    /// <param name="шаг">Шаг мастера.</param>
    /// <returns>Пояснение к шагу.</returns>
    public static string Description(ImportWizardStep шаг) => шаг switch
    {
        ImportWizardStep.File =>
            "Выберите файл импорта (CSV, TXT, XLSX, HTML) и лист книги. "
            + "Показываются количество листов, записей и столбцов файла.",
        ImportWizardStep.StartRowAndGroup =>
            "Укажите номер строки, с которой начинается импорт, и категорию адресной книги.",
        ImportWizardStep.ColumnMapping =>
            "Сопоставьте столбцы файла с полями: ФИО, Название организации, Почтовый адрес, "
            + "Юридический адрес, Фактический адрес, Прочее, Название, Сокращенное название.",
        ImportWizardStep.RecordPreview =>
            "Просмотрите записи по одной, исправьте значения, отметьте проверенные "
            + "и пропустите ненужные знаком «<< >>».",
        ImportWizardStep.ErrorFilter =>
            "Показывать все записи или только записи с ошибками проверки.",
        ImportWizardStep.Summary =>
            "Проверьте числа непроверенных и ошибочных записей, выберите политику импорта "
            + "и политику слияния с существующей базой.",
        ImportWizardStep.Commit =>
            "Импортировать записи по выбранной политике и посмотреть итоговые счётчики.",
        _ => string.Empty,
    };

    /// <summary>
    /// Возвращает следующий шаг мастера.
    /// </summary>
    /// <param name="шаг">Текущий шаг.</param>
    /// <returns>Следующий шаг либо текущий, если текущий — последний.</returns>
    public static ImportWizardStep Next(ImportWizardStep шаг) => шаг switch
    {
        ImportWizardStep.File => ImportWizardStep.StartRowAndGroup,
        ImportWizardStep.StartRowAndGroup => ImportWizardStep.ColumnMapping,
        ImportWizardStep.ColumnMapping => ImportWizardStep.RecordPreview,
        ImportWizardStep.RecordPreview => ImportWizardStep.ErrorFilter,
        ImportWizardStep.ErrorFilter => ImportWizardStep.Summary,
        ImportWizardStep.Summary => ImportWizardStep.Commit,
        _ => шаг,
    };

    /// <summary>
    /// Возвращает предыдущий шаг мастера.
    /// </summary>
    /// <param name="шаг">Текущий шаг.</param>
    /// <returns>Предыдущий шаг либо текущий, если текущий — первый.</returns>
    public static ImportWizardStep Previous(ImportWizardStep шаг) => шаг switch
    {
        ImportWizardStep.File => шаг,
        ImportWizardStep.StartRowAndGroup => ImportWizardStep.File,
        ImportWizardStep.ColumnMapping => ImportWizardStep.StartRowAndGroup,
        ImportWizardStep.RecordPreview => ImportWizardStep.ColumnMapping,
        ImportWizardStep.ErrorFilter => ImportWizardStep.RecordPreview,
        ImportWizardStep.Summary => ImportWizardStep.ErrorFilter,
        ImportWizardStep.Commit => ImportWizardStep.Summary,
        _ => шаг,
    };
}
