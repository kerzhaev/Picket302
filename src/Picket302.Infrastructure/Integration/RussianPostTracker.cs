using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Picket302.Infrastructure.Integration;

/// <summary>
/// Регистрация партии отправлений на трекере Почты России (ТЗ §8.8,
/// функция 110: <c>public TrackBatch RegisterBatch(RegisterBatchRequest request)</c>).
/// </summary>
/// <param name="BatchNumber">Номер партии.</param>
/// <param name="ShippingDate">Дата отправки.</param>
/// <param name="ShippingPoint">Пункт отправки.</param>
/// <param name="TrackingIds">Идентификаторы ШПИ (по 14 знаков).</param>
public sealed record RegisterBatchRequest(
    string BatchNumber,
    DateTime ShippingDate,
    string ShippingPoint,
    IReadOnlyList<string> TrackingIds);

/// <summary>
/// Зарегистрированная партия отправлений (ТЗ §8.8, функция 110).
/// </summary>
/// <param name="BatchNumber">Номер партии.</param>
/// <param name="ShippingDate">Дата отправки.</param>
/// <param name="ShippingPoint">Пункт отправки.</param>
/// <param name="TrackingIds">Идентификаторы ШПИ.</param>
/// <param name="RegisteredAtUtc">Момент регистрации (UTC).</param>
public sealed record TrackBatch(
    string BatchNumber,
    DateTime ShippingDate,
    string ShippingPoint,
    IReadOnlyList<string> TrackingIds,
    DateTime RegisteredAtUtc);

/// <summary>
/// Статус отправления, полученный от трекера (ТЗ §8.8, функция 110).
/// </summary>
/// <param name="TrackingId">Идентификатор ШПИ.</param>
/// <param name="Status">Текст статуса от сервиса.</param>
/// <param name="Delivered">Признак вручения отправления.</param>
/// <param name="DeliveredAtUtc">Момент вручения (UTC); <see langword="null"/> — не вручено.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
public sealed record TrackStatus(
    string TrackingId,
    string Status,
    bool Delivered,
    DateTime? DeliveredAtUtc,
    string? Error);

/// <summary>
/// Учётные данные трекера Почты России (ТЗ §8.8, функция 110: «учетные данные
/// (логин/пароль или токен) — пароль/токен в DPAPI»).
/// </summary>
/// <param name="Login">Логин оператора.</param>
/// <param name="ProtectedPassword">Пароль в защищённом виде (DPAPI).</param>
/// <param name="ProtectedToken">Токен в защищённом виде (DPAPI).</param>
public sealed record RussianPostCredentials(
    string Login,
    string? ProtectedPassword,
    string? ProtectedToken);

/// <summary>
/// Результат опроса статусов партии (ТЗ §8.8, функция 110:
/// <c>public TrackStatus[] QueryStatuses(string batchNumber)</c>).
/// </summary>
public sealed record TrackQueryResult(
    string BatchNumber,
    IReadOnlyList<TrackStatus> Statuses,
    string? Error)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => Error is not null;

    /// <summary>Число отправлений, по которым получено вручение.</summary>
    public int DeliveredCount => Statuses.Count(статус => статус.Delivered);
}

/// <summary>
/// Трекер отправлений на сайте Почты России (ТЗ §8.8, функция 110).
/// </summary>
/// <remarks>
/// <para>
/// Трекер регистрирует партию отправлений (ШПИ — 14 знаков, номер партии,
/// дата отправки, пункт отправки) и опрашивает статусы доставки. По факту
/// вручения отправление переводится из состояния «Отправлено» в «Подтверждено».
/// </para>
/// <para>
/// Базовый адрес сервиса задаётся настройкой: тем самым трекер работает и с
/// официальным сервисом, и с его корпоративным аналогом. Сетевой обмен
/// выполняется через внедряемый адаптер — так трекер проверяется без сети.
/// </para>
/// </remarks>
public sealed class RussianPostTracker
{
    /// <summary>Число знаков в идентификаторе ШПИ.</summary>
    public const int TrackingIdLength = 14;

    /// <summary>Статус «Отправлено».</summary>
    public const string СтатусОтправлено = "Отправлено";

    /// <summary>Статус «Подтверждено» (получено вручение).</summary>
    public const string СтатусПодтверждено = "Подтверждено";

