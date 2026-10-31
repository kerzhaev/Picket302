using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий движений входящих документов на SQLite (ТЗ §3.1.14).
/// </summary>
public sealed class DocumentMovementRepository : IDocumentMovementRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий движений входящих документов.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public DocumentMovementRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(DocumentMovement movement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);

        movement.MovedAt = movement.MovedAt == default ? DateTime.UtcNow : movement.MovedAt;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO document_movements (mail_id, moved_at, target_department, executor, action)
            VALUES (@MailId, @MovedAt, @TargetDepartment, @Executor, @Action);
            SELECT last_insert_rowid();
            """,
            new
            {
                movement.MailId,
                movement.MovedAt,
                movement.TargetDepartment,
                movement.Executor,
                Action = movement.Action is null ? (int?)null : (int)movement.Action.Value,
            },
            cancellationToken).ConfigureAwait(false);

        movement.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentMovement>> GetByMailAsync(
        long mailId,
        CancellationToken cancellationToken = default)
    {
        var движения = await _сессия.QueryAsync<DocumentMovement>(
            "SELECT * FROM document_movements WHERE mail_id = @MailId ORDER BY moved_at, id;",
            new { MailId = mailId },
            cancellationToken).ConfigureAwait(false);

        return движения.ToList();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM document_movements WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
