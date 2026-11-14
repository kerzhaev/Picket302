using System.Text.Json;
using Picket302.Composition;
using Picket302.Shared.Protocol;
using Picket302.Service.Data;

namespace Picket302.Service.Hosting;

/// <summary>
/// Область выполнения одной серверной команды: сессия базы, состав данных,
/// маркеры версий и журнал идемпотентности.
/// </summary>
/// <remarks>
/// <para>
/// Область открывает <b>одну</b> транзакцию на команду. Фиксация результата
/// в журнале идемпотентности выполняется в той же транзакции, поэтому
/// прерванный ответ клиенту не приводит к повторному письму: команда и
/// отметка о её выполнении фиксируются вместе.
/// </para>
/// <para>
/// Сессия освобождается при выходе из области — соединение не держится между
/// запросами.
/// </para>
/// </remarks>
public sealed class SharedDbScope : IAsyncDisposable
{
    private readonly SqliteUnitOfWorkHolder _держатель;

    private SharedDbScope(
        SqliteUnitOfWorkHolder держатель,
        SharedUnitOfWork единица,
        DataServices данные,
        SharedRowVersionStore версии,
        SharedOperationStore операции)
    {
        _держатель = держатель;
        Единица = единица;
        Данные = данные;
        Версии = версии;
        Операции = операции;
    }

    /// <summary>Единица работы команды.</summary>
    public SharedUnitOfWork Единица { get; }

    /// <summary>Состав слоя данных над сессией команды.</summary>
    public DataServices Данные { get; }

    /// <summary>Маркеры версий общих сущностей.</summary>
    public SharedRowVersionStore Версии { get; }

    /// <summary>Журнал идемпотентности.</summary>
    public SharedOperationStore Операции { get; }

    /// <summary>
    /// Создаёт область над новой сессией базы данных.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Готовая область.</returns>
    public static Task<SharedDbScope> СоздатьAsync(
        ServerDatabase база,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(база);

        var держатель = new SqliteUnitOfWorkHolder(база.СоздатьСессию());
        var единица = new SharedUnitOfWork(держатель.Сессия);
        var данные = new DataServices(единица);

        return Task.FromResult(new SharedDbScope(
            держатель,
            единица,
            данные,
            new SharedRowVersionStore(единица),
            new SharedOperationStore(единица)));
    }

    /// <summary>
    /// Открывает транзакцию команды.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения открытия транзакции.</returns>
    public Task ОткрытьТранзакциюAsync(CancellationToken cancellationToken = default)
        => Единица.ОткрытьТранзакциюКомандыAsync(cancellationToken);

    /// <summary>
    /// Фиксирует транзакцию команды.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения фиксации.</returns>
    public Task ЗафиксироватьAsync(CancellationToken cancellationToken = default)
        => Единица.ЗафиксироватьКомандуAsync(cancellationToken);

    /// <summary>
    /// Откатывает транзакцию команды, если она ещё открыта.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения отката.</returns>
    public Task ОткатитьAsync(CancellationToken cancellationToken = default)
        => Единица.ОткатитьКомандуAsync(cancellationToken);

    /// <summary>
    /// Фиксирует результат команды в журнале идемпотентности. Вызывается
    /// внутри транзакции команды.
    /// </summary>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="результат">Результат команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    public async Task СохранитьРезультатAsync<T>(
        string operationId,
        T результат,
        DateTime моментЗапуска,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(результат, SharedJson.Options);

        var зафиксировано = await Операции
            .СохранитьРезультатAsync(operationId, json, моментЗапуска, cancellationToken)
            .ConfigureAwait(false);

        if (!зафиксировано)
        {
            // Запись операции перехвачена другой попыткой: результат этой
            // попытки сохранять нельзя, команда откатывается целиком.
            throw new SharedOperationTakenException(
                "Операция была перехвачена другой попыткой. Повторите действие.");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _держатель.ОсвободитьAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Владелец сессии: освобождает соединение при выходе из области.
    /// </summary>
    private sealed class SqliteUnitOfWorkHolder
    {
        private readonly Picket302.Infrastructure.Data.SqliteUnitOfWork _сессия;

        public SqliteUnitOfWorkHolder(Picket302.Infrastructure.Data.SqliteUnitOfWork сессия)
        {
            _сессия = сессия;
        }

        public Picket302.Infrastructure.Data.SqliteUnitOfWork Сессия => _сессия;

        public async Task ОсвободитьAsync()
        {
            await _сессия.DisposeAsync().ConfigureAwait(false);
        }
    }
}
