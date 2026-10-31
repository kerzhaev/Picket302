namespace Picket302.Printing.Elements;

/// <summary>
/// Результат проверки параметров кодового штампа (ТЗ §6.3, §6.6).
/// </summary>
public sealed record PostIndexValidationResult
{
    /// <summary>Признак блокирующего нарушения нормативных параметров.</summary>
    public bool IsBlocked { get; init; }

    /// <summary>Предупреждения сессии печати.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Причина блокировки, если печать запрещена.</summary>
    public string? BlockReason { get; init; }
}

/// <summary>
/// Исключение блокировки печати кодового штампа (ТЗ §6.3: превышение толщины
/// линии сверх 0,5 мм либо отсутствие шрифта ZIPcode).
/// </summary>
public sealed class PostIndexBlockedException : InvalidOperationException
{
    /// <summary>
    /// Создаёт исключение блокировки.
    /// </summary>
    /// <param name="message">Причина блокировки.</param>
    public PostIndexBlockedException(string message)
        : base(message)
    {
    }
}
