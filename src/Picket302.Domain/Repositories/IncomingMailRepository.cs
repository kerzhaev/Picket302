using Picket302.Domain.Entities;

namespace Picket302.Domain.Repositories;

/// <summary>
/// Репозиторий журнала входящей корреспонденции (ТЗ §8.2, функция 29).
/// </summary>
public interface IIncomingMailRepository
{
    /// <summary>Регистрирует входящее отправление.</summary>
    /// <param name="mail">Запись журнала.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор записи.</returns>
    Task<long> AddAsync(IncomingMail mail, CancellationToken cancellationToken = default);

    /// <summary>Возвращает запись по идентификатору.</summary>
    /// <param name="id">Идентификатор.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись либо <see langword="null"/>.</returns>
    Task<IncomingMail?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Поиск по подстроке регистрационного номера, отправителя или содержания.
    /// </summary>
    /// <param name="query">Подстрока поиска; пустая — весь журнал.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Записи в порядке убывания даты регистрации.</returns>
    Task<IReadOnlyList<IncomingMail>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет запись журнала.</summary>
    /// <param name="id">Идентификатор.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
