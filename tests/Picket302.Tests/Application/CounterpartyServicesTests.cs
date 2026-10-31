using Dapper;
using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверка сервисов адресной книжки на встроенной SQLite в памяти
/// (ТЗ §8.1, функции 7, 15, 18, 19, 20).
/// </summary>
public sealed class CounterpartyServicesTests
{
    private static Counterparty НовыйКонтрагент(
        string имя,
        bool отправитель = false,
        bool активен = true,
        bool удалён = false)
        => new()
        {
            Type = CounterpartyType.LegalEntity,
            Name = имя,
            Inn = "7707083893",
            IsSender = отправитель,
            IsActive = активен,
            IsDeleted = удалён,
            CreatedAt = DateTime.UtcNow,
            PostalAddress = Address.Create(
                "101000",
                [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
        };

    [Fact]
    public async Task CustomFieldManager_ПовторноеИмяПоля_ОбновляетСуществующее()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var поля = new CounterpartyFieldRepository(база.Session);
        var менеджер = new CustomFieldManager(поля, база.UnitOfWork);

        var идентификатор = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Техно»"));

        var первое = await менеджер.SetCustomValueAsync(идентификатор, "КодКлиента", "К-104");
        var второе = await менеджер.SetCustomValueAsync(идентификатор, "КодКлиента", "К-105");

        Assert.True(первое.IsSuccess);
        Assert.True(второе.IsSuccess);

        var все = await менеджер.GetCustomFieldsAsync(идентификатор);

        Assert.Single(все);
        Assert.Equal("К-105", все[0].FieldValue);
    }

    [Fact]
    public async Task CustomFieldManager_ПустоеИмяПоля_Отклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var поля = new CounterpartyFieldRepository(база.Session);
        var менеджер = new CustomFieldManager(поля, база.UnitOfWork);

        var результат = await менеджер.SetCustomValueAsync(1, "  ", "значение");

        Assert.True(результат.IsFailure);
    }

