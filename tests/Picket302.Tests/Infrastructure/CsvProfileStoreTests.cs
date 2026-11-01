using System.Text;
using Picket302.Application.Import;
using Picket302.Domain.Contracts;
using Picket302.Infrastructure.Import;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка хранения профилей сопоставления CSV в файлах *.csvprof
/// (ТЗ §8.12, функция 73: «Данные: Файлы конфигурации *.csvprof»).
/// </summary>
public sealed class CsvProfileStoreTests
{
    [Fact]
    public void SaveИLoad_ПрофильСохраняетсяИЧитаетсяБезПотерь()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "выгрузка_1с.csvprof");

            var профиль = new CsvProfile
            {
                Name = "Выгрузка 1С",
                EncodingName = "windows-1251",
                Delimiter = ';',
                QuoteChar = '\'',
                HasHeaderRow = true,
                FirstDataRow = 2,
                Bindings =
                [
                    new ImportColumnBinding(0, ImportFieldSlot.Name, "Название"),
                    new ImportColumnBinding(1, ImportFieldSlot.PostalAddress, "Почтовый адрес"),
                    new ImportColumnBinding(2, null, "ИНН"),
                ],
            };

            var хранилище = new CsvProfileStore();
            хранилище.Save(профиль, путь);

            Assert.True(File.Exists(путь));

            var загруженный = хранилище.Load(путь);

            Assert.Equal("Выгрузка 1С", загруженный.Name);
            Assert.Equal("windows-1251", загруженный.EncodingName);
            Assert.Equal(';', загруженный.Delimiter);
            Assert.Equal('\'', загруженный.QuoteChar);
            Assert.True(загруженный.HasHeaderRow);
            Assert.Equal(2, загруженный.FirstDataRow);
            Assert.Equal(Path.GetFullPath(путь), загруженный.FilePath);

            Assert.Equal(3, загруженный.Bindings.Count);
            Assert.Equal(0, загруженный.Bindings[0].ColumnIndex);
            Assert.Equal(ImportFieldSlot.Name, загруженный.Bindings[0].Slot);
            Assert.Equal("Название", загруженный.Bindings[0].ColumnName);
            Assert.Equal(ImportFieldSlot.PostalAddress, загруженный.Bindings[1].Slot);
            Assert.Null(загруженный.Bindings[2].Slot);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Load_ПовреждённыйФайлПрофиля_СообщаетОшибкуНаРусскомЯзыке()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "повреждённый.csvprof");

            File.WriteAllText(путь, "это не содержимое профиля {", Encoding.UTF8);

            var хранилище = new CsvProfileStore();

            var ошибка = Assert.Throws<CsvProfileFormatException>(() => хранилище.Load(путь));

            Assert.Contains("повреждён", ошибка.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("повреждённый.csvprof", ошибка.Message, StringComparison.Ordinal);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Load_ЧужойJson_СообщаетОшибкуОНеверномФормате()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "чужой.csvprof");

            File.WriteAllText(путь, "{\"format\":\"Что-то другое\",\"version\":1,\"name\":\"Профиль\"}", Encoding.UTF8);

            var хранилище = new CsvProfileStore();

            var ошибка = Assert.Throws<CsvProfileFormatException>(() => хранилище.Load(путь));

            Assert.Contains("повреждён", ошибка.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Load_ОтсутствующийФайл_СообщаетОбОтсутствииФайла()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "нет_такого.csvprof");

            var хранилище = new CsvProfileStore();

            var ошибка = Assert.Throws<FileNotFoundException>(() => хранилище.Load(путь));

            Assert.Contains("не найден", ошибка.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void ListProfiles_ПеречисляетТолькоФайлыПрофилей()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var хранилище = new CsvProfileStore();

            хранилище.Save(new CsvProfile { Name = "Первый" }, Path.Combine(каталог, "первый.csvprof"));
            хранилище.Save(new CsvProfile { Name = "Второй" }, Path.Combine(каталог, "второй.csvprof"));
            File.WriteAllText(Path.Combine(каталог, "посторонний.csv"), "данные", Encoding.UTF8);

            var файлы = хранилище.ListProfiles(каталог);

            Assert.Equal(2, файлы.Count);
            Assert.All(файлы, путь => Assert.EndsWith(".csvprof", путь, StringComparison.Ordinal));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Хранилище_ПодключаетсяКПриложениюЧерезИнтерфейс()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var реализация = new CsvProfileStore();

            ICsvProfileStore хранилище = new DelegateCsvProfileStore(
                реализация.Save,
                реализация.Load,
                реализация.ListProfiles);

            var путь = Path.Combine(каталог, "профиль.csvprof");

            хранилище.Save(new CsvProfile { Name = "Профиль", Delimiter = '\t' }, путь);

            var загруженный = хранилище.Load(путь);

            Assert.Equal("Профиль", загруженный.Name);
            Assert.Equal('\t', загруженный.Delimiter);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    /// <summary>
    /// Создаёт временный каталог для файлов профилей.
    /// </summary>
    /// <returns>Путь к созданному каталогу.</returns>
    private static string СоздатьКаталог()
    {
        var путь = Path.Combine(Path.GetTempPath(), $"picket302_профили_{Guid.NewGuid():N}");

        Directory.CreateDirectory(путь);

        return путь;
    }

    /// <summary>
    /// Удаляет временный каталог с профилями.
    /// </summary>
    /// <param name="каталог">Путь к каталогу.</param>
    private static void УдалитьКаталог(string каталог)
    {
        if (Directory.Exists(каталог))
        {
            Directory.Delete(каталог, recursive: true);
        }
    }
}
