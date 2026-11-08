using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Подстановка значений полей бланка по данным отправлений
/// (ТЗ §6.1; §3.2). Регрессия 2026-11-08: арифметика длин имён колонок
/// всегда давала строку 1 — поля <c>to_str2</c>…<c>to_str6</c> и
/// <c>from_str2</c>…<c>from_str6</c> печатали первую адресную строку
/// (найдено runtime-доказательством рендеринга предпросмотра).
/// </summary>
public sealed class PrintDataSourceTests
{
    /// <summary>
    /// Каждая адресная строка попадает в СВОЁ поле: to_str1..to_str6 и
    /// from_str1..from_str6 — по номеру строки, а не по длине имени колонки.
    /// </summary>
    [Fact]
    public async Task ЗначенияПолей_КаждаяСтрокаАдреса_ВСвоёмПоле()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var письма = new MailRepository(база.Session);
        var контрагенты = new CounterpartyRepository(база.Session);
        var источник = new PrintDataSource(письма, контрагенты);

        var строки = new[]
        {
            "Строка Один",
            "Строка Два",
            "Строка Три",
            "Строка Четыре",
            "Строка Пять",
            "Строка Шесть",
        };

        var контрагент = await контрагенты.AddAsync(new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Получатель»",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            PostalAddress = Address.Create("125009", строки).Value,
        });

        var отправитель = await контрагенты.AddAsync(new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Отправитель»",
            IsActive = true,
            IsSender = true,
            CreatedAt = DateTime.UtcNow,
            PostalAddress = Address.Create("101000",
            [
                "От Строка Один",
                "От Строка Два",
                "От Строка Три",
                "От Строка Четыре",
                "От Строка Пять",
                "От Строка Шесть",
            ]).Value,
        });

        var письмо = await письма.AddAsync(new Mail
        {
            CounterpartyId = контрагент,
            SenderId = отправитель,
            EnvelopeType = "C6",
            CopyCount = Mail.MinimumCopyCount,
            Status = MailStatus.Draft,
            DateCreated = DateTime.UtcNow,
            RecipientAddress = Address.Create("125009", строки).Value,
        });

        var элементы = await источник.GetItemsAsync([письмо]);
        var элемент = Assert.Single(элементы);

        Assert.Equal("Строка Один", элемент.Values["[edt_str_to1]"]);
        Assert.Equal("Строка Два", элемент.Values["[edt_str_to2]"]);
        Assert.Equal("Строка Три", элемент.Values["[edt_str_to3]"]);
        Assert.Equal("Строка Четыре", элемент.Values["[edt_str_to4]"]);
        Assert.Equal("Строка Пять", элемент.Values["[edt_str_to5]"]);
        Assert.Equal("Строка Шесть", элемент.Values["[edt_str_to6]"]);
        Assert.Equal("125009", элемент.Values["[edtIndeks_to]"]);

        Assert.Equal("От Строка Один", элемент.Values["[edt_str_from1]"]);
        Assert.Equal("От Строка Два", элемент.Values["[edt_str_from2]"]);
        Assert.Equal("От Строка Шесть", элемент.Values["[edt_str_from6]"]);
        Assert.Equal("101000", элемент.Values["[edtIndeks_from]"]);
    }
}
