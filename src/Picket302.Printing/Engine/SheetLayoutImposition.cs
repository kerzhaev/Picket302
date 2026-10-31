using System.Drawing;
using System.Globalization;
using Picket302.Domain.Entities;

namespace Picket302.Printing.Engine;

/// <summary>
/// Поля раскладки адресных этикеток на листе (ТЗ §8.4, функция 44).
/// </summary>
/// <param name="LeftMm">Поле слева, мм.</param>
/// <param name="TopMm">Поле сверху, мм.</param>
/// <param name="RightMm">Поле справа, мм.</param>
/// <param name="BottomMm">Поле снизу, мм.</param>
public sealed record Margins(decimal LeftMm, decimal TopMm, decimal RightMm, decimal BottomMm)
{
    /// <summary>Поля по умолчанию — 5 мм.</summary>
    public static Margins Default { get; } = new(5m, 5m, 5m, 5m);
}

/// <summary>
/// Формат листа для спуска адресных этикеток (ТЗ §8.4, функции 44 и 146).
/// </summary>
public enum SheetSize
{
    /// <summary>A5 — 148×210 мм.</summary>
    A5 = 0,

    /// <summary>A4 — 210×297 мм.</summary>
    A4 = 1,

    /// <summary>A3 — 297×420 мм.</summary>
    A3 = 2,
}

/// <summary>
/// Результат раскладки адресных этикеток на листе (ТЗ §8.4, функция 44).
/// </summary>
public sealed record SheetLayoutResult
{
    /// <summary>Число рядов сетки.</summary>
    public int Рядов { get; init; }

    /// <summary>Число столбцов сетки.</summary>
    public int Столбцов { get; init; }

    /// <summary>Число наклеек на листе.</summary>
    public int НаклеекНаЛисте { get; init; }

    /// <summary>Ширина наклейки, мм.</summary>
    public decimal ШиринаНаклейкиМм { get; init; }

    /// <summary>Высота наклейки, мм.</summary>
    public decimal ВысотаНаклейкиМм { get; init; }

    /// <summary>Вылет за печатное поле, мм.</summary>
    public decimal ВылетМм { get; init; }

    /// <summary>Позиции наклеек на листе, мм.</summary>
    public IReadOnlyList<RectangleF> Позиции { get; init; } = Array.Empty<RectangleF>();

    /// <summary>Метки обреза (crop marks), мм.</summary>
    public IReadOnlyList<RectangleF> МеткиОбреза { get; init; } = Array.Empty<RectangleF>();

    /// <summary>Текст ошибки разбивки, если раскладка невозможна.</summary>
    public string? Ошибка { get; init; }
}

/// <summary>
/// Электронный спуск полос: сетка адресных этикеток на листах формата
/// A4/A3/A5 (ТЗ §8.4, функция 44:
/// <c>Printing.Engine.SheetLayoutImposition.LayoutGridOnSheet</c>).
/// </summary>
public sealed class SheetLayoutImposition
{
    /// <summary>Вылет за печатное поле по умолчанию, мм.</summary>
    public const decimal DefaultBleedMm = 3m;

    /// <summary>Длина метки обреза, мм.</summary>
    public const decimal CropMarkLengthMm = 3m;

    /// <summary>Толщина метки обреза, мм.</summary>
    public const decimal CropMarkThicknessMm = 0.25m;

    /// <summary>
    /// Текст ошибки разбивки при превышении габаритов листа (ТЗ §8.6,
    /// функция 44: «Если размеры наклеек превышают габариты листа, выводится
    /// ошибка разбивки»).
    /// </summary>
    public const string SheetOverflowErrorTemplate =
        "Габарит наклейки ({0:0.##}×{1:0.##} мм) превышает габариты листа {2} ({3:0.##}×{4:0.##} мм): "
        + "разбивка невозможна. Наклейка не помещается и в ячейку сетки ({5:0.##}×{6:0.##} мм).";

