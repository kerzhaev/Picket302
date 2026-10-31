using System.Drawing;
using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог подбора значения поля из имеющихся записей
/// (ТЗ §7.7: «подбор значения поля из имеющихся записей»; §8.1, функция 133).
/// </summary>
public sealed class FormValuePicker : Form
{
    private readonly ListBox _список = new();

    /// <summary>
    /// Создаёт диалог подбора значения.
    /// </summary>
    /// <param name="заголовок">Заголовок диалога.</param>
    /// <param name="значения">Имеющиеся значения поля.</param>
    /// <param name="выбранное">Значение, выбранное по умолчанию.</param>
    public FormValuePicker(string заголовок, IReadOnlyList<string> значения, string? выбранное = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(заголовок);
        ArgumentNullException.ThrowIfNull(значения);

        Text = заголовок;
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(320, 260);

        _список.Location = new Point(8, 8);
        _список.Size = new Size(304, 200);
        _список.Font = new Font("Tahoma", 8f);

        foreach (var значение in значения)
        {
            _список.Items.Add(значение);
        }

        if (выбранное is not null)
        {
            var индекс = _список.Items.IndexOf(выбранное);

            if (индекс >= 0)
            {
                _список.SelectedIndex = индекс;
            }
        }
        else if (_список.Items.Count > 0)
        {
            _список.SelectedIndex = 0;
        }

        _список.DoubleClick += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var выбрать = new Button
        {
            Text = "Выбрать",
            Size = new Size(90, 24),
            Location = new Point(8, 216),
        };

        выбрать.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var отмена = new Button
        {
            Text = "Отмена",
            Size = new Size(90, 24),
            Location = new Point(222, 216),
            DialogResult = DialogResult.Cancel,
        };

        Controls.Add(_список);
        Controls.Add(выбрать);
        Controls.Add(отмена);

        AcceptButton = выбрать;
        CancelButton = отмена;
    }

    /// <summary>Выбранное значение поля; пустая строка, если ничего не выбрано.</summary>
    public string ВыбранноеЗначение => _список.SelectedItem as string ?? string.Empty;
}
