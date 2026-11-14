using System.Text.Json;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки формата сообщений протокола: сериализация, обратный разбор и
/// регистрация команд.
/// </summary>
/// <remarks>
/// Формат сообщений — часть контракта: клиент и служба обязаны понимать
/// одинаковые байты. Проверки фиксируют camelCase-имена, перечисления именами
/// и обратимость разбора.
/// </remarks>
public sealed class SharedWireFormatTests
{
    [Fact]
    public void Контрагент_Переживает_Разбор_Сообщения()
    {
        var dto = new CounterpartyDto(
            Id: 1,
            Type: CounterpartyType.LegalEntity,
            Name: "ООО «Ромашка»",
            ShortName: null,
            Inn: "7701234567",
            Kpp: null,
            Ogrn: null,
            Okfs: null,
            Okpo: null,
            Okved: null,
            Bik: null,
            Ks: null,
            Rs: null,
            Rkc: null,
            BankName: null,
            PassportSeries: null,
            PassportNumber: null,
            PassportIssuedBy: null,
            PassportIssuedDate: null,
            BirthDate: null,
            BirthPlace: null,
            Position: null,
            DirectorFio: null,
            Email: null,
            Phone: null,
            SpecialNotes: null,
            Source: null,
            Logo: null,
            Addresses: new AddressSetDto(
                new AddressDto("101000", new[] { "а", "б", "в", "г", "д", "е" }, null),
                null,
                null,
                AddressMainType.Postal),
            GroupId: null,
            IsVip: false,
            IsSender: false,
            IsActive: true,
            IsDeleted: false,
            CreatedAt: new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt: new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc),
            Version: 3);

        var текст = JsonSerializer.Serialize(dto, SharedJson.Options);
        var разобранный = JsonSerializer.Deserialize<CounterpartyDto>(текст, SharedJson.Options);

        Assert.NotNull(разобранный);

        // Повторная сериализация разобранного значения должна дать тот же
        // текст: ни один реквизит не потерян и не искажён при разборе.
        Assert.Equal(текст, JsonSerializer.Serialize(разобранный, SharedJson.Options));
        Assert.Equal(dto.Id, разобранный!.Id);
        Assert.Equal(dto.Name, разобранный.Name);
        Assert.Equal(dto.Inn, разобранный.Inn);
        Assert.Equal(dto.Type, разобранный.Type);
        Assert.Equal("101000", разобранный.Addresses.Legal!.Zip);
        Assert.Equal(AddressMainType.Postal, разобранный.Addresses.MainType);
        Assert.Equal(3, разобранный.Version);
    }

    [Fact]
    public void Имена_Свойств_В_Сообщении_Регистр_Камбэка()
    {
        var dto = new VersionedResponse(5, 2);

        var текст = JsonSerializer.Serialize(dto, SharedJson.Options);

        Assert.Contains("\"id\":5", текст);
        Assert.Contains("\"version\":2", текст);
    }

    [Fact]
    public void Перечисления_Передаются_Именами()
    {
        var запрос = new TransitionMailStatusRequest(1, MailStatus.Dispatched, null, 0);

        var текст = JsonSerializer.Serialize(запрос, SharedJson.Options);

        Assert.Contains("\"Dispatched\"", текст);
        Assert.DoesNotContain("\"status\":3", текст);
    }

    [Fact]
    public void Пустые_Значения_Не_Передаются()
    {
        var dto = new VersionedResponse(1, 0);

        var текст = JsonSerializer.Serialize(dto, SharedJson.Options);

        Assert.DoesNotContain("null", текст);
    }

    [Fact]
    public void Страница_Данных_Переживает_Разбор_Сообщения()
    {
        var страница = new PageDto<GroupDto>(
            new[]
            {
                new GroupDto(1, "Поставщики", null, true, false, null, 0, 1),
                new GroupDto(2, "Получатели", null, false, false, null, 1, 1),
            },
            Offset: 0,
            Limit: 100,
            TotalCount: 2);

        var текст = JsonSerializer.Serialize(страница, SharedJson.Options);
        var разобранный = JsonSerializer.Deserialize<PageDto<GroupDto>>(текст, SharedJson.Options);

        Assert.NotNull(разобранный);
        Assert.Equal(2, разобранный!.Items.Count);
        Assert.Equal(2, разобранный.TotalCount);
        Assert.Equal("Поставщики", разобранный.Items[0].Name);
        Assert.Equal(1, разобранный.Items[0].Version);
    }

    [Fact]
    public void Фильтр_Контрагентов_Переживает_Разбор_Сообщения()
    {
        var фильтр = new CounterpartyFilterDto(
            NameFragment: "Ромашка",
            Criteria: new FilterCriteriaDto(
                new[] { new FilterConditionDto("inn", FilterOperator.Equals, "7701234567") },
                SortColumn: "name",
                SortDescending: true,
                OnlyFavorites: true,
                GroupId: 3));

        var текст = JsonSerializer.Serialize(фильтр, SharedJson.Options);
        var разобранный = JsonSerializer.Deserialize<CounterpartyFilterDto>(текст, SharedJson.Options);

        Assert.NotNull(разобранный);
        Assert.Equal("Ромашка", разобранный!.NameFragment);
        Assert.Single(разобранный.Criteria!.Conditions);
        Assert.Equal(FilterOperator.Equals, разобранный.Criteria.Conditions[0].Operator);
        Assert.True(разобранный.Criteria.OnlyFavorites);
    }

    [Fact]
    public void План_Рассылки_Переживает_Разбор_Сообщения()
    {
        var запрос = new GenerateDistributionRequest(new DistributionPlan
        {
            Name = "Ноябрь",
            Kind = DistributionKind.OneToMany,
            SenderId = 2,
            CounterpartyIds = new long[] { 3, 4, 5 },
            Note = "заметка",
            EnvelopeType = "C6",
            CopyCount = 2,
        });

        var текст = JsonSerializer.Serialize(запрос, SharedJson.Options);
        var разобранный = JsonSerializer.Deserialize<GenerateDistributionRequest>(текст, SharedJson.Options);

        Assert.NotNull(разобранный);
        Assert.Equal("Ноябрь", разобранный!.Plan.Name);
        Assert.Equal(DistributionKind.OneToMany, разобранный.Plan.Kind);
        Assert.Equal(3, разобранный.Plan.CounterpartyIds.Count);
        Assert.Equal(2, разобранный.Plan.CopyCount);
    }
}

