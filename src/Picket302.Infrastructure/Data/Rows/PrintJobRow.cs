using Picket302.Domain.Entities;

namespace Picket302.Infrastructure.Data.Rows;

/// <summary>
/// Плоская строка таблицы <c>print_jobs</c> (ТЗ §3.1.6).
/// </summary>
/// <remarks>
/// Запись реестра неизменяема, поэтому доменная сущность собирается из строки
/// только при чтении: изменяемых полей у сущности нет.
/// </remarks>
internal sealed class PrintJobRow
{
    public long Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? TemplateId { get; set; }

    public string PrinterName { get; set; } = string.Empty;

    public string PaperSizeName { get; set; } = string.Empty;

    public decimal PaperWidthMm { get; set; }

    public decimal PaperHeightMm { get; set; }

    public string? FontInfo { get; set; }

    public int Copies { get; set; }

    public string PrintMode { get; set; } = PrintModes.Selected;

    public string Status { get; set; } = PrintJobStatus.Success;

    public string? ErrorText { get; set; }

    public byte[]? DevmodeSnapshot { get; set; }

    /// <summary>Собирает неизменяемую доменную сущность из строки таблицы.</summary>
    /// <returns>Запись реестра сессий печати.</returns>
    public PrintJob ToEntity() => new(
        Id,
        CreatedAt,
        TemplateId,
        PrinterName,
        PaperSizeName,
        PaperWidthMm,
        PaperHeightMm,
        FontInfo,
        Copies,
        PrintMode,
        Status,
        ErrorText,
        DevmodeSnapshot);
}
