using System.Text.Json;
using Picket302.Composition;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Service.Data;
using Picket302.Service.Security;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Служебное состояние серверной службы.
/// </summary>
/// <param name="Параметры">Настройки службы.</param>
/// <param name="База">Фабрика сессий базы данных.</param>
/// <param name="Реестр">Реестр команд.</param>
/// <param name="Конвейер">Конвейер выполнения команд.</param>
/// <param name="Опознаватель">Опознаватель устройств-клиентов.</param>
/// <param name="АдминТокен">Административный токен службы.</param>
/// <param name="ИдентификаторСервера">Идентификатор сервера.</param>
/// <param name="ОтпечатокСервера">Отпечаток открытого ключа сервера.</param>
/// <param name="Журнал">Приёмник записей журнала службы.</param>
public sealed record SharedServiceContext(
    Configuration.ServiceOptions Параметры,
    ServerDatabase База,
    SharedCommandRegistry Реестр,
    SharedCommandPipeline Конвейер,
    SharedDeviceAuthenticator Опознаватель,
    SharedAdminToken АдминТокен,
    string ИдентификаторСервера,
    string ОтпечатокСервера,
    Action<string> Журнал);

/// <summary>
/// Маршруты API совместного режима.
/// </summary>
public static class SharedEndpoints
{
    /// <summary>
    /// Подключает маршруты службы к приложению.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    public static void Подключить(WebApplication приложение, SharedServiceContext контекст)
    {
        ArgumentNullException.ThrowIfNull(приложение);
        ArgumentNullException.ThrowIfNull(контекст);

        ПодключитьСлужебные(приложение, контекст);
        ПодключитьСопряжение(приложение, контекст);
        ПодключитьКоманды(приложение, контекст);
        ПодключитьЗапросы(приложение, контекст);
        SharedCatalogEndpoints.Подключить(приложение, контекст);
        SharedDetailEndpoints.Подключить(приложение, контекст);
        SharedDistributionMailEndpoint.Подключить(приложение, контекст);
        SharedPackageEndpoints.Подключить(приложение, контекст);
        ПодключитьАдминистративные(приложение, контекст);
    }

