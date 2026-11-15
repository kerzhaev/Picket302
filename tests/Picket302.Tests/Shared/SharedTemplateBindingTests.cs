using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Picket302.Domain.Entities;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Связь строки шаблона с управляемым пакетом .lbk в совместном режиме.
/// </summary>
/// <remarks>
/// Без явной связи рабочее место не может получить содержимое макета: путь к
/// файлу на сервере клиенту не передаётся. Связь состоит из идентификатора
/// пакета, версии и SHA-256 содержимого; после смены версии пакета привязка
/// переходит на новую версию, чтобы рабочее место не печатало по устаревшему
/// макету.
/// </remarks>
public sealed class SharedTemplateBindingTests
{
    [Fact]
    public async Task ПакетЗагружен_ШаблонСвязывается_ПривязкаВозвращается()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var пакет = await ЗагрузитьПакетAsync(клиент, "Конверт C5");

        var сохранённый = await клиент.ВыполнитьИРазобратьAsync<SavePrintTemplateRequest, PrintTemplateDto>(
            "templates.save",
            new SavePrintTemplateRequest(
                new PrintTemplateDto(
                    0,
                    "Конверт C5",
                    "envelope",
                    "C5",
                    229m,
                    162m,
                    TemplateOrientation.Landscape,
                    false,
                    "RUS",
                    пакет.PackageId,
                    пакет.Version,
                    пакет.ContentHash,
                    0),
                0));

        Assert.Equal(пакет.PackageId, сохранённый.PackageId);
        Assert.Equal(пакет.Version, сохранённый.PackageVersion);
        Assert.Equal(пакет.ContentHash, сохранённый.ContentHash);

        var список = await ПрочитатьШаблоныAsync(клиент);

        var строка = Assert.Single(список);

