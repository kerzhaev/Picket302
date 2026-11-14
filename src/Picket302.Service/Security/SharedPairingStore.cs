using System.Globalization;
using Dapper;
using Picket302.Infrastructure.Data;

namespace Picket302.Service.Security;

/// <summary>
/// Результат проверки кода сопряжения.
/// </summary>
/// <param name="Успешно">Признак успешной проверки.</param>
/// <param name="Причина">Пояснение отказа.</param>
/// <param name="PairingId">Идентификатор записи сопряжения.</param>
public sealed record SharedPairingResult(
    bool Успешно,
    string? Причина = null,
    string? PairingId = null);

/// <summary>
/// Хранилище одноразовых кодов сопряжения.
/// </summary>
/// <remarks>
/// <para>
/// Код создаётся только локально: экран администратора на серверном
/// компьютере. Удалённым запросом код получить нельзя — для этого служебные
/// запросы принимаются только с локального адреса и с административным
/// токеном.
/// </para>
/// <para>
/// В базе хранится только отпечаток кода в привязке к одноразовому значению
/// вызова; сам код и секрет устройства не сохраняются. Число попыток
/// ограничено, поэтому код нельзя подобрать перебором.
/// </para>
/// </remarks>
public sealed class SharedPairingStore
{
    private readonly ISqliteSession _сессия;

    /// <summary>
    /// Создаёт хранилище над указанной сессией.
    /// </summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public SharedPairingStore(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _сессия = сессия;
    }

    /// <summary>
    /// Создаёт одноразовый код сопряжения.
    /// </summary>
    /// <param name="pairingId">Идентификатор записи сопряжения.</param>
    /// <param name="codeHash">Отпечаток кода.</param>
    /// <param name="nonce">Одноразовое значение вызова.</param>
    /// <param name="expiresAt">Момент истечения кода (UTC).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task СоздатьAsync(
        string pairingId,
        string codeHash,
        string nonce,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_pairings
                    (pairing_id, code_hash, nonce, expires_at, max_uses, attempts, created_at)
                VALUES (@Id, @Hash, @Nonce, @Expires, 1, 0, @Now);
                """,
                new
                {
                    Id = pairingId,
                    Hash = codeHash,
                    Nonce = nonce,
                    Expires = Момент(expiresAt),
                    Now = Момент(DateTime.UtcNow),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет код сопряжения и отмечает его использованным.
    /// </summary>
    /// <param name="code">Предъявленный код.</param>
    /// <param name="nonce">Одноразовое значение вызова.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат проверки.</returns>
    public async Task<SharedPairingResult> ПроверитьAsync(
        string code,
        string nonce,
        CancellationToken cancellationToken = default)
    {
        var строка = await _сессия.Connection.QueryFirstOrDefaultAsync<СтрокаСопряжения>(
            new CommandDefinition(
                """
                SELECT pairing_id AS PairingId,
                       code_hash AS CodeHash,
                       expires_at AS ExpiresAt,
                       attempts AS Attempts,
                       used_at AS UsedAt
                FROM shared_pairings
                WHERE nonce = @Nonce;
                """,
                new { Nonce = nonce },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (строка is null)
        {
            return new SharedPairingResult(false, "Код сопряжения не найден.");
        }

        var попытки = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE shared_pairings SET attempts = attempts + 1
                WHERE pairing_id = @Id AND attempts < @Max;
                """,
                new
                {
                    Id = строка.PairingId,
                    Max = SharedSecrets.МаксимумПопытокСопряжения,
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (попытки == 0)
        {
            return new SharedPairingResult(
                false,
                "Число попыток сопряжения по этому коду исчерпано.");
        }

        if (строка.UsedAt is not null)
        {
            return new SharedPairingResult(false, "Код сопряжения уже использован.");
        }

        if (строка.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return new SharedPairingResult(false, "Срок действия кода сопряжения истёк.");
        }

        var отпечаток = SharedSecrets.ВычислитьОтпечатокКода(code, nonce);

        if (!SharedSecrets.СравнитьПостоянно(отпечаток, строка.CodeHash))
        {
            return new SharedPairingResult(false, "Код сопряжения неверный.");
        }

        var отмечено = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE shared_pairings SET used_at = @Now
                WHERE pairing_id = @Id AND used_at IS NULL;
                """,
                new
                {
                    Id = строка.PairingId,
                    Now = Момент(DateTime.UtcNow),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (отмечено == 0)
        {
            return new SharedPairingResult(false, "Код сопряжения уже использован.");
        }

        return new SharedPairingResult(true, null, строка.PairingId);
    }

    /// <summary>
    /// Удаляет коды сопряжения, срок которых истёк.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых записей.</returns>
    public async Task<int> УдалитьПросроченныеAsync(CancellationToken cancellationToken = default)
    {
        return await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM shared_pairings
                WHERE expires_at < @Now OR (used_at IS NOT NULL AND used_at < @Old);
                """,
                new
                {
                    Now = Момент(DateTime.UtcNow),
                    Old = Момент(DateTime.UtcNow.AddHours(-1)),
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

    private sealed class СтрокаСопряжения
    {
        public string PairingId { get; init; } = string.Empty;

        public string CodeHash { get; init; } = string.Empty;

        public string ExpiresAt { get; init; } = string.Empty;

        public int Attempts { get; init; }

        public string? UsedAt { get; init; }

        public DateTime ExpiresAtUtc =>
            DateTime.TryParse(
                ExpiresAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var разобранное)
                ? разобранное
                : DateTime.MinValue;
    }
}