    /// <summary>
    /// Подключает служебные маршруты: состояние службы и сведения о сервере.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    private static void ПодключитьСлужебные(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapGet(SharedProtocol.HealthRoute, async (CancellationToken токен) =>
        {
            var устройств = await ПодсчитатьУстройстваAsync(контекст, токен).ConfigureAwait(false);

            return Results.Json(
                new HealthResponse(
                    "готова",
                    SharedProtocol.Version,
                    контекст.ИдентификаторСервера,
                    DateTime.UtcNow),
                SharedJson.Options);
        });

        приложение.MapGet(SharedProtocol.ServerInfoRoute, async (CancellationToken токен) =>
        {
            var устройств = await ПодсчитатьУстройстваAsync(контекст, токен).ConfigureAwait(false);

            return Results.Json(
                new ServerInfoResponse(
                    контекст.ИдентификаторСервера,
                    SharedProtocol.Version,
                    контекст.ОтпечатокСервера,
                    устройств),
                SharedJson.Options);
        });
    }

    /// <summary>
    /// Подключает маршрут сопряжения клиентского устройства.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    private static void ПодключитьСопряжение(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapPost(SharedProtocol.PairRoute, async (
            PairRequest? запрос,
            HttpContext http,
            CancellationToken токен) =>
        {
            if (запрос is null || string.IsNullOrWhiteSpace(запрос.Code))
            {
                return Ошибка(
                    SharedErrorCodes.ValidationFailed,
                    "Код сопряжения не задан.",
                    http);
            }

            // Код состоит из фрагмента отпечатка сервера и одноразового
            // секрета. Фрагмент позволяет клиенту убедиться, что он говорит
            // с тем сервером, который показал код; секретная часть ищется в
            // базе по своему отпечатку.
            var (фрагмент, секрет) = SharedSecrets.РазделитьКодСопряжения(запрос.Code);

            if (секрет.Length == 0)
            {
                return Ошибка(
                    SharedErrorCodes.ValidationFailed,
                    "Код сопряжения не задан.",
                    http);
            }

            var сессия = контекст.База.СоздатьСессию();
            await using var освобождение = сессия.ConfigureAwait(false);
            var сопряжения = new SharedPairingStore(сессия);

            // Предел попыток проверяется до проверки кода: после исчерпания
            // предела ни верный, ни неверный код не принимается.
            if (await сопряжения.ПревышенПределПопытокAsync(токен).ConfigureAwait(false))
            {
                контекст.Журнал(
                    $"Попытка сопряжения отклонена: число попыток исчерпано "
                    + $"(устройство «{запрос.DeviceName}»).");

                return Ошибка(
                    SharedErrorCodes.Unauthorized,
                    "Число попыток сопряжения исчерпано. Обратитесь к администратору сервера.",
                    http);
            }

            var проверка = await сопряжения
                .ПроверитьAsync(
                    SharedSecrets.ВычислитьОтпечатокСекретаКода(секрет),
                    фрагмент,
                    токен)
                .ConfigureAwait(false);

            if (!проверка.Успешно)
            {
                // Каждая неудачная попытка учитывается, в том числе та, при
                // которой секретная часть кода не найдена: иначе перебор
                // секрета ничем не ограничивался бы.
                await сопряжения
                    .ЗарегистрироватьНеудачнуюПопыткуAsync(токен)
                    .ConfigureAwait(false);

                контекст.Журнал(
                    $"Неудачная попытка сопряжения: {проверка.Причина} "
                    + $"(устройство «{запрос.DeviceName}»).");

                return Ошибка(
                    SharedErrorCodes.Unauthorized,
                    проверка.Причина ?? "Код сопряжения неверный.",
                    http);
            }

            var устройство = Guid.NewGuid().ToString("D");
            var секретУстройства = SharedSecrets.СоздатьСекретУстройства();

            var устройства = new SharedDeviceStore(сессия);

            await устройства.ЗарегистрироватьAsync(
                устройство,
                string.IsNullOrWhiteSpace(запрос.DeviceName) ? "Клиент" : запрос.DeviceName,
                SharedSecrets.ВычислитьОтпечатокСекрета(секретУстройства),
                токен).ConfigureAwait(false);

            контекст.Журнал($"Сопряжено устройство «{запрос.DeviceName}» ({устройство}).");

            return Results.Json(
                new PairResponse(
                    устройство,
                    секретУстройства,
                    контекст.ИдентификаторСервера,
                    контекст.ОтпечатокСервера),
                SharedJson.Options);
        });
    }

    /// <summary>
    /// Подключает маршрут выполнения команд.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    private static void ПодключитьКоманды(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapPost(
            SharedProtocol.CommandRoute,
            async (string command, HttpContext http, CancellationToken токен) =>
            {
                var устройство = await ОпознатьУстройствоAsync(контекст, http, токен)
                    .ConfigureAwait(false);

                if (устройство is null)
                {
                    return Ошибка(
                        SharedErrorCodes.Unauthorized,
                        "Устройство не сопряжено или доступ отозван.",
                        http);
                }

                var версия = http.Request.Headers[SharedProtocol.ProtocolVersionHeader].ToString();

                if (!int.TryParse(версия, out var версияПротокола)
                    || !SharedProtocol.IsSupported(версияПротокола))
                {
                    return Ошибка(
                        SharedErrorCodes.VersionIncompatible,
                        "Версия протокола клиента не поддерживается службой.",
                        http);
                }

                var operationId = http.Request.Headers[SharedProtocol.OperationIdHeader].ToString();

                if (string.IsNullOrWhiteSpace(operationId))
                {
                    return Ошибка(
                        SharedErrorCodes.ValidationFailed,
                        "Не задан идентификатор операции.",
                        http);
                }

                var тело = await ПрочитатьТелоAsync(http, токен).ConfigureAwait(false);

                if (тело is null)
                {
                    return Ошибка(
                        SharedErrorCodes.PayloadTooLarge,
                        "Размер запроса превышает допустимый.",
                        http);
                }

                var результат = await контекст.Конвейер
                    .ВыполнитьAsync(
                        command,
                        operationId,
                        устройство.DeviceId,
                        устройство.Name,
                        тело,
                        токен)
                    .ConfigureAwait(false);

                if (результат.Успешно)
                {
                    http.Response.Headers[SharedProtocol.ProtocolVersionHeader] =
                        SharedProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    return Results.Content(результат.Результат ?? "null", "application/json");
                }

                http.Response.StatusCode = результат.КодСостояния;

                return Results.Json(результат.Ошибка, SharedJson.Options);
            });
    }

    /// <summary>
    /// Подключает маршруты чтения данных общей базы.
    /// </summary>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    private static void ПодключитьЗапросы(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapGet("/api/v1/queries/counterparties", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            var offset = ЧитатьЦелое(http, "offset", PageRequest.DefaultOffset);
            var limit = ЧитатьЦелое(http, "limit", PageRequest.DefaultLimit);
            var фильтр = ЧитатьФильтр(http);

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var страница = await область.Данные.Контрагенты
                .GetPagedAsync(new PageRequest(offset, limit), SharedMapping.КФильтруКонтрагентов(фильтр), токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Counterparty,
                    страница.Items.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            var dto = new PageDto<CounterpartyDto>(
                страница.Items
                    .Select(з => SharedMapping.ККонтрагенту(з, версии.GetValueOrDefault(з.Id)))
                    .ToArray(),
                страница.Offset,
                страница.Limit,
                страница.TotalCount);

            return Results.Json(dto, SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/counterparties/{id:long}", async (
            long id,
            HttpContext http,
            CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var контрагент = await область.Данные.Контрагенты.GetByIdAsync(id, токен)
                .ConfigureAwait(false);

            if (контрагент is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;

                return Results.Json(
                    new SharedError(SharedErrorCodes.NotFound, "Контрагент не найден."),
                    SharedJson.Options);
            }

            var версия = await область.Версии
                .ПолучитьВерсиюAsync(SharedEntityKind.Counterparty, id, токен)
                .ConfigureAwait(false);

            return Results.Json(SharedMapping.ККонтрагенту(контрагент, версия), SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/groups", async (HttpContext http, CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var группы = await область.Данные.Группы.GetAllAsync(токен).ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Group,
                    группы.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                группы.Select(з => SharedMapping.КГруппе(з, версии.GetValueOrDefault(з.Id))).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/folders", async (HttpContext http, CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var папки = await область.Данные.Папки.GetAllAsync(токен).ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Folder,
                    папки.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                папки.Select(з => SharedMapping.КПапке(з, версии.GetValueOrDefault(з.Id))).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/mails", async (HttpContext http, CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            var offset = ЧитатьЦелое(http, "offset", PageRequest.DefaultOffset);
            var limit = ЧитатьЦелое(http, "limit", PageRequest.DefaultLimit);

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var страница = await область.Данные.Письма
                .GetPagedAsync(new PageRequest(offset, limit), null, токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Mail,
                    страница.Items.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                new PageDto<MailDto>(
                    страница.Items
                        .Select(з => SharedMapping.КОтправлению(з, версии.GetValueOrDefault(з.Id)))
                        .ToArray(),
                    страница.Offset,
                    страница.Limit,
                    страница.TotalCount),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/distributions", async (HttpContext http, CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var рассылки = await область.Данные.СлужбаРассылок
                .ListDistributionsAsync(null, токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.Distribution,
                    рассылки.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                рассылки
                    .Select(з => SharedMapping.КРассылке(з, версии.GetValueOrDefault(з.Id)))
                    .ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/templates", async (HttpContext http, CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var шаблоны = await область.Данные.ШаблоныПечати.GetAllAsync(null, токен).ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.PrintTemplate,
                    шаблоны.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                шаблоны
                    .Select(з => SharedMapping.КШаблону(з, версии.GetValueOrDefault(з.Id), null))
                    .ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/template-fields", async (
            long templateId,
            HttpContext http,
            CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var поля = await область.Данные.ПоляШаблонов
                .GetByTemplateAsync(templateId, токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.BlankField,
                    поля.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                поля.Select(з => SharedMapping.КПолюБланка(з, версии.GetValueOrDefault(з.Id))).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/custom-fields", async (
            long counterpartyId,
            HttpContext http,
            CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var поля = await область.Данные.ПоляКонтрагентов
                .GetByCounterpartyAsync(counterpartyId, токен)
                .ConfigureAwait(false);

            var версии = await область.Версии
                .ПолучитьВерсииAsync(
                    SharedEntityKind.CounterpartyField,
                    поля.Select(з => з.Id).ToArray(),
                    токен)
                .ConfigureAwait(false);

            return Results.Json(
                поля.Select(з => SharedMapping.КПолюКонтрагента(з, версии.GetValueOrDefault(з.Id))).ToArray(),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/usage", async (HttpContext http, CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var контрагентов = await область.Данные.Обслуживание
                .CountScopeAsync(ClearScope.Counterparties, токен)
                .ConfigureAwait(false);

            var писем = await область.Данные.Обслуживание
                .CountScopeAsync(ClearScope.SentMails, токен)
                .ConfigureAwait(false);

            return Results.Json(new UsageSnapshotDto(контрагентов, писем), SharedJson.Options);
        });

        приложение.MapGet("/api/v1/queries/scope-counts", async (HttpContext http, CancellationToken токен) =>
        {
            var устройство = await ОпознатьУстройствоAsync(контекст, http, токен).ConfigureAwait(false);

            if (устройство is null)
            {
                return Ошибка(SharedErrorCodes.Unauthorized, "Устройство не сопряжено или доступ отозван.", http);
            }

            await using var область = await SharedDbScope.СоздатьAsync(контекст.База, токен)
                .ConfigureAwait(false);

            var счётчики = await область.Данные.СлужбаСидов.GetScopeCountsAsync(токен)
                .ConfigureAwait(false);

            return Results.Json(
                new ScopeCountsDto(
                    счётчики.GetValueOrDefault(ClearScope.Counterparties),
                    счётчики.GetValueOrDefault(ClearScope.Distributions),
                    счётчики.GetValueOrDefault(ClearScope.SentMails)),
                SharedJson.Options);
        });
    }

    /// <summary>
    /// Подключает административные маршруты: выдача кода сопряжения, список
    /// устройств и отзыв доступа.
    /// </summary>
    /// <remarks>
    /// Служебные запросы принимаются только с локального адреса и только с
    /// административным токеном: код сопряжения нельзя получить удалённым
    /// запросом.
    /// </remarks>
    /// <param name="приложение">Веб-приложение службы.</param>
    /// <param name="контекст">Служебное состояние службы.</param>
    private static void ПодключитьАдминистративные(WebApplication приложение, SharedServiceContext контекст)
    {
        приложение.MapPost("/api/v1/admin/pairing-code", async (
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!ПроверитьАдминистратора(контекст, http))
            {
                return Ошибка(
                    SharedErrorCodes.Unauthorized,
                    "Служебный запрос отклонён: требуется локальный адрес и административный токен.",
                    http);
            }

            // Код состоит из фрагмента отпечатка сервера и одноразового
            // секрета: клиент сверяет фрагмент с сертификатом сервера и
            // отправляет секрет только настоящему серверу.
            var фрагмент = SharedSecrets.ПолучитьФрагментОтпечатка(контекст.ОтпечатокСервера);

            if (фрагмент.Length < SharedSecrets.ДлинаФрагмента)
            {
                контекст.Журнал(
                    "Сопряжение невозможно: у службы не задан сертификат сервера.");

                return Ошибка(
                    SharedErrorCodes.ServiceUnavailable,
                    "Служба не настроена: не задан сертификат сервера. Обратитесь к администратору.",
                    http);
            }

            var код = SharedSecrets.СоздатьКодСопряжения(фрагмент);
            var (полученныйФрагмент, секрет) = SharedSecrets.РазделитьКодСопряжения(код);
            var истечение = DateTime.UtcNow.Add(SharedSecrets.СрокКодаСопряжения);

            var сессия = контекст.База.СоздатьСессию();
            await using var освобождение = сессия.ConfigureAwait(false);
            await сессия.BeginTransactionAsync(токен).ConfigureAwait(false);

            try
            {
                await new SharedPairingStore(сессия)
                    .СоздатьAsync(
                        Guid.NewGuid().ToString("D"),
                        SharedSecrets.ВычислитьОтпечатокСекретаКода(секрет),
                        полученныйФрагмент,
                        истечение,
                        токен)
                    .ConfigureAwait(false);

                await сессия.CommitAsync(токен).ConfigureAwait(false);
            }
            catch
            {
                await сессия.RollbackAsync(токен).ConfigureAwait(false);
                throw;
            }

            контекст.Журнал("Выдан одноразовый код сопряжения.");

            return Results.Json(
                new AdminPairingCodeResponse(
                    код,
                    истечение,
                    контекст.ОтпечатокСервера),
                SharedJson.Options);
        });

        приложение.MapGet("/api/v1/admin/devices", async (HttpContext http, CancellationToken токен) =>
        {
            if (!ПроверитьАдминистратора(контекст, http))
            {
                return Ошибка(
                    SharedErrorCodes.Unauthorized,
                    "Служебный запрос отклонён: требуется локальный адрес и административный токен.",
                    http);
            }

            var устройства = await ПеречислитьУстройстваAsync(контекст, токен).ConfigureAwait(false);

            return Results.Json(
                устройства
                    .Select(з => new DeviceInfoResponse(
                        з.DeviceId,
                        з.Name,
                        з.CreatedAt,
                        з.LastSeenAt,
                        з.RevokedAt,
                        з.RevokedReason))
                    .ToArray(),
                SharedJson.Options);
        });

        приложение.MapPost("/api/v1/admin/devices/{deviceId}/revoke", async (
            string deviceId,
            HttpContext http,
            CancellationToken токен) =>
        {
            if (!ПроверитьАдминистратора(контекст, http))
            {
                return Ошибка(
                    SharedErrorCodes.Unauthorized,
                    "Служебный запрос отклонён: требуется локальный адрес и административный токен.",
                    http);
            }

            var сессия = контекст.База.СоздатьСессию();
            await using var освобождение = сессия.ConfigureAwait(false);
            var устройства = new SharedDeviceStore(сессия);

            var отозвано = await устройства
                .ОтозватьAsync(deviceId, "Отозван администратором", токен)
                .ConfigureAwait(false);

            if (отозвано)
            {
                контекст.Журнал($"Отозван доступ устройства {deviceId}.");
            }

            return Results.Json(new RevokeDeviceResponse(deviceId, отозвано), SharedJson.Options);
        });
    }

    /// <summary>
    /// Проверяет, что клиент объявил поддерживаемую версию протокола.
    /// </summary>
    /// <param name="http">Контекст запроса.</param>
    /// <returns><see langword="true"/>, если версия поддерживается.</returns>
    internal static bool ВерсияПоддерживается(HttpContext http)
    {
        var версия = http.Request.Headers[SharedProtocol.ProtocolVersionHeader].ToString();

        return int.TryParse(версия, out var разобранная)
            && SharedProtocol.IsSupported(разобранная);
    }

    /// <summary>
    /// Опознаёт устройство-клиента по заголовку авторизации.
    /// </summary>
    /// <param name="контекст">Служебное состояние службы.</param>
    /// <param name="http">Контекст запроса.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Сведения об устройстве либо <see langword="null"/>.</returns>
    internal static async Task<SharedDevice?> ОпознатьУстройствоAsync(
        SharedServiceContext контекст,
        HttpContext http,
        CancellationToken токен)
    {
        var заголовок = http.Request.Headers.Authorization.ToString();

        return await контекст.Опознаватель.ОпознатьAsync(заголовок, токен).ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет служебный запрос: локальный адрес и административный токен.
    /// </summary>
    /// <param name="контекст">Служебное состояние службы.</param>
    /// <param name="http">Контекст запроса.</param>
    /// <returns><see langword="true"/>, если запрос служебный и допустимый.</returns>
    private static bool ПроверитьАдминистратора(SharedServiceContext контекст, HttpContext http)
    {
        var адрес = http.Connection.RemoteIpAddress;

        if (адрес is null || !System.Net.IPAddress.IsLoopback(адрес))
        {
            return false;
        }

        var заголовок = http.Request.Headers["X-Picket302-Admin-Token"].ToString();

        return контекст.АдминТокен.Проверить(заголовок);
    }

    /// <summary>
    /// Подсчитывает сопряжённые устройства, не читая строки устройств.
    /// </summary>
    /// <param name="контекст">Служебное состояние службы.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Число сопряжённых устройств.</returns>
    private static async Task<int> ПодсчитатьУстройстваAsync(
        SharedServiceContext контекст,
        CancellationToken токен)
    {
        var сессия = контекст.База.СоздатьСессию();

        await using var освобождение = сессия.ConfigureAwait(false);

        return await new SharedDeviceStore(сессия)
            .ПодсчитатьУстройстваAsync(токен)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Перечисляет сопряжённые устройства.
    /// </summary>
    /// <param name="контекст">Служебное состояние службы.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Список устройств.</returns>
    private static async Task<IReadOnlyList<SharedDevice>> ПеречислитьУстройстваAsync(
        SharedServiceContext контекст,
        CancellationToken токен)
    {
        var сессия = контекст.База.СоздатьСессию();
        await using var освобождение = сессия.ConfigureAwait(false);

        return await new SharedDeviceStore(сессия).ПеречислитьAsync(токен).ConfigureAwait(false);
    }

    /// <summary>
    /// Читает тело запроса с ограничением размера.
    /// </summary>
    /// <param name="http">Контекст запроса.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Тело запроса либо <see langword="null"/>, если размер превышен.</returns>
    private static async Task<string?> ПрочитатьТелоAsync(HttpContext http, CancellationToken токен)
    {
        var ограничение = new MemoryStream();

        var буфер = new byte[8192];
        var прочитано = 0;

        while (true)
        {
            var часть = await http.Request.Body.ReadAsync(буфер, токен).ConfigureAwait(false);

            if (часть == 0)
            {
                break;
            }

            прочитано += часть;

            if (прочитано > SharedProtocol.MaxRequestBodyBytes)
            {
                return null;
            }

            await ограничение.WriteAsync(буфер.AsMemory(0, часть), токен).ConfigureAwait(false);
        }

        return System.Text.Encoding.UTF8.GetString(ограничение.ToArray());
    }

    /// <summary>
    /// Читает целочисленный параметр запроса.
    /// </summary>
    /// <param name="http">Контекст запроса.</param>
    /// <param name="имя">Имя параметра.</param>
    /// <param name="поУмолчанию">Значение по умолчанию.</param>
    /// <returns>Значение параметра.</returns>
    internal static int ЧитатьЦелое(HttpContext http, string имя, int поУмолчанию)
    {
        var значение = http.Request.Query[имя].ToString();

        return int.TryParse(значение, out var результат) ? результат : поУмолчанию;
    }

    /// <summary>
    /// Читает длинное целое значение параметра запроса.
    /// </summary>
    /// <param name="http">Контекст запроса.</param>
    /// <param name="имя">Имя параметра.</param>
    /// <returns>Значение параметра; 0 — параметр не задан или не разобран.</returns>
    internal static long ПрочитатьДлинное(HttpContext http, string имя)
    {
        var значение = http.Request.Query[имя].ToString();

        return long.TryParse(значение, out var результат) ? результат : 0;
    }

    /// <summary>
    /// Формирует ответ с отказом в доступе.
    /// </summary>
    /// <param name="http">Контекст запроса.</param>
    /// <returns>Результат ответа.</returns>
    internal static IResult ОшибкаОтказа(HttpContext http)
        => Ошибка(
            SharedErrorCodes.Unauthorized,
            "Устройство не сопряжено или доступ отозван.",
            http);

    /// <summary>
    /// Читает фильтр выборки контрагентов из строки запроса.
    /// </summary>
    /// <param name="http">Контекст запроса.</param>
    /// <returns>Фильтр выборки.</returns>
    private static CounterpartyFilterDto ЧитатьФильтр(HttpContext http)
    {
        var значение = http.Request.Query["filter"].ToString();

        if (string.IsNullOrWhiteSpace(значение))
        {
            return new CounterpartyFilterDto();
        }

        try
        {
            return JsonSerializer.Deserialize<CounterpartyFilterDto>(значение, SharedJson.Options)
                ?? new CounterpartyFilterDto();
        }
        catch (JsonException)
        {
            return new CounterpartyFilterDto();
        }
    }

    /// <summary>
    /// Формирует ответ с ошибкой протокола.
    /// </summary>
    /// <param name="код">Код ошибки.</param>
    /// <param name="сообщение">Сообщение оператору.</param>
    /// <param name="http">Контекст запроса.</param>
    /// <returns>Результат ответа.</returns>
    private static IResult Ошибка(string код, string сообщение, HttpContext http)
    {
        http.Response.StatusCode = код switch
        {
            SharedErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
            SharedErrorCodes.VersionIncompatible => StatusCodes.Status426UpgradeRequired,
            SharedErrorCodes.NotFound => StatusCodes.Status404NotFound,
            SharedErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
            SharedErrorCodes.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Json(new SharedError(код, сообщение), SharedJson.Options);
    }
}
