namespace Picket302.Shared.Protocol;

/// <summary>
/// Стабильные коды ошибок протокола совместного режима.
/// </summary>
/// <remarks>
/// Коды не переименовываются и не переиспользуются: по ним клиент принимает
/// решение о повторе запроса, о показах сообщения оператору и о записи в
/// журнал. Тексты сообщений — на русском языке; техническая диагностика
/// остаётся в журнале службы с идентификатором операции.
/// </remarks>
public static class SharedErrorCodes
{
    /// <summary>Устройство не сопряжено либо доступ отозван.</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>Несовместимая версия протокола.</summary>
    public const string VersionIncompatible = "version-incompatible";

    /// <summary>
    /// Запись изменена другим клиентом: версия, предъявленная при сохранении,
    /// устарела.
    /// </summary>
    public const string Conflict = "conflict";

    /// <summary>Сервер временно недоступен.</summary>
    public const string ServiceUnavailable = "service-unavailable";

    /// <summary>Недостаточно места на диске сервера.</summary>
    public const string InsufficientSpace = "insufficient-space";

    /// <summary>Ошибка миграции схемы базы данных.</summary>
    public const string MigrationFailed = "migration-failed";

    /// <summary>Запись не найдена.</summary>
    public const string NotFound = "not-found";

    /// <summary>Данные не прошли проверку.</summary>
    public const string ValidationFailed = "validation-failed";

    /// <summary>
    /// Идентификатор операции использован повторно с другим содержимым:
    /// результат прежней операции возвращать нельзя.
    /// </summary>
    public const string IdempotencyMismatch = "idempotency-mismatch";

    /// <summary>Тело запроса превышает предельный размер.</summary>
    public const string PayloadTooLarge = "payload-too-large";

    /// <summary>Архив пакета шаблона отклонён (небезопасное содержимое).</summary>
    public const string ArchiveRejected = "archive-rejected";

    /// <summary>Операция уже выполняется.</summary>
    public const string OperationInProgress = "operation-in-progress";

    /// <summary>Внутренняя ошибка службы.</summary>
    public const string Internal = "internal";
}

/// <summary>
/// Ошибка протокола совместного режима.
/// </summary>
/// <param name="Code">Стабильный код ошибки (см. <see cref="SharedErrorCodes"/>).</param>
/// <param name="Message">Понятное оператору сообщение на русском языке.</param>
/// <param name="OperationId">
/// Идентификатор операции, по которому в журнале службы ищется подробная
/// диагностика; может быть пустым.
/// </param>
/// <param name="Details">
/// Дополнительные сведения, безопасные для показа оператору (например, имя
/// изменившей записи). Трассировки, секреты и абсолютные пути сюда не
/// попадают.
/// </param>
public sealed record SharedError(
    string Code,
    string Message,
    string? OperationId = null,
    string? Details = null)
{
    /// <summary>Признак конфликта версий записи.</summary>
    public bool IsConflict => Code == SharedErrorCodes.Conflict;

    /// <summary>Признак отказа в авторизации.</summary>
    public bool IsUnauthorized => Code == SharedErrorCodes.Unauthorized;

    /// <summary>
    /// Создаёт ошибку с идентификатором операции.
    /// </summary>
    /// <param name="code">Код ошибки.</param>
    /// <param name="message">Сообщение оператору.</param>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="details">Дополнительные сведения.</param>
    /// <returns>Готовая ошибка.</returns>
    public static SharedError Create(
        string code,
        string message,
        string? operationId = null,
        string? details = null)
        => new(code, message, operationId, details);
}
