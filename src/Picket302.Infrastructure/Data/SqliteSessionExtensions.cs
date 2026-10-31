using System.Data;
using Dapper;

namespace Picket302.Infrastructure.Data;

/// <summary>
/// Вспомогательные методы выполнения SQL-команд в рамках сессии SQLite.
/// </summary>
internal static class SqliteSessionExtensions
{
    /// <summary>
    /// Выполняет команду в указанной транзакции, возвращая число затронутых строк.
    /// </summary>
    /// <param name="session">Сессия базы данных.</param>
    /// <param name="sql">Текст команды.</param>
    /// <param name="parameters">Параметры команды.</param>
    /// <param name="transaction">Транзакция выполнения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число затронутых строк.</returns>
    public static Task<int> ExecuteInTransactionAsync(
        this ISqliteSession session,
        string sql,
        object? parameters,
        IDbTransaction? transaction,
        CancellationToken cancellationToken = default)
    {
        DapperConfiguration.Применить();

        return session.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    /// <summary>Выполняет команду, возвращая число затронутых строк.</summary>
    /// <param name="session">Сессия базы данных.</param>
    /// <param name="sql">Текст команды.</param>
    /// <param name="parameters">Параметры команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число затронутых строк.</returns>
    public static Task<int> ExecuteAsync(
        this ISqliteSession session,
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        DapperConfiguration.Применить();

        return session.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            session.Transaction,
            cancellationToken: cancellationToken));
    }

    /// <summary>Выполняет запрос, возвращая первую строку либо значение по умолчанию.</summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="session">Сессия базы данных.</param>
    /// <param name="sql">Текст запроса.</param>
    /// <param name="parameters">Параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Первая строка результата либо значение по умолчанию.</returns>
    public static async Task<T?> QueryFirstOrDefaultAsync<T>(
        this ISqliteSession session,
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        DapperConfiguration.Применить();

        return await session.Connection.QueryFirstOrDefaultAsync<T>(new CommandDefinition(
            sql,
            parameters,
            session.Transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Выполняет запрос, возвращая первую строку.</summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="session">Сессия базы данных.</param>
    /// <param name="sql">Текст запроса.</param>
    /// <param name="parameters">Параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Первая строка результата.</returns>
    public static async Task<T> QuerySingleAsync<T>(
        this ISqliteSession session,
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        DapperConfiguration.Применить();

        return await session.Connection.QuerySingleAsync<T>(new CommandDefinition(
            sql,
            parameters,
            session.Transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Выполняет запрос, возвращая все строки.</summary>
    /// <typeparam name="T">Тип строки.</typeparam>
    /// <param name="session">Сессия базы данных.</param>
    /// <param name="sql">Текст запроса.</param>
    /// <param name="parameters">Параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Коллекция строк результата.</returns>
    public static async Task<IEnumerable<T>> QueryAsync<T>(
        this ISqliteSession session,
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        DapperConfiguration.Применить();

        return await session.Connection.QueryAsync<T>(new CommandDefinition(
            sql,
            parameters,
            session.Transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Выполняет запрос, возвращающий единственное скалярное значение.</summary>
    /// <typeparam name="T">Тип значения.</typeparam>
    /// <param name="session">Сессия базы данных.</param>
    /// <param name="sql">Текст запроса.</param>
    /// <param name="parameters">Параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Скалярное значение результата.</returns>
    public static async Task<T> ExecuteScalarAsync<T>(
        this ISqliteSession session,
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        DapperConfiguration.Применить();

        var значение = await session.Connection.ExecuteScalarAsync<T>(new CommandDefinition(
            sql,
            parameters,
            session.Transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return значение!;
    }
}
