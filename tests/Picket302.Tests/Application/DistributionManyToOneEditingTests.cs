using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Правка рассылки «многие к одному» (ТЗ §8.12, функция 156): центральный
/// получатель неизменен, периферийные отправители добавляются и удаляются,
/// роли писем не переворачиваются, дубли не создаются.
/// </summary>
/// <remarks>
/// Правила вида (F-D1): <c>Distribution.SenderId</c> — центральный получатель;
/// <c>Mail.CounterpartyId</c> — центральный получатель; <c>Mail.SenderId</c> —
/// периферийный отправитель; <c>Mail.RecipientAddress</c> — адрес центрального
/// получателя. Отдельного поля <c>SenderAddress</c> в модели нет: адрес
/// отправителя подставляется источником данных печати из карточки контрагента.
/// </remarks>
public sealed class DistributionManyToOneEditingTests
{
    private static Counterparty Контрагент(string имя, string индекс = "101000") => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = "7707083893",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            индекс,
            [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
    };

    private static async Task<(Distribution Рассылка, long Центральный, long Первый, long Второй)>
        СоздатьМногиеКОдному(SqliteTestDatabase база, DistributionService служба, CounterpartyRepository контрагенты)
    {
        var центральный = await контрагенты.AddAsync(Контрагент("ООО «Центральный»", "220050"));
        var первый = await контрагенты.AddAsync(Контрагент("ООО «Периферийный 1»", "101000"));
        var второй = await контрагенты.AddAsync(Контрагент("ООО «Периферийный 2»", "101000"));

        var результат = await служба.GenerateDistributionAsync(new DistributionPlan
        {
            Name = "Многие к одному",
            Kind = DistributionKind.ManyToOne,
            SenderId = центральный,
            CounterpartyIds = [первый, второй],
        });

        Assert.True(результат.IsSuccess);

        return (результат.Value, центральный, первый, второй);
    }

    private static (CounterpartyRepository Контрагенты, DistributionService Служба) Служба(
        SqliteTestDatabase база)
    {
        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);
        var служба = new DistributionService(
            new DistributionRepository(база.Session),
            письма,
            контрагенты,
            база.UnitOfWork);

