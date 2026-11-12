using Picket302.Domain.Entities;
using Picket302.Printing.Devices;
using Picket302.Printing.Metrics;

namespace Picket302.Printing.Engine;

/// <summary>
/// Режим «Зафиксировать размер A4» (ТЗ §6.2; §8.6, функция 84:
/// <c>Printing.Engine.FixA4Mode.ApplyFixA4</c>).
/// </summary>
/// <remarks>
/// <para>Принудительно устанавливается DEVMODE.dmPaperSize = DMPAPER_A4;
/// координаты элементов масштабируются с привязкой к верхнему левому углу
/// листа A4 без изменения взаимного расположения.</para>
/// <para>Если бланк больше A4, выдаётся честное предупреждение «всё равно не
/// поместится» с указанием превышения в миллиметрах: печать остаётся
/// возможной, но обрезаемая зона подсвечивается в предпросмотре.</para>
/// <para>Кроме того, в соответствии с ТЗ §6.2 пользователю всегда выводится
/// предупреждение о том, что включена принудительная печать конверта в
/// габаритах листа A4, с напоминанием проверить физическую ориентацию
/// конверта в лотке.</para>
/// </remarks>
public sealed class FixA4Mode
{
    /// <summary>Ширина листа A4 в книжной ориентации, мм.</summary>
    public const decimal A4ShortSideMm = 210m;

    /// <summary>Высота листа A4 в книжной ориентации, мм.</summary>
    public const decimal A4LongSideMm = 297m;

    /// <summary>
    /// Предупреждение ТЗ §6.2 о принудительной печати конверта в габаритах
    /// листа A4: выводится пользователю при активации режима «Зафиксировать
    /// размер A4».
    /// </summary>
    public const string FixA4Warning =
        "Внимание: включена принудительная печать конверта в габаритах листа А4. "
        + "Убедитесь в правильности физической ориентации конверта в лотке";

    /// <summary>Шаблон честного предупреждения о непомещении бланка на лист A4.</summary>
    public const string OversizeWarningTemplate =
        "Бланк {0:0.##}×{1:0.##} мм всё равно не поместится на лист A4 ({2:0.##}×{3:0.##} мм): " +
        "превышение по ширине {4:0.##} мм, по высоте {5:0.##} мм. Печать возможна, но обрезаемая зона " +
        "подсвечивается в предпросмотре.";

    /// <summary>
    /// Переводит задание в режим «Зафиксировать размер A4».
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <returns>Контекст с форматом A4 и пересчитанными координатами полей.</returns>
    public PrintJobContext ApplyFixA4(PrintJobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (ширинаA4, высотаA4) = РазмерыA4(context.Orientation);

        var превышениеX = context.Blank.WidthMm - ширинаA4;
        var превышениеY = context.Blank.HeightMm - высотаA4;

        var помещается = превышениеX <= 0 && превышениеY <= 0;

        var масштаб = помещается
            ? Масштаб(context.Blank.WidthMm, context.Blank.HeightMm, ширинаA4, высотаA4)
            : 1m;

        var поля = context.Fields
            .Select(поле => МасштабироватьПоле(поле, масштаб))
            .ToList();

        var предупреждения = new List<string>(context.Warnings);
        var обрезанныеПоля = new List<string>(context.CroppedFieldNames);

        // ТЗ §6.2: при активации режима пользователь предупреждается о
        // принудительной печати конверта в габаритах листа A4.
        предупреждения.Add(FixA4Warning);

        if (!помещается)
        {
            предупреждения.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                OversizeWarningTemplate,
                context.Blank.WidthMm,
                context.Blank.HeightMm,
                ширинаA4,
                высотаA4,
                Math.Max(превышениеX, 0m),
                Math.Max(превышениеY, 0m)));

