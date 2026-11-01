using System.Text;
using System.Xml;
using System.Xml.Linq;
using Picket302.Infrastructure.Templating;

namespace Picket302.Infrastructure.Export;

/// <summary>
/// Абзац документа WordprocessingML (ТЗ §8.5, функция 68).
/// </summary>
/// <param name="Text">Текст абзаца.</param>
/// <param name="StyleId">
/// Идентификатор стиля абзаца; должен быть объявлен в части <c>word/styles.xml</c>.
/// </param>
public sealed record WordprocessingParagraph(string Text, string StyleId = "Normal");

/// <summary>
/// Экспорт в Microsoft Word (DOCX) без установленного MS Office
/// (ТЗ §8.5, функция 68 — «Экспорт в Microsoft Word без установленного MS Office»).
/// </summary>
/// <remarks>
/// <para>
/// DOCX — это пакет Open Packaging Conventions: ZIP-архив с картой типов
/// содержимого <c>[Content_Types].xml</c>, связями <c>_rels/.rels</c> и
/// частями <c>word/document.xml</c>, <c>word/_rels/document.xml.rels</c>,
/// <c>word/styles.xml</c>. Пакет собирается штатным
/// <see cref="NativeOpenXmlPackager"/> (ТЗ §8.5, функция 70) на базе
/// <c>System.IO.Compression</c>: ни OLE, ни COM, ни Interop-библиотеки
/// Microsoft Office не используются.
/// </para>
/// <para>
/// Текст документа — настоящие абзацы WordprocessingML
/// (<c>w:p</c>/<c>w:r</c>/<c:w:t</c>) со ссылкой на стиль, объявленный
/// в <c>word/styles.xml</c>.
/// </para>
/// </remarks>
public sealed class WordprocessingMlExporter
{
    /// <summary>Пространство имён WordprocessingML.</summary>
    private const string WordprocessingNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Карта типов содержимого пакета DOCX.</summary>
    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
        </Types>
        """;

    /// <summary>Связи пакета DOCX: корневой документ.</summary>
    private const string PackageRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    /// <summary>Связи части документа: таблица стилей.</summary>
    private const string DocumentRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    /// <summary>Таблица стилей документа WordprocessingML.</summary>
    private const string StylesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:docDefaults>
            <w:rPrDefault>
              <w:rPr>
                <w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman" w:cs="Times New Roman"/>
                <w:sz w:val="24"/>
                <w:szCs w:val="24"/>
                <w:lang w:val="ru-RU"/>
              </w:rPr>
            </w:rPrDefault>
            <w:pPrDefault/>
          </w:docDefaults>
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
            <w:name w:val="Normal"/>
            <w:qFormat/>
          </w:style>
          <w:style w:type="paragraph" w:styleId="Heading1">
            <w:name w:val="heading 1"/>
            <w:basedOn w:val="Normal"/>
            <w:next w:val="Normal"/>
            <w:qFormat/>
            <w:pPr>
              <w:keepNext/>
              <w:spacing w:before="240" w:after="120"/>
              <w:outlineLvl w:val="0"/>
            </w:pPr>
            <w:rPr>
              <w:b/>
              <w:sz w:val="32"/>
            </w:rPr>
          </w:style>
        </w:styles>
        """;

    /// <summary>Упаковщик пакетов OPC.</summary>
    private readonly NativeOpenXmlPackager _packager;

    /// <summary>Движок XSLT-преобразования.</summary>
    private readonly XsltTransformEngine _transformEngine;

    /// <summary>Создаёт экспортёр DOCX.</summary>
    /// <param name="packager">Упаковщик пакетов OPC; по умолчанию новый.</param>
    /// <param name="transformEngine">Движок XSLT; по умолчанию новый.</param>
    public WordprocessingMlExporter(
        NativeOpenXmlPackager? packager = null,
        XsltTransformEngine? transformEngine = null)
    {
        _packager = packager ?? new NativeOpenXmlPackager();
        _transformEngine = transformEngine ?? new XsltTransformEngine();
    }

