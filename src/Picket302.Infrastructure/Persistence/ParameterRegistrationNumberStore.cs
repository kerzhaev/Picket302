using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Domain.Repositories;
using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Persistence;

/// <summary>
/// Хранилище сквозного счётчика регистрационных номеров в таблице параметров
/// (ТЗ §8.1, функция 25: «Сквозная регистрационная нумерация»).
/// </summary>
/// <remarks>
/// Инкремент выполняется одной SQL-командой: операция <c>INSERT … ON CONFLICT
/// DO UPDATE … RETURNING</c> атомарна, поэтому два параллельных запроса
/// получают разные последовательные номера без коллизий.
/// </remarks>
public sealed class ParameterRegistrationNumberStore : IRegistrationNumberStore
{
    /// <summary>
    /// Код ошибки SQLite «база занята» (SQLITE_BUSY).
    /// </summary>
    private const int КодБазыЗанята = 5;

    /// <summary>
    /// Код ошибки SQLite «таблица заблокирована» (SQLITE_LOCKED).
    /// </summary>
    private const int КодТаблицаЗаблокирована = 6;

    /// <summary>
    /// Число попыток повтора при конкурентной записи.
    /// </summary>
    private const int ЧислоПопыток = 100;

    /// <summary>
    /// Пауза между попытками повтора, миллисекунды.
    /// </summary>
    private const int ЗадержкаПопыткиМс = 20;

    private readonly ISqliteSession _сессия;
    private readonly SemaphoreSlim _блокировка = new(1, 1);

    /// <summary>Создаёт хранилище счётчика номеров.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public ParameterRegistrationNumberStore(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> NextAsync(string counterKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(counterKey);

        DapperConfiguration.Применить();

        // Одно соединение обслуживает один запрос: обращения сериализуются, чтобы
        // параллельные вызовы получали разные номера без коллизий.
        await _блокировка.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            for (var попытка = 1; ; попытка++)
            {
                try
                {
                    var значение = await _сессия.Connection.ExecuteScalarAsync<long>(
                        new CommandDefinition(
                            """
                            INSERT INTO parameters (name, value, updated_at)
                            VALUES (@Name, '1', @UpdatedAt)
                            ON CONFLICT(name) DO UPDATE SET
                                value = CAST(CAST(parameters.value AS INTEGER) + 1 AS TEXT),
                                updated_at = @UpdatedAt
                            RETURNING CAST(value AS INTEGER);
                            """,
                            new
                            {
                                Name = counterKey,
                                UpdatedAt = DateTime.UtcNow.ToString(
                                    "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                            },
                            _сессия.Transaction,
                            cancellationToken: cancellationToken)).ConfigureAwait(false);

                    return значение;
                }
                catch (SqliteException исключение) when (
                    исключение.SqliteErrorCode is КодБазыЗанята or КодТаблицаЗаблокирована
                    && попытка < ЧислоПопыток)
                {
                    // Конкурентная запись: освобождение блокировки занимает
                    // единицы миллисекунд — повторяем попытку.
                    await Task.Delay(ЗадержкаПопыткиМс, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _блокировка.Release();
        }
    }

    /// <summary>
    /// Читает текущее значение счётчика без инкремента.
    /// </summary>
    /// <param name="counterKey">Ключ счётчика.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Текущее значение счётчика; 0 — счётчик ещё не использовался.</returns>
    public async Task<long> ReadAsync(string counterKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(counterKey);

        var значение = await _сессия.ExecuteScalarAsync<long?>(
            "SELECT CAST(value AS INTEGER) FROM parameters WHERE name = @Name;",
            new { Name = counterKey },
            cancellationToken).ConfigureAwait(false);

        return значение ?? 0;
    }
}
