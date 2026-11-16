using System.Net.Http.Json;
using System.Text.Json;
using Picket302.Domain.Entities;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Регрессии по итогам независимой приёмки 16.11.2026.
/// </summary>
/// <remarks>
/// <para>
/// Здесь фиксируются два дефекта, найденные на живом пилоте: карточка
/// отправителя сохранялась усечённой, а дату создания новой записи задавал
/// клиент.
/// </para>
/// <para>
/// Проверки идут по настоящему протоколу на поднятой службе: так же, как
/// клиент обращается к общему серверу.
/// </para>
/// </remarks>
public sealed class SenderCardRegressionTests
{
    /// <summary>
    /// Карточка отправителя со всеми заполненными реквизитами сохраняется
    /// целиком и читается обратно без потерь.
    /// </summary>
    [Fact]
    public async Task Отправитель_Сохраняется_Полной_Карточкой()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК QA");

        var адрес = new AddressDto(
            "190000",
            new[] { "г. Санкт-Петербург", "ул. Проверочная, д. 7", string.Empty, string.Empty, string.Empty, string.Empty },
            null);

        var карточка = SharedServiceIntegrationTests.Контрагент("ООО «Отправитель Полный»") with
        {
            ShortName = "Отправитель Полный",
            Inn = "7707083893",
            Kpp = "770701001",
            BankName = "Тестовый банк",
            Phone = "+7 000 000-00-33",
            Email = "sender@example.invalid",
            SpecialNotes = "Тестовая карточка отправителя, создана автотестом.",
            IsSender = true,
            Addresses = new AddressSetDto(адрес, адрес, адрес, AddressMainType.Postal),
        };

        var созданный = await клиент
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(карточка, 0));

        Assert.True(созданный.Id > 0);

        using var ответ = await клиент.Клиент.GetAsync(
            $"/api/v1/queries/counterparties/{созданный.Id}");

        ответ.EnsureSuccessStatusCode();

        var прочитанный = JsonSerializer.Deserialize<CounterpartyDto>(
            await ответ.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.True(прочитанный.IsSender);
        Assert.Equal("Отправитель Полный", прочитанный.ShortName);
        Assert.Equal("7707083893", прочитанный.Inn);
        Assert.Equal("770701001", прочитанный.Kpp);
        Assert.Equal("Тестовый банк", прочитанный.BankName);
        Assert.Equal("+7 000 000-00-33", прочитанный.Phone);
        Assert.Equal("sender@example.invalid", прочитанный.Email);
        Assert.Equal("Тестовая карточка отправителя, создана автотестом.", прочитанный.SpecialNotes);

        Assert.NotNull(прочитанный.Addresses.Postal);
        Assert.Equal("190000", прочитанный.Addresses.Postal!.Zip);
        Assert.Equal("г. Санкт-Петербург", прочитанный.Addresses.Postal.Lines[0]);
        Assert.Equal("ул. Проверочная, д. 7", прочитанный.Addresses.Postal.Lines[1]);
    }

    /// <summary>
    /// Служба назначает дату создания новой записи сама и не принимает
    /// клиентскую; при изменении записи дата создания не меняется.
    /// </summary>
    [Fact]
    public async Task Новая_Запись_Получает_Дату_Создания_Службы()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК QA");

        var чужаяДата = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var карточка = SharedServiceIntegrationTests.Контрагент("ООО «Дата создания»") with
        {
            CreatedAt = чужаяДата,
            UpdatedAt = чужаяДата,
        };

        var созданный = await клиент
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(карточка, 0));

        using var послеСоздания = await клиент.Клиент.GetAsync(
            $"/api/v1/queries/counterparties/{созданный.Id}");

        послеСоздания.EnsureSuccessStatusCode();

        var прочитанный = JsonSerializer.Deserialize<CounterpartyDto>(
            await послеСоздания.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.True(
            Math.Abs((прочитанный.CreatedAt - DateTime.UtcNow).TotalMinutes) < 5,
            $"Дата создания должна быть моментом создания, а получена {прочитанный.CreatedAt:O}.");
        Assert.NotEqual(чужаяДата, прочитанный.CreatedAt);

        var датаСоздания = прочитанный.CreatedAt;

        // Изменение записи: телефон меняется, дата создания остаётся прежней.
        var изменённая = карточка with
        {
            Id = прочитанный.Id,
            Version = прочитанный.Version,
            Phone = "+7 000 000-00-44",
            CreatedAt = чужаяДата,
        };

        await клиент
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(изменённая, прочитанный.Version));

        using var послеИзменения = await клиент.Клиент.GetAsync(
            $"/api/v1/queries/counterparties/{прочитанный.Id}");

        послеИзменения.EnsureSuccessStatusCode();

        var итог = JsonSerializer.Deserialize<CounterpartyDto>(
            await послеИзменения.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Equal("+7 000 000-00-44", итог.Phone);
        Assert.Equal(датаСоздания, итог.CreatedAt);
    }

    /// <summary>
    /// Изменение по устаревшей версии отклоняется: иначе второе рабочее место
    /// молча затёрло бы первое.
    /// </summary>
    [Fact]
    public async Task Сохранение_По_Устаревшей_Версии_Отклоняется()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиентA = await стенд.СоздатьКлиентаAsync("ПК A");
        using var клиентB = await стенд.СоздатьКлиентаAsync("ПК B");

        var созданный = await клиентA
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(
                    SharedServiceIntegrationTests.Контрагент("ООО «Версии»"), 0));

        var перваяПравка = SharedServiceIntegrationTests.Контрагент("ООО «Версии»") with
        {
            Id = созданный.Id,
            Phone = "+7 000 000-00-55",
        };

        var послеA = await клиентA
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(перваяПравка, созданный.Version));

        var устаревшаяПравка = SharedServiceIntegrationTests.Контрагент("ООО «Версии»") with
        {
            Id = созданный.Id,
            Phone = "+7 000 000-00-66",
        };

        var ошибка = await Assert.ThrowsAsync<SharedTestCommandException>(() =>
            клиентB.ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(устаревшаяПравка, созданный.Version)));

        Assert.Equal(SharedErrorCodes.Conflict, ошибка.Код);

        // Правка A не потеряна.
        using var ответ = await клиентA.Клиент.GetAsync(
            $"/api/v1/queries/counterparties/{созданный.Id}");

        ответ.EnsureSuccessStatusCode();

        var итог = JsonSerializer.Deserialize<CounterpartyDto>(
            await ответ.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Equal("+7 000 000-00-55", итог.Phone);
        Assert.Equal(послеA.Version, итог.Version);
    }
}
