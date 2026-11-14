using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Infrastructure.Data;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Data;

/// <summary>
/// Хранилище маркеров версий общих сущностей.
/// </summary>
/// <remarks>
/// <para>
/// Маркер версии — проверяемый признак того, что запись не менялась с
/// момента чтения. Клиент предъявляет версию при сохранении; если запись
/// успели изменить с другого рабочего места, сохранение отклоняется с
/// сообщением о конфликте и <b>ничего не пишется</b>.
/// </para>
/// <para>
/// Сравнение и увеличение версии выполняются одной транзакцией, поэтому две
/// одновременные правки не могут обе завершиться успешно: вторая увидит
/// увеличенную версию.
/// </para>
/// </remarks>
public sealed class SharedRowVersionStore
{
    private readonly ISqliteSession _сессия;

    /// <summary>
    /// Создаёт хранилище над указанной сессией.
    /// </summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public SharedRowVersionStore(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _сессия = сессия;
    }

    /// <summary>
    /// Читает текущую версию записи.
    /// </summary>
    /// <param name="вид">Вид общей сущности.</param>
    /// <param name="идентификатор">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Версия записи; 0 — версия ещё не фиксировалась (запись существовала до
    /// включения общего режима).
    /// </returns>
    public async Task<long> ПолучитьВерсиюAsync(
        SharedEntityKind вид,
        long идентификатор,
        CancellationToken cancellationToken = default)
    {
        var значения = await ПолучитьВерсииAsync(
            вид,
            new[] { идентификатор },
            cancellationToken).ConfigureAwait(false);

        return значения.TryGetValue(идентификатор, out var версия) ? версия : 0;
    }

    /// <summary>
    /// Читает версии нескольких записей одним запросом.
    /// </summary>
    /// <param name="вид">Вид общей сущности.</param>
    /// <param name="идентификаторы">Идентификаторы записей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Версии записей; отсутствующие записи отсутствуют и в словаре.</returns>
    public async Task<IReadOnlyDictionary<long, long>> ПолучитьВерсииAsync(
        SharedEntityKind вид,
        IReadOnlyList<long> идентификаторы,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(идентификаторы);

        if (идентификаторы.Count == 0)
        {
            return new Dictionary<long, long>();
        }

        var строки = await _сессия.Connection.QueryAsync<СтрокаВерсии>(
            new CommandDefinition(
                """
                SELECT entity_id AS EntityId, version AS Version
                FROM shared_row_versions
                WHERE entity_kind = @Kind AND entity_id IN @Ids;
                """,
                new { Kind = (int)вид, Ids = идентификаторы },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        var результат = new Dictionary<long, long>();

        foreach (var строка in строки)
        {
            результат[строка.EntityId] = строка.Version;
        }

        return результат;
    }

    /// <summary>
    /// Увеличивает версию записи, если она совпадает с ожидаемой.
    /// </summary>
    /// <param name="вид">Вид общей сущности.</param>
    /// <param name="идентификатор">Идентификатор записи.</param>
    /// <param name="ожидаемаяВерсия">Версия, предъявленная клиентом.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// <see langword="true"/>, если версия совпала и была увеличена;
    /// <see langword="false"/> — версия устарела (запись изменена другим
    /// клиентом).
    /// </returns>
    public async Task<bool> УвеличитьВерсиюAsync(
        SharedEntityKind вид,
        long идентификатор,
        long ожидаемаяВерсия,
        CancellationToken cancellationToken = default)
    {
        var увеличено = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE shared_row_versions
                SET version = version + 1, updated_at = @Now
                WHERE entity_kind = @Kind AND entity_id = @Id AND version = @Expected;
                """,
                new
                {
                    Kind = (int)вид,
                    Id = идентификатор,
                    Expected = ожидаемаяВерсия,
                    Now = Момент(),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (увеличено > 0)
        {
            return true;
        }

        if (ожидаемаяВерсия != 0)
        {
            // Версия записи уже зафиксирована и не совпадает с предъявленной:
            // запись изменена другим рабочим местом.
            return false;
        }

        // Версии ещё не было: создаём её. Конкурентное создание отсеивается
        // ограничением первичного ключа.
        var создано = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_row_versions (entity_kind, entity_id, version, updated_at)
                VALUES (@Kind, @Id, 1, @Now)
                ON CONFLICT (entity_kind, entity_id) DO NOTHING;
                """,
                new
                {
                    Kind = (int)вид,
                    Id = идентификатор,
                    Now = Момент(),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return создано > 0;
    }

    /// <summary>
    /// Фиксирует первую версию вновь созданной записи.
    /// </summary>
    /// <param name="вид">Вид общей сущности.</param>
    /// <param name="идентификатор">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task СоздатьВерсиюAsync(
        SharedEntityKind вид,
        long идентификатор,
        CancellationToken cancellationToken = default)
    {
        await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_row_versions (entity_kind, entity_id, version, updated_at)
                VALUES (@Kind, @Id, 1, @Now)
                ON CONFLICT (entity_kind, entity_id) DO NOTHING;
                """,
                new
                {
                    Kind = (int)вид,
                    Id = идентификатор,
                    Now = Момент(),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Текущий момент в формате, принятом в базе (UTC).
    /// </summary>
    /// <returns>Момент времени.</returns>
    private static string Момент() => DateTime.UtcNow.ToString(
        "yyyy-MM-dd HH:mm:ss.fff",
        System.Globalization.CultureInfo.InvariantCulture);

    private sealed class СтрокаВерсии
    {
        public long EntityId { get; init; }

        public long Version { get; init; }
    }
}
