using Picket302.Printing.Devices;

namespace Picket302.Printing.Engine;

/// <summary>
/// Решение по режиму двусторонней печати (ТЗ §8.6, функция 86).
/// </summary>
/// <param name="ЭффективныйРежим">Применённый режим печати.</param>
/// <param name="DevModeDuplex">Значение DEVMODE.dmDuplex.</param>
/// <param name="ТребуетсяИнструкция">Признак необходимости инструкции оператору.</param>
/// <param name="Инструкция">Текст инструкции для оператора.</param>
public sealed record DuplexDecision(
    DuplexMode ЭффективныйРежим,
    short DevModeDuplex,
    bool ТребуетсяИнструкция,
    string? Инструкция)
{
    /// <summary>
    /// Чётность страниц первого прохода (ТЗ §8.6, функции 86 и 87): при
    /// двухпроходной печати в первом проходе выводятся нечётные (лицевые)
    /// страницы.
    /// </summary>
    public PageParity ЧётностьПервогоПрохода { get; init; } = PageParity.All;

    /// <summary>
    /// Чётность страниц второго прохода (ТЗ §8.6, функции 86 и 87): при
    /// двухпроходной печати во втором проходе выводятся чётные (оборотные)
    /// страницы.
    /// </summary>
    public PageParity ЧётностьВторогоПрохода { get; init; } = PageParity.All;
}

/// <summary>
/// Настройка двусторонней печати уведомлений (ТЗ §8.6, функция 86:
/// <c>Printing.Engine.DuplexController.ConfigureDuplex</c>).
/// </summary>
/// <remarks>
/// Дуплекс настраивается через DEVMODE.dmDuplex при аппаратной поддержке
/// (возможность DC_DUPLEX, отражённая в <c>PrinterDeviceInfo.SupportsDuplex</c>).
/// Если принтер не поддерживает двустороннюю печать, автоматически
/// применяется программная сепарация: печать всех лицевых сторон, пауза с
/// инструкцией перевернуть стопку, печать всех оборотных.
/// </remarks>
public sealed class DuplexController
{
    /// <summary>Текст инструкции при двухпроходной печати.</summary>
    public const string ManualInstruction =
        "Принтер не поддерживает двустороннюю печать. Будет выполнена двухпроходная печать: " +
        "сначала лицевые стороны, затем переверните стопку и нажмите «Продолжить» для печати оборотных.";

    /// <summary>
    /// Определяет, требуется ли двухпроходная печать с ручным переворотом
    /// стопки: дуплекс запрошен, но принтер его не поддерживает.
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <param name="mode">Запрошенный режим.</param>
    /// <returns><see langword="true"/>, если требуется ручная сепарация.</returns>
    public static bool ТребуетсяДвухпроходнаяПечать(PrintJobContext context, DuplexMode mode)
    {
        ArgumentNullException.ThrowIfNull(context);

        return mode != DuplexMode.Simplex && !context.Printer.SupportsDuplex;
    }

    /// <summary>
    /// Определяет режим двусторонней печати для контекста сессии.
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <param name="mode">Запрошенный режим.</param>
    /// <returns>Принятое решение.</returns>
    public DuplexDecision ConfigureDuplex(PrintJobContext context, DuplexMode mode)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (mode == DuplexMode.Simplex)
        {
            return new DuplexDecision(
                DuplexMode.Simplex,
                DevModeConstants.DMDUP_SIMPLEX,
                false,
                null)
            {
                ЧётностьПервогоПрохода = PageParity.All,
                ЧётностьВторогоПрохода = PageParity.All,
            };
        }

        // Дуплекс запрошен, но принтер его не поддерживает (DC_DUPLEX):
        // автоматический фолбэк на двухпроходную печать с инструкцией
        // перевернуть стопку (ТЗ §8.6, функция 86).
        if (mode == DuplexMode.ManualTwoPass || !context.Printer.SupportsDuplex)
        {
            return new DuplexDecision(
                DuplexMode.ManualTwoPass,
                DevModeConstants.DMDUP_SIMPLEX,
                true,
                ManualInstruction)
            {
                ЧётностьПервогоПрохода = PageParity.OddOnly,
                ЧётностьВторогоПрохода = PageParity.EvenOnly,
            };
        }

        return new DuplexDecision(
            DuplexMode.Duplex,
            DevModeConstants.DMDUP_VERTICAL,
            false,
            null)
        {
            ЧётностьПервогоПрохода = PageParity.All,
            ЧётностьВторогоПрохода = PageParity.All,
        };
    }
}
