using Picket302.App;
using Picket302.App.Printing;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Сценарий печати выбранных рассылок (ТЗ §8.12, функция 156): сбор писем,
/// пропуск пустых и отсутствующих рассылок, реакции на отмену, ошибку и
/// отсутствие принтера. Запуск печати подменяется делегатом-fake — реальный
/// принтер и диалоги не используются.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class DistributionPrintScenarioTests
{
    /// <summary>
    /// Fake печатной операции: запоминает переданные письма и возвращает
    /// заданный итог.
    /// </summary>
    private sealed class ПечатьFake(ПечатьИтог итог)
    {
        public int Вызовов { get; private set; }

        public List<long> Письма { get; } = [];

        public async Task<ПечатьИтог> Запустить(IReadOnlyList<long> mailIds)
        {
            await Task.Yield();
            Вызовов++;
            Письма.AddRange(mailIds);
            return итог;
        }
    }

    private static Counterparty Контрагент(string имя) => new()
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

    private static async Task<AppServices> СоставСРассылками(
        ИзолированныйПрофиль профиль,
        params string[] именаРассылок)
    {
        var состав = профиль.ВзятьВладение(
            await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false));

        var отправитель = await состав.СоставДанных!.Контрагенты.AddAsync(Контрагент("ООО «Отправитель»"));
        var получатель = await состав.СоставДанных!.Контрагенты.AddAsync(Контрагент("ООО «Получатель»"));

        foreach (var имя in именаРассылок)
        {
            await состав.СоставДанных!.СлужбаРассылок.GenerateDistributionAsync(new()
            {
                Name = имя,
                Kind = DistributionKind.OneToMany,
                SenderId = отправитель,
                CounterpartyIds = [получатель],
            });
        }

        return состав;
    }

    private static async Task<long> ИдентификаторРассылки(AppServices состав, string имя)
    {
        var перечень = await состав.СоставДанных!.СлужбаРассылок.ListDistributionsAsync();

        return перечень.Single(рассылка => рассылка.Name == имя).Id;
    }

    [Fact]
    public async Task НичегоНеВыбрано_ПечатьНеВызывается()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая");

        var fake = new ПечатьFake(ПечатьИтог.Выполнено);

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [], fake.Запустить, () => "Принтер");

        Assert.True(итог.НичегоНеВыбрано);
        Assert.False(итог.ПечатьЗапущена);
        Assert.Equal(0, fake.Вызовов);
    }

    [Fact]
    public async Task ОднаРассылка_ВПечатьПередаютсяТолькоЕёПисьма()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая", "Вторая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var письмаПервой = await состав.СоставДанных!.Письма.GetByDistributionAsync(первая);
        var fake = new ПечатьFake(ПечатьИтог.Выполнено);

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [первая], fake.Запустить, () => "Принтер");

        Assert.True(итог.ЗавершёноУспешно);
        Assert.Equal(письмаПервой.Select(письмо => письмо.Id).ToList(), fake.Письма);
    }

    [Fact]
    public async Task НесколькоРассылок_ПередаютсяПисьмаВсехВПорядкеВыбора()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая", "Вторая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var вторая = await ИдентификаторРассылки(состав, "Вторая");
        var письмаПервой = await состав.СоставДанных!.Письма.GetByDistributionAsync(первая);
        var письмаВторой = await состав.СоставДанных!.Письма.GetByDistributionAsync(вторая);
        var fake = new ПечатьFake(ПечатьИтог.Выполнено);

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [вторая, первая], fake.Запустить, () => "Принтер");

        var ожидаемо = письмаВторой.Select(письмо => письмо.Id)
            .Concat(письмаПервой.Select(письмо => письмо.Id))
            .ToList();

        Assert.Equal(ожидаемо, fake.Письма);
        Assert.True(итог.ЗавершёноУспешно);

        // Письмо принадлежит одной рассылке — дубликаты идентификаторов
        // невозможны по контракту сбора.
        Assert.Equal(fake.Письма.Distinct().Count(), fake.Письма.Count);
    }

    [Fact]
    public async Task ПустаяРассылка_ПропускаетсяСПредупреждениемПечатьПродолжается()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "С письмами", "Пустая");

        var сПисьмами = await ИдентификаторРассылки(состав, "С письмами");
        var пустая = await ИдентификаторРассылки(состав, "Пустая");

        foreach (var письмо in await состав.СоставДанных!.Письма.GetByDistributionAsync(пустая))
        {
            await состав.СоставДанных!.Письма.DeleteAsync(письмо.Id);
        }

        var fake = new ПечатьFake(ПечатьИтог.Выполнено);

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [сПисьмами, пустая], fake.Запустить, () => "Принтер");

        Assert.Contains(итог.ПропущенныеРассылки, имя => имя.Contains("Пустая", StringComparison.Ordinal));
        Assert.True(итог.ЗавершёноУспешно);
        Assert.Equal(1, fake.Вызовов);
    }

    [Fact]
    public async Task ОтсутствующаяРассылка_ПропускаетсяБезПадения()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "С письмами");

        var сПисьмами = await ИдентификаторРассылки(состав, "С письмами");
        var fake = new ПечатьFake(ПечатьИтог.Выполнено);

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [сПисьмами, 404], fake.Запустить, () => "Принтер");

        Assert.Contains(итог.ПропущенныеРассылки, имя => имя.Contains("404", StringComparison.Ordinal));
        Assert.True(итог.ЗавершёноУспешно);
    }

    [Fact]
    public async Task ОшибкаПечати_НеПревращаетсяВУспехИСообщается()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var fake = new ПечатьFake(new ПечатьИтог(ПечатьКод.Ошибка, "Ошибка принтера."));

        var сообщения = new List<string?>();

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [первая], fake.Запустить, () => "Принтер", сообщения.Add);

        Assert.False(итог.ЗавершёноУспешно);
        Assert.Equal("Ошибка принтера.", итог.Ошибка);
        Assert.False(итог.ОшибкаСообщенаОператору);
        Assert.Null(итог.ПредупреждениеБезПринтера);

        // Строка состояния не содержит ложного сообщения о завершении:
        // после ошибки она очищается.
        Assert.Equal(string.Empty, сообщения.Last());
    }

    /// <summary>
    /// Отмена оператора с production-текстом «Печать прервана оператором.»:
    /// итог — отмена без ошибки и без ложного успеха.
    /// </summary>
    [Fact]
    public async Task ОтменаСТекстомПрерванаОператором_ОтменойИОстаетсяБезОшибки()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var fake = new ПечатьFake(ПечатьИтог.Отменено);

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [первая], fake.Запустить, () => "Принтер");

        Assert.True(итог.Отменено);
        Assert.False(итог.ЗавершёноУспешно);
        Assert.Null(итог.Ошибка);
        Assert.False(итог.ОшибкаСообщенаОператору);
    }

    /// <summary>
    /// Ошибка без текста: сценарий подставляет общий стабильный текст и не
    /// превращает отказ в отмену или успех.
    /// </summary>
    [Fact]
    public async Task ОшибкаБезТекста_ПолучаетОбщийСтабильныйТекст()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var fake = new ПечатьFake(new ПечатьИтог(ПечатьКод.Ошибка));

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [первая], fake.Запустить, () => "Принтер");

        Assert.False(итог.ЗавершёноУспешно);
        Assert.False(итог.Отменено);
        Assert.Equal(ПечатьИтог.ОбщийТекстОшибки, итог.Ошибка);
    }

    /// <summary>
    /// Сообщение об ошибке, уже показанное оператору внутри конвейера,
    /// проходит сценарий без потери: оболочка не покажет его повторно.
    /// </summary>
    [Fact]
    public async Task ОшибкаСообщённаяКонвейером_СохраняетТекстИПризнакПоказа()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var fake = new ПечатьFake(
            new ПечатьИтог(ПечатьКод.Ошибка, "Принтер недоступен.", СообщениеПоказано: true));

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [первая], fake.Запустить, () => "Принтер");

        Assert.False(итог.ЗавершёноУспешно);
        Assert.False(итог.Отменено);
        Assert.Equal("Принтер недоступен.", итог.Ошибка);
        Assert.True(итог.ОшибкаСообщенаОператору);
    }

    [Fact]
    public async Task ОтменаПредпросмотра_ПечатьЗапущенаНоНеЗавершенаУспехом()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var рассылкиДо = (await состав.СоставДанных!.СлужбаРассылок.ListDistributionsAsync()).Count;
        var fake = new ПечатьFake(ПечатьИтог.Отменено);

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [первая], fake.Запустить, () => "Принтер");

        Assert.True(итог.Отменено);
        Assert.False(итог.ЗавершёноУспешно);
        Assert.Null(итог.Ошибка);

        // Отмена ничего не меняет: записи не создаются и не удаляются.
        Assert.Equal(рассылкиДо, (await состав.СоставДанных!.СлужбаРассылок.ListDistributionsAsync()).Count);
    }

    [Fact]
    public async Task НетПринтераПоУмолчанию_ПечатьНеВызываетсяВозвращаетсяПредупреждение()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var fake = new ПечатьFake(ПечатьИтог.Выполнено);

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [первая], fake.Запустить, () => null);

        Assert.False(итог.ПечатьЗапущена);
        Assert.NotNull(итог.ПредупреждениеБезПринтера);
        Assert.Equal(0, fake.Вызовов);
    }

    [Fact]
    public async Task УспешнаяОперация_СтатусВыставляетсяИОчищаетсяЗаписиНеСоздаются()
    {
        using var профиль = new ИзолированныйПрофиль();
        await using var состав = await СоставСРассылками(профиль, "Первая");

        var первая = await ИдентификаторРассылки(состав, "Первая");
        var рассылкиДо = (await состав.СоставДанных!.СлужбаРассылок.ListDistributionsAsync()).Count;
        var письмаДо = (await состав.СоставДанных!.Письма.GetByDistributionAsync(первая)).Count;
        var fake = new ПечатьFake(ПечатьИтог.Выполнено);
        var сообщения = new List<string?>();

        var итог = await DistributionPrintScenario.ВыполнитьAsync(
            состав, [первая], fake.Запустить, () => "Принтер", сообщения.Add);

        Assert.True(итог.ЗавершёноУспешно);
        Assert.Equal(1, fake.Вызовов);

        // Статус: сообщение о ходе выставлено, затем строка очищена.
        Assert.Contains(сообщения, текст => текст!.Contains("Печать рассылок", StringComparison.Ordinal));
        Assert.Equal(string.Empty, сообщения.Last());

        // Новые записи рассылок и писем не создаются.
        Assert.Equal(рассылкиДо, (await состав.СоставДанных!.СлужбаРассылок.ListDistributionsAsync()).Count);
        Assert.Equal(письмаДо, (await состав.СоставДанных!.Письма.GetByDistributionAsync(первая)).Count);
    }
}