    /// <summary>Число попыток опроса статусов по умолчанию.</summary>
    public const int ПопытокПоУмолчанию = 5;

    private readonly Func<string, string, CancellationToken, Task<string>> _запрос;
    private readonly Action<string>? _журнал;

    /// <summary>
    /// Создаёт трекер с сетевым адаптером по умолчанию.
    /// </summary>
    /// <param name="базовыйАдрес">Базовый адрес сервиса трекера.</param>
    /// <param name="учётныеДанные">Учётные данные оператора.</param>
    /// <param name="журнал">Приёмник записей в журнал.</param>
    public RussianPostTracker(
        string базовыйАдрес,
        RussianPostCredentials учётныеДанные,
        Action<string>? журнал = null)
        : this(базовыйАдрес, учётныеДанные, null, журнал)
    {
    }

    /// <summary>
    /// Создаёт трекер с внедряемым сетевым адаптером.
    /// </summary>
    /// <param name="базовыйАдрес">Базовый адрес сервиса трекера.</param>
    /// <param name="учётныеДанные">Учётные данные оператора.</param>
    /// <param name="запрос">
    /// Адаптер сетевого обмена: принимает базовый адрес и текст запроса,
    /// возвращает текст ответа. При <see langword="null"/> используется
    /// HTTP-клиент.
    /// </param>
    /// <param name="журнал">Приёмник записей в журнал.</param>
    public RussianPostTracker(
        string базовыйАдрес,
        RussianPostCredentials учётныеДанные,
        Func<string, string, CancellationToken, Task<string>>? запрос,
        Action<string>? журнал = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(базовыйАдрес);
        ArgumentNullException.ThrowIfNull(учётныеДанные);

        БазовыйАдрес = базовыйАдрес.TrimEnd('/');
        УчётныеДанные = учётныеДанные;
        _запрос = запрос ?? ЗапроситьПоHttp;
        _журнал = журнал;
    }

    /// <summary>Базовый адрес сервиса трекера.</summary>
    public string БазовыйАдрес { get; }

    /// <summary>Учётные данные оператора.</summary>
    public RussianPostCredentials УчётныеДанные { get; }

