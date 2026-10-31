using Picket302.Domain.Entities;

namespace Picket302.Printing.Metrics;

/// <summary>
/// Метрики бланка: размер носителя и ориентация макета (ТЗ §6.1, §8.6, функция 83).
/// </summary>
/// <remarks>
/// Метрики бланка читаются из <c>blank.xml</c> и сравниваются с физическими
/// границами носителя, сообщаемыми драйвером принтера.
/// </remarks>
public sealed record BlankMetrics
{
    /// <summary>Имя файла бланка.</summary>
    public required string TemplateName { get; init; }

    /// <summary>Ширина бланка, мм.</summary>
    public required decimal WidthMm { get; init; }

    /// <summary>Высота бланка, мм.</summary>
    public required decimal HeightMm { get; init; }

    /// <summary>Ориентация макета бланка.</summary>
    public TemplateOrientation Orientation { get; init; } = TemplateOrientation.Landscape;

    /// <summary>
    /// Имя формата носителя, запрошенного макетом (метрика <c>PaperSize</c>);
    /// пустое значение — формат не задан явно.
    /// </summary>
    public string PaperSizeName { get; init; } = string.Empty;

    /// <summary>Число страниц бланка.</summary>
    public int PageCount { get; init; } = 1;
}
