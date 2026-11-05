using Picket302.App;
using Picket302.App.UI.Forms;
using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Templates;
using Picket302.Tests.Infrastructure;
using Xunit;
using static Picket302.Application.Services.MailFactory;

namespace Picket302.Tests.App;

/// <summary>
/// Интеграция клипарта как логотипа получателя (ТЗ §8.10, функция 158):
/// выбор элемента каталога, сохранение PNG контрагенту настоящим
/// репозиторием во временную базу, повторное чтение, подстановка в
/// PrintDataSource и удаление логотипа. Внешние файлы и сеть не используются.
/// </summary>
public sealed class ClipartLogoIntegrationTests
{
    private static Counterparty Контрагент(string имя) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = "7707083893",
        IsSender = false,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "101000",
            [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
    };

    [Fact]
    public async Task Клипарт_НазначаетсяЛоготипомПолучателяИДоходитДоИсточникаПечати()
    {
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);

        var отправитель = await состав.Контрагенты.AddAsync(Контрагент("ООО «Отправитель»"));
        var получатель = await состав.Контрагенты.AddAsync(Контрагент("ООО «Получатель»"));

        // 1–2. Выбор элемента каталога через диалог.
        var каталог = new ClipartLibrary();

        Assert.True(каталог.TryGet("конверт", out var png));

        using var диалог = new FormClipartPicker(каталог);

        Assert.True(диалог.ВыбратьЭлемент("конверт"));
        Assert.NotNull(диалог.ВыбранныйPng);

        // 3. Назначение PNG выбранному контрагенту и сохранение настоящим
        // репозиторием во временную базу.
        var карточка = await состав.Контрагенты.GetByIdAsync(получатель);

        Assert.NotNull(карточка);
        Assert.Null(карточка!.Logo);

        карточка.Logo = диалог.ВыбранныйPng;

        await состав.Контрагенты.UpdateAsync(карточка);

        // 4–5. Повторное чтение: байты PNG совпадают с выбранным элементом.
        var перечитанный = await состав.Контрагенты.GetByIdAsync(получатель);

        Assert.NotNull(перечитанный);
        Assert.NotNull(перечитанный!.Logo);
        Assert.Equal(png, перечитанный.Logo);
        Assert.True(
            перечитанный.Logo.Length >= 4
                && перечитанный.Logo[0] == 0x89
                && перечитанный.Logo[1] == 0x50
                && перечитанный.Logo[2] == 0x4E
                && перечитанный.Logo[3] == 0x47,
            "Сохранённый логотип обязан быть PNG.");

        // 6–7. Печатный конвейер: PrintDataSource передаёт сохранённый PNG
        // в LogoBytes элемента печати.
        var письмо = CreateOutcoming(
            получатель,
            отправитель,
            "C6",
            перечитанный.GetAddress(перечитанный.AddressMainType)!);

        письмо.DistributionId = null;
        var идентификаторПисьма = await состав.Письма.AddAsync(письмо);

        var источник = new PrintDataSource(состав.Письма, состав.Контрагенты);
        var элементы = await источник.GetItemsAsync([идентификаторПисьма]);

        var элемент = Assert.Single(элементы);

        Assert.NotNull(элемент.LogoBytes);
        Assert.Equal(png, элемент.LogoBytes);

        // 8–9. Удаление логотипа: сохраняется и повторно читается пустым.
        перечитанный.Logo = null;

        await состав.Контрагенты.UpdateAsync(перечитанный);

        var безЛоготипа = await состав.Контрагенты.GetByIdAsync(получатель);

        Assert.Null(безЛоготипа!.Logo);

        var послеУдаления = await источник.GetItemsAsync([идентификаторПисьма]);

        Assert.Null(Assert.Single(послеУдаления).LogoBytes);
    }
}
