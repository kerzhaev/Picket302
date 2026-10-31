using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка правила снимков: журнал напечатанных конвертов хранит адреса на
/// момент печати, последующие изменения карточек контрагентов не изменяют
/// исторические записи (ТЗ §3.1.19).
/// </summary>
public sealed class PrintHistorySnapshotTests
{
    private static Address Адрес(string организация, string город) => Address.Create(
        "101000",
        [организация, string.Empty, string.Empty, string.Empty, город, string.Empty]).Value;

    [Fact]
    public async Task Журнал_СнимокАдресаНеИзменяетсяПриПравкеКарточки()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);
        var журнал = new PrintHistoryRepository(база.Session);
        var отправления = new MailService(письма, контрагенты, журнал, база.UnitOfWork);

        var получатель = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Получатель»",
            PostalAddress = Адрес("ООО «Получатель»", "г. Москва"),
        };

        var отправитель = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Отправитель»",
            PostalAddress = Адрес("ООО «Отправитель»", "г. Санкт-Петербург"),
        };

        await контрагенты.AddAsync(получатель);
        await контрагенты.AddAsync(отправитель);

        var письмо = new Mail
        {
            CounterpartyId = получатель.Id,
            SenderId = отправитель.Id,
            CopyCount = 2,
            IsVip = true,
            RecipientAddress = получатель.PostalAddress,
        };

        await отправления.CreateAsync(письмо);

        var запись = await отправления.RegisterPrintAsync(
            письмо.Id,
            printJobId: null,
            "Конверт C5");

        Assert.True(запись.IsSuccess);
        Assert.Contains("ООО «Получатель»", запись.Value.RecipientAddressSnapshot, StringComparison.Ordinal);
        Assert.Contains("ООО «Отправитель»", запись.Value.SenderAddressSnapshot, StringComparison.Ordinal);
        Assert.Equal(2, запись.Value.CopyCount);
        Assert.True(запись.Value.IsVip);

        // Карточка получателя меняется после печати.
        получатель.PostalAddress = Адрес("ООО «Получатель» (переименовано)", "г. Казань");
        await контрагенты.UpdateAsync(получатель);

        var сохранённая = await журнал.GetByIdAsync(запись.Value.Id);

        Assert.NotNull(сохранённая);
        Assert.Contains("ООО «Получатель»", сохранённая!.RecipientAddressSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("переименовано", сохранённая.RecipientAddressSnapshot, StringComparison.Ordinal);
        Assert.Contains("г. Москва", сохранённая.RecipientAddressSnapshot, StringComparison.Ordinal);
    }

    /// <summary>
    /// Снимок содержит индекс и шесть адресных строк — адрес не хранится одной
    /// строкой (ТЗ §3.1.19).
    /// </summary>
    [Fact]
    public async Task Журнал_СнимокСодержитИндексИШестьСтрок()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);
        var журнал = new PrintHistoryRepository(база.Session);
        var отправления = new MailService(письма, контрагенты, журнал, база.UnitOfWork);

        var контрагент = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Получатель»",
            PostalAddress = Адрес("ООО «Получатель»", "г. Москва"),
        };

        await контрагенты.AddAsync(контрагент);

        var письмо = new Mail
        {
            CounterpartyId = контрагент.Id,
            SenderId = контрагент.Id,
            CopyCount = 1,
            RecipientAddress = контрагент.PostalAddress,
        };

        await отправления.CreateAsync(письмо);

        var запись = await отправления.RegisterPrintAsync(письмо.Id, null, "Конверт C5");
        var строки = запись.Value.RecipientAddressSnapshot.Split('\n');

        Assert.Equal(Address.LineCount + 1, строки.Length);
        Assert.Equal("101000", строки[0]);
        Assert.Equal("ООО «Получатель»", строки[1]);
    }

    /// <summary>
    /// Отправление не может быть подтверждено без промежуточного «Отправлено»
    /// (ТЗ §8.1, функция 24).
    /// </summary>
    [Fact]
    public async Task Отправление_ПодтверждениеМинуяОтправкуОтклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);
        var журнал = new PrintHistoryRepository(база.Session);
        var отправления = new MailService(письма, контрагенты, журнал, база.UnitOfWork);

        var контрагент = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Получатель»",
            PostalAddress = Адрес("ООО «Получатель»", "г. Москва"),
        };

        await контрагенты.AddAsync(контрагент);

        var письмо = new Mail
        {
            CounterpartyId = контрагент.Id,
            SenderId = контрагент.Id,
            CopyCount = 1,
            RecipientAddress = контрагент.PostalAddress,
        };

        await отправления.CreateAsync(письмо);

        var недопустимый = await отправления.TransitionStatusAsync(письмо.Id, MailStatus.Confirmed);
        Assert.True(недопустимый.IsFailure);

        var печать = await отправления.TransitionStatusAsync(письмо.Id, MailStatus.Printed);
        Assert.True(печать.IsSuccess);

        var отправка = await отправления.TransitionStatusAsync(письмо.Id, MailStatus.Dispatched);
        Assert.True(отправка.IsSuccess);

        var подтверждение = await отправления.TransitionStatusAsync(письмо.Id, MailStatus.Confirmed);
        Assert.True(подтверждение.IsSuccess);
        Assert.Equal(MailStatus.Confirmed, подтверждение.Value.Status);
        Assert.NotNull(подтверждение.Value.DateConfirmation);
    }

    /// <summary>
    /// Число экземпляров отправления не может быть меньше единицы.
    /// </summary>
    [Fact]
    public async Task Отправление_ЧислоЭкземпляровНеМеньшеЕдиницы()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);
        var журнал = new PrintHistoryRepository(база.Session);
        var отправления = new MailService(письма, контрагенты, журнал, база.UnitOfWork);

        var контрагент = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Получатель»",
            PostalAddress = Адрес("ООО «Получатель»", "г. Москва"),
        };

        await контрагенты.AddAsync(контрагент);

        var результат = await отправления.CreateAsync(new Mail
        {
            CounterpartyId = контрагент.Id,
            SenderId = контрагент.Id,
            CopyCount = 0,
            RecipientAddress = контрагент.PostalAddress,
        });

        Assert.True(результат.IsFailure);
    }

    /// <summary>
    /// Служба адресной книги отклоняет карточку с неверным ИНН и с неверным
    /// индексом (ТЗ §8.1, функция 1).
    /// </summary>
    [Fact]
    public async Task Карточка_НекорректныеРеквизитыОтклоняются()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var контрагенты = new CounterpartyRepository(база.Session);
        var служба = new CounterpartyService(контрагенты, база.UnitOfWork);

        var сНевернымИнн = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Техно»",
            Inn = "7707083894",
            PostalAddress = Адрес("ООО «Техно»", "г. Москва"),
        };

        Assert.True((await служба.SaveAsync(сНевернымИнн)).IsFailure);

        var сНевернымКпп = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Техно»",
            Kpp = "77070800",
            PostalAddress = Адрес("ООО «Техно»", "г. Москва"),
        };

        Assert.True((await служба.SaveAsync(сНевернымКпп)).IsFailure);

        var безНаименования = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "   ",
            PostalAddress = Адрес("ООО «Техно»", "г. Москва"),
        };

        Assert.True((await служба.SaveAsync(безНаименования)).IsFailure);

        var корректный = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Техно»",
            Inn = "7707083893",
            Kpp = "770708001",
            Bik = "044525225",
            PostalAddress = Адрес("ООО «Техно»", "г. Москва"),
        };

        Assert.True((await служба.SaveAsync(корректный)).IsSuccess);
    }
}
