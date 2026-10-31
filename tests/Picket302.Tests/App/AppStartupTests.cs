using Picket302.App;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверка создания состава приложения и запуска главного окна
/// (ТЗ §8.11, функция 137: «Демо-база при первом запуске»).
/// </summary>
public sealed class AppStartupTests
{
    [Fact]
    public async Task СоставПриложения_СоздаётБазуИНаполняетДемоДанными()
    {
        var путь = Path.Combine(Path.GetTempPath(), $"picket302_запуск_{Guid.NewGuid():N}.db");

        try
        {
            await using var состав = await AppServices.СоздатьAsync(путь, создатьДемоБазу: true);

            Assert.True(File.Exists(путь));
            Assert.Equal(Path.GetFullPath(путь), состав.ПутьБазы);

            var страница = await состав.Контрагенты
                .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 50));

            Assert.True(страница.TotalCount > 0, "Демо-база должна содержать записи.");
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

    [Fact]
    public async Task ГлавноеОкно_Инициализация_ЗагружаетДанные()
    {
        var путь = Path.Combine(Path.GetTempPath(), $"picket302_окно_{Guid.NewGuid():N}.db");

        try
        {
            await using var состав = await AppServices.СоздатьAsync(путь, создатьДемоБазу: true);

            using var окно = new MainShell(состав);

            await окно.ИнициализироватьAsync(состав);

            Assert.True(окно.Инициализировано);
            Assert.True(окно.ТаблицаПолучателей.Rows.Count > 0, "Адресная книга должна быть заполнена.");
            Assert.True(окно.ТаблицаОтправителей.Rows.Count > 0, "Справочник отправителей должен быть заполнен.");
            Assert.Contains("Всего:", окно.СчётчикВсего.Text, StringComparison.Ordinal);
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
}
