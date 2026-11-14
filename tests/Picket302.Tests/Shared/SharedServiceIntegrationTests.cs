using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Picket302.Domain.Entities;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки совместного режима на поднятой службе: два и пять клиентов,
/// конфликты, идемпотентность и отказ доступа.
/// </summary>
/// <remarks>
/// <para>
/// Проверки идут по настоящему сетевому протоколу: клиент обращается к
/// поднятой службе через HTTP API, данные лежат в отдельной базе службы.
/// Локальная база клиента в проверке не участвует — общий режим не имеет
/// локальной копии общих данных.
/// </para>
/// <para>
/// Стенд изолирован: временный каталог, отдельный файл базы, свободный порт.
/// </para>
/// </remarks>
public sealed class SharedServiceIntegrationTests
{
    [Fact]
    public async Task Служба_Отвечает_На_Проверку_Состояния()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = new HttpClient { BaseAddress = new Uri(стенд.БазовыйАдрес) };

        using var ответ = await клиент.GetAsync(SharedProtocol.HealthRoute);

        Assert.True(ответ.IsSuccessStatusCode);

        var состояние = JsonSerializer.Deserialize<HealthResponse>(
            await ответ.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Equal("готова", состояние.Status);
        Assert.Equal(SharedProtocol.Version, состояние.ProtocolVersion);
        Assert.NotEmpty(состояние.ServerId);
    }

    [Fact]
    public async Task Клиент_A_Создаёт_Контрагента_Клиент_B_Видит_Его()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиентA = await стенд.СоздатьКлиентаAsync("ПК оператора 1");
        using var клиентB = await стенд.СоздатьКлиентаAsync("ПК оператора 2");

