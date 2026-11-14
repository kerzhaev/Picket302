using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Picket302.Domain.Entities;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки граничных случаев совместного режима: одноразовость кода
/// сопряжения, предел попыток, гонка с одинаковым идентификатором операции,
/// предел размера запроса, исходы печати и конфликт версий рассылки.
/// </summary>
public sealed class SharedServiceBoundaryTests
{
    [Fact]
    public async Task Код_Сопряжения_Действует_Только_Один_Раз()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        var админТокен = new Picket302.Service.Security.SharedAdminToken(стенд.КаталогДанных)
            .ПолучитьИлиСоздать();

        using var админский = стенд.СоздатьHttpClient();
        админский.DefaultRequestHeaders.Add("X-Picket302-Admin-Token", админТокен);

        using var ответКода = await админский.PostAsync("/api/v1/admin/pairing-code", content: null);
        ответКода.EnsureSuccessStatusCode();

        var код = JsonSerializer.Deserialize<AdminPairingCodeResponse>(
            await ответКода.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        using var первый = стенд.СоздатьHttpClient();

        using var ответПервого = await первый.PostAsJsonAsync(
            SharedProtocol.PairRoute,
            new PairRequest(код.Code, "ПК оператора 1"),
            SharedJson.Options);

        Assert.True(ответПервого.IsSuccessStatusCode);

        // Тот же код повторно не принимается: код одноразовый.
        using var второй = стенд.СоздатьHttpClient();

        using var ответВторого = await второй.PostAsJsonAsync(
            SharedProtocol.PairRoute,
            new PairRequest(код.Code, "ПК оператора 2"),
            SharedJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, ответВторого.StatusCode);
    }

    [Fact]
    public async Task Число_Попыток_Сопряжения_Ограничено()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        var админТокен = new Picket302.Service.Security.SharedAdminToken(стенд.КаталогДанных)
            .ПолучитьИлиСоздать();

        using var админский = стенд.СоздатьHttpClient();
        админский.DefaultRequestHeaders.Add("X-Picket302-Admin-Token", админТокен);

        using var ответКода = await админский.PostAsync("/api/v1/admin/pairing-code", content: null);
        ответКода.EnsureSuccessStatusCode();

        var код = JsonSerializer.Deserialize<AdminPairingCodeResponse>(
            await ответКода.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        // Пять неверных попыток подряд: после них код блокируется, и даже
        // верный код больше не принимается.
        for (var попытка = 0; попытка < 5; попытка++)
        {
            using var неверный = стенд.СоздатьHttpClient();

            using var ответ = await неверный.PostAsJsonAsync(
                SharedProtocol.PairRoute,
                new PairRequest(НеверныйКод(код.Code), "ПК оператора"),
                SharedJson.Options);

            Assert.Equal(HttpStatusCode.Unauthorized, ответ.StatusCode);
        }

        using var верный = стенд.СоздатьHttpClient();

        using var ответВерного = await верный.PostAsJsonAsync(
            SharedProtocol.PairRoute,
            new PairRequest(код.Code, "ПК оператора"),
            SharedJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, ответВерного.StatusCode);
    }

    [Fact]
    public async Task Параллельные_Запросы_С_Одним_Идентификатором_Не_Создают_Два_Письма()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var operationId = Guid.NewGuid().ToString("D");
        var запрос = new SaveCounterpartyRequest(Контрагент("ООО «Гонка»"), 0);

        var первый = клиент.ВыполнитьAsync("counterparties.save", запрос, operationId);
        var второй = клиент.ВыполнитьAsync("counterparties.save", запрос, operationId);

        var ответы = await Task.WhenAll(первый, второй);

        var успешные = ответы
            .Where(ответ => ответ.IsSuccessStatusCode)
            .Select(ответ => ответ.Content.ReadAsStringAsync().GetAwaiter().GetResult())
            .ToArray();

        Assert.NotEmpty(успешные);

