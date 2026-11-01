using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Picket302.Domain.Common;

namespace Picket302.Application.Templating;

/// <summary>
/// Вид шаблона документа (ТЗ §8.5, функция 65).
/// </summary>
public enum DocumentTemplateKind
{
    /// <summary>
    /// Обычный текстовый шаблон (UTF-8) либо HTML: маркеры заменяются
    /// текстовой подстановкой.
    /// </summary>
    Text,

    /// <summary>
    /// Таблица стилей XSLT (UTF-8 XML): параметры передаются в модель данных
    /// XML, после чего применяется таблица стилей.
    /// </summary>
    Xslt,

    /// <summary>
    /// Пакет OpenXML (<c>.docx</c>): маркеры заменяются внутри частей
    /// <c>word/document.xml</c> и колонтитулов.
    /// </summary>
    OpenXml,
}

/// <summary>
/// Текстовая часть шаблона документа (ТЗ §8.5, функция 66).
/// </summary>
/// <param name="Name">
/// Имя части: имя файла шаблона либо путь части пакета OpenXML.
/// </param>
/// <param name="Text">Текст части.</param>
public sealed record DocumentTemplatePart(string Name, string Text);

/// <summary>
/// Вхождение маркера документа в шаблоне (ТЗ §8.5, функция 66: «предпросмотр
/// структуры документа»).
/// </summary>
/// <param name="Name">Имя поля без обрамляющих решёток.</param>
/// <param name="Marker">Текст маркера вида <c>#Имя#</c>.</param>
/// <param name="Order">Порядковый номер вхождения в шаблоне.</param>
/// <param name="Line">Номер строки, в которой найден маркер.</param>
/// <param name="Position">Позиция маркера в тексте части (в символах).</param>
/// <param name="Part">Имя части шаблона, в которой найден маркер.</param>
/// <param name="IsResolved">
/// Признак того, что значение маркера найдено в наборе параметров.
/// </param>
/// <param name="Value">Значение маркера; <see langword="null"/>, если не найдено.</param>
public sealed record DocumentMarkerOccurrence(
    string Name,
    string Marker,
    int Order,
    int Line,
    int Position,
    string Part,
    bool IsResolved,
    string? Value);

/// <summary>
/// Анализатор шаблона документа: предпросмотр структуры, перечень маркеров и
/// проверка структуры получаемого документа (ТЗ §8.5, функция 66).
/// </summary>
/// <remarks>
/// Анализатор не меняет шаблон: он только читает его и сообщает структуру —
/// маркеры в порядке появления с указанием строки и позиции, а также
/// найденные в пакете OpenXML обязательные части.
/// </remarks>
public sealed class DocumentTemplateAnalyzer
{
    /// <summary>Обязательная часть пакета OpenXML с описанием типов содержимого.</summary>
    public const string ContentTypesPart = "[Content_Types].xml";

    /// <summary>Обязательная часть пакета OpenXML с основным текстом документа.</summary>
    public const string DocumentPart = "word/document.xml";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly DocumentMarkerCatalog _catalog;

    /// <summary>Создаёт анализатор шаблона.</summary>
    /// <param name="catalog">Каталог маркеров; при отсутствии — общий каталог.</param>
    public DocumentTemplateAnalyzer(DocumentMarkerCatalog? catalog = null)
        => _catalog = catalog ?? new DocumentMarkerCatalog();

