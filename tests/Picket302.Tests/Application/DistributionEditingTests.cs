using Picket302.Application.Services;
using Picket302.Domain;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Правка сохранённой рассылки (ТЗ §8.12, функция 156): изменение имени, вида
/// и состава писем без создания дублирующей записи, с откатом транзакции при
/// ошибке.
/// </summary>
public sealed class DistributionEditingTests
{
    private static Counterparty НовыйКонтрагент(string имя, bool отправитель = false, bool сАдресом = true)
    {
        var контрагент = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = имя,
            Inn = "7707083893",
            IsSender = отправитель,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        if (сАдресом)
        {
            контрагент.PostalAddress = Address.Create(
                "101000",
                [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value;
        }

        return контрагент;
    }

    private static DistributionService Служба(SqliteTestDatabase база, out CounterpartyRepository контрагенты)
    {
        контрагенты = new CounterpartyRepository(база.Session);

        var рассылки = new DistributionRepository(база.Session);
        var письма = new MailRepository(база.Session);

        return new DistributionService(рассылки, письма, контрагенты, база.UnitOfWork);
    }

    private static async Task<(Distribution Рассылка, long Первый, long Второй)> СоздатьРассылкуСПолучателями(
        DistributionService служба,
        CounterpartyRepository контрагенты)
    {
        var первый = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Первый»"));
        var второй = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Второй»"));

        var результат = await служба.GenerateDistributionAsync(new DistributionPlan
        {
            Name = "Рассылка до правки",
            Kind = DistributionKind.OneToMany,
            SenderId = первый,
            CounterpartyIds = [первый, второй],
        });

        return (результат.Value, первый, второй);
    }

    [Fact]
    public async Task Правка_ИмениИВида_СохраняетТотЖеИдентификаторБезДубля()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);
        var (рассылка, первый, второй) = await СоздатьРассылкуСПолучателями(служба, контрагенты);
        var моментСоздания = рассылка.CreatedAt;

        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            "Рассылка после правки",
            DistributionKind.OneToMany,
            первый,
            [первый, второй]));

        Assert.True(результат.IsSuccess);
        Assert.Equal(рассылка.Id, результат.Value.Distribution.Id);
        Assert.Equal("Рассылка после правки", результат.Value.Distribution.Name);
        Assert.Equal(моментСоздания, результат.Value.Distribution.CreatedAt);

        var перечень = await служба.ListDistributionsAsync();
        var единственная = Assert.Single(перечень);
        Assert.Equal(рассылка.Id, единственная.Id);
    }

    [Fact]
    public async Task Правка_СоставаПисем_ДобавляетНовыхИУдаляетИсключённых()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);
        var (рассылка, первый, второй) = await СоздатьРассылкуСПолучателями(служба, контрагенты);
        var третий = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Третий»"));

        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            рассылка.Name,
            DistributionKind.OneToMany,
            первый,
            [второй, третий]));

        Assert.True(результат.IsSuccess);
        Assert.Equal(1, результат.Value.LettersAdded);
        Assert.Equal(1, результат.Value.LettersRemoved);

        var письма = await new MailRepository(база.Session)
            .GetByDistributionAsync(рассылка.Id);

        Assert.Equal(2, письма.Count);
        Assert.Contains(письма, письмо => письмо.CounterpartyId == второй);
        Assert.Contains(письма, письмо => письмо.CounterpartyId == третий);
        Assert.DoesNotContain(письма, письмо => письмо.CounterpartyId == первый);

        // Счётчик конвертов пересчитан по итоговому составу.
        var обновлённая = await new DistributionRepository(база.Session)
            .GetByIdAsync(рассылка.Id);

        Assert.Equal(2, обновлённая!.EnvelopesRequired);
    }

    [Fact]
    public async Task Правка_ПовторноеСохранениеТогоЖеСостава_НеСоздаётДубля()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);
        var (рассылка, первый, второй) = await СоздатьРассылкуСПолучателями(служба, контрагенты);

        for (var повтор = 0; повтор < 2; повтор++)
        {
            var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
                рассылка.Id,
                рассылка.Name,
                DistributionKind.OneToMany,
                первый,
                [первый, второй]));

            Assert.True(результат.IsSuccess);
            Assert.Equal(0, результат.Value.LettersAdded);
            Assert.Equal(0, результат.Value.LettersRemoved);
        }

        var письма = await new MailRepository(база.Session)
            .GetByDistributionAsync(рассылка.Id);

        Assert.Equal(2, письма.Count);

        var перечень = await служба.ListDistributionsAsync();
        Assert.Single(перечень);
    }

    [Fact]
    public async Task Правка_ПустойСписокАдресатов_Отклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);
        var (рассылка, _, _) = await СоздатьРассылкуСПолучателями(служба, контрагенты);

        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            "Имя",
            DistributionKind.OneToMany,
            null,
            []));

        Assert.True(результат.IsFailure);
        Assert.Contains("Список адресатов пуст", результат.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Правка_НесуществующаяРассылка_Отклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out _);

        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            404,
            "Имя",
            DistributionKind.OneToMany,
            null,
            [1]));

        Assert.True(результат.IsFailure);
        Assert.Contains("не найдена", результат.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Правка_ТолькоБезАдреса_ОткатываетТранзакцию()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var служба = Служба(база, out var контрагенты);
        var (рассылка, первый, второй) = await СоздатьРассылкуСПолучателями(служба, контрагенты);
        var безАдреса = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Без адреса»", сАдресом: false));

        var результат = await служба.UpdateDistributionAsync(new DistributionUpdateRequest(
            рассылка.Id,
            "Имя после отката",
            DistributionKind.OneToMany,
            первый,
            [безАдреса]));

        Assert.True(результат.IsFailure);

        // Транзакция откатана: имя и состав писем рассылки не изменились.
        var письма = await new MailRepository(база.Session)
            .GetByDistributionAsync(рассылка.Id);

        Assert.Equal(2, письма.Count);

        var сохранённая = await new DistributionRepository(база.Session)
            .GetByIdAsync(рассылка.Id);

        Assert.Equal("Рассылка до правки", сохранённая!.Name);
    }

    [Fact]
    public async Task Правка_СбойФиксацииТранзакции_ОткатываетИзменения()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var рассылки = new DistributionRepository(база.Session);
        var письмаРепозиторий = new MailRepository(база.Session);
        var падающаяФиксация = new ПадающаяПриФиксацииЕдиницаРаботы(база.UnitOfWork);

        var служба = new DistributionService(рассылки, письмаРепозиторий, контрагенты, падающаяФиксация);
        var (рассылка, первый, второй) = await СоздатьРассылкуСПолучателями(служба, контрагенты);
        var третий = await контрагенты.AddAsync(НовыйКонтрагент("ООО «Третий»"));

        падающаяФиксация.Отказывать = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            служба.UpdateDistributionAsync(new DistributionUpdateRequest(
                рассылка.Id,
                "Имя, которое не должно сохраниться",
                DistributionKind.OneToMany,
                первый,
                [третий])));

        падающаяФиксация.Отказывать = false;

        var письма = await письмаРепозиторий.GetByDistributionAsync(рассылка.Id);

        Assert.Equal(2, письма.Count);

        var сохранённая = await рассылки.GetByIdAsync(рассылка.Id);

        Assert.Equal("Рассылка до правки", сохранённая!.Name);
    }

    /// <summary>
    /// Обёртка единицы работы, имитирующая сбой фиксации транзакции: все
    /// операции делегируются реальной единице, кроме <see cref="CommitAsync"/>.
    /// </summary>
    private sealed class ПадающаяПриФиксацииЕдиницаРаботы(IUnitOfWork внутренняя) : IUnitOfWork
    {
        public bool Отказывать { get; set; }

        public bool InTransaction => внутренняя.InTransaction;

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
            => внутренняя.BeginTransactionAsync(cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (Отказывать)
            {
                // Откат выполняет сервис в обработчике исключения — обёртка
                // лишь имитирует сбой самой фиксации.
                throw new InvalidOperationException("Имитация сбоя фиксации транзакции.");
            }

            await внутренняя.CommitAsync(cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => внутренняя.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => внутренняя.DisposeAsync();
    }
}
