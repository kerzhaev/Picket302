namespace Picket302.Application.Formatting;

/// <summary>
/// Вывод номеров связанных документов через запятую (ТЗ §8.4, функция 60:
/// <c>Application.Formatting.DocumentNumbersAggregator.AggregateDocNumbers</c>).
/// </summary>
public sealed class DocumentNumbersAggregator
{
    /// <summary>Разделитель перечня номеров (ТЗ §8.4, ф.60: «разделителем „, “»).</summary>
    public const string Separator = ", ";

    /// <summary>Признак усечения перечня из-за ограничения длины ячейки.</summary>
    public bool Truncated { get; private set; }

    /// <summary>
    /// Склеивает множество номеров первичных документов в единую строку
    /// с разделителем «, » и контролем длины ячейки (ТЗ §8.4, функция 60).
    /// </summary>
    /// <param name="numbers">Номера документов.</param>
    /// <param name="maxCellLength">Предельная длина ячейки; по умолчанию 255 знаков.</param>
    /// <returns>
    /// Перечень номеров («№12, №15, №18»); при превышении предела перечень
    /// усекается с многоточием и выставляется признак <see cref="Truncated"/>.
    /// </returns>
    public string AggregateDocNumbers(IEnumerable<string> numbers, int maxCellLength = 255)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        Truncated = false;

        var номера = numbers
            .Where(номер => !string.IsNullOrWhiteSpace(номер))
            .Select(номер => номер.Trim());

        var перечень = string.Join(Separator, номера);

        if (перечень.Length <= maxCellLength)
        {
            return перечень;
        }

        // Контроль длины ячейки: усечение с многоточием.
        Truncated = true;

        return перечень[..Math.Max(0, maxCellLength - 1)].TrimEnd() + "…";
    }
}
