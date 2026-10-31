namespace Picket302.Printing.Engine;

/// <summary>
/// Измеритель текста для автоподбора кегля (ТЗ §8.6, функция 91).
/// </summary>
/// <remarks>
/// Абстракция вынесена из автоподбора, чтобы правило переноса и уменьшения кегля
/// проверялось юнит-тестом без графического контекста (ТЗ §11.1).
/// </remarks>
public interface ITextMeasurer
{
    /// <summary>
    /// Измеряет ширину строки текста.
    /// </summary>
    /// <param name="text">Строка текста.</param>
    /// <param name="fontName">Гарнитура шрифта.</param>
    /// <param name="fontSizePt">Кегль, пунктов.</param>
    /// <returns>Ширина строки, мм.</returns>
    double MeasureWidth(string text, string fontName, decimal fontSizePt);

    /// <summary>
    /// Измеряет высоту одной строки текста.
    /// </summary>
    /// <param name="fontName">Гарнитура шрифта.</param>
    /// <param name="fontSizePt">Кегль, пунктов.</param>
    /// <returns>Высота строки, мм.</returns>
    double MeasureLineHeight(string fontName, decimal fontSizePt);
}