        var созданный = await клиентA
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(Контрагент("ООО «Ромашка»"), 0))
            ;

        Assert.True(созданный.Id > 0);
        Assert.True(созданный.Version >= 1);

        using var ответ = await клиентB.Клиент.GetAsync("/api/v1/queries/counterparties");
        ответ.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await ответ.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Single(страница.Items);
        Assert.Equal("ООО «Ромашка»", страница.Items[0].Name);
        Assert.Equal(созданный.Id, страница.Items[0].Id);
    }

    [Fact]
    public async Task Одновременное_Изменение_Даёт_Явный_Конфликт_И_Не_Теряет_Данные()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиентA = await стенд.СоздатьКлиентаAsync("ПК оператора 1");
        using var клиентB = await стенд.СоздатьКлиентаAsync("ПК оператора 2");

        var созданный = await клиентA
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(Контрагент("ООО «Ромашка»"), 0))
            ;

        // Клиент B читает запись и получает её версию.
        using var ответB = await клиентB.Клиент.GetAsync(
            $"/api/v1/queries/counterparties/{созданный.Id}");

        ответB.EnsureSuccessStatusCode();

        var прочитанныйB = JsonSerializer.Deserialize<CounterpartyDto>(
            await ответB.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Equal(созданный.Version, прочитанныйB.Version);

        // Клиент A меняет запись: версия увеличивается.
        var изменённыйA = await клиентA
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(
                    Контрагент("ООО «Ромашка» (новое наименование)", созданный.Id),
                    прочитанныйB.Version))
            ;

        Assert.True(изменённыйA.Version > прочитанныйB.Version);

        // Клиент B меняет ту же запись по устаревшей версии — ожидается отказ.
        var ошибка = await Assert.ThrowsAsync<SharedTestCommandException>(() =>
            клиентB.ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(
                    Контрагент("ООО «Ромашка» (правка B)", созданный.Id),
                    прочитанныйB.Version)));

        Assert.Equal(SharedErrorCodes.Conflict, ошибка.Код);

        // Изменение A не потеряно: запись содержит именно его наименование.
        using var ответA = await клиентA.Клиент.GetAsync(
            $"/api/v1/queries/counterparties/{созданный.Id}");

        ответA.EnsureSuccessStatusCode();

        var итог = JsonSerializer.Deserialize<CounterpartyDto>(
            await ответA.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Equal("ООО «Ромашка» (новое наименование)", итог.Name);
    }

    [Fact]
    public async Task Повтор_Команды_С_Тем_Же_Идентификатором_Не_Создаёт_Дубль()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var operationId = Guid.NewGuid().ToString("D");

        // Повтор отправляет тот же запрос: тело сообщения идентично первому,
        // иначе служба справедливо считает это другой операцией.
        var запрос = new SaveCounterpartyRequest(Контрагент("ООО «Повтор»"), 0);

        var первый = await клиент
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                запрос,
                operationId)
            ;

        var второй = await клиент
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                запрос,
                operationId)
            ;

        Assert.Equal(первый.Id, второй.Id);
        Assert.Equal(первый.Version, второй.Version);

        using var ответ = await клиент.Клиент.GetAsync("/api/v1/queries/counterparties");
        ответ.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await ответ.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Single(страница.Items);
    }

    [Fact]
    public async Task Пять_Клиентов_Одновременно_Создают_Отправления_С_Уникальными_Номерами()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var первый = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        // Общий отправитель для всех клиентов.
        var отправитель = await первый
            .ВыполнитьИРазобратьAsync<CreateAgentRequest, CounterpartyResponse>(
                "counterparties.create-agent",
                new CreateAgentRequest("ООО «Отправитель»"))
            ;

        var клиенты = new List<SharedTestClient>();

        try
        {
            for (var i = 0; i < 5; i++)
            {
                клиенты.Add(await стенд.СоздатьКлиентаAsync($"ПК оператора {i + 2}"));
            }

            var получатели = new List<SaveCounterpartyResponse>();

            foreach (var клиент in клиенты)
            {
                получатели.Add(await клиент
                    .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                        "counterparties.save",
                        new SaveCounterpartyRequest(Контрагент("ООО «Получатель»"), 0)));
            }

            // Все пять клиентов одновременно выдают номера своим отправлениям.
            var задачи = new List<Task<string>>();

            for (var i = 0; i < клиенты.Count; i++)
            {
                var клиент = клиенты[i];
                var получатель = получатели[i];

                задачи.Add(СоздатьОтправлениеИВыдатьНомерAsync(
                    клиент,
                    получатель.Id,
                    отправитель.Id));
            }

            var номера = await Task.WhenAll(задачи);

            Assert.Equal(5, номера.Distinct(StringComparer.Ordinal).Count());
            Assert.All(номера, номер => Assert.False(string.IsNullOrWhiteSpace(номер)));
        }
        finally
        {
            foreach (var клиент in клиенты)
            {
                клиент.Dispose();
            }
        }
    }

    [Fact]
    public async Task Отозванное_Устройство_Не_Читает_И_Не_Меняет_Данные()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        await клиент
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(Контрагент("ООО «Ромашка»"), 0))
            ;

        var админТокен = new Picket302.Service.Security.SharedAdminToken(стенд.КаталогДанных)
            .ПолучитьИлиСоздать();

        using var админский = new HttpClient { BaseAddress = new Uri(стенд.БазовыйАдрес) };
        админский.DefaultRequestHeaders.Add("X-Picket302-Admin-Token", админТокен);

        using var ответОтзыва = await админский.PostAsync(
            $"/api/v1/admin/devices/{клиент.Устройство}/revoke",
            content: null);

        ответОтзыва.EnsureSuccessStatusCode();

        // Чтение запрещено.
        using var ответЧтения = await клиент.Клиент.GetAsync("/api/v1/queries/counterparties");

        Assert.Equal(HttpStatusCode.Unauthorized, ответЧтения.StatusCode);

        // Запись запрещена.
        using var ответЗаписи = await клиент.ВыполнитьAsync(
            "counterparties.save",
            new SaveCounterpartyRequest(Контрагент("ООО «Новая»"), 0));

        Assert.Equal(HttpStatusCode.Unauthorized, ответЗаписи.StatusCode);
    }

    [Fact]
    public async Task Неверная_Версия_Протокола_Отклоняется()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        клиент.Клиент.DefaultRequestHeaders.Remove(SharedProtocol.ProtocolVersionHeader);
        клиент.Клиент.DefaultRequestHeaders.Add(SharedProtocol.ProtocolVersionHeader, "99");

        using var ответ = await клиент.ВыполнитьAsync(
            "counterparties.save",
            new SaveCounterpartyRequest(Контрагент("ООО «Ромашка»"), 0));

        Assert.Equal((HttpStatusCode)426, ответ.StatusCode);
    }

    [Fact]
    public async Task Служба_Не_Создаёт_Запись_Без_Сопряжённого_Устройства()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var без_сопряжения = new HttpClient { BaseAddress = new Uri(стенд.БазовыйАдрес) };

        без_сопряжения.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "foreign-secret-value");
        без_сопряжения.DefaultRequestHeaders.Add(SharedProtocol.ProtocolVersionHeader, SharedProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var сообщение = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SharedProtocol.RoutePrefix}/commands/counterparties.save");

        сообщение.Headers.Add(SharedProtocol.OperationIdHeader, Guid.NewGuid().ToString("D"));
        сообщение.Content = new StringContent(
            JsonSerializer.Serialize(
                new SaveCounterpartyRequest(Контрагент("ООО «Ромашка»"), 0),
                SharedJson.Options),
            System.Text.Encoding.UTF8,
            "application/json");

        using var ответ = await без_сопряжения.SendAsync(сообщение);

        Assert.Equal(HttpStatusCode.Unauthorized, ответ.StatusCode);
    }

    /// <summary>
    /// Создаёт отправление и выдаёт ему регистрационный номер.
    /// </summary>
    /// <param name="клиент">Клиент.</param>
    /// <param name="получательId">Получатель.</param>
    /// <param name="отправительId">Отправитель.</param>
    /// <returns>Выданный номер.</returns>
    private static async Task<string> СоздатьОтправлениеИВыдатьНомерAsync(
        SharedTestClient клиент,
        long получательId,
        long отправительId)
    {
        var письмо = new MailDto(
            Id: 0,
            CounterpartyId: получательId,
            SenderId: отправительId,
            DistributionId: null,
            EnvelopeType: "C6",
            NoticeType: null,
            NotificationType: NotificationType.None,
            CopyCount: 1,
            IsVip: false,
            Note: null,
            DeclaredValue: 0m,
            Weight: 0m,
            Fee: 0m,
            Status: MailStatus.Draft,
            NeedNotification: false,
            NeedReturnAddress: true,
            MailCategory: MailCategory.Simple,
            DateCreated: DateTime.UtcNow,
            DatePrinted: null,
            DateDispatched: null,
            DateConfirmation: null,
            DateCanceled: null,
            IncomingNumber: null,
            OutgoingNumber: null,
            PostRegisterId: null,
            RussianPostId: null,
            RussianPostBatch: null,
            InventoryId: null,
            RecipientAddress: new AddressDto(
                "101000",
                new[] { "ООО «Получатель»", "ул. Тверская", "д. 1", string.Empty, "Москва", "Московская обл." },
                null),
            FolderId: null,
            Version: 0);

        var созданное = await клиент
            .ВыполнитьИРазобратьAsync<CreateMailRequest, MailDto>(
                "mails.create",
                new CreateMailRequest(письмо))
            ;

        var сНомером = await клиент
            .ВыполнитьИРазобратьAsync<AssignMailNumberRequest, MailDto>(
                "mails.assign-number",
                new AssignMailNumberRequest(созданное.Id, null, null, созданное.Version))
            ;

        return сНомером.OutgoingNumber ?? string.Empty;
    }

    /// <summary>
    /// Создаёт карточку контрагента с заполненным адресом.
    /// </summary>
    /// <param name="наименование">Наименование.</param>
    /// <returns>DTO контрагента.</returns>
    internal static CounterpartyDto Контрагент(string наименование, long идентификатор = 0) => new(
        Id: идентификатор,
        Type: CounterpartyType.LegalEntity,
        Name: наименование,
        ShortName: null,
        Inn: null,
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
            new AddressDto(
                "101000",
                new[] { наименование, "ул. Тверская", "д. 1", string.Empty, "Москва", "Московская обл." },
                null),
            null,
            null,
            AddressMainType.Postal),
        GroupId: null,
        IsVip: false,
        IsSender: false,
        IsActive: true,
        IsDeleted: false,
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: DateTime.UtcNow,
        Version: 0);
}
