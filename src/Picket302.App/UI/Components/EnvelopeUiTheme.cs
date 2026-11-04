using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Picket302.App.UI.Components;

/// <summary>
/// Единая визуальная система рабочего экрана печати конвертов.
///
/// Внешний слой оставляет операторскую плотность и две смысловые роли
/// прототипа (красный отправитель / синий получатель), но убирает разрозненный
/// «классический» вид отдельных форм: одинаковые шрифты, карточки, границы,
/// состояния кнопок и читаемые пустые области задаются из одного места.
/// Класс не содержит бизнес-правил и не меняет сохраняемые настройки таблиц.
/// </summary>
public static class EnvelopeUiTheme
{
    /// <summary>Семейство шрифта интерфейса Windows-оператора.</summary>
    public const string ШрифтСемейство = "Segoe UI";

    /// <summary>Базовый размер текста формы.</summary>
    public const float РазмерШрифта = 9f;

    /// <summary>Компактный размер текста строки состояния.</summary>
    public const float РазмерКомпактногоШрифта = 8.5f;

    /// <summary>Фон рабочего экрана.</summary>
    public static Color WorkspaceBackground => Color.FromArgb(238, 238, 238);

    /// <summary>Фон карточек и полей.</summary>
    public static Color CardBackground => SystemColors.Window;

    /// <summary>Фон групповых панелей.</summary>
    public static Color PanelBackground => Color.FromArgb(232, 232, 232);

    /// <summary>Основной цвет текста.</summary>
    public static Color Text => Color.FromArgb(32, 32, 32);

    /// <summary>Вторичный цвет подписей и вспомогательных сведений.</summary>
    public static Color MutedText => Color.FromArgb(86, 86, 86);

    /// <summary>Основной синий цвет действий и фокуса.</summary>
    public static Color Primary => Color.FromArgb(0, 102, 153);

    /// <summary>Синий цвет наведения.</summary>
    public static Color PrimaryHover => Color.FromArgb(0, 82, 130);

    /// <summary>Янтарный цвет предупреждений и вторичного акцента.</summary>
    public static Color Accent => Color.FromArgb(190, 115, 0);

    /// <summary>Цвет успешно завершённого состояния.</summary>
    public static Color Success => Color.FromArgb(21, 128, 61);

    /// <summary>Цвет ошибки и опасного действия.</summary>
    public static Color Danger => Color.FromArgb(220, 38, 38);

    /// <summary>Акцент заголовка отправителя.</summary>
    public static Color SenderAccent => Color.FromArgb(180, 0, 0);

    /// <summary>Акцент заголовка получателя.</summary>
    public static Color RecipientAccent => Color.FromArgb(0, 82, 160);

    /// <summary>Акцент параметров отображения.</summary>
    public static Color DisplayAccent => Color.FromArgb(80, 80, 80);

    /// <summary>Акцент поля примечания.</summary>
    public static Color NoteAccent => Color.FromArgb(80, 80, 80);

    /// <summary>Фон заголовка таблицы отправителей.</summary>
    public static Color SenderHeaderBackground => Color.FromArgb(252, 238, 238);

    /// <summary>Фон заголовка таблицы получателей.</summary>
    public static Color RecipientHeaderBackground => Color.FromArgb(232, 241, 252);

    /// <summary>Рамка компактных панелей.</summary>
    public static Color Border => Color.FromArgb(166, 166, 166);

    /// <summary>Более заметная рамка фокуса и разделителей.</summary>
    public static Color StrongBorder => Color.FromArgb(128, 128, 128);

    /// <summary>Создаёт базовый шрифт обычного текста.</summary>
    public static Font ШрифтТекста(float? размер = null, FontStyle стиль = FontStyle.Regular) =>
        new(ШрифтСемейство, размер ?? РазмерШрифта, стиль, GraphicsUnit.Point);

    /// <summary>Создаёт шрифт подписи групп и заголовков.</summary>
    public static Font ШрифтЗаголовка(float? размер = null) =>
        ШрифтТекста(размер ?? РазмерШрифта, FontStyle.Bold);

    /// <summary>
    /// Применяет тему ко всему окну и его дочерним элементам. Это намеренно
    /// идемпотентная операция: формы могут вызвать её после построения своих
    /// контролов, не меняя обработчики и имена элементов.
    /// </summary>
    /// <param name="форма">Форма, которую нужно оформить.</param>
    public static void ОформитьОкно(Form форма)
    {
        ArgumentNullException.ThrowIfNull(форма);

        форма.Font = ШрифтТекста();
        форма.ForeColor = Text;
        форма.BackColor = WorkspaceBackground;
        ОформитьКонтрол(форма);
    }

    /// <summary>
    /// Оформляет ленту действий и её кнопки в одном стиле с вкладками.
    /// </summary>
    /// <param name="полоса">Полоса действий.</param>
    public static void ОформитьПолосуДействий(ToolStrip полоса)
    {
        ArgumentNullException.ThrowIfNull(полоса);

        полоса.Font = ШрифтТекста();
        полоса.BackColor = CardBackground;
        полоса.ForeColor = Text;
        полоса.GripStyle = ToolStripGripStyle.Hidden;
        полоса.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());

