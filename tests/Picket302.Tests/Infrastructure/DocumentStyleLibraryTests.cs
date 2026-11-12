using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Xsl;
using Picket302.Application.Services;
using Picket302.Application.Templating;
using Picket302.Domain.Queries;
using Picket302.Infrastructure.Export;
using Picket302.Infrastructure.Queries;
using Picket302.Infrastructure.Templates;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// S30 (ТЗ §8.4, функция 67): инвентаризация и валидация поставляемых
/// шаблонов документов — 12 XML (описания запросов слоя данных) и 16 XSLT
/// (стили для конвейера WordprocessingML). Реальная применимость и
/// независимость от MS Office доказываются генерацией DOCX штатным
/// XslCompiledTransform без Office.
/// </summary>
public sealed class DocumentStyleLibraryTests
{
    /// <summary>Модель данных с типовыми значениями маркеров.</summary>
    private static readonly Dictionary<string, object> Модель =
        new()
        {
            ["Counterparty_Name"] = "ООО «Приёмочная Ромашка»",
            ["Counterparty_ShortName"] = "Приёмочная Ромашка",
            ["Counterparty_Inn"] = "7707083893",
            ["Counterparty_Kpp"] = "770701001",
            ["Counterparty_Ogrn"] = "1157746294375",
            ["Counterparty_Rs"] = "40702810400000012345",
            ["Counterparty_Ks"] = "30101810400000000225",
            ["Counterparty_Bik"] = "044525225",
            ["Counterparty_BankName"] = "ПАО «Приёмочный банк»",
            ["Counterparty_DirectorFio"] = "Иванов Иван Иванович",
            ["Counterparty_Position"] = "Генеральный директор",
            ["Counterparty_Phone"] = "+7 (495) 000-00-00",
            ["Counterparty_Email"] = "acceptance@example.test",
            ["Counterparty_Address_Main"] = "101000, г. Москва, ул. Тестовая, д. 10",
            ["Counterparty_Address_Legal"] = "101000, г. Москва, ул. Тестовая, д. 10, стр. 2",
            ["Counterparty_Address_Postal"] = "101000, г. Москва, а/я 1",
            ["to_zip"] = "101000",
        };

    [Fact]
    public void Инвентаризация_ДвенадцатьОпределенийЗапросовВРесурсах()
    {
        var сборка = typeof(BuiltinReportQueryCatalog).Assembly;

        var имена = сборка
            .GetManifestResourceNames()
            .Where(имя => имя.StartsWith(BuiltinReportQueryCatalog.ResourcePrefix, StringComparison.Ordinal))
            .Where(имя => имя.EndsWith(".xml", StringComparison.Ordinal))
            .OrderBy(имя => имя, (IComparer<string>)StringComparer.Ordinal)
            .ToList();

        // ТЗ §8.4, функция 67: «12 XML» — описания запросов слоя данных.
        Assert.Equal(12, имена.Count);

        var сериализатор = new ReportQueryXmlSerializer();
        var валидатор = new QueryValidator();

        foreach (var имя in имена)
        {
            using var поток = сборка.GetManifestResourceStream(имя)!;
            using var читатель = new StreamReader(поток);
            var определение = сериализатор.Deserialize(читатель.ReadToEnd());

            Assert.False(string.IsNullOrWhiteSpace(определение.Id));
            валидатор.ValidateDefinition(определение);

            // Поставка §10.1 — SQLite; PostgreSQL-вариант для совместимости.
            Assert.NotNull(определение.GetCommand(QueryDialects.Sqlite).Sql);
            Assert.NotNull(определение.GetCommand(QueryDialects.PostgreSql).Sql);
        }
    }