    /// <summary>
    /// Формирует файл Word (DOCX) из XML-данных, преобразованных таблицей
    /// стилей XSLT (ТЗ §8.5, функция 68:
    /// <c>public void ExportToDocx(string sourceXml, string xsltTemplate, string outputDocxPath)</c>).
    /// </summary>
    /// <param name="sourceXml">Исходные XML-данные документа.</param>
    /// <param name="xsltTemplate">Путь к файлу таблицы стилей XSLT.</param>
    /// <param name="outputDocxPath">Путь к целевому файлу DOCX.</param>
    public void ExportToDocx(string sourceXml, string xsltTemplate, string outputDocxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDocxPath);

        var paragraphs = ExtractParagraphs(_transformEngine.TransformToOutput(sourceXml, xsltTemplate));

        WriteDocument(paragraphs, outputDocxPath);
    }

    /// <summary>
    /// Формирует файл Word (DOCX) в поток из XML-данных, преобразованных
    /// таблицей стилей XSLT (ТЗ §8.5, функция 68).
    /// </summary>
    /// <param name="sourceXml">Исходные XML-данные документа.</param>
    /// <param name="xsltTemplate">Путь к файлу таблицы стилей XSLT.</param>
    /// <param name="output">Целевой поток; остаётся открытым после записи.</param>
    public void ExportToDocx(string sourceXml, string xsltTemplate, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var paragraphs = ExtractParagraphs(_transformEngine.TransformToOutput(sourceXml, xsltTemplate));

        WriteDocument(paragraphs, output);
    }

    /// <summary>
    /// Формирует файл Word (DOCX) из готового списка абзацев
    /// (ТЗ §8.5, функция 68 — вариант без XSLT и без UI).
    /// </summary>
    /// <param name="paragraphs">Абзацы документа.</param>
    /// <param name="outputDocxPath">Путь к целевому файлу DOCX.</param>
    public void ExportParagraphsToDocx(IEnumerable<string> paragraphs, string outputDocxPath)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDocxPath);

        var normalized = paragraphs.Select(text => new WordprocessingParagraph(text ?? string.Empty));

        WriteDocument(normalized, outputDocxPath);
    }

    /// <summary>
    /// Формирует файл Word (DOCX) из готового списка абзацев в поток
    /// (ТЗ §8.5, функция 68).
    /// </summary>
    /// <param name="paragraphs">Абзацы документа.</param>
    /// <param name="output">Целевой поток; остаётся открытым после записи.</param>
    public void ExportParagraphsToDocx(IEnumerable<string> paragraphs, Stream output)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(output);

        var normalized = paragraphs.Select(text => new WordprocessingParagraph(text ?? string.Empty));

        WriteDocument(normalized, output);
    }

    /// <summary>
    /// Собирает содержимое части <c>word/document.xml</c> по абзацам
    /// (ТЗ §8.5, функция 68).
    /// </summary>
    /// <param name="paragraphs">Абзацы документа.</param>
    /// <returns>XML части документа WordprocessingML.</returns>
    public static string BuildDocumentXml(IEnumerable<WordprocessingParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);

        XNamespace w = WordprocessingNamespace;

        var body = new XElement(w + "body");

        foreach (var paragraph in paragraphs)
        {
            body.Add(new XElement(
                w + "p",
                new XElement(
                    w + "pPr",
                    new XElement(w + "pStyle", new XAttribute(w + "val", paragraph.StyleId))),
                new XElement(
                    w + "r",
                    new XElement(
                        w + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"),
                        paragraph.Text))));
        }

        body.Add(new XElement(
            w + "sectPr",
            new XElement(
                w + "pgSz",
                new XAttribute(w + "w", "11906"),
                new XAttribute(w + "h", "16838")),
            new XElement(
                w + "pgMar",
                new XAttribute(w + "top", "1134"),
                new XAttribute(w + "right", "850"),
                new XAttribute(w + "bottom", "1134"),
                new XAttribute(w + "left", "1701"),
                new XAttribute(w + "header", "708"),
                new XAttribute(w + "footer", "708"),
                new XAttribute(w + "gutter", "0"))));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(w + "document", body));

        return SerializeXml(document);
    }

    /// <summary>Записывает документ DOCX в файл.</summary>
    /// <param name="paragraphs">Абзацы документа.</param>
    /// <param name="outputDocxPath">Путь к целевому файлу DOCX.</param>
    private void WriteDocument(IEnumerable<WordprocessingParagraph> paragraphs, string outputDocxPath)
    {
        var parts = BuildPackage(paragraphs);

        _packager.WritePackage(outputDocxPath, parts);
    }

    /// <summary>Записывает документ DOCX в поток.</summary>
    /// <param name="paragraphs">Абзацы документа.</param>
    /// <param name="output">Целевой поток.</param>
    private void WriteDocument(IEnumerable<WordprocessingParagraph> paragraphs, Stream output)
    {
        var parts = BuildPackage(paragraphs);

        _packager.WritePackageToStream(output, parts);
    }

    /// <summary>Формирует части пакета DOCX.</summary>
    /// <param name="paragraphs">Абзацы документа.</param>
    /// <returns>Части пакета в порядке записи.</returns>
    private static IReadOnlyList<OpenXmlPackagePart> BuildPackage(IEnumerable<WordprocessingParagraph> paragraphs)
    {
        var documentXml = BuildDocumentXml(paragraphs);

        return new List<OpenXmlPackagePart>
        {
            new(NativeOpenXmlPackager.ContentTypesPartName, ContentTypesXml),
            new(NativeOpenXmlPackager.PackageRelationshipsPartName, PackageRelationshipsXml),
            new("word/document.xml", documentXml),
            new("word/_rels/document.xml.rels", DocumentRelationshipsXml),
            new("word/styles.xml", StylesXml),
        };
    }

    /// <summary>
    /// Извлекает абзацы документа из результата XSLT-преобразования.
    /// </summary>
    /// <param name="transformed">Результат преобразования.</param>
    /// <returns>Абзацы документа WordprocessingML.</returns>
    private static IReadOnlyList<WordprocessingParagraph> ExtractParagraphs(string transformed)
    {
        ArgumentNullException.ThrowIfNull(transformed);

        XDocument document;

        try
        {
            document = XDocument.Parse(transformed, LoadOptions.None);
        }
        catch (XmlException)
        {
            // Результат не является XML (например, текстовый метод вывода
            // XSLT): каждая строка трактуется как отдельный абзац.
            return SplitLines(transformed);
        }

        XNamespace w = WordprocessingNamespace;

        var root = document.Root;

        if (root is null)
        {
            return Array.Empty<WordprocessingParagraph>();
        }

        if (root.Name == w + "document")
        {
            var body = root.Element(w + "body");

            if (body is null)
            {
                return Array.Empty<WordprocessingParagraph>();
            }

            return body.Elements(w + "p")
                .Select(paragraph => new WordprocessingParagraph(
                    ReadText(paragraph),
                    ReadStyleId(paragraph) ?? "Normal"))
                .ToArray();
        }

        var children = root.Elements().ToArray();

        if (children.Length == 0)
        {
            return new[] { new WordprocessingParagraph(ReadText(root), "Normal") };
        }

        return children
            .Select(element => new WordprocessingParagraph(ReadText(element), "Normal"))
            .ToArray();
    }

    /// <summary>Читает текст абзаца WordprocessingML.</summary>
    /// <param name="element">Элемент абзаца.</param>
    /// <returns>Текст абзаца.</returns>
    private static string ReadText(XElement element)
    {
        XNamespace w = WordprocessingNamespace;

        var runs = element.Descendants(w + "t").Select(node => node.Value);

        var text = string.Concat(runs).Trim();

        if (text.Length > 0)
        {
            return text;
        }

        return string.Concat(element.DescendantNodes().OfType<XText>().Select(node => node.Value)).Trim();
    }

    /// <summary>Читает идентификатор стиля абзаца WordprocessingML.</summary>
    /// <param name="paragraph">Элемент абзаца.</param>
    /// <returns>Идентификатор стиля; <see langword="null"/> — стиль не задан.</returns>
    private static string? ReadStyleId(XElement paragraph)
    {
        XNamespace w = WordprocessingNamespace;

        return paragraph.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value;
    }

    /// <summary>Делит текст на строки-абзацы.</summary>
    /// <param name="text">Исходный текст.</param>
    /// <returns>Список абзацев.</returns>
    private static IReadOnlyList<WordprocessingParagraph> SplitLines(string text) =>
        text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => new WordprocessingParagraph(line.Trim(), "Normal"))
            .ToArray();

    /// <summary>Сериализует XML-документ в строку UTF-8 без BOM.</summary>
    /// <param name="document">Документ.</param>
    /// <returns>Текст XML.</returns>
    private static string SerializeXml(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false,
        };

        using var buffer = new MemoryStream();

        using (var writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
