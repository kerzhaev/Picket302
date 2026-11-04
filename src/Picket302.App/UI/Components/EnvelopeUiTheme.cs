using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Picket302.App.UI.Components;

/// <summary>
/// Визуальные константы рабочего экрана печати конвертов.
///
/// Палитра намеренно остаётся классической: светлая рабочая поверхность,
/// тонкие рамки групп, красный акцент отправителя и синий акцент получателя.
/// Это связывает Picket302 с привычным операторским экраном прототипа, не
/// вмешиваясь в сохраняемые настройки таблиц и бизнес-логику.
/// </summary>
public static class EnvelopeUiTheme
{
    /// <summary>Фон рабочего экрана.</summary>
    public static Color WorkspaceBackground => Color.FromArgb(242, 244, 247);

    /// <summary>Фон групповых панелей.</summary>
    public static Color PanelBackground => Color.FromArgb(238, 239, 241);

    /// <summary>Акцент заголовка отправителя.</summary>
    public static Color SenderAccent => Color.FromArgb(148, 35, 35);

    /// <summary>Акцент заголовка получателя.</summary>
    public static Color RecipientAccent => Color.FromArgb(36, 72, 128);

    /// <summary>Акцент параметров отображения.</summary>
    public static Color DisplayAccent => Color.FromArgb(72, 72, 72);

    /// <summary>Акцент поля примечания.</summary>
    public static Color NoteAccent => Color.FromArgb(86, 86, 86);

    /// <summary>Фон заголовка таблицы отправителей.</summary>
    public static Color SenderHeaderBackground => Color.FromArgb(244, 228, 228);

    /// <summary>Фон заголовка таблицы получателей.</summary>
    public static Color RecipientHeaderBackground => Color.FromArgb(225, 233, 246);

    /// <summary>Рамка компактных панелей.</summary>
    public static Color Border => Color.FromArgb(150, 150, 150);

    /// <summary>
    /// Настраивает GroupBox рабочего экрана в стиле классического оператора:
    /// плотный заголовок, явная рамка и нейтральная поверхность.
    /// </summary>
    /// <param name="группа">Группа.</param>
    /// <param name="акцент">Цвет заголовка.</param>
    public static void ОформитьГруппу(GroupBox группа, Color акцент)
    {
        ArgumentNullException.ThrowIfNull(группа);

        группа.Font = new Font("Tahoma", 8f, FontStyle.Bold);
        группа.ForeColor = акцент;
        группа.BackColor = PanelBackground;
        группа.Padding = new Padding(6, 17, 6, 4);
    }

    /// <summary>
    /// Накладывает тонкий внешний стиль на таблицу, не меняя пользовательские
    /// цвета выделения, шрифт или признак видимости заголовков.
    /// </summary>
    /// <param name="таблица">Таблица.</param>
    /// <param name="фонЗаголовка">Фон заголовка.</param>
    /// <param name="цветЗаголовка">Цвет текста заголовка.</param>
    public static void ОформитьЗаголовокТаблицы(
        DataGridView таблица,
        Color фонЗаголовка,
        Color цветЗаголовка)
    {
        ArgumentNullException.ThrowIfNull(таблица);

        таблица.EnableHeadersVisualStyles = false;
        таблица.ColumnHeadersDefaultCellStyle.BackColor = фонЗаголовка;
        таблица.ColumnHeadersDefaultCellStyle.ForeColor = цветЗаголовка;
        таблица.ColumnHeadersDefaultCellStyle.Font = new Font("Tahoma", 8f, FontStyle.Bold);
        таблица.ColumnHeadersDefaultCellStyle.SelectionBackColor = фонЗаголовка;
        таблица.ColumnHeadersDefaultCellStyle.SelectionForeColor = цветЗаголовка;
        таблица.ColumnHeadersHeight = 21;
        таблица.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
    }

    /// <summary>
    /// Добавляет на кнопку небольшой векторный знак. Текст кнопки сохраняется,
    /// чтобы горячие действия оставались понятными и доступными для UIA.
    /// </summary>
    /// <param name="вид">Тип действия: add, edit, delete, print, preview, export или settings.</param>
    /// <returns>Bitmap для свойства <see cref="Button.Image"/>.</returns>
    public static Bitmap СоздатьЗначок(string вид)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(вид);

        var bitmap = new Bitmap(14, 14);

        using var graphics = Graphics.FromImage(bitmap);
        using var pen = new Pen(Color.FromArgb(55, 65, 81), 1.35f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        switch (вид)
        {
            case "add":
                graphics.DrawLine(pen, 7, 2, 7, 12);
                graphics.DrawLine(pen, 2, 7, 12, 7);
                break;

            case "edit":
                graphics.DrawLine(pen, 3, 11, 11, 3);
                graphics.DrawLine(pen, 2, 12, 5, 11);
                graphics.DrawLine(pen, 10, 2, 12, 4);
                break;

            case "delete":
                graphics.DrawRectangle(pen, 4, 4, 6, 8);
                graphics.DrawLine(pen, 3, 3, 11, 3);
                graphics.DrawLine(pen, 6, 1, 8, 1);
                break;

            case "print":
                graphics.DrawRectangle(pen, 3, 1, 8, 4);
                graphics.DrawRectangle(pen, 2, 5, 10, 5);
                graphics.DrawLine(pen, 4, 10, 10, 10);
                graphics.DrawLine(pen, 4, 11, 10, 11);
                break;

            case "preview":
                graphics.DrawRectangle(pen, 1, 3, 12, 8);
                graphics.DrawLine(pen, 4, 6, 10, 6);
                graphics.DrawLine(pen, 4, 8, 8, 8);
                break;

            case "export":
                graphics.DrawRectangle(pen, 2, 2, 8, 10);
                graphics.DrawLine(pen, 5, 5, 12, 5);
                graphics.DrawLine(pen, 9, 2, 12, 5);
                graphics.DrawLine(pen, 9, 5, 12, 5);
                break;

            case "settings":
                graphics.DrawEllipse(pen, 3, 3, 8, 8);
                graphics.DrawEllipse(pen, 6, 6, 2, 2);
                graphics.DrawLine(pen, 7, 1, 7, 3);
                graphics.DrawLine(pen, 7, 11, 7, 13);
                graphics.DrawLine(pen, 1, 7, 3, 7);
                graphics.DrawLine(pen, 11, 7, 13, 7);
                break;

            default:
                graphics.DrawRectangle(pen, 2, 2, 10, 10);
                break;
        }

        return bitmap;
    }
}
