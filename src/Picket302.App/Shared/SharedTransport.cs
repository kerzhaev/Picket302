using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;

namespace Picket302.App.Shared;

/// <summary>
/// Ошибка совместного режима, понятная оператору.
/// </summary>
/// <remarks>
/// <para>
/// Ошибка различает отказ службы (недоступна, отозван доступ, конфликт
/// версий) и внутренние подробности, которые в журнал попадают только на
/// сервере.
/// </para>
/// <para>
/// Ошибка <b>не</b> приводит к переходу на местную базу: недоступный сервер
/// остаётся недоступным, данные оператора не подменяются.
/// </para>
/// </remarks>
public class SharedModeException : Exception
{
    /// <summary>
    /// Создаёт ошибку совместного режима.
    /// </summary>
    /// <param name="код">Код ошибки протокола.</param>
    /// <param name="сообщение">Сообщение оператору.</param>
    public SharedModeException(string код, string сообщение)
        : base(сообщение)
    {
        Код = код;
    }

    /// <summary>Код ошибки протокола.</summary>
    public string Код { get; }

    /// <summary>
    /// Признак того, что сервер недоступен и повтор возможен позже.
    /// </summary>
    public bool СерверНедоступен =>
        Код == SharedErrorCodes.ServiceUnavailable
        || Код == "network-unreachable";
}

/// <summary>
/// Исход операции неизвестен: запрос был отправлен, ответ не получен.
/// </summary>
/// <remarks>
/// <para>
/// Для мутаций это отдельный класс ошибок: операция <b>могла</b> быть
/// выполнена на сервере, и утверждать обратное нельзя. Повтор с тем же
/// идентификатором операции безопасен — служба вернёт прежний результат и не
/// выполнит команду второй раз; повтор с новым идентификатором создал бы
/// дубль.
/// </para>
/// <para>
/// Для запросов чтения исход на данные не влияет: сообщение говорит только
/// о недоступности.
/// </para>
/// </remarks>
public sealed class SharedOutcomeUnknownException : SharedModeException
{
    /// <summary>
    /// Создаёт ошибку неизвестного исхода.
    /// </summary>
    /// <param name="сообщение">Сообщение оператору.</param>
    public SharedOutcomeUnknownException(string сообщение)
        : base(SharedErrorCodes.OutcomeUnknown, сообщение)
    {
    }
}

/// <summary>
/// Транспорт клиента совместного режима: защищённые запросы к серверной
/// службе.
/// </summary>
/// <remarks>
/// <para>
/// Соединение защищено: сертификат сервера проверяется не по цепочке
/// доверия (сертификат самоподписанный), а по закреплённому отпечатку
/// открытого ключа. Проверка отключается только на время сопряжения — и даже
/// там она не отключается: сверяется фрагмент отпечатка из кода сопряжения.
/// </para>
/// <para>
/// Сбой сети различается честно: если соединение не установлено, запрос не
/// был доставлен и данные на сервере не изменялись; если запрос ушёл, а
/// ответ потерян, исход мутации неизвестен — об этом сообщается отдельной
/// ошибкой, и повтор выполняется с прежним идентификатором операции.
/// </para>
/// </remarks>
public sealed class SharedTransport : IDisposable
{
    private HttpClient _клиент;
    private readonly Action<string> _журнал;

    /// <summary>
    /// Создаёт транспорт для подключения к службе.
    /// </summary>
    /// <param name="реквизиты">Реквизиты подключения.</param>
    /// <param name="секрет">Секрет устройства.</param>
    /// <param name="журнал">Приёмник записей журнала клиента.</param>
    public SharedTransport(
        SharedClientSettings реквизиты,
        string секрет,
        Action<string>? журнал = null)
    {
        ПроверитьРеквизиты(реквизиты, секрет);

        _журнал = журнал ?? (_ => { });

        _клиент = СоздатьКлиент(реквизиты);
        НазначитьЗаголовки(_клиент, секрет);
    }

