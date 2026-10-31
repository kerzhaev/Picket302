using Picket302.Domain.Entities;

namespace Picket302.Printing.Engine;

/// <summary>
/// Автоподбор кегля текста в поле бланка (ТЗ §8.1, функция 34; §8.6, функция 91:
/// <c>Printing.Engine.TextFitter.FitField(BlankField, string)</c>).
/// </summary>
/// <remarks>
/// Кегль уменьшается от максимального к минимальному шагом 0,5 пункта; текст
/// переносится по словам. Если текст не помещается в границы поля даже при
/// минимальном кегле, печать не блокируется: выставляется маркер переполнения
/// для предпросмотра и предупреждение в журнал сессии.
/// </remarks>
public sealed class TextFitter
{
    /// <summary>Шаг уменьшения кегля при автоподборе, пунктов.</summary>
    public const decimal StepPt = 0.5m;

    private readonly ITextMeasurer _measurer;

    /// <summary>
    /// Создаёт автоподбор кегля.
    /// </summary>
    /// <param name="measurer">Измеритель текста.</param>
    public TextFitter(ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);

        _measurer = measurer;
    }

    /// <summary>
    /// Подбирает кегль и перенос текста для поля бланка.
    /// </summary>
    /// <param name="field">Поле бланка.</param>
    /// <param name="text">Текст поля.</param>
    /// <returns>Результат подбора.</returns>
    public FittingResult FitField(BlankField field, string text)
    {
        ArgumentNullException.ThrowIfNull(field);

        var исходныйТекст = text ?? string.Empty;
        var усечён = false;

        if (field.MaxChars > 0 && исходныйТекст.Length > field.MaxChars)
        {
            исходныйТекст = исходныйТекст[..field.MaxChars];
            усечён = true;
        }

        исходныйТекст = исходныйТекст.Trim();

        if (исходныйТекст.Length == 0)
        {
            return new FittingResult
            {
                FontSizePt = field.FontHeightMaxPt,
                Lines = Array.Empty<string>(),
                Truncated = усечён,
                Warning = усечён ? ПредупреждениеОбУсечении(field) : null,
            };
        }

        foreach (var кегль in Кегли(field.FontHeightMaxPt, field.FontHeightMinPt))
        {
            var строки = РазбитьПоСловам(исходныйТекст, field, кегль);

            if (Влезает(строки, field, кегль))
            {
                return new FittingResult
                {
                    FontSizePt = кегль,
                    Lines = строки,
                    Truncated = усечён,
                    Warning = усечён ? ПредупреждениеОбУсечении(field) : null,
                };
            }
        }

        var строкиМинимум = РазбитьПоСловам(исходныйТекст, field, field.FontHeightMinPt);

        return new FittingResult
        {
            FontSizePt = field.FontHeightMinPt,
            Lines = строкиМинимум,
            Overflow = true,
            Truncated = усечён,
            Warning = string.Join(
                " ",
                $"Поле «{field.Name}» переполнено: текст не помещается в границы поля даже при " +
                $"минимальном кегле {field.FontHeightMinPt:0.##}pt.",
                усечён ? ПредупреждениеОбУсечении(field) : string.Empty).Trim(),
        };
    }

    /// <summary>
    /// Возвращает последовательность кеглей от максимального к минимальному
    /// шагом 0,5 пункта.
    /// </summary>
    /// <param name="maxPt">Максимальный кегль, пунктов.</param>
    /// <param name="minPt">Минимальный кегль, пунктов.</param>
    /// <returns>Последовательность кеглей.</returns>
    public static IEnumerable<decimal> Кегли(decimal maxPt, decimal minPt)
    {
        if (maxPt <= minPt)
        {
            yield return maxPt;
            yield break;
        }

        var шагов = (int)Math.Floor((maxPt - minPt) / StepPt);

        for (var шаг = 0; шаг <= шагов; шаг++)
        {
            yield return maxPt - (шаг * StepPt);
        }
    }

    private bool Влезает(IReadOnlyList<string> строки, BlankField field, decimal кегль)
    {
        var высотаСтроки = _measurer.MeasureLineHeight(field.FontName, кегль);

        if (строки.Count * высотаСтроки > (double)field.HeightMm + 0.01)
        {
            return false;
        }

        foreach (var строка in строки)
        {
            if (_measurer.MeasureWidth(строка, field.FontName, кегль) > (double)field.LengthMm + 0.01)
            {
                return false;
            }
        }

        return true;
    }

    private List<string> РазбитьПоСловам(string text, BlankField field, decimal кегль)
    {
        var строки = new List<string>();
        var текущая = string.Empty;

        foreach (var слово in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var кандидат = текущая.Length == 0 ? слово : $"{текущая} {слово}";

            if (_measurer.MeasureWidth(кандидат, field.FontName, кегль) <= (double)field.LengthMm + 0.01)
            {
                текущая = кандидат;
                continue;
            }

            if (текущая.Length > 0)
            {
                строки.Add(текущая);
            }

            текущая = слово;
        }

        if (текущая.Length > 0)
        {
            строки.Add(текущая);
        }

        return строки;
    }

    private static string ПредупреждениеОбУсечении(BlankField field) =>
        $"Текст поля «{field.Name}» усечён по пределу {field.MaxChars} символов.";
}
