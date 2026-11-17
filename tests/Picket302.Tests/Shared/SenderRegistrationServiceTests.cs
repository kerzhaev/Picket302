using Picket302.App.Shared;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Services;
using Picket302.Domain.ValueObjects;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Регистрация отправителя: полная карточка, отказ и частичное сохранение.
/// </summary>
/// <remarks>
/// Проверяется именно тот сценарий, который раньше терял реквизиты: карточка
/// должна уходить в хранилище целиком, текущий отправитель — меняться только
/// при полном успехе, а частичный результат не должен подталкивать оператора
/// к созданию дубля.
/// </remarks>
public sealed class SenderRegistrationServiceTests
{
    [Fact]
    public async Task Полная_карточка_сохраняется_и_отправитель_назначается()
    {
        var карточка = Карточка("ООО «Полный отправитель»");
        var хранилище = new ПоддельноеХранилище { Идентификатор = 42 };
        var отправители = new ПоддельныеОтправители();

        var служба = new SenderRegistrationService(хранилище, new CurrentSenderContext(отправители));

        var итог = await служба.СоздатьAsync(
            карточка,
            new[] { ("Custom_Код", "A-1"), ("Custom_Отдел", "Продажи") });

        Assert.Equal(SenderRegistrationStatus.Создан, итог.Статус);
        Assert.Equal(42, итог.Идентификатор);
        Assert.Empty(итог.НесохранённыеПоля);

        // Карточка ушла в хранилище целиком и с признаком отправителя.
        Assert.NotNull(хранилище.Сохранённая);
        Assert.True(хранилище.Сохранённая!.IsSender);
        Assert.Equal("ООО «Полный отправитель»", хранилище.Сохранённая.Name);
        Assert.Equal("Полный отправитель", хранилище.Сохранённая.ShortName);
        Assert.Equal("+7 000 000-00-33", хранилище.Сохранённая.Phone);
        Assert.Equal("sender@example.invalid", хранилище.Сохранённая.Email);
        Assert.Equal("Тестовая карточка", хранилище.Сохранённая.SpecialNotes);
        Assert.Equal("190000", хранилище.Сохранённая.PostalAddress?.Zip);

        // Пользовательские поля записаны оба.
        Assert.Equal(2, хранилище.ЗаписанныеПоля.Count);

        // Текущий отправитель назначен ровно один раз.
        Assert.Equal(new long?[] { 42 }, отправители.Записи);
    }

    [Fact]
    public async Task При_отказе_сохранения_отправитель_не_переключается()
    {
        var карточка = Карточка("ООО «Отказ»");
        var хранилище = new ПоддельноеХранилище { ОшибкаСохранения = "Общая база недоступна." };
        var отправители = new ПоддельныеОтправители();

        var служба = new SenderRegistrationService(хранилище, new CurrentSenderContext(отправители));

        var итог = await служба.СоздатьAsync(карточка, Array.Empty<(string, string)>());

        Assert.Equal(SenderRegistrationStatus.НеСоздан, итог.Статус);
        Assert.Equal(0, итог.Идентификатор);
        Assert.Equal("Общая база недоступна.", итог.Ошибка);
        Assert.Empty(отправители.Записи);
        Assert.Empty(хранилище.ЗаписанныеПоля);
    }

    [Fact]
    public async Task Частичное_сохранение_называет_поля_и_не_переключает_отправителя()
    {
        var карточка = Карточка("ООО «Частично»");
        var хранилище = new ПоддельноеХранилище
        {
            Идентификатор = 77,
            ПолеСОшибкой = "Custom_Отдел",
        };
        var отправители = new ПоддельныеОтправители();

        var служба = new SenderRegistrationService(хранилище, new CurrentSenderContext(отправители));

        var итог = await служба.СоздатьAsync(
            карточка,
            new[] { ("Custom_Код", "A-1"), ("Custom_Отдел", "Продажи") });

        Assert.Equal(SenderRegistrationStatus.СозданБезДополнительныхПолей, итог.Статус);
        Assert.Equal(77, итог.Идентификатор);
        Assert.Contains("Custom_Отдел", итог.НесохранённыеПоля);

        // Карточка существует, но текущим отправителем не стала.
        Assert.NotNull(хранилище.Сохранённая);
        Assert.Empty(отправители.Записи);
    }