    /// <summary>
    /// Создаёт транспорт поверх готового обработчика HTTP: используется в
    /// проверках, где ответ теряется управляемо. Проверка сертификата не
    /// отключается — обработчик оборачивает штатный обработчик с
    /// закреплением отпечатка.
    /// </summary>
    /// <param name="реквизиты">Реквизиты подключения.</param>
    /// <param name="секрет">Секрет устройства.</param>
    /// <param name="обработчик">Готовый обработчик HTTP.</param>
    /// <param name="журнал">Приёмник записей журнала клиента.</param>
    internal SharedTransport(
        SharedClientSettings реквизиты,
        string секрет,
        HttpMessageHandler обработчик,
        Action<string>? журнал = null)
    {
        ПроверитьРеквизиты(реквизиты, секрет);
        ArgumentNullException.ThrowIfNull(обработчик);

        _журнал = журнал ?? (_ => { });

        _клиент = new HttpClient(обработчик)
        {
            BaseAddress = БазовыйАдрес(реквизиты),
            Timeout = TimeSpan.FromSeconds(60),
        };

        НазначитьЗаголовки(_клиент, секрет);
    }

    /// <summary>
    /// Строит базовый адрес службы из реквизитов подключения.
    /// </summary>
    /// <param name="реквизиты">Реквизиты подключения.</param>
    /// <returns>Базовый адрес HTTPS.</returns>
    private static Uri БазовыйАдрес(SharedClientSettings реквизиты)
    {
        var хост = реквизиты.Host.Trim();

        if (хост.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            хост = хост["https://".Length..];
        }
        else if (хост.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            хост = хост["http://".Length..];
        }

        хост = хост.TrimEnd('/');

        return new Uri($"https://{хост}:{реквизиты.Port}/");
    }

    /// <summary>
    /// Создаёт транспорт для сопряжения: сертификат сервера сверяется с
    /// фрагментом отпечатка из кода сопряжения.
    /// </summary>
    /// <param name="адрес">Адрес службы.</param>
    /// <param name="фрагмент">Фрагмент отпечатка сервера из кода сопряжения.</param>
    /// <param name="журнал">Приёмник записей журнала клиента.</param>
    /// <returns>Готовый транспорт сопряжения.</returns>
    public static SharedTransport СоздатьДляСопряжения(
        SharedClientSettings адрес,
        string фрагмент,
        Action<string>? журнал = null)
    {
        ArgumentNullException.ThrowIfNull(адрес);

        if (string.IsNullOrWhiteSpace(фрагмент))
        {
            throw new ArgumentException(
                "Код сопряжения не содержит фрагмента отпечатка сервера.",
                nameof(фрагмент));
        }

        var транспорт = new SharedTransport(
            new SharedClientSettings(
                адрес.Host,
                адрес.Port,
                string.Empty,
                string.Empty,
                фрагмент),
            // Заголовок Authorization должен состоять только из ASCII:
            // HttpClient отклоняет не-ASCII значения заголовков, и запрос
            // сопряжения с таким значением не уходит вовсе.
            "pairing",
            журнал);

        return транспорт;
    }

    /// <summary>
    /// Выполняет команду службы с заданным телом запроса.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Тело передаётся в виде готового JSON: повтор той же операции
    /// отправляет те же байты. Служба хранит отпечаток содержимого и
    /// отклоняет повтор под тем же идентификатором с другим телом.
    /// </para>
    /// </remarks>
    /// <typeparam name="TОтвет">Тип ответа.</typeparam>
    /// <param name="имяКоманды">Имя команды.</param>
    /// <param name="телоJson">Тело запроса в виде JSON.</param>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ команды.</returns>
    public Task<TОтвет> ВыполнитьКомандуJsonAsync<TОтвет>(
        string имяКоманды,
        string телоJson,
        string operationId,
        CancellationToken cancellationToken = default)
        => РазобратьКомандуAsync<TОтвет>(
            ВыполнитьКомандуТекстомAsync(имяКоманды, телоJson, operationId, cancellationToken));

