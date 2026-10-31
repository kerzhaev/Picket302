using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Data.Rows;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий неизменяемого реестра сессий печати на SQLite (ТЗ §3.1.6).
/// </summary>
/// <remarks>
/// Записи реестра только добавляются и читаются: ни изменения, ни удаления не
/// предусмотрено ни интерфейсом, ни триггерами схемы.
/// </remarks>
public sealed class PrintJobRepository : IPrintJobRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий реестра сессий печати.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public PrintJobRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(PrintJob printJob, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printJob);

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO print_jobs (created_at, template_id, printer_name, paper_size_name,
                paper_width_mm, paper_height_mm, font_info, copies, print_mode, status, error_text, devmode_snapshot)
            VALUES (@CreatedAt, @TemplateId, @PrinterName, @PaperSizeName, @PaperWidthMm, @PaperHeightMm,
                @FontInfo, @Copies, @PrintMode, @Status, @ErrorText, @DevmodeSnapshot);
            SELECT last_insert_rowid();
            """,
            new
            {
                printJob.CreatedAt,
                printJob.TemplateId,
                printJob.PrinterName,
                printJob.PaperSizeName,
                printJob.PaperWidthMm,
                printJob.PaperHeightMm,
                printJob.FontInfo,
                printJob.Copies,
                printJob.PrintMode,
                printJob.Status,
                printJob.ErrorText,
                printJob.DevmodeSnapshot,
            },
            cancellationToken).ConfigureAwait(false);

        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<PrintJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var строка = await _сессия.QueryFirstOrDefaultAsync<PrintJobRow>(
            "SELECT * FROM print_jobs WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return строка?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<Page<PrintJob>> GetPagedAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var нормализованный = request.Normalized();

        var всего = await _сессия.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM print_jobs;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (всего == 0)
        {
            return Page<PrintJob>.Empty(нормализованный);
        }

        var строки = await _сессия.QueryAsync<PrintJobRow>(
            "SELECT * FROM print_jobs ORDER BY id LIMIT @Limit OFFSET @Offset;",
            new { Limit = нормализованный.Limit, Offset = нормализованный.Offset },
            cancellationToken).ConfigureAwait(false);

        var записи = строки.Select(строка => строка.ToEntity()).ToList();

        return new Page<PrintJob>(записи, нормализованный.Offset, нормализованный.Limit, всего);
    }
}
