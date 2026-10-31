namespace Picket302.Domain;

/// <summary>
/// Единица работы над базой данных: объединяет несколько операций в одну транзакцию.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Признак открытой транзакции.</summary>
    bool InTransaction { get; }

    /// <summary>Открывает транзакцию.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения открытия транзакции.</returns>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Фиксирует транзакцию.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения фиксации.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Откатывает транзакцию.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения отката.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
