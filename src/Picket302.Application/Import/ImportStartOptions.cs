using Picket302.Domain.Contracts;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Import;

/// <summary>
/// Параметры запуска мастера импорта адресной книги (ТЗ §8.12, функция 159,
/// шаги 1 и 2).
/// </summary>
/// <remarks>
/// Параметры описывают источник данных (файл, лист книги, кодировку и
/// разделитель), начало чтения («Начать импорт с»), категорию адресной книги,
/// а также готовое сопоставление столбцов со слотами полей.
/// </remarks>
public sealed class ImportStartOptions
{
    /// <summary>
    /// Путь к импортируемому файлу.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Имя листа книги для файлов XLSX.
    /// </summary>
    public string? SheetName { get; set; }

    /// <summary>
    /// Номер листа книги для файлов XLSX (нумерация с единицы).
    /// </summary>
    public int? SheetIndex { get; set; }

    /// <summary>
    /// Категория (группа) адресной книги, с которой начинается импорт.
    /// </summary>
    public long? GroupId { get; set; }

    /// <summary>
    /// Готовый профиль сопоставления столбцов со слотами полей.
    /// </summary>
    public CsvProfile? Profile { get; set; }

    /// <summary>
    /// Явное сопоставление столбцов со слотами полей, заданное оператором.
    /// </summary>
    public List<ImportColumnBinding>? Bindings { get; set; }

    /// <summary>
    /// Номер строки, с которой начинается импорт («Начать импорт с»).
    /// </summary>
    public int StartRow { get; set; } = 1;

    /// <summary>
    /// Имя кодировки файла; пустое значение означает автоопределение.
    /// </summary>
    public string? EncodingName { get; set; }

    /// <summary>
    /// Разделитель столбцов; пустое значение означает автоопределение.
    /// </summary>
    public char? Delimiter { get; set; }

    /// <summary>
    /// Признак наличия строки заголовков.
    /// </summary>
    public bool HasHeaderRow { get; set; } = true;

    /// <summary>
    /// Политика слияния с существующей базой, применяемая при финальном шаге.
    /// </summary>
    public MergePolicy MergePolicy { get; set; } = MergePolicy.SkipExisting;

    /// <summary>
    /// Признак автоматического распознавания адресов при импорте.
    /// </summary>
    public bool RecognizeAddresses { get; set; } = true;

    /// <summary>
    /// Репозиторий контрагентов, используемый финальным шагом импорта.
    /// </summary>
    /// <remarks>
    /// Репозиторий внедряется через параметры запуска, чтобы мастер импорта
    /// (окно) и прикладной уровень работали с одной и той же сессией.
    /// </remarks>
    public ICounterpartyRepository? Repository { get; set; }

    /// <summary>
    /// Слиятель пакета импорта.
    /// </summary>
    /// <remarks>
    /// Задаётся в совместном режиме: слияние выполняет серверная служба
    /// одной командой в одной транзакции. Пусто — слияние выполняет локальная
    /// служба тонкого слияния над репозиторием контрагентов.
    /// </remarks>
    public IImportMerger? Merger { get; set; }

    /// <summary>
    /// Создаёт параметры запуска для указанного файла.
    /// </summary>
    /// <param name="filePath">Путь к импортируемому файлу.</param>
    /// <returns>Параметры запуска мастера импорта.</returns>
    public static ImportStartOptions ForFile(string filePath) => new() { FilePath = filePath };
}
