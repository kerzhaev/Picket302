using Picket302.Application.Queries;
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
/// Проверки сортировки и фильтрации адресной книги на встроенной SQLite в памяти
/// (ТЗ §8.1, функция 9: «Фильтры и сортировка по любой колонке»).
/// </summary>
public sealed class CounterpartyFilterRepositoryTests
{
    /// <summary>
    /// Создаёт контрагента с заданными реквизитами для проверок отбора.
    /// </summary>
    /// <param name="имя">Наименование.</param>
    /// <param name="инн">ИНН.</param>
    /// <param name="избранный">Признак избранного адреса.</param>
    /// <param name="группа">Идентификатор группы.</param>
    /// <param name="индекс">Почтовый индекс.</param>
    /// <param name="телефон">Телефон.</param>
    /// <returns>Контрагент.</returns>
    private static Counterparty Контрагент(
        string имя,
        string инн = "7707083893",
        bool избранный = false,
        long? группа = null,
        string индекс = "101000",
        string? телефон = null) => new()
        {
            Type = CounterpartyType.LegalEntity,
            Name = имя,
            Inn = инн,
            Phone = телефон,
            IsVip = избранный,
            IsSender = false,
            IsActive = true,
            IsDeleted = false,
            GroupId = группа,
            PostalAddress = Address.Create(
                индекс,
                [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
        };

    /// <summary>
    /// Сортировка по колонке меняет порядок выборки: по возрастанию и по
    /// убыванию.
    /// </summary>
    [Fact]
    public async Task Сортировка_ПоКолонке_МеняетПорядокВыборки()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        await репозиторий.AddAsync(Контрагент("Бутафория"));
        await репозиторий.AddAsync(Контрагент("Аптека"));
        await репозиторий.AddAsync(Контрагент("Салон"));

        var поВозрастанию = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(SortColumn: "name"));

        Assert.Equal(3, поВозрастанию.TotalCount);
        Assert.Equal(["Аптека", "Бутафория", "Салон"], поВозрастанию.Items.Select(запись => запись.Name));

        var поУбыванию = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(SortColumn: "name", SortDescending: true));

