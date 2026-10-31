namespace Picket302.Printing.Elements;

/// <summary>
/// Нормативные параметры машиночитаемого индекса (ТЗ §6.3; §8.6, функция 90).
/// </summary>
/// <remarks>
/// Значения по умолчанию соответствуют ГОСТ Р 51506-99: ширина знака 3,7 мм
/// (±0,1 мм), высота знака 5,5 мм (±0,1 мм), толщина линии элементов цифр
/// 0,45 мм в диапазоне 0,20–0,50 мм, шаг между знаками 5,4 мм, шесть знаков.
/// </remarks>
public sealed record PostIndexSpec
{
    /// <summary>Нормативная ширина знака, мм.</summary>
    public const decimal DefaultDigitWidthMm = 3.7m;

    /// <summary>Нормативная высота знака, мм.</summary>
    public const decimal DefaultDigitHeightMm = 5.5m;

    /// <summary>Нормативная толщина линии элементов цифр, мм.</summary>
    public const decimal DefaultLineWidthMm = 0.45m;

    /// <summary>Нормативный шаг между знаками, мм.</summary>
    public const decimal DefaultAdvanceMm = 5.4m;

    /// <summary>Число знаков внутреннего (шестизначного) штампа.</summary>
    public const int DefaultDigitCount = 6;

    /// <summary>Минимальная допустимая толщина линии, мм.</summary>
    public const decimal MinLineWidthMm = 0.20m;

    /// <summary>Максимальная допустимая толщина линии, мм.</summary>
    public const decimal MaxLineWidthMm = 0.50m;

    /// <summary>Допуск по ширине знака, мм.</summary>
    public const decimal DigitWidthToleranceMm = 0.1m;

    /// <summary>Допуск по высоте знака, мм.</summary>
    public const decimal DigitHeightToleranceMm = 0.1m;

    /// <summary>Наименование шрифта кодового штампа.</summary>
    public const string DefaultFontName = "ZIPcode";

    /// <summary>Ширина знака, мм.</summary>
    public decimal DigitWidthMm { get; init; } = DefaultDigitWidthMm;

    /// <summary>Высота знака, мм.</summary>
    public decimal DigitHeightMm { get; init; } = DefaultDigitHeightMm;

    /// <summary>Толщина линии элементов цифр, мм.</summary>
    public decimal LineWidthMm { get; init; } = DefaultLineWidthMm;

    /// <summary>Шаг между знаками, мм.</summary>
    public decimal AdvanceMm { get; init; } = DefaultAdvanceMm;

    /// <summary>Число знаков кодового штампа.</summary>
    public int DigitCount { get; init; } = DefaultDigitCount;

    /// <summary>Гарнитура шрифта кодового штампа.</summary>
    public string FontName { get; init; } = DefaultFontName;

    /// <summary>
    /// Фиксированный индекс трёхзначного штампа международного отправления
    /// («Мн»); при заданном значении рисуется именно он.
    /// </summary>
    public string? InternationalIndex { get; init; }

    /// <summary>
    /// Ширина поля индекса, мм. Задаётся для проверки возможности
    /// пропорционального уменьшения штампа при нехватке места.
    /// </summary>
    public decimal? AvailableWidthMm { get; init; }
}
