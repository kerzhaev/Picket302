using System.Globalization;
using Dapper;
using Picket302.Infrastructure.Data;

namespace Picket302.Service.Security;

/// <summary>
/// Сведения о сопряжённом устройстве-клиенте.
/// </summary>
/// <param name="DeviceId">Идентификатор устройства.</param>
/// <param name="Name">Имя устройства, показанное администратору.</param>
/// <param name="SecretHash">Отпечаток секрета устройства.</param>
/// <param name="CreatedAt">Момент сопряжения (UTC).</param>
/// <param name="LastSeenAt">Момент последнего обращения (UTC).</param>
/// <param name="RevokedAt">Момент отзыва доступа (UTC).</param>
/// <param name="RevokedReason">Причина отзыва.</param>
public sealed record SharedDevice(
    string DeviceId,
    string Name,
    string SecretHash,
    DateTime CreatedAt,
    DateTime? LastSeenAt,
    DateTime? RevokedAt,
    string? RevokedReason)
{
    /// <summary>Признак отозванного устройства.</summary>
    public bool IsRevoked => RevokedAt is not null;
}

/// <summary>
/// Хранилище сопряжённых устройств-клиентов.
/// </summary>
/// <remarks>
/// Секрет устройства в открытом виде на сервере не хранится: хранится
/// только его отпечаток, по которому проверяется предъявленный секрет.
/// </remarks>
public sealed class SharedDeviceStore
{
    private readonly ISqliteSession _сессия;

    /// <summary>
    /// Создаёт хранилище над указанной сессией.
    /// </summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public SharedDeviceStore(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _сессия = сессия;
    }

    /// <summary>
    /// Регистрирует новое устройство.
    /// </summary>
    /// <param name="deviceId">Идентификатор устройства.</param>
    /// <param name="name">Имя устройства.</param>
    /// <param name="secretHash">Отпечаток секрета устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task ЗарегистрироватьAsync(
        string deviceId,
        string name,
        string secretHash,
        CancellationToken cancellationToken = default)
    {
        await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_devices (device_id, name, secret_hash, created_at)
                VALUES (@Id, @Name, @Hash, @Now);
                """,
                new
                {
                    Id = deviceId,
                    Name = name,
                    Hash = secretHash,
                    Now = Момент(DateTime.UtcNow),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Ищет устройство по предъявленному секрету.
    /// </summary>
    /// <param name="secretHash">Отпечаток предъявленного секрета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Устройство либо <see langword="null"/>, если такого нет.</returns>
    public async Task<SharedDevice?> НайтиПоСекретуAsync(
        string secretHash,
        CancellationToken cancellationToken = default)
    {
        var строка = await _сессия.Connection.QueryFirstOrDefaultAsync<СтрокаУстройства>(
            new CommandDefinition(
                """
                SELECT device_id AS DeviceId,
                       name AS Name,
                       secret_hash AS SecretHash,
                       created_at AS CreatedAt,
                       last_seen_at AS LastSeenAt,
                       revoked_at AS RevokedAt,
                       revoked_reason AS RevokedReason
                FROM shared_devices
                WHERE secret_hash = @Hash;
                """,
                new { Hash = secretHash },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return строка?.Преобразовать();
    }

    /// <summary>
    /// Ищет устройство по идентификатору.
    /// </summary>
    /// <param name="deviceId">Идентификатор устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Устройство либо <see langword="null"/>, если такого нет.</returns>
    public async Task<SharedDevice?> НайтиПоИдентификаторуAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var строка = await _сессия.Connection.QueryFirstOrDefaultAsync<СтрокаУстройства>(
            new CommandDefinition(
                """
                SELECT device_id AS DeviceId,
                       name AS Name,
                       secret_hash AS SecretHash,
                       created_at AS CreatedAt,
                       last_seen_at AS LastSeenAt,
                       revoked_at AS RevokedAt,
                       revoked_reason AS RevokedReason
                FROM shared_devices
                WHERE device_id = @Id;
                """,
                new { Id = deviceId },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return строка?.Преобразовать();
    }

    /// <summary>
    /// Перечисляет все сопряжённые устройства.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список устройств.</returns>
    public async Task<IReadOnlyList<SharedDevice>> ПеречислитьAsync(
        CancellationToken cancellationToken = default)
    {
        var строки = await _сессия.Connection.QueryAsync<СтрокаУстройства>(
            new CommandDefinition(
                """
                SELECT device_id AS DeviceId,
                       name AS Name,
                       secret_hash AS SecretHash,
                       created_at AS CreatedAt,
                       last_seen_at AS LastSeenAt,
                       revoked_at AS RevokedAt,
                       revoked_reason AS RevokedReason
                FROM shared_devices
                ORDER BY created_at;
                """,
                null,
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return строки.Select(с => с.Преобразовать()).ToArray();
    }

    /// <summary>
    /// Подсчитывает сопряжённые устройства: строки устройств при этом не
    /// читаются, поэтому отпечатки секретов в память не попадают.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число сопряжённых устройств.</returns>
    public async Task<int> ПодсчитатьУстройстваAsync(CancellationToken cancellationToken = default)
    {
        return await _сессия.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM shared_devices;",
                null,
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Отзывает доступ отдельного устройства.
    /// </summary>
    /// <param name="deviceId">Идентификатор устройства.</param>
    /// <param name="причина">Причина отзыва.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если устройство было отозвано.</returns>
    public async Task<bool> ОтозватьAsync(
        string deviceId,
        string? причина,
        CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE shared_devices
                SET revoked_at = @Now, revoked_reason = @Reason
                WHERE device_id = @Id AND revoked_at IS NULL;
                """,
                new
                {
                    Id = deviceId,
                    Reason = причина,
                    Now = Момент(DateTime.UtcNow),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <summary>
    /// Фиксирует момент последнего обращения устройства.
    /// </summary>
    /// <param name="deviceId">Идентификатор устройства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task ОтметитьОбращениеAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE shared_devices SET last_seen_at = @Now WHERE device_id = @Id;
                """,
                new
                {
                    Id = deviceId,
                    Now = Момент(DateTime.UtcNow),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Текущий момент в формате, принятом в базе (UTC).
    /// </summary>
    /// <param name="момент">Момент времени.</param>
    /// <returns>Текст момента.</returns>
    private static string Момент(DateTime момент) => момент.ToString(
        "yyyy-MM-dd HH:mm:ss.fff",
        CultureInfo.InvariantCulture);

    private sealed class СтрокаУстройства
    {
        public string DeviceId { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string SecretHash { get; init; } = string.Empty;

        public string CreatedAt { get; init; } = string.Empty;

        public string? LastSeenAt { get; init; }

        public string? RevokedAt { get; init; }

        public string? RevokedReason { get; init; }

        public SharedDevice Преобразовать() => new(
            DeviceId,
            Name,
            SecretHash,
            Разобрать(CreatedAt) ?? DateTime.MinValue,
            Разобрать(LastSeenAt),
            Разобрать(RevokedAt),
            RevokedReason);

        /// <summary>
        /// Разбирает момент времени из базы. Пустое значение означает «момента
        /// не было» и возвращает <see langword="null"/>: значение по
        /// умолчанию <c>DateTime.MinValue</c> означало бы, что событие
        /// произошло, и устройство выглядело бы отозванным.
        /// </summary>
        /// <param name="значение">Текст момента времени.</param>
        /// <returns>Момент времени либо <see langword="null"/>.</returns>
        private static DateTime? Разобрать(string? значение) =>
            значение is not null
            && DateTime.TryParse(
                значение,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var разобранное)
                ? разобранное
                : null;
    }
}
