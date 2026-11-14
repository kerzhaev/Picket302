using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Дополнительные проверки преобразования доменной модели в контракт.
/// </summary>
/// <remarks>
/// Проверки закрывают случаи, которые легко потерять при преобразовании:
/// независимо хранимые признаки, пустые коллекции и поля, не переносимые по
/// назначению.
/// </remarks>
public sealed class SharedMappingCoverageTests
{
    [Fact]
    public void Признак_Нужно_Уведомление_Не_Выводится_Из_Вида_Уведомления()
    {
        // Признак «нужно уведомление» и вид уведомления хранятся в базе
        // независимо друг от друга: преобразование не должно выводить одно из
        // другого.
        var письмо = new Mail
        {
            Id = 5,
            CounterpartyId = 1,
            SenderId = 2,
            CopyCount = 1,
            Status = MailStatus.Draft,
            NeedNotification = true,
            NeedReturnAddress = true,
            MailCategory = MailCategory.Simple,
            DateCreated = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc),
        };

        var dto = SharedMapping.КОтправлению(письмо, 1);
        var обратно = SharedMapping.ИзОтправления(dto);

        Assert.Equal(NotificationType.None, обратно.NotificationType);
        Assert.True(обратно.NeedNotification);
    }

    [Fact]
    public void Вид_Уведомления_Переносится_Как_Строковое_Обозначение()
    {
        var письмо = new Mail
        {
            Id = 5,
            CounterpartyId = 1,
            SenderId = 2,
            CopyCount = 1,
            Status = MailStatus.Draft,
            NeedReturnAddress = true,
            MailCategory = MailCategory.Simple,
            DateCreated = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc),
        };

        письмо.NoticeType = "119";

        var dto = SharedMapping.КОтправлению(письмо, 1);
        var обратно = SharedMapping.ИзОтправления(dto);

        Assert.Equal("119", dto.NoticeType);
        Assert.Equal(NotificationType.Form119, обратно.NotificationType);
        Assert.Equal("119", обратно.NoticeType);
    }

    [Fact]
    public void Пустой_Список_Строк_Адреса_Дополняется_Пустыми()
    {
        var dto = new AddressDto("101000", Array.Empty<string>(), null);

        var адрес = SharedMapping.ИзАдреса(dto);

        Assert.NotNull(адрес);
        Assert.Equal(Address.LineCount, адрес!.Lines.Count);
        Assert.All(адрес.Lines, строка => Assert.Equal(string.Empty, строка));
    }

    [Fact]
    public void Пустые_Коллекции_Не_Теряются_При_Преобразовании_Отправления()
    {
        var письмо = new Mail
        {
            Id = 5,
            CounterpartyId = 1,
            SenderId = 2,
            CopyCount = 1,
            Status = MailStatus.Draft,
            NeedReturnAddress = true,
            MailCategory = MailCategory.Simple,
            DateCreated = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc),
            RecipientAddress = null,
        };

        var dto = SharedMapping.КОтправлению(письмо, 1);

        Assert.Null(dto.RecipientAddress);
    }

    [Fact]
    public void Сессия_Печати_Преобразуется_В_Контрактную_Форму()
    {
        var сессия = new PrintJob(
            7,
            DateTime.UtcNow,
            3,
            "Принтер HP",
            "A4",
            210m,
            297m,
            "Tahoma 10",
            2,
            "selected",
            "SUCCESS",
            null,
            null);

        var dto = SharedMapping.КСессииПечати(сессия, "устройство-1");

        Assert.Equal("Принтер HP", dto.PrinterName);
        Assert.Equal("A4", dto.PaperSizeName);
        Assert.Equal(2, dto.Copies);
        Assert.Equal("SUCCESS", dto.Status);
        Assert.Equal("устройство-1", dto.DeviceId);
    }

    [Fact]
    public void Категория_Преобразуется_В_Контрактную_Форму()
    {
        var категория = new Picket302.Domain.Contracts.Category(
            4,
            "Поставщики",
            1,
            false,
            false,
            2,
            17);

        var dto = SharedCatalogMapping.ККатегории(категория, 3);

        Assert.Equal(4, dto.Id);
        Assert.Equal("Поставщики", dto.Name);
        Assert.Equal(17, dto.CounterpartyCount);
        Assert.Equal(3, dto.Version);
    }
}
