namespace Picket302.Printing.Engine;

/// <summary>
/// Результат автоподбора кегля (ТЗ §8.6, функция 91: <c>FittingResult</c>).
/// </summary>
public sealed record FittingResult
{
    /// <summary>Подобранный кегль, пунктов.</summary>
    public decimal FontSizePt { get; init; }

    /// <summary>
    /// Признак переполнения поля: текст не помещается в границы даже при
    /// минимальном кегле. В предпросмотре выводится маркер переполнения.
    /// </summary>
    public bool Overflow { get; init; }

    /// <summary>Признак усечения текста по пределу числа символов поля.</summary>
    public bool Truncated { get; init; }

    /// <summary>Строки текста после переноса по словам.</summary>
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    /// <summary>Предупреждение сессии печати, если текст усечён или переполняет поле.</summary>
    public string? Warning { get; init; }
}
