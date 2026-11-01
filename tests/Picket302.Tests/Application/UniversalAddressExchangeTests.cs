using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка универсального обмена адресными списками (ТЗ §8.5, функция 71:
/// «Импорт и экспорт адресных списков в форматах TXT/CSV/HTML/RTF/XLS/XLSX»).
/// </summary>
/// <remarks>
/// Проверка из ТЗ: «Экспорт в CSV с последующим импортом в чистую базу полностью
/// восстанавливает все поля адресов».
/// </remarks>
public sealed class UniversalAddressExchangeTests
{
    private readonly UniversalAddressExchange _обмен = new();

    [Fact]
    public void ExportData_ЭкспортВCsvИИмпортВЧистуюБазу_ВосстанавливаетВсеПоляАдресов()
    {
        var контрагент = СоздатьКонтрагента();

        using var поток = new MemoryStream();

        _обмен.ExportData(
            [контрагент],
            ExchangeFormat.Csv,
            поток,
            new AddressExchangeOptions { Delimiter = ';' });

        поток.Position = 0;

        var профиль = new CsvProfile
        {
            Name = "Проверка round-trip",
            EncodingName = "utf-8",
            Delimiter = ';',
            QuoteChar = '"',
            HasHeaderRow = true,
            FirstDataRow = 1,
        };

        var черновики = _обмен.ImportData(поток, профиль);
        var черновик = Assert.Single(черновики);

        Assert.Equal("ООО «Ромашка»", черновик.Name);
        Assert.Equal("Ромашка", черновик.ShortName);
        Assert.Equal(CounterpartyType.LegalEntity, черновик.Type);
        Assert.Equal("7701234567", черновик.Inn);
        Assert.Equal("770101001", черновик.Kpp);
        Assert.Equal("1107746123456", черновик.Ogrn);
        Assert.Equal("+7 495 000-00-00", черновик.Phone);
        Assert.Equal("info@romashka.ru", черновик.Email);
        Assert.Equal("Отгрузка по вторникам", черновик.Note);

        Assert.Equal("101000", черновик.PostalZip);
        Assert.Equal(
            new[] { "ООО «Ромашка»", "ул. Мясницкая", "д. 1", string.Empty, "г. Москва", "Московская обл." },
            черновик.PostalLines);

        Assert.Equal("770000", черновик.LegalZip);
        Assert.Equal(
            new[] { "ООО «Ромашка»", "ул. Тверская", "д. 7", "офис 12", "г. Москва", "Московская обл." },
            черновик.LegalLines);

        Assert.Equal("190000", черновик.ActualZip);
        Assert.Equal(
            new[] { "ООО «Ромашка»", "ул. Пушкинская", "д. 3", string.Empty, "г. Тула", "Тульская обл." },
            черновик.ActualLines);

        Assert.False(черновик.NeedsVerification);
    }

    [Fact]
    public void ExportData_ЭкспортВTxt_СодержитЗаголовкиИЗначения()
    {
        var контрагент = СоздатьКонтрагента();

        using var поток = new MemoryStream();

        _обмен.ExportData(
            [контрагент],
            ExchangeFormat.Txt,
            поток,
            new AddressExchangeOptions { Delimiter = '\t', IncludeHeader = true });

        var текст = Encoding.UTF8.GetString(поток.ToArray());

        Assert.Contains("Название", текст, StringComparison.Ordinal);
        Assert.Contains("ООО «Ромашка»", текст, StringComparison.Ordinal);
        Assert.Contains("101000", текст, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportData_ЭкспортВHtml_СодержитДокументТаблицуИЗапись()
    {
        var контрагент = СоздатьКонтрагента();

        using var поток = new MemoryStream();

        _обмен.ExportData([контрагент], ExchangeFormat.Html, поток);

        var текст = Encoding.UTF8.GetString(поток.ToArray());

        Assert.StartsWith("<!DOCTYPE html>", текст, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", текст, StringComparison.Ordinal);
        Assert.Contains("<table>", текст, StringComparison.Ordinal);
        Assert.Contains("ООО «Ромашка»", текст, StringComparison.Ordinal);
        Assert.Contains("<td>101000", текст, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportData_ЭкспортВRtf_НачинаетсяСЗаголовкаRtf()
    {
        var контрагент = СоздатьКонтрагента();

        using var поток = new MemoryStream();

        _обмен.ExportData([контрагент], ExchangeFormat.Rtf, поток);

        var текст = Encoding.ASCII.GetString(поток.ToArray());

        Assert.StartsWith("{\\rtf1", текст, StringComparison.Ordinal);
        Assert.Contains("\\row", текст, StringComparison.Ordinal);
        Assert.Contains("\\u", текст, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportData_ЭкспортВXlsx_ОткрываетсяКакZip_ИСодержитОбязательныеЧасти()
    {
        var контрагент = СоздатьКонтрагента();

        using var поток = new MemoryStream();

        _обмен.ExportData([контрагент], ExchangeFormat.Xlsx, поток);

        поток.Position = 0;

        using var архив = new ZipArchive(поток, ZipArchiveMode.Read);

        var части = архив.Entries.Select(запись => запись.FullName).ToArray();

        Assert.Contains("[Content_Types].xml", части);
        Assert.Contains("_rels/.rels", части);
        Assert.Contains("xl/workbook.xml", части);
        Assert.Contains("xl/_rels/workbook.xml.rels", части);
        Assert.Contains("xl/worksheets/sheet1.xml", части);
        Assert.Contains("xl/sharedStrings.xml", части);
        Assert.Contains("xl/styles.xml", части);
    }

    [Fact]
    public void ExportData_ЭкспортВXlsx_ЧисловоеЗначение_ЗаписываетсяЧислом()
    {
        var контрагент = СоздатьКонтрагента();

        using var поток = new MemoryStream();

        _обмен.ExportData([контрагент], ExchangeFormat.Xlsx, поток);

        поток.Position = 0;

        using var архив = new ZipArchive(поток, ZipArchiveMode.Read);

        var лист = ПрочитатьЧасть(архив, "xl/worksheets/sheet1.xml");
        var документ = XDocument.Parse(лист);

        XNamespace пространство = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var ячейка = документ
            .Descendants(пространство + "c")
            .FirstOrDefault(элемент => (string?)элемент.Attribute("r") == "A2");

        Assert.NotNull(ячейка);
        Assert.Null(ячейка.Attribute("t"));
        Assert.Equal("1", ячейка.Element(пространство + "v")?.Value);
    }

    [Fact]
    public void ImportData_СтрокаСНевернымЧисломКолонок_СообщаетНомерСтроки()
    {
        const string содержимое = "Наименование;ИНН\r\nООО «Ромашка»;7701234567\r\nООО «Ромашка»\r\n";

        var профиль = new CsvProfile
        {
            Delimiter = ';',
            HasHeaderRow = true,
            FirstDataRow = 1,
        };

        var ошибка = Assert.Throws<AddressExchangeImportException>(
            () => _обмен.ImportData(содержимое, профиль));

        Assert.Equal(3, ошибка.RowNumber);
        Assert.Contains("3", ошибка.Message, StringComparison.Ordinal);
        Assert.Contains("Строка 3", ошибка.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportData_СопоставлениеПрофиля_ЗаполняетПоляПоИндексамСтолбцов()
    {
        const string содержимое =
            "ООО «Ромашка»;\"101000\nООО «Ромашка»\nул. Мясницкая\nд. 1\n\nг. Москва\nМосковская обл.\"\r\n";

        var профиль = new CsvProfile
        {
            Delimiter = ';',
            HasHeaderRow = false,
            FirstDataRow = 1,
            Bindings =
            [
                new ImportColumnBinding(0, ImportFieldSlot.Name),
                new ImportColumnBinding(1, ImportFieldSlot.PostalAddress),
            ],
        };

        var черновики = _обмен.ImportData(содержимое, профиль);
        var черновик = Assert.Single(черновики);

        Assert.Equal("ООО «Ромашка»", черновик.Name);
        Assert.Equal("101000", черновик.PostalZip);
        Assert.Equal(
            new[] { "ООО «Ромашка»", "ул. Мясницкая", "д. 1", string.Empty, "г. Москва", "Московская обл." },
            черновик.PostalLines);
    }

    /// <summary>
    /// Читает текст части архива OPC.
    /// </summary>
    /// <param name="архив">Архив пакета.</param>
    /// <param name="имя">Имя части.</param>
    /// <returns>Текст части.</returns>
    private static string ПрочитатьЧасть(ZipArchive архив, string имя)
    {
        var запись = архив.GetEntry(имя);

        Assert.NotNull(запись);

        using var поток = запись!.Open();
        using var читатель = new StreamReader(поток, Encoding.UTF8);

        return читатель.ReadToEnd();
    }

    /// <summary>
    /// Создаёт контрагента с тремя срезами адресации.
    /// </summary>
    /// <returns>Контрагент для проверки выгрузки.</returns>
    private static Counterparty СоздатьКонтрагента()
    {
        var контрагент = new Counterparty
        {
            Id = 42,
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Ромашка»",
            ShortName = "Ромашка",
            Inn = "7701234567",
            Kpp = "770101001",
            Ogrn = "1107746123456",
            Phone = "+7 495 000-00-00",
            Email = "info@romashka.ru",
            SpecialNotes = "Отгрузка по вторникам",
            CreatedAt = new DateTime(2024, 5, 17, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 5, 18, 11, 45, 0, DateTimeKind.Utc),
        };

        контрагент.SetAddress(
            AddressMainType.Postal,
            СоздатьАдрес("101000", "ООО «Ромашка»", "ул. Мясницкая", "д. 1", string.Empty, "г. Москва", "Московская обл."));

        контрагент.SetAddress(
            AddressMainType.Legal,
            СоздатьАдрес("770000", "ООО «Ромашка»", "ул. Тверская", "д. 7", "офис 12", "г. Москва", "Московская обл."));

        контрагент.SetAddress(
            AddressMainType.Actual,
            СоздатьАдрес("190000", "ООО «Ромашка»", "ул. Пушкинская", "д. 3", string.Empty, "г. Тула", "Тульская обл."));

        return контрагент;
    }

    /// <summary>
    /// Создаёт адрес по индексу и шести адресным строкам.
    /// </summary>
    /// <param name="индекс">Почтовый индекс.</param>
    /// <param name="строки">Адресные строки.</param>
    /// <returns>Адрес контрагента.</returns>
    private static Address СоздатьАдрес(string индекс, params string[] строки)
    {
        var результат = Address.Create(индекс, строки);

        Assert.True(результат.IsSuccess, результат.Error);

        return результат.Value;
    }
}
