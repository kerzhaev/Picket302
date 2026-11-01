using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Picket302.Infrastructure.Export;
using Picket302.Infrastructure.Templating;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверки экспорта в OpenXML без установленного Microsoft Office
/// (ТЗ §8.5, функции 67, 68, 69 и 70).
/// </summary>
/// <remarks>
/// Файлы проверяются как пакеты Open Packaging Conventions: экспортёр
/// формирует обычный ZIP-архив с картой типов содержимого и связями,
/// поэтому проверки не требуют ни Office, ни сторонних библиотек.
/// </remarks>
public sealed class OpenXmlExportTests : IDisposable
{
    /// <summary>Пространство имён SpreadsheetML.</summary>
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Пространство имён WordprocessingML.</summary>
    private static readonly XNamespace WordprocessingNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Временный каталог теста.</summary>
    private readonly string _directory;

    /// <summary>Создаёт тест с временным каталогом.</summary>
    public OpenXmlExportTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Picket302.S6", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_directory);
    }

    /// <summary>Удаляет временный каталог теста.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временные файлы теста не влияют на результат проверки.
        }
    }

    /// <summary>
    /// Экспортированный XLSX — корректный ZIP-пакет с обязательными частями
    /// (ТЗ §8.5, функция 69).
    /// </summary>
    [Fact]
    public void XlsxPackage_ContainsRequiredParts()
    {
        var path = Path.Combine(_directory, "export.xlsx");

        new SpreadsheetMlExporter().ExportToXlsx(BuildTable(), path);

        using var archive = ZipFile.OpenRead(path);

        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/_rels/workbook.xml.rels", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
        Assert.Contains("xl/sharedStrings.xml", names);
        Assert.Contains("xl/styles.xml", names);
    }

    /// <summary>
    /// Числовая ячейка записывается числом, а не текстом (ТЗ §8.5, функция 69:
    /// «числовые колонки импортируются в Excel как формульные числа, а не как
    /// текст с апострофом»).
    /// </summary>
    [Fact]
    public void XlsxPackage_NumericCellIsWrittenAsNumber()
    {
        var path = Path.Combine(_directory, "numbers.xlsx");

        new SpreadsheetMlExporter().ExportToXlsx(BuildTable(), path);

        using var archive = ZipFile.OpenRead(path);

        var sheet = XDocument.Parse(ReadEntry(archive, "xl/worksheets/sheet1.xml"));

        var cell = sheet.Descendants(SpreadsheetNamespace + "c")
            .Single(element => (string?)element.Attribute("r") == "B2");

        var type = (string?)cell.Attribute("t");

        Assert.True(
            type is null || type == "n",
            $"Числовая ячейка должна иметь тип «n» либо не иметь типа, фактический тип: {type}.");

        var raw = cell.Element(SpreadsheetNamespace + "v")?.Value;

        Assert.False(string.IsNullOrEmpty(raw));

        Assert.True(
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number),
            $"Значение ячейки «{raw}» не является числом.");

        Assert.Equal(1234.56, number, 3);
    }

    /// <summary>
    /// Текстовая ячейка хранится в таблице общих строк (ТЗ §8.5, функция 69).
    /// </summary>
    [Fact]
    public void XlsxPackage_TextCellIsStoredInSharedStrings()
    {
        var path = Path.Combine(_directory, "text.xlsx");

        new SpreadsheetMlExporter().ExportToXlsx(BuildTable(), path);

        using var archive = ZipFile.OpenRead(path);

        var sheet = XDocument.Parse(ReadEntry(archive, "xl/worksheets/sheet1.xml"));

        var cell = sheet.Descendants(SpreadsheetNamespace + "c")
            .Single(element => (string?)element.Attribute("r") == "A2");

        Assert.Equal("s", (string?)cell.Attribute("t"));

        var index = int.Parse(
            cell.Element(SpreadsheetNamespace + "v")!.Value,
            CultureInfo.InvariantCulture);

        var sharedStrings = XDocument.Parse(ReadEntry(archive, "xl/sharedStrings.xml"));

        var values = sharedStrings.Descendants(SpreadsheetNamespace + "si")
            .Select(item => item.Value)
            .ToArray();

        Assert.Equal("ООО «Ромашка»", values[index]);
    }

    /// <summary>
    /// Карта типов содержимого и связи пакета XLSX описывают книгу, лист,
    /// таблицу общих строк и стили (ТЗ §8.5, функции 69 и 70).
    /// </summary>
    [Fact]
    public void XlsxPackage_ContentTypesAndRelationshipsAreCorrect()
    {
        var path = Path.Combine(_directory, "structure.xlsx");

        new SpreadsheetMlExporter().ExportToXlsx(BuildTable(), path);

        using var archive = ZipFile.OpenRead(path);

        var contentTypes = XDocument.Parse(ReadEntry(archive, "[Content_Types].xml"));

        XNamespace contentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

        var overrides = contentTypes.Descendants(contentTypesNamespace + "Override")
            .Select(element => (string?)element.Attribute("PartName"))
            .ToArray();

        Assert.Contains("/xl/workbook.xml", overrides);
        Assert.Contains("/xl/worksheets/sheet1.xml", overrides);
        Assert.Contains("/xl/sharedStrings.xml", overrides);
        Assert.Contains("/xl/styles.xml", overrides);

        XNamespace relationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

        var packageRelationships = XDocument.Parse(ReadEntry(archive, "_rels/.rels"));

        Assert.Contains(
            packageRelationships.Descendants(relationshipsNamespace + "Relationship"),
            element => (string?)element.Attribute("Target") == "xl/workbook.xml");

        var workbookRelationships = XDocument.Parse(ReadEntry(archive, "xl/_rels/workbook.xml.rels"));

        var targets = workbookRelationships.Descendants(relationshipsNamespace + "Relationship")
            .Select(element => (string?)element.Attribute("Target"))
            .ToArray();

        Assert.Contains("worksheets/sheet1.xml", targets);
        Assert.Contains("sharedStrings.xml", targets);
        Assert.Contains("styles.xml", targets);
    }

    /// <summary>
    /// Экспортированный DOCX — корректный ZIP-пакет с обязательными частями
    /// (ТЗ §8.5, функция 68).
    /// </summary>
    [Fact]
    public void DocxPackage_ContainsRequiredParts()
    {
        var path = Path.Combine(_directory, "document.docx");

        new WordprocessingMlExporter().ExportParagraphsToDocx(new[] { "Первая строка" }, path);

        using var archive = ZipFile.OpenRead(path);

        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("word/document.xml", names);
        Assert.Contains("word/_rels/document.xml.rels", names);
        Assert.Contains("word/styles.xml", names);
    }

    /// <summary>
    /// Документ DOCX содержит экспортированный текст настоящими абзацами
    /// WordprocessingML (ТЗ §8.5, функция 68).
    /// </summary>
    [Fact]
    public void DocxPackage_DocumentContainsTextAndWordprocessingNamespace()
    {
        var path = Path.Combine(_directory, "document.docx");

        new WordprocessingMlExporter()
            .ExportParagraphsToDocx(new[] { "Первая строка", "Вторая строка" }, path);

        using var archive = ZipFile.OpenRead(path);

        var raw = ReadEntry(archive, "word/document.xml");

        Assert.Contains("http://schemas.openxmlformats.org/wordprocessingml/2006/main", raw);
        Assert.Contains("Первая строка", raw);
        Assert.Contains("Вторая строка", raw);

        var document = XDocument.Parse(raw);

        Assert.Equal(WordprocessingNamespace + "document", document.Root!.Name);

        var paragraphs = document.Descendants(WordprocessingNamespace + "p").ToArray();

        Assert.Equal(2, paragraphs.Length);

        Assert.Equal(
            "Первая строка",
            string.Concat(paragraphs[0].Descendants(WordprocessingNamespace + "t").Select(node => node.Value)));

        Assert.Equal(
            "Вторая строка",
            string.Concat(paragraphs[1].Descendants(WordprocessingNamespace + "t").Select(node => node.Value)));
    }

    /// <summary>
    /// Стили документа объявлены в <c>word/styles.xml</c> и корректно
    /// referenced абзацами (ТЗ §8.5, функция 68).
    /// </summary>
    [Fact]
    public void DocxPackage_StylesAreDeclaredAndReferenced()
    {
        var path = Path.Combine(_directory, "styles.docx");

        new WordprocessingMlExporter().ExportParagraphsToDocx(new[] { "Текст" }, path);

        using var archive = ZipFile.OpenRead(path);

        var document = XDocument.Parse(ReadEntry(archive, "word/document.xml"));

        var styleReference = document.Descendants(WordprocessingNamespace + "pStyle")
            .Select(element => (string?)element.Attribute(WordprocessingNamespace + "val"))
            .FirstOrDefault();

        Assert.False(string.IsNullOrEmpty(styleReference));

        var styles = XDocument.Parse(ReadEntry(archive, "word/styles.xml"));

        Assert.Contains(
            styles.Descendants(WordprocessingNamespace + "style"),
            element => (string?)element.Attribute(WordprocessingNamespace + "styleId") == styleReference);
    }

    /// <summary>
    /// Упаковщик сохраняет и возвращает текстовые и двоичные части пакета
    /// (ТЗ §8.5, функция 70).
    /// </summary>
    [Fact]
    public void Packager_RoundTripsTextAndBinaryParts()
    {
        var packager = new NativeOpenXmlPackager();
        var path = Path.Combine(_directory, "package.zip");

        var binary = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0xFF, 0x10, 0x20 };

        packager.WritePackage(path, new[]
        {
            OpenXmlPackagePart.FromText("[Content_Types].xml", "<Types/>"),
            OpenXmlPackagePart.FromText("custom/data.xml", "<data>текст</data>"),
            OpenXmlPackagePart.FromBytes("media/image.bin", binary),
        });

        var parts = packager.ReadPackage(path);

        Assert.Equal(3, parts.Count);

        // Карта типов содержимого открывает пакет OPC.
        Assert.Equal("[Content_Types].xml", parts[0].Name);

        var text = parts.Single(part => part.Name == "custom/data.xml");

        Assert.Equal("<data>текст</data>", Encoding.UTF8.GetString(text.Content));
        Assert.Equal("<data>текст</data>", packager.ReadTextPart(path, "custom/data.xml"));

        var bytes = parts.Single(part => part.Name == "media/image.bin");

        Assert.False(bytes.IsText);
        Assert.Equal(binary, bytes.Content);
    }

    /// <summary>
    /// Вложенный ZIP-архив, переданный внутри XML-части, извлекается штатным
    /// средством (ТЗ §9.4; §8.5, функция 70).
    /// </summary>
    [Fact]
    public void Packager_ExtractsNestedZipFromXmlPart()
    {
        var packager = new NativeOpenXmlPackager();
        var path = Path.Combine(_directory, "package.zip");

        using var nested = new MemoryStream();

        using (var archive = new ZipArchive(nested, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("blank.xml");

            using var stream = entry.Open();

            stream.Write(Encoding.UTF8.GetBytes("<blank/>"));
        }

        var payload = Convert.ToBase64String(nested.ToArray());

        packager.WritePackage(path, new[]
        {
            OpenXmlPackagePart.FromText("[Content_Types].xml", "<Types/>"),
            OpenXmlPackagePart.FromText("custom/data.xml", $"<data><archive>{payload}</archive></data>"),
        });

        var extracted = packager.ExtractNestedZipBytes(path, "custom/data.xml");

        using var buffer = new MemoryStream(extracted);
        using var result = new ZipArchive(buffer, ZipArchiveMode.Read);

        Assert.Contains("blank.xml", result.Entries.Select(entry => entry.FullName));
    }

    /// <summary>
    /// Проверка независимости экспорта от Microsoft Office возвращает успех
    /// (ТЗ §8.5, функция 70).
    /// </summary>
    [Fact]
    public void Packager_ValidatesOfficeAbsence()
    {
        Assert.True(new NativeOpenXmlPackager().ValidateOfficeAbsenceCompatibility());
    }

    /// <summary>
    /// XSLT-преобразование простого XML простой таблицей стилей даёт ожидаемый
    /// результат (ТЗ §8.5, функция 67).
    /// </summary>
    [Fact]
    public void XsltTransform_ProducesExpectedOutput()
    {
        var stylesheet = Path.Combine(_directory, "transform.xslt");

        File.WriteAllText(stylesheet, """
            <?xml version="1.0" encoding="utf-8"?>
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" indent="no"/>
              <xsl:template match="/root">
                <result>
                  <xsl:value-of select="item"/>
                </result>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var engine = new XsltTransformEngine();

        var output = engine.TransformToOutput("<root><item>Привет</item></root>", stylesheet);

        var document = XDocument.Parse(output);

        Assert.Equal("result", document.Root!.Name.LocalName);
        Assert.Equal("Привет", document.Root.Value);
    }

    /// <summary>
    /// Испорченная таблица стилей прерывает преобразование с сообщением на
    /// русском языке и указанием строкового смещения (ТЗ §8.5, функция 67).
    /// </summary>
    [Fact]
    public void XsltTransform_BrokenStylesheet_ReportsRussianError()
    {
        var stylesheet = Path.Combine(_directory, "broken.xslt");

        File.WriteAllText(stylesheet, """
            <?xml version="1.0" encoding="utf-8"?>
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="/root">
                <result>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var engine = new XsltTransformEngine();

        var succeeded = engine.TryTransform("<root><item>Привет</item></root>", stylesheet, out var output, out var error);

        Assert.False(succeeded);
        Assert.Equal(string.Empty, output);

        Assert.False(string.IsNullOrEmpty(error));
        Assert.Contains("Ошибка", error, StringComparison.Ordinal);
        Assert.Contains("строка", error, StringComparison.Ordinal);

        var exception = Assert.Throws<XsltTransformException>(
            () => engine.TransformToOutput("<root><item>Привет</item></root>", stylesheet));

        Assert.Contains("Ошибка", exception.Message, StringComparison.Ordinal);
        Assert.Contains("строка", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Экспорт документа по таблице стилей XSLT: преобразованный текст
    /// попадает в WordprocessingML (ТЗ §8.5, функции 67 и 68).
    /// </summary>
    [Fact]
    public void DocxPackage_ExportsTransformedXml()
    {
        var stylesheet = Path.Combine(_directory, "word.xslt");

        File.WriteAllText(stylesheet, """
            <?xml version="1.0" encoding="utf-8"?>
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:output method="xml" indent="no"/>
              <xsl:template match="/root">
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:r>
                        <w:t>
                          <xsl:value-of select="item"/>
                        </w:t>
                      </w:r>
                    </w:p>
                  </w:body>
                </w:document>
              </xsl:template>
            </xsl:stylesheet>
            """);

        var path = Path.Combine(_directory, "transformed.docx");

        new WordprocessingMlExporter()
            .ExportToDocx("<root><item>Текст документа</item></root>", stylesheet, path);

        using var archive = ZipFile.OpenRead(path);

        var document = XDocument.Parse(ReadEntry(archive, "word/document.xml"));

        Assert.Equal(WordprocessingNamespace + "document", document.Root!.Name);

        Assert.Equal(
            "Текст документа",
            string.Concat(document.Descendants(WordprocessingNamespace + "t").Select(node => node.Value)).Trim());
    }

    /// <summary>
    /// Таблица для экспорта: текстовый и числовой столбцы (ТЗ §8.5, функция 69).
    /// </summary>
    /// <returns>Модель экспортируемой таблицы.</returns>
    private static ExportTable BuildTable() => new ExportTable(
        new[]
        {
            new ExportColumn("Контрагент"),
            new ExportColumn("Сумма", ExportColumnType.Number),
        },
        new[]
        {
            new object?[] { "ООО «Ромашка»", 1234.56 },
            new object?[] { "ИП Иванов", 42 },
        });

    /// <summary>Читает часть пакета как текст.</summary>
    /// <param name="archive">Открытый архив пакета.</param>
    /// <param name="name">Имя части.</param>
    /// <returns>Текст части.</returns>
    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);

        Assert.NotNull(entry);

        using var stream = entry!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
