using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий версий схемы базы данных на SQLite (ТЗ §3.1.18).
/// </summary>
public sealed class SchemaVersionRepository : ISchemaVersionRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий версий схемы.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public SchemaVersionRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(SchemaVersion schemaVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaVersion);

        schemaVersion.AppliedAt = schemaVersion.AppliedAt == default ? DateTime.UtcNow : schemaVersion.AppliedAt;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO schema_version (version, description, applied_at)
            VALUES (@Version, @Description, @AppliedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                schemaVersion.Version,
                schemaVersion.Description,
                schemaVersion.AppliedAt,
            },
            cancellationToken).ConfigureAwait(false);

        schemaVersion.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SchemaVersion>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var версии = await _сессия.QueryAsync<SchemaVersion>(
            "SELECT * FROM schema_version ORDER BY id;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return версии.ToList();
    }

    /// <inheritdoc />
    public async Task<SchemaVersion?> GetLatestAsync(CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<SchemaVersion>(
            "SELECT * FROM schema_version ORDER BY id DESC LIMIT 1;",
            cancellationToken: cancellationToken).ConfigureAwait(false);
}
