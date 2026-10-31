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
