namespace Picket302.Printing.Metrics;

/// <summary>
/// Перевод размеров носителя между единицами Windows API (ТЗ §6.1, §6.2).
/// </summary>
/// <remarks>
/// <para>В коде применяются три разные единицы, которые нельзя смешивать:</para>
/// <list type="bullet">
/// <item><description>
/// <c>System.Drawing.Printing.PaperSize</c> — размеры в <b>сотых долях дюйма</b>;
/// </description></item>
/// <item><description>
/// <c>DEVMODE.dmPaperWidth</c>/<c>dmPaperLength</c> и
/// <c>DeviceCapabilities(DC_PAPERWIDTH|DC_PAPERHEIGHT)</c> — в
/// <b>десятых долях миллиметра</b>;
/// </description></item>
/// <item><description>
/// <c>DEVMODE.dmPaperSize</c> — код формата <c>DMPAPER_*</c>.
/// </description></item>
/// </list>
/// <para>
/// Класс сводит перевод к одному месту, чтобы размер бланка не подменялся
/// на порядок величины.
/// </para>
/// </remarks>
public static class PrintUnits
{
    /// <summary>Миллиметров в дюйме (точная константа).</summary>
    public const decimal MmPerInch = 25.4m;

    /// <summary>Десятых долей миллиметра в одном миллиметре.</summary>
    public const decimal TenthsOfMmPerMm = 10m;

    /// <summary>Сотых долей дюйма в одном дюйме.</summary>
    public const decimal HundredthsOfInchPerInch = 100m;

    /// <summary>
    /// Переводит миллиметры в сотые доли дюйма — единицу измерения
    /// <c>System.Drawing.Printing.PaperSize</c>.
    /// </summary>
    /// <param name="мм">Размер, мм.</param>
    /// <returns>Размер в сотых долях дюйма, округлённый до целого.</returns>
    public static int MmToHundredthsOfInch(decimal мм) =>
        (int)Math.Round(мм / MmPerInch * HundredthsOfInchPerInch, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Переводит сотые доли дюйма в миллиметры.
    /// </summary>
    /// <param name="сотыеДюйма">Размер в сотых долях дюйма.</param>
    /// <returns>Размер, мм.</returns>
    public static decimal HundredthsOfInchToMm(int сотыеДюйма) =>
        сотыеДюйма / HundredthsOfInchPerInch * MmPerInch;

    /// <summary>
    /// Переводит десятые доли миллиметра — единицу DEVMODE и
    /// <c>DeviceCapabilities</c> — в миллиметры.
    /// </summary>
    /// <param name="десятыеМм">Размер в десятых долях миллиметра.</param>
    /// <returns>Размер, мм.</returns>
    public static decimal TenthsOfMmToMm(decimal десятыеМм) => десятыеМм / TenthsOfMmPerMm;
}