    /// <summary>
    /// Определяет вид шаблона по расширению файла и сигнатуре содержимого.
    /// </summary>
    /// <param name="templatePath">Путь к шаблону.</param>
    /// <returns>Вид шаблона.</returns>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    public DocumentTemplateKind DetectKind(string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Файл шаблона не найден: {templatePath}", templatePath);
        }

        var extension = Path.GetExtension(templatePath).ToLowerInvariant();

        if (extension == ".docx")
        {
            return DocumentTemplateKind.OpenXml;
        }

        if (extension is ".xslt" or ".xsl")
        {
            return DocumentTemplateKind.Xslt;
        }

        return IsZipSignature(File.ReadAllBytes(templatePath))
            ? DocumentTemplateKind.OpenXml
            : DocumentTemplateKind.Text;
    }

    /// <summary>
    /// Возвращает структуру шаблона: маркеры в порядке появления.
    /// </summary>
    /// <param name="templatePath">Путь к шаблону.</param>
    /// <param name="parameters">
    /// Набор параметров подстановки; при передаче определяются также
    /// найденные значения маркеров.
    /// </param>
    /// <returns>Перечень вхождений маркеров.</returns>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    public IReadOnlyList<DocumentMarkerOccurrence> Analyze(
        string templatePath,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);

        var kind = DetectKind(templatePath);
        var parts = ReadParts(templatePath, kind);

        return AnalyzeParts(parts, parameters);
    }

    /// <summary>
    /// Возвращает маркеры частей шаблона в порядке появления.
    /// </summary>
    /// <param name="parts">Части шаблона.</param>
    /// <param name="parameters">
    /// Набор параметров подстановки; при передаче определяются также
    /// найденные значения маркеров.
    /// </param>
    /// <returns>Перечень вхождений маркеров.</returns>
    public IReadOnlyList<DocumentMarkerOccurrence> AnalyzeParts(
        IReadOnlyList<DocumentTemplatePart> parts,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var occurrences = new List<DocumentMarkerOccurrence>();
        var order = 0;

        foreach (var part in parts)
        {
            var line = 1;
            var lineStart = 0;

            foreach (Match match in DocumentMarkerCatalog.MarkerPattern.Matches(part.Text))
            {
                for (var index = lineStart; index < match.Index; index++)
                {
                    if (part.Text[index] == '\n')
                    {
                        line++;
                        lineStart = index + 1;
                    }
                }

                var name = match.Groups["name"].Value;
                var value = string.Empty;
                var resolved = parameters is not null
                    && _catalog.TryGetParameterValue(name, parameters, out value);

                occurrences.Add(new DocumentMarkerOccurrence(
                    name,
                    match.Value,
                    order++,
                    line,
                    match.Index,
                    part.Name,
                    resolved,
                    resolved ? value : null));
            }
        }

        return occurrences;
    }

    /// <summary>
    /// Проверяет структуру шаблона документа.
    /// </summary>
    /// <param name="templatePath">Путь к шаблону.</param>
    /// <returns>
    /// Перечень проблем; пустой перечень означает, что структура корректна.
    /// </returns>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    public IReadOnlyList<string> Validate(string templatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Файл шаблона не найден: {templatePath}", templatePath);
        }

        return ValidateContent(File.ReadAllBytes(templatePath), templatePath);
    }

    /// <summary>
    /// Проверяет структуру сформированного документа.
    /// </summary>
    /// <param name="content">Содержимое документа.</param>
    /// <param name="fileName">Имя файла документа (определяет вид проверки).</param>
    /// <returns>
    /// Перечень проблем на русском языке; пустой перечень означает, что
    /// структура корректна.
    /// </returns>
    public IReadOnlyList<string> ValidateContent(byte[] content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);

        var problems = new List<string>();

        if (IsZipSignature(content))
        {
            ValidateOpenXml(content, problems);
            return problems;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (extension is ".xml" or ".xslt" or ".xsl")
        {
            ValidateXml(content, fileName, problems);
        }

        return problems;
    }

    /// <summary>
    /// Проверяет структуру документа и возвращает результат в виде
    /// <see cref="Result{T}"/>.
    /// </summary>
    /// <param name="content">Содержимое документа.</param>
    /// <param name="fileName">Имя файла документа.</param>
    /// <returns>
    /// Успешный результат с перечнем проблем (пустым при корректной структуре)
    /// либо текст ошибки разбора документа.
    /// </returns>
    public Result<IReadOnlyList<string>> ValidateContentAsResult(byte[] content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            var problems = ValidateContent(content, fileName);

            return Result<IReadOnlyList<string>>.Success(problems);
        }
        catch (Exception ошибка)
        {
            return Result<IReadOnlyList<string>>.Failure(
                $"Не удалось проверить структуру документа «{fileName}»: {ошибка.Message}");
        }
    }

    /// <summary>
    /// Проверяет, что часть пакета OpenXML допускает подстановку маркеров:
    /// основной текст документа и колонтитулы.
    /// </summary>
    /// <param name="partName">Имя части пакета.</param>
    /// <returns><see langword="true"/>, если часть подлежит подстановке.</returns>
    public static bool IsSubstitutablePart(string partName)
    {
        if (string.IsNullOrWhiteSpace(partName))
        {
            return false;
        }

        var normalized = partName.Replace('\\', '/');

        if (!normalized.StartsWith("word/", StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = normalized["word/".Length..];

        return fileName.Equals("document.xml", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("header", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("footer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Декодирует текст части документа (UTF-8, с учётом BOM).
    /// </summary>
    /// <param name="content">Байты части документа.</param>
    /// <returns>Текст части документа.</returns>
    public static string DecodeText(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return Utf8WithoutBom.GetString(content, 3, content.Length - 3);
        }

        return Encoding.UTF8.GetString(content);
    }

    /// <summary>
    /// Определяет, что содержимое является ZIP-пакетом.
    /// </summary>
    /// <param name="content">Проверяемое содержимое.</param>
    /// <returns><see langword="true"/>, если содержимое — ZIP-пакет.</returns>
    private static bool IsZipSignature(byte[] content)
        => content.Length >= 2 && content[0] == 0x50 && content[1] == 0x4B;

    /// <summary>
    /// Читает части шаблона: текст файла либо подлежащие подстановке части
    /// пакета OpenXML.
    /// </summary>
    /// <param name="templatePath">Путь к шаблону.</param>
    /// <param name="kind">Вид шаблона.</param>
    /// <returns>Перечень частей шаблона.</returns>
    private static IReadOnlyList<DocumentTemplatePart> ReadParts(string templatePath, DocumentTemplateKind kind)
    {
        if (kind != DocumentTemplateKind.OpenXml)
        {
            return new[]
            {
                new DocumentTemplatePart(Path.GetFileName(templatePath), File.ReadAllText(templatePath, Encoding.UTF8)),
            };
        }

        var parts = new List<DocumentTemplatePart>();

        using var archive = ZipFile.OpenRead(templatePath);

        foreach (var entry in archive.Entries)
        {
            if (!IsSubstitutablePart(entry.FullName))
            {
                continue;
            }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            parts.Add(new DocumentTemplatePart(entry.FullName, DecodeText(buffer.ToArray())));
        }

        return parts;
    }

    /// <summary>
    /// Проверяет структуру пакета OpenXML: наличие обязательных частей и
    /// корректность XML основной части.
    /// </summary>
    /// <param name="content">Содержимое пакета.</param>
    /// <param name="problems">Список накопленных проблем.</param>
    private static void ValidateOpenXml(byte[] content, List<string> problems)
    {
        try
        {
            using var buffer = new MemoryStream(content);
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

            var names = archive.Entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!names.Contains(ContentTypesPart))
            {
                problems.Add($"В пакете документа отсутствует обязательная часть «{ContentTypesPart}».");
            }

            if (!names.Contains(DocumentPart))
            {
                problems.Add($"В пакете документа отсутствует обязательная часть «{DocumentPart}».");
            }

            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(item.FullName.Replace('\\', '/'), DocumentPart, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                return;
            }

            using var stream = entry.Open();
            using var partBuffer = new MemoryStream();
            stream.CopyTo(partBuffer);

            ValidateXml(partBuffer.ToArray(), DocumentPart, problems);
        }
        catch (InvalidDataException ошибка)
        {
            problems.Add($"Пакет документа не является корректным ZIP-архивом: {ошибка.Message}");
        }
    }

    /// <summary>
    /// Проверяет корректность XML-документа.
    /// </summary>
    /// <param name="content">Содержимое XML.</param>
    /// <param name="name">Имя проверяемой части (используется в сообщении).</param>
    /// <param name="problems">Список накопленных проблем.</param>
    private static void ValidateXml(byte[] content, string name, List<string> problems)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        try
        {
            using var buffer = new MemoryStream(content);
            using var reader = XmlReader.Create(buffer, settings);

            while (reader.Read())
            {
                // Чтение до конца документа выявляет ошибки структуры.
            }
        }
        catch (XmlException ошибка)
        {
            problems.Add($"Документ «{name}» не является корректным XML: {ошибка.Message}");
        }
    }
}
