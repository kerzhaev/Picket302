using System.Text;
using Picket302.Application.Export;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка мастера выгрузки с настройками и пост-действиями
/// (ТЗ §8.12, функция 160: «Экспорт с настройками и пост-действиями»).
/// </summary>
public sealed class ExportWizardTests
{
    [Fact]
    public void Export_ЗаписываетФайл_ВозвращаетПутьЧислоЗаписейИФормат()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "address_book.csv");
            var мастер = new ExportWizard(new UniversalAddressExchange(), new ПодставнойИсполнитель());

            var запрос = СоздатьЗапрос(путь);
            var результат = мастер.Export(запрос);

            Assert.Equal(Path.GetFullPath(путь), результат.FilePath);
            Assert.Equal(2, результат.RecordCount);
            Assert.Equal(ExchangeFormat.Csv, результат.Format);
            Assert.True(File.Exists(путь));

            var текст = File.ReadAllText(путь, Encoding.UTF8);

            Assert.Contains("ООО «Ромашка»", текст, StringComparison.Ordinal);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Export_ИспользуетЗапрошенныйФорматДаты()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "address_book.csv");
            var мастер = new ExportWizard(new UniversalAddressExchange(), new ПодставнойИсполнитель());

            var запрос = СоздатьЗапрос(путь);
            запрос.DateFormat = ExportDateFormat.DotSeparated;

            мастер.Export(запрос);

            var текст = File.ReadAllText(путь, Encoding.UTF8);

            Assert.Contains("17.05.2024", текст, StringComparison.Ordinal);
            Assert.DoesNotContain("2024-05-17", текст, StringComparison.Ordinal);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Export_ПользовательскийФорматДаты_ПрименяетсяКВыгрузке()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "address_book.csv");
            var мастер = new ExportWizard(new UniversalAddressExchange(), new ПодставнойИсполнитель());

            var запрос = СоздатьЗапрос(путь);
            запрос.DateFormat = ExportDateFormat.Custom;
            запрос.CustomDateFormat = "yyyy/MM/dd HH:mm";

            мастер.Export(запрос);

            var текст = File.ReadAllText(путь, Encoding.UTF8);

            Assert.Contains("2024/05/17 10:30", текст, StringComparison.Ordinal);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Export_ФайлЗанят_ПредлагаетАльтернативноеИмя()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "address_book.csv");

            // Файл занят другим приложением: удерживаем открытый дескриптор.
            using var блокировка = new FileStream(путь, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            var мастер = new ExportWizard(new UniversalAddressExchange(), new ПодставнойИсполнитель());

            var результат = мастер.Export(СоздатьЗапрос(путь));

            Assert.NotEqual(Path.GetFullPath(путь), результат.FilePath);
            Assert.Equal("address_book (2).csv", Path.GetFileName(результат.FilePath));
            Assert.True(File.Exists(результат.FilePath));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Export_ПостДействиеОткрытьФайл_ПередаётПутьВыгруженногоФайла()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "address_book.csv");
            var исполнитель = new ПодставнойИсполнитель();
            var мастер = new ExportWizard(new UniversalAddressExchange(), исполнитель);

            var запрос = СоздатьЗапрос(путь);
            запрос.PostAction = ExportPostAction.OpenFile;

            var результат = мастер.Export(запрос);

            var открытый = Assert.Single(исполнитель.ОткрытыеФайлы);

            Assert.Equal(результат.FilePath, открытый);
            Assert.Empty(исполнитель.ОткрытыеПапки);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Export_ПостДействиеОткрытьПапку_ПередаётПутьПапкиФайла()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "address_book.csv");
            var исполнитель = new ПодставнойИсполнитель();
            var мастер = new ExportWizard(new UniversalAddressExchange(), исполнитель);

            var запрос = СоздатьЗапрос(путь);
            запрос.PostAction = ExportPostAction.OpenFolder;

            var результат = мастер.Export(запрос);

            var открытая = Assert.Single(исполнитель.ОткрытыеПапки);

            Assert.Equal(Path.GetDirectoryName(результат.FilePath), открытая);
            Assert.Empty(исполнитель.ОткрытыеФайлы);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Export_ПостДействиеНеЗадано_ИсполнительНеВызывается()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "address_book.csv");
            var исполнитель = new ПодставнойИсполнитель();
            var мастер = new ExportWizard(new UniversalAddressExchange(), исполнитель);

            мастер.Export(СоздатьЗапрос(путь));

            Assert.Empty(исполнитель.ОткрытыеФайлы);
            Assert.Empty(исполнитель.ОткрытыеПапки);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    /// <summary>
    /// Создаёт запрос выгрузки двух контрагентов.
    /// </summary>
    /// <param name="путь">Путь итогового файла.</param>
    /// <returns>Запрос выгрузки.</returns>
    private static ExportRequest СоздатьЗапрос(string путь) => new()
    {
        Format = ExchangeFormat.Csv,
        OutputPath = путь,
        DateFormat = ExportDateFormat.Iso,
        Delimiter = ';',
        IncludeHeader = true,
        Items = [СоздатьКонтрагента("ООО «Ромашка»"), СоздатьКонтрагента("ООО «Гвоздь»")],
    };

    /// <summary>
    /// Создаёт контрагента с датой создания 17.05.2024.
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
    /// Создаёт временный каталог для выгрузки.
    /// </summary>
    /// <returns>Путь к созданному каталогу.</returns>
    private static string СоздатьКаталог()
    {
        var путь = Path.Combine(Path.GetTempPath(), $"picket302_экспорт_{Guid.NewGuid():N}");

        Directory.CreateDirectory(путь);

        return путь;
    }

    /// <summary>
    /// Удаляет временный каталог с выгрузкой.
    /// </summary>
    /// <param name="каталог">Путь к каталогу.</param>
    private static void УдалитьКаталог(string каталог)
    {
        if (Directory.Exists(каталог))
        {
            Directory.Delete(каталог, recursive: true);
        }
    }

    /// <summary>
    /// Подставной исполнитель пост-действия: записывает вызванные пути
    /// вместо запуска Проводника.
    /// </summary>
    private sealed class ПодставнойИсполнитель : IExportPostActionRunner
    {
        /// <summary>Пути файлов, переданные исполнителю.</summary>
        public List<string> ОткрытыеФайлы { get; } = [];

        /// <summary>Пути папок, переданные исполнителю.</summary>
        public List<string> ОткрытыеПапки { get; } = [];

        /// <inheritdoc />
        public void OpenFile(string filePath) => ОткрытыеФайлы.Add(filePath);

        /// <inheritdoc />
        public void OpenFolder(string folderPath) => ОткрытыеПапки.Add(folderPath);
    }
}
