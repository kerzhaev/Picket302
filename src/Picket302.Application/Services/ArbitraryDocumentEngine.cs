using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using Picket302.Application.Templating;
using Picket302.Domain.Common;

namespace Picket302.Application.Services;

/// <summary>
/// Генератор документов произвольного вида: обрабатывает шаблоны свободной
/// формы (уведомления, претензии, акты), заменяя маркеры <c>#Field#</c> на
/// значения из модели данных (ТЗ §8.5, функция 65).
/// </summary>
/// <remarks>
/// <para>
/// Поддерживаются три вида шаблонов (ТЗ §8.5, функция 66):
/// </para>
/// <list type="bullet">
/// <item>
/// текстовый шаблон (UTF-8) либо HTML — подстановка выполняется текстово;
/// </item>
/// <item>
/// таблица стилей XSLT — параметры записываются в модель XML
/// (<c>&lt;parameters&gt;</c> с дочерними элементами по именам маркеров), после
/// чего таблица стилей применяется штатным <see cref="XslCompiledTransform"/>;
/// выполнение сценариев XSLT и разрешение внешних сущностей отключены;
/// </item>
/// <item>
/// пакет OpenXML (<c>.docx</c>) — подстановка выполняется внутри частей
/// <c>word/document.xml</c> и колонтитулов, остальные части пакета переносятся
/// без изменений.
/// </item>
/// </list>
/// <para>
/// Неизвестный маркер не подменяется: он остаётся в документе и сообщается
/// как предупреждение. Пакетная генерация (см. <see cref="DocumentBatchGenerator"/>)
/// такую запись пропускает, не прерывая формирование остальных документов.
/// </para>
/// </remarks>
public sealed class ArbitraryDocumentEngine
{
    /// <summary>Пространство имён блоков сценариев XSLT.</summary>
    private const string MsXsltNamespace = "urn:schemas-microsoft-com:xslt";

    /// <summary>Корень модели данных, передаваемой в таблицу стилей XSLT.</summary>
    public const string ModelRootElement = "parameters";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly DocumentMarkerCatalog _catalog;

    private readonly DocumentTemplateAnalyzer _analyzer;

    /// <summary>Создаёт генератор документов.</summary>
    public ArbitraryDocumentEngine()
        : this(new DocumentMarkerCatalog())
    {
    }

    /// <summary>Создаёт генератор документов.</summary>
    /// <param name="catalog">Каталог маркеров.</param>
    public ArbitraryDocumentEngine(DocumentMarkerCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _analyzer = new DocumentTemplateAnalyzer(catalog);
    }

    /// <summary>
    /// Формирует документ по шаблону свободной формы (ТЗ §8.5, функция 65).
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <returns>Содержимое сформированного документа.</returns>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    /// <exception cref="InvalidOperationException">Шаблон не удалось обработать.</exception>
    public byte[] GenerateDocument(string templatePath, Dictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return Render(templatePath, parameters).Content;
    }

    /// <summary>
    /// Формирует документ по шаблону и возвращает результат с предупреждениями.
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <returns>Результат формирования документа.</returns>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    /// <exception cref="InvalidOperationException">Шаблон не удалось обработать.</exception>
    public DocumentRenderResult Render(string templatePath, IReadOnlyDictionary<string, object> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentNullException.ThrowIfNull(parameters);

        EnsureTemplateExists(templatePath);

        return _analyzer.DetectKind(templatePath) switch
        {
            DocumentTemplateKind.OpenXml => RenderOpenXml(templatePath, parameters),
            DocumentTemplateKind.Xslt => RenderXslt(templatePath, parameters),
            _ => RenderText(templatePath, parameters),
        };
    }

    /// <summary>
    /// Формирует документ по шаблону без выбрасывания исключений.
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <returns>
    /// Успешный результат с документом либо текст ошибки на русском языке.
    /// </returns>
    public Result<DocumentRenderResult> TryRender(
        string templatePath,
        IReadOnlyDictionary<string, object> parameters)
    {
        try
        {
            return Result<DocumentRenderResult>.Success(Render(templatePath, parameters));
        }
        catch (FileNotFoundException ошибка)
        {
            return Result<DocumentRenderResult>.Failure(ошибка.Message);
        }
        catch (Exception ошибка)
        {
            return Result<DocumentRenderResult>.Failure(
                $"Не удалось сформировать документ по шаблону «{templatePath}»: {ошибка.Message}");
        }
    }

    /// <summary>
    /// Проверяет структуру шаблона документа (ТЗ §8.5, функция 66: «проверка
    /// структуры получаемого XML»).
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <returns>Перечень проблем на русском языке; пустой перечень — структура корректна.</returns>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    public IReadOnlyList<string> ValidateStructure(string templatePath) => _analyzer.Validate(templatePath);

    /// <summary>
    /// Проверяет структуру сформированного документа.
    /// </summary>
    /// <param name="content">Содержимое документа.</param>
    /// <param name="fileName">Имя файла документа (определяет вид проверки).</param>
    /// <returns>Перечень проблем на русском языке; пустой перечень — структура корректна.</returns>
    public IReadOnlyList<string> ValidateStructure(byte[] content, string fileName)
        => _analyzer.ValidateContent(content, fileName);