            foreach (var поле in поля)
            {
                if (поле.XMm + поле.LengthMm > ширинаA4 || поле.YMm + поле.HeightMm > высотаA4)
                {
                    обрезанныеПоля.Add(поле.Name);
                }
            }
        }

        return context with
        {
            Blank = context.Blank with
            {
                WidthMm = ширинаA4,
                HeightMm = высотаA4,
                PaperSizeName = "A4",
            },
            Fields = поля,
            PaperSizeName = "A4",
            PaperWidthMm = ширинаA4,
            PaperHeightMm = высотаA4,
            DmPaperSize = DevModeConstants.DMPAPER_A4,
            FixA4 = true,
            Warnings = предупреждения,
            CroppedFieldNames = обрезанныеПоля,
        };
    }

    /// <summary>
    /// Переводит сведения о принтере под лист A4 (D-S17-01): сведения
    /// запрашиваются у драйвера под код формата бланка (например, конверт
    /// C6); на драйверах без конвертных форматов печатная область
    /// приходит меньше бланка, и повторная валидация после
    /// «Зафиксировать A4» зацикливалась на блокировке. После перевода
    /// задания в A4 сведения принтера обязаны соответствовать A4.
    /// </summary>
    /// <param name="printer">Сведения принтера, полученные для формата бланка.</param>
    /// <param name="orientation">Ориентация листа A4.</param>
    /// <returns>Сведения принтера с габаритами и печатной областью A4.</returns>
    public static PrinterDeviceInfo ОбновитьПринтерПодФорматA4(
        PrinterDeviceInfo printer,
        TemplateOrientation orientation)
    {
        ArgumentNullException.ThrowIfNull(printer);

        var (ширина, высота) = РазмерыA4(orientation);

        return printer with
        {
            PaperSizeName = "A4",
            PaperWidthMm = ширина,
            PaperHeightMm = высота,
            PrintableWidthMm = ширина,
            PrintableHeightMm = высота,
            DmPaperSize = DevModeConstants.DMPAPER_A4,
        };
    }

    /// <summary>
    /// Возвращает габариты листа A4 для указанной ориентации.
    /// </summary>
    /// <param name="orientation">Ориентация листа.</param>
    /// <returns>Ширина и высота листа, мм.</returns>
    public static (decimal Width, decimal Height) РазмерыA4(TemplateOrientation orientation) =>
        orientation == TemplateOrientation.Landscape
            ? (A4LongSideMm, A4ShortSideMm)
            : (A4ShortSideMm, A4LongSideMm);

    private static decimal Масштаб(decimal ширинаБланка, decimal высотаБланка, decimal ширинаA4, decimal высотаA4)
    {
        if (ширинаБланка <= 0 || высотаБланка <= 0)
        {
            return 1m;
        }

        return Math.Min(ширинаA4 / ширинаБланка, высотаA4 / высотаБланка);
    }

    private static BlankField МасштабироватьПоле(BlankField поле, decimal масштаб)
    {
        if (масштаб == 1m)
        {
            return поле;
        }

        return new BlankField
        {
            Id = поле.Id,
            TemplateId = поле.TemplateId,
            Name = поле.Name,
            Kind = поле.Kind,
            Page = поле.Page,
            XMm = поле.XMm * масштаб,
            YMm = поле.YMm * масштаб,
            LengthMm = поле.LengthMm * масштаб,
            HeightMm = поле.HeightMm * масштаб,
            FontName = поле.FontName,
            FontHeightPt = поле.FontHeightPt,
            FontHeightMinPt = поле.FontHeightMinPt,
            FontHeightMaxPt = поле.FontHeightMaxPt,
            IsBold = поле.IsBold,
            IsItalic = поле.IsItalic,
            ColorRgb = поле.ColorRgb,
            Align = поле.Align,
            MaxChars = поле.MaxChars,
            IsVisible = поле.IsVisible,
            IsPrintable = поле.IsPrintable,
            ZoneRole = поле.ZoneRole,
        };
    }
}
