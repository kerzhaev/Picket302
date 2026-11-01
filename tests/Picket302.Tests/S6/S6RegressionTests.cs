using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using Picket302.Application.Import;
using Picket302.Application.Services;
using Picket302.ComAutomation;
using Picket302.Domain.Contracts;
using Picket302.Domain.Exchange;
using Picket302.Infrastructure.Export;
using Picket302.Infrastructure.Templating;
using Picket302.Tests.ComAutomation;
using Xunit;

namespace Picket302.Tests.S6;

/// <summary>
/// Регрессионные проверки дефектов, выявленных при аудите стадии S6
/// (ТЗ §8.5, §8.9, §8.12, §9.1–9.4).
/// </summary>
public sealed class S6RegressionTests : IDisposable
{
    /// <summary>Временный каталог теста.</summary>
    private readonly string _каталог;

    /// <summary>Создаёт временный каталог теста.</summary>
    public S6RegressionTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "Picket302.S6Regression", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_каталог);
    }

    /// <summary>Удаляет временный каталог теста.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, recursive: true);
            }
        }
        catch (IOException)
        {
            // Каталог может быть занят: содержимое временное.
        }
    }

    /// <summary>
    /// Выгрузка грида сохраняет десятичную дробь: «1234,56» записывается
    /// числом 1234,56, а не 123456.
    /// </summary>
    [Fact]
    public void ExportGridToExcel_СохраняетДесятичнуюДробь()
    {
        using var grid = new DataGridView();
        grid.Columns.Add("Сумма", "Сумма");
        grid.Rows.Add("1234,56");
        grid.Rows.Add("789,10");

        var путь = Path.Combine(_каталог, "Суммы.xlsx");

        new SpreadsheetMlExporter().ExportGridToExcel(grid, путь);

        var значения = ПрочитатьЧисла(путь);

        Assert.Equal(2, значения.Count);
        Assert.Equal(1234.56d, значения[0], 4);
        Assert.Equal(789.10d, значения[1], 4);
    }

    /// <summary>
    /// Выгрузка грида сохраняет ведущие нули: почтовый индекс «012345»
    /// записывается текстом, а не числом 12345.
    /// </summary>
    [Fact]
    public void ExportGridToExcel_СохраняетВедущиеНули()
    {
        using var grid = new DataGridView();
        grid.Columns.Add("Индекс", "Индекс");
        grid.Rows.Add("012345");
        grid.Rows.Add("101000");

        var путь = Path.Combine(_каталог, "Индексы.xlsx");

        new SpreadsheetMlExporter().ExportGridToExcel(grid, путь);

        using var архив = ZipFile.OpenRead(путь);
        var лист = ПрочитатьТекст(архив, "xl/worksheets/sheet1.xml");
        var общие = ПрочитатьТекст(архив, "xl/sharedStrings.xml");

        Assert.Contains("012345", общие, StringComparison.Ordinal);
        Assert.DoesNotContain("<v>12345</v>", лист, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ошибка разбора таблицы стилей содержит строковое смещение.
    /// </summary>
    [Fact]
    public void XsltTransformEngine_СообщаетНомерСтрокиПриОшибке()
    {
        var путь = Path.Combine(_каталог, "Ошибка.xslt");

        File.WriteAllText(
            путь,
            "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
            + "<xsl:template match=\"/\"><xsl:nosuchelement/></xsl:template>"
            + "</xsl:stylesheet>",
            new UTF8Encoding(false));

        var исключение = Assert.Throws<XsltTransformException>(
            () => new XsltTransformEngine().TransformToOutput("<root/>", путь));

        Assert.Contains("строка", исключение.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Таблица стилей с включением xsl:include из соседнего каталога
    /// применяется (EVIDENCE: 11 из 16 поставляемых шаблонов используют
    /// включения вида «../styles/…»).
    /// </summary>
    [Fact]
    public void XsltTransformEngine_ПоддерживаетВключения()
    {
        var формы = Directory.CreateDirectory(Path.Combine(_каталог, "forms"));
        var стили = Directory.CreateDirectory(Path.Combine(_каталог, "styles"));

        File.WriteAllText(
            Path.Combine(стили.FullName, "Общие.xslt"),
            "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
            + "<xsl:template name=\"общее\"><out>готово</out></xsl:template>"
            + "</xsl:stylesheet>",
            new UTF8Encoding(false));

        var главный = Path.Combine(формы.FullName, "Главный.xslt");

        File.WriteAllText(
            главный,
            "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
            + "<xsl:include href=\"../styles/Общие.xslt\"/>"
            + "<xsl:template match=\"/\"><xsl:call-template name=\"общее\"/></xsl:template>"
            + "</xsl:stylesheet>",
            new UTF8Encoding(false));

        var результат = new XsltTransformEngine().TransformToOutput("<root/>", главный);

        Assert.Contains("готово", результат, StringComparison.Ordinal);
    }

    /// <summary>
    /// Импорт книги XLSX уважает «Начать импорт с» и строку заголовков.
    /// </summary>
    [Fact]
    public void ImportSession_УчитываетНачалоИмпортаДляXlsx()
    {
        var путь = Path.Combine(_каталог, "Книга.xlsx");

        var таблица = new DataTable();
        таблица.Columns.Add("Наименование", typeof(string));
        таблица.Rows.Add("ООО «Первая»");
        таблица.Rows.Add("ООО «Вторая»");
        таблица.Rows.Add("ООО «Третья»");

        new SpreadsheetMlExporter().ExportToXlsx(таблица, путь);

        var сЗаголовком = ImportSession.Start(
            путь,
            new ImportStartOptions
            {
                FilePath = путь,
                SheetIndex = 1,
                StartRow = 1,
                HasHeaderRow = true,
            });

        Assert.True(сЗаголовком.IsSuccess, сЗаголовком.Error);
        Assert.Equal(3, сЗаголовком.Value!.Records.Count);

        var соСдвигом = ImportSession.Start(
            путь,
            new ImportStartOptions
            {
                FilePath = путь,
                SheetIndex = 1,
                StartRow = 3,
                HasHeaderRow = true,
            });

        Assert.True(соСдвигом.IsSuccess, соСдвигом.Error);

        var записи = соСдвигом.Value!.Records;

        // Строка заголовков остаётся первой строкой файла, поэтому имена
        // столбцов сохраняются, а данные начинаются с указанной строки.
        Assert.Equal(2, записи.Count);
        Assert.Equal("ООО «Вторая»", записи[0].Draft.Name);

        var безЗаголовка = ImportSession.Start(
            путь,
            new ImportStartOptions
            {
                FilePath = путь,
                SheetIndex = 1,
                StartRow = 3,
                HasHeaderRow = false,
            });

        Assert.True(безЗаголовка.IsSuccess, безЗаголовка.Error);

        var записиБезЗаголовка = безЗаголовка.Value!.Records;

        // Без строки заголовков имена столбцов неизвестны, поэтому
        // автоматического сопоставления нет: поля задаёт оператор.
        Assert.Equal(2, записиБезЗаголовка.Count);
        Assert.Equal(string.Empty, записиБезЗаголовка[0].Draft.Name);
    }

    /// <summary>
    /// Импорт книги XLSX без строки заголовков считает заголовок данными.
    /// </summary>
    [Fact]
    public void ImportSession_БезЗаголовковСчитаетСтрокуДанными()
    {
        var путь = Path.Combine(_каталог, "КнигаБезЗаголовка.xlsx");

        var таблица = new DataTable();
        таблица.Columns.Add("Наименование", typeof(string));
        таблица.Rows.Add("ООО «Первая»");

        new SpreadsheetMlExporter().ExportToXlsx(таблица, путь);

        var результат = ImportSession.Start(
            путь,
            new ImportStartOptions
            {
                FilePath = путь,
                SheetIndex = 1,
                StartRow = 1,
                HasHeaderRow = false,
            });

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.Equal(2, результат.Value!.Records.Count);
    }

    /// <summary>
    /// Проверка импорта усекает длинные строки адреса и предупреждает об этом.
    /// </summary>
    [Fact]
    public void ImportValidator_УсекаетСтрокиАдреса()
    {
        var длинная = new string('а', 300);

        var черновик = new CounterpartyDraft
        {
            Name = "ООО «Тест»",
            PostalLines = [длинная, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty],
        };

        var запись = new ImportRecord
        {
            RowNumber = 1,
            Draft = черновик,
        };

        var сводка = new ImportValidator(
            new ImportBatch { Records = [запись] }).ValidateAll();

        Assert.Equal(255, черновик.PostalLines[0].Length);
        Assert.Contains(сводка.Records[0].Warnings, текст => текст.Contains("адреса", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Сессия импорта запускается с одним аргументом — путём к файлу.
    /// </summary>
    [Fact]
    public void ImportSession_ЗапускаетсяСПутемКФайлу()
    {
        var путь = Path.Combine(_каталог, "Данные.csv");

        File.WriteAllText(путь, "Наименование;ИНН\r\nООО «Первая»;7707083893\r\n", new UTF8Encoding(false));

        var результат = ImportSession.Start(путь);

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.Single(результат.Value!.Records);
    }

    /// <summary>
    /// Маппер импорта распознаёт псевдонимы единого словаря полей (ТЗ §3.2).
    /// </summary>
    [Fact]
    public void ImportFieldMapper_РаспознаётПсевдонимыСловаряПолей()
    {
        var маппер = new ImportFieldMapper();

        var привязки = маппер.Suggest(["to_str1", "to_str2"]);

        Assert.Contains(привязки, привязка => привязка.Slot == ImportFieldSlot.PostalAddress);

        var дополнительные = маппер.SuggestExtras(["ind_to"]);

        Assert.Contains(дополнительные, привязка => привязка.Field == ImportExtraField.PostalZip);
    }

    /// <summary>
    /// Фасад писем и импорта контрагентов возвращает коды ошибок по таблице
    /// 9.2 ТЗ: 2 — ошибка XML, 1 — база данных не загружена.
    /// </summary>
    [Fact]
    public void ApiLettersFacade_ВозвращаетКодыОшибок()
    {
        var среда = new FakeAutomationEnvironment { IsDatabaseLoaded = false };
        var фасад = new ApiLettersFacade(среда);

        Assert.Equal(2, фасад.CreateFromXmlLetters("<broken"));

        var корректный =
            "<EnvelopesData><EnvelopeData From=\"A\" To=\"B\" Number=\"1\" /></EnvelopesData>";

        Assert.Equal(1, фасад.CreateFromXmlLetters(корректный));
    }

    /// <summary>
    /// Флаг модального показа окон влияет на поведение среды автоматизации.
    /// </summary>
    [Fact]
    public void PEProApplication_ФлагМодальныхОконВлияетНаСреду()
    {
        var среда = new FakeAutomationEnvironment();
        var флаги = new AutomationFlagSettings(Path.Combine(_каталог, "Реестр"));

        var приложение = new PEProApplication(среда, флаги, new FakeForegroundWindowService());

        Assert.False(среда.ShowFormsAsModal);

        приложение.SetFlagShowFormsAsModal(true, out var значение);

        Assert.True(значение);
        Assert.True(среда.ShowFormsAsModal);
    }

    /// <summary>
    /// Проверка независимости экспорта от Microsoft Office охватывает сборки
    /// каталога поставки.
    /// </summary>
    [Fact]
    public void NativeOpenXmlPackager_ПодтверждаетОтсутствиеOffice()
    {
        Assert.True(new NativeOpenXmlPackager().ValidateOfficeAbsenceCompatibility());
    }

    /// <summary>
    /// Регистрация COM-сервера указывает на исполняемый файл сервера, а не
    /// оболочки; файл сервера поставляется рядом с оболочкой.
    /// </summary>
    [Fact]
    public void ComRegistration_УказываетНаФайлComСервера()
    {
        var регистрация = new ComRegistration();

        Assert.False(
            Path.GetFileName(регистрация.ServerPath)
                .Equals("Picket302.App.exe", StringComparison.OrdinalIgnoreCase),
            $"Регистрация COM-сервера указывает на файл оболочки: {регистрация.ServerPath}.");

        var каталогОболочки = НайтиКаталогОболочки();

        Assert.NotNull(каталогОболочки);

        Assert.True(
            File.Exists(Path.Combine(каталогОболочки!, ComRegistration.ServerFileName)),
            $"Файл COM-сервера {ComRegistration.ServerFileName} не поставляется рядом с оболочкой.");
    }

    /// <summary>
    /// Поставляемые примеры вызова COM-сервера содержат макрос печати реестра
    /// из XML без формы (ТЗ §8.9, функция 115).
    /// </summary>
    [Fact]
    public void ExcelCallExample_СодержитВызовPrintFromXmlRegistersWithoutForm()
    {
        var корень = НайтиКореньРепозитория();

        var модуль = File.ReadAllText(Path.Combine(корень, "Scripts", "ExcelCallExample.bas"));

        Assert.Contains("PrintFromXmlRegistersWithoutForm", модуль, StringComparison.Ordinal);

        var книга = File.ReadAllText(Path.Combine(корень, "Scripts", "ExcelCallExample.xls"));

        Assert.Contains("PrintFromXmlRegistersWithoutForm", книга, StringComparison.Ordinal);
        Assert.True(System.Xml.Linq.XDocument.Parse(книга) is not null);
    }

    /// <summary>
    /// Документ конвертов не экранирует значения повторно: символы
    /// «&amp;», «&lt;», «&gt;», кавычки возвращаются при чтении без изменений.
    /// </summary>
    [Fact]
    public void КонтрактКонвертов_НеЭкранируетЗначенияДважды()
    {
        var примечание = "a&b <c> \"d\" 'e'";

        var конверт = new PrintableEnvelope
        {
            Number = "1",
            Note = примечание,
            To = new EnvelopeParty
            {
                Name = "Получатель",
                Address = "г. Москва, ул. Тверская, д. 1",
                ZipCode = "125009",
            },
        };

        var документ = new EnvelopesDataSerializer().WriteEnvelopes([конверт]);
        var xml = документ.ToString();

        Assert.DoesNotContain("&amp;amp;", xml, StringComparison.Ordinal);

        var прочитанные = EnvelopesData.ReadListFromXml(
            ExchangeXmlReader.CreateReader(xml));

        Assert.True(прочитанные.IsSuccess, прочитанные.Error);
        Assert.Equal(примечание, прочитанные.Value![0].Note);
    }

    /// <summary>
    /// Многострочное значение сохраняется при круговом преобразовании потоковым
    /// способом: перевод строки CRLF не превращается в LF.
    /// </summary>
    /// <remarks>
    /// Проверяется потоковая запись <c>WriteListToXml</c> — основной способ
    /// контрактов. Обёртка <c>XDocument</c> сохраняет возврат каретки как символ
    /// дерева, поэтому при повторном разборе документа XML 1.0 приводит CRLF к LF;
    /// это свойство формата, а не записи.
    /// </remarks>
    [Fact]
    public void КонтрактКонвертов_СохраняетПереводыСтрок()
    {
        var примечание = "строка1\r\nстрока2\r\nстрока3";

        var конверт = new PrintableEnvelope
        {
            Number = "1",
            Note = примечание,
        };

        var буфер = new StringBuilder();

        using (var writer = ExchangeXmlWriter.CreateWriter(new StringWriter(буфер), indent: false))
        {
            EnvelopesData.WriteListToXml([конверт], writer);
        }

        var прочитанные = EnvelopesData.ReadListFromXml(ExchangeXmlReader.CreateReader(буфер.ToString()));

        Assert.True(прочитанные.IsSuccess, прочитанные.Error);
        Assert.Equal(примечание, прочитанные.Value![0].Note);
    }

    /// <summary>
    /// Адрес раскладывается по шести строкам адресного блока бланка
    /// (ТЗ §8.5, функция 77: «выравнивание 6 адресных строк»).
    /// </summary>
    [Fact]
    public void КонтрактКонвертов_ВыравниваетАдресПоШестиСтрокам()
    {
        var сторона = new EnvelopeParty
        {
            Name = "Получатель",
            Address = "г. Москва, ул. Тверская, д. 1, кв. 5",
        };

        var строки = сторона.AddressLines;

        Assert.Equal(6, строки.Length);
        Assert.Equal("г. Москва", строки[0]);
        Assert.Equal("ул. Тверская", строки[1]);
        Assert.Equal("д. 1", строки[2]);
        Assert.Equal("кв. 5", строки[3]);
        Assert.Equal(string.Empty, строки[4]);
        Assert.Equal(string.Empty, строки[5]);
    }

    /// <summary>
    /// Почтовый индекс не выкусывается из более длинного блока цифр: адрес
    /// получателя не искажается.
    /// </summary>
    [Fact]
    public void ExtractZip_НеБерётИндексИзТелефонногоНомера()
    {
        Assert.Equal("123456", ZipIndexExtractor.ExtractZip("123456, г. Москва"));
        Assert.Equal(string.Empty, ZipIndexExtractor.ExtractZip("84951234567, г. Москва"));
    }

    /// <summary>
    /// Некорректный XML не приводит к падению: все четыре контракта
    /// возвращают ошибку разбора (код 2 по таблице 9.2 ТЗ).
    /// </summary>
    [Fact]
    public void Контракты_ВозвращаютОшибкуНаНекорректномXml()
    {
        const string битый = "<EnvelopesData><EnvelopeData>";

        Assert.True(EnvelopesData.ReadListFromXml(ExchangeXmlReader.CreateReader(битый)).IsFailure);
        Assert.True(RegistersData.ReadListFromXml(ExchangeXmlReader.CreateReader(битый)).IsFailure);
        Assert.True(InventoriesData.ReadListFromXml(ExchangeXmlReader.CreateReader(битый)).IsFailure);
        Assert.True(EconomicMembers.ReadListFromXml(ExchangeXmlReader.CreateReader(битый)).IsFailure);
    }

    /// <summary>
    /// Документы остальных контрактов не экранивают значения дважды.
    /// </summary>
    [Fact]
    public void ОстальныеКонтракты_НеЭкранируютЗначенияДважды()
    {
        const string значение = "a&b <c>";

        var регистры = new RegistersDataSerializer().WriteRegisters(
        [
            new RegisterData
            {
                Number = "1",
                From = new EnvelopeParty { Name = "Отправитель" },
                Packages = [new RegisterPackage { Number = "1", To = new EnvelopeParty { Name = "Получатель" } }],
            },
        ]);

        Assert.DoesNotContain("&amp;amp;", регистры.ToString(), StringComparison.Ordinal);

        var описи = new InventoriesDataSerializer().WriteInventories(
        [
            new InventoryData
            {
                FromName = "Отправитель",
                ToName = "Получатель",
                Items = [new InventoryItem { ItemName = значение }],
            },
        ]);

        Assert.DoesNotContain("&amp;amp;", описи.ToString(), StringComparison.Ordinal);
        Assert.Contains("&amp;b &lt;c&gt;", описи.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Скрипты Windows Script Host сохраняются в кодировке, которую читает
    /// движок сценариев: UTF-16 LE с BOM.
    /// </summary>
    [Fact]
    public void Scripts_СохраненыВКодировкеWindowsScriptHost()
    {
        var корень = НайтиКореньРепозитория();

        foreach (var имя in new[] { "printenvelope.js", "printregistry.vbs" })
        {
            var байты = File.ReadAllBytes(Path.Combine(корень, "Scripts", имя));

            Assert.True(
                байты.Length >= 2 && байты[0] == 0xFF && байты[1] == 0xFE,
                $"Файл {имя} должен быть сохранён в UTF-16 LE с BOM: иначе движок сценариев не читает русские тексты.");
        }
    }

    /// <summary>Читает числовые значения ячеек листа книги XLSX.</summary>
    /// <param name="путь">Путь к книге.</param>
    /// <returns>Числовые значения ячеек в порядке следования.</returns>
    private static List<double> ПрочитатьЧисла(string путь)
    {
        using var архив = ZipFile.OpenRead(путь);
        var лист = XDocument.Parse(ПрочитатьТекст(архив, "xl/worksheets/sheet1.xml"));

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var значения = new List<double>();

        foreach (var ячейка in лист.Descendants(ns + "c"))
        {
            // Общие строки и текстовые значения числовыми не являются.
            var тип = ячейка.Attribute("t")?.Value;

            if (тип is not null && тип != "n")
            {
                continue;
            }

            var значение = ячейка.Element(ns + "v")?.Value;

            if (значение is null)
            {
                continue;
            }

            if (double.TryParse(значение, NumberStyles.Float, CultureInfo.InvariantCulture, out var число))
            {
                значения.Add(число);
            }
        }

        return значения;
    }

    /// <summary>Читает текст части пакета XLSX.</summary>
    /// <param name="архив">Архив книги.</param>
    /// <param name="часть">Путь к части пакета.</param>
    /// <returns>Содержимое части.</returns>
    private static string ПрочитатьТекст(ZipArchive архив, string часть)
    {
        var запись = архив.GetEntry(часть);

        Assert.NotNull(запись);

        using var поток = запись!.Open();
        using var чтец = new StreamReader(поток, Encoding.UTF8);

        return чтец.ReadToEnd();
    }

    /// <summary>Находит каталог сборки оболочки приложения.</summary>
    /// <returns>Путь к каталогу оболочки либо <see langword="null"/>.</returns>
    private static string? НайтиКаталогОболочки()
    {
        var корень = НайтиКореньРепозитория();
        var каталоги = Path.Combine(корень, "src", "Picket302.App", "bin");

        if (!Directory.Exists(каталоги))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(каталоги, "Picket302.App.exe", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault();
    }

    /// <summary>Находит корень репозитория по файлу решения.</summary>
    /// <returns>Путь к корню репозитория.</returns>
    private static string НайтиКореньРепозитория()
    {
        var каталог = new DirectoryInfo(AppContext.BaseDirectory);

        while (каталог is not null)
        {
            if (File.Exists(Path.Combine(каталог.FullName, "Picket302.sln")))
            {
                return каталог.FullName;
            }

            каталог = каталог.Parent;
        }

        throw new InvalidOperationException("Файл решения Picket302.sln не найден.");
    }
}
