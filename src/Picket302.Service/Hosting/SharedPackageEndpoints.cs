using Picket302.Service.Data;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Маршруты управляемых пакетов шаблонов.
/// </summary>
internal static class SharedPackageEndpoints
{
    /// <summary>
    /// Подключает маршруты пакетов шаблонов.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    public static void Подключить(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapGet(SharedProtocol.TemplatePackagesRoute, async (
            HttpContext http,
            CancellationToken токен) =>
        {
            var устройство = await SharedEndpoints
                .ОпознатьУстройствоAsync(контекст, http, токен)
                .ConfigureAwait(false);

            if (устройство is null)
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var хранилище = new SharedTemplatePackageStore(
                контекст.Параметры.TemplatesDirectory,
                область.Единица);

            var пакеты = await хранилище.ПеречислитьAsync(токен).ConfigureAwait(false);

            return Results.Json(
                new Shared.Commands.TemplatePackageListResponse(пакеты),
                SharedJson.Options);
        });

        приложение.MapGet(SharedProtocol.TemplatePackageContentRoute, async (
            string packageId,
            HttpContext http,
            CancellationToken токен) =>
        {
            var устройство = await SharedEndpoints
                .ОпознатьУстройствоAsync(контекст, http, токен)
                .ConfigureAwait(false);

            if (устройство is null)
            {
                return SharedEndpoints.ОшибкаОтказа(http);
            }

            var версияСтрока = http.Request.Query["version"].ToString();

            if (!long.TryParse(версияСтрока, out var версия) || версия <= 0)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;

                return Results.Json(
                    new SharedError(SharedErrorCodes.ValidationFailed, "Версия пакета не указана."),
                    SharedJson.Options);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var хранилище = new SharedTemplatePackageStore(
                контекст.Параметры.TemplatesDirectory,
                область.Единица);

            byte[]? содержимое;

            try
            {
                содержимое = await хранилище
                    .ПрочитатьВерсиюAsync(packageId, версия, токен)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ошибка)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;

                return Results.Json(
                    new SharedError(SharedErrorCodes.ValidationFailed, ошибка.Message),
                    SharedJson.Options);
            }

            if (содержимое is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;

                return Results.Json(
                    new SharedError(SharedErrorCodes.NotFound, "Версия пакета не найдена."),
                    SharedJson.Options);
            }

            return Results.File(содержимое, "application/octet-stream");
        });
    }
}
