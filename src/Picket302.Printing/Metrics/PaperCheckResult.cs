using Picket302.Domain.Entities;

namespace Picket302.Printing.Metrics;

/// <summary>
/// Результат двусторонней сверки метрик бланка и возможностей принтера
/// (ТЗ §6.2; §8.6, функция 83).
/// </summary>
public sealed record PaperCheckResult
{
    /// <summary>Признак блокирующего несоответствия размеров.</summary>
    public bool IsBlocked { get; init; }

    /// <summary>Итог сессии печати: <c>SUCCESS</c> либо <c>BLOCKED_SIZE</c>.</summary>
    public string Status { get; init; } = PrintJobStatus.Success;

    /// <summary>Текст блокирующей ошибки.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Превышение по ширине: <c>ΔW = W_blank − W_printable</c>, мм.</summary>
    public decimal DeltaWidthMm { get; init; }

    /// <summary>Превышение по высоте: <c>ΔH = H_blank − H_printable</c>, мм.</summary>
    public decimal DeltaHeightMm { get; init; }

    /// <summary>Признак предложения режима «Зафиксировать A4».</summary>
    public bool SuggestsFixA4 { get; init; }

    /// <summary>Предупреждения сессии печати.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Форматы носителя, доступные на выбранном принтере.</summary>
    public IReadOnlyList<string> AvailablePaperSizes { get; init; } = Array.Empty<string>();
}