/// <summary>
/// Проверки реестра команд протокола.
/// </summary>
public sealed class SharedCommandRegistryTests
{
    [Fact]
    public void Повторная_Регистрация_Команды_Отклоняется()
    {
        var реестр = new SharedCommandRegistry();

        реестр.Register(new ПробнаяКоманда());

        var ошибка = Assert.Throws<ArgumentException>(() => реестр.Register(new ПробнаяКоманда()));

        Assert.Contains("уже зарегистрирована", ошибка.Message);
    }

    [Fact]
    public void Описание_Команды_Содержит_Типы_Запроса_И_Ответа()
    {
        var реестр = new SharedCommandRegistry();
        реестр.Register(new ПробнаяКоманда());

        Assert.True(реестр.TryGet(ПробнаяКоманда.Имя, out var описание));
        Assert.Equal(typeof(ПробнаяКоманда.Запрос), описание.RequestType);
        Assert.Equal(typeof(ПробнаяКоманда.Ответ), описание.ResponseType);
        Assert.False(описание.IsSafeToRetry);
    }

    [Fact]
    public void Неизвестная_Команда_Не_Находится()
    {
        var реестр = new SharedCommandRegistry();

        Assert.False(реестр.TryGet("нет-такой-команды", out _));
    }

    [Fact]
    public async Task Команда_Выполняется_Через_Описание()
    {
        var реестр = new SharedCommandRegistry();
        реестр.Register(new ПробнаяКоманда());

        реестр.TryGet(ПробнаяКоманда.Имя, out var описание);

        var результат = await описание.InvokeAsync(
            new SharedCommandContext("оп-1", "устройство-1", "ПК оператора"),
            new ПробнаяКоманда.Запрос("значение"),
            CancellationToken.None);

        Assert.Equal("значение", Assert.IsType<ПробнаяКоманда.Ответ>(результат).Значение);
    }

    [Fact]
    public void Безопасная_Команда_Помечается_Для_Повтора()
    {
        var реестр = new SharedCommandRegistry();
        реестр.Register(new ПробнаяБезопаснаяКоманда());

        реестр.TryGet(ПробнаяБезопаснаяКоманда.Имя, out var описание);

        Assert.True(описание.IsSafeToRetry);
    }

    private sealed class ПробнаяКоманда : SharedCommand<ПробнаяКоманда.Запрос, ПробнаяКоманда.Ответ>
    {
        public const string Имя = "tests.probe";

        public override string Name => Имя;

        public override Task<Ответ> ExecuteAsync(
            SharedCommandContext context,
            Запрос request,
            CancellationToken cancellationToken)
            => Task.FromResult(new Ответ(request.Значение));

        public sealed record Запрос(string Значение);

        public sealed record Ответ(string Значение);
    }

    private sealed class ПробнаяБезопаснаяКоманда
        : SharedCommand<ПробнаяБезопаснаяКоманда.Запрос, ПробнаяБезопаснаяКоманда.Ответ>
    {
        public const string Имя = "tests.safe";

        public override string Name => Имя;

        public override bool IsSafeToRetry => true;

        public override Task<Ответ> ExecuteAsync(
            SharedCommandContext context,
            Запрос request,
            CancellationToken cancellationToken)
            => Task.FromResult(new Ответ());

        public sealed record Запрос;

        public sealed record Ответ;
    }
}
