using System.Text;
using Picket302.App.UI.Forms;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Мастер редактирования шаблонов документов и мастер выгрузки: проверка
/// контроллеров, не требующих создания окна
/// (ТЗ §8.5, функция 66; ТЗ §8.12, функция 160).
/// </summary>
public sealed class DocTemplateWizardControllerTests
{
    /// <summary>Текст корректного шаблона таблицы стилей.</summary>
    private const string КорректныйШаблон =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
          <xsl:template match="/">
            <document>#Counterparty_Name#</document>
          </xsl:template>
        </xsl:stylesheet>
        """;

    /// <summary>Текст шаблона с нарушенной структурой XML.</summary>
    private const string БитыйШаблон =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
          <xsl:template match="/">
            <document>#Counterparty_Name#</document>
          </xsl:template>
        """;

    [Fact]
    public void Контроллер_ОткрываетШаблон_ВозвращаетМаркерыВПорядкеПоявления()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = НаписатьШаблон(
                каталог,
                "template.txt",
                "Уважаемый #Counterparty_Name#! Индекс #to_zip#. Снова #Counterparty_Name#.");

            var контроллер = new DocTemplateWizardController();

            контроллер.OpenTemplate(путь);

            Assert.Equal(3, контроллер.Markers.Count);

            Assert.Equal("Counterparty_Name", контроллер.Markers[0].Name);
            Assert.Equal("to_zip", контроллер.Markers[1].Name);
            Assert.Equal("Counterparty_Name", контроллер.Markers[2].Name);

            Assert.Equal(0, контроллер.Markers[0].Order);
            Assert.Equal(1, контроллер.Markers[1].Order);
            Assert.Equal(2, контроллер.Markers[2].Order);

            Assert.Equal("#Counterparty_Name#", контроллер.Markers[0].Marker);
            Assert.Equal(1, контроллер.Markers[0].Line);
            Assert.Equal(Path.GetFullPath(путь), контроллер.TemplatePath);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Контроллер_ВставляетМаркер_ВПозициюКурсора()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = НаписатьШаблон(каталог, "template.txt", "Уважаемый #Counterparty_Name#!");
            var контроллер = new DocTemplateWizardController();

            контроллер.OpenTemplate(путь);
            контроллер.SetCursorPosition(10);

            var вставленный = контроллер.InsertMarker("to_zip");

            Assert.Equal("#to_zip#", вставленный);

            // Смещение вставки учитывается: текст маркера вставлен ровно в
            // позицию курсора, а курсор сдвинут за вставленный текст.
            Assert.Equal("Уважаемый #to_zip##Counterparty_Name#!", контроллер.TemplateText);
            Assert.Equal(18, контроллер.CursorPosition);
            Assert.True(контроллер.IsDirty);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Контроллер_ВставляетМаркер_ПоЗаданномуСмещению()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = НаписатьШаблон(каталог, "template.txt", "Уважаемый #Counterparty_Name#!");
            var контроллер = new DocTemplateWizardController();

            контроллер.OpenTemplate(путь);

            var вставленный = контроллер.InsertMarkerAt("to_zip", 0);

            Assert.Equal("#to_zip#", вставленный);
            Assert.Equal("#to_zip#Уважаемый #Counterparty_Name#!", контроллер.TemplateText);
            Assert.Equal(8, контроллер.CursorPosition);

            // Предпросмотр структуры пересчитан: вставленный маркер стал первым.
            Assert.Equal("to_zip", контроллер.Markers[0].Name);
            Assert.Equal("Counterparty_Name", контроллер.Markers[1].Name);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Контроллер_ВставляетНеизвестныйМаркер_ОтклоняетИмя()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = НаписатьШаблон(каталог, "template.txt", "Уважаемый!");
            var контроллер = new DocTemplateWizardController();

            контроллер.OpenTemplate(путь);

            Assert.Throws<ArgumentException>(() => контроллер.InsertMarker("НетТакогоМаркера"));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Контроллер_ПроверяетСтруктуру_БитыйШаблон_ВозвращаетПроблемы()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = НаписатьШаблон(каталог, "template.xslt", БитыйШаблон);
            var контроллер = new DocTemplateWizardController();

            контроллер.OpenTemplate(путь);

            var проблемы = контроллер.CheckStructure();

