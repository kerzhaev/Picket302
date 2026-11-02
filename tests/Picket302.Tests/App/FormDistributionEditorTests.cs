using Picket302.App;
using Picket302.App.UI.Forms;
using System.Windows.Forms;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Регрессия F-E2: «Добавить выделенные» читает идентификатор контрагента из
/// Tag строки, а не из столбца наименования. Прежде <c>Convert.ToInt64</c>
/// применялся к наименованию («ООО «Ромашка»») и падал исключением
/// <see cref="FormatException"/>.
/// </summary>
public sealed class FormDistributionEditorTests
{
    [Fact]
    public async Task ДобавитьВыделенные_СтрокаСИменемВКавычках_ДобавляетПолучателя()
    {
        var путь = Path.Combine(Path.GetTempPath(), $"picket302_рассылка_{Guid.NewGuid():N}.db");

        try
        {
            await using var состав = await AppServices.СоздатьAsync(путь, создатьДемоБазу: true);

            using var форма = new FormDistributionEditor(состав);

            // Загрузка контрагентов выполняется асинхронно: ожидаем строки.
            var таблица = НайтиКонтрол<DataGridView>(форма)
                ?? throw new InvalidOperationException("Таблица доступных не найдена.");

            var крайнийСрок = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < крайнийСрок && таблица.Rows.Count == 0)
            {
                await Task.Delay(50);
            }

            Assert.True(таблица.Rows.Count > 0, "Доступные контрагенты должны быть загружены.");

            // Наименования демо-базы содержат кавычки « » — прежний разбор
            // первого столбца числом падал на такой строке.
            var строка = таблица.Rows[0];

            Assert.True(строка.Tag is long, "Идентификатор должен храниться в Tag строки.");
            Assert.Contains("«", (string)строка.Cells[0].Value, StringComparison.Ordinal);

            строка.Selected = true;

            var метод = typeof(FormDistributionEditor).GetMethod(
                "ДобавитьВыделенные",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(метод);
            метод!.Invoke(форма, []);

            var получатели = НайтиКонтрол<ListView>(форма)
                ?? throw new InvalidOperationException("Список получателей не найден.");

            Assert.Single(получатели.Items);
            Assert.Equal((long)строка.Tag!, (long)получатели.Items[0].Tag!);
            Assert.Contains("«", получатели.Items[0].Text, StringComparison.Ordinal);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (File.Exists(путь))
            {
                File.Delete(путь);
            }
        }
    }

    /// <summary>
    /// Ищет контрол заданного типа во всём дереве формы.
    /// </summary>
    private static T? НайтиКонтрол<T>(Control корень)
        where T : Control
    {
        if (корень is T совпадение)
        {
            return совпадение;
        }

        foreach (Control потомок in корень.Controls)
        {
            var найденный = НайтиКонтрол<T>(потомок);

            if (найденный is not null)
            {
                return найденный;
            }
        }

        return null;
    }
}
