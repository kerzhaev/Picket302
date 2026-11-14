using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Маршрут чтения писем рассылки.
/// </summary>
internal static class SharedDistributionMailEndpoint
{
    /// <summary>
    /// Подключает маршрут писем рассылки.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    public static void Подключить(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapGet("/api/v1/queries/distribution-mails", async (
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

            var рассылка = SharedEndpoints.ПрочитатьДлинное(http, "distributionId");

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var письма = await область.Данные.Письма
                .GetByDistributionAsync(рассылка, токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Mail,
                    письма.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                письма
                    .Select(з => SharedMapping.КОтправлению(з, версии.GetValueOrDefault(з.Id)))
                    .ToArray(),
                SharedJson.Options);
        });
    }
}
