using System.Drawing;
using System.Windows.Forms;
using Picket302.Domain.Contracts;

namespace Picket302.App.UI.Components;

/// <summary>
/// Применение настраиваемого оформления таблиц
/// (ТЗ §7.10: «Правила цветовой схемы и плотности»; §8.11, функция 131).
/// </summary>
/// <remarks>
/// <para>
/// Плотность рабочего режима: кегль таблиц 8pt, высота строки ~17px, классический
/// Windows-вид без анимации.
/// </para>
/// <para>
/// Три независимых настраиваемых цвета: фокус, выделение и подсказка; «зебра»
/// (чередование строк) выключается глобально.
/// </para>
/// </remarks>
public static class GridAppearanceManager
{
    /// <summary>
    /// Область сброса оформления (ТЗ §8.11, функция 131:
    /// <c>public void ResetToDefaults(Scope scope) // Grid | MainForm</c>).
    /// </summary>
    public enum GridAppearanceScope
    {
        /// <summary>Сброс оформления отдельной таблицы.</summary>
        Grid = 0,

        /// <summary>Сброс оформления главной формы; требует перезапуска.</summary>
        MainForm = 1,
    }

    /// <summary>
    /// Результат сброса оформления (ТЗ §8.11, функция 131: сброс главной формы
    /// требует перезапуска — предупреждение выводится явно).
    /// </summary>
    /// <param name="Appearance">Оформление после сброса.</param>
    /// <param name="RequiresRestart">
    /// Признак того, что для применения сброса требуется перезапуск главной формы.
    /// </param>
    /// <param name="Message">Предупреждение для оператора.</param>
    public sealed record GridResetResult(
        GridAppearance Appearance,
        bool RequiresRestart,
        string Message);

    /// <summary>Таблицы, к которым применяется оформление единообразно.</summary>
    private static readonly List<DataGridView> ЗарегистрированныеТаблицы = [];

    /// <summary>
    /// Регистрирует таблицу, к которой применяется оформление.
    /// </summary>
    /// <param name="таблица">Регистрируемая таблица.</param>
    public static void Register(DataGridView таблица)
    {
        ArgumentNullException.ThrowIfNull(таблица);

        if (!ЗарегистрированныеТаблицы.Contains(таблица))
        {
            ЗарегистрированныеТаблицы.Add(таблица);
        }
    }

    /// <summary>
    /// Применяет оформление ко всем таблицам программы единообразно
    /// (ТЗ §8.11, функция 131: <c>public void ApplyAppearance(GridAppearance appearance)</c>).
    /// </summary>
    /// <param name="appearance">Оформление.</param>
    public static void ApplyAppearance(GridAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        foreach (var таблица in ЗарегистрированныеТаблицы.ToArray())
        {
            if (таблица.IsDisposed)
            {
                ЗарегистрированныеТаблицы.Remove(таблица);
                continue;
            }

            Применить(таблица, appearance);
        }
    }

    /// <summary>
    /// Сбрасывает оформление к значениям по умолчанию (ТЗ §8.11, функция 131:
    /// <c>public void ResetToDefaults(Scope scope) // Grid | MainForm</c>).
    /// </summary>
    /// <param name="scope">Область сброса.</param>
    /// <returns>Оформление после сброса и предупреждение о перезапуске.</returns>
    public static GridResetResult ResetToDefaults(GridAppearanceScope scope)
    {
        var требуетсяПерезапуск = scope == GridAppearanceScope.MainForm;

        var сообщение = требуетсяПерезапуск
            ? "Сброс оформления главной формы вступит в силу после перезапуска программы."
            : "Оформление таблицы сброшено к значениям по умолчанию.";

        var оформление = GridAppearance.Default;

        ApplyAppearance(оформление);

        return new GridResetResult(оформление, требуетсяПерезапуск, сообщение);
    }