    [Fact]
    public async Task CascadeAddressUpdater_НапечатанноеПисьмо_НеИзменяется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        var идентификатор = await контрагенты.AddAsync(контрагент);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));

        var черновик = MailFactory.CreateOutcoming(
            идентификатор, отправитель, "C6", контрагент.PostalAddress!);
        await письма.AddAsync(черновик);

        var напечатанное = MailFactory.CreateOutcoming(
            идентификатор, отправитель, "C6", контрагент.PostalAddress!);
        напечатанное.Status = MailStatus.Printed;
        await письма.AddAsync(напечатанное);

        var новыйАдрес = Address.Create("125009", ["ООО «Техно»", "", "", "", "г. Москва", ""]).Value!;
        var исполнитель = new CascadeAddressUpdater(письма);

        var изменено = await исполнитель.UpdateUnprintedLettersAddressAsync(идентификатор, новыйАдрес);

        Assert.Equal(1, изменено);

        var черновикПосле = await письма.GetByIdAsync(черновик.Id);
        var напечатанноеПосле = await письма.GetByIdAsync(напечатанное.Id);

        Assert.Equal("125009", черновикПосле!.RecipientAddress!.Zip);
        Assert.Equal("101000", напечатанноеПосле!.RecipientAddress!.Zip);
    }

    [Fact]
    public async Task BatchEditService_ПакетноеИзменениеПоля_МеняетУВсехЗаписей()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var служба = new BatchEditService(контрагенты, база.UnitOfWork);

        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первое»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второе»"));

        var результат = await служба.BatchUpdateFieldAsync([первый, второй], "Phone", "+7 495 000-00-00");

        Assert.True(результат.IsSuccess);
        Assert.Equal(2, результат.Value);

        Assert.Equal("+7 495 000-00-00", (await контрагенты.GetByIdAsync(первый))!.Phone);
        Assert.Equal("+7 495 000-00-00", (await контрагенты.GetByIdAsync(второй))!.Phone);
    }

    [Fact]
    public async Task BatchEditService_НеизвестноеПоле_Отклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var служба = new BatchEditService(контрагенты, база.UnitOfWork);

        var результат = await служба.BatchUpdateFieldAsync([1], "НеизвестноеПоле", "значение");

        Assert.True(результат.IsFailure);
    }

    [Fact]
    public async Task CounterpartyManager_МягкоеУдаление_ЗаписьИсключаетсяИзРабочейВыборки()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var менеджер = new CounterpartyManager(контрагенты, база.UnitOfWork);

        var идентификатор = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Техно»"));

        var результат = await менеджер.FastDeleteMarkAsync([идентификатор]);

        Assert.True(результат.IsSuccess);
        Assert.Equal(1, результат.Value);

        var страница = await контрагенты.GetPagedAsync(new PageRequest(0, 50));

        Assert.Equal(0, страница.TotalCount);

        var восстановление = await менеджер.RestoreAsync([идентификатор]);

        Assert.True(восстановление.IsSuccess);
        Assert.Equal(1, (await контрагенты.GetPagedAsync(new PageRequest(0, 50))).TotalCount);
    }

    [Fact]
    public void MailFactory_НовоеОтправление_ПолучаетСтатусСозданоИСнимокАдреса()
    {
        var адрес = Address.Create(
            "101000",
            ["ПАО «МосБанк»", "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", ""]).Value!;

        var письмо = MailFactory.CreateOutcoming(10, 20, "C6", адрес, isVip: true);

        Assert.Equal(MailStatus.Draft, письмо.Status);
        Assert.Equal(10, письмо.CounterpartyId);
        Assert.Equal(20, письмо.SenderId);
        Assert.Equal("C6", письмо.EnvelopeType);
        Assert.Equal(Mail.MinimumCopyCount, письмо.CopyCount);
        Assert.True(письмо.IsVip);
        Assert.Equal("101000", письмо.RecipientAddress!.Zip);
        Assert.NotEqual(default, письмо.DateCreated);
    }

    [Fact]
    public void MailFactory_БезОтправителя_ВыбрасываетИсключение()
    {
        var адрес = Address.Create("101000", ["", "", "", "", "", ""]).Value!;

        Assert.Throws<InvalidOperationException>(() =>
            MailFactory.CreateOutcoming(10, 0, "C6", адрес));
    }

    [Fact]
    public void MailFactory_ЧислоКопийМеньшеМинимального_ПриводитсяКМинимальному()
    {
        var адрес = Address.Create("101000", ["", "", "", "", "", ""]).Value!;

        var письмо = MailFactory.CreateOutcoming(10, 20, "C6", адрес, copyCount: 0);

        Assert.Equal(Mail.MinimumCopyCount, письмо.CopyCount);
    }

    [Fact]
    public async Task MailService_СозданиеОтправления_ФиксируетСнимокАдреса()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);
        var журнал = new PrintHistoryRepository(база.Session);
        var служба = new MailService(письма, контрагенты, журнал, база.UnitOfWork);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        var идентификатор = await контрагенты.AddAsync(контрагент);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));

        var письмо = MailFactory.CreateOutcoming(идентификатор, отправитель, "C6", контрагент.PostalAddress!);

        var результат = await служба.CreateAsync(письмо);

        Assert.True(результат.IsSuccess);
        Assert.Equal("101000", результат.Value.RecipientAddress!.Zip);
        Assert.Equal(MailStatus.Draft, результат.Value.Status);
    }

    [Fact]
    public async Task GetSendersAsync_Отправитель_ВозвращаетсяТолькоОтмеченный()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);

        await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));
        await контрагенты.AddAsync(НовыйКонтрагент("ООО «Получатель»"));

        var отправители = await контрагенты.GetSendersAsync();

        Assert.Single(отправители);
        Assert.Equal("ООО «Отправитель»", отправители[0].Name);
    }

    [Fact]
    public async Task GetDistinctFieldValuesAsync_ПолеВозвращаетУникальныеЗначения()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);

        var первый = НовыйКонтрагент("ООО «Первое»");
        первый.Inn = "7707083893";
        await контрагенты.AddAsync(первый);

        var второй = НовыйКонтрагент("ООО «Второе»");
        второй.Inn = "7707083893";
        await контрагенты.AddAsync(второй);

        var третий = НовыйКонтрагент("ООО «Третье»");
        третий.Inn = "7740000005";
        await контрагенты.AddAsync(третий);

        var значения = await контрагенты.GetDistinctFieldValuesAsync("inn");

        Assert.Equal(["7707083893", "7740000005"], значения);
    }

    [Fact]
    public async Task GetDistinctFieldValuesAsync_НеизвестноеПоле_ВыбрасываетИсключение()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            контрагенты.GetDistinctFieldValuesAsync("неизвестное_поле"));
    }

    [Fact]
    public async Task Пагинация_СмещениеИРазмерСтраницы_ВозвращаетОкноВыборки()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);

        for (var индекс = 0; индекс < 250; индекс++)
        {
            await контрагенты.AddAsync(НовыйКонтрагент($"Контрагент {индекс:000}"));
        }

        var первая = await контрагенты.GetPagedAsync(new PageRequest(0, 100));
        var вторая = await контрагенты.GetPagedAsync(new PageRequest(100, 100));
        var третья = await контрагенты.GetPagedAsync(new PageRequest(200, 100));

        Assert.Equal(250, первая.TotalCount);
        Assert.Equal(100, первая.Items.Count);
        Assert.Equal(100, вторая.Items.Count);
        Assert.Equal(50, третья.Items.Count);

        Assert.Equal("Контрагент 000", первая.Items[0].Name);
        Assert.Equal("Контрагент 100", вторая.Items[0].Name);
        Assert.Equal("Контрагент 200", третья.Items[0].Name);
    }

    [Fact]
    public async Task Пагинация_ОтборПоГруппе_ОграничиваетВыборку()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);

        var группа = new Group { Name = "Поставщики" };
        await группы.AddAsync(группа);

        var первый = НовыйКонтрагент("ООО «Первое»");
        первый.GroupId = группа.Id;
        await контрагенты.AddAsync(первый);

        await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второе»"));

        var страница = await контрагенты.GetPagedAsync(
            new PageRequest(0, 100),
            new CounterpartyFilter(GroupId: группа.Id));

        Assert.Equal(1, страница.TotalCount);
        Assert.Equal("ООО «Первое»", страница.Items[0].Name);
    }

    [Fact]
    public void Пагинация_НормализацияЗапроса_ОграничиваетРазмерСтраницы()
    {
        var запрос = new PageRequest(-10, 10_000);

        var нормализованный = запрос.Normalized();

        Assert.Equal(0, нормализованный.Offset);
        Assert.Equal(PageRequest.MaxLimit, нормализованный.Limit);
    }

    [Fact]
    public async Task ОбновлениеСчетчикаЧерновиков_НеТронутыНапечатанные()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        var идентификатор = await контрагенты.AddAsync(контрагент);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));

        var черновик = MailFactory.CreateOutcoming(идентификатор, отправитель, "C6", контрагент.PostalAddress!);
        await письма.AddAsync(черновик);

        await база.Connection.ExecuteAsync(
            "UPDATE mails SET to_zip = '125009' WHERE id = @Id;",
            new { Id = черновик.Id });

        var после = await письма.GetByIdAsync(черновик.Id);

        Assert.Equal("125009", после!.RecipientAddress!.Zip);
    }
}
