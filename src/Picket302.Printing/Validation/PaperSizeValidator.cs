using Picket302.Domain.Entities;
using Picket302.Printing.Metrics;

namespace Picket302.Printing.Validation;

/// <summary>
/// Двусторонняя сверка метрик бланка и возможностей принтера (ТЗ §6.2;
/// §8.6, функция 83: <c>Printing.Validation.PaperSizeValidator</c>).
/// </summary>
/// <remarks>
/// Сверка выполняется по двум независимым источникам:
/// (1) размер бланка (<c>blank.xml</c>) против физических границ носителя,
/// сообщаемых драйвером через <c>DeviceCapabilitiesW(DC_PAPERS, DC_PAPERSIZE)</c>;
/// (2) метрики документа <c>PaperSize</c> против <c>DEVMODE.dmPaperSize</c>,
/// <c>dmPaperWidth</c>, <c>dmPaperLength</c>.
/// </remarks>
public sealed class PaperSizeValidator
{
    /// <summary>
    /// Допустимое превышение размера бланка над печатной областью, мм (ТЗ §6.2:
    /// блокировка при <c>Δ &gt; 1.0 мм</c>).
    /// </summary>
    public const decimal ToleranceMm = 1.0m;

    /// <summary>
    /// Текст блокирующей ошибки (ТЗ §8.6, функции 83 и 85).
    /// </summary>
    public const string BlockedSizeMessage = "Конверт слишком большой для выбранного принтера";

    /// <summary>
    /// Выполняет проверку соответствия размеров бланка носителю принтера.
    /// </summary>
    /// <param name="blank">Метрики бланка.</param>
    /// <param name="printer">Сведения о принтере.</param>
    /// <returns>Результат проверки.</returns>
    public PaperCheckResult Validate(BlankMetrics blank, PrinterDeviceInfo printer)
    {
        ArgumentNullException.ThrowIfNull(blank);
        ArgumentNullException.ThrowIfNull(printer);

        var дельтаШирины = blank.WidthMm - printer.PrintableWidthMm;
        var дельтаВысоты = blank.HeightMm - printer.PrintableHeightMm;

        if (дельтаШирины > ToleranceMm || дельтаВысоты > ToleranceMm)
        {
            return new PaperCheckResult
            {
                IsBlocked = true,
                Status = PrintJobStatus.BlockedSize,
                ErrorMessage = BlockedSizeMessage,
                DeltaWidthMm = дельтаШирины,
                DeltaHeightMm = дельтаВысоты,
                AvailablePaperSizes = printer.AvailablePaperSizes,
            };
        }

        var предупреждения = new List<string>();
        var предлагатьA4 = false;

        if (!string.IsNullOrWhiteSpace(blank.PaperSizeName)
            && !string.IsNullOrWhiteSpace(printer.PaperSizeName)
            && !string.Equals(
                blank.PaperSizeName.Trim(),
                printer.PaperSizeName.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            предлагатьA4 = true;
            предупреждения.Add(
                $"Формат носителя в бланке («{blank.PaperSizeName}») отличается от формата, " +
                $"сообщаемого драйвером принтера («{printer.PaperSizeName}»). " +
                "Продолжить печать можно в режиме «Зафиксировать A4».");
        }

        if (Math.Abs(blank.WidthMm - printer.PaperWidthMm) > ToleranceMm
            || Math.Abs(blank.HeightMm - printer.PaperHeightMm) > ToleranceMm)
        {
            предлагатьA4 = true;
            предупреждения.Add(
                $"Размер бланка ({blank.WidthMm:0.##}×{blank.HeightMm:0.##} мм) отличается " +
                $"от размера носителя, сообщаемого драйвером " +
                $"({printer.PaperWidthMm:0.##}×{printer.PaperHeightMm:0.##} мм). " +
                "Продолжить печать можно в режиме «Зафиксировать A4».");
        }

        return new PaperCheckResult
        {
            IsBlocked = false,
            Status = PrintJobStatus.Success,
            DeltaWidthMm = дельтаШирины,
            DeltaHeightMm = дельтаВысоты,
            SuggestsFixA4 = предлагатьA4,
            Warnings = предупреждения,
            AvailablePaperSizes = printer.AvailablePaperSizes,
        };
    }
}
