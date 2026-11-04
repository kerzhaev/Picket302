using Picket302.App;
using Picket302.Tests.Infrastructure;
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
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: true);

        Assert.True(File.Exists(профиль.Пути.DatabasePath));
        Assert.Equal(Path.GetFullPath(профиль.Пути.DatabasePath), состав.ПутьБазы);

        var страница = await состав.Контрагенты
            .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 50));

        Assert.True(страница.TotalCount > 0, "Демо-база должна содержать записи.");
    }

    [Fact]
    public async Task ГлавноеОкно_Инициализация_ЗагружаетДанные()
    {
        using var профиль = new ИзолированныйПрофиль();

        using var окно = await профиль.СоздатьОболочкуAsync();

        await окно.ИнициализироватьAsync();

        Assert.True(окно.Инициализировано);
        Assert.True(окно.ТаблицаПолучателей.Rows.Count > 0, "Адресная книга должна быть заполнена.");
        Assert.True(окно.ТаблицаОтправителей.Rows.Count > 0, "Справочник отправителей должен быть заполнен.");
        Assert.Contains("Всего:", окно.СчётчикВсего.Text, StringComparison.Ordinal);
    }
}
