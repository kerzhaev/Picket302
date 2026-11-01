using System.Text;
using Picket302.Application.Templating;
using Picket302.Domain.Entities;

namespace Picket302.Application.Services;

/// <summary>
/// Сформированный документ пакета (ТЗ §8.5, функция 65: пакетная генерация
/// документов произвольного вида).
/// </summary>
/// <param name="CounterpartyId">Идентификатор контрагента.</param>
/// <param name="CounterpartyName">Наименование контрагента.</param>
/// <param name="FilePath">Путь к сформированному файлу.</param>
/// <param name="Warnings">Предупреждения, возникшие при формировании.</param>
public sealed record GeneratedDocument(
    long CounterpartyId,
    string CounterpartyName,
    string FilePath,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Пропущенная запись пакета с причиной пропуска (ТЗ §8.5, функция 65).
/// </summary>
/// <param name="CounterpartyId">Идентификатор контрагента.</param>
/// <param name="CounterpartyName">Наименование контрагента.</param>
/// <param name="Reason">Причина пропуска на русском языке.</param>
public sealed record SkippedDocument(
    long CounterpartyId,
    string CounterpartyName,
    string Reason);

/// <summary>
/// Результат пакетной генерации документов (ТЗ §8.5, функция 65).
/// </summary>
public sealed class DocumentBatchResult
{
    /// <summary>
    /// Создаёт результат пакетной генерации.
    /// </summary>
    /// <param name="generated">Сформированные документы.</param>
    /// <param name="skipped">Пропущенные записи с причинами.</param>
    /// <param name="totalCount">Общее число обработанных записей.</param>
    public DocumentBatchResult(
        IReadOnlyList<GeneratedDocument> generated,
        IReadOnlyList<SkippedDocument> skipped,
        int totalCount)
    {
        Generated = generated ?? Array.Empty<GeneratedDocument>();
        Skipped = skipped ?? Array.Empty<SkippedDocument>();
        TotalCount = totalCount;
    }

    /// <summary>Сформированные документы.</summary>
    public IReadOnlyList<GeneratedDocument> Generated { get; }

    /// <summary>Пропущенные записи с причинами пропуска.</summary>
    public IReadOnlyList<SkippedDocument> Skipped { get; }

    /// <summary>Общее число обработанных записей.</summary>
    public int TotalCount { get; }

    /// <summary>Признак наличия пропущенных записей.</summary>
    public bool HasSkipped => Skipped.Count > 0;

    /// <summary>Итоговая строка о результате пакетной генерации.</summary>
    public string Summary =>
        $"Обработано записей: {TotalCount}; сформировано документов: {Generated.Count}; пропущено: {Skipped.Count}.";
}

/// <summary>
/// Пакетная генерация документов произвольного вида по списку контрагентов
/// (ТЗ §8.5, функция 65).
/// </summary>
/// <remarks>
/// Каждый документ формируется по одному шаблону с параметрами своего
/// контрагента. Запись с битым маркером (значение маркера отсутствует) либо
/// с некорректной структурой документа пропускается с указанием причины:
/// формирование остальных документов пакета при этом не прерывается.
/// </remarks>
public sealed class DocumentBatchGenerator
{
    /// <summary>Максимальная длина имени контрагента в имени выходного файла.</summary>
    private const int MaxFileNameSuffixLength = 60;

    private readonly ArbitraryDocumentEngine _engine;

    private readonly DocumentMarkerCatalog _catalog;

    /// <summary>Создаёт пакетный генератор документов.</summary>
    public DocumentBatchGenerator()
        : this(new ArbitraryDocumentEngine(), new DocumentMarkerCatalog())
    {
    }

    /// <summary>Создаёт пакетный генератор документов.</summary>
    /// <param name="engine">Генератор документов.</param>
    /// <param name="catalog">Каталог маркеров.</param>
    public DocumentBatchGenerator(ArbitraryDocumentEngine engine, DocumentMarkerCatalog catalog)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>
    /// Формирует документы по списку контрагентов.
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <param name="counterparties">Список контрагентов.</param>
    /// <param name="outputDirectory">Каталог для сформированных документов.</param>
    /// <param name="customValues">
    /// Значения пользовательских полей контрагентов; имя поля образует маркер
    /// вида <c>#Custom_имя#</c> (ТЗ §3.1.9).
    /// </param>
    /// <returns>
    /// Сформированные документы и пропущенные записи с причинами пропуска.
    /// </returns>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    public DocumentBatchResult GenerateBatch(
        string templatePath,
        IReadOnlyList<Counterparty> counterparties,
        string outputDirectory,
        IReadOnlyDictionary<string, string>? customValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentNullException.ThrowIfNull(counterparties);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Файл шаблона не найден: {templatePath}", templatePath);
        }

        Directory.CreateDirectory(outputDirectory);

        var generated = new List<GeneratedDocument>();
        var skipped = new List<SkippedDocument>();

        foreach (var counterparty in counterparties)
        {
            if (counterparty is null)
            {
                continue;
            }

            var parameters = _catalog.BuildParameters(counterparty, customValues);

            DocumentRenderResult rendered;

            try
            {
                rendered = _engine.Render(templatePath, parameters);
            }
            catch (Exception ошибка)
            {
                skipped.Add(new SkippedDocument(
                    counterparty.Id,
                    counterparty.Name,
                    $"Документ не сформирован: {ошибка.Message}"));

                continue;
            }

            // Битый маркер не отменяет документ: незаполненный маркер остаётся
            // в тексте, а проблема попадает в предупреждения сформированного
            // документа (ТЗ §8.5, функция 65).
            var предупреждения = rendered.Warnings.ToList();

            if (rendered.UnresolvedMarkers.Count > 0)
            {
                предупреждения.Add(
                    $"Не найдено значение маркера «{rendered.UnresolvedMarkers[0]}» — маркер оставлен без изменений.");
            }

            var problems = _engine.ValidateStructure(rendered.Content, rendered.FileName);

            if (problems.Count > 0)
            {
                skipped.Add(new SkippedDocument(
                    counterparty.Id,
                    counterparty.Name,
                    $"Проверка структуры документа не пройдена: {problems[0]}"));

                continue;
            }

            var filePath = Path.Combine(outputDirectory, BuildFileName(rendered.FileName, counterparty.Name));

            File.WriteAllBytes(filePath, rendered.Content);

            generated.Add(new GeneratedDocument(
                counterparty.Id,
                counterparty.Name,
                filePath,
                предупреждения));
        }

        return new DocumentBatchResult(generated, skipped, counterparties.Count);
    }

    /// <summary>
    /// Формирует имя выходного файла: имя шаблона, наименование контрагента и
    /// расширение шаблона.
    /// </summary>
    /// <param name="templateFileName">Имя файла шаблона.</param>
    /// <param name="counterpartyName">Наименование контрагента.</param>
    /// <returns>Имя выходного файла.</returns>
    private static string BuildFileName(string templateFileName, string counterpartyName)
    {
        var baseName = Path.GetFileNameWithoutExtension(templateFileName);
        var extension = Path.GetExtension(templateFileName);
        var suffix = SanitizeFileName(counterpartyName);

        return suffix.Length == 0
            ? baseName + extension
            : $"{baseName}_{suffix}{extension}";
    }

    /// <summary>
    /// Приводит наименование контрагента к допустимому имени файла.
    /// </summary>
    /// <param name="value">Наименование контрагента.</param>
    /// <returns>Подготовленное имя файла.</returns>
    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var symbol in value)
        {
            builder.Append(Array.IndexOf(invalid, symbol) >= 0 ? '_' : symbol);
        }

        var result = builder.ToString().Trim();

        return result.Length > MaxFileNameSuffixLength
            ? result[..MaxFileNameSuffixLength]
            : result;
    }
}
