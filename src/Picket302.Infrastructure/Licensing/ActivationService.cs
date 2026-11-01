using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Domain.Licensing;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Онлайн-активация и офлайн-файл запроса (ТЗ §8.10, функция 122:
/// «Онлайн-активация отправляет ключ + MachineID на сервер активации, получает
/// подписанный ответ (подпись проверяется локально). При отсутствии сети
/// формируется офлайн-файл запроса со строгим именем
/// На_регистрацию_ПК!{uuid}_{date}-{n}-{n}»).
/// </summary>
/// <remarks>
/// <para>
/// Формат офлайн-файла запроса:
/// <c>На_регистрацию_ПК!{uuid}_{yyyyMMdd}-{n}-{n}</c>, где первое число —
/// порядковый номер запроса, второе — номер части (копии) запроса.
/// </para>
/// <para>
/// Ответ активации — файл JSON с полями <c>payload</c> (данные лицензии в
/// кодировке Base64) и <c>signature</c> (подпись в кодировке Base64). Подпись
/// проверяется локально открытым ключом поставщика; неверная подпись означает
/// отказ с сообщением о повреждении ответа.
/// </para>
/// </remarks>
public sealed class ActivationService
{
    /// <summary>Префикс строгого имени файла запроса.</summary>
    public const string ПрефиксИмениФайлаЗапроса = "На_регистрацию_ПК!";

    private static readonly HttpClient Клиент = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    private readonly LicenseStore _хранилище;
    private readonly LicenseStateMachine _автомат;
    private readonly Func<string, string, CancellationToken, Task<string>> _отправитьЗапрос;
    private readonly string? _адресСервера;
    private readonly string _каталогЗапросов;
    private readonly byte[]? _открытыйКлючПоставщика;
    private readonly LicenseSignature _подпись;

    /// <summary>
    /// Создаёт службу активации.
    /// </summary>
    /// <param name="store">Хранилище данных лицензии.</param>
    /// <param name="machine">Автомат состояний лицензии.</param>
    /// <param name="отправитьЗапрос">
    /// Функция сетевого запроса: принимает адрес сервера, тело запроса и токен
    /// отмены, возвращает тело ответа. По умолчанию используется
    /// <see cref="HttpClient"/> с таймаутом; подмена позволяет проверять
    /// активацию без сетевого доступа.
    /// </param>
    /// <param name="адресСервера">
    /// Адрес сервера активации (задаётся в настройках); пустое значение —
    /// сервер не задан, доступен только офлайн-путь.
    /// </param>
    /// <param name="каталогЗапросов">
    /// Каталог для офлайн-файлов запроса; по умолчанию — каталог файла лицензии.
    /// </param>
    /// <param name="открытыйКлючПоставщика">
    /// Открытый ключ поставщика для проверки подписи ответа; без него
    /// активация невозможна.
    /// </param>
    /// <param name="средствоПодписи">Проверяльщик подписи ответа.</param>
    /// <exception cref="ArgumentNullException">Хранилище или автомат не заданы.</exception>
    public ActivationService(
        LicenseStore store,
        LicenseStateMachine machine,
        Func<string, string, CancellationToken, Task<string>>? отправитьЗапрос = null,
        string? адресСервера = null,
        string? каталогЗапросов = null,
        byte[]? открытыйКлючПоставщика = null,
        LicenseSignature? средствоПодписи = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(machine);

        _хранилище = store;
        _автомат = machine;
        _отправитьЗапрос = отправитьЗапрос ?? ОтправитьПоHttpAsync;
        _адресСервера = адресСервера;
        _каталогЗапросов = каталогЗапросов
            ?? Path.GetDirectoryName(store.ПутьФайла)
            ?? Path.GetTempPath();
        _открытыйКлючПоставщика = открытыйКлючПоставщика;
        _подпись = средствоПодписи ?? new LicenseSignature();
    }

    /// <summary>
    /// Ключ активации, введённый оператором; используется при формировании
    /// офлайн-файла запроса.
    /// </summary>
    public string Ключ { get; set; } = string.Empty;

    /// <summary>
    /// Адрес сервера активации; <see langword="null"/> — сервер не задан.
    /// </summary>
    public string? АдресСервера => _адресСервера;

    /// <summary>
    /// Каталог, в котором формируются офлайн-файлы запроса.
    /// </summary>
    public string КаталогЗапросов => _каталогЗапросов;

    /// <summary>
    /// Активирует лицензию через сервер активации
    /// (ТЗ §8.10, функция 122: <c>public ActivationResult ActivateOnline(string key)</c>).
    /// </summary>
    /// <remarks>
    /// Синхронная обёртка над <see cref="ActivateOnlineAsync(string, CancellationToken)"/>.
    /// В оболочке следует вызывать асинхронный вариант: сетевой запрос не
    /// должен выполняться в потоке интерфейса.
    /// </remarks>
    /// <param name="key">Ключ активации.</param>
    /// <returns>Результат активации.</returns>
    public ActivationResult ActivateOnline(string key)
        => ActivateOnlineAsync(key).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <summary>
    /// Активирует лицензию через сервер активации.
    /// </summary>
    /// <param name="key">Ключ активации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат активации.</returns>
    public async Task<ActivationResult> ActivateOnlineAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return ActivationResult.Failure("Ключ активации не задан.");
        }

        Ключ = key;

        if (string.IsNullOrWhiteSpace(_адресСервера))
        {
            return ActivationResult.Failure(
                "Адрес сервера активации не задан. Сформируйте офлайн-файл запроса и передайте его в поддержку.");
        }

        if (_открытыйКлючПоставщика is null)
        {
            return ActivationResult.Failure(
                "Открытый ключ поставщика не настроен: проверка подписи ответа невозможна.");
        }

        var тело = JsonSerializer.Serialize(
            new ЗапросАктивации
            {
                Key = key,
                MachineId = _хранилище.MachineId,
                MachineIdVariant = MachineIdVariant.Composite.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                RequestId = Guid.NewGuid(),
            },
            ПараметрыJson);

        string ответ;

        try
        {
            ответ = await _отправитьЗапрос(_адресСервера, тело, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ActivationResult.Failure(
                "Сервер активации не ответил вовремя. Сформируйте офлайн-файл запроса и передайте его в поддержку.");
        }
        catch (HttpRequestException исключение)
        {
            return ActivationResult.Failure(
                "Сервер активации недоступен: " + исключение.Message
                + " Сформируйте офлайн-файл запроса и передайте его в поддержку.");
        }
        catch (IOException исключение)
        {
            return ActivationResult.Failure(
                "Сервер активации недоступен: " + исключение.Message
                + " Сформируйте офлайн-файл запроса и передайте его в поддержку.");
        }

        return ПрименитьОтвет(ответ);
    }

    /// <summary>
    /// Формирует офлайн-файл запроса на регистрацию
    /// (ТЗ §8.10, функция 122: <c>public string BuildOfflineRequest()</c>).
    /// </summary>
    /// <returns>Путь к созданному файлу запроса.</returns>
    /// <exception cref="InvalidOperationException">Ключ активации не задан.</exception>
    public string BuildOfflineRequest()
    {
        if (string.IsNullOrWhiteSpace(Ключ))
        {
            throw new InvalidOperationException(
                "Ключ активации не задан: введите ключ перед созданием файла запроса.");
        }

        Directory.CreateDirectory(_каталогЗапросов);

        var номер = СледующийНомерЗапроса();
        var имя = BuildOfflineRequestFileName(Guid.NewGuid(), DateTime.Now, номер, 1);
        var путь = Path.Combine(_каталогЗапросов, имя);

        var содержимое = new ЗапросАктивации
        {
            Key = Ключ,
            MachineId = _хранилище.MachineId,
            MachineIdVariant = MachineIdVariant.Composite.ToString(),
            CreatedAtUtc = DateTime.UtcNow,
            RequestId = Guid.NewGuid(),
            FileName = имя,
        };

        System.IO.File.WriteAllText(
            путь,
            JsonSerializer.Serialize(содержимое, ПараметрыJson),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return путь;
    }

    /// <summary>
    /// Применяет ответный файл активации: проверяет подпись локально и
    /// сохраняет лицензию.
    /// </summary>
    /// <param name="responseFilePath">Путь к ответному файлу.</param>
    /// <returns>Результат применения ответа.</returns>
    public ActivationResult ApplyOfflineResponse(string responseFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseFilePath);

        if (!System.IO.File.Exists(responseFilePath))
        {
            return ActivationResult.Failure("Файл ответа активации не найден: " + responseFilePath);
        }

        string содержимое;

        try
        {
            содержимое = System.IO.File.ReadAllText(responseFilePath, Encoding.UTF8);
        }
        catch (Exception исключение) when (исключение is IOException or UnauthorizedAccessException)
        {
            return ActivationResult.Failure("Не удалось прочитать файл ответа активации: " + исключение.Message);
        }

        return ПрименитьОтвет(содержимое);
    }

    /// <summary>
    /// Формирует строгое имя файла запроса на регистрацию
    /// (ТЗ §8.10, функция 122: <c>На_регистрацию_ПК!{uuid}_{date}-{n}-{n}</c>).
    /// </summary>
    /// <param name="uuid">Идентификатор запроса.</param>
    /// <param name="дата">Дата запроса.</param>
    /// <param name="номер">Порядковый номер запроса.</param>
    /// <param name="номер2">Номер части (копии) запроса.</param>
    /// <returns>
    /// Имя файла вида <c>На_регистрацию_ПК!{uuid}_{yyyyMMdd}-{n}-{n}</c>.
    /// </returns>
    public static string BuildOfflineRequestFileName(Guid uuid, DateTime дата, int номер, int номер2)
        => ПрефиксИмениФайлаЗапроса
            + uuid.ToString()
            + "_"
            + дата.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            + "-"
            + номер.ToString(CultureInfo.InvariantCulture)
            + "-"
            + номер2.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Формирует файл ответа активации: используется сервером активации и
    /// проверочными сценариями.
    /// </summary>
    /// <param name="payload">Подписываемые данные лицензии.</param>
    /// <param name="signature">Подпись данных.</param>
    /// <returns>Содержимое файла ответа (JSON).</returns>
    public static string BuildResponseEnvelope(byte[] payload, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signature);

        return JsonSerializer.Serialize(
            new ОтветАктивации
            {
                Payload = Convert.ToBase64String(payload),
                Signature = Convert.ToBase64String(signature),
            },
            ПараметрыJson);
    }

    /// <summary>
    /// Разбирает ответ активации, проверяет подпись и сохраняет лицензию.
    /// </summary>
    /// <param name="ответ">Тело ответа (JSON).</param>
    /// <returns>Результат применения ответа.</returns>
    private ActivationResult ПрименитьОтвет(string ответ)
    {
        if (_открытыйКлючПоставщика is null)
        {
            return ActivationResult.Failure(
                "Открытый ключ поставщика не настроен: проверка подписи ответа невозможна.");
        }

        ОтветАктивации? разобранный;

        try
        {
            разобранный = JsonSerializer.Deserialize<ОтветАктивации>(ответ, ПараметрыJson);
        }
        catch (JsonException исключение)
        {
            return ActivationResult.Failure("Ответ повреждён: данные ответа не читаются (" + исключение.Message + ").");
        }

        if (разобранный is null
            || string.IsNullOrWhiteSpace(разобранный.Payload)
            || string.IsNullOrWhiteSpace(разобранный.Signature))
        {
            return ActivationResult.Failure(
                "Ответ повреждён: не найдены данные лицензии и подпись.");
        }

        byte[] данные;
        byte[] подпись;

        try
        {
            данные = Convert.FromBase64String(разобранный.Payload);
            подпись = Convert.FromBase64String(разобранный.Signature);
        }
        catch (FormatException)
        {
            return ActivationResult.Failure(
                "Ответ повреждён: данные лицензии или подпись не декодируются.");
        }

        if (!_подпись.Verify(данные, подпись, _открытыйКлючПоставщика))
        {
            return ActivationResult.Failure(LicenseSignature.ТекстОшибкиПовреждения);
        }

        LicensePayload? лицензия;

        try
        {
            лицензия = JsonSerializer.Deserialize<LicensePayload>(данные, LicenseStore.ПараметрыJson);
        }
        catch (JsonException исключение)
        {
            return ActivationResult.Failure("Ответ повреждён: сведения о лицензии не читаются (" + исключение.Message + ").");
        }

        if (лицензия is null)
        {
            return ActivationResult.Failure("Ответ повреждён: сведения о лицензии пусты.");
        }

        var машина = _хранилище.MachineId;

        if (!string.IsNullOrWhiteSpace(лицензия.MachineId)
            && !string.Equals(лицензия.MachineId, машина, StringComparison.OrdinalIgnoreCase))
        {
            return ActivationResult.Failure(
                "Ключ выписан на другое оборудование (идентификатор " + лицензия.MachineId
                + "). Обратитесь в поддержку.");
        }

        var привязка = new LicenseBinding(машина, DateTime.UtcNow);

        var прежниеПривязки = лицензия.Bindings is { } перечень && перечень.Count > 0
            ? перечень
            : Array.Empty<LicenseBinding>();

        var сохраняемая = лицензия with
        {
            MachineId = машина,
            Bindings = new[] { привязка }.Concat(прежниеПривязки).ToArray(),
        };

        var защищённый = _хранилище.Crypto.Protect(
            JsonSerializer.SerializeToUtf8Bytes(сохраняемая, LicenseStore.ПараметрыJson));

        _хранилище.Save(защищённый, привязка, сохраняемая.Edition, сохраняемая.ExpiresAtUtc, сохраняемая.Owner);

        _автомат.EvaluateState();

        return ActivationResult.Success(_автомат.Status);
    }

    /// <summary>
    /// Определяет порядковый номер следующего офлайн-файла запроса.
    /// </summary>
    /// <returns>Номер запроса.</returns>
    private int СледующийНомерЗапроса()
    {
        if (!Directory.Exists(_каталогЗапросов))
        {
            return 1;
        }

        var количество = Directory
            .EnumerateFiles(_каталогЗапросов, ПрефиксИмениФайлаЗапроса + "*")
            .Count();

        return количество + 1;
    }

    /// <summary>
    /// Отправляет запрос серверу активации по протоколу HTTP.
    /// </summary>
    /// <param name="адрес">Адрес сервера активации.</param>
    /// <param name="тело">Тело запроса (JSON).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Тело ответа (JSON).</returns>
    private static async Task<string> ОтправитьПоHttpAsync(string адрес, string тело, CancellationToken cancellationToken)
    {
        using var содержимое = new StringContent(тело, Encoding.UTF8, "application/json");

        using var ответ = await Клиент.PostAsync(адрес, содержимое, cancellationToken).ConfigureAwait(false);

        ответ.EnsureSuccessStatusCode();

        return await ответ.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Содержимое офлайн-файла запроса на регистрацию.
    /// </summary>
    private sealed record ЗапросАктивации
    {
        /// <summary>Ключ активации.</summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>Идентификатор оборудования.</summary>
        public string MachineId { get; init; } = string.Empty;

        /// <summary>Вариант вычисления идентификатора оборудования.</summary>
        public string MachineIdVariant { get; init; } = string.Empty;

        /// <summary>Метка времени создания запроса (UTC).</summary>
        public DateTime CreatedAtUtc { get; init; }

        /// <summary>Идентификатор запроса.</summary>
        public Guid RequestId { get; init; }

        /// <summary>Имя файла запроса.</summary>
        public string FileName { get; init; } = string.Empty;
    }

    /// <summary>
    /// Содержимое ответного файла активации.
    /// </summary>
    private sealed record ОтветАктивации
    {
        /// <summary>Данные лицензии в кодировке Base64.</summary>
        public string Payload { get; init; } = string.Empty;

        /// <summary>Подпись данных в кодировке Base64.</summary>
        public string Signature { get; init; } = string.Empty;
    }
}
