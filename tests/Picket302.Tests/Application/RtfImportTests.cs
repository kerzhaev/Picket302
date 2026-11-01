using System.Globalization;
using System.Text;
using Picket302.Application.Import;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверки импорта адресного списка из документа RTF (ТЗ §8.5, функция 71:
/// «Импорт и экспорт адресных списков в форматах TXT/CSV/HTML/RTF/XLS/XLSX»).
/// </summary>
/// <remarks>
/// Проверяется обязательный сценарий round-trip: выгрузка адресного списка в
/// RTF и импорт того же файла обратно с восстановлением наименования, ИНН,
/// почтового индекса и строк адреса.
/// </remarks>
public sealed class RtfImportTests
{
    private readonly UniversalAddressExchange _обмен = new();

    [Fact]
    public void ЭкспортВRtf_ИмпортОбратно_ВосстанавливаетНазваниеИННИндексИСтрокиАдреса()
    {
        var контрагенты = new[] { СоздатьКонтрагента(), СоздатьКонтрагента("ООО «Ромашка-2»", "7701234568") };

        using var поток = new MemoryStream();

        _обмен.ExportData(контрагенты, ExchangeFormat.Rtf, поток);

        поток.Position = 0;

        var черновики = _обмен.ImportRtf(поток, Профиль());

        Assert.Equal(2, черновики.Count);

        var черновик = черновики[0];

        Assert.Equal("ООО «Ромашка»", черновик.Name);
        Assert.Equal("Ромашка", черновик.ShortName);
        Assert.Equal(CounterpartyType.LegalEntity, черновик.Type);
        Assert.Equal("7701234567", черновик.Inn);
        Assert.Equal("770101001", черновик.Kpp);
        Assert.Equal("1107746123456", черновик.Ogrn);
        Assert.Equal("+7 495 000-00-00", черновик.Phone);
        Assert.Equal("info@romashka.ru", черновик.Email);

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

        Assert.Equal("ООО «Ромашка-2»", черновики[1].Name);
        Assert.Equal("7701234568", черновики[1].Inn);
    }

    [Fact]
    public void ФайлRtf_ИмпортЧерезМастер_ВосстанавливаетЗаписиАдреснойКниги()
    {
        var контрагенты = new[] { СоздатьКонтрагента(), СоздатьКонтрагента("ООО «Ромашка-2»", "7701234568") };

        using var поток = new MemoryStream();

        _обмен.ExportData(контрагенты, ExchangeFormat.Rtf, поток);

        var путь = ЗаписатьФайл(поток.ToArray(), ".rtf");

        var результат = ImportSession.Start(путь, new ImportStartOptions { FilePath = путь });

        Assert.True(результат.IsSuccess, результат.Error);

        var сессия = результат.Value!;

        Assert.Equal(2, сессия.Records.Count);

        var запись = сессия.Records[0];

        Assert.Equal("ООО «Ромашка»", запись.Draft.Name);
        Assert.Equal("7701234567", запись.Draft.Inn);
        Assert.Equal("101000", запись.Draft.PostalZip);
        Assert.Equal(
            new[] { "ООО «Ромашка»", "ул. Мясницкая", "д. 1", string.Empty, "г. Москва", "Московская обл." },
            запись.Draft.PostalLines);

        Assert.Equal("ООО «Ромашка-2»", сессия.Records[1].Draft.Name);
    }

    [Fact]
    public void ФайлRtf_ОпределяетсяКакПоддерживаемыйФормат()
    {
        var путь = ЗаписатьФайл(
            Encoding.ASCII.GetBytes(ТаблицаRtf(["Название"], ["ООО «Первая»"])),
            ".rtf");

        var результат = ImportSession.Start(путь, new ImportStartOptions { FilePath = путь });

        Assert.True(результат.IsSuccess, результат.Error);
    }

    [Fact]
    public void ФайлБезРасширенияRtf_ОпределяетсяПоСигнатуреДокумента()
    {
        var путь = ЗаписатьФайл(
            Encoding.ASCII.GetBytes(ТаблицаRtf(["Название"], ["ООО «Первая»"])),
            ".dat");

        var результат = ImportSession.Start(путь, new ImportStartOptions { FilePath = путь });

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.Single(результат.Value!.Records);
    }

    [Fact]
    public void ДокументRtfСТаблицей_ЧитаетСтрокиИСтолбцы()
    {
        const string документ =
            "{\\rtf1\\ansi\\ansicpg1251\\deff0\n"
            + "{\\fonttbl{\\f0\\fnil\\fcharset204 Calibri;}}\n"
            + "\\trowd\\trgaph80\\trleft0\\cellx2400\\cellx4800\n"
            + "{\\intbl Name\\cell Value}\n"
            + "\\row\n"
            + "\\trowd\\trgaph80\\trleft0\\cellx2400\\cellx4800\n"
            + "{\\intbl First\\cell Second}\n"
            + "\\row\n"
            + "\\trowd\\trgaph80\\trleft0\\cellx2400\\cellx4800\n"
            + "{\\intbl Third\\cell Fourth}\n"
            + "\\row\n"
            + "}";

        var таблица = new RtfTableReader().Read(документ);

        Assert.Equal(new[] { "Name", "Value" }, таблица.Headers);
        Assert.Equal(2, таблица.Rows.Count);

        Assert.Equal(new[] { "First", "Second" }, таблица.Rows[0].Cells);
        Assert.Equal(new[] { "Third", "Fourth" }, таблица.Rows[1].Cells);
    }

    [Fact]
    public void ДокументRtf_ЧитаетСимволыUnicodeИКодовойСтраницы()
    {
        const string документ =
            "{\\rtf1\\ansi\\ansicpg1251\\deff0\n"
            + "\\trowd\\trgaph80\\trleft0\\cellx2400\\cellx4800\n"
            + "{\\intbl Code\\cell Symbol}\n"
            + "\\row\n"
            + "\\trowd\\trgaph80\\trleft0\\cellx2400\\cellx4800\n"
            + "{\\intbl \\'c0\\'c1\\'c2\\cell \\u1058?\\u1077?\\u1089?\\u1090?}\n"
            + "\\row\n"
            + "}";

        var таблица = new RtfTableReader().Read(документ);

        Assert.Equal(new[] { "Code", "Symbol" }, таблица.Headers);

        var строка = Assert.Single(таблица.Rows);

        Assert.Equal("АБВ", строка.Cells[0]);
        Assert.Equal("Тест", строка.Cells[1]);
    }

    [Fact]
    public void ДокументRtfБезТаблицы_ЧитаетАбзацыКакСтроки()
    {
        const string документ =
            "{\\rtf1\\ansi\\deff0\n"
            + "{\\fonttbl{\\f0\\fnil Calibri;}}\n"
            + "First\\par\n"
            + "Second\\par\n"
            + "Third\\par\n"
            + "}";

        var таблица = new RtfTableReader().Read(документ);

        Assert.Equal(new[] { "First" }, таблица.Headers);
        Assert.Equal(2, таблица.Rows.Count);
        Assert.Equal(new[] { "Second" }, таблица.Rows[0].Cells);
        Assert.Equal(new[] { "Third" }, таблица.Rows[1].Cells);
    }

    [Fact]
    public void ДокументRtfБезТаблицы_БезЗаголовков_ЧитаетВсеАбзацыКакДанные()
    {
        const string документ =
            "{\\rtf1\\ansi\\deff0\n"
            + "First\\par\n"
            + "Second\\par\n"
            + "}";

        var таблица = new RtfTableReader().Read(документ, естьЗаголовки: false);

        Assert.Empty(таблица.Headers);
        Assert.Equal(2, таблица.Rows.Count);
        Assert.Equal(new[] { "First" }, таблица.Rows[0].Cells);
        Assert.Equal(new[] { "Second" }, таблица.Rows[1].Cells);
    }

    [Fact]
    public void ДокументRtf_РазбираетРеквизитыПризнакИзбранногоИГруппу()
    {
        var путь = ЗаписатьФайл(
            Encoding.ASCII.GetBytes(ТаблицаRtf(
                ["Название", "ИНН", "КПП", "ОГРН", "Избранное", "Группа"],
                ["ООО «Первая»", "7707083893", "770101001", "1107746123456", "да", "42"])),
            ".rtf");

        var результат = ImportSession.Start(путь, new ImportStartOptions { FilePath = путь });

        Assert.True(результат.IsSuccess, результат.Error);

        var запись = Assert.Single(результат.Value!.Records);

        Assert.Equal("ООО «Первая»", запись.Draft.Name);
        Assert.Equal("7707083893", запись.Draft.Inn);
        Assert.Equal("770101001", запись.Draft.Kpp);
        Assert.Equal("1107746123456", запись.Draft.Ogrn);
        Assert.True(запись.Draft.IsVip);
        Assert.Equal(42, запись.Draft.GroupId);
    }

    [Fact]
    public void ЗначениеДлиннее255Символов_ЗаписьПомечаетсяОшибкой()
    {
        var длинное = new string('A', 256);

        var путь = ЗаписатьФайл(
            Encoding.ASCII.GetBytes(ТаблицаRtf(
                ["Название", "ИНН"],
                [длинное, "7707083893"])),
            ".rtf");

        var результат = ImportSession.Start(путь, new ImportStartOptions { FilePath = путь });

        Assert.True(результат.IsSuccess, результат.Error);

        var запись = Assert.Single(результат.Value!.Records);

        Assert.True(запись.HasErrors);
        Assert.Equal(ImportRecordStatus.HasErrors, запись.Status);
        Assert.Contains(запись.Errors, сообщение => сообщение.Contains("255", StringComparison.Ordinal));

        // Запись с ошибкой не отбрасывается: она остаётся в предпросмотре.
        Assert.Single(результат.Value!.Records);
    }

    [Fact]
    public void НекорректныйИНН_ЗаписьПомечаетсяОшибкой()
    {
        var путь = ЗаписатьФайл(
            Encoding.ASCII.GetBytes(ТаблицаRtf(
                ["Название", "ИНН"],
                ["ООО «Первая»", "123"])),
            ".rtf");

        var результат = ImportSession.Start(путь, new ImportStartOptions { FilePath = путь });

        Assert.True(результат.IsSuccess, результат.Error);

        var сессия = результат.Value!;

        сессия.ValidateAll();

        var запись = Assert.Single(сессия.Records);

        Assert.True(запись.HasErrors);
        Assert.Equal(ImportRecordStatus.HasErrors, запись.Status);
        Assert.Contains(запись.Errors, сообщение => сообщение.Contains("ИНН", StringComparison.Ordinal));
    }

    /// <summary>
    /// Профиль сопоставления столбцов для импорта RTF.
    /// </summary>
    /// <returns>Профиль сопоставления.</returns>
    private static CsvProfile Профиль() => new()
    {
        Name = "Проверка round-trip RTF",
        EncodingName = "utf-8",
        Delimiter = ';',
        QuoteChar = '"',
        HasHeaderRow = true,
        FirstDataRow = 1,
    };

    /// <summary>
    /// Записывает файл импорта во временный каталог.
    /// </summary>
    /// <param name="содержимое">Содержимое файла.</param>
    /// <param name="расширение">Расширение файла.</param>
    /// <returns>Путь к записанному файлу.</returns>
    private static string ЗаписатьФайл(byte[] содержимое, string расширение)
    {
        var путь = Path.Combine(
            Path.GetTempPath(),
            "picket302_rtf_" + Guid.NewGuid().ToString("N") + расширение);

        File.WriteAllBytes(путь, содержимое);

        return путь;
    }

    /// <summary>
    /// Собирает документ RTF с таблицей.
    /// </summary>
    /// <param name="заголовки">Заголовки столбцов.</param>
    /// <param name="строки">Строки таблицы.</param>
    /// <returns>Текст документа RTF.</returns>
    private static string ТаблицаRtf(string[] заголовки, params string[][] строки)
    {
        var документ = new StringBuilder();

        документ.Append("{\\rtf1\\ansi\\ansicpg1251\\deff0\n");
        документ.Append("{\\fonttbl{\\f0\\fnil\\fcharset204 Calibri;}}\n");

        if (заголовки.Length > 0)
        {
            документ.Append(СтрокаRtf(заголовки, жирный: true));
        }

        foreach (var строка in строки)
        {
            документ.Append(СтрокаRtf(строка, жирный: false));
        }

        документ.Append("}\n");

        return документ.ToString();
    }

    /// <summary>
    /// Собирает строку таблицы документа RTF.
    /// </summary>
    /// <param name="значения">Значения ячеек строки.</param>
    /// <param name="жирный">Признак строки заголовков.</param>
    /// <returns>Текст строки RTF.</returns>
    private static string СтрокаRtf(IReadOnlyList<string> значения, bool жирный)
    {
        var документ = new StringBuilder();

        документ.Append("\\trowd\\trgaph80\\trleft0");

        for (var индекс = 0; индекс < значения.Count; индекс++)
        {
            документ.Append(CultureInfo.InvariantCulture, $@"\cellx{(индекс + 1) * 2400}");
        }

        документ.Append('\n');
        документ.Append(жирный ? "{\\intbl\\b " : "{\\intbl ");

        for (var индекс = 0; индекс < значения.Count; индекс++)
        {
            if (индекс > 0)
            {
                документ.Append("\\cell ");
            }

            документ.Append(ЭкранироватьRtf(значения[индекс]));
        }

        документ.Append(жирный ? "\\b0}" : "}");
        документ.Append("\\row\n");

        return документ.ToString();
    }

    /// <summary>
    /// Экранирует текст для включения в документ RTF.
    /// </summary>
    /// <param name="значение">Исходный текст.</param>
    /// <returns>Экранированный текст.</returns>
    private static string ЭкранироватьRtf(string значение)
    {
        var результат = new StringBuilder(значение.Length);

        foreach (var символ in значение)
        {
            switch (символ)
            {
                case '\\':
                    результат.Append("\\\\");
                    break;

                case '{':
                    результат.Append("\\{");
                    break;

                case '}':
                    результат.Append("\\}");
                    break;

                case '\n':
                    результат.Append("\\line ");
                    break;

                default:
                    if (символ < 128)
                    {
                        результат.Append(символ);
                        break;
                    }

                    var код = символ < 32768 ? символ : символ - 65536;

                    результат.Append(CultureInfo.InvariantCulture, $"\\u{код.ToString(CultureInfo.InvariantCulture)}?");
                    break;
            }
        }

        return результат.ToString();
    }

    /// <summary>
    /// Создаёт контрагента с тремя срезами адресации.
    /// </summary>
    /// <param name="название">Наименование контрагента.</param>
    /// <param name="инн">ИНН контрагента.</param>
    /// <returns>Контрагент для проверки выгрузки.</returns>
    private static Counterparty СоздатьКонтрагента(
        string название = "ООО «Ромашка»",
        string инн = "7701234567")
    {
        var контрагент = new Counterparty
        {
            Id = 42,
            Type = CounterpartyType.LegalEntity,
            Name = название,
            ShortName = "Ромашка",
            Inn = инн,
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
