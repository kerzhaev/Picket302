using System.Text;
using Picket302.Application.Import;
using Picket302.Domain.Contracts;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка примитивов чтения текста с разделителями (ТЗ §8.5, функция 71:
/// «поддерживает разделители запятая/точка с запятой/табуляция, кодировки UTF-8
/// и CP1251»; ТЗ §8.12, функция 73: «Несовпадение числа колонок в строке
/// помечает строку как ошибочную»).
/// </summary>
public sealed class DelimitedTextReaderTests
{
    private readonly DelimitedTextReader _читатель = new();
    private readonly CsvProfileService _сервисПрофилей = new();

    [Fact]
    public void Read_ЗначениеВКавычкахСРазделителемВнутри_ЧитаетсяКакОдноЗначение()
    {
        var профиль = new CsvProfile { Delimiter = ';', HasHeaderRow = false, FirstDataRow = 1 };

        var таблица = _читатель.Read("Наименование;\"ООО; Ромашка\";ИНН", профиль);

        var строка = Assert.Single(таблица.Rows);

        Assert.Equal(3, строка.Cells.Count);
        Assert.Equal("Наименование", строка.Cells[0]);
        Assert.Equal("ООО; Ромашка", строка.Cells[1]);
        Assert.Equal("ИНН", строка.Cells[2]);
    }

    [Fact]
    public void Read_УдвоеннаяКавычкаВЗначении_ЧитаетсяКакОднаКавычка()
    {
        var профиль = new CsvProfile { Delimiter = ';', HasHeaderRow = false, FirstDataRow = 1 };

        var таблица = _читатель.Read("\"a\"\"b\";c", профиль);

        var строка = Assert.Single(таблица.Rows);

        Assert.Equal("a\"b", строка.Cells[0]);
        Assert.Equal("c", строка.Cells[1]);
    }

    [Fact]
    public void Read_ЗначениеВКавычкахСПереводомСтроки_НеРазрываетСтрокуФайла()
    {
        var профиль = new CsvProfile { Delimiter = ';', HasHeaderRow = false, FirstDataRow = 1 };

        var таблица = _читатель.Read("\"первая\nвторая\";второе", профиль);

        var строка = Assert.Single(таблица.Rows);

        Assert.Equal(2, строка.Cells.Count);
        Assert.Equal("первая\nвторая", строка.Cells[0]);
        Assert.Equal(1, строка.RowNumber);
    }

    [Fact]
    public void Read_СтрокаЗаголовков_ОтделяетсяОтДанных()
    {
        var профиль = new CsvProfile { Delimiter = ';', HasHeaderRow = true, FirstDataRow = 1 };

        var таблица = _читатель.Read("Наименование;ИНН\r\nООО «Ромашка»;7701234567\r\n", профиль);

        Assert.Equal(new[] { "Наименование", "ИНН" }, таблица.Headers);
        var строка = Assert.Single(таблица.Rows);
        Assert.Equal(2, строка.RowNumber);
        Assert.False(строка.HasError);
    }

    [Fact]
    public void Read_ЧислоКолонокНеСовпадает_СтрокаПомечаетсяОшибочнойСУказаниемНомера()
    {
        var профиль = new CsvProfile { Delimiter = ';', HasHeaderRow = true, FirstDataRow = 1 };

        var таблица = _читатель.Read(
            "Наименование;ИНН\r\nООО «Ромашка»;7701234567\r\nООО «Ромашка»\r\n",
            профиль);

        Assert.Equal(2, таблица.Rows.Count);
        Assert.Equal(1, таблица.ErrorCount);

        var ошибочная = Assert.Single(таблица.ErrorRows);

        Assert.Equal(3, ошибочная.RowNumber);
        Assert.NotNull(ошибочная.Error);
        Assert.Contains("3", ошибочная.Error!, StringComparison.Ordinal);
        Assert.Contains("Строка 3", ошибочная.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectDelimiter_ФайлСТочкойСЗапятой_ОпределяетсяТочкаСЗапятой()
    {
        var разделитель = _сервисПрофилей.DetectDelimiter("Наименование;ИНН;Адрес\r\nООО «Ромашка»;7701234567;101000\r\n");

        Assert.Equal(';', разделитель);
    }

    [Fact]
    public void DetectDelimiter_ФайлСТабуляцией_ОпределяетсяТабуляция()
    {
        var разделитель = _сервисПрофилей.DetectDelimiter("Наименование\tИНН\tАдрес\nООО «Ромашка»\t7701234567\t101000");

        Assert.Equal('\t', разделитель);
    }

    [Fact]
    public void DetectDelimiter_ФайлСЗапятой_ОпределяетсяЗапятая()
    {
        var разделитель = _сервисПрофилей.DetectDelimiter("Name,Inn,Address\nRomashka,7701234567,101000");

        Assert.Equal(',', разделитель);
    }

    [Fact]
    public void DetectEncoding_Utf8СМаркеромBom_ОпределяетсяКакUtf8()
    {
        var байты = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("Наименование;ИНН"))
            .ToArray();

        var определение = TextEncodingDetector.Detect(байты);

        Assert.True(определение.IsConfident);
        Assert.Equal("utf-8", определение.EncodingName);
        Assert.Equal("Наименование;ИНН", определение.Encoding.GetString(байты, 3, байты.Length - 3));
    }

    [Fact]
    public void DetectEncoding_Utf8БезМаркераСКириллицей_ОпределяетсяКакUtf8()
    {
        var байты = Encoding.UTF8.GetBytes("Наименование;ИНН;Москва");

        var определение = TextEncodingDetector.Detect(байты);

        Assert.True(определение.IsConfident);
        Assert.Equal("utf-8", определение.EncodingName);
        Assert.Equal("Наименование;ИНН;Москва", определение.Encoding.GetString(байты));
    }

    [Fact]
    public void DetectEncoding_БайтыCp1251_ОпределяетсяКакCp1251()
    {
        var байты = TextEncodingDetector.Cp1251.GetBytes("Наименование;ИНН;Москва");

        var определение = TextEncodingDetector.Detect(байты);

        Assert.False(определение.IsConfident);
        Assert.Equal("windows-1251", определение.EncodingName);
        Assert.Equal("Наименование;ИНН;Москва", определение.Encoding.GetString(байты));
    }

    [Fact]
    public void Read_ФайлВCp1251_ЧитаетсяБезПотериКириллицы()
    {
        var байты = TextEncodingDetector.Cp1251.GetBytes("Наименование;ИНН\r\nООО «Ромашка»;7701234567\r\n");

        using var поток = new MemoryStream(байты);

        var профиль = new CsvProfile
        {
            Delimiter = ';',
            EncodingName = string.Empty,
            HasHeaderRow = true,
            FirstDataRow = 1,
        };

        var таблица = _читатель.Read(поток, профиль);

        Assert.Equal(new[] { "Наименование", "ИНН" }, таблица.Headers);

        var строка = Assert.Single(таблица.Rows);

        Assert.Equal("ООО «Ромашка»", строка.Cells[0]);
        Assert.False(строка.HasError);
    }

    [Fact]
    public void Propose_ПрофильПредлагаетРазделительКодировкуИСопоставление()
    {
        var текст = "Наименование;Почтовый адрес\r\nООО «Ромашка»;101000\r\n";

        var профиль = _сервисПрофилей.Propose(текст, "Профиль 1С");

        Assert.Equal("Профиль 1С", профиль.Name);
        Assert.Equal(';', профиль.Delimiter);
        Assert.Equal("utf-8", профиль.EncodingName);
        Assert.True(профиль.HasHeaderRow);
        Assert.Equal(1, профиль.FirstDataRow);
        Assert.Contains(
            профиль.Bindings,
            привязка => привязка.ColumnIndex == 0 && привязка.Slot == ImportFieldSlot.Name);
    }
}
