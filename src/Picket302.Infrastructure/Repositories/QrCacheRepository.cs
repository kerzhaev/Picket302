using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий кэша сгенерированных кодов на SQLite (ТЗ §3.1.8).
/// </summary>
public sealed class QrCacheRepository : IQrCacheRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий кэша кодов.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public QrCacheRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(QrCache cache, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);

        cache.CreatedAt = cache.CreatedAt == default ? DateTime.UtcNow : cache.CreatedAt;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO qr_cache (payload_hash, image_blob, created_at)
            VALUES (@PayloadHash, @ImageBlob, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                cache.PayloadHash,
                cache.ImageBlob,
                cache.CreatedAt,
            },
            cancellationToken).ConfigureAwait(false);

        cache.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<QrCache?> GetByPayloadHashAsync(
        string payloadHash,
        CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<QrCache>(
            "SELECT * FROM qr_cache WHERE payload_hash = @PayloadHash;",
            new { PayloadHash = payloadHash },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM qr_cache WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
