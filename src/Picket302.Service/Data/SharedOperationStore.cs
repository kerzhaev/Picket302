using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Picket302.Infrastructure.Data;

namespace Picket302.Service.Data;

/// <summary>
/// Исход попытки начать операцию в журнале идемпотентности.
/// </summary>
public enum SharedOperationOutcomeKind
{
    /// <summary>Операцию нужно выполнить.</summary>
    Proceed = 0,

    /// <summary>Операция уже выполнена — вернуть сохранённый результат.</summary>
    ReplayResult = 1,

    /// <summary>Операция уже завершилась ошибкой — вернуть сохранённую ошибку.</summary>
    ReplayError = 2,

    /// <summary>
    /// Идентификатор операции уже занят другой незавершённой попыткой либо
    /// использован с другим содержимым.
    /// </summary>
    Conflict = 3,
}

/// <summary>
/// Результат попытки начать операцию.
/// </summary>
/// <param name="Вид">Исход попытки.</param>
/// <param name="Результат">Сохранённый результат операции.</param>
/// <param name="Ошибка">Сохранённая ошибка операции.</param>
/// <param name="Причина">Пояснение для журнала.</param>
public sealed record SharedOperationOutcome(
    SharedOperationOutcomeKind Вид,
    string? Результат = null,
    string? Ошибка = null,
    string? Причина = null);

/// <summary>
/// Журнал выполненных операций — основа идемпотентности.
/// </summary>
/// <remarks>
/// <para>
/// Клиент задаёт идентификатор операции один раз на команду. Если связь
/// оборвалась и клиент повторил запрос, служба не выполняет команду заново, а
/// возвращает прежний результат: второго письма или второй рассылки не
/// возникает.
/// </para>
/// <para>
/// Журнал хранит отпечаток содержимого запроса. Повтор с тем же
/// идентификатором, но другим содержимым, отклоняется: возвращать результат
/// другой операции нельзя.
/// </para>
/// </remarks>
public sealed class SharedOperationStore
{
    /// <summary>Статус незавершённой операции.</summary>
    public const string СтатусВРаботе = "in-progress";

    /// <summary>Статус успешно завершённой операции.</summary>
    public const string СтатусЗавершена = "completed";

    /// <summary>Статус завершённой с ошибкой операции.</summary>
    public const string СтатусОшибка = "failed";

    /// <summary>
    /// Время, после которого незавершённую операцию можно перехватить:
    /// служба могла завершиться аварийно, не записав результат.
    /// </summary>
    public static readonly TimeSpan СрокПерехвата = TimeSpan.FromMinutes(5);

    private readonly ISqliteSession _сессия;
    private readonly Action<string> _журнал;

