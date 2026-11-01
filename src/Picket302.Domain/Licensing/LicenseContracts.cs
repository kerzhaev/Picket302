namespace Picket302.Domain.Licensing;

/// <summary>
/// Состояние лицензии — результат работы автомата состояний (ТЗ §8.10,
/// функция 119: «состояние лицензии — один автомат, вычисляемый при старте»).
/// </summary>
public enum LicenseState
{
    /// <summary>Пробный период: действует срок и лимиты объёма.</summary>
    Trial = 0,

    /// <summary>Лицензия получена: срок и лимиты не действуют.</summary>
    Licensed = 1,

    /// <summary>Срок истёк либо ключ снят.</summary>
    Expired = 2,
}

/// <summary>
/// Редакция программы (ТЗ §8.10, функция 126: «Три редакции продукта»).
/// </summary>
public enum Edition
{
    /// <summary>Free — базовые печать и адресная книга с лимитами trial.</summary>
    Free = 0,

    /// <summary>Home — без лимитов времени, печать всех форматов конвертов.</summary>
    Home = 1,

    /// <summary>
    /// Professional — без лимита адресов, импорт из Excel/CSV, автоматический
    /// разбор адресов, автоматизация, обновления и поддержка.
    /// </summary>
    Professional = 2,
}

/// <summary>
/// Вариант вычисления идентификатора оборудования (ТЗ §8.10, функция 120).
/// </summary>
public enum MachineIdVariant
{
    /// <summary>Серийный номер тома.</summary>
    VolumeSerial = 0,

    /// <summary>UUID материнской платы.</summary>
    BoardUuid = 1,

    /// <summary>Составной отпечаток оборудования.</summary>
    Composite = 2,
}

/// <summary>
/// Возможность программы, доступность которой зависит от редакции
/// (ТЗ §8.10, функция 126: окно преимуществ показывает перечень функций).
/// </summary>
public enum LicenseFeature
{
    /// <summary>Базовая печать конвертов и работа с адресной книгой.</summary>
    BasicPrinting = 0,

    /// <summary>Печать всех форматов конвертов.</summary>
    AllEnvelopeFormats = 1,

    /// <summary>Импорт данных из Excel и CSV.</summary>
    SpreadsheetImport = 2,

    /// <summary>Автоматический разбор адресов.</summary>
    AutomaticAddressParsing = 3,

    /// <summary>Автоматизация и расписание задач.</summary>
    Automation = 4,

    /// <summary>Автоматические обновления и поддержка.</summary>
    UpdatesAndSupport = 5,
}

/// <summary>
/// Лимиты редакции и пробного периода (ТЗ §8.10, функция 119).
/// </summary>
/// <param name="MaxCounterparties">
/// Предельное число контрагентов; <see langword="null"/> — без ограничения.
/// </param>
/// <param name="MaxDocuments">
/// Предельное число документов; <see langword="null"/> — без ограничения.
/// </param>
/// <param name="TrialDays">
/// Длительность пробного периода в днях; <see langword="null"/> — срок не
/// ограничен.
/// </param>
public sealed record LicenseLimits(
    int? MaxCounterparties,
    int? MaxDocuments,
    int? TrialDays)
{
    /// <summary>Лимиты без ограничений (редакция Professional).</summary>
    public static LicenseLimits Unlimited { get; } = new(null, null, null);
}

/// <summary>
/// Описание редакции: лимиты и перечень доступных возможностей
/// (ТЗ §8.10, функция 126).
/// </summary>
/// <param name="Edition">Редакция.</param>
/// <param name="Limits">Лимиты редакции.</param>
/// <param name="Features">Доступные возможности.</param>
/// <param name="Advantages">
/// Перечень преимуществ для окна апгрейда (ТЗ §8.10, функция 126: «окно
/// апгрейда со списком преимуществ»).
/// </param>
public sealed record EditionInfo(
    Edition Edition,
    LicenseLimits Limits,
    IReadOnlyList<LicenseFeature> Features,
    IReadOnlyList<string> Advantages);

/// <summary>
/// Статус лицензии, показываемый оператору (ТЗ §8.10, функция 123:
/// <c>public LicenseStatus GetStatus()</c>).
/// </summary>
/// <param name="State">Состояние лицензии.</param>
/// <param name="Edition">Действующая редакция.</param>
/// <param name="ExpiresAtUtc">Момент истечения срока (UTC); <see langword="null"/> — срок не ограничен.</param>
/// <param name="Owner">Владелец лицензии.</param>
/// <param name="MachineId">Идентификатор оборудования, к которому привязана лицензия.</param>
/// <param name="DaysRemaining">Число оставшихся дней срока.</param>
/// <param name="Message">Пояснение для оператора; <see langword="null"/> — замечаний нет.</param>
public sealed record LicenseStatus(
    LicenseState State,
    Edition Edition,
    DateTime? ExpiresAtUtc,
    string? Owner,
    string? MachineId,
    int DaysRemaining,
    string? Message)
{
    /// <summary>
    /// Признак блокировки создания записей и печати партий: состояние
    /// истёкло либо исчерпан лимит пробного периода (ТЗ §8.10, функция 119).
    /// </summary>
    public bool IsRestricted => State != LicenseState.Licensed;

    /// <summary>
    /// Текст сообщения NAG-окна с числом оставшихся дней
    /// (ТЗ §8.10, функция 119: «Осталось дней:»).
    /// </summary>
    public string BuildNagMessage()
    {
        if (State == LicenseState.Licensed)
        {
            return $"Редакция: {Редакции.Название(Edition)}. Осталось дней: без ограничения.";
        }

        return State == LicenseState.Trial
            ? $"Пробный период. Осталось дней: {DaysRemaining}."
            : $"Пробный период закончился. Осталось дней: {DaysRemaining}.";
    }
}

