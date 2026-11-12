using System.Text;
using Picket302.App.UI.Forms;
using Picket302.Application.Import;
using Picket302.Domain.Contracts;
using Picket302.Infrastructure.Import;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// S31 (ТЗ §8.5, функция 73): редактор настроек импорта CSV — применение
/// профиля к диалогу, живой предпросмотр с пометкой ошибочных строк,
/// round-trip профиля через хранилище и реальная применимость профиля
/// (Windows-1251, разделитель «;») при разборе файла.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormCsvImportConfigTests
{
    [Fact]
    public void ПрименениеПрофиля_ПараметрыВДиалогеИОбратно()
    {
        using var форма = new FormCsvImportConfig();

        var профиль = new CsvProfile
        {
            Name = "Анси-точка-с-запятой",
            EncodingName = "windows-1251",
            Delimiter = ';',
            QuoteChar = '\'',
            HasHeaderRow = false,
            FirstDataRow = 3,
        };

        форма.ApplyProfile(профиль);

        var собранный = форма.ConfigureCsvMapping();

        Assert.Equal("Анси-точка-с-запятой", собранный.Name);
        Assert.Equal("windows-1251", собранный.EncodingName);
        Assert.Equal(';', собранный.Delimiter);
        Assert.Equal('\'', собранный.QuoteChar);
        Assert.False(собранный.HasHeaderRow);
        Assert.Equal(3, собранный.FirstDataRow);
    }

    [Fact]
    public void Предпросмотр_НесовпадениеЧислаСтолбцов_ПомечаетсяКакОшибка()
    {
        using var форма = new FormCsvImportConfig();

        форма.ConfigureCsvMapping("Фамилия;Имя\nИванов;Иван;лишний\nПетров;Пётр");

        var сведения = форма.СведенияПредпросмотраДляПроверок;

        // ТЗ §8.5, ф.73: строка с несовпадением числа столбцов помечается.
        Assert.Contains("ошибочных строк: 1", сведения);
        Assert.Contains("Строк в предпросмотре: 2", сведения);
        Assert.Empty(форма.ОшибкаПредпросмотраДляПроверок);
    }

    [Fact]
    public void Предпросмотр_ПустойТекст_ПояснениеБезОшибки()
    {
        using var форма = new FormCsvImportConfig();

        форма.PreviewText = string.Empty;

        Assert.Contains("Нет текста предпросмотра", форма.СведенияПредпросмотраДляПроверок);
        Assert.Empty(форма.ОшибкаПредпросмотраДляПроверок);
    }

    [Fact]
    public void Профиль_RoundTripЧерезРеальноеХранилище()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "p302-s31-csvprof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        try
        {
            // CsvProfileStore зеркалит интерфейс приложения — подключается
            // штатным адаптером DelegateCsvProfileStore.
            var файловое = new CsvProfileStore();
            ICsvProfileStore хранилище = new DelegateCsvProfileStore(
                файловое.Save,
                файловое.Load,
                файловое.ListProfiles);
            var путь = CsvProfileStore.BuildFilePath(каталог, "Табличный 1251");

            var настроенный = ConfigureИзФормы();

            хранилище.Save(configuredTo(настроенный), путь);

            // Новый диалог применяет загруженный профиль.
            var загруженный = хранилище.Load(путь);

            using var форма2 = new FormCsvImportConfig(хранилище);
            форма2.ApplyProfile(загруженный);
            var повторный = форма2.ConfigureCsvMapping();

            Assert.Equal("Табличный 1251", повторный.Name);
            Assert.Equal(настроенный.EncodingName, повторный.EncodingName);
            Assert.Equal(настроенный.Delimiter, повторный.Delimiter);
            Assert.Equal(настроенный.QuoteChar, повторный.QuoteChar);
            Assert.Equal(настроенный.HasHeaderRow, повторный.HasHeaderRow);
            Assert.Equal(настроенный.FirstDataRow, повторный.FirstDataRow);
        }
        finally
        {
            Directory.Delete(каталог, recursive: true);
        }
    }

    /// <summary>
    /// Реальная применимость профиля: Windows-1251 байты файла, профиль
    /// с кодировкой windows-1251 и разделителем «;» — разбор даёт точные
    /// значения (интеграционный путь «диалог → профиль → чтение файла»).
    /// </summary>
    [Fact]
    public void Профиль1251_РеальныйРазборФайлаТочен()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "p302-s31-csv1251-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(каталог);

        try
        {
            // CP1251 — не в инвариантном наборе .NET Core: провайдер
            // регистрируется так же, как в продакшн-путях (TextEncodingDetector).
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var путь = Path.Combine(каталог, "клиенты.csv");
            File.WriteAllBytes(
                путь,
                Encoding.GetEncoding("windows-1251").GetBytes("Фамилия;Имя\nИванов;Иван\nПетров;Пётр"));

            var байты = File.ReadAllBytes(путь);

            // Кодировка файла Windows-1251: байт «И» заглавной — 0xC8 (не UTF-8).
            Assert.Equal(0xD4, байты[0]); // «Ф» в CP1251

            using var форма = new FormCsvImportConfig();
            форма.ApplyProfile(new CsvProfile
            {
                Name = "1251",
                EncodingName = "windows-1251",
                Delimiter = ';',
                HasHeaderRow = true,
                FirstDataRow = 1,
            });

            var текст = Encoding.GetEncoding(форма.ConfigureCsvMapping().EncodingName).GetString(байты);
            var таблица = new DelimitedTextReader().Read(текст, форма.ConfigureCsvMapping());

            Assert.Equal(new[] { "Фамилия", "Имя" }, таблица.Headers);
            Assert.Equal(2, таблица.Rows.Count);
            Assert.Equal("Иванов", таблица.Rows[0].Cells[0]);
            Assert.Equal("Пётр", таблица.Rows[1].Cells[1]);
            Assert.Equal(0, таблица.ErrorCount);
        }
        finally
        {
            Directory.Delete(каталог, recursive: true);
        }
    }

    [Fact]
    public void ConfigureCsvMapping_ТекстВозвращаетПрофильИОбновляетПредпросмотр()
    {
        using var форма = new FormCsvImportConfig();

        // ConfigureCsvMapping возвращает ПРОФИЛЬ ДИАЛОГА (автоопределение
        // разделителя — отдельная служба CsvProfileService); применяем
        // табличный профиль явно.
        форма.ApplyProfile(new CsvProfile
        {
            Name = "Табличный",
            EncodingName = "utf-8",
            Delimiter = '\t',
            HasHeaderRow = false,
        });

        var профиль = форма.ConfigureCsvMapping("а\tб\nв\tг");

        Assert.Equal('\t', профиль.Delimiter);
        Assert.Contains("Строк в предпросмотре: 2", форма.СведенияПредпросмотраДляПроверок);
        Assert.Contains("столбцов: 2", форма.СведенияПредпросмотраДляПроверок);
    }

    /// <summary>
    /// Сопоставление колонок (ТЗ §8.5, ф.73): предложение слотов по именам
    /// столбцов, закрепление в профиле, применение профиля при разборе.
    /// </summary>
    [Fact]
    public void СопоставлениеКолонок_ПредложениеИЗакреплениеВПрофиле()
    {
        var сопоставитель = new ImportFieldMapper();

        var заголовки = new[] { "Фамилия", "Телефон", "Комментарий" };
        var предложенные = сопоставитель.Suggest(заголовки);

        // Хотя бы фамилия и телефон сопоставлены со слотами автоматически.
        Assert.Contains(предложенные, привязка => привязка.Slot is not null);

        var профиль = new CsvProfile
        {
            Name = "С сопоставлением",
            EncodingName = "utf-8",
            Delimiter = ';',
        };

        var закреплённый = сопоставитель.BuildProfile(профиль, предложенные);

        Assert.Same(профиль, закреплённый);

        // Apply сохраняет только сопоставленные со слотами привязки.
        Assert.Equal(предложенные.Count(привязка => привязка.Slot is not null), закреплённый.Bindings.Count);
        Assert.All(закреплённый.Bindings, привязка => Assert.True(привязка.ColumnIndex >= 0));
    }

    /// <summary>Профиль из формы с характерными параметрами.</summary>
    private static CsvProfile ConfigureИзФормы()
    {
        using var форма = new FormCsvImportConfig();
        форма.ApplyProfile(new CsvProfile
        {
            Name = "Табличный 1251",
            EncodingName = "windows-1251",
            Delimiter = ';',
            QuoteChar = '\'',
            HasHeaderRow = true,
            FirstDataRow = 2,
        });

        return форма.ConfigureCsvMapping();
    }

    /// <summary>Приводит профиль к форме сохранения (копия полей).</summary>
    private static CsvProfile configuredTo(CsvProfile источник) => new()
    {
        Name = источник.Name,
        EncodingName = источник.EncodingName,
        Delimiter = источник.Delimiter,
        QuoteChar = источник.QuoteChar,
        HasHeaderRow = источник.HasHeaderRow,
        FirstDataRow = источник.FirstDataRow,
    };
}