    [Fact]
    public void Инвентаризация_ШестнадцатьСтилей_УникальныеИдентификаторы()
    {
        var стили = DocumentStyleLibrary.Styles;

        Assert.Equal(16, стили.Count);
        Assert.Equal(16, стили.Select(стиль => стиль.Id).Distinct().Count());

        foreach (var стиль in стили)
        {
            Assert.False(string.IsNullOrWhiteSpace(стиль.Name));

            var текст = DocumentStyleLibrary.LoadFromResource(стиль.Id);

            Assert.False(string.IsNullOrWhiteSpace(текст));
            Assert.Contains("<xsl:stylesheet", текст, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void КаждыйСтиль_КомпилируетсяШтатнымXslCompiledTransform()
    {
        // Настройки по умолчанию: сценарии и document() запрещены.
        var движок = new XslCompiledTransform();

        foreach (var стиль in DocumentStyleLibrary.Styles)
        {
            var текст = DocumentStyleLibrary.LoadFromResource(стиль.Id);

            using var читатель = new StringReader(текст);
            using var xmlЧитатель = System.Xml.XmlReader.Create(читатель);

            var ошибка = Record.Exception(() => движок.Load(xmlЧитатель));

            Assert.True(ошибка is null, $"Стиль «{стиль.Id}» не компилируется: {ошибка?.Message}");
        }
    }

    [Fact]
    public void КаждыйСтиль_ИспользуетТолькоМаркерыКаталога()
    {
        var каталог = DocumentMarkerCatalog.Shared;
        var известные = каталог.MarkerNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var выражения = new Regex("select=\"([A-Za-zА-Яа-яЁё_][A-Za-zА-Яа-яЁё0-9_.]*)\"");

        foreach (var стиль in DocumentStyleLibrary.Styles)
        {
            var текст = DocumentStyleLibrary.LoadFromResource(стиль.Id);

            foreach (Match совпадение in выражения.Matches(текст))
            {
                var имя = совпадение.Groups[1].Value;

                Assert.True(
                    известные.Contains(имя),
                    $"Стиль «{стиль.Id}» использует маркер «{имя}», отсутствующий в каталоге.");
            }
        }
    }

    /// <summary>
    /// Реальная применимость: каждый стиль рендерится движком документов
    /// (функция 65) в WordprocessingML-документ с подстановкой значений.
    /// </summary>
    [Fact]
    public void КаждыйСтиль_РендеритсяДвижкомВДокументWordprocessingML()
    {
        var движок = new ArbitraryDocumentEngine();
        var каталог = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "p302-s30-styles-" + Guid.NewGuid().ToString("N")));

        каталог.Create();

        try
        {
            foreach (var стиль in DocumentStyleLibrary.Styles)
            {
                var путь = Path.Combine(каталог.FullName, стиль.Id + ".xslt");
                File.WriteAllText(путь, DocumentStyleLibrary.LoadFromResource(стиль.Id));

                var результат = движок.Render(путь, Модель);

                Assert.True(результат.Warnings.Count == 0, $"Стиль «{стиль.Id}»: {string.Join("; ", результат.Warnings)}");

                var текст = System.Text.Encoding.UTF8.GetString(результат.Content);
                var документ = XDocument.Parse(текст);
                var пространство = документ.Root!.Name.NamespaceName;

                Assert.Contains("wordprocessingml", пространство, StringComparison.Ordinal);
                Assert.NotNull(документ.Root!.Element(XNamespace.Get(пространство) + "body"));

                // Подстановка значений реально произошла.
                Assert.Contains("ООО «Приёмочная Ромашка»", текст, StringComparison.Ordinal);
            }
        }
        finally
        {
            каталог.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Независимость от MS Office: стиль через ExportToDocx формирует
    /// корректный DOCX-пакет (функция 68) без Office — сборка пакета штатным
    /// System.IO.Compression.
    /// </summary>
    [Fact]
    public void Стиль_ЭкспортируетсяВДокхБезOffice()
    {
        var каталог = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "p302-s30-docx-" + Guid.NewGuid().ToString("N")));

        каталог.Create();

        try
        {
            var стильПуть = Path.Combine(каталог.FullName, "payment-reminder.xslt");
            File.WriteAllText(стильПуть, DocumentStyleLibrary.LoadFromResource("payment-reminder"));

            // Модель данных для таблицы стилей: <parameters> с дочерними
            // элементами по именам маркеров (формат ArbitraryDocumentEngine).
            var модельXml = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    "parameters",
                    Модель.OrderBy(пара => пара.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(пара => new XElement(пара.Key, пара.Value?.ToString() ?? string.Empty))))
                .ToString();

            var докхПуть = Path.Combine(каталог.FullName, "payment-reminder.docx");

            new WordprocessingMlExporter().ExportToDocx(модельXml, стильПуть, докхПуть);

            Assert.True(File.Exists(докхПуть));

            using var архив = System.IO.Compression.ZipFile.OpenRead(докхПуть);

            Assert.NotNull(архив.GetEntry("word/document.xml"));

            var запись = архив.GetEntry("word/document.xml")!;

            using var читатель = new StreamReader(запись.Open());
            var документ = читатель.ReadToEnd();

            Assert.Contains("ООО «Приёмочная Ромашка»", документ, StringComparison.Ordinal);
        }
        finally
        {
            каталог.Delete(recursive: true);
        }
    }
}
