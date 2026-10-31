using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий координатной сетки полей бланка на SQLite (ТЗ §3.1.5).
/// </summary>
public sealed class BlankFieldRepository : IBlankFieldRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий полей бланка.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public BlankFieldRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(BlankField field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO blank_fields (template_id, name, kind, page, x_mm, y_mm, length_mm, height_mm,
                font_name, font_height_pt, font_height_min_pt, font_height_max_pt, is_bold, is_italic,
                color_rgb, align, max_chars, is_visible, is_printable, zone_role)
            VALUES (@TemplateId, @Name, @Kind, @Page, @XMm, @YMm, @LengthMm, @HeightMm,
                @FontName, @FontHeightPt, @FontHeightMinPt, @FontHeightMaxPt, @IsBold, @IsItalic,
                @ColorRgb, @Align, @MaxChars, @IsVisible, @IsPrintable, @ZoneRole);
            SELECT last_insert_rowid();
            """,
            new
            {
                field.TemplateId,
                field.Name,
                field.Kind,
                field.Page,
                field.XMm,
                field.YMm,
                field.LengthMm,
                field.HeightMm,
                field.FontName,
                field.FontHeightPt,
                field.FontHeightMinPt,
                field.FontHeightMaxPt,
                field.IsBold,
                field.IsItalic,
                field.ColorRgb,
                Align = (int)field.Align,
                field.MaxChars,
                field.IsVisible,
                field.IsPrintable,
                field.ZoneRole,
            },
            cancellationToken).ConfigureAwait(false);

        field.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlankField>> GetByTemplateAsync(
        long templateId,
        CancellationToken cancellationToken = default)
    {
        var поля = await _сессия.QueryAsync<BlankField>(
            "SELECT * FROM blank_fields WHERE template_id = @TemplateId ORDER BY page, id;",
            new { TemplateId = templateId },
            cancellationToken).ConfigureAwait(false);

        return поля.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(BlankField field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);

        return await _сессия.ExecuteAsync(
            """
            UPDATE blank_fields SET name = @Name, kind = @Kind, page = @Page, x_mm = @XMm, y_mm = @YMm,
                length_mm = @LengthMm, height_mm = @HeightMm, font_name = @FontName,
                font_height_pt = @FontHeightPt, font_height_min_pt = @FontHeightMinPt,
                font_height_max_pt = @FontHeightMaxPt, is_bold = @IsBold, is_italic = @IsItalic,
                color_rgb = @ColorRgb, align = @Align, max_chars = @MaxChars, is_visible = @IsVisible,
                is_printable = @IsPrintable, zone_role = @ZoneRole
            WHERE id = @Id;
            """,
            new
            {
                field.Name,
                field.Kind,
                field.Page,
                field.XMm,
                field.YMm,
                field.LengthMm,
                field.HeightMm,
                field.FontName,
                field.FontHeightPt,
                field.FontHeightMinPt,
                field.FontHeightMaxPt,
                field.IsBold,
                field.IsItalic,
                field.ColorRgb,
                Align = (int)field.Align,
                field.MaxChars,
                field.IsVisible,
                field.IsPrintable,
                field.ZoneRole,
                field.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM blank_fields WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
