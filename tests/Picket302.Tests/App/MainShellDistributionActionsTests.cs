using System.Reflection;
using System.Windows.Forms;
using Picket302.App;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Действия вкладок «Рассылки» и «Отправленные»: правка рассылки и печать
/// выбранных рассылок (ТЗ §8.12, функция 156), а также сбор идентификаторов
/// выбранных строк без выбора и с множественным выбором.
/// </summary>
public sealed class MainShellDistributionActionsTests
{
    [Fact]
    public void ПолосаДействийРассылок_СодержитПравкуИПечать()
    {
        using var окно = new MainShell();

        var пункты = ПунктыПолосыДействий(окно, "Рассылки");

        Assert.Contains("Новая рассылка…", пункты);
        Assert.Contains("Изменить…", пункты);
        Assert.Contains("Печать…", пункты);
    }

    [Fact]
    public void ПолосаДействийОтправленных_СодержитПечатьВыделенных()
    {
        using var окно = new MainShell();

        var пункты = ПунктыПолосыДействий(окно, "Отправленные");

        Assert.Contains("Печать выделенных…", пункты);
        Assert.Contains("Обновить", пункты);
    }

    /// <summary>
    /// U2-стиль предупреждения о пустом выборе: сбор идентификаторов без
    /// выделенных строк возвращает пустой список, множественный выбор —
    /// уникальные идентификаторы.
    /// </summary>
    [Fact]
    public void ИдентификаторыВыбранныхСтрок_БезВыбораИСМножественнымВыбором()
    {
        using var таблица = new DataGridView();
        таблица.Columns.Add("Имя", "Имя");
        таблица.Rows.Add("Первая");
        таблица.Rows.Add("Вторая");
        таблица.Rows.Add("Третья");

        var метод = typeof(MainShell).GetMethod(
            "ИдентификаторыВыбранныхСтрок",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(DataGridView)]);

        Assert.NotNull(метод);

        var безВыбора = метод!.Invoke(null, [таблица]);

        Assert.Empty((System.Collections.IEnumerable)безВыбора!);

        таблица.Rows[0].Tag = 11L;
        таблица.Rows[1].Tag = 22L;
        таблица.Rows[2].Tag = 11L;

        таблица.Rows[0].Selected = true;
        таблица.Rows[1].Selected = true;
        таблица.Rows[2].Selected = true;

        var сВыбором = Assert.IsAssignableFrom<IReadOnlyList<long>>(метод.Invoke(null, [таблица]));

        Assert.Equal([11L, 22L], сВыбором.OrderBy(значение => значение).ToArray());
    }

    [Fact]
    public async Task СобратьПисьмаРассылок_ПропускаетПустыеИОтсутствующие()
    {
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);

        var отправитель = await состав.Контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»"));
        var получатель = await состав.Контрагенты.AddAsync(НовыйКонтрагент("ООО «Получатель»"));

        var сПисьмами = await состав.СлужбаРассылок.GenerateDistributionAsync(new()
        {
            Name = "С письмами",
            Kind = DistributionKind.OneToMany,
            SenderId = отправитель,
            CounterpartyIds = [получатель],
        });

        var пустая = await состав.СлужбаРассылок.GenerateDistributionAsync(new()
        {
            Name = "Без писем",
            Kind = DistributionKind.OneToMany,
            SenderId = отправитель,
            CounterpartyIds = [получатель],
        });

        // Письма второй рассылки удаляются — рассылка остаётся «пустой»:
        // печать обязана пропустить её и сообщить об этом.
        foreach (var письмо in await состав.Письма.GetByDistributionAsync(пустая.Value.Id))
        {
            await состав.Письма.DeleteAsync(письмо.Id);
        }

        var сборка = await Picket302.App.Printing.DistributionPrintScenario
            .СобратьПисьмаРассылокAsync(состав, [сПисьмами.Value.Id, пустая.Value.Id, 404L]);

        var письмаРассылки = await состав.Письма.GetByDistributionAsync(сПисьмами.Value.Id);

        Assert.Equal(письмаРассылки.Select(письмо => письмо.Id).ToList(), сборка.MailIds.ToList());
        Assert.Equal(2, сборка.ПропущенныеРассылки.Count);
        Assert.Contains(сборка.ПропущенныеРассылки, имя => имя.Contains("Без писем", StringComparison.Ordinal));
        Assert.Contains(сборка.ПропущенныеРассылки, имя => имя.Contains("404", StringComparison.Ordinal));
    }

    private static Counterparty НовыйКонтрагент(string имя) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = "7707083893",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "101000",
            [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
    };

    private static string[] ПунктыПолосыДействий(MainShell окно, string вкладка) =>
        Обойти(Вкладка(окно, вкладка))
            .OfType<ToolStrip>()
            .SelectMany(полоса => полоса.Items.Cast<ToolStripItem>())
            .Select(пункт => пункт.Text)
            .Where(текст => !string.IsNullOrEmpty(текст))
            .Cast<string>()
            .ToArray();

    private static TabPage Вкладка(MainShell окно, string название)
    {
        var вкладки = окно.Controls.OfType<TabControl>().Single();

        return вкладки.TabPages.Cast<TabPage>().Single(страница =>
            string.Equals(страница.Text, название, StringComparison.Ordinal));
    }

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
}