    /// <summary>
    /// Возвращает структуру шаблона: маркеры в порядке появления со строкой,
    /// позицией и найденным значением (ТЗ §8.5, функция 66: «предпросмотр
    /// структуры документа»).
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <returns>Перечень вхождений маркеров.</returns>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    public IReadOnlyList<DocumentMarkerOccurrence> AnalyzeTemplate(
        string templatePath,
        IReadOnlyDictionary<string, object>? parameters = null)
        => _analyzer.Analyze(templatePath, parameters);

    /// <summary>
    /// Каталог маркеров, доступных для подстановки (ТЗ §8.5, функция 66:
    /// «автоматическая вставка доступных маркеров БД»).
    /// </summary>
    public DocumentMarkerCatalog Markers => _catalog;

    /// <summary>
    /// Проверяет наличие файла шаблона.
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <exception cref="FileNotFoundException">Файл шаблона не найден.</exception>
    private static void EnsureTemplateExists(string templatePath)
    {
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Файл шаблона не найден: {templatePath}", templatePath);
        }
    }

    /// <summary>
    /// Формирует документ по текстовому шаблону либо по шаблону HTML.
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <returns>Результат формирования документа.</returns>
    private DocumentRenderResult RenderText(string templatePath, IReadOnlyDictionary<string, object> parameters)
    {
        var text = File.ReadAllText(templatePath, Encoding.UTF8);
        var outcome = ReplaceMarkers(text, parameters, xmlContext: false);

        var parts = new[] { new DocumentTemplatePart(Path.GetFileName(templatePath), text) };
        var markers = _analyzer.AnalyzeParts(parts, parameters);

        return new DocumentRenderResult(
            Utf8WithoutBom.GetBytes(outcome.Text),
            Path.GetFileName(templatePath),
            Path.GetExtension(templatePath),
            outcome.Warnings,
            outcome.Unresolved,
            markers);
    }

    /// <summary>
    /// Формирует документ по таблице стилей XSLT: параметры записываются в
    /// модель XML, после чего применяется таблица стилей.
    /// </summary>
    /// <param name="templatePath">Путь к файлу таблицы стилей.</param>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <returns>Результат формирования документа.</returns>
    private DocumentRenderResult RenderXslt(string templatePath, IReadOnlyDictionary<string, object> parameters)
    {
        var stylesheetText = File.ReadAllText(templatePath, Encoding.UTF8);
        var outcome = ReplaceMarkers(stylesheetText, parameters, xmlContext: true);

        var parts = new[] { new DocumentTemplatePart(Path.GetFileName(templatePath), stylesheetText) };
        var markers = _analyzer.AnalyzeParts(parts, parameters);

        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        XDocument stylesheet;

        try
        {
            using var reader = XmlReader.Create(new StringReader(outcome.Text), readerSettings);
            stylesheet = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ошибка)
        {
            throw new InvalidOperationException(
                $"Таблица стилей «{templatePath}» не является корректным XML: {ошибка.Message}",
                ошибка);
        }

        if (stylesheet.Descendants().Any(element => element.Name.NamespaceName == MsXsltNamespace))
        {
            throw new InvalidOperationException(
                $"Таблица стилей «{templatePath}» содержит блок сценария — выполнение сценариев XSLT запрещено.");
        }

        // Выполнение сценариев XSLT и вызов document() запрещены, разрешение
        // внешних сущностей и внешних включений отключено: таблица стилей
        // загружается через XmlReader без XmlResolver.
        var settings = new XsltSettings
        {
            EnableScript = false,
            EnableDocumentFunction = false,
        };

        var transform = new XslCompiledTransform();

        try
        {
            using var styleReader = stylesheet.CreateReader();
            transform.Load(styleReader, settings, null);
        }
        catch (XsltException ошибка)
        {
            throw new InvalidOperationException(
                $"Не удалось загрузить таблицу стилей «{templatePath}»: {ошибка.Message}",
                ошибка);
        }

        var outputSettings = new XmlWriterSettings
        {
            Encoding = Utf8WithoutBom,
            Indent = false,
            OmitXmlDeclaration = false,
        };

        using var buffer = new MemoryStream();

        try
        {
            using (var writer = XmlWriter.Create(buffer, outputSettings))
            using (var modelReader = XmlReader.Create(new StringReader(BuildModel(parameters)), readerSettings))
            {
                transform.Transform(modelReader, null, writer);
            }
        }
        catch (XsltException ошибка)
        {
            throw new InvalidOperationException(
                $"Не удалось применить таблицу стилей «{templatePath}»: {ошибка.Message}",
                ошибка);
        }

        return new DocumentRenderResult(
            buffer.ToArray(),
            Path.GetFileName(templatePath),
            Path.GetExtension(templatePath),
            outcome.Warnings,
            outcome.Unresolved,
            markers);
    }

    /// <summary>
    /// Формирует документ по пакету OpenXML: маркеры заменяются внутри
    /// <c>word/document.xml</c> и колонтитулов, остальные части пакета
    /// переносятся без изменений.
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <returns>Результат формирования документа.</returns>
    private DocumentRenderResult RenderOpenXml(string templatePath, IReadOnlyDictionary<string, object> parameters)
    {
        var entries = ReadPackage(templatePath);
        var updated = new List<(string Name, byte[] Content)>(entries.Count);
        var parts = new List<DocumentTemplatePart>();
        var warnings = new List<string>();
        var unresolved = new List<string>();

        foreach (var (name, content) in entries)
        {
            if (!DocumentTemplateAnalyzer.IsSubstitutablePart(name))
            {
                updated.Add((name, content));
                continue;
            }

            var text = DocumentTemplateAnalyzer.DecodeText(content);
            var outcome = ReplaceMarkers(text, parameters, xmlContext: true);

            parts.Add(new DocumentTemplatePart(name, text));
            warnings.AddRange(outcome.Warnings);
            unresolved.AddRange(outcome.Unresolved);

            updated.Add((name, outcome.Changed ? Utf8WithoutBom.GetBytes(outcome.Text) : content));
        }

        var markers = _analyzer.AnalyzeParts(parts, parameters);

        return new DocumentRenderResult(
            WritePackage(updated),
            Path.GetFileName(templatePath),
            Path.GetExtension(templatePath),
            warnings,
            unresolved,
            markers);
    }

    /// <summary>
    /// Заменяет маркеры <c>#Field#</c> значениями параметров.
    /// </summary>
    /// <param name="text">Исходный текст.</param>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <param name="xmlContext">
    /// Признак XML-контекста: вставляемые значения экранируются.
    /// </param>
    /// <returns>Текст с подстановкой, перечень ненайденных маркеров и предупреждения.</returns>
    private (string Text, bool Changed, IReadOnlyList<string> Unresolved, IReadOnlyList<string> Warnings) ReplaceMarkers(
        string text,
        IReadOnlyDictionary<string, object> parameters,
        bool xmlContext)
    {
        var unresolved = new List<string>();
        var changed = false;

        var result = DocumentMarkerCatalog.MarkerPattern.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;

            if (_catalog.TryGetParameterValue(name, parameters, out var value))
            {
                changed = true;
                return xmlContext ? EscapeXml(value) : value;
            }

            unresolved.Add(match.Value);
            return match.Value;
        });

        var distinct = unresolved.Distinct(StringComparer.Ordinal).ToList();
        var warnings = distinct
            .Select(marker => $"Маркер «{marker}» не найден в наборе параметров — оставлен без изменений.")
            .ToList();

        return (result, changed, distinct, warnings);
    }

    /// <summary>
    /// Экранирует значение для подстановки в XML-документ.
    /// </summary>
    /// <param name="value">Подставляемое значение.</param>
    /// <returns>Экранированное значение.</returns>
    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>
    /// Строит модель данных для таблицы стилей: корневой элемент
    /// <c>parameters</c> с дочерними элементами по именам маркеров.
    /// </summary>
    /// <param name="parameters">Значения, подставляемые вместо маркеров.</param>
    /// <returns>Текст модели данных в формате XML.</returns>
    private static string BuildModel(IReadOnlyDictionary<string, object> parameters)
    {
        var root = new XElement(ModelRootElement);

        foreach (var pair in parameters.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var name = SanitizeXmlName(pair.Key);

            if (name is null)
            {
                continue;
            }

            root.Add(new XElement(name, DocumentMarkerCatalog.FormatValue(pair.Value)));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }

    /// <summary>
    /// Приводит имя параметра к допустимому имени XML-элемента.
    /// </summary>
    /// <param name="name">Имя параметра.</param>
    /// <returns>Имя элемента либо <see langword="null"/>, если имя пустое.</returns>
    private static string? SanitizeXmlName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var builder = new StringBuilder(name.Length);

        foreach (var symbol in name)
        {
            builder.Append(char.IsLetterOrDigit(symbol) || symbol is '_' or '-' or '.' ? symbol : '_');
        }

        var result = builder.ToString();

        if (result.Length == 0)
        {
            return null;
        }

        if (!char.IsLetter(result[0]) && result[0] != '_')
        {
            result = "_" + result;
        }

        if (result.StartsWith("xml", StringComparison.OrdinalIgnoreCase))
        {
            result = "_" + result;
        }

        return result;
    }

    /// <summary>
    /// Читает части пакета OpenXML.
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    /// <returns>Перечень частей пакета.</returns>
    private static IReadOnlyList<(string Name, byte[] Content)> ReadPackage(string templatePath)
    {
        var entries = new List<(string Name, byte[] Content)>();

        using var archive = ZipFile.OpenRead(templatePath);

        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            entries.Add((entry.FullName, buffer.ToArray()));
        }

        return entries;
    }

    /// <summary>
    /// Записывает части пакета OpenXML в новый ZIP-пакет.
    /// </summary>
    /// <param name="entries">Перечень частей пакета.</param>
    /// <returns>Содержимое пакета.</returns>
    private static byte[] WritePackage(IReadOnlyList<(string Name, byte[] Content)> entries)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);

                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return buffer.ToArray();
    }
}