            Assert.NotEmpty(проблемы);
            Assert.Contains(проблемы, проблема => проблема.Contains("XML", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(проблемы.Count, контроллер.StructureProblems.Count);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Контроллер_ПроверяетСтруктуру_КорректныйШаблон_БезПроблем()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = НаписатьШаблон(каталог, "template.xslt", КорректныйШаблон);
            var контроллер = new DocTemplateWizardController();

            контроллер.OpenTemplate(путь);

            var проблемы = контроллер.CheckStructure();

            Assert.Empty(проблемы);
            Assert.Empty(контроллер.StructureProblems);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Контроллер_ПоказываетРазрешённыеЗначенияМаркеров()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = НаписатьШаблон(каталог, "template.txt", "Уважаемый #Counterparty_Name#!");

            var значения = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Counterparty_Name"] = "ООО «Ромашка»",
            };

            var контроллер = new DocTemplateWizardController(parameters: значения);

            контроллер.OpenTemplate(путь);

            var маркер = Assert.Single(контроллер.Markers);

            Assert.True(маркер.IsResolved);
            Assert.Equal("ООО «Ромашка»", маркер.Value);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Контроллер_СохраняетШаблон_ИзмененияПишутсяВФайл()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = НаписатьШаблон(каталог, "template.txt", "Уважаемый #Counterparty_Name#!");
            var контроллер = new DocTemplateWizardController();

            контроллер.OpenTemplate(путь);
            контроллер.InsertMarkerAt("to_zip", 0);

            Assert.True(контроллер.Save());

            Assert.False(контроллер.IsDirty);

            var текст = File.ReadAllText(путь, Encoding.UTF8);

            Assert.Equal("#to_zip#Уважаемый #Counterparty_Name#!", текст);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Контроллер_Экспорта_СобираетЗапрос_ИзСостоянияФормы()
    {
        var контроллер = new ExportWizardController
        {
            Format = ExchangeFormat.Csv,
            DateFormat = ExportDateFormat.Custom,
            CustomDateFormat = "dd.MM.yyyy HH:mm",
            PostAction = ExportPostAction.OpenFolder,
            OutputPath = Path.Combine(Path.GetTempPath(), "address_book.csv"),
            Delimiter = ',',
            IncludeHeader = false,
        };

        var запрос = контроллер.BuildRequest([СоздатьКонтрагента("ООО «Ромашка»")]);

        Assert.Equal(ExchangeFormat.Csv, запрос.Format);
        Assert.Equal(ExportDateFormat.Custom, запрос.DateFormat);
        Assert.Equal("dd.MM.yyyy HH:mm", запрос.CustomDateFormat);
        Assert.Equal("dd.MM.yyyy HH:mm", запрос.ResolveDateFormat());
        Assert.Equal(ExportPostAction.OpenFolder, запрос.PostAction);
        Assert.Equal(контроллер.OutputPath, запрос.OutputPath);
        Assert.Equal(',', запрос.Delimiter);
        Assert.False(запрос.IncludeHeader);
        Assert.Single(запрос.Items);
    }

    [Fact]
    public void Контроллер_Экспорта_ОпределяетЗанятыйФайл_ИПредлагаетАльтернативу()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "address_book.csv");

            File.WriteAllText(путь, "данные", new UTF8Encoding(false));

            Assert.False(ExportWizardController.IsFileBusy(путь));

            using (var поток = new FileStream(путь, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.True(ExportWizardController.IsFileBusy(путь));

                var альтернатива = ExportWizardController.SuggestAvailablePath(путь);

                Assert.Equal(Path.Combine(каталог, "address_book (2).csv"), альтернатива);
                Assert.False(ExportWizardController.IsFileBusy(альтернатива));
            }
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void ФормаМастераШаблонов_Открывается_ИПоказываетМаркеры()
    {
        using var форма = new FormDocTemplateWizard();

        Assert.NotNull(форма.Контроллер);
        Assert.Empty(форма.ПутьШаблона);
        Assert.Contains("to_zip", форма.Контроллер.FindMarkers("to_zip").Select(маркер => маркер.Name));
    }

    [Fact]
    public void ФормаМастераЭкспорта_Открывается_ИСобираетЗапрос()
    {
        using var форма = new FormExportWizard();

        Assert.NotNull(форма.Контроллер);
        Assert.Null(форма.Результат);

        форма.Контроллер.Format = ExchangeFormat.Xlsx;
        форма.Контроллер.DateFormat = ExportDateFormat.DotSeparated;
        форма.Контроллер.PostAction = ExportPostAction.OpenFile;
        форма.Контроллер.OutputPath = Path.Combine(Path.GetTempPath(), "address_book.xlsx");

        var запрос = форма.Контроллер.BuildRequest();

        Assert.Equal(ExchangeFormat.Xlsx, запрос.Format);
        Assert.Equal(ExportDateFormat.DotSeparated, запрос.DateFormat);
        Assert.Equal(ExportPostAction.OpenFile, запрос.PostAction);
    }

    /// <summary>
    /// Создаёт контрагента для выгрузки.
    /// </summary>
    /// <param name="название">Наименование контрагента.</param>
    /// <returns>Контрагент для выгрузки.</returns>
    private static Counterparty СоздатьКонтрагента(string название) => new()
    {
        Id = 1,
        Type = CounterpartyType.LegalEntity,
        Name = название,
        CreatedAt = new DateTime(2024, 5, 17, 10, 30, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2024, 5, 17, 10, 30, 0, DateTimeKind.Utc),
    };

    /// <summary>
    /// Создаёт временный каталог для шаблонов.
    /// </summary>
    /// <returns>Путь к созданному каталогу.</returns>
    private static string СоздатьКаталог()
    {
        var путь = Path.Combine(Path.GetTempPath(), $"picket302_шаблоны_{Guid.NewGuid():N}");

        Directory.CreateDirectory(путь);

        return путь;
    }

    /// <summary>
    /// Записывает шаблон во временный каталог.
    /// </summary>
    /// <param name="каталог">Каталог временных файлов.</param>
    /// <param name="имя">Имя файла шаблона.</param>
    /// <param name="текст">Текст шаблона.</param>
    /// <returns>Путь к записанному файлу.</returns>
    private static string НаписатьШаблон(string каталог, string имя, string текст)
    {
        var путь = Path.Combine(каталог, имя);

        File.WriteAllText(путь, текст, new UTF8Encoding(false));

        return путь;
    }

    /// <summary>
    /// Удаляет временный каталог.
    /// </summary>
    /// <param name="каталог">Каталог временных файлов.</param>
    private static void УдалитьКаталог(string каталог)
    {
        try
        {
            Directory.Delete(каталог, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог может быть занят: очистка не влияет на результат проверки.
        }
        catch (UnauthorizedAccessException)
        {
            // Временный каталог может быть занят: очистка не влияет на результат проверки.
        }
    }
}
