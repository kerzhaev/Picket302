using System.Net;
using System.Net.Http.Headers;
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
public sealed class SharedModeException : Exception
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
/// Тело запроса для идентификатора операции сохраняется: повтор той же
/// операции отправляет те же байты, иначе служба справедливо сочтёт это
/// другой операцией.
/// </para>
/// </remarks>
public sealed class SharedTransport : IDisposable
{
    private HttpClient _клиент;
    private readonly Dictionary<string, string> _телаОпераций = new(StringComparer.Ordinal);
    private readonly Action<string> _журнал;

    /// <summary>Предельный размер кэша тел операций.</summary>
    public const int ПредельныйКэшТелОпераций = 64;

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
        ArgumentNullException.ThrowIfNull(реквизиты);
        ArgumentException.ThrowIfNullOrWhiteSpace(секрет);

        if (string.IsNullOrWhiteSpace(реквизиты.ServerFingerprint))
        {
            throw new ArgumentException(
                "Не задан закреплённый отпечаток сервера: подключение без проверки сертификата запрещено.",
                nameof(реквизиты));
        }

        _журнал = журнал ?? (_ => { });

        _клиент = СоздатьКлиент(реквизиты.ServerFingerprint);

        _клиент.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", секрет);
        _клиент.DefaultRequestHeaders.Add(
            SharedProtocol.ProtocolVersionHeader,
            SharedProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
            "сопряжение",
            журнал);

        return транспорт;
    }

    /// <summary>
    /// Выполняет команду службы.
    /// </summary>
    /// <typeparam name="TЗапрос">Тип запроса.</typeparam>
    /// <typeparam name="TОтвет">Тип ответа.</typeparam>
    /// <param name="имяКоманды">Имя команды.</param>
    /// <param name="запрос">Запрос.</param>
    /// <param name="operationId">
    /// Идентификатор операции. Повтор с тем же идентификатором отправляет те
    /// же байты и возвращает прежний результат.
    /// </param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Ответ команды.</returns>
    public async Task<TОтвет> ВыполнитьКомандуAsync<TЗапрос, TОтвет>(
        string имяКоманды,
        TЗапрос запрос,
        string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        var идентификатор = operationId ?? Guid.NewGuid().ToString("D");

        if (!_телаОпераций.TryGetValue(идентификатор, out var тело))
        {
            тело = JsonSerializer.Serialize(запрос, SharedJson.Options);

            // Кэш тел ограничен: он нужен только для повтора недавней
            // операции, чей ответ ещё не дошёл до клиента.
            while (_телаОпераций.Count >= ПредельныйКэшТелОпераций)
            {
                _телаОпераций.Remove(_телаОпераций.Keys.First());
            }

            _телаОпераций[идентификатор] = тело;
        }

        using var сообщение = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SharedProtocol.RoutePrefix}/commands/{имяКоманды}");

        сообщение.Headers.Add(SharedProtocol.OperationIdHeader, идентификатор);

        сообщение.Content = new StringContent(тело, Encoding.UTF8, "application/json");

        var текст = await ОтправитьAsync(сообщение, cancellationToken).ConfigureAwait(false);

        return Разобрать<TОтвет>(текст);
    }

    /// <summary>
    /// Выполняет запрос чтения данных.
    /// </summary>
    /// <typeparam name="TОтвет">Тип ответа.</typeparam>
    /// <param name="маршрут">Маршрут запроса.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Ответ запроса.</returns>
    public async Task<TОтвет> ВыполнитьЗапросAsync<TОтвет>(
        string маршрут,
        CancellationToken cancellationToken = default)
    {
        using var сообщение = new HttpRequestMessage(HttpMethod.Get, маршрут);

        var текст = await ОтправитьAsync(сообщение, cancellationToken).ConfigureAwait(false);

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
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Метаданные сохранённой версии.</returns>
    public async Task<TemplatePackageDto> ВыгрузитьПакетШаблонаAsync(
        string name,
        byte[] содержимое,
        CancellationToken cancellationToken = default)
    {
        var тело = JsonSerializer.Serialize(
            new UploadTemplatePackageRequest(name, Convert.ToBase64String(содержимое)),
            SharedJson.Options);

        using var сообщение = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SharedProtocol.RoutePrefix}/commands/templates.upload-package");

        сообщение.Headers.Add(SharedProtocol.OperationIdHeader, Guid.NewGuid().ToString("D"));
        сообщение.Content = new StringContent(тело, System.Text.Encoding.UTF8, "application/json");

        var текст = await ОтправитьAsync(сообщение, cancellationToken).ConfigureAwait(false);

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
    /// <param name="токен">Токен отмены.</param>
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

        var текст = await ОтправитьAsync(сообщение, cancellationToken).ConfigureAwait(false);

        return Разобрать<PairResponse>(текст);
    }

    /// <summary>
    /// Отправляет запрос и возвращает тело ответа.
    /// </summary>
    /// <param name="сообщение">Запрос.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Тело ответа.</returns>
    private async Task<string> ОтправитьAsync(HttpRequestMessage сообщение, CancellationToken токен)
    {
        HttpResponseMessage ответ;

        try
        {
            ответ = await _клиент.SendAsync(сообщение, токен).ConfigureAwait(false);
        }
        catch (HttpRequestException ошибка)
        {
            _журнал($"Общая база недоступна: {ошибка.Message}");

            throw new SharedModeException(
                "network-unreachable",
                "Общая база недоступна. Проверьте, что серверная служба запущена и что компьютер "
                + "с ней доступен по сети. Данные на этом компьютере не изменялись.");
        }
        catch (TaskCanceledException) when (!токен.IsCancellationRequested)
        {
            throw new SharedModeException(
                "network-unreachable",
                "Превычено время ожидания ответа общей базы. Повторите действие.");
        }

        using (ответ)
        {
            var текст = await ответ.Content.ReadAsStringAsync(токен).ConfigureAwait(false);

            if (ответ.IsSuccessStatusCode)
            {
                return текст;
            }

            throw ПреобразоватьОшибку(ответ.StatusCode, текст);
        }
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
    /// Создаёт HTTP-клиент, принимающий только сертификат с заданным
    /// отпечатком открытого ключа.
    /// </summary>
    /// <param name="отпечаток">
    /// Ожидаемый отпечаток. Пусто — сверка ведётся по фрагменту кода
    /// сопряжения (см. <see cref="СоздатьДляСопряжения"/>).
    /// </param>
    /// <returns>Готовый HTTP-клиент.</returns>
    private static HttpClient СоздатьКлиент(string отпечаток)
    {
        var ожидаемый = отпечаток?.Trim() ?? string.Empty;

        var обработчик = new HttpClientHandler
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

        return new HttpClient(обработчик)
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    /// <inheritdoc />
    public void Dispose() => _клиент.Dispose();
}