        // Запись в базе одна: дубля не возникло.
        using var список = await клиент.Клиент.GetAsync("/api/v1/queries/counterparties");
        список.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await список.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Single(страница.Items);
    }

    [Fact]
    public async Task Слишком_Большое_Тело_Запроса_Отклоняется()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        using var сообщение = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SharedProtocol.RoutePrefix}/commands/counterparties.save");

        сообщение.Headers.Add(SharedProtocol.OperationIdHeader, Guid.NewGuid().ToString("D"));

        var огромное = new string('x', SharedProtocol.MaxRequestBodyBytes + 1024);

        сообщение.Content = new StringContent(
            JsonSerializer.Serialize(new SaveCounterpartyRequest(Контрагент(огромное), 0), SharedJson.Options),
            Encoding.UTF8,
            "application/json");

        // Слишком большое тело отклоняется: сервер закрывает соединение во
        // время передачи, поэтому клиент может получить и отказ по состоянию,
        // и сетевую ошибку. Проверяется главное: команда не была выполнена и
        // запись не создана.
        try
        {
            using var ответ = await клиент.Клиент.SendAsync(сообщение);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, ответ.StatusCode);
        }
        catch (HttpRequestException)
        {
            // Соединение закрыто сервером при передаче слишком большого тела.
        }

        using var список = await клиент.Клиент.GetAsync("/api/v1/queries/counterparties");
        список.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await список.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Empty(страница.Items);
    }

    [Fact]
    public async Task Исход_Печати_Фиксируется_В_Общем_Журнале()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var созданный = await клиент
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(Контрагент("ООО «Печать»"), 0))
            ;

        var отправитель = await клиент
            .ВыполнитьИРазобратьAsync<CreateAgentRequest, CounterpartyResponse>(
                "counterparties.create-agent",
                new CreateAgentRequest("ООО «Отправитель печати»"))
            ;

        var письмо = await клиент
            .ВыполнитьИРазобратьAsync<CreateMailRequest, MailDto>(
                "mails.create",
                new CreateMailRequest(Письмо(созданный.Id, отправитель.Id)))
            ;

        // Реестр сессий создаётся отдельной командой без привязки к
        // отправлениям.
        var сессия = await клиент
            .ВыполнитьИРазобратьAsync<CreatePrintSessionRequest, CreatePrintSessionResponse>(
                "print.create-session",
                new CreatePrintSessionRequest(
                    null,
                    "Принтер HP",
                    "A4",
                    210m,
                    297m,
                    null,
                    2,
                    "selected",
                    "SUCCESS",
                    null))
            ;

        Assert.True(сессия.PrintJobId > 0);

        var ответ = await клиент
            .ВыполнитьИРазобратьAsync<RecordPrintEventRequest, PrintEventResponse>(
                "print.record-event",
                new RecordPrintEventRequest(
                    new[] { письмо.Id },
                    "Конверт C6",
                    PrintEventOutcome.Spooled,
                    "Принтер HP",
                    "A4",
                    2,
                    "selected",
                    null))
            ;

        Assert.Equal(PrintEventOutcome.Spooled, ответ.Outcome);

        using var журнал = await клиент.Клиент.GetAsync("/api/v1/queries/print-history?offset=0&limit=50");
        журнал.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<PrintHistoryDto>>(
            await журнал.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.NotEmpty(страница.Items);
        Assert.Equal(PrintEventOutcome.Spooled.ToString(), страница.Items[0].Outcome);
        Assert.Equal(клиент.Устройство, страница.Items[0].DeviceId);
    }

    [Fact]
    public async Task Конфликт_Версий_Рассылки_Не_Теряет_Данные()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиентA = await стенд.СоздатьКлиентаAsync("ПК оператора 1");
        using var клиентB = await стенд.СоздатьКлиентаAsync("ПК оператора 2");

        var получатель = await клиентA
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(Контрагент("ООО «Получатель»"), 0))
            ;

        var отправитель = await клиентA
            .ВыполнитьИРазобратьAsync<CreateAgentRequest, CounterpartyResponse>(
                "counterparties.create-agent",
                new CreateAgentRequest("ООО «Отправитель»"))
            ;

        var рассылка = await клиентA
            .ВыполнитьИРазобратьAsync<CreateDistributionFromSelectionRequest, DistributionDto>(
                "distributions.create-from-selection",
                new CreateDistributionFromSelectionRequest(
                    new[] { получатель.Id },
                    отправитель.Id,
                    "Ноябрьская рассылка",
                    "C6"))
            ;

        // Клиент B читает рассылку и получает её версию.
        using var ответB = await клиентB.Клиент.GetAsync("/api/v1/queries/distributions");
        ответB.EnsureSuccessStatusCode();

        var список = JsonSerializer.Deserialize<DistributionDto[]>(
            await ответB.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        var версияB = список[0].Version;

        // Клиент A меняет рассылку: версия увеличивается.
        var изменённая = await клиентA
            .ВыполнитьИРазобратьAsync<UpdateDistributionRequest, UpdateDistributionResponse>(
                "distributions.update",
                new UpdateDistributionRequest(
                    рассылка.Id,
                    "Ноябрьская рассылка (правка A)",
                    DistributionKind.OneToMany,
                    null,
                    new[] { получатель.Id },
                    "C6",
                    рассылка.Version))
            ;

        Assert.True(изменённая.Distribution.Version > версияB);

        // Клиент B меняет по устаревшей версии — отказ без частичной записи.
        var ошибка = await Assert.ThrowsAsync<SharedTestCommandException>(() =>
            клиентB.ВыполнитьИРазобратьAsync<UpdateDistributionRequest, UpdateDistributionResponse>(
                "distributions.update",
                new UpdateDistributionRequest(
                    рассылка.Id,
                    "Ноябрьская рассылка (правка B)",
                    DistributionKind.OneToMany,
                    null,
                    new[] { получатель.Id },
                    "C6",
                    версияB)));

        Assert.Equal(SharedErrorCodes.Conflict, ошибка.Код);

        // Правка A сохранилась: правка B не была применена частично.
        using var итог = await клиентA.Клиент.GetAsync("/api/v1/queries/distributions");
        итог.EnsureSuccessStatusCode();

        var финальный = JsonSerializer.Deserialize<DistributionDto[]>(
            await итог.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Equal("Ноябрьская рассылка (правка A)", финальный[0].Name);
    }

    [Fact]
    public async Task Конфликт_При_Одновременном_Изменении_Одного_Контрагента()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиентA = await стенд.СоздатьКлиентаAsync("ПК оператора 1");
        using var клиентB = await стенд.СоздатьКлиентаAsync("ПК оператора 2");

        var созданный = await клиентA
            .ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(Контрагент("ООО «Гонка»"), 0))
            ;

        // Оба клиента читают одну и ту же версию и меняют запись одновременно.
        var задачаA = клиентA.ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
            "counterparties.save",
            new SaveCounterpartyRequest(
                Контрагент("ООО «Гонка» (A)", созданный.Id),
                созданный.Version));

        var задачаB = клиентB.ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
            "counterparties.save",
            new SaveCounterpartyRequest(
                Контрагент("ООО «Гонка» (B)", созданный.Id),
                созданный.Version));

        var результаты = await Task.WhenAll(
            Итог(задачаA),
            Итог(задачаB));

        Assert.Equal(1, результаты.Count(результат => результат.Успех));
        Assert.Equal(
            1,
            результаты.Count(результат => результат.Код == SharedErrorCodes.Conflict));

        // Запись в базе одна: ни одно изменение не потеряно молча, а запись
        // не продублировалась.
        using var список = await клиентA.Клиент.GetAsync("/api/v1/queries/counterparties");
        список.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await список.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Single(страница.Items);
    }

    /// <summary>
    /// Строит заведомо неверный код сопряжения: все знаки заменены на другие,
    /// поэтому код не может случайно совпасть с верным.
    /// </summary>
    /// <param name="код">Верный код.</param>
    /// <returns>Неверный код того же формата.</returns>
    private static string НеверныйКод(string код)
    {
        var знаки = код.ToCharArray();

        for (var i = 0; i < знаки.Length; i++)
        {
            знаки[i] = знаки[i] == 'A' ? 'B' : 'A';
        }

        return new string(знаки);
    }

    /// <summary>
    /// Определяет исход одной из двух одновременных попыток сохранения.
    /// </summary>
    /// <param name="задача">Задача сохранения.</param>
    /// <returns>Признак успеха и код ошибки при отказе.</returns>
    private static async Task<(bool Успех, string? Код)> Итог(
        Task<SaveCounterpartyResponse> задача)
    {
        try
        {
            await задача;

            return (true, null);
        }
        catch (SharedTestCommandException ошибка)
        {
            return (false, ошибка.Код);
        }
    }

    private static MailDto Письмо(long получательId, long отправительId) => new(
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
            new[] { "ООО «Печать»", "ул. Тверская", "д. 1", string.Empty, "Москва", "Московская обл." },
            null),
        FolderId: null,
        Version: 0);

    private static CounterpartyDto Контрагент(string наименование, long идентификатор = 0) => new(
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
            new AddressDto(
                "101000",
                new[] { наименование, "ул. Тверская", "д. 1", string.Empty, "Москва", "Московская обл." },
                null),
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
