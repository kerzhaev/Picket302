namespace Picket302.Printing.Editor;

/// <summary>
/// Единицы измерения представления редактора бланков (ТЗ §5.2: «UI-панель
/// позволяет налету переключать представление между миллиметрами, пунктами (pt)
/// и пикселями экрана (96/120 DPI)»).
/// </summary>
public enum EditorUnit
{
    /// <summary>Миллиметры (внутренняя единица движка — 0,01 мм).</summary>
    Millimeters = 0,

    /// <summary>Пункты: 1 pt = 25,4/72 мм.</summary>
    Points = 1,

    /// <summary>Пиксели экрана.</summary>
    Pixels = 2,
}

/// <summary>
/// Пересчёт координат редактора бланков (ТЗ §5.2: движок считает позиции
/// в сотых долях миллиметра; UI переключает мм / pt / px).
/// </summary>
public static class EditorUnits
{
    /// <summary>Миллиметров в дюйме (точная константа).</summary>
    public const decimal MmPerInch = 25.4m;

    /// <summary>Пунктов в дюйме.</summary>
    public const decimal PointsPerInch = 72m;

    /// <summary>Плотность экрана по умолчанию, dpi (ТЗ §7.5: 96/120 DPI).</summary>
    public const int DefaultScreenDpi = 96;

    /// <summary>Увеличенная плотность экрана, dpi.</summary>
    public const int LargeScreenDpi = 120;

    /// <summary>Шаг сетки привязки, мм.</summary>
    public const decimal SnapStepMm = 0.1m;

    /// <summary>
    /// Переводит миллиметры в пункты.
    /// </summary>
    /// <param name="mm">Значение в миллиметрах.</param>
    /// <returns>Значение в пунктах.</returns>
    public static decimal MillimetersToPoints(decimal mm) => mm * PointsPerInch / MmPerInch;

    /// <summary>
    /// Переводит пункты в миллиметры.
    /// </summary>
    /// <param name="pt">Значение в пунктах.</param>
    /// <returns>Значение в миллиметрах.</returns>
    public static decimal PointsToMillimeters(decimal pt) => pt * MmPerInch / PointsPerInch;

    /// <summary>
    /// Переводит миллиметры в пиксели экрана.
    /// </summary>
    /// <param name="mm">Значение в миллиметрах.</param>
    /// <param name="dpi">Плотность экрана, dpi (96 или 120).</param>
    /// <returns>Значение в пикселях.</returns>
    public static decimal MillimetersToPixels(decimal mm, int dpi = DefaultScreenDpi) =>
        mm * dpi / MmPerInch;

    /// <summary>
    /// Переводит пиксели экрана в миллиметры.
    /// </summary>
    /// <param name="pixels">Значение в пикселях.</param>
    /// <param name="dpi">Плотность экрана, dpi (96 или 120).</param>
    /// <returns>Значение в миллиметрах.</returns>
    public static decimal PixelsToMillimeters(decimal pixels, int dpi = DefaultScreenDpi) =>
        pixels * MmPerInch / dpi;

    /// <summary>
    /// Форматирует значение координаты в выбранной единице представления.
    /// </summary>
    /// <param name="mm">Внутреннее значение, мм.</param>
    /// <param name="unit">Единица представления.</param>
    /// <param name="dpi">Плотность экрана для пикселей.</param>
    /// <returns>Отформатированная строка с суффиксом единицы.</returns>
    public static string Format(decimal mm, EditorUnit unit, int dpi = DefaultScreenDpi)
    {
        var культура = System.Globalization.CultureInfo.InvariantCulture;

        return unit switch
        {
            EditorUnit.Points => MillimetersToPoints(mm).ToString("0.##", культура) + " pt",
            EditorUnit.Pixels => MillimetersToPixels(mm, dpi).ToString("0.#", культура) + " px",
            _ => mm.ToString("0.##", культура) + " мм",
        };
    }

    /// <summary>
    /// Привязывает значение к шагу сетки (Snap, ТЗ §7.5).
    /// </summary>
    /// <param name="mm">Значение, мм.</param>
    /// <param name="stepMm">Шаг сетки, мм.</param>
    /// <returns>Значение, привязанное к шагу.</returns>
    public static decimal Snap(decimal mm, decimal stepMm = SnapStepMm) =>
        Math.Round(mm / stepMm, MidpointRounding.ToEven) * stepMm;
}