        Assert.Equal(пакет.PackageId, строка.PackageId);
        Assert.Equal(пакет.Version, строка.PackageVersion);
        Assert.Equal(пакет.ContentHash, строка.ContentHash);
    }

    [Fact]
    public async Task НоваяВерсияПакета_ПривязкаШаблонаПереходитНаНовуюВерсию()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var первый = await ЗагрузитьПакетAsync(клиент, "Конверт C5");

        await клиент.ВыполнитьИРазобратьAsync<SavePrintTemplateRequest, PrintTemplateDto>(
            "templates.save",
            new SavePrintTemplateRequest(
                new PrintTemplateDto(
                    0,
                    "Конверт C5",
                    "envelope",
                    "C5",
                    229m,
                    162m,
                    TemplateOrientation.Landscape,
                    false,
                    "RUS",
                    первый.PackageId,
                    первый.Version,
                    первый.ContentHash,
                    0),
                0));

        // Обновление пакета: содержимое изменено, версия выросла.
        var второй = await ЗагрузитьПакетAsync(клиент, "Конверт C5", "v2");

        Assert.Equal(первый.PackageId, второй.PackageId);
        Assert.Equal(первый.Version + 1, второй.Version);
        Assert.NotEqual(первый.ContentHash, второй.ContentHash);

        // Привязка шаблона переходит на новую версию: иначе рабочее место
        // запросило бы прежнюю версию и напечатало по устаревшему макету.
        var список = await ПрочитатьШаблоныAsync(клиент);

        var строка = Assert.Single(список);

        Assert.Equal(второй.PackageId, строка.PackageId);
        Assert.Equal(второй.Version, строка.PackageVersion);
        Assert.Equal(второй.ContentHash, строка.ContentHash);
    }

    [Fact]
    public async Task ШаблонССсылкойНаНесуществующийПакет_Отклонён()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var ошибка = await Assert.ThrowsAsync<SharedTestCommandException>(
            () => клиент.ВыполнитьИРазобратьAsync<SavePrintTemplateRequest, PrintTemplateDto>(
                "templates.save",
                new SavePrintTemplateRequest(
                    new PrintTemplateDto(
                        0,
                        "Конверт C5",
                        "envelope",
                        "C5",
                        229m,
                        162m,
                        TemplateOrientation.Landscape,
                        false,
                        "RUS",
                        Guid.NewGuid().ToString("D"),
                        1,
                        new string('a', 64),
                        0),
                    0)));

        Assert.Equal(SharedErrorCodes.ValidationFailed, ошибка.Код);
    }

    [Fact]
    public async Task ЖурналПечати_ХранитИсходУстройствоИИдентификаторОперации()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var получатель = await клиент.ВыполнитьИРазобратьAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
            "counterparties.save",
            new SaveCounterpartyRequest(Контрагент("ООО «Получатель печати»"), 0));

        var отправитель = await клиент.ВыполнитьИРазобратьAsync<CreateAgentRequest, CounterpartyResponse>(
            "counterparties.create-agent",
            new CreateAgentRequest("ООО «Отправитель печати»"));

        var письмо = await клиент.ВыполнитьИРазобратьAsync<CreateMailRequest, MailDto>(
            "mails.create",
            new CreateMailRequest(Письмо(получатель.Id, отправитель.Id)));

        var идентификатор = Guid.NewGuid().ToString("D");

        await клиент.ВыполнитьИРазобратьAsync<RecordPrintEventRequest, PrintEventResponse>(
            "print.record-event",
            new RecordPrintEventRequest(
                new[] { письмо.Id },
                "Конверт C6",
                PrintEventOutcome.Prepared,
                null,
                null,
                1,
                "selected",
                null),
            идентификатор);

        using var журнал = await клиент.Клиент.GetAsync("/api/v1/queries/print-history?offset=0&limit=50");
        журнал.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<PrintHistoryDto>>(
            await журнал.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        var запись = Assert.Single(страница.Items);

        Assert.Equal(PrintEventOutcome.Prepared.ToString(), запись.Outcome);
        Assert.Equal(клиент.Устройство, запись.DeviceId);
        Assert.Equal(идентификатор, запись.OperationId);
    }

    private static async Task<TemplatePackageDto> ЗагрузитьПакетAsync(
        SharedTestClient клиент,
        string имя,
        string? содержимоеBlank = null)
    {
        var метаданные = await клиент.ВыполнитьИРазобратьAsync<UploadTemplatePackageRequest, TemplatePackageDto>(
            "templates.upload-package",
            new UploadTemplatePackageRequest(имя, Convert.ToBase64String(СобратьПакет(содержимоеBlank))));

        return метаданные;
    }

    private static async Task<IReadOnlyList<PrintTemplateDto>> ПрочитатьШаблоныAsync(SharedTestClient клиент)
    {
        using var ответ = await клиент.Клиент.GetAsync("/api/v1/queries/templates");
        ответ.EnsureSuccessStatusCode();

        return JsonSerializer.Deserialize<PrintTemplateDto[]>(
            await ответ.Content.ReadAsStringAsync(),
            SharedJson.Options)!;
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
            new[] { "ООО «Получатель печати»", "ул. Тверская", "д. 1", string.Empty, "Москва", "Московская обл." },
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

    /// <summary>
    /// Собирает минимальный пакет шаблона: ZIP с обязательным blank.xml.
    /// </summary>
    /// <param name="содержимое">Содержимое blank.xml.</param>
    /// <returns>Содержимое пакета.</returns>
    private static byte[] СобратьПакет(string? содержимое)
    {
        using var поток = new MemoryStream();

        using (var архив = new ZipArchive(поток, ZipArchiveMode.Create, leaveOpen: true))
        {
            var запись = архив.CreateEntry("blank.xml");

            using var писатель = new StreamWriter(запись.Open(), new UTF8Encoding(false));

            писатель.Write(содержимое ?? "<blank version=\"1\"></blank>");
        }

        return поток.ToArray();
    }
}
