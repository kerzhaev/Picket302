using Picket302.Domain.Entities;

namespace Picket302.Shared.Commands;

/// <summary>
/// Идентификатор записи вместе с версией, которую клиент прочитал.
/// </summary>
/// <remarks>
/// Значение версии предъявляется при изменении записи: если запись успели
/// изменить с другого рабочего места, версия не совпадёт и изменение будет
/// отклонено без частичной записи.
/// </remarks>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="Version">Прочитанная версия записи.</param>
public sealed record VersionedId(long Id, long Version);

/// <summary>
/// Ответ команды, изменяющей запись: идентификатор и новая версия.
/// </summary>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="Version">Версия записи после изменения.</param>
public sealed record VersionedResponse(long Id, long Version);

/// <summary>
/// Ответ команды, изменившей несколько записей.
/// </summary>
/// <param name="AffectedIds">Идентификаторы затронутых записей.</param>
/// <param name="Count">Число затронутых записей.</param>
public sealed record BatchOperationResponse(
    IReadOnlyList<long> AffectedIds,
    int Count);

/// <summary>
/// Итог печатной операции, зафиксированный в общем журнале.
/// </summary>
/// <param name="MailIds">Отправления, к которым относится событие.</param>
/// <param name="PrintJobId">Запись реестра печатных сессий.</param>
/// <param name="Outcome">Зафиксированный исход печати.</param>
public sealed record PrintEventResponse(
    IReadOnlyList<long> MailIds,
    long? PrintJobId,
    PrintEventOutcome Outcome);

/// <summary>
/// Исход печатной операции в общем журнале.
/// </summary>
/// <remarks>
/// Значения различают четыре состояния, которые нельзя смешивать: «подготовлено»
/// (данные собраны, задание в спулер не передавалось), «передано локальному
/// спулеру» (задание принято спулером рабочего места), «ошибка» и «неизвестный
/// исход после обрыва» (состояние нельзя установить).
/// </remarks>
public enum PrintEventOutcome
{
    /// <summary>Подготовлено: данные собраны, печать не подтверждена.</summary>
    Prepared = 0,

    /// <summary>Передано локальному спулеру рабочего места.</summary>
    Spooled = 1,

    /// <summary>Ошибка печати.</summary>
    Error = 2,

    /// <summary>Неизвестный исход после обрыва связи.</summary>
    Unknown = 3,
}

/// <summary>
/// Ответ команды слияния импорта.
/// </summary>
/// <param name="Created">Число созданных записей.</param>
/// <param name="Updated">Число обновлённых записей.</param>
/// <param name="Skipped">Число пропущенных записей.</param>
/// <param name="Conflicts">Число записей с конфликтами слияния.</param>
/// <param name="Errors">Число записей, записанных с ошибками проверки.</param>
/// <param name="Unverified">Число записей, не подтверждённых оператором.</param>
public sealed record ImportMergeResponse(
    int Created,
    int Updated,
    int Skipped,
    int Conflicts,
    int Errors = 0,
    int Unverified = 0);

/// <summary>
/// Ответ команды сохранения пользовательского поля.
/// </summary>
/// <param name="Id">Идентификатор поля.</param>
/// <param name="CounterpartyId">Контрагент.</param>
/// <param name="FieldName">Имя поля.</param>
/// <param name="FieldValue">Значение поля.</param>
/// <param name="FieldType">Тип поля.</param>
/// <param name="UpdatedAt">Момент последнего изменения (UTC).</param>
/// <param name="Version">Маркер версии поля.</param>
public sealed record CounterpartyFieldResponse(
    long Id,
    long CounterpartyId,
    string FieldName,
    string? FieldValue,
    CounterpartyFieldType FieldType,
    DateTime UpdatedAt,
    long Version);

/// <summary>
/// Ответ команды создания или чтения контрагента.
/// </summary>
/// <param name="Id">Идентификатор контрагента.</param>
/// <param name="Type">Тип контрагента.</param>
/// <param name="Name">Наименование.</param>
/// <param name="ShortName">Сокращённое наименование.</param>
/// <param name="Inn">ИНН.</param>
/// <param name="Kpp">КПП.</param>
/// <param name="IsSender">Признак отправителя.</param>
/// <param name="IsActive">Признак активности.</param>
/// <param name="CreatedAt">Момент создания (UTC).</param>
/// <param name="UpdatedAt">Момент последнего изменения (UTC).</param>
/// <param name="Version">Маркер версии.</param>
public sealed record CounterpartyResponse(
    long Id,
    CounterpartyType Type,
    string Name,
    string? ShortName,
    string? Inn,
    string? Kpp,
    bool IsSender,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Version);