    [Fact]
    public async Task Повтор_после_частичного_сохранения_обновляет_ту_же_запись()
    {
        var карточка = Карточка("ООО «Повтор»");
        var хранилище = new ПоддельноеХранилище
        {
            Идентификатор = 91,
            ПолеСОшибкой = "Custom_Отдел",
        };
        var отправители = new ПоддельныеОтправители();

        var служба = new SenderRegistrationService(хранилище, new CurrentSenderContext(отправители));

        var первый = await служба.СоздатьAsync(
            карточка,
            new[] { ("Custom_Код", "A-1"), ("Custom_Отдел", "Продажи") });

        Assert.Equal(SenderRegistrationStatus.СозданБезДополнительныхПолей, первый.Статус);

        // Оператор открывает созданную карточку и сохраняет повторно: шлюз
        // получает идентификатор, то есть обновление, а не создание.
        хранилище.ПолеСОшибкой = null;

        var карточкаПовтора = Карточка("ООО «Повтор»");
        карточкаПовтора.Id = первый.Идентификатор;
        карточкаПовтора.Version = 1;

        var второй = await служба.СоздатьAsync(
            карточкаПовтора,
            new[] { ("Custom_Код", "A-1"), ("Custom_Отдел", "Продажи") });

        Assert.Equal(SenderRegistrationStatus.Создан, второй.Статус);
        Assert.Equal(первый.Идентификатор, второй.Идентификатор);
        Assert.Equal(2, хранилище.ВызововСохранения);
        Assert.Equal(new long?[] { 91 }, отправители.Записи);
    }

    private static Counterparty Карточка(string имя)
    {
        var карточка = new Counterparty
        {
            Name = имя,
            ShortName = "Полный отправитель",
            Type = CounterpartyType.LegalEntity,
            Inn = "7707083893",
            Kpp = "770701001",
            Bik = "044525225",
            Rs = "40702810400000000001",
            BankName = "Тестовый банк",
            Phone = "+7 000 000-00-33",
            Email = "sender@example.invalid",
            SpecialNotes = "Тестовая карточка",
        };

        карточка.PostalAddress = Address.Create(
            "190000",
            ["г. Санкт-Петербург", "ул. Проверочная, д. 7", string.Empty, string.Empty, string.Empty, string.Empty]).Value;
        карточка.AddressMainType = AddressMainType.Postal;

        return карточка;
    }

    /// <summary>Поддельное хранилище: считает вызовы и умеет отказывать точечно.</summary>
    private sealed class ПоддельноеХранилище : ISenderPersistence
    {
        public Counterparty? Сохранённая { get; private set; }

        public List<(string Имя, string Значение)> ЗаписанныеПоля { get; } = [];

        public long Идентификатор { get; set; } = 1;

        public string? ОшибкаСохранения { get; set; }

        public string? ПолеСОшибкой { get; set; }

        public int ВызововСохранения { get; private set; }

        public Task<Result<Counterparty>> СохранитьКарточкуAsync(
            Counterparty карточка,
            CancellationToken токен = default)
        {
            ВызововСохранения++;

            if (ОшибкаСохранения is not null)
            {
                return Task.FromResult(Result<Counterparty>.Failure(ОшибкаСохранения));
            }

            Сохранённая = карточка;
            карточка.Id = карточка.Id == 0 ? Идентификатор : карточка.Id;

            return Task.FromResult(Result<Counterparty>.Success(карточка));
        }

        public Task<Result<CounterpartyField>> ЗаписатьПолеAsync(
            long идентификатор,
            string имя,
            string значение,
            CancellationToken токен = default)
        {
            if (имя == ПолеСОшибкой)
            {
                return Task.FromResult(Result<CounterpartyField>.Failure("Поле отклонено службой."));
            }

            ЗаписанныеПоля.Add((имя, значение));

            return Task.FromResult(Result<CounterpartyField>.Success(
                new CounterpartyField { CounterpartyId = идентификатор, FieldName = имя, FieldValue = значение }));
        }
    }

    /// <summary>Поддельное хранилище текущего отправителя.</summary>
    private sealed class ПоддельныеОтправители : ICurrentSenderStore
    {
        public List<long?> Записи { get; } = [];

        public long? Read() => Записи.Count == 0 ? null : Записи[^1];

        public void Write(long? senderId) => Записи.Add(senderId);
    }
}