    /// <summary>
    /// Применяет оформление к таблице.
    /// </summary>
    /// <param name="таблица">Таблица, к которой применяется оформление.</param>
    /// <param name="оформление">Оформление.</param>
    public static void Применить(DataGridView таблица, GridAppearance оформление)
    {
        ArgumentNullException.ThrowIfNull(таблица);
        ArgumentNullException.ThrowIfNull(оформление);

        таблица.Font = new Font("Tahoma", оформление.FontSize, FontStyle.Regular, GraphicsUnit.Point);
        таблица.RowTemplate.Height = оформление.RowHeight;
        таблица.RowHeadersVisible = false;
        таблица.AllowUserToOrderColumns = true;
        таблица.AllowUserToResizeRows = false;
        таблица.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        таблица.MultiSelect = true;
        таблица.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        таблица.GridColor = SystemColors.Control;
        таблица.BorderStyle = BorderStyle.Fixed3D;

        // Область грида вне строк и столбцов — белая «бумага», а не серый
        // Control (аудит UX 2026-11-03, G3): пустая часть таблицы читается
        // как рабочее поле, а не как неисправность.
        таблица.BackgroundColor = SystemColors.Window;

        таблица.EnableHeadersVisualStyles = false;
        таблица.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        таблица.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
        таблица.ColumnHeadersDefaultCellStyle.Font = new Font("Tahoma", оформление.FontSize, FontStyle.Regular);

        таблица.DefaultCellStyle.SelectionBackColor = Цвет(оформление.SelectedColor);
        таблица.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        таблица.DefaultCellStyle.BackColor = SystemColors.Window;
        таблица.DefaultCellStyle.ForeColor = SystemColors.WindowText;
        таблица.DefaultCellStyle.WrapMode = DataGridViewTriState.False;

        ПрименитьЗебру(таблица, оформление.ZebraEnabled);
    }

    /// <summary>
    /// Включает или выключает чередование строк («зебру»).
    /// </summary>
    /// <param name="таблица">Таблица.</param>
    /// <param name="включена">Признак включения «зелбры».</param>
    public static void ПрименитьЗебру(DataGridView таблица, bool включена)
    {
        ArgumentNullException.ThrowIfNull(таблица);

        if (!включена)
        {
            таблица.AlternatingRowsDefaultCellStyle.BackColor = SystemColors.Window;
            return;
        }

        таблица.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
        таблица.RowsDefaultCellStyle.BackColor = SystemColors.Window;
        таблица.AlternatingRowsDefaultCellStyle.SelectionBackColor = таблица.DefaultCellStyle.SelectionBackColor;
    }

    /// <summary>
    /// Подсвечивает строку таблицы цветом фокуса.
    /// </summary>
    /// <param name="таблица">Таблица.</param>
    /// <param name="индексСтроки">Индекс строки.</param>
    /// <param name="оформление">Оформление.</param>
    public static void ПодсветитьФокус(DataGridView таблица, int индексСтроки, GridAppearance оформление)
    {
        ArgumentNullException.ThrowIfNull(таблица);
        ArgumentNullException.ThrowIfNull(оформление);

        if (индексСтроки < 0 || индексСтроки >= таблица.Rows.Count)
        {
            return;
        }

        таблица.Rows[индексСтроки].DefaultCellStyle.BackColor = Цвет(оформление.FocusColor);
    }

    /// <summary>
    /// Возвращает цвет подсказки — им окрашиваются подсказки и подсветка полей,
    /// требующих проверки.
    /// </summary>
    /// <param name="оформление">Оформление.</param>
    /// <returns>Цвет подсказки.</returns>
    public static Color ЦветПодсказки(GridAppearance оформление)
    {
        ArgumentNullException.ThrowIfNull(оформление);
        return Цвет(оформление.HintColor);
    }

    /// <summary>
    /// Преобразует числовое значение цвета в объект <see cref="Color"/>.
    /// </summary>
    /// <param name="значение">
    /// Значение цвета в формате RGB (24 бита): младший байт — красный канал,
    /// средний — зелёный, старший — синий. Значения по умолчанию из ТЗ §7.10
    /// (4227327, 6205183, 3723516) заданы именно в этом формате.
    /// </param>
    /// <returns>Цвет.</returns>
    public static Color Цвет(int значение) => Color.FromArgb(
        значение & 0xFF,
        (значение >> 8) & 0xFF,
        (значение >> 16) & 0xFF);
}