    /// <summary>
    /// Регистрирует партию отправлений (ТЗ §8.8, функция 110:
    /// <c>public TrackBatch RegisterBatch(RegisterBatchRequest request)</c>).
    /// </summary>
    /// <param name="request">Сведения о партии.</param>
    /// <returns>Зарегистрированная партия.</returns>
    /// <exception cref="ArgumentException">
    /// Номер партии не задан либо идентификатор ШПИ имеет не 14 знаков.
    /// </exception>
    public TrackBatch RegisterBatch(RegisterBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BatchNumber))
        {
            throw new ArgumentException("Не указан номер партии.", nameof(request));
        }

        if (request.TrackingIds.Count == 0)
        {
            throw new ArgumentException("Партия не содержит отправлений.", nameof(request));
        }

        foreach (var идентификатор in request.TrackingIds)
        {
            if (!ПроверитьИдентификатор(идентификатор))
            {
                throw new ArgumentException(
                    $"Идентификатор ШПИ «{идентификатор}» должен состоять из {TrackingIdLength} знаков.",
                    nameof(request));
            }
        }

        return new TrackBatch(
            request.BatchNumber,
            request.ShippingDate,
            request.ShippingPoint,
            request.TrackingIds.ToArray(),
            DateTime.UtcNow);
    }

    /// <summary>
    /// Проверяет идентификатор ШПИ: 14 знаков (ТЗ §8.8, функция 110).
    /// </summary>
    /// <param name="идентификатор">Проверяемый идентификатор.</param>
    /// <returns><see langword="true"/>, если идентификатор корректен.</returns>
    public static bool ПроверитьИдентификатор(string? идентификатор)
    {
        if (string.IsNullOrWhiteSpace(идентификатор))
        {
            return false;
        }

        var обрезанный = идентификатор.Trim();

        if (обрезанный.Length != TrackingIdLength)
        {
            return false;
        }

        return обрезанный.All(символ => char.IsDigit(символ) || char.IsLetter(символ));
    }

    /// <summary>
    /// Нормализует идентификатор ШПИ: пробелы и дефисы удаляются, регистр
    /// приводится к верхнему.
    /// </summary>
    /// <param name="идентификатор">Нормализуемый идентификатор.</param>
    /// <returns>Нормализованный идентификатор.</returns>
    public static string НормализоватьИдентификатор(string идентификатор)
    {
        ArgumentNullException.ThrowIfNull(идентификатор);

        return идентификатор.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    /// <summary>
    /// Опрашивает статусы доставки по номеру партии (ТЗ §8.8, функция 110:
    /// <c>public TrackStatus[] QueryStatuses(string batchNumber)</c>).
    /// </summary>
    /// <param name="batchNumber">Номер партии.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Статусы отправлений партии.</returns>
    public async Task<TrackStatus[]> QueryStatusesAsync(string batchNumber, CancellationToken cancellationToken = default)
    {
        var результат = await QueryStatusesDetailedAsync(batchNumber, cancellationToken).ConfigureAwait(false);

        return результат.Statuses.ToArray();
    }

    /// <summary>
    /// Опрашивает статусы доставки и возвращает подробный результат.
    /// </summary>
    /// <param name="batchNumber">Номер партии.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Подробный результат опроса.</returns>
    public async Task<TrackQueryResult> QueryStatusesDetailedAsync(
        string batchNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchNumber);

        var запрос = BuildQueryRequest(batchNumber);

        string ответ;

        try
        {
            ответ = await _запрос(БазовыйАдрес, запрос, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.Net.Http.HttpRequestException ошибка)
        {
            ЗаписатьВЖурнал($"Сервис трекера Почты России недоступен: {ошибка.Message}");

            return new TrackQueryResult(batchNumber, Array.Empty<TrackStatus>(), ошибка.Message);
        }
        catch (System.IO.IOException ошибка)
        {
            ЗаписатьВЖурнал($"Сбой связи с сервисом трекера Почты России: {ошибка.Message}");

            return new TrackQueryResult(batchNumber, Array.Empty<TrackStatus>(), ошибка.Message);
        }

        return ParseResponse(batchNumber, ответ);
    }

    /// <summary>
    /// Опрашивает статусы доставки с повторами по расписанию (ТЗ §8.8,
    /// функция 110: «Отказ сервиса — повтор по расписанию; исчерпание попыток —
    /// запись в журнал ошибок, вручение подтверждается вручную»).
    /// </summary>
    /// <param name="batchNumber">Номер партии.</param>
    /// <param name="попыток">Предельное число попыток.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат опроса; при исчерпании попыток — с текстом ошибки.</returns>
    public async Task<TrackQueryResult> QueryStatusesWithRetriesAsync(
        string batchNumber,
        int попыток = ПопытокПоУмолчанию,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(попыток);

        TrackQueryResult? последний = null;

        for (var попытка = 1; попытка <= попыток; попытка++)
        {
            последний = await QueryStatusesDetailedAsync(batchNumber, cancellationToken).ConfigureAwait(false);

            if (последний.IsFailure is false)
            {
                return последний;
            }

            ЗаписатьВЖурнал(
                $"Опрос статусов партии {batchNumber} не удался (попытка {попытка} из {попыток}): {последний.Error}");
        }

        var сообщение = $"Исчерпаны попытки опроса статусов партии {batchNumber} ({попыток}). "
            + "Вручение подтверждается вручную.";

        ЗаписатьВЖурнал(сообщение);

        return new TrackQueryResult(batchNumber, Array.Empty<TrackStatus>(), сообщение);
    }

    /// <summary>
    /// Определяет, требуется ли перевод отправления в состояние «Подтверждено»
    /// (ТЗ §8.8, функция 110: переход «Отправлено → Подтверждено» по факту
    /// вручения).
    /// </summary>
    /// <param name="статус">Полученный статус отправления.</param>
    /// <returns><see langword="true"/>, если вручение подтверждено.</returns>
    public static bool ПодтвержденоВручение(TrackStatus статус)
    {
        ArgumentNullException.ThrowIfNull(статус);

        return статус.Delivered
            || статус.Status.Contains(СтатусПодтверждено, StringComparison.OrdinalIgnoreCase)
            || статус.Status.Contains("вручен", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Формирует текст запроса статусов для сервиса трекера.
    /// </summary>
    /// <param name="batchNumber">Номер партии.</param>
    /// <returns>Текст запроса в формате JSON.</returns>
    public string BuildQueryRequest(string batchNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchNumber);

        var модель = new
        {
            login = УчётныеДанные.Login,
            batch = batchNumber,
            token = УчётныеДанные.ProtectedToken,
        };

        return JsonSerializer.Serialize(модель, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
    }

    /// <summary>
    /// Разбирает ответ сервиса трекера.
    /// </summary>
    /// <param name="batchNumber">Номер партии.</param>
    /// <param name="ответ">Текст ответа сервиса.</param>
    /// <returns>Результат опроса.</returns>
    public static TrackQueryResult ParseResponse(string batchNumber, string? ответ)
    {
        if (string.IsNullOrWhiteSpace(ответ))
        {
            return new TrackQueryResult(
                batchNumber,
                Array.Empty<TrackStatus>(),
                "Сервис трекера вернул пустой ответ.");
        }

        try
        {
            using var документ = JsonDocument.Parse(ответ);
            var корень = документ.RootElement;

            if (корень.TryGetProperty("error", out var ошибка)
                && ошибка.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(ошибка.GetString()))
            {
                return new TrackQueryResult(batchNumber, Array.Empty<TrackStatus>(), ошибка.GetString());
            }

            var статусы = new List<TrackStatus>();

            if (корень.TryGetProperty("items", out var элементы) && элементы.ValueKind == JsonValueKind.Array)
            {
                foreach (var элемент in элементы.EnumerateArray())
                {
                    статусы.Add(ПрочитатьСтатус(элемент));
                }
            }
            else if (корень.ValueKind == JsonValueKind.Array)
            {
                foreach (var элемент in корень.EnumerateArray())
                {
                    статусы.Add(ПрочитатьСтатус(элемент));
                }
            }

            return new TrackQueryResult(batchNumber, статусы, null);
        }
        catch (JsonException ошибка)
        {
            return new TrackQueryResult(
                batchNumber,
                Array.Empty<TrackStatus>(),
                $"Ответ сервиса трекера не разобран: {ошибка.Message}");
        }
    }

    /// <summary>
    /// Читает сведения об одном отправлении из ответа сервиса.
    /// </summary>
    private static TrackStatus ПрочитатьСтатус(JsonElement элемент)
    {
        var идентификатор = ПрочитатьСтроку(элемент, "trackingId")
            ?? ПрочитатьСтроку(элемент, "id")
            ?? string.Empty;

        var статус = ПрочитатьСтроку(элемент, "status")
            ?? ПрочитатьСтроку(элемент, "state")
            ?? string.Empty;

        var вручено = статус.Contains("вручен", StringComparison.OrdinalIgnoreCase)
            || статус.Contains(СтатусПодтверждено, StringComparison.OrdinalIgnoreCase)
            || (элемент.TryGetProperty("delivered", out var признак)
                && признак.ValueKind == JsonValueKind.True);

        DateTime? моментВручения = null;

        if (элемент.TryGetProperty("deliveredAt", out var дата)
            && дата.ValueKind == JsonValueKind.String
            && DateTime.TryParse(
                дата.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var разобранная))
        {
            моментВручения = разобранная;
        }

        return new TrackStatus(идентификатор, статус, вручено, моментВручения, null);
    }

    /// <summary>
    /// Читает строковое свойство элемента JSON.
    /// </summary>
    private static string? ПрочитатьСтроку(JsonElement элемент, string имя)
        => элемент.TryGetProperty(имя, out var значение) && значение.ValueKind == JsonValueKind.String
            ? значение.GetString()
            : null;

    /// <summary>
    /// Выполняет запрос к сервису трекера по HTTP.
    /// </summary>
    private async Task<string> ЗапроситьПоHttp(string базовыйАдрес, string запрос, CancellationToken cancellationToken)
    {
        using var клиент = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        using var содержимое = new StringContent(запрос, Encoding.UTF8, "application/json");

        using var ответ = await клиент
            .PostAsync(new Uri(new Uri(базовыйАдрес), "statuses"), содержимое, cancellationToken)
            .ConfigureAwait(false);

        return await ответ.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Записывает сообщение в журнал.
    /// </summary>
    private void ЗаписатьВЖурнал(string сообщение) => _журнал?.Invoke(сообщение);
}
