using System.Globalization;
using System.Windows.Forms;

namespace Picket302.App.UI.Components;

/// <summary>
/// Всплывающий календарь для выбора даты (ТЗ §8.12, функция 163: «встроенные
/// калькулятор и всплывающий календарь»).
/// </summary>
/// <remarks>
/// <para>
/// Календарь показывается отдельным всплывающим окном поверх элемента, из
/// которого он вызван; выбранная дата возвращается вызывающему коду через
/// событие <see cref="DateSelected"/>.
/// </para>
/// <para>
/// Реализация не использует вызовы системных функций: календарь строится на
/// штатном элементе <see cref="MonthCalendar"/> Windows Forms.
/// </para>
/// </remarks>
public sealed class PopupCalendar
{
    private readonly ToolStripDropDown _всплывающее;
    private readonly MonthCalendar _календарь = new();

    /// <summary>
    /// Создаёт всплывающий календарь.
    /// </summary>
    public PopupCalendar()
    {
        _календарь.Dock = DockStyle.Fill;
        _календарь.Size = new Size(220, 200);
        _календарь.DateChanged += (_, _) =>
        {
            ВыбраннаяДата = _календарь.SelectionStart;
            DateSelected?.Invoke(this, ВыбраннаяДата);
        };

        _всплывающее = new ToolStripDropDown
        {
            AutoClose = true,
            Padding = new Padding(2),
        };

        _всплывающее.Items.Add(new ToolStripControlHost(_календарь) { Margin = Padding.Empty });
    }

    /// <summary>
    /// Событие выбора даты.
    /// </summary>
    public event EventHandler<DateTime>? DateSelected;

    /// <summary>
    /// Выбранная дата.
    /// </summary>
    public DateTime ВыбраннаяДата { get; private set; } = DateTime.Today;

    /// <summary>
    /// Показывает календарь рядом с указанным элементом.
    /// </summary>
    /// <param name="якорь">Элемент, рядом с которым показывается календарь.</param>
    public void ShowPopup(Control якорь)
    {
        ArgumentNullException.ThrowIfNull(якорь);

        _календарь.SelectionStart = ВыбраннаяДата;

        var экранная = якорь.PointToScreen(new Point(0, якорь.Height));

        _всплывающее.Show(new Point(экранная.X, экранная.Y));
    }

    /// <summary>
    /// Скрывает всплывающий календарь.
    /// </summary>
    public void HidePopup() => _всплывающее.Close();

    /// <summary>
    /// Формирует название месяца и года для заголовка календаря.
    /// </summary>
    /// <param name="дата">Отображаемая дата.</param>
    /// <returns>Название месяца и года на русском языке.</returns>
    public static string BuildCaption(DateTime дата)
    {
        var название = CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat.GetMonthName(дата.Month);

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{название} {дата.Year}");
    }
}
