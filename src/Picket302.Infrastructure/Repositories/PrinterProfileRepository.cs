using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий профилей принтеров на SQLite (ТЗ §3.1.16).
/// </summary>
/// <remarks>
/// Калибровка хранится отдельно для каждой пары «принтер + формат»:
/// ограничение уникальности задано в схеме БД.
/// </remarks>
public sealed class PrinterProfileRepository : IPrinterProfileRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий профилей принтеров.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public PrinterProfileRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(PrinterProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO printer_profiles (printer_name, envelope_format, offset_x_mm, offset_y_mm,
                feed_alignment, fix_a4, duplex_capable, is_default, devmode_template)
            VALUES (@PrinterName, @EnvelopeFormat, @OffsetXMm, @OffsetYMm, @FeedAlignment, @FixA4,
                @DuplexCapable, @IsDefault, @DevmodeTemplate);
            SELECT last_insert_rowid();
            """,
            new
            {
                profile.PrinterName,
                profile.EnvelopeFormat,
                profile.OffsetXMm,
                profile.OffsetYMm,
                FeedAlignment = (int)profile.FeedAlignment,
                profile.FixA4,
                profile.DuplexCapable,
                profile.IsDefault,
                profile.DevmodeTemplate,
            },
            cancellationToken).ConfigureAwait(false);

        profile.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<PrinterProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<PrinterProfile>(
            "SELECT * FROM printer_profiles WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PrinterProfile?> GetByPrinterAndFormatAsync(
        string printerName,
        string envelopeFormat,
        CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<PrinterProfile>(
            "SELECT * FROM printer_profiles WHERE printer_name = @PrinterName AND envelope_format = @EnvelopeFormat;",
            new { PrinterName = printerName, EnvelopeFormat = envelopeFormat },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrinterProfile>> GetByPrinterAsync(
        string printerName,
        CancellationToken cancellationToken = default)
    {
        var профили = await _сессия.QueryAsync<PrinterProfile>(
            "SELECT * FROM printer_profiles WHERE printer_name = @PrinterName ORDER BY envelope_format;",
            new { PrinterName = printerName },
            cancellationToken).ConfigureAwait(false);

        return профили.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(PrinterProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return await _сессия.ExecuteAsync(
            """
            UPDATE printer_profiles SET printer_name = @PrinterName, envelope_format = @EnvelopeFormat,
                offset_x_mm = @OffsetXMm, offset_y_mm = @OffsetYMm, feed_alignment = @FeedAlignment,
                fix_a4 = @FixA4, duplex_capable = @DuplexCapable, is_default = @IsDefault,
                devmode_template = @DevmodeTemplate
            WHERE id = @Id;
            """,
            new
            {
                profile.PrinterName,
                profile.EnvelopeFormat,
                profile.OffsetXMm,
                profile.OffsetYMm,
                FeedAlignment = (int)profile.FeedAlignment,
                profile.FixA4,
                profile.DuplexCapable,
                profile.IsDefault,
                profile.DevmodeTemplate,
                profile.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM printer_profiles WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
