using System.Net;
using System.Text.Json;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Слияние пакета импорта в совместном режиме: записи применяются серверной
/// командой в одной транзакции.
/// </summary>
/// <remarks>
/// Требования: записи импорта видны всем клиентам; повтор команды с прежним
/// идентификатором операции не создаёт дублей; повтор с другим телом
/// отклоняется; пустой пакет отклоняется без записи; неверные данные не
/// оставляют частичного импорта.
/// </remarks>
public sealed class SharedImportMergeTests
{
    [Fact]
    public async Task Импорт_ЗаписиВидныОбоимКлиентам()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиентA = await стенд.СоздатьКлиентаAsync("ПК оператора 1");
        using var клиентB = await стенд.СоздатьКлиентаAsync("ПК оператора 2");

        var ответ = await клиентA.ВыполнитьИРазобратьAsync<ImportMergeRequest, ImportMergeResponse>(
            "import.merge",
            Запрос("ООО «Импорт 1»", "101000"));

        Assert.Equal(1, ответ.Created);
        Assert.Equal(0, ответ.Skipped);
        Assert.Equal(0, ответ.Conflicts);

        using var список = await клиентB.Клиент.GetAsync("/api/v1/queries/counterparties");
        список.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await список.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Single(страница.Items);
        Assert.Equal("ООО «Импорт 1»", страница.Items[0].Name);
    }

    [Fact]
    public async Task Импорт_ПовторСТемЖеИдентификатором_НеСоздаётДубль()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var идентификатор = Guid.NewGuid().ToString("D");
        var запрос = Запрос("ООО «Импорт 2»", "101000");

        var первый = await клиент.ВыполнитьИРазобратьAsync<ImportMergeRequest, ImportMergeResponse>(
            "import.merge",
            запрос,
            идентификатор);

        Assert.Equal(1, первый.Created);

        // Повтор с прежним идентификатором операции и тем же телом: сервер
        // возвращает прежний результат, запись не дублируется.
        var повтор = await клиент.ВыполнитьИРазобратьAsync<ImportMergeRequest, ImportMergeResponse>(
            "import.merge",
            запрос,
            идентификатор);

        Assert.Equal(1, повтор.Created);

        using var список = await клиент.Клиент.GetAsync("/api/v1/queries/counterparties");
        список.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await список.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Single(страница.Items);
    }

    [Fact]
    public async Task Импорт_ПовторСДругимТелом_Отклонён()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var идентификатор = Guid.NewGuid().ToString("D");

        await клиент.ВыполнитьИРазобратьAsync<ImportMergeRequest, ImportMergeResponse>(
            "import.merge",
            Запрос("ООО «Импорт 3»", "101000"),
            идентификатор);

        var ошибка = await Assert.ThrowsAsync<SharedTestCommandException>(
            () => клиент.ВыполнитьИРазобратьAsync<ImportMergeRequest, ImportMergeResponse>(
                "import.merge",
                Запрос("ООО «Другая фирма»", "101000"),
                идентификатор));

        Assert.Equal(SharedErrorCodes.IdempotencyMismatch, ошибка.Код);
    }

    [Fact]
    public async Task Импорт_ПустойПакет_ОтклонёнБезЗаписей()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var ошибка = await Assert.ThrowsAsync<SharedTestCommandException>(
            () => клиент.ВыполнитьИРазобратьAsync<ImportMergeRequest, ImportMergeResponse>(
                "import.merge",
                new ImportMergeRequest(
                    "CSV",
                    "пусто.csv",
                    DateTime.UtcNow,
                    Array.Empty<ImportRecord>(),
                    MergePolicy.SkipExisting)));

        Assert.Equal(SharedErrorCodes.ValidationFailed, ошибка.Код);

        using var список = await клиент.Клиент.GetAsync("/api/v1/queries/counterparties");
        список.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await список.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Empty(страница.Items);
    }

    [Fact]
    public async Task Импорт_НекорректнаяСхемаЗапроса_ОтклоненаБезЗаписи()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        // Тело команды не соответствует контракту: поле records должно быть
        // массивом записей, а не строкой.
        using var сообщение = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SharedProtocol.RoutePrefix}/commands/import.merge");

        сообщение.Headers.Add(SharedProtocol.OperationIdHeader, Guid.NewGuid().ToString("D"));

        сообщение.Content = new StringContent(
            "{\"sourceFormat\":\"CSV\",\"records\":\"не массив\"}",
            System.Text.Encoding.UTF8,
            "application/json");

        using var ответ = await клиент.Клиент.SendAsync(сообщение);

        Assert.Equal(HttpStatusCode.BadRequest, ответ.StatusCode);

        var ошибка = JsonSerializer.Deserialize<SharedError>(
            await ответ.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Equal(SharedErrorCodes.ValidationFailed, ошибка.Code);

        using var список = await клиент.Клиент.GetAsync("/api/v1/queries/counterparties");
        список.EnsureSuccessStatusCode();

        var страница = JsonSerializer.Deserialize<PageDto<CounterpartyDto>>(
            await список.Content.ReadAsStringAsync(),
            SharedJson.Options)!;

        Assert.Empty(страница.Items);
    }

    private static ImportMergeRequest Запрос(string наименование, string индекс) => new(
        "CSV",
        "адреса.csv",
        DateTime.UtcNow,
        new[] { Запись(наименование, null, индекс) },
        MergePolicy.SkipExisting);

    private static ImportRecord Запись(string наименование, string? инн, string индекс = "101000") => new()
    {
        RowNumber = 1,
        Status = ImportRecordStatus.Verified,
        Draft = new CounterpartyDraft
        {
            Type = CounterpartyType.LegalEntity,
            Name = наименование,
            Inn = инн,
            PostalZip = индекс,
            PostalLines = [наименование, "ул. Тверская", "д. 1", string.Empty, "Москва", "Московская обл."],
            Source = "Импорт CSV от 15.11.2026 из адреса.csv",
        },
    };
}