    /// <summary>
    /// Выполняет команду службы и возвращает тело ответа без разбора.
    /// </summary>
    /// <remarks>
    /// Повтор операции не знает тип ответа команды: хранилище незавершённых
    /// операций возвращает тело как есть, повторивший — разбирает сам.
    /// </remarks>
    /// <param name="имяКоманды">Имя команды.</param>
    /// <param name="телоJson">Тело запроса в виде JSON.</param>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Тело ответа в виде JSON.</returns>
    public async Task<string> ВыполнитьКомандуТекстомAsync(
        string имяКоманды,
        string телоJson,
        string operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяКоманды);
        ArgumentException.ThrowIfNullOrWhiteSpace(телоJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        using var сообщение = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SharedProtocol.RoutePrefix}/commands/{имяКоманды}");

        сообщение.Headers.Add(SharedProtocol.OperationIdHeader, operationId);
        сообщение.Content = new StringContent(телоJson, Encoding.UTF8, "application/json");

        return await ОтправитьAsync(сообщение, cancellationToken, команда: true)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Разбирает тело ответа команды после его получения.
    /// </summary>
    /// <typeparam name="TОтвет">Тип ответа.</typeparam>
    /// <param name="получение">Задача получения тела ответа.</param>
    /// <returns>Разобранный ответ.</returns>
    private static async Task<TОтвет> РазобратьКомандуAsync<TОтвет>(Task<string> получение)
    {
        return Разобрать<TОтвет>(await получение.ConfigureAwait(false));
    }

    /// <summary>
    /// Выполняет запрос чтения данных.
    /// </summary>
    /// <typeparam name="TОтвет">Тип ответа.</typeparam>
    /// <param name="маршрут">Маршрут запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответ запроса.</returns>
    public async Task<TОтвет> ВыполнитьЗапросAsync<TОтвет>(
        string маршрут,
        CancellationToken cancellationToken = default)
    {
        using var сообщение = new HttpRequestMessage(HttpMethod.Get, маршрут);

        var текст = await ОтправитьAsync(сообщение, cancellationToken, команда: false)
            .ConfigureAwait(false);

        return Разобрать<TОтвет>(текст);
    }

