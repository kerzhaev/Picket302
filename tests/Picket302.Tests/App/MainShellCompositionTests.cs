using Picket302.App;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Tests.Infrastructure;
using System.Windows.Forms;
using Xunit;
using CounterpartyType = Picket302.Domain.Entities.CounterpartyType;

namespace Picket302.Tests.App;

/// <summary>
/// Регрессия F-E1: состав приложения, создаваемый оболочкой, обязан
/// присваиваться полю и становиться доступным обработчикам. Прежде поле
/// <c>_состав</c> было только для чтения и никогда не получало значение,
/// поэтому после инициализации по умолчанию обработчики молча пропускали
/// работу с базой данных.
/// </summary>
public sealed class MainShellCompositionTests
{
    [Fact]
    public async Task MainShell_ИнициализацияБезСостава_СоздаётСоставИВыполняетКомандуОбновить()
    {
        using var профиль = new ИзолированныйПрофиль();

        using var окно = new MainShell();

        // Путь по умолчанию в проверках недоступен, поэтому используется
        // перегрузка с изолированным профилем путей: ветка создания состава
        // та же, но все файлы создаются во временном каталоге.
        await окно.ИнициализироватьAsync(профиль.Пути);

        Assert.True(окно.Инициализировано);
        Assert.NotNull(окно.ТекущийСостав);
        Assert.True(File.Exists(профиль.Пути.DatabasePath), "База данных должна быть создана составом.");
        Assert.True(окно.ТаблицаПолучателей.Rows.Count > 0, "Демо-данные должны быть загружены.");

        // Реальная операция с базой: запись добавляется через состав,
        // созданный самой оболочкой.
        var состав = окно.ТекущийСостав!;
        var доДобавления = (await состав.Контрагенты.GetPagedAsync(new PageRequest(0, 500))).TotalCount;

        await состав.Контрагенты.AddAsync(new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "Проверка F-E1",
            Inn = "7707083893",
            IsSender = false,
            IsActive = true,
            IsDeleted = false,
            PostalAddress = Address.Create(
                "101000",
                ["Проверка F-E1", "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
        });

        var поДобавлении = (await состав.Контрагенты.GetPagedAsync(new PageRequest(0, 500))).TotalCount;

        Assert.Equal(доДобавления + 1, поДобавлении);

        // Обработчик пункта «Обновить» обязан обратиться к базе через
        // состав и отразить новую запись в таблице и счётчике.
        var пунктОбновить = окно.MainMenuStrip!.Items
            .Cast<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Вид")
            .DropDownItems
            .Cast<ToolStripItem>()
            .Single(пункт => пункт.Text == "Обновить");

        пунктОбновить.PerformClick();

        // Обработчик асинхронный: ожидаем обновления счётчика «Всего».
        var ожидаемоеЗначение = $"Всего: {поДобавлении}";
        var крайнийСрок = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < крайнийСрок
            && окно.СчётчикВсего.Text != ожидаемоеЗначение)
        {
            await Task.Delay(50);
        }

        Assert.Equal(ожидаемоеЗначение, окно.СчётчикВсего.Text);

        var естьСтрока = окно.ТаблицаПолучателей.Rows
            .Cast<DataGridViewRow>()
            .Any(строка => string.Equals(
                строка.Cells[2].Value as string,
                "Проверка F-E1",
                StringComparison.Ordinal));

        Assert.True(естьСтрока, "Новая запись должна отображаться в адресной книге.");
    }
}
