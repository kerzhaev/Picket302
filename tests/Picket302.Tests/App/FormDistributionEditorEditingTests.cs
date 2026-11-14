using System.Reflection;
using System.Windows.Forms;
using Picket302.App;
using Picket302.App.UI.Forms;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Правка существующей рассылки из редактора (ТЗ §8.12, функция 156):
/// сохранение изменяет ту же запись — письма добавляются и удаляются,
/// дублирующая рассылка не создаётся.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormDistributionEditorEditingTests
{
    private static Counterparty НовыйКонтрагент(string имя, bool отправитель = false) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = "7707083893",
        IsSender = отправитель,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "101000",
            [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
    };

    private static async Task<(Distribution Рассылка, long Первый, long Второй, long Третий)> Подготовить(
        AppServices состав)
    {
        var отправитель = await состав.СоставДанных!.Контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));
        var первый = await состав.СоставДанных!.Контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await состав.СоставДанных!.Контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));
        var третий = await состав.СоставДанных!.Контрагенты.AddAsync(НовыйКонтрагент("ООО «Третий»"));

        состав.ТекущийОтправитель.SetGlobalSender(отправитель);

        var результат = await состав.СоставДанных!.СлужбаРассылок.GenerateDistributionAsync(new()
        {
            Name = "Рассылка до правки",
            Kind = DistributionKind.OneToMany,
            SenderId = отправитель,
            CounterpartyIds = [первый, второй],
        });

        Assert.True(результат.IsSuccess);

        return (результат.Value, первый, второй, третий);
    }

    private static T НайтиКонтрол<T>(Control контейнер)
        where T : Control
        => Обойти(контейнер).OfType<T>().First();

    private static IEnumerable<Control> Обойти(Control контейнер)
    {
        foreach (Control элемент in контейнер.Controls)
        {
            yield return элемент;

            foreach (var вложенный in Обойти(элемент))
            {
                yield return вложенный;
            }
        }
    }

    private static object? Вызвать(FormDistributionEditor форма, string имяМетода)
    {
        var метод = typeof(FormDistributionEditor).GetMethod(
            имяМетода,
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(метод);

        return метод!.Invoke(форма, []);
    }

    private static async Task Дождаться(Func<bool> условие, string пояснение)
    {
        var крайнийСрок = DateTime.UtcNow.AddSeconds(8);

        while (DateTime.UtcNow < крайнийСрок && !условие())
        {
            await Task.Delay(50);
        }

        Assert.True(условие(), пояснение);
    }

    [Fact]
    public async Task СохранениеПравки_ИзменяетТуЖеРассылкуБезДубля()
    {
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);

        var (рассылка, первый, второй, третий) = await Подготовить(состав);

        using var форма = new FormDistributionEditor(состав, рассылка);
        var получатели = НайтиКонтрол<ListView>(форма);

        // Загрузка писем выполняется асинхронно: оба письма рассылки
        // появляются в списке получателей.
        await Дождаться(() => получатели.Items.Count == 2, "Письма рассылки должны загрузиться в список.");

        // Состав изменяется: «Первый» удаляется из списка (итог действия
        // «Удалить выбранные»), «Третий» добавляется выделением слева.
        var удалить = получатели.Items.Cast<ListViewItem>()
            .Single(элемент => (long)элемент.Tag! == первый);

        получатели.Items.Remove(удалить);
        Вызвать(форма, "ОбновитьСчётчик");

        var доступные = НайтиКонтрол<DataGridView>(форма);

        await Дождаться(() => доступные.Rows.Count > 0, "Список доступных контрагентов должен загрузиться.");

        var строкаТретьего = доступные.Rows.Cast<DataGridViewRow>()
            .Single(строка => строка.Tag is long идентификатор && идентификатор == третий);

        строкаТретьего.Selected = true;

        Вызвать(форма, "ДобавитьВыделенные");

        // Наименование меняется через поле редактора.
        var поле = (TextBox)typeof(FormDistributionEditor)
            .GetField("_наименование", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(форма)!;

        поле.Text = "Рассылка после правки";

        Вызвать(форма, "Сохранить");

        await Дождаться(() => форма.Результат is not null, "Сохранение правки должно завершиться.");

        // Дублирующая запись не создаётся: рассылка одна, идентификатор тот же.
        var перечень = await состав.СоставДанных!.СлужбаРассылок.ListDistributionsAsync();
        var единственная = Assert.Single(перечень);

        Assert.Equal(рассылка.Id, единственная.Id);
        Assert.Equal("Рассылка после правки", единственная.Name);

        // Состав писем: «Второй» остался, «Третий» добавлен, «Первый» удалён.
        var письма = await состав.СоставДанных!.Письма.GetByDistributionAsync(рассылка.Id);

        Assert.Equal(2, письма.Count);
        Assert.Contains(письма, письмо => письмо.CounterpartyId == второй);
        Assert.Contains(письма, письмо => письмо.CounterpartyId == третий);
        Assert.DoesNotContain(письма, письмо => письмо.CounterpartyId == первый);
    }

    [Fact]
    public async Task ПовторноеСохранениеПравки_НеСоздаётДубля()
    {
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);

        var (рассылка, _, _, _) = await Подготовить(состав);

        for (var повтор = 0; повтор < 2; повтор++)
        {
            using var форма = new FormDistributionEditor(состав, рассылка);
            var получатели = НайтиКонтрол<ListView>(форма);

            await Дождаться(() => получатели.Items.Count == 2, "Письма рассылки должны загрузиться в список.");

            Вызвать(форма, "Сохранить");

            await Дождаться(() => форма.Результат is not null, "Сохранение правки должно завершиться.");

            рассылка = форма.Результат!;
        }

        var перечень = await состав.СоставДанных!.СлужбаРассылок.ListDistributionsAsync();

        Assert.Single(перечень);

        var письма = await состав.СоставДанных!.Письма.GetByDistributionAsync(рассылка.Id);

        Assert.Equal(2, письма.Count);
    }

    /// <summary>
    /// Правка не требует выбранного глобального отправителя: используется
    /// сохранённый отправитель рассылки, и он не заменяется текущим.
    /// </summary>
    [Fact]
    public async Task ПравкаБезТекущегоОтправителя_ИспользуетСохранённогоИНеСоздаётДубля()
    {
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);

        var (рассылка, первый, второй, _) = await Подготовить(состав);

        // Глобальный отправитель сброшен: правка обязана работать через
        // сохранённого отправителя рассылки.
        состав.ТекущийОтправитель.ClearGlobalSender();
        Assert.Null(состав.ТекущийОтправитель.CurrentSenderId);

        using var форма = new FormDistributionEditor(состав, рассылка);
        var получатели = НайтиКонтрол<ListView>(форма);

        await Дождаться(() => получатели.Items.Count == 2, "Письма рассылки должны загрузиться в список.");

        var поле = (TextBox)typeof(FormDistributionEditor)
            .GetField("_наименование", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(форма)!;

        поле.Text = "Правка без отправителя";

        Вызвать(форма, "Сохранить");

        await Дождаться(() => форма.Результат is not null, "Сохранение правки должно завершиться.");

        // Идентификатор сохранён, имя изменено, отправитель прежний.
        Assert.Equal(рассылка.Id, форма.Результат!.Id);
        Assert.Equal("Правка без отправителя", форма.Результат.Name);
        Assert.Equal(рассылка.SenderId, форма.Результат.SenderId);

        var перечень = await состав.СоставДанных!.СлужбаРассылок.ListDistributionsAsync();
        var единственная = Assert.Single(перечень);

        Assert.Equal(рассылка.Id, единственная.Id);
        Assert.Equal(рассылка.SenderId, единственная.SenderId);

        // Письма не продублировались, состав прежний.
        var письма = await состав.СоставДанных!.Письма.GetByDistributionAsync(рассылка.Id);

        Assert.Equal(2, письма.Count);
        Assert.Contains(письма, письмо => письмо.CounterpartyId == первый);
        Assert.Contains(письма, письмо => письмо.CounterpartyId == второй);
    }

    /// <summary>
    /// Вид существующей рассылки заблокирован: смена вида без пересоздания
    /// состава молча переориентировала бы письма.
    /// </summary>
    [Fact]
    public async Task ПравкаСуществующей_ВидРассылкиЗаблокирован()
    {
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);

        var (рассылка, _, _, _) = await Подготовить(состав);

        using var правка = new FormDistributionEditor(состав, рассылка);
        using var создание = new FormDistributionEditor(состав);

        var видПравки = (ComboBox)typeof(FormDistributionEditor)
            .GetField("_видРассылки", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(правка)!;
        var видСоздания = (ComboBox)typeof(FormDistributionEditor)
            .GetField("_видРассылки", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(создание)!;

        Assert.False(видПравки.Enabled);
        Assert.True(видСоздания.Enabled);
    }
}