        return (контрагенты, служба);
    }

    private static async Task<IReadOnlyList<Mail>> Письма(SqliteTestDatabase база, long id)
        => await new MailRepository(база.Session).GetByDistributionAsync(id);

    [Fact]
    public async Task Правка_БезИзмененияСостава_СохраняетРолиИЦентральногоПолучателя()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (контрагенты, служба) = Служба(база);
        var (рассылка, центральный, первый, второй) = await СоздатьМногиеКОдному(база, служба, контрагенты);

        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            "Многие к одному (правка)",
            DistributionKind.ManyToOne,
            null,
            [первый, второй]));

        Assert.True(результат.IsSuccess);

        var письма = await Письма(база, рассылка.Id);
        var единственная = Assert.Single(await служба.ListDistributionsAsync());

        Assert.Equal(рассылка.Id, единственная.Id);
        Assert.Equal(центральный, единственная.SenderId);
        Assert.Equal(2, письма.Count);
        Assert.All(письма, письмо => Assert.Equal(центральный, письмо.CounterpartyId));
        Assert.Contains(письма, письмо => письмо.SenderId == первый);
        Assert.Contains(письма, письмо => письмо.SenderId == второй);
    }

    [Fact]
    public async Task Правка_ДобавлениеПериферийногоОтправителя_НеМеняетЦентральногоПолучателя()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (контрагенты, служба) = Служба(база);
        var (рассылка, центральный, первый, второй) = await СоздатьМногиеКОдному(база, служба, контрагенты);
        var третий = await контрагенты.AddAsync(Контрагент("ООО «Периферийный 3»", "101000"));

        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            рассылка.Name,
            DistributionKind.ManyToOne,
            центральный,
            [первый, второй, третий]));

        Assert.True(результат.IsSuccess);
        Assert.Equal(1, результат.Value.LettersAdded);
        Assert.Equal(0, результат.Value.LettersRemoved);

        var письма = await Письма(база, рассылка.Id);

        Assert.Equal(3, письма.Count);
        Assert.All(письма, письмо => Assert.Equal(центральный, письмо.CounterpartyId));
        Assert.Contains(письма, письмо => письмо.SenderId == третий);
    }

    [Fact]
    public async Task Правка_УдалениеПериферийногоОтправителя_УдаляетЕгоПисьмо()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (контрагенты, служба) = Служба(база);
        var (рассылка, центральный, первый, второй) = await СоздатьМногиеКОдному(база, служба, контрагенты);

        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            рассылка.Name,
            DistributionKind.ManyToOne,
            null,
            [второй]));

        Assert.True(результат.IsSuccess);
        Assert.Equal(1, результат.Value.LettersRemoved);

        var письма = await Письма(база, рассылка.Id);
        var письмо = Assert.Single(письма);

        Assert.Equal(центральный, письмо.CounterpartyId);
        Assert.Equal(второй, письмо.SenderId);
        Assert.DoesNotContain(письма, п => п.SenderId == первый);
    }

    [Fact]
    public async Task Правка_ПарыСторонПисьмаСохраняются()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (контрагенты, служба) = Служба(база);
        var (рассылка, центральный, первый, второй) = await СоздатьМногиеКОдному(база, служба, контрагенты);
        var третий = await контрагенты.AddAsync(Контрагент("ООО «Периферийный 3»", "101000"));

        await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            рассылка.Name,
            DistributionKind.ManyToOne,
            null,
            [второй, третий]));

        var письма = await Письма(база, рассылка.Id);
        var пары = письма.Select(письмо => (письмо.CounterpartyId, письмо.SenderId)).ToHashSet();

        Assert.Equal(2, письма.Count);
        Assert.Contains((центральный, второй), пары);
        Assert.Contains((центральный, третий), пары);
    }

    [Fact]
    public async Task Правка_RecipientAddressОстаётсяАдресомЦентральногоПолучателя()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (контрагенты, служба) = Служба(база);
        var (рассылка, центральный, первый, второй) = await СоздатьМногиеКОдному(база, служба, контрагенты);
        var третий = await контрагенты.AddAsync(Контрагент("ООО «Периферийный 3»", "101000"));

        await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            рассылка.Name,
            DistributionKind.ManyToOne,
            null,
            [первый, второй, третий]));

        var письма = await Письма(база, рассылка.Id);
        var центральнаяКарточка = await контрагенты.GetByIdAsync(центральный);
        var адресЦентрального = центральнаяКарточка!.GetAddress(центральнаяКарточка.AddressMainType);

        Assert.All(письма, письмо =>
        {
            Assert.NotNull(письмо.RecipientAddress);
            Assert.Equal(адресЦентрального!.Zip, письмо.RecipientAddress!.Zip);
            Assert.Equal(адресЦентрального.Line1, письмо.RecipientAddress!.Line1);
        });
    }

    [Fact]
    public async Task Правка_ЦентральныйПолучательНеСтановитсяПериферийнымОтправителем()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (контрагенты, служба) = Служба(база);
        var (рассылка, центральный, первый, второй) = await СоздатьМногиеКОдному(база, служба, контрагенты);
        var третий = await контрагенты.AddAsync(Контрагент("ООО «Периферийный 3»", "101000"));

        // Центральный получатель не входит в состав участников: письма ему
        // не создаются, и он не оказывается в роли отправителя.
        await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            рассылка.Name,
            DistributionKind.ManyToOne,
            null,
            [первый, второй, третий]));

        var письма = await Письма(база, рассылка.Id);

        Assert.All(письма, письмо => Assert.NotEqual(центральный, письмо.SenderId));
        Assert.All(письма, письмо => Assert.Equal(центральный, письмо.CounterpartyId));
    }

    [Fact]
    public async Task Правка_ПовторноеСохранениеТогоЖеСостава_НеСоздаётДублей()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (контрагенты, служба) = Служба(база);
        var (рассылка, _, первый, второй) = await СоздатьМногиеКОдному(база, служба, контрагенты);

        for (var повтор = 0; повтор < 2; повтор++)
        {
            var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
                рассылка.Id,
                рассылка.Name,
                DistributionKind.ManyToOne,
                null,
                [первый, второй]));

            Assert.True(результат.IsSuccess);
            Assert.Equal(0, результат.Value.LettersAdded);
            Assert.Equal(0, результат.Value.LettersRemoved);
        }

        Assert.Equal(2, (await Письма(база, рассылка.Id)).Count);
        Assert.Single(await служба.ListDistributionsAsync());
    }

    [Fact]
    public async Task Правка_БезОтправителяВЗапросе_ИспользуетСохранённогоЦентральногоПолучателя()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var (контрагенты, служба) = Служба(база);
        var (рассылка, центральный, первый, второй) = await СоздатьМногиеКОдному(база, служба, контрагенты);
        var третий = await контрагенты.AddAsync(Контрагент("ООО «Периферийный 3»", "101000"));

        // Отправитель не передан: сервис обязан взять сохранённого
        // центрального получателя рассылки, а не отказываться от правки.
        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            "Правка без отправителя",
            DistributionKind.ManyToOne,
            SenderId: null,
            [первый, второй, третий]));

        Assert.True(результат.IsSuccess);
        Assert.Equal(центральный, результат.Value.Distribution.SenderId);

        var письма = await Письма(база, рассылка.Id);

        Assert.Equal(3, письма.Count);
        Assert.All(письма, письмо => Assert.Equal(центральный, письмо.CounterpartyId));
    }
}