    /// <summary>
    /// Создаёт журнал над указанной сессией.
    /// </summary>
    /// <param name="сессия">Сессия базы данных.</param>
    /// <param name="журнал">
    /// Приёмник диагностических сообщений: в журнал попадают факты о
    /// несовпадении содержимого операции, без самих данных запроса.
    /// </param>
    public SharedOperationStore(ISqliteSession сессия, Action<string>? журнал = null)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _сессия = сессия;
        _журнал = журнал ?? (_ => { });
    }

    /// <summary>
    /// Регистрирует попытку выполнить операцию.
    /// </summary>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="deviceId">Идентификатор устройства-клиента.</param>
    /// <param name="commandName">Имя команды.</param>
    /// <param name="requestHash">Отпечаток содержимого запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Исход попытки.</returns>
    public async Task<SharedOperationOutcome> НачатьAsync(
        string operationId,
        string deviceId,
        string commandName,
        string requestHash,
        DateTime моментЗапуска,
        CancellationToken cancellationToken = default)
    {
        var сейчас = моментЗапуска;

        var добавлено = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_operations
                    (operation_id, device_id, command_name, request_hash, status, created_at)
                VALUES (@Id, @DeviceId, @Command, @Hash, @Status, @Now)
                ON CONFLICT (operation_id) DO NOTHING;
                """,
                new
                {
                    Id = operationId,
                    DeviceId = deviceId,
                    Command = commandName,
                    Hash = requestHash,
                    Status = СтатусВРаботе,
                    Now = Момент(сейчас),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (добавлено > 0)
        {
            return new SharedOperationOutcome(SharedOperationOutcomeKind.Proceed);
        }

        var существующая = await ПрочитатьAsync(operationId, cancellationToken).ConfigureAwait(false);

        if (существующая is null)
        {
            // Запись исчезла между вставкой и чтением. Выполнять команду без
            // записи в журнале нельзя: результат не удалось бы зафиксировать,
            // и повтор запроса выполнил бы команду второй раз.
            return new SharedOperationOutcome(
                SharedOperationOutcomeKind.Conflict,
                Причина: "Запись операции в журнале недоступна: повторите действие.");
        }

        if (!string.Equals(существующая.RequestHash, requestHash, StringComparison.Ordinal)
            || !string.Equals(существующая.CommandName, commandName, StringComparison.Ordinal))
        {
            _журнал(
                "Идентификатор операции использован повторно с другим содержимым: "
                + $"сохранённая команда «{существующая.CommandName}», "
                + $"повторная команда «{commandName}». Повтор отклонён.");
            return new SharedOperationOutcome(
                SharedOperationOutcomeKind.Conflict,
                Причина: "Идентификатор операции уже использован с другим содержимым.");
        }

        if (существующая.Status == СтатусЗавершена)
        {
            return new SharedOperationOutcome(
                SharedOperationOutcomeKind.ReplayResult,
                Результат: существующая.ResultJson);
        }

        if (существующая.Status == СтатусОшибка)
        {
            return new SharedOperationOutcome(
                SharedOperationOutcomeKind.ReplayError,
                Ошибка: существующая.ErrorJson);
        }

        if (существующая.Status == СтатусВРаботе
            && (сейчас - существующая.CreatedAtUtc) < СрокПерехвата)
        {
            return new SharedOperationOutcome(
                SharedOperationOutcomeKind.Conflict,
                Причина: "Операция с этим идентификатором уже выполняется.");
        }

        // Незавершённая попытка устарела (служба завершилась аварийно):
        // перехватываем операцию и выполняем заново.
        var перехвачено = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE shared_operations
                SET status = @Status, created_at = @Now, device_id = @DeviceId
                WHERE operation_id = @Id AND status = @Status;
                """,
                new
                {
                    Id = operationId,
                    Status = СтатусВРаботе,
                    Now = Момент(сейчас),
                    DeviceId = deviceId,
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return перехвачено > 0
            ? new SharedOperationOutcome(SharedOperationOutcomeKind.Proceed)
            : new SharedOperationOutcome(
                SharedOperationOutcomeKind.Conflict,
                Причина: "Операция с этим идентификатором уже выполняется.");
    }

    /// <summary>
    /// Сохраняет успешный результат операции.
    /// </summary>
    /// <remarks>
    /// Запись обновляется только если она всё ещё принадлежит этой попытке:
    /// статус «выполняется» и момент начала должны совпадать. Иначе попытка
    /// была перехвачена другой (например, после аварийного завершения службы),
    /// и запись результата была бы неверной.
    /// </remarks>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="resultJson">Результат в виде JSON.</param>
    /// <param name="моментЗапуска">Момент начала операции (UTC).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если результат зафиксирован.</returns>
    public async Task<bool> СохранитьРезультатAsync(
        string operationId,
        string resultJson,
        DateTime моментЗапуска,
        CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE shared_operations
                SET status = @Status, result_json = @Result, completed_at = @Now
                WHERE operation_id = @Id AND status = @Working AND created_at = @StartedAt;
                """,
                new
                {
                    Id = operationId,
                    Status = СтатусЗавершена,
                    Working = СтатусВРаботе,
                    Result = resultJson,
                    StartedAt = Момент(моментЗапуска),
                    Now = Момент(DateTime.UtcNow),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <summary>
    /// Сохраняет ошибку операции: повтор запроса вернёт её, а не выполнит
    /// команду заново.
    /// </summary>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="errorJson">Ошибка в виде JSON.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task<bool> СохранитьОшибкуAsync(
        string operationId,
        string errorJson,
        DateTime моментЗапуска,
        CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE shared_operations
                SET status = @Status, error_json = @Error, completed_at = @Now
                WHERE operation_id = @Id AND status = @Working AND created_at = @StartedAt;
                """,
                new
                {
                    Id = operationId,
                    Status = СтатусОшибка,
                    Working = СтатусВРаботе,
                    Error = errorJson,
                    StartedAt = Момент(моментЗапуска),
                    Now = Момент(DateTime.UtcNow),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <summary>
    /// <summary>
    /// Удаляет завершённые операции старше указанного срока.
    /// </summary>
    /// <remarks>
    /// Журнал идемпотентности нужен, пока операция может быть повторена.
    /// Через месяц повтор той же операции уже не ожидается, поэтому запись
    /// удаляется: иначе журнал растёт без ограничения.
    /// </remarks>
    /// <param name="срок">Срок хранения завершённых операций.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых записей.</returns>
    public async Task<int> УдалитьУстаревшиеAsync(
        TimeSpan срок,
        CancellationToken cancellationToken = default)
    {
        return await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM shared_operations
                WHERE created_at < @Граница AND status <> @Working;
                """,
                new
                {
                    Граница = Момент(DateTime.UtcNow - срок),
                    Working = СтатусВРаботе,
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Вычисляет отпечаток содержимого запроса: одинаковое содержимое даёт
    /// одинаковый отпечаток, разное — разный.
    /// </summary>
    /// <param name="содержимое">Содержимое запроса в виде JSON.</param>
    /// <returns>Шестнадцатеричный SHA-256 содержимого.</returns>
    public static string ВычислитьОтпечаток(string содержимое)
    {
        var хеш = SHA256.HashData(Encoding.UTF8.GetBytes(содержимое ?? string.Empty));

        return Convert.ToHexString(хеш).ToLowerInvariant();
    }

    /// <summary>
    /// Читает запись журнала операций.
    /// </summary>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись журнала либо <see langword="null"/>, если её нет.</returns>
    private async Task<ЗаписьОперации?> ПрочитатьAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        var строка = await _сессия.Connection.QueryFirstOrDefaultAsync<ЗаписьОперации>(
            new CommandDefinition(
                """
                SELECT operation_id AS OperationId,
                       command_name AS CommandName,
                       request_hash AS RequestHash,
                       status AS Status,
                       result_json AS ResultJson,
                       error_json AS ErrorJson,
                       created_at AS CreatedAt
                FROM shared_operations
                WHERE operation_id = @Id;
                """,
                new { Id = operationId },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return строка;
    }

    /// <summary>
    /// Текущий момент в формате, принятом в базе (UTC).
    /// </summary>
    /// <param name="момент">Момент времени.</param>
    /// <returns>Текст момента.</returns>
    private static string Момент(DateTime момент) => момент.ToString(
        "yyyy-MM-dd HH:mm:ss.fff",
        CultureInfo.InvariantCulture);

    private sealed class ЗаписьОперации
    {
        public string OperationId { get; init; } = string.Empty;

        public string CommandName { get; init; } = string.Empty;

        public string RequestHash { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string? ResultJson { get; init; }

        public string? ErrorJson { get; init; }

        public string CreatedAt { get; init; } = string.Empty;

        public DateTime CreatedAtUtc =>
            DateTime.TryParse(
                CreatedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var разобранное)
                ? разобранное
                : DateTime.MinValue;
    }
}
