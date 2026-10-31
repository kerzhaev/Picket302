using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий хранилища параметров «ключ — значение» на SQLite (ТЗ §3.1.17).
/// </summary>
public sealed class ParameterRepository : IParameterRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий параметров.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public ParameterRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(Parameter parameter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.UpdatedAt = DateTime.UtcNow;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO parameters (name, value, updated_at)
            VALUES (@Name, @Value, @UpdatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                parameter.Name,
                parameter.Value,
                parameter.UpdatedAt,
            },
            cancellationToken).ConfigureAwait(false);

        parameter.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<Parameter?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<Parameter>(
            "SELECT * FROM parameters WHERE name = @Name;",
            new { Name = name },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default)
    {
        var значение = await _сессия.QueryFirstOrDefaultAsync<string>(
            "SELECT value FROM parameters WHERE name = @Name;",
            new { Name = name },
            cancellationToken).ConfigureAwait(false);

        return значение;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Parameter>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var параметры = await _сессия.QueryAsync<Parameter>(
            "SELECT * FROM parameters ORDER BY name;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return параметры.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(Parameter parameter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.UpdatedAt = DateTime.UtcNow;

        return await _сессия.ExecuteAsync(
            "UPDATE parameters SET value = @Value, updated_at = @UpdatedAt WHERE name = @Name;",
            new
            {
                parameter.Value,
                parameter.UpdatedAt,
                parameter.Name,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM parameters WHERE name = @Name;",
            new { Name = name },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
