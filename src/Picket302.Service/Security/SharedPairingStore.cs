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
    /// <summary>Ключ счётчика неудачных попыток сопряжения.</summary>
    private const string ИмяСчётчика = "pairing-failures";

    /// <summary>Ключ момента последней неудачной попытки.</summary>
    private const string ИмяПоследнейПопытки = "pairing-failures-at";

    /// <summary>Окно, за пределами которого счётчик неудачных попыток сбрасывается.</summary>
    private static readonly TimeSpan ОкноПопыток = TimeSpan.FromMinutes(10);

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
    /// <param name="secretHash">Отпечаток секретной части кода.</param>
    /// <param name="fragment">Фрагмент отпечатка сервера, входящий в код.</param>
    /// <param name="expiresAt">Момент истечения кода (UTC).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task СоздатьAsync(
        string pairingId,
        string secretHash,
        string fragment,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_pairings
                    (pairing_id, secret_hash, fragment, expires_at, max_uses, attempts, created_at)
                VALUES (@Id, @Hash, @Fragment, @Expires, 1, 0, @Now);
                """,
                new
                {
                    Id = pairingId,
                    Hash = secretHash,
                    Fragment = fragment,
                    Expires = Момент(expiresAt),
                    Now = Момент(DateTime.UtcNow),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет код сопряжения и отмечает его использованным.
    /// </summary>
    /// <param name="secretHash">Отпечаток секретной части предъявленного кода.</param>
    /// <param name="fragment">Фрагмент отпечатка, предъявленный клиентом.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат проверки.</returns>
    public async Task<SharedPairingResult> ПроверитьAsync(
        string secretHash,
        string fragment,
        CancellationToken cancellationToken = default)
    {
        var строка = await _сессия.Connection.QueryFirstOrDefaultAsync<СтрокаСопряжения>(
            new CommandDefinition(
                """
                SELECT pairing_id AS PairingId,
                       secret_hash AS SecretHash,
                       fragment AS Fragment,
                       expires_at AS ExpiresAt,
                       attempts AS Attempts,
                       used_at AS UsedAt
                FROM shared_pairings
                WHERE secret_hash = @Hash;
                """,
                new { Hash = secretHash },
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

        if (!SharedSecrets.СравнитьПостоянно(fragment, строка.Fragment))
        {
            return new SharedPairingResult(
                false,
                "Код сопряжения выдан другому серверу.");
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
    /// Регистрирует неудачную попытку сопряжения и сообщает, не превышен ли
    /// предел.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Попытки, при которых секретная часть кода не найдена, тоже учитываются:
    /// иначе перебор секрета не ограничивался бы ничем.
    /// </para>
    /// <para>
    /// Счётчик сбрасывается, если последняя неудача была давно: после
    /// перерыва в попытках попытка честного оператора не должна отклоняться
    /// из-за чужих старых попыток.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если предел попыток превышен.</returns>
    public async Task<bool> ПревышенПределПопытокAsync(CancellationToken cancellationToken = default)
    {
        var сейчас = DateTime.UtcNow;

        var последняя = await _сессия.Connection.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT value FROM shared_service_meta WHERE name = @Name;",
                new { Name = ИмяПоследнейПопытки },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var давность = DateTime.TryParse(
            последняя,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var разобранное)
            ? сейчас - разобранное
            : TimeSpan.MaxValue;

        if (давность > ОкноПопыток)
        {
            // Давняя неудача не должна влиять на нынешние попытки.
            await _сессия.Connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO shared_service_meta (name, value) VALUES (@Name, '0')
                    ON CONFLICT (name) DO UPDATE SET value = '0';
                    """,
                    new { Name = ИмяСчётчика },
                    _сессия.Transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        var счётчик = await _сессия.Connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                SELECT CAST(value AS INTEGER) FROM shared_service_meta WHERE name = @Name;
                """,
                new { Name = ИмяСчётчика },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return счётчик >= SharedSecrets.МаксимумПопытокСопряжения;
    }

    /// <summary>
    /// Учитывает неудачную попытку сопряжения.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task ЗарегистрироватьНеудачнуюПопыткуAsync(CancellationToken cancellationToken = default)
    {
        await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_service_meta (name, value) VALUES (@Name, '1')
                ON CONFLICT (name) DO UPDATE SET
                    value = CAST(CAST(value AS INTEGER) + 1 AS TEXT);
                """,
                new { Name = ИмяСчётчика },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_service_meta (name, value) VALUES (@Name, @Now)
                ON CONFLICT (name) DO UPDATE SET value = @Now;
                """,
                new { Name = ИмяПоследнейПопытки, Now = Момент(DateTime.UtcNow) },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
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

        public string SecretHash { get; init; } = string.Empty;

        public string Fragment { get; init; } = string.Empty;

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
