using System.Text.Json;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Маршруты чтения справочных данных общей базы.
/// </summary>
/// <remarks>
/// Справочные данные (реестры, документы, описи, журналы) читаются теми же
/// маршрутами, что и основные справочники: единый способ доступа и единая
/// проверка доступа устройства.
/// </remarks>
internal static class SharedCatalogEndpoints
{
    /// <summary>
    /// Подключает маршруты чтения справочных данных.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    public static void Подключить(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapGet("/api/v1/queries/senders", async (HttpContext http, CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var отправители = await область.Данные.Контрагенты.GetSendersAsync(токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Counterparty,
                    отправители.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                отправители
                    .Select(з => SharedMapping.ККонтрагенту(з, версии.GetValueOrDefault(з.Id)))
                    .ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/merge-key", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var инн = http.Request.Query["inn"].ToString();
            var наименование = http.Request.Query["name"].ToString();
            var индекс = http.Request.Query["zip"].ToString();

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var найден = await область.Данные.Контрагенты
                .FindByMergeKeyAsync(
                    string.IsNullOrWhiteSpace(инн) ? null : инн,
                    наименование,
                    string.IsNullOrWhiteSpace(индекс) ? null : индекс,
                    токен)
                .ConfigureAwait(false);

            if (найден is null)
            {
                // Контрагент не найден: пустой результат — обычный ответ на
                // поиск, а не ошибка.
                return Results.Json(
                    JsonSerializer.SerializeToElement<CounterpartyDto?>(null, SharedJson.Options),
                    SharedJson.Options);
            }

            var версия = await область.Версии
                .ПолучитьВерсиюAsync(SharedEntityKind.Counterparty, найден.Id, токен)
                .ConfigureAwait(false);

            return Results.Json(SharedMapping.ККонтрагенту(найден, версия), SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/distinct-values", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var поле = http.Request.Query["field"].ToString();

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var значения = await область.Данные.Контрагенты
                .GetDistinctFieldValuesAsync(поле, токен)
                .ConfigureAwait(false);

            return Results.Json(значения, SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/category-tree", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var дерево = await область.Данные.СлужбаКатегорий.GetTreeAsync(токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Group,
                    дерево.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                дерево.Select(з => SharedCatalogMapping.ККатегории(з, версии.GetValueOrDefault(з.Id)))
                    .ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/print-jobs", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var offset = SharedEndpoints.ЧитатьЦелое(http, "offset", PageRequest.DefaultOffset);
            var limit = SharedEndpoints.ЧитатьЦелое(http, "limit", PageRequest.DefaultLimit);

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var страница = await область.Данные.ЖурналПечати
                .GetPagedAsync(new PageRequest(offset, limit), токен)
                .ConfigureAwait(false);

            return Results.Json(
                new PageDto<PrintJobDto>(
                    страница.Items.Select(з => SharedMapping.КСессииПечати(з, null)).ToArray(),
                    страница.Offset,
                    страница.Limit,
                    страница.TotalCount),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/print-history", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var offset = SharedEndpoints.ЧитатьЦелое(http, "offset", PageRequest.DefaultOffset);
            var limit = SharedEndpoints.ЧитатьЦелое(http, "limit", PageRequest.DefaultLimit);

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var страница = await область.Данные.ИсторииПечати
                .GetPagedAsync(new PageRequest(offset, limit), токен)
                .ConfigureAwait(false);

            return Results.Json(
                new PageDto<PrintHistoryDto>(
                    страница.Items.Select(з => SharedMapping.КИсторииПечати(з, з.DeviceId)).ToArray(),
                    страница.Offset,
                    страница.Limit,
                    страница.TotalCount),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/email-log", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var limit = SharedEndpoints.ЧитатьЦелое(http, "limit", 100);

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var писатель = new Picket302.Infrastructure.Mail.SqliteEmailLogWriter(область.Единица);
            var записи = await писатель.ReadAsync(limit, токен).ConfigureAwait(false);

            return Results.Json(
                записи.Select(з => new EmailLogEntryDto(
                    з.Recipient,
                    з.Status,
                    з.SmtpCode,
                    з.ServerResponse,
                    з.Error,
                    з.SentAtUtc)).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/post-registers", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var реестры = await область.Данные.ПочтовыеРеестры.GetAllAsync(токен)
                .ConfigureAwait(false);

            return Results.Json(
                реестры.Select(SharedCatalogMapping.КРеестру).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/mail-documents", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var идентификатор = SharedEndpoints.ПрочитатьДлинное(http, "mailId");

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var документы = await область.Данные.ДокументыПисем
                .GetByMailAsync(идентификатор, токен)
                .ConfigureAwait(false);

            return Results.Json(
                документы.Select(SharedCatalogMapping.КДокументу).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/document-movements", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var идентификатор = SharedEndpoints.ПрочитатьДлинное(http, "mailId");

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var движения = await область.Данные.ДвиженияДокументов
                .GetByMailAsync(идентификатор, токен)
                .ConfigureAwait(false);

            return Results.Json(
                движения.Select(SharedCatalogMapping.КДвижению).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/inventories", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var описи = await область.Данные.Описи.GetAllAsync(токен).ConfigureAwait(false);

            return Results.Json(
                описи.Select(SharedCatalogMapping.КОписи).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/inventory-items", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var идентификатор = SharedEndpoints.ПрочитатьДлинное(http, "inventoryId");

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var позиции = await область.Данные.ПозицииОписей
                .GetByInventoryAsync(идентификатор, токен)
                .ConfigureAwait(false);

            return Results.Json(
                позиции.Select(SharedCatalogMapping.КПозиции).ToArray(),
                SharedJson.Options);
        });
    }

    /// <summary>
    /// Проверяет, что обращение разрешено устройству-клиенту.
    /// </summary>
    /// <param name="контекст">Служебное состояние службы.</param>
    /// <param name="http">Контекст запроса.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns><see langword="true"/>, если устройство опознано.</returns>
    private static async Task<bool> РазрешёнAsync(
        SharedServiceContext контекст,
        HttpContext http,
        CancellationToken токен)
    {
        var устройство = await SharedEndpoints
            .ОпознатьУстройствоAsync(контекст, http, токен)
            .ConfigureAwait(false);

        return устройство is not null;
    }
}