        foreach (ToolStripItem элемент in полоса.Items)
        {
            элемент.Font = ШрифтТекста(8.5f);
            элемент.ForeColor = Text;

            if (элемент is ToolStripButton кнопка)
            {
                кнопка.BackColor = CardBackground;
                кнопка.Padding = new Padding(8, 4, 8, 4);
                кнопка.Margin = new Padding(1, 1, 1, 1);
                кнопка.AutoSize = true;
            }
        }
    }

    /// <summary>
    /// Настраивает GroupBox рабочего экрана в стиле классического оператора:
    /// плотный заголовок, явная рамка и нейтральная поверхность.
    /// </summary>
    /// <param name="группа">Группа.</param>
    /// <param name="акцент">Цвет заголовка.</param>
    public static void ОформитьГруппу(GroupBox группа, Color акцент)
    {
        ArgumentNullException.ThrowIfNull(группа);

        группа.Font = ШрифтЗаголовка();
        группа.ForeColor = акцент;
        группа.BackColor = CardBackground;
        группа.Padding = new Padding(8, 17, 8, 4);
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
        таблица.ColumnHeadersDefaultCellStyle.Font = ШрифтЗаголовка(8.5f);
        таблица.ColumnHeadersDefaultCellStyle.SelectionBackColor = фонЗаголовка;
        таблица.ColumnHeadersDefaultCellStyle.SelectionForeColor = цветЗаголовка;
        таблица.ColumnHeadersHeight = 25;
        таблица.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
    }

    /// <summary>
    /// Рекурсивно задаёт общие свойства WinForms-контролов. Специальные
    /// акценты GroupBox/DataGridView сохраняются, а разрозненные старые шрифты и серые
    /// системные поверхности заменяются токенами темы.
    /// </summary>
    private static void ОформитьКонтрол(Control контрол)
    {
        if (контрол is not Form)
        {
            var текущийРазмер = контрол.Font.Size <= 8.5f
                ? РазмерШрифта
                : контрол.Font.Size;

            контрол.Font = ШрифтТекста(текущийРазмер, контрол.Font.Style);
        }

        switch (контрол)
        {
            case GroupBox группа:
                группа.Font = ШрифтЗаголовка();
                группа.BackColor = CardBackground;
                группа.Padding = группа.Padding == Padding.Empty
                    ? new Padding(8, 17, 8, 4)
                    : группа.Padding;
                break;

            case Button кнопка:
                кнопка.Font = ШрифтТекста(8.5f, кнопка.Font.Style);
                кнопка.FlatStyle = FlatStyle.Flat;
                кнопка.FlatAppearance.BorderColor = Border;
                кнопка.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 241, 252);
                кнопка.FlatAppearance.MouseDownBackColor = Color.FromArgb(210, 225, 242);
                кнопка.BackColor = CardBackground;
                кнопка.ForeColor = Text;
                кнопка.UseVisualStyleBackColor = false;
                кнопка.Padding = new Padding(8, 4, 8, 4);
                break;

            case TextBox поле:
                поле.BackColor = CardBackground;
                поле.ForeColor = Text;
                поле.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ComboBox выбор:
                выбор.BackColor = CardBackground;
                выбор.ForeColor = Text;
                выбор.FlatStyle = FlatStyle.Standard;
                break;

            case DataGridView таблица:
                таблица.BackgroundColor = CardBackground;
                таблица.GridColor = Border;
                таблица.BorderStyle = BorderStyle.FixedSingle;
                break;

            case TreeView дерево:
                дерево.BackColor = CardBackground;
                дерево.ForeColor = Text;
                дерево.BorderStyle = BorderStyle.FixedSingle;
                дерево.HideSelection = false;
                break;

            case TabControl вкладки:
                вкладки.Font = ШрифтТекста();
                вкладки.Padding = new Point(14, 7);
                вкладки.BackColor = CardBackground;
                break;

            case MenuStrip меню:
                ОформитьМеню(меню);
                break;

            case ToolStrip полоса:
                ОформитьПолосуДействий(полоса);
                break;

            case Label метка when метка.Parent is not StatusStrip:
                метка.ForeColor = метка.ForeColor == SystemColors.ControlText
                    ? Text
                    : метка.ForeColor;
                break;

            case CheckBox флажок:
                флажок.ForeColor = Text;
                break;

            case RadioButton переключатель:
                переключатель.ForeColor = Text;
                break;

            case Panel панель when панель.BackColor == SystemColors.Control:
                панель.BackColor = WorkspaceBackground;
                break;
        }

        foreach (Control дочерний in контрол.Controls)
        {
            ОформитьКонтрол(дочерний);
        }
    }

    /// <summary>Оформляет меню главного окна и контекстные меню.</summary>
    private static void ОформитьМеню(MenuStrip меню)
    {
        меню.Font = ШрифтТекста();
        меню.BackColor = CardBackground;
        меню.ForeColor = Text;
        меню.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable());

        foreach (ToolStripItem элемент in меню.Items)
        {
            ОформитьЭлементМеню(элемент);
        }
    }

    private static void ОформитьЭлементМеню(ToolStripItem элемент)
    {
        элемент.Font = ШрифтТекста();
        элемент.ForeColor = Text;

        if (элемент is ToolStripDropDownItem выпадающий)
        {
            foreach (ToolStripItem дочерний in выпадающий.DropDownItems)
            {
                ОформитьЭлементМеню(дочерний);
            }
        }
    }

    private sealed class ThemeColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => Border;

        public override Color MenuItemBorder => Border;

        public override Color MenuItemSelected => Color.FromArgb(232, 241, 252);

        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(232, 241, 252);

        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(210, 225, 242);

        public override Color MenuStripGradientBegin => CardBackground;

        public override Color MenuStripGradientEnd => CardBackground;

        public override Color ToolStripGradientBegin => CardBackground;

        public override Color ToolStripGradientEnd => CardBackground;

        public override Color ToolStripDropDownBackground => CardBackground;

        public override Color ImageMarginGradientBegin => PanelBackground;

        public override Color ImageMarginGradientMiddle => PanelBackground;

        public override Color ImageMarginGradientEnd => PanelBackground;
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
