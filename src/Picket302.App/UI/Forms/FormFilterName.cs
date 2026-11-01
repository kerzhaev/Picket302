using System.Drawing;
using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог ввода имени сохраняемого фильтра адресной книги
/// (ТЗ §8.1, функция 9: «сохранённые пользовательские фильтры»).
/// </summary>
public sealed class FormFilterName : Form
{
    private readonly TextBox _поле = new();

    /// <summary>
    /// Создаёт диалог ввода имени фильтра.
    /// </summary>
    /// <param name="заголовок">Заголовок приглашения.</param>
    /// <param name="начальное">Начальное значение имени.</param>
    public FormFilterName(string заголовок, string начальное = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(заголовок);

        Text = "Фильтр адресной книги";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 120);

        var надпись = new Label
        {
            Text = заголовок,
            AutoSize = true,
            Location = new Point(16, 18),
        };

        _поле.Location = new Point(16, 44);
        _поле.Width = 348;
        _поле.Text = начальное;

        var кнопкаОк = new Button
        {
            Text = "ОК",
            DialogResult = DialogResult.OK,
            Size = new Size(110, 28),
            Location = new Point(158, 78),
        };

        var кнопкаОтмена = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 28),
            Location = new Point(274, 78),
        };

        Controls.AddRange(new Control[] { надпись, _поле, кнопкаОк, кнопкаОтмена });

        AcceptButton = кнопкаОк;
        CancelButton = кнопкаОтмена;
    }

    /// <summary>Введённое имя фильтра.</summary>
    public string Имя => _поле.Text.Trim();
}
