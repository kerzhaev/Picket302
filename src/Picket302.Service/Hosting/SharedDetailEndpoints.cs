using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Маршруты чтения карточек и счётчиков общей базы.
/// </summary>
/// <remarks>
/// Карточка отправления, карточка рассылки, контрагенты группы и счётчики
/// папок читаются теми же маршрутами, что и списки: единый способ доступа и
/// единая проверка доступа устройства.
/// </remarks>
internal static class SharedDetailEndpoints
{
    /// <summary>
    /// Подключает маршруты чтения карточек и счётчиков.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    public static void Подключить(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapGet("/api/v1/queries/mails/{id:long}", async (
            long id,
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var письмо = await область.Данные.Письма.GetByIdAsync(id, токен).ConfigureAwait(false);

            if (письмо is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;

                return Results.Json(
                    new SharedError(SharedErrorCodes.NotFound, "Отправление не найдено."),
                    SharedJson.Options);
            }

            var версия = await область.Версии
                .ПолучитьВерсиюAsync(SharedEntityKind.Mail, id, токен)
                .ConfigureAwait(false);

            return Results.Json(SharedMapping.КОтправлению(письмо, версия), SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/distributions/{id:long}", async (
            long id,
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var рассылка = await область.Данные.Рассылки.GetByIdAsync(id, токен).ConfigureAwait(false);

            if (рассылка is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;

                return Results.Json(
                    new SharedError(SharedErrorCodes.NotFound, "Рассылка не найдена."),
                    SharedJson.Options);
            }

            var версия = await область.Версии
                .ПолучитьВерсиюAsync(SharedEntityKind.Distribution, id, токен)
                .ConfigureAwait(false);

            var письма = await область.Данные.Письма
                .GetByDistributionAsync(id, токен)
                .ConfigureAwait(false);

            return Results.Json(
                SharedMapping.КРассылке(рассылка, версия, письма.Count),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/counterparties/by-group/{groupId:long}", async (
            long groupId,
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var контрагенты = await область.Данные.Контрагенты
                .GetByGroupAsync(groupId, токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Counterparty,
                    контрагенты.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                контрагенты
                    .Select(з => SharedMapping.ККонтрагенту(з, версии.GetValueOrDefault(з.Id)))
                    .ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/folder-counts", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var счётчики = await область.Данные.Письма
                .ПолучитьСчётчикиПоПапкамAsync(токен)
                .ConfigureAwait(false);

            return Results.Json(счётчики, SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/folder-mail-counts", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!await РазрешёнAsync(контекст, http, токен).ConfigureAwait(false))
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var папка = SharedEndpoints.ПрочитатьДлинное(http, "folderId");

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var число = await область.Данные.СлужбаПапок
                .CountMailsAsync(папка, токен)
                .ConfigureAwait(false);

            return Results.Json(
                new FolderMailCountResponse(папка, число),
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
