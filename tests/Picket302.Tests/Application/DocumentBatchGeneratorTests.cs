using System.Text;
using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка пакетной генерации документов произвольного вида
/// (ТЗ §8.5, функция 65).
/// </summary>
public sealed class DocumentBatchGeneratorTests
{
    [Fact]
    public void GenerateBatch_ТриКонтрагента_ФормируетТриДокумента()
    {
        var каталог = СоздатьКаталог();
        var выходнойКаталог = Path.Combine(каталог, "output");

        try
        {
            var путь = Path.Combine(каталог, "letter.txt");
            File.WriteAllText(путь, "Уважаемый #Counterparty_Name#!", Encoding.UTF8);

            var контрагенты = new[]
            {
                СоздатьКонтрагента(1, "ООО «Ромашка»", "7701234567"),
                СоздатьКонтрагента(2, "АО «Клевер»", "7707654321"),
                СоздатьКонтрагента(3, "ИП Иванов", "123456789012"),
            };

            var генератор = new DocumentBatchGenerator();
            var результат = генератор.GenerateBatch(путь, контрагенты, выходнойКаталог);

            Assert.Equal(3, результат.TotalCount);
            Assert.Equal(3, результат.Generated.Count);
            Assert.Empty(результат.Skipped);

            foreach (var документ in результат.Generated)
            {
                Assert.True(File.Exists(документ.FilePath));

                var текст = File.ReadAllText(документ.FilePath, Encoding.UTF8);

                Assert.Contains(документ.CounterpartyName, текст, StringComparison.Ordinal);
                Assert.DoesNotContain("#Counterparty_Name#", текст, StringComparison.Ordinal);
            }
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void GenerateBatch_ЗаписьСБитымМаркером_Формируется_БитыйМаркерОстаётсяВДокументе()
    {
        var каталог = СоздатьКаталог();
        var выходнойКаталог = Path.Combine(каталог, "output");

        try
        {
            var путь = Path.Combine(каталог, "claim.txt");
            File.WriteAllText(путь, "Кому: #Counterparty_Name#, ИНН: #Counterparty_Inn#.", Encoding.UTF8);

            var контрагенты = new[]
            {
                СоздатьКонтрагента(1, "ООО «Ромашка»", "7701234567"),
                СоздатьКонтрагента(2, "ООО «Без ИНН»", null),
                СоздатьКонтрагента(3, "АО «Клевер»", "7707654321"),
            };

            var генератор = new DocumentBatchGenerator();
            var результат = генератор.GenerateBatch(путь, контрагенты, выходнойКаталог);

            // Битый маркер не отменяет документ: он остаётся в тексте, а
            // проблема попадает в предупреждения сформированного документа.
            Assert.Equal(3, результат.TotalCount);
            Assert.Equal(3, результат.Generated.Count);
            Assert.Empty(результат.Skipped);

            var сБитым = Assert.Single(
                результат.Generated,
                документ => документ.CounterpartyId == 2);

            Assert.Contains(
                сБитым.Warnings,
                предупреждение => предупреждение.Contains("#Counterparty_Inn#", StringComparison.Ordinal));

            var текст = File.ReadAllText(сБитым.FilePath, Encoding.UTF8);

            Assert.Contains("ООО «Без ИНН»", текст, StringComparison.Ordinal);
            Assert.Contains("#Counterparty_Inn#", текст, StringComparison.Ordinal);

            Assert.Equal(3, Directory.GetFiles(выходнойКаталог).Length);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void GenerateBatch_ШаблонОтсутствует_ВыбрасываетFileNotFoundException()
    {
        var каталог = СоздатьКаталог();

        try
        {
            var путь = Path.Combine(каталог, "нет-шаблона.txt");
            var генератор = new DocumentBatchGenerator();

            var исключение = Assert.Throws<FileNotFoundException>(() => генератор.GenerateBatch(
                путь,
                new[] { СоздатьКонтрагента(1, "ООО «Ромашка»", "7701234567") },
                Path.Combine(каталог, "output")));

            Assert.Contains(путь, исключение.Message, StringComparison.Ordinal);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    private static Counterparty СоздатьКонтрагента(long id, string имя, string? инн)
    {
        var контрагент = new Counterparty
        {
            Id = id,
            Name = имя,
            Inn = инн,
        };

        var адрес = Address.Create(
            "101000",
            new[] { имя, string.Empty, string.Empty, string.Empty, "г. Москва", string.Empty });

        if (адрес.IsSuccess)
        {
            контрагент.SetAddress(AddressMainType.Postal, адрес.Value, makeMain: true);
        }

        return контрагент;
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

    /// <summary>
    /// Регрессия F-D5: два контрагента с одинаковым наименованием формируют
    /// два разных файла. Прежде идентичные имена файлов перезаписывали
    /// документы друг друга.
    /// </summary>
    [Fact]
    public void GenerateBatch_ОдноимённыеКонтрагенты_ФайлыНеПерезаписываются()
    {
        var каталог = СоздатьКаталог();
        var выходнойКаталог = Path.Combine(каталог, "output");

        try
        {
            var путь = Path.Combine(каталог, "letter.txt");
            File.WriteAllText(путь, "Уважаемый #Counterparty_Name#!", Encoding.UTF8);

            var контрагенты = new[]
            {
                СоздатьКонтрагента(1, "ООО «Двойник»", "7701234567"),
                СоздатьКонтрагента(2, "ООО «Двойник»", "7707654321"),
            };

            var генератор = new DocumentBatchGenerator();
            var результат = генератор.GenerateBatch(путь, контрагенты, выходнойКаталог);

            Assert.Equal(2, результат.Generated.Count);
            Assert.Equal(2, Directory.GetFiles(выходнойКаталог).Length);

            // Оба документа существуют и содержат свой текст: файлы различны.
            foreach (var документ in результат.Generated)
            {
                Assert.True(File.Exists(документ.FilePath));
                Assert.Contains("ООО «Двойник»", File.ReadAllText(документ.FilePath, Encoding.UTF8), StringComparison.Ordinal);
            }

            Assert.NotEqual(результат.Generated[0].FilePath, результат.Generated[1].FilePath);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }
}
