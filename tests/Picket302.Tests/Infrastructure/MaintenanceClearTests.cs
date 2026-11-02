using Dapper;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;
using CounterpartyType = Picket302.Domain.Entities.CounterpartyType;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Регрессии F-B6 и F-B7: очистка адресной книги и категорий не падает
/// на внешних ключах.
///
/// F-B6 — distributions.sender_id и inventories.counterparty_id ссылаются
/// на counterparties без ON DELETE-правила: прежде DELETE FROM counterparties
/// падал SqliteException (FK включены MigrationRunner).
/// F-B7 — самоссылка groups.parent_id ломала очистку категорий.
/// </summary>
public sealed class MaintenanceClearTests
{
    [Fact]
    public async Task ОчисткаКонтрагентов_РассылкаИОписьСуществуют_ОбнулениеБезSqliteException()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var контрагенты = new CounterpartyRepository(база.Session);
        var отправитель = await контрагенты.AddAsync(Контрагент("ООО «Отправитель»"));

        var рассылки = new DistributionRepository(база.Session);
        var рассылка = new Distribution
        {
            Name = "Рассылка",
            SenderId = отправитель,
            CreatedAt = DateTime.UtcNow,
        };

        await рассылки.AddAsync(рассылка);

        await база.Connection.ExecuteAsync(
            "INSERT INTO inventories (name, counterparty_id, created_at) VALUES (@Name, @Id, @Момент);",
            new { Name = "Опись", Id = отправитель, Момент = DateTime.UtcNow });

        var обслуживание = new DatabaseMaintenanceRepository(база.Session);

        // Прежде DELETE FROM counterparties падал на внешних ключах.
        var число = await обслуживание.ClearCounterpartiesAsync();

        Assert.Equal(1, число);

        var senderРассылки = await база.Connection.ExecuteScalarAsync<long?>(
            "SELECT sender_id FROM distributions WHERE id = @Id;",
            new { Id = рассылка.Id });

        var получательОписи = await база.Connection.ExecuteScalarAsync<long?>(
            "SELECT counterparty_id FROM inventories;");

        Assert.Null(senderРассылки);
        Assert.Null(получательОписи);
    }

    [Fact]
    public async Task ОчисткаКатегорий_ДеревоСПодчинёнными_УдаляетсяБезSqliteException()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var группы = new GroupRepository(база.Session);
        var родитель = await группы.AddAsync(new Group { Name = "Родитель" });
        var подчинённая = await группы.AddAsync(new Group { Name = "Подчинённая", ParentId = родитель });

        var обслуживание = new DatabaseMaintenanceRepository(база.Session);

        // Прежде DELETE FROM groups падал на самоссылке parent_id.
        // В базе после миграций уже есть «Неразобранные», поэтому 3.
        var число = await обслуживание.ClearCounterpartyCategoriesAsync();

        Assert.Equal(3, число);

        var группыПосле = await группы.GetAllAsync();

        // Инвариант «Неразобранные» пересоздаётся (ТЗ §8.11, функция 138).
        Assert.Single(группыПосле);
        Assert.Equal(Group.DefaultGroupName, группыПосле[0].Name);
    }

    private static Counterparty Контрагент(string имя) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = "7707083893",
        IsSender = false,
        IsActive = true,
        IsDeleted = false,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "101000",
            [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
    };
}