    /// <summary>
    /// Скачивает содержимое версии пакета шаблона.
    /// </summary>
    /// <param name="packageId">Идентификатор пакета.</param>
    /// <param name="version">Версия пакета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Содержимое пакета.</returns>
    public async Task<byte[]> СкачатьПакетШаблонаAsync(
        string packageId,
        long version,
        CancellationToken cancellationToken = default)
    {
        using var сообщение = new HttpRequestMessage(
            HttpMethod.Get,
            $"{SharedProtocol.TemplatePackagesRoute}/{Uri.EscapeDataString(packageId)}/content?version={version}");

        HttpResponseMessage ответ;

        try
        {
            ответ = await _клиент.SendAsync(сообщение, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ошибка)
        {
            _журнал($"Общая база недоступна: {ошибка.Message}");

            throw new SharedModeException(
                "network-unreachable",
                "Общая база недоступна. Проверьте, что серверная служба запущена и что компьютер "
                + "с ней доступен по сети. Данные на этом компьютере не изменялись.");
        }

        using (ответ)
        {
            if (!ответ.IsSuccessStatusCode)
            {
                var текст = await ответ.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                throw ПреобразоватьОшибку(ответ.StatusCode, текст);
            }

            return await ответ.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Выгружает содержимое пакета шаблона на сервер.
    /// </summary>
    /// <param name="name">Наименование пакета.</param>
    /// <param name="содержимое">Содержимое .lbk-файла.</param>
    /// <param name="operationId">
    /// Идентификатор операции; пусто — создаётся новый. Повтор выгрузки
    /// выполняется с прежним идентификатором.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Метаданные сохранённой версии.</returns>
    public async Task<TemplatePackageDto> ВыгрузитьПакетШаблонаAsync(
        string name,
        byte[] содержимое,
        string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(содержимое);

        var тело = JsonSerializer.Serialize(
            new UploadTemplatePackageRequest(name, Convert.ToBase64String(содержимое)),
            SharedJson.Options);

        using var сообщение = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SharedProtocol.RoutePrefix}/commands/templates.upload-package");

        сообщение.Headers.Add(
            SharedProtocol.OperationIdHeader,
            string.IsNullOrWhiteSpace(operationId)
                ? Guid.NewGuid().ToString("D")
                : operationId);

        сообщение.Content = new StringContent(тело, Encoding.UTF8, "application/json");

        var текст = await ОтправитьAsync(сообщение, cancellationToken, команда: true)
            .ConfigureAwait(false);

        var разобранный = JsonSerializer.Deserialize<TemplatePackageDto>(текст, SharedJson.Options);

        return разобранный ?? throw new SharedModeException(
            SharedErrorCodes.Internal,
            "Общая база вернула ответ, который не удалось прочитать.");
    }

    /// <summary>
    /// Сопрягает устройство со службой.
    /// </summary>
    /// <param name="код">Код сопряжения, показанный администратором.</param>
    /// <param name="имяУстройства">Имя устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат сопряжения.</returns>
    public async Task<PairResponse> СопрячьAsync(
        string код,
        string имяУстройства,
        CancellationToken cancellationToken = default)
    {
        using var сообщение = new HttpRequestMessage(HttpMethod.Post, SharedProtocol.PairRoute);

        сообщение.Content = new StringContent(
            JsonSerializer.Serialize(new PairRequest(код, имяУстройства), SharedJson.Options),
            Encoding.UTF8,
            "application/json");

        var текст = await ОтправитьAsync(сообщение, cancellationToken, команда: false)
            .ConfigureAwait(false);

        return Разобрать<PairResponse>(текст);
    }

    /// <summary>
    /// Отправляет запрос и возвращает тело ответа.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Сбой сети классифицируется честно. Соединение не установлено —
    /// HTTP-запрос не отправлялся, и данные на сервере не изменялись.
    /// Ответ потерян (таймаут, разрыв после отправки, обрыв чтения) — исход
    /// мутации неизвестен, что сообщается отдельной ошибкой.
    /// </para>
    /// </remarks>
    /// <param name="сообщение">Запрос.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <param name="команда">
    /// Признак мутации: для мутаций потерянный ответ означает неизвестный
    /// исход, для запросов чтения — только недоступность.
    /// </param>
    /// <returns>Тело ответа.</returns>
    private async Task<string> ОтправитьAsync(
        HttpRequestMessage сообщение,
        CancellationToken токен,
        bool команда)
    {
        HttpResponseMessage? ответ = null;

        try
        {
            ответ = await _клиент.SendAsync(сообщение, токен).ConfigureAwait(false);

            var текст = await ответ.Content.ReadAsStringAsync(токен).ConfigureAwait(false);

            if (ответ.IsSuccessStatusCode)
            {
                return текст;
            }

            throw ПреобразоватьОшибку(ответ.StatusCode, текст);
        }
        catch (SharedModeException)
        {
            throw;
        }
        catch (TaskCanceledException) when (!токен.IsCancellationRequested)
        {
            throw ИсходНеизвестен("превышено время ожидания ответа", команда);
        }
        catch (HttpRequestException ошибка) when (СоединениеНеУстановлено(ошибка))
        {
            _журнал($"Общая база недоступна: {ошибка.Message}");

            throw new SharedModeException(
                "network-unreachable",
                "Общая база недоступна. Проверьте, что серверная служба запущена и что компьютер "
                + "с ней доступен по сети. Запрос не был доставлен серверу: данные на сервере "
                + "не изменялись.");
        }
        catch (HttpRequestException ошибка)
        {
            _журнал($"Ответ общей базы потерян: {ошибка.Message}");

            throw ИсходНеизвестен($"ошибка соединения ({ошибка.Message})", команда);
        }
        catch (IOException)
        {
            // Обрыв при чтении ответа: запрос уже был отправлен.
            throw ИсходНеизвестен("связь оборвалась при получении ответа", команда);
        }
        finally
        {
            ответ?.Dispose();
        }
    }

    /// <summary>
    /// Строит ошибку неизвестного исхода.
    /// </summary>
    /// <param name="причина">Причина для журнала и сообщения.</param>
    /// <param name="команда">Признак мутации.</param>
    /// <returns>Готовая ошибка.</returns>
    private static SharedOutcomeUnknownException ИсходНеизвестен(string причина, bool команда)
    {
        if (!команда)
        {
            // Для запросов чтения исход на данные не влияет.
            return new SharedOutcomeUnknownException(
                "Общая база не ответила: " + причина + ". Данные не изменялись.");
        }

        return new SharedOutcomeUnknownException(
            "Исход операции неизвестен: " + причина + ". Запрос был отправлен, ответ не "
            + "получен — операция могла быть выполнена на сервере. Не создавайте запись "
            + "заново: откройте «Сервис → Незавершённые операции» и повторите действие — "
            + "команда будет отправлена с прежним идентификатором, дубль не возникнет.");
    }

    /// <summary>
    /// Определяет, что соединение не было установлено: HTTP-запрос не
    /// отправлялся.
    /// </summary>
    /// <remarks>
    /// Отказ TCP-соединения и ошибка рукопожатия TLS происходят до отправки
    /// HTTP-запроса, поэтому команда не могла дойти до службы. Иные ошибки
    /// классифицируются консервативно: исход считается неизвестным.
    /// </remarks>
    /// <param name="ошибка">Исходная ошибка.</param>
    /// <returns>
    /// <see langword="true"/>, если соединение не установлено.
    /// </returns>
    private static bool СоединениеНеУстановлено(Exception ошибка)
    {
        for (var текущая = (Exception?)ошибка; текущая is not null; текущая = текущая.InnerException)
        {
            if (текущая is SocketException or AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Преобразует ответ службы в ошибку для оператора.
    /// </summary>
    /// <param name="состояние">Код состояния HTTP.</param>
    /// <param name="текст">Тело ответа.</param>
    /// <returns>Готовая ошибка.</returns>
    private static SharedModeException ПреобразоватьОшибку(HttpStatusCode состояние, string текст)
    {
        SharedError? ошибка = null;

        try
        {
            ошибка = JsonSerializer.Deserialize<SharedError>(текст, SharedJson.Options);
        }
        catch (JsonException)
        {
            // Тело не является сообщением протокола: сообщается общий отказ.
        }

        if (ошибка is null)
        {
            var код = состояние switch
            {
                HttpStatusCode.Unauthorized => SharedErrorCodes.Unauthorized,
                HttpStatusCode.Conflict => SharedErrorCodes.Conflict,
                HttpStatusCode.NotFound => SharedErrorCodes.NotFound,
                HttpStatusCode.BadRequest => SharedErrorCodes.ValidationFailed,
                HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable =>
                    SharedErrorCodes.ServiceUnavailable,
                _ => SharedErrorCodes.Internal,
            };

            return new SharedModeException(
                код,
                состояние == HttpStatusCode.Unauthorized
                    ? "Общая база отклонила устройство: доступ отозван или устройство не сопряжено."
                    : $"Общая база вернула отказ ({(int)состояние}).");
        }

        return new SharedModeException(ошибка.Code, ошибка.Message);
    }

    /// <summary>
    /// Разбирает тело ответа.
    /// </summary>
    /// <typeparam name="TОтвет">Тип ответа.</typeparam>
    /// <param name="текст">Тело ответа.</param>
    /// <returns>Разобранный ответ.</returns>
    private static TОтвет Разобрать<TОтвет>(string текст)
    {
        var результат = JsonSerializer.Deserialize<TОтвет>(текст, SharedJson.Options);

        if (результат is null)
        {
            throw new SharedModeException(
                SharedErrorCodes.Internal,
                "Общая база вернула ответ, который не удалось прочитать.");
        }

        return результат;
    }

    /// <summary>
    /// Проверяет реквизиты подключения.
    /// </summary>
    /// <param name="реквизиты">Реквизиты подключения.</param>
    /// <param name="секрет">Секрет устройства.</param>
    private static void ПроверитьРеквизиты(SharedClientSettings реквизиты, string секрет)
    {
        ArgumentNullException.ThrowIfNull(реквизиты);
        ArgumentException.ThrowIfNullOrWhiteSpace(секрет);

        if (string.IsNullOrWhiteSpace(реквизиты.ServerFingerprint))
        {
            throw new ArgumentException(
                "Не задан закреплённый отпечаток сервера: подключение без проверки сертификата запрещено.",
                nameof(реквизиты));
        }
    }

    /// <summary>
    /// Назначает клиенту заголовки аутентификации и версии протокола.
    /// </summary>
    /// <param name="клиент">HTTP-клиент.</param>
    /// <param name="секрет">Секрет устройства.</param>
    private static void НазначитьЗаголовки(HttpClient клиент, string секрет)
    {
        клиент.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", секрет);
        клиент.DefaultRequestHeaders.Add(
            SharedProtocol.ProtocolVersionHeader,
            SharedProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Создаёт HTTP-клиент с базовым адресом службы, принимающий только
    /// сертификат с заданным отпечатком открытого ключа.
    /// </summary>
    /// <param name="реквизиты">Реквизиты подключения.</param>
    /// <returns>Готовый HTTP-клиент.</returns>
    private static HttpClient СоздатьКлиент(SharedClientSettings реквизиты)
        => new(СоздатьОбработчикОтпечатка(реквизиты.ServerFingerprint))
        {
            BaseAddress = БазовыйАдрес(реквизиты),
            Timeout = TimeSpan.FromSeconds(60),
        };

    /// <summary>
    /// Создаёт обработчик HTTP с закреплением отпечатка: для проверок,
    /// оборачивающих штатный обработчик (управляемая потеря ответа и
    /// подобные сценарии). Проверка сертификата не отключается.
    /// </summary>
    /// <param name="отпечаток">
    /// Ожидаемый отпечаток. Пусто — сверка ведётся по фрагменту кода
    /// сопряжения (см. <see cref="СоздатьДляСопряжения"/>).
    /// </param>
    /// <returns>Обработчик с проверкой сертификата.</returns>
    internal static HttpClientHandler СоздатьОбработчикОтпечатка(string отпечаток)
    {
        var ожидаемый = отпечаток?.Trim() ?? string.Empty;

        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, сертификат, _, _) =>
            {
                if (сертификат is null)
                {
                    return false;
                }

                var вычисленный = SharedFingerprint.ВычислитьОтпечаток(сертификат);

                if (ожидаемый.Length == SharedFingerprint.ДлинаФрагмента)
                {
                    // Сопряжение: сверяется фрагмент отпечатка из кода,
                    // показанного администратором.
                    return string.Equals(
                        SharedFingerprint.ПолучитьФрагмент(вычисленный),
                        ожидаемый,
                        StringComparison.OrdinalIgnoreCase);
                }

                return string.Equals(вычисленный, ожидаемый, StringComparison.OrdinalIgnoreCase);
            },
        };
    }

    /// <inheritdoc />
    public void Dispose() => _клиент.Dispose();
}