    /// <summary>
    /// Текст ошибки разбивки, когда наклейка не помещается в ячейку сетки.
    /// </summary>
    public const string CellOverflowErrorTemplate =
        "Габарит наклейки ({0:0.##}×{1:0.##} мм) превышает ячейку сетки ({2:0.##}×{3:0.##} мм) "
        + "на листе {4} ({5:0.##}×{6:0.##} мм): разбивка невозможна. Габариты листа наклейка "
        + "не превышает, но правило ТЗ о размерах наклеек на листе не выполнено.";

    /// <summary>
    /// Формирует сетку наклеек на листе.
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <param name="rows">Число рядов.</param>
    /// <param name="cols">Число столбцов.</param>
    /// <param name="mmMargins">Поля листа.</param>
    /// <param name="sheetSize">Формат листа.</param>
    /// <returns>Результат раскладки.</returns>
    public SheetLayoutResult LayoutGridOnSheet(
        PrintJobContext context,
        int rows,
        int cols,
        Margins mmMargins,
        SheetSize sheetSize = SheetSize.A4)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mmMargins);

        var (ширинаЛиста, высотаЛиста) = РазмерыЛиста(sheetSize, context.Orientation);

        var ширинаНаклейки = context.LabelWidthMm > 0
            ? context.LabelWidthMm
            : context.Blank.WidthMm;
        var высотаНаклейки = context.LabelHeightMm > 0
            ? context.LabelHeightMm
            : context.Blank.HeightMm;

        var доступноX = ширинаЛиста - mmMargins.LeftMm - mmMargins.RightMm;
        var доступноY = высотаЛиста - mmMargins.TopMm - mmMargins.BottomMm;

        if (rows <= 0 || cols <= 0)
        {
            return new SheetLayoutResult
            {
                Рядов = Math.Max(rows, 0),
                Столбцов = Math.Max(cols, 0),
                Ошибка = "Число рядов и столбцов сетки должно быть больше нуля.",
            };
        }

        var шагX = доступноX / cols;
        var шагY = доступноY / rows;

        if (ширинаНаклейки > ширинаЛиста || высотаНаклейки > высотаЛиста)
        {
            return new SheetLayoutResult
            {
                Рядов = rows,
                Столбцов = cols,
                ШиринаНаклейкиМм = ширинаНаклейки,
                ВысотаНаклейкиМм = высотаНаклейки,
                Ошибка = string.Format(
                    CultureInfo.InvariantCulture,
                    SheetOverflowErrorTemplate,
                    ширинаНаклейки,
                    высотаНаклейки,
                    ИмяЛиста(sheetSize),
                    ширинаЛиста,
                    высотаЛиста,
                    шагX,
                    шагY),
            };
        }

        if (ширинаНаклейки > шагX || высотаНаклейки > шагY)
        {
            return new SheetLayoutResult
            {
                Рядов = rows,
                Столбцов = cols,
                ШиринаНаклейкиМм = ширинаНаклейки,
                ВысотаНаклейкиМм = высотаНаклейки,
                Ошибка = string.Format(
                    CultureInfo.InvariantCulture,
                    CellOverflowErrorTemplate,
                    ширинаНаклейки,
                    высотаНаклейки,
                    шагX,
                    шагY,
                    ИмяЛиста(sheetSize),
                    ширинаЛиста,
                    высотаЛиста),
            };
        }

        var позиции = new List<RectangleF>(rows * cols);
        var метки = new List<RectangleF>();

        for (var ряд = 0; ряд < rows; ряд++)
        {
            for (var столбец = 0; столбец < cols; столбец++)
            {
                var левый = (float)(mmMargins.LeftMm + (столбец * шагX));
                var верхний = (float)(mmMargins.TopMm + (ряд * шагY));

                позиции.Add(new RectangleF(
                    левый,
                    верхний,
                    (float)ширинаНаклейки,
                    (float)высотаНаклейки));

                ДобавитьМеткиОбреза(
                    метки,
                    левый,
                    верхний,
                    (float)ширинаНаклейки,
                    (float)высотаНаклейки,
                    (float)ширинаЛиста,
                    (float)высотаЛиста);
            }
        }

        return new SheetLayoutResult
        {
            Рядов = rows,
            Столбцов = cols,
            НаклеекНаЛисте = rows * cols,
            ШиринаНаклейкиМм = ширинаНаклейки,
            ВысотаНаклейкиМм = высотаНаклейки,
            ВылетМм = DefaultBleedMm,
            Позиции = позиции,
            МеткиОбреза = метки,
        };
    }

    /// <summary>
    /// Возвращает габариты листа выбранного формата с учётом ориентации.
    /// </summary>
    /// <param name="sheetSize">Формат листа.</param>
    /// <param name="orientation">Ориентация листа.</param>
    /// <returns>Ширина и высота листа, мм.</returns>
    public static (decimal Width, decimal Height) РазмерыЛиста(
        SheetSize sheetSize,
        TemplateOrientation orientation)
    {
        var (книжнаяШирина, книжнаяВысота) = sheetSize switch
        {
            SheetSize.A5 => (148m, 210m),
            SheetSize.A3 => (297m, 420m),
            _ => (210m, 297m),
        };

        return orientation == TemplateOrientation.Landscape
            ? (книжнаяВысота, книжнаяШирина)
            : (книжнаяШирина, книжнаяВысота);
    }

    private static void ДобавитьМеткиОбреза(
        List<RectangleF> метки,
        float левый,
        float верхний,
        float ширина,
        float высота,
        float ширинаЛиста,
        float высотаЛиста)
    {
        var длина = (float)CropMarkLengthMm;
        var толщина = (float)CropMarkThicknessMm;
        var отступ = (float)DefaultBleedMm;

        // Метки выносятся за границы наклейки на величину вылета, но всегда
        // остаются внутри листа: иначе они обрезаются краем листа.
        ДобавитьМетку(
            метки,
            new RectangleF(левый - отступ - длина, верхний - отступ, длина, толщина),
            ширинаЛиста,
            высотаЛиста);

        ДобавитьМетку(
            метки,
            new RectangleF(левый - отступ - длина, верхний + высота + отступ, длина, толщина),
            ширинаЛиста,
            высотаЛиста);

        ДобавитьМетку(
            метки,
            new RectangleF(левый + ширина + отступ, верхний - отступ, длина, толщина),
            ширинаЛиста,
            высотаЛиста);

        ДобавитьМетку(
            метки,
            new RectangleF(левый + ширина + отступ, верхний + высота + отступ, длина, толщина),
            ширинаЛиста,
            высотаЛиста);
    }

    /// <summary>
    /// Добавляет метку обреза, прижимая её к границам листа: метка никогда не
    /// выходит за пределы листа. Если внутри листа для метки не остаётся
    /// места, метка не выводится вовсе.
    /// </summary>
    /// <param name="метки">Накопитель меток обреза.</param>
    /// <param name="метка">Исходное положение метки, мм.</param>
    /// <param name="ширинаЛиста">Ширина листа, мм.</param>
    /// <param name="высотаЛиста">Высота листа, мм.</param>
    private static void ДобавитьМетку(
        List<RectangleF> метки,
        RectangleF метка,
        float ширинаЛиста,
        float высотаЛиста)
    {
        if (метка.Width <= 0f || метка.Height <= 0f)
        {
            return;
        }

        if (метка.Width > ширинаЛиста || метка.Height > высотаЛиста)
        {
            return;
        }

        var левый = Math.Clamp(метка.Left, 0f, ширинаЛиста - метка.Width);
        var верхний = Math.Clamp(метка.Top, 0f, высотаЛиста - метка.Height);

        метки.Add(new RectangleF(левый, верхний, метка.Width, метка.Height));
    }

    /// <summary>
    /// Возвращает обозначение формата листа для текста ошибки разбивки.
    /// </summary>
    /// <param name="sheetSize">Формат листа.</param>
    /// <returns>Обозначение формата (A4, A3, A5).</returns>
    private static string ИмяЛиста(SheetSize sheetSize) => sheetSize switch
    {
        SheetSize.A5 => "A5",
        SheetSize.A3 => "A3",
        _ => "A4",
    };
}
