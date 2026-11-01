using System.IO.Compression;
using System.Text;
using Picket302.Application.Services;
using Picket302.Application.Templating;
using Picket302.Domain.Dictionaries;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка генерации документов произвольного вида и мастера шаблонов
/// (ТЗ §8.5, функции 65 и 66).
/// </summary>
public sealed class ArbitraryDocumentEngineTests
{
    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    private const string DocumentXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body><w:p><w:r><w:t>Уважаемый #Counterparty_Name#!</w:t></w:r></w:p></w:body>
        </w:document>
        """;

    private const string ExtraPartXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
          <Application>Тестовый пакет</Application>
        </Properties>
        """;

    private const string XsltTemplate =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
          <xsl:output method="xml" encoding="utf-8" indent="no"/>
          <xsl:template match="/">
            <document>
              <name><xsl:value-of select="parameters/Counterparty_Name"/></name>
              <inn><xsl:value-of select="parameters/Counterparty_Inn"/></inn>
            </document>
          </xsl:template>
        </xsl:stylesheet>
        """;

    [Fact]
    public void GenerateDocument_ТекстовыйШаблон_ПодставляетНаименованиеОрганизации()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "notification.txt");
            File.WriteAllText(путь, "Уважаемый #Counterparty_Name#!", Encoding.UTF8);

            var движок = new ArbitraryDocumentEngine();
            var содержимое = движок.GenerateDocument(
                путь,
                new Dictionary<string, object> { ["Counterparty_Name"] = "ООО «Ромашка»" });

            var текст = Encoding.UTF8.GetString(содержимое);

            Assert.Contains("ООО «Ромашка»", текст, StringComparison.Ordinal);
            Assert.DoesNotContain("#Counterparty_Name#", текст, StringComparison.Ordinal);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Render_НеизвестныйМаркер_ОставляетМаркерИСообщаетПредупреждение()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "notification.txt");
            File.WriteAllText(путь, "Кому: #Counterparty_Name#, код: #Unknown_Field#.", Encoding.UTF8);

            var движок = new ArbitraryDocumentEngine();
            var результат = движок.Render(
                путь,
                new Dictionary<string, object> { ["Counterparty_Name"] = "ООО «Ромашка»" });

            var текст = Encoding.UTF8.GetString(результат.Content);

            Assert.Contains("#Unknown_Field#", текст, StringComparison.Ordinal);
            Assert.Contains("ООО «Ромашка»", текст, StringComparison.Ordinal);
            Assert.Contains("#Unknown_Field#", результат.UnresolvedMarkers);
            Assert.True(результат.HasWarnings);
            Assert.Contains(
                результат.Warnings,
                предупреждение => предупреждение.Contains("#Unknown_Field#", StringComparison.Ordinal)
                    && предупреждение.Contains("не найден", StringComparison.Ordinal));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void GenerateDocument_ФайлШаблонаОтсутствует_ВыбрасываетFileNotFoundException()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "нет-такого-шаблона.txt");
            var движок = new ArbitraryDocumentEngine();

            var исключение = Assert.Throws<FileNotFoundException>(() => движок.GenerateDocument(
                путь,
                new Dictionary<string, object> { ["Counterparty_Name"] = "ООО «Ромашка»" }));

            Assert.Contains(путь, исключение.Message, StringComparison.Ordinal);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Render_ТаблицаСтилейXslt_ПрименяетСтилиИПодставляетЗначение()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "letter.xslt");
            File.WriteAllText(путь, XsltTemplate, Encoding.UTF8);

            var движок = new ArbitraryDocumentEngine();
            var результат = движок.Render(
                путь,
                new Dictionary<string, object>
                {
                    ["Counterparty_Name"] = "ООО «Ромашка»",
                    ["Counterparty_Inn"] = "7701234567",
                });

            var текст = Encoding.UTF8.GetString(результат.Content);

            Assert.Contains("ООО «Ромашка»", текст, StringComparison.Ordinal);
            Assert.Contains("7701234567", текст, StringComparison.Ordinal);
            Assert.Equal("letter.xslt", результат.FileName);
            Assert.Empty(результат.UnresolvedMarkers);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Render_ПакетDocx_СохраняетПакетИЗаменяетМаркерВDocumentXml()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "акт.docx");
            СоздатьПакетDocx(путь);

            var движок = new ArbitraryDocumentEngine();
            var результат = движок.Render(
                путь,
                new Dictionary<string, object> { ["Counterparty_Name"] = "ООО «Ромашка»" });

            var части = ПрочитатьПакет(результат.Content);

            Assert.Contains("word/document.xml", части.Keys);
            Assert.Contains("[Content_Types].xml", части.Keys);
            Assert.Contains("docProps/app.xml", части.Keys);

            Assert.Contains("ООО «Ромашка»", части["word/document.xml"], StringComparison.Ordinal);
            Assert.DoesNotContain("#Counterparty_Name#", части["word/document.xml"], StringComparison.Ordinal);

            Assert.Equal(
                ExtraPartXml.Replace("\r\n", "\n", StringComparison.Ordinal),
                части["docProps/app.xml"].Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void ValidateStructure_НекорректныйXml_ВозвращаетРусскоеСообщение()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "broken.xml");
            File.WriteAllText(путь, "<document><name>Тест</document>", Encoding.UTF8);

            var движок = new ArbitraryDocumentEngine();
            var проблемы = движок.ValidateStructure(путь);

            Assert.NotEmpty(проблемы);
            Assert.Contains(
                проблемы,
                проблема => проблема.Contains("не является корректным XML", StringComparison.Ordinal));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void ValidateStructure_КорректныйXml_ПроблемНеВыявлено()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "correct.xml");
            File.WriteAllText(путь, "<document><name>Тест</name></document>", Encoding.UTF8);

            var движок = new ArbitraryDocumentEngine();

            Assert.Empty(движок.ValidateStructure(путь));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void ValidateStructure_ПакетDocxБезОбязательныхЧастей_СообщаетОПроблемах()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "broken.docx");

            using (var архив = ZipFile.Open(путь, ZipArchiveMode.Create))
            {
                var запись = архив.CreateEntry("docProps/app.xml");
                using var поток = запись.Open();
                поток.Write(Encoding.UTF8.GetBytes(ExtraPartXml));
            }

            var движок = new ArbitraryDocumentEngine();
            var проблемы = движок.ValidateStructure(путь);

            Assert.Equal(2, проблемы.Count);
            Assert.Contains(
                проблемы,
                проблема => проблема.Contains("[Content_Types].xml", StringComparison.Ordinal));
            Assert.Contains(
                проблемы,
                проблема => проблема.Contains("word/document.xml", StringComparison.Ordinal));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void AnalyzeTemplate_ВозвращаетМаркерыВПорядкеПоявленияСоЗначениями()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "notification.txt");
            File.WriteAllText(путь, "Первая строка\nКому: #Counterparty_Name#\nИНН: #Counterparty_Inn#", Encoding.UTF8);

            var движок = new ArbitraryDocumentEngine();
            var вхождения = движок.AnalyzeTemplate(
                путь,
                new Dictionary<string, object> { ["Counterparty_Name"] = "ООО «Ромашка»" });

            Assert.Equal(2, вхождения.Count);

            Assert.Equal("Counterparty_Name", вхождения[0].Name);
            Assert.Equal(2, вхождения[0].Line);
            Assert.True(вхождения[0].IsResolved);
            Assert.Equal("ООО «Ромашка»", вхождения[0].Value);

            Assert.Equal("Counterparty_Inn", вхождения[1].Name);
            Assert.Equal(3, вхождения[1].Line);
            Assert.False(вхождения[1].IsResolved);
            Assert.Null(вхождения[1].Value);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void КаталогМаркеров_СодержитПоляСловаря()
    {
        var каталог = new DocumentMarkerCatalog();

        foreach (var поле in FieldDictionary.All)
        {
            Assert.True(
                каталог.IsKnown(поле.ColumnName),
                $"Маркер поля «{поле.ColumnName}» отсутствует в каталоге.");

            var маркер = каталог.Find(поле.ColumnName);

            Assert.NotNull(маркер);
            Assert.Equal(поле.Description, маркер!.Description);
            Assert.Equal($"#{поле.ColumnName}#", маркер.Text);
        }

        var маркерКонтрагента = каталог.Find("Counterparty_Name");

        Assert.NotNull(маркерКонтрагента);
        Assert.Equal("#Counterparty_Name#", маркерКонтрагента!.Text);

        Assert.True(каталог.IsKnown("edt_str_to1"));
        Assert.True(каталог.IsKnown("КомуСтрока1"));
        Assert.False(каталог.IsKnown("СовершенноНеизвестноеПоле"));
        Assert.NotEmpty(каталог.Search("индекс"));
        Assert.Empty(каталог.Search("СовершенноНеизвестноеПоле"));
    }

    [Fact]
    public void BuildParameters_СодержитЗначенияКарточкиКонтрагента()
    {
        var каталог = new DocumentMarkerCatalog();
        var контрагент = new Picket302.Domain.Entities.Counterparty
        {
            Id = 42,
            Name = "ООО «Ромашка»",
            Inn = "7701234567",
        };

        var параметры = каталог.BuildParameters(контрагент);

        Assert.Equal("ООО «Ромашка»", параметры["Counterparty_Name"]);
        Assert.Equal("7701234567", параметры["Counterparty_Inn"]);
        Assert.Equal(42L, параметры["Counterparty_Id"]);
        Assert.DoesNotContain("Counterparty_Kpp", параметры.Keys);
    }

    private static string СоздатьКаталог()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "picket302-s6", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        return каталог;
    }

    private static void УдалитьКаталог(string каталог)
    {
        if (Directory.Exists(каталог))
        {
            Directory.Delete(каталог, recursive: true);
        }
    }

    private static void СоздатьПакетDocx(string путь)
    {
        using var архив = ZipFile.Open(путь, ZipArchiveMode.Create);

        ЗаписатьЧасть(архив, "[Content_Types].xml", ContentTypesXml);
        ЗаписатьЧасть(архив, "word/document.xml", DocumentXml);
        ЗаписатьЧасть(архив, "docProps/app.xml", ExtraPartXml);
    }

    private static void ЗаписатьЧасть(ZipArchive архив, string имя, string содержимое)
    {
        var запись = архив.CreateEntry(имя, CompressionLevel.Optimal);

        using var поток = запись.Open();
        поток.Write(Encoding.UTF8.GetBytes(содержимое));
    }

    private static Dictionary<string, string> ПрочитатьПакет(byte[] содержимое)
    {
        var части = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var буфер = new MemoryStream(содержимое);
        using var архив = new ZipArchive(буфер, ZipArchiveMode.Read);

        foreach (var запись in архив.Entries)
        {
            using var поток = запись.Open();
            using var чтение = new StreamReader(поток, Encoding.UTF8);
            части[запись.FullName] = чтение.ReadToEnd();
        }

        return части;
    }
}