        Assert.Equal(["Салон", "Бутафория", "Аптека"], поУбыванию.Items.Select(запись => запись.Name));
    }

    /// <summary>
    /// Сортировка по вычисляемой проекции основного адреса учитывает тип
    /// основного адреса записи (ТЗ §3.1.2: «Три среза адресации»).
    /// </summary>
    [Fact]
    public async Task Сортировка_ПоВычисляемойПроекции_УчитываетТипОсновногоАдреса()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        var поЮридическому = Контрагент("Альфа", индекс: "999999");
        поЮридическому.LegalAddress = Address.Create(
            "101000",
            ["Альфа", "Управление делами", "ул. Тверская, 12", string.Empty, "г. Москва", string.Empty]).Value;
        поЮридическому.AddressMainType = AddressMainType.Legal;
        await репозиторий.AddAsync(поЮридическому);

        var поФактическому = Контрагент("Бета", индекс: "111111");
        поФактическому.ActualAddress = Address.Create(
            "222222",
            ["Бета", "Управление делами", "ул. Тверская, 12", string.Empty, "г. Москва", string.Empty]).Value;
        поФактическому.AddressMainType = AddressMainType.Actual;
        await репозиторий.AddAsync(поФактическому);

        // Индекс основного адреса: «Альфа» — 101000, «Бета» — 222222.
        // По почтовому индексу порядок был бы обратным.
        var выборка = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(SortColumn: "main_zip"));

        Assert.Equal(["Альфа", "Бета"], выборка.Items.Select(запись => запись.Name));
    }

    /// <summary>
    /// Фильтры сужают выборку: текстовый поиск, ИНН, группа (категория),
    /// «избранное».
    /// </summary>
    [Fact]
    public async Task Фильтры_СужаютВыборку()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);

        var группа = new Group { Name = "Поставщики" };
        await группы.AddAsync(группа);

        await репозиторий.AddAsync(Контрагент(
            "Аптека «Здоровье»",
            инн: "500100732259",
            избранный: true,
            группа: группа.Id));
        await репозиторий.AddAsync(Контрагент("Салон «Веснушка»", инн: "7707083893"));

        var поТексту = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(NameFragment: "Здоровье"));

        Assert.Single(поТексту.Items);
        Assert.Equal("Аптека «Здоровье»", поТексту.Items[0].Name);

        var поИнНу = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(Inn: "500100732259"));

        Assert.Single(поИнНу.Items);

        var поГруппе = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(GroupId: группа.Id));

        Assert.Single(поГруппе.Items);

        var поИзбранному = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(IsVip: true));

        Assert.Single(поИзбранному.Items);
        Assert.Equal("Аптека «Здоровье»", поИзбранному.Items[0].Name);
    }

    /// <summary>
    /// Условия слоя запроса применяются репозиторием: слой запроса подключён к
    /// выборке (ТЗ §8.1, функция 9).
    /// </summary>
    [Fact]
    public async Task УсловияСлояЗапроса_ПрименяютсяРепозиторием()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        await репозиторий.AddAsync(Контрагент("Аптека", телефон: "495-111-22-33"));
        await репозиторий.AddAsync(Контрагент("Салон", телефон: "495-444-55-66"));

        var критерии = new FilterCriteria
        {
            Conditions =
            [
                new FilterCriteria.Condition
                {
                    Field = "phone",
                    Operator = FilterOperator.Contains,
                    Value = "111-22-33",
                },
            ],
            SortColumn = "name",
        };

        var выборка = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(Criteria: критерии));

        Assert.Equal(1, выборка.TotalCount);
        Assert.Equal("Аптека", выборка.Items[0].Name);

        // Встроенный фильтр «Избранное» слоя запроса.
        await репозиторий.AddAsync(Контрагент("Банк", избранный: true));

        var избранные = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(Criteria: new FilterCriteria { OnlyFavorites = true }));

        Assert.Single(избранные.Items);
        Assert.Equal("Банк", избранные.Items[0].Name);
    }

    /// <summary>
    /// Недопустимое имя колонки сортировки отвергается: произвольный текст в
    /// запрос не попадает (SQL-инъекция недопустима).
    /// </summary>
    [Fact]
    public async Task НедопустимаяКолонкаСортировки_Отвергается_БезВнедренияSql()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        await репозиторий.AddAsync(Контрагент("Аптека"));

        await Assert.ThrowsAsync<ArgumentException>(() => репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(SortColumn: "name; DROP TABLE counterparties; --")));

        // Таблица цела, данные на месте.
        Assert.Equal(1, (await репозиторий.GetPagedAsync(new PageRequest(0, 10))).TotalCount);
    }

    /// <summary>
    /// Сортировка по наименованию группы учитывает наименование группы —
    /// вычисляемая проекция через подзапрос.
    /// </summary>
    [Fact]
    public async Task Сортировка_ПоНаименованиюГруппы_УчитываетНазваниеГруппы()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);
        var группы = new GroupRepository(база.Session);

        var альфа = new Group { Name = "Альфа" };
        var бета = new Group { Name = "Бета" };
        await группы.AddAsync(альфа);
        await группы.AddAsync(бета);

        await репозиторий.AddAsync(Контрагент("Запись 1", группа: альфа.Id));
        await репозиторий.AddAsync(Контрагент("Запись 2", группа: бета.Id));

        var выборка = await репозиторий.GetPagedAsync(
            new PageRequest(0, 10),
            new CounterpartyFilter(SortColumn: "group"));

        Assert.Equal(["Запись 1", "Запись 2"], выборка.Items.Select(запись => запись.Name));
    }

    /// <summary>
    /// Каждая колонка адресной книги поддерживает сортировку: ключи каталога
    /// согласуются с белым списком репозитория.
    /// </summary>
    [Fact]
    public async Task КаталогКолонокАдреснойКниги_ВсеКлючиПоддерживаютсяСортировкой()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        await репозиторий.AddAsync(Контрагент("Аптека"));
        await репозиторий.AddAsync(Контрагент("Салон"));

        foreach (var колонка in AddressBookColumnCatalog.Columns)
        {
            Assert.True(AddressBookColumnCatalog.ПоддерживаетсяСортировка(колонка.SortKey));

            var выборка = await репозиторий.GetPagedAsync(
                new PageRequest(0, 10),
                new CounterpartyFilter(SortColumn: колонка.SortKey));

            Assert.Equal(2, выборка.TotalCount);
        }
    }
}
