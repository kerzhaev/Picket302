using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий рассылок на SQLite (ТЗ §3.1.10).
/// </summary>
public sealed class DistributionRepository : IDistributionRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий рассылок.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public DistributionRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(Distribution distribution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(distribution);

        distribution.CreatedAt = distribution.CreatedAt == default ? DateTime.UtcNow : distribution.CreatedAt;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO distributions (name, sender_id, kind, note, state, envelopes_required, created_at, dispatched_at)
            VALUES (@Name, @SenderId, @Kind, @Note, @State, @EnvelopesRequired, @CreatedAt, @DispatchedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                distribution.Name,
                distribution.SenderId,
                Kind = (int)distribution.Kind,
                distribution.Note,
                State = (int)distribution.State,
                distribution.EnvelopesRequired,
                distribution.CreatedAt,
                distribution.DispatchedAt,
            },
            cancellationToken).ConfigureAwait(false);

        distribution.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<Distribution?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<Distribution>(
            "SELECT * FROM distributions WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Distribution>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var рассылки = await _сессия.QueryAsync<Distribution>(
            "SELECT * FROM distributions ORDER BY created_at, id;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return рассылки.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(Distribution distribution, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(distribution);

        return await _сессия.ExecuteAsync(
            """
            UPDATE distributions SET name = @Name, sender_id = @SenderId, kind = @Kind, note = @Note,
                state = @State, envelopes_required = @EnvelopesRequired, dispatched_at = @DispatchedAt
            WHERE id = @Id;
            """,
            new
            {
                distribution.Name,
                distribution.SenderId,
                Kind = (int)distribution.Kind,
                distribution.Note,
                State = (int)distribution.State,
                distribution.EnvelopesRequired,
                distribution.DispatchedAt,
                distribution.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM distributions WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <inheritdoc />
    public async Task<int> RecalculateEnvelopesRequiredAsync(
        long distributionId,
        CancellationToken cancellationToken = default)
    {
        var количество = await _сессия.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(copy_count), 0) FROM mails WHERE distribution_id = @DistributionId;",
            new { DistributionId = distributionId },
            cancellationToken).ConfigureAwait(false);

        await _сессия.ExecuteAsync(
            "UPDATE distributions SET envelopes_required = @EnvelopesRequired WHERE id = @Id;",
            new { EnvelopesRequired = (int)количество, Id = distributionId },
            cancellationToken).ConfigureAwait(false);

        return (int)количество;
    }
}
