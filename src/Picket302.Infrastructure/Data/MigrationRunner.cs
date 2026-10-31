using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Picket302.Infrastructure.Data;

/// <summary>
/// Исполнитель миграций схемы базы данных (ТЗ §3.1.18, §3.1.19; §10.2).
/// </summary>
/// <remarks>
/// <para>
/// Миграции применяются только вперед (forward-only). Каждая миграция выполняется
/// в собственной транзакции: применение скрипта и запись строки в
/// <c>schema_version</c> либо завершаются целиком, либо откатываются.
/// Откат схемы вручную не поддерживается — восстановление выполняется из архива БД.
/// </para>
/// <para>
/// Если база содержит версию схемы, которой нет в сборке, применение прерывается:
/// версия схемы новее программы.
/// </para>
/// </remarks>
public sealed class MigrationRunner
{
    private readonly IReadOnlyList<Migration> _миграции;

    /// <summary>Создаёт исполнитель миграций для сборки инфраструктуры.</summary>
    public MigrationRunner()
        : this(typeof(MigrationRunner).Assembly)
    {
    }

    /// <summary>Создаёт исполнитель миграций для указанной сборки.</summary>
    /// <param name="assembly">Сборка с встроенными ресурсами миграций.</param>
    public MigrationRunner(System.Reflection.Assembly assembly)
    {
        _миграции = MigrationCatalog.Load(assembly);
    }

    /// <summary>Доступные миграции, упорядоченные по возрастанию версии.</summary>
    public IReadOnlyList<Migration> Migrations => _миграции;

    /// <summary>
    /// Применяет все ещё не применённые миграции.
    /// </summary>
    /// <param name="connection">Открытое соединение с базой данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список применённых миграций.</returns>
    /// <exception cref="InvalidOperationException">
    /// Версия схемы в базе новее версии, известной программе.
    /// </exception>
    public async Task<IReadOnlyList<AppliedMigration>> ApplyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        DapperConfiguration.Применить();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await ВыполнитьНастройкиСоединенияAsync(connection, cancellationToken).ConfigureAwait(false);

        var применённые = await ПрочитатьПрименённыеAsync(connection, cancellationToken).ConfigureAwait(false);
        ПроверитьСовместимостьВерсии(применённые);

        var результат = new List<AppliedMigration>();

        foreach (var миграция in _миграции)
        {
            if (применённые.ContainsKey(миграция.Version))
            {
                continue;
            }

            результат.Add(await ПрименитьAsync(connection, миграция, cancellationToken).ConfigureAwait(false));
        }

        return результат;
    }

    /// <summary>
    /// Возвращает версию схемы, зафиксированную в базе данных.
    /// </summary>
    /// <param name="connection">Открытое соединение с базой данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Номер последней применённой версии либо <see langword="null"/>.</returns>
    public async Task<string?> GetCurrentVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var применённые = await ПрочитатьПрименённыеAsync(connection, cancellationToken).ConfigureAwait(false);

        return применённые.Count == 0
            ? null
            : применённые.Keys.OrderBy(версия => int.Parse(версия, CultureInfo.InvariantCulture)).Last();
    }

    /// <summary>
    /// Настраивает соединение: включает проверку внешних ключей.
    /// </summary>
    /// <param name="connection">Открытое соединение с базой данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения настройки.</returns>
    private static async Task ВыполнитьНастройкиСоединенияAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        // PRAGMA foreign_keys нельзя менять внутри транзакции — устанавливается
        // до её открытия.
        await connection.ExecuteAsync(new CommandDefinition(
            "PRAGMA foreign_keys = ON;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// Читает журнал применённых миграций.
    /// </summary>
    /// <param name="connection">Открытое соединение с базой данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Словарь «версия — описание».</returns>
    private static async Task<IReadOnlyDictionary<string, string>> ПрочитатьПрименённыеAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var таблицаСуществует = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_version';",
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (таблицаСуществует == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var строки = await connection.QueryAsync<(string Version, string? Description)>(new CommandDefinition(
            "SELECT version, description FROM schema_version ORDER BY id;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return строки.ToDictionary(
            строка => строка.Version,
            строка => строка.Description ?? string.Empty,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Проверяет, что версия схемы в базе не новее известной программе.
    /// </summary>
    /// <param name="применённые">Журнал применённых миграций.</param>
    private void ПроверитьСовместимостьВерсии(IReadOnlyDictionary<string, string> применённые)
    {
        if (применённые.Count == 0)
        {
            return;
        }

        var известные = _миграции.Select(миграция => миграция.Version).ToHashSet(StringComparer.Ordinal);
        var неизвестные = применённые.Keys.Where(версия => !известные.Contains(версия)).ToList();

        if (неизвестные.Count > 0)
        {
            throw new InvalidOperationException(
                "Версия схемы базы данных новее установленной программы. "
                + $"Требуется версия: {string.Join(", ", неизвестные.OrderBy(v => int.Parse(v, CultureInfo.InvariantCulture)))}.");
        }
    }

    /// <summary>
    /// Применяет одну миграцию в отдельной транзакции.
    /// </summary>
    /// <param name="connection">Открытое соединение с базой данных.</param>
    /// <param name="миграция">Применяемая миграция.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сведения о применённой миграции.</returns>
    private static async Task<AppliedMigration> ПрименитьAsync(
        SqliteConnection connection,
        Migration миграция,
        CancellationToken cancellationToken)
    {
        try
        {
            if (миграция.RequiresForeignKeysOff)
            {
                // PRAGMA foreign_keys не действует внутри транзакции, поэтому
                // проверка внешних ключей отключается до её открытия.
                await УстановитьВнешниеКлючиAsync(connection, false, cancellationToken).ConfigureAwait(false);
            }

            using var транзакция = connection.BeginTransaction(deferred: false);

            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    миграция.Sql,
                    transaction: транзакция,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                var момент = DateTime.UtcNow;

                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO schema_version (version, description, applied_at) VALUES (@Version, @Description, @AppliedAt);",
                    new
                    {
                        миграция.Version,
                        миграция.Description,
                        AppliedAt = момент,
                    },
                    транзакция,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                await транзакция.CommitAsync(cancellationToken).ConfigureAwait(false);

                return new AppliedMigration(миграция.Version, миграция.Description, момент);
            }
            catch (SqliteException ошибка)
            {
                await транзакция.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"Миграция {миграция.Version} ({миграция.Name}) не выполнена: {ошибка.Message}",
                    ошибка);
            }
        }
        finally
        {
            if (миграция.RequiresForeignKeysOff)
            {
                // Проверка внешних ключей возвращается в исходное состояние
                // независимо от результата миграции.
                await УстановитьВнешниеКлючиAsync(connection, true, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Включает или отключает проверку внешних ключей соединения.
    /// </summary>
    /// <param name="connection">Открытое соединение с базой данных.</param>
    /// <param name="включить">Включить ли проверку.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения настройки.</returns>
    private static async Task УстановитьВнешниеКлючиAsync(
        SqliteConnection connection,
        bool включить,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            включить ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
