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
/// Проверка рассылок на встроенной SQLite в памяти (ТЗ §8.1, функция 21; §8.11,
/// функции 141 и 142; §8.12, функции 155 и 156).
/// </summary>
public sealed class DistributionServiceTests
{
    private static Counterparty НовыйКонтрагент(string имя, bool отправитель = false)
        => new()
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

    private static DistributionService Служба(SqliteTestDatabase база, out CounterpartyRepository контрагенты)
    {
        контрагенты = new CounterpartyRepository(база.Session);

        var рассылки = new DistributionRepository(база.Session);
        var письма = new MailRepository(база.Session);

        return new DistributionService(рассылки, письма, контрагенты, база.UnitOfWork);
    }

    [Fact]
    public async Task GenerateDistribution_ОдинКоМногим_СоздаётПисьмоНаКаждогоПолучателя()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));
        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));

        var план = new DistributionPlan
        {
            Name = "Рассылка по категориям",
            Kind = DistributionKind.OneToMany,
            SenderId = отправитель,
            CounterpartyIds = [первый, второй],
        };

        var результат = await служба.GenerateDistributionAsync(план);

        Assert.True(результат.IsSuccess);
        Assert.Equal(2, результат.Value.EnvelopesRequired);

        var письма = await new MailRepository(база.Session)
            .GetByDistributionAsync(результат.Value.Id);

        Assert.Equal(2, письма.Count);
        Assert.All(письма, письмо => Assert.Equal(отправитель, письмо.SenderId));
        Assert.Contains(письма, письмо => письмо.CounterpartyId == первый);
        Assert.Contains(письма, письмо => письмо.CounterpartyId == второй);
    }

    [Fact]
    public async Task GenerateDistribution_МногиеКОдному_ОбщийПолучательИРазныеОтправители()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var центральный = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Центральный»"));
        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));

        var план = new DistributionPlan
        {
            Name = "Рассылка на категорию",
            Kind = DistributionKind.ManyToOne,
            SenderId = центральный,
            CounterpartyIds = [первый, второй],
        };

        var результат = await служба.GenerateDistributionAsync(план);

        Assert.True(результат.IsSuccess);

        var письма = await new MailRepository(база.Session)
            .GetByDistributionAsync(результат.Value.Id);

        Assert.Equal(2, письма.Count);
        Assert.All(письма, письмо => Assert.Equal(центральный, письмо.CounterpartyId));
        Assert.Contains(письма, письмо => письмо.SenderId == первый);
        Assert.Contains(письма, письмо => письмо.SenderId == второй);
    }

    [Fact]
    public async Task GenerateDistribution_ПустойСписокАдресатов_ГенерацияОтменяется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));

        var результат = await служба.GenerateDistributionAsync(new DistributionPlan
        {
            SenderId = отправитель,
            CounterpartyIds = [],
        });

        Assert.True(результат.IsFailure);
        Assert.Empty(await new DistributionRepository(база.Session).GetAllAsync());
    }

    [Fact]
    public async Task CreateFromSelection_ВыделенныеЗаписи_СоздаётсяРассылкаСГотовымСоставом()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));
        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));

        var результат = await служба.CreateFromSelectionAsync([первый, второй], отправитель, "Объединённая");

        Assert.True(результат.IsSuccess);
        Assert.Equal("Объединённая", результат.Value.Name);
        Assert.Equal(DistributionState.Draft, результат.Value.State);
        Assert.Equal(2, результат.Value.EnvelopesRequired);

        var письма = await new MailRepository(база.Session)
            .GetByDistributionAsync(результат.Value.Id);

        Assert.Equal(2, письма.Count);
    }

    [Fact]
    public async Task CreateFromSelection_ПустоеВыделение_Отменяется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out _);

        var результат = await служба.CreateFromSelectionAsync([], 1);

        Assert.True(результат.IsFailure);
    }

    [Fact]
    public async Task CloneForResend_СохранённаяРассылка_СоздаётКопиюИНеМеняетИсторию()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));
        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));

        var исходная = await служба.GenerateDistributionAsync(new DistributionPlan
        {
            Name = "Исходная",
            SenderId = отправитель,
            CounterpartyIds = [первый, второй],
        });

        var копия = await служба.CloneForResendAsync(исходная.Value.Id);

        Assert.True(копия.IsSuccess);
        Assert.NotEqual(исходная.Value.Id, копия.Value.Distribution.Id);
        Assert.Equal(DistributionState.Draft, копия.Value.Distribution.State);
        Assert.Equal(2, копия.Value.LettersCreated);
        Assert.Empty(копия.Value.SkippedCounterparties);

        // Исходная рассылка не изменена.
        var письмаИсходной = await new MailRepository(база.Session)
            .GetByDistributionAsync(исходная.Value.Id);

        Assert.Equal(2, письмаИсходной.Count);
    }

    [Fact]
    public async Task CloneForResend_УдалённыйКонтрагент_ПропускаетсяСПеречислением()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));
        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));

        var исходная = await служба.GenerateDistributionAsync(new DistributionPlan
        {
            Name = "Исходная",
            SenderId = отправитель,
            CounterpartyIds = [первый, второй],
        });

        await контрагенты.SoftDeleteAsync(второй);

        var копия = await служба.CloneForResendAsync(исходная.Value.Id);

        Assert.True(копия.IsSuccess);
        Assert.Equal(1, копия.Value.LettersCreated);
        Assert.Contains("ООО «Второй»", копия.Value.SkippedCounterparties);
    }

    [Fact]
    public async Task CloneForResend_НесуществующаяРассылка_Отменяется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out _);

        var результат = await служба.CloneForResendAsync(9999);

        Assert.True(результат.IsFailure);
    }

    [Fact]
    public async Task ListDistributions_ФильтрПоСостоянию_ВозвращаетТолькоНужные()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var отправитель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Отправитель»", отправитель: true));
        var получатель = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Получатель»"));

        var созданная = await служба.GenerateDistributionAsync(new DistributionPlan
        {
            Name = "Черновик",
            SenderId = отправитель,
            CounterpartyIds = [получатель],
        });

        await служба.SaveAsync(созданная.Value.Id);

        var черновики = await служба.ListDistributionsAsync(new DistributionFilter(State: DistributionState.Draft));
        var сохранённые = await служба.ListDistributionsAsync(new DistributionFilter(State: DistributionState.Saved));

        Assert.Empty(черновики);
        Assert.Single(сохранённые);
        Assert.Equal("Черновик", сохранённые[0].Name);
        Assert.Equal(1, сохранённые[0].RecipientCount);
        Assert.Equal(1, сохранённые[0].EnvelopesRequired);
    }

    [Fact]
    public async Task MarkDispatched_ПустаяРассылка_Блокируется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out _);

        var рассылки = new DistributionRepository(база.Session);
        var пустая = new Distribution { Name = "Пустая", CreatedAt = DateTime.UtcNow };
        await рассылки.AddAsync(пустая);

        var результат = await служба.MarkDispatchedAsync(пустая.Id);

        Assert.True(результат.IsFailure);
    }

    /// <summary>
    /// Регрессия F-D1: в «многие к одному» адрес получателя снимается
    /// у центрального контрагента, а не у перебираемого отправителя.
    /// Прежде в <c>RecipientAddress</c> попадал адрес стороны отправителя.
    /// </summary>
    [Fact]
    public async Task GenerateDistribution_МногиеКОдному_АдресПолучателяУЦентрального()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var центральный = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Центральный»"));
        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));

        var план = new DistributionPlan
        {
            Kind = DistributionKind.ManyToOne,
            SenderId = центральный,
            CounterpartyIds = [первый, второй],
        };

        var результат = await служба.GenerateDistributionAsync(план);

        Assert.True(результат.IsSuccess);

        var письма = await new MailRepository(база.Session)
            .GetByDistributionAsync(результат.Value.Id);

        Assert.Equal(2, письма.Count);

        // Адрес получателя в обоих письмах — адрес центрального контрагента.
        Assert.All(письма, письмо =>
        {
            Assert.Equal("ООО «Центральный»", письмо.RecipientAddress.Lines[0]);
            Assert.Equal(центральный, письмо.CounterpartyId);
        });

        // Отправители писем различаются.
        Assert.Equal(
            new HashSet<long> { первый, второй },
            письма.Select(письмо => письмо.SenderId).ToHashSet());
    }

    /// <summary>
    /// Регрессия F-D2: клон рассылки «многие к одному» сохраняет пары
    /// (получатель, отправитель) исходных писем. Прежде роли инвертировались.
    /// </summary>
    [Fact]
    public async Task CloneForResend_МногиеКОдному_СохраняетРолиПисем()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);

        var центральный = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Центральный»"));
        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));

        var исходная = await служба.GenerateDistributionAsync(new DistributionPlan
        {
            Kind = DistributionKind.ManyToOne,
            SenderId = центральный,
            CounterpartyIds = [первый, второй],
        });

        Assert.True(исходная.IsSuccess);

        var копия = await служба.CloneForResendAsync(исходная.Value.Id);

        Assert.True(копия.IsSuccess);
        Assert.Equal(2, копия.Value.LettersCreated);

        var письма = await new MailRepository(база.Session)
            .GetByDistributionAsync(копия.Value.Distribution.Id);

        Assert.Equal(2, письма.Count);

        Assert.All(письма, письмо => Assert.Equal(центральный, письмо.CounterpartyId));

        Assert.Equal(
            new HashSet<long> { первый, второй },
            письма.Select(письмо => письмо.SenderId).ToHashSet());
    }
}
