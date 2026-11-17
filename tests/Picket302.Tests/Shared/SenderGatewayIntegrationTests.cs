using Picket302.App.Shared;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Полный клиентский путь сохранения отправителя: RemoteDataGateway →
/// SharedTransport (HTTPS с закреплением отпечатка) → служба → база.
/// </summary>
/// <remarks>
/// Проверяются не отдельные строки кода, а факт: карточка, собранная формой,
/// доходит до постоянного хранилища целиком и читается обратно без потерь.
/// </remarks>
public sealed class SenderGatewayIntegrationTests
{
    [Fact]
    public async Task Полная_карточка_проходит_через_клиентский_шлюз_без_потерь()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК QA шлюз");

        var реквизиты = new SharedClientSettings(
            "127.0.0.1",
            стенд.Порт,
            клиент.Устройство,
            клиент.Сервер,
            клиент.ОтпечатокСервера);

        using var транспорт = new SharedTransport(реквизиты, клиент.Секрет);

        var файлНезавершённых = Path.Combine(стенд.КаталогДанных, "qa-pending-operations.json");

        var шлюз = new RemoteDataGateway(
            транспорт,
            new PendingOperationsStore(файлНезавершённых));

        var карточка = Карточка();

        var сохранение = await шлюз.СохранитьКонтрагентаAsync(карточка);

        Assert.True(сохранение.IsSuccess, сохранение.Error);
        Assert.True(сохранение.Value.Id > 0);

        var прочитанная = await шлюз.ПолучитьКонтрагентаAsync(сохранение.Value.Id);

        Assert.NotNull(прочитанная);

        // Идентичность и признак отправителя.
        Assert.Equal(сохранение.Value.Id, прочитанная!.Id);
        Assert.True(прочитанная.IsSender);

        // Реквизиты.
        Assert.Equal("P302-QA-GW ООО «Полный отправитель через шлюз»", прочитанная.Name);
        Assert.Equal("Полный отправитель через шлюз", прочитанная.ShortName);
        Assert.Equal(CounterpartyType.LegalEntity, прочитанная.Type);
        Assert.Equal("7707083893", прочитанная.Inn);
        Assert.Equal("770701001", прочитанная.Kpp);
        Assert.Equal("1027700132195", прочитанная.Ogrn);
        Assert.Equal("16", прочитанная.Okfs);
        Assert.Equal("00000001", прочитанная.Okpo);
        Assert.Equal("62.01", прочитанная.Okved);

        // Банк.
        Assert.Equal("044525225", прочитанная.Bik);
        Assert.Equal("30101810400000000225", прочитанная.Ks);
        Assert.Equal("40702810400000000001", прочитанная.Rs);
        Assert.Equal("ГУ Банка России", прочитанная.Rkc);
        Assert.Equal("Тестовый банк", прочитанная.BankName);

        // Контакты и примечание.
        Assert.Equal("+7 000 000-00-33", прочитанная.Phone);
        Assert.Equal("sender-gw@example.invalid", прочитанная.Email);
        Assert.Equal("Полная карточка через клиентский шлюз", прочитанная.SpecialNotes);
        Assert.Equal("Директор", прочитанная.Position);
        Assert.Equal("Иван Иванович", прочитанная.DirectorFio);

        // Три адресных среза с разными значениями: перепутать их нельзя.
        Assert.NotNull(прочитанная.LegalAddress);
        Assert.Equal("190000", прочитанная.LegalAddress!.Zip);
        Assert.Equal("Невский проспект, д. 1", прочитанная.LegalAddress.Lines[1]);

        Assert.NotNull(прочитанная.ActualAddress);
        Assert.Equal("191000", прочитанная.ActualAddress!.Zip);
        Assert.Equal("ул. Фактическая, д. 2", прочитанная.ActualAddress.Lines[1]);