/// <summary>
/// Результат активации лицензии (ТЗ §8.10, функция 122).
/// </summary>
/// <param name="IsSuccess">Признак успешного исхода.</param>
/// <param name="Status">Статус лицензии после активации.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
public sealed record ActivationResult(bool IsSuccess, LicenseStatus? Status, string? Error)
{
    /// <summary>Создаёт успешный результат.</summary>
    /// <param name="status">Статус лицензии после активации.</param>
    /// <returns>Успешный результат.</returns>
    public static ActivationResult Success(LicenseStatus status) => new(true, status, null);

    /// <summary>Создаёт неуспешный результат.</summary>
    /// <param name="error">Текст ошибки на русском языке.</param>
    /// <returns>Неуспешный результат.</returns>
    public static ActivationResult Failure(string error) => new(false, null, error);
}

/// <summary>
/// Каталог редакций продукта (ТЗ §8.10, функция 126).
/// </summary>
public static class Редакции
{
    /// <summary>Длительность пробного периода по умолчанию, дней.</summary>
    public const int ПробныйСрокДнейПоУмолчанию = 10;

    /// <summary>Описание редакции Free.</summary>
    public static EditionInfo Free { get; } = new(
        Edition.Free,
        new LicenseLimits(500, 100, ПробныйСрокДнейПоУмолчанию),
        [LicenseFeature.BasicPrinting],
        [
            "Базовые печать конвертов и работа с адресной книгой.",
            "Лимит пробного периода — 10 дней.",
        ]);

    /// <summary>Описание редакции Home.</summary>
    public static EditionInfo Home { get; } = new(
        Edition.Home,
        new LicenseLimits(null, null, null),
        [LicenseFeature.BasicPrinting, LicenseFeature.AllEnvelopeFormats],
        [
            "Без ограничения срока работы.",
            "Печать всех форматов конвертов.",
        ]);

    /// <summary>Описание редакции Professional.</summary>
    public static EditionInfo Professional { get; } = new(
        Edition.Professional,
        LicenseLimits.Unlimited,
        [
            LicenseFeature.BasicPrinting,
            LicenseFeature.AllEnvelopeFormats,
            LicenseFeature.SpreadsheetImport,
            LicenseFeature.AutomaticAddressParsing,
            LicenseFeature.Automation,
            LicenseFeature.UpdatesAndSupport,
        ],
        [
            "Без лимита адресов и срока работы.",
            "Импорт из Excel и CSV.",
            "Автоматический разбор адресов.",
            "Автоматизация и обновления, поддержка.",
        ]);

    /// <summary>
    /// Возвращает описание редакции.
    /// </summary>
    /// <param name="edition">Редакция.</param>
    /// <returns>Описание редакции.</returns>
    public static EditionInfo Описание(Edition edition) => edition switch
    {
        Edition.Home => Home,
        Edition.Professional => Professional,
        _ => Free,
    };

    /// <summary>
    /// Русское название редакции.
    /// </summary>
    /// <param name="edition">Редакция.</param>
    /// <returns>Название редакции.</returns>
    public static string Название(Edition edition) => edition switch
    {
        Edition.Home => "Home",
        Edition.Professional => "Professional",
        _ => "Free",
    };

    /// <summary>
    /// Определяет, доступна ли возможность в редакции.
    /// </summary>
    /// <param name="edition">Редакция.</param>
    /// <param name="feature">Проверяемая возможность.</param>
    /// <returns><see langword="true"/>, если возможность доступна.</returns>
    public static bool Поддерживает(Edition edition, LicenseFeature feature)
        => Описание(edition).Features.Contains(feature);
}

/// <summary>
/// Запись о привязке лицензии к управляющей машине (ТЗ §8.10, функция 123:
/// «активация ключей на нескольких управляющих машинах»).
/// </summary>
/// <param name="MachineId">Идентификатор оборудования.</param>
/// <param name="ActivatedAtUtc">Момент активации (UTC).</param>
public sealed record LicenseBinding(string MachineId, DateTime ActivatedAtUtc);
