using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий шаблонов конвертов и бланков на SQLite (ТЗ §3.1.4).
/// </summary>
public sealed class PrintTemplateRepository : IPrintTemplateRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий шаблонов.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public PrintTemplateRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(PrintTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO print_templates (name, kind, format, width_mm, height_mm, orientation, file_path, is_builtin, country,
                package_id, package_version, package_hash)
            VALUES (@Name, @Kind, @Format, @WidthMm, @HeightMm, @Orientation, @FilePath, @IsBuiltin, @Country,
                @PackageId, @PackageVersion, @PackageHash);
            SELECT last_insert_rowid();
            """,
            new
            {
                template.Name,
                template.Kind,
                template.Format,
                template.WidthMm,
                template.HeightMm,
                Orientation = (int)template.Orientation,
                template.FilePath,
                template.IsBuiltin,
                template.Country,
                template.PackageId,
                template.PackageVersion,
                template.PackageHash,
            },
            cancellationToken).ConfigureAwait(false);

        template.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<PrintTemplate?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<PrintTemplate>(
            "SELECT * FROM print_templates WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintTemplate>> GetAllAsync(
        string? kind = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            var все = await _сессия.QueryAsync<PrintTemplate>(
                "SELECT * FROM print_templates ORDER BY name, id;",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return все.ToList();
        }

        var отфильтрованные = await _сессия.QueryAsync<PrintTemplate>(
            "SELECT * FROM print_templates WHERE kind = @Kind ORDER BY name, id;",
            new { Kind = kind },
            cancellationToken).ConfigureAwait(false);

        return отфильтрованные.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(PrintTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        return await _сессия.ExecuteAsync(
            """
            UPDATE print_templates SET name = @Name, kind = @Kind, format = @Format, width_mm = @WidthMm,
                height_mm = @HeightMm, orientation = @Orientation, file_path = @FilePath, is_builtin = @IsBuiltin,
                country = @Country, package_id = @PackageId, package_version = @PackageVersion,
                package_hash = @PackageHash
            WHERE id = @Id;
            """,
            new
            {
                template.Name,
                template.Kind,
                template.Format,
                template.WidthMm,
                template.HeightMm,
                Orientation = (int)template.Orientation,
                template.FilePath,
                template.IsBuiltin,
                template.Country,
                template.PackageId,
                template.PackageVersion,
                template.PackageHash,
                template.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM print_templates WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
