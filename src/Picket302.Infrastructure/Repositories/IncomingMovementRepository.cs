using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий движений входящих документов на SQLite (ТЗ §8.2, функция 30).
/// Только добавление и чтение: истории движений не предусмотрены операции
/// изменения — «история не переписывается».
/// </summary>
public sealed class IncomingMovementRepository : IIncomingMovementRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий движений входящих документов.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public IncomingMovementRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(IncomingMovement movement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO incoming_movements (incoming_id, moved_at, action, department, executor)
            VALUES (@IncomingId, @MovedAt, @Action, @Department, @Executor);
            SELECT last_insert_rowid();
            """,
            new
            {
                movement.IncomingId,
                MovedAt = ФорматДаты(movement.MovedAt),
                Action = (long)movement.Action,
                movement.Department,
                movement.Executor,
            },
            cancellationToken).ConfigureAwait(false);

        movement.Id = идентификатор;

        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncomingMovement>> GetByIncomingAsync(
        long incomingId,
        CancellationToken cancellationToken = default)
    {
        var движения = await _сессия.QueryAsync<IncomingMovement>(
            """
            SELECT * FROM incoming_movements
            WHERE incoming_id = @IncomingId
            ORDER BY moved_at, id;
            """,
            new { IncomingId = incomingId },
            cancellationToken).ConfigureAwait(false);

        return движения.ToList();
    }

    /// <summary>Формат даты для хранения: сортируемая строка UTC.</summary>
    private static string ФорматДаты(DateTime момент) => момент
        .ToUniversalTime()
        .ToString(SqliteDateTimeHandler.Формат, System.Globalization.CultureInfo.InvariantCulture);
}
