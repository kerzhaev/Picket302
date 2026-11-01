using Picket302.Application.Services;
using Picket302.Application.Setup;
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
/// Проверка демо-базы, дерева категорий и отправителей (ТЗ §8.11, функции 137 и
/// 154; §8.12, функции 153 и 157).
/// </summary>
public sealed class DemoAndCategoriesTests
{
    private static (DemoDatabaseSeeder Сид, CounterpartyRepository Контрагенты, GroupRepository Группы)
        Состав(SqliteTestDatabase база)
    {
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var поля = new CounterpartyFieldRepository(база.Session);
        var обслуживание = new DatabaseMaintenanceRepository(база.Session);

        return (
            new DemoDatabaseSeeder(группы, контрагенты, поля, обслуживание, база.UnitOfWork),
            контрагенты,
            группы);
    }

    [Fact]
    public async Task SeedDemoData_ЧистаяБаза_СоздаётКатегорииИКонтрагентов()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (сид, контрагенты, группы) = Состав(база);

        var создано = await сид.SeedDemoDataAsync();

        Assert.True(создано > 0);
        Assert.Equal(создано, (await контрагенты.GetPagedAsync(new PageRequest(0, 50))).TotalCount);

        var категории = await группы.GetAllAsync();

        Assert.Contains(категории, категория => категория.Name == Category.UnparsedName);
        Assert.Contains(категории, категория => категория.IsDefault);
        Assert.Single(категории, категория => категория.IsDefault);
    }

    [Fact]
    public async Task SeedDemoData_ПовторныйВызов_НеДублируетКатегории()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (сид, _, группы) = Состав(база);

        await сид.SeedDemoDataAsync();
        var количествоДо = (await группы.GetAllAsync()).Count;

        await сид.SeedDemoDataAsync();

        Assert.Equal(количествоДо, (await группы.GetAllAsync()).Count);
    }

    [Fact]
    public async Task ClearDatabase_ОчисткаКатегорий_УдаляетКатегорииИСоздаётНеразобранные()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (сид, контрагенты, группы) = Состав(база);

        await сид.SeedDemoDataAsync();

        var результат = await сид.ClearDatabaseAsync(ClearScope.CounterpartyCategories);

        Assert.True(результат.IsSuccess);

        var категории = await группы.GetAllAsync();

        Assert.Single(категории);
        Assert.Equal(Category.UnparsedName, категории[0].Name);
        Assert.True(категории[0].IsDefault);

        // Контрагенты сохраняются: очистка категорий их не затрагивает.
        Assert.True((await контрагенты.GetPagedAsync(new PageRequest(0, 50))).TotalCount > 0);
    }

    [Fact]
    public async Task ClearDatabase_ОчисткаКонтрагентов_УдаляетЗаписиИСохраняетКатегории()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (сид, контрагенты, группы) = Состав(база);

        await сид.SeedDemoDataAsync();
        var категорийДо = (await группы.GetAllAsync()).Count;

        var результат = await сид.ClearDatabaseAsync(ClearScope.Counterparties);

        Assert.True(результат.IsSuccess);
        Assert.Equal(0, (await контрагенты.GetPagedAsync(new PageRequest(0, 50))).TotalCount);
        Assert.Equal(категорийДо, (await группы.GetAllAsync()).Count);
    }

    [Fact]
    public async Task ClearDatabase_ОчисткаРассылок_НеЗатрагиваетАдреснуюКнигу()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (сид, контрагенты, _) = Состав(база);

        await сид.SeedDemoDataAsync();
        var записейДо = (await контрагенты.GetPagedAsync(new PageRequest(0, 50))).TotalCount;

        var результат = await сид.ClearDatabaseAsync(ClearScope.Distributions);

        Assert.True(результат.IsSuccess);
        Assert.Equal(записейДо, (await контрагенты.GetPagedAsync(new PageRequest(0, 50))).TotalCount);
    }

    [Fact]
    public async Task GetScopeCountsAsync_ВозвращаетЧисленностьПоВсемОбластям()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (сид, _, _) = Состав(база);

        await сид.SeedDemoDataAsync();

        var численность = await сид.GetScopeCountsAsync();

        Assert.Equal(4, численность.Count);
        Assert.True(численность[ClearScope.Counterparties] > 0);
        Assert.True(численность[ClearScope.CounterpartyCategories] > 0);
    }

    [Fact]
    public async Task CategoryTreeService_НоваяКатегория_ДобавляетсяВСправочник()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        var результат = await служба.CreateCategoryAsync("Поставщики");

        Assert.True(результат.IsSuccess);
        Assert.Equal("Поставщики", результат.Value.Name);
        Assert.Null(результат.Value.ParentId);
    }

    [Fact]
    public async Task CategoryTreeService_ДубликатНазвания_Отклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        await служба.CreateCategoryAsync("Поставщики");

        var повтор = await служба.CreateCategoryAsync("Поставщики");

        Assert.True(повтор.IsFailure);
    }

    [Fact]
    public async Task CategoryTreeService_УдалениеКатегории_ЗаписиПереносятсяВНеразобранные()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        var поУмолчанию = await группы.GetDefaultAsync();

        Assert.NotNull(поУмолчанию);

        var категория = await служба.CreateCategoryAsync("Поставщики");

        var контрагент = new Counterparty
        {
            Name = "ООО «Техно»",
            Type = CounterpartyType.LegalEntity,
            GroupId = категория.Value.Id,
            CreatedAt = DateTime.UtcNow,
        };

        var идентификатор = await контрагенты.AddAsync(контрагент);

        var результат = await служба.DeleteCategoryAsync(категория.Value.Id);

        Assert.True(результат.IsSuccess);
        Assert.Equal(1, результат.Value);

        var перенесённый = await контрагенты.GetByIdAsync(идентификатор);

        Assert.NotNull(перенесённый);
        Assert.Equal(поУмолчанию.Id, перенесённый!.GroupId);
    }

    [Fact]
    public async Task CategoryTreeService_УдалениеНеразобранных_Блокируется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        var поУмолчанию = await группы.GetDefaultAsync();

        Assert.NotNull(поУмолчанию);

        var результат = await служба.DeleteCategoryAsync(поУмолчанию!.Id);

        Assert.True(результат.IsFailure);
    }

    [Fact]
    public async Task CategoryTreeService_ПереносВКатегорию_МеняетГруппуКонтрагента()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        var категория = await служба.CreateCategoryAsync("Получатели");

        var контрагент = new Counterparty
        {
            Name = "ООО «Техно»",
            Type = CounterpartyType.LegalEntity,
            CreatedAt = DateTime.UtcNow,
        };

        var идентификатор = await контрагенты.AddAsync(контрагент);

        var результат = await служба.MoveCounterpartyAsync(идентификатор, категория.Value.Id);

        Assert.True(результат.IsSuccess);
        Assert.Equal(категория.Value.Id, результат.Value.GroupId);
    }

    [Fact]
    public async Task AgentService_СозданиеАгента_ЗаписьПомечаетсяКакОтправитель()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var служба = new AgentService(контрагенты, база.UnitOfWork);

        var результат = await служба.CreateAgentAsync(new AgentDto
        {
            Name = "ООО «Отправитель»",
            Type = CounterpartyType.LegalEntity,
            Inn = "7707083893",
            Kpp = "774001001",
            Bik = "044525225",
            Account = "40702810000000000001",
            BankName = "ПАО СБЕРБАНК",
        });

        Assert.True(результат.IsSuccess);
        Assert.True(результат.Value.IsSender);

        var отправители = await служба.ListAgentsAsync();

        Assert.Single(отправители);
        Assert.Equal("ООО «Отправитель»", отправители[0].Name);
    }

    [Fact]
    public async Task AgentService_НекорректныйИНН_Отклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var служба = new AgentService(контрагенты, база.UnitOfWork);

        var результат = await служба.CreateAgentAsync(new AgentDto
        {
            Name = "ООО «Отправитель»",
            Type = CounterpartyType.LegalEntity,
            Inn = "1234567890",
        });

        Assert.True(результат.IsFailure);
    }

    /// <summary>
    /// Функция 8: закрытая группа (запрет добавления и удаления записей).
    /// </summary>
    [Fact]
    public async Task CategoryTreeService_ЗакрытаяКатегория_НеПринимаетНовыеЗаписи()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        var категория = await служба.CreateCategoryAsync("Закрытая категория");
        Assert.True(категория.IsSuccess);

        await группы.UpdateAsync(new Group
        {
            Id = категория.Value.Id,
            Name = категория.Value.Name,
            ParentId = null,
            IsDefault = false,
            IsClosed = true,
            SortOrder = 0,
        });

        var контрагент = new Counterparty
        {
            Name = "ООО «Ромашка»",
            Type = CounterpartyType.LegalEntity,
            Inn = "7707083893",
            IsActive = true,
            PostalAddress = Address.Create(
                "101000",
                ["ООО «Ромашка»", "Управление делами", "ул. Тверская, 12", string.Empty, "г. Москва", string.Empty]).Value,
        };

        await контрагенты.AddAsync(контрагент);

        var результат = await служба.MoveCounterpartyAsync(контрагент.Id, категория.Value.Id);

        Assert.True(результат.IsFailure);
        Assert.Contains("закрыта", результат.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Функция 154: переименование категории.
    /// </summary>
    [Fact]
    public async Task CategoryTreeService_Переименование_МеняетНаименование()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        var категория = await служба.CreateCategoryAsync("Поставщики");
        Assert.True(категория.IsSuccess);

        var результат = await служба.RenameCategoryAsync(категория.Value.Id, "  Основные поставщики  ");

        Assert.True(результат.IsSuccess);
        Assert.Equal("Основные поставщики", результат.Value.Name);

        var сохранённая = await группы.GetByIdAsync(категория.Value.Id);

        Assert.NotNull(сохранённая);
        Assert.Equal("Основные поставщики", сохранённая!.Name);
    }

    [Fact]
    public async Task CategoryTreeService_ПереименованиеВДубликат_Отклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        await служба.CreateCategoryAsync("Поставщики");
        var вторая = await служба.CreateCategoryAsync("Получатели");
        Assert.True(вторая.IsSuccess);

        var результат = await служба.RenameCategoryAsync(вторая.Value.Id, "поставщики");

        Assert.True(результат.IsFailure);
        Assert.Contains("уже существует", результат.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CategoryTreeService_ПустоеНаименование_Отклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);
        var служба = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);

        var категория = await служба.CreateCategoryAsync("Поставщики");
        Assert.True(категория.IsSuccess);

        var результат = await служба.RenameCategoryAsync(категория.Value.Id, "   ");

        Assert.True(результат.IsFailure);
        Assert.Contains("не задано", результат.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