        Assert.NotNull(прочитанная.PostalAddress);
        Assert.Equal("192000", прочитанная.PostalAddress!.Zip);
        Assert.Equal("ул. Почтовая, д. 3", прочитанная.PostalAddress.Lines[1]);

        Assert.Equal(AddressMainType.Postal, прочитанная.AddressMainType);

        // Признаки и служебные поля.
        Assert.True(прочитанная.IsVip);
        Assert.True(прочитанная.IsActive);
        Assert.False(прочитанная.IsDeleted);
        Assert.Equal("QA-шлюз", прочитанная.Source);

        // Дата создания назначена службой, а не клиентом.
        Assert.True(
            Math.Abs((прочитанная.CreatedAt - DateTime.UtcNow).TotalMinutes) < 5,
            $"Дата создания должна быть текущей, получена {прочитанная.CreatedAt:O}.");
    }

    [Fact]
    public async Task Пользовательские_поля_сохраняются_и_читаются_через_шлюз()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК QA поля");

        var реквизиты = new SharedClientSettings(
            "127.0.0.1",
            стенд.Порт,
            клиент.Устройство,
            клиент.Сервер,
            клиент.ОтпечатокСервера);

        using var транспорт = new SharedTransport(реквизиты, клиент.Секрет);

        var шлюз = new RemoteDataGateway(
            транспорт,
            new PendingOperationsStore(Path.Combine(стенд.КаталогДанных, "qa-pending-fields.json")));

        var карточка = Карточка();
        карточка.Name = "P302-QA-GW ООО «Поля через шлюз»";

        var сохранение = await шлюз.СохранитьКонтрагентаAsync(карточка);
        Assert.True(сохранение.IsSuccess, сохранение.Error);

        var запись = await шлюз.ЗаписатьПолеКонтрагентаAsync(
            сохранение.Value.Id,
            "Custom_Код",
            "A-42");

        Assert.True(запись.IsSuccess, запись.Error);

        var поля = await шлюз.ПолучитьПоляКонтрагентаAsync(сохранение.Value.Id);

        Assert.Contains(поля, поле => поле.FieldName == "Custom_Код" && поле.FieldValue == "A-42");
    }

    private static Counterparty Карточка()
    {
        var карточка = new Counterparty
        {
            Name = "P302-QA-GW ООО «Полный отправитель через шлюз»",
            ShortName = "Полный отправитель через шлюз",
            Type = CounterpartyType.LegalEntity,
            Inn = "7707083893",
            Kpp = "770701001",
            Ogrn = "1027700132195",
            Okfs = "16",
            Okpo = "00000001",
            Okved = "62.01",
            Bik = "044525225",
            Ks = "30101810400000000225",
            Rs = "40702810400000000001",
            Rkc = "ГУ Банка России",
            BankName = "Тестовый банк",
            Phone = "+7 000 000-00-33",
            Email = "sender-gw@example.invalid",
            SpecialNotes = "Полная карточка через клиентский шлюз",
            Position = "Директор",
            DirectorFio = "Иван Иванович",
            Source = "QA-шлюз",
            IsSender = true,
            IsVip = true,
            IsActive = true,
        };

        карточка.LegalAddress = Address.Create(
            "190000",
            ["ООО «Полный отправитель»", "Невский проспект, д. 1", string.Empty, string.Empty, "Санкт-Петербург", "Ленинградская обл."]).Value;

        карточка.ActualAddress = Address.Create(
            "191000",
            ["ООО «Полный отправитель»", "ул. Фактическая, д. 2", string.Empty, string.Empty, "Санкт-Петербург", "Ленинградская обл."]).Value;

        карточка.PostalAddress = Address.Create(
            "192000",
            ["ООО «Полный отправитель»", "ул. Почтовая, д. 3", string.Empty, string.Empty, "Санкт-Петербург", "Ленинградская обл."]).Value;

        карточка.AddressMainType = AddressMainType.Postal;

        return карточка;
    }
}
