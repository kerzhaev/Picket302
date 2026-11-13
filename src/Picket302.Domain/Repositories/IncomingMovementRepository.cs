using Picket302.Domain.Entities;

namespace Picket302.Domain.Repositories;

/// <summary>
/// Репозиторий движений входящего документа (ТЗ §8.2, функция 30). Поверхность
/// — только дополнение и чтение: история движений не редактируется и не
/// удаляется (критерий приёмки «история не переписывается»).
/// </summary>
public interface IIncomingMovementRepository
{
    /// <summary>Добавляет движение (append-only).</summary>
    /// <param name="movement">Движение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор записи.</returns>
    Task<long> AddAsync(IncomingMovement movement, CancellationToken cancellationToken = default);

    /// <summary>История движений документа в хронологическом порядке.</summary>
    /// <param name="incomingId">Документ журнала входящей корреспонденции.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Движения по возрастанию момента.</returns>
    Task<IReadOnlyList<IncomingMovement>> GetByIncomingAsync(
        long incomingId,
        CancellationToken cancellationToken = default);
}
