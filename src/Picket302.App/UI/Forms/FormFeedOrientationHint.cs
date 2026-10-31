using System.Drawing;
using System.Windows.Forms;
using Picket302.Printing.Warnings;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Модальное окно обучающего предупреждения о подаче конвертов узкой стороной
/// (ТЗ §8.5, функция 147).
/// </summary>
/// <remarks>
/// Окно содержит галку «Больше не показывать это сообщение»; после её включения
/// предупреждение не показывается до сброса настроек.
/// </remarks>
public sealed class FormFeedOrientationHint : Form
{
    private readonly CheckBox _подавить = new()
    {
        Text = FeedOrientationHint.SuppressCaption,
        AutoSize = true,
        Location = new Point(16, 96),
    };

    /// <summary>
    /// Создаёт окно предупреждения.
    /// </summary>
    public FormFeedOrientationHint()
    {
        Text = "Подача конвертов";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 170);

        var текст = new Label
        {
            Text = FeedOrientationHint.WarningText,
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(428, 40),
        };

        var справка = new Label
        {
            Text = FeedOrientationHint.HelpText,
            AutoSize = false,
            Location = new Point(16, 60),
            Size = new Size(428, 32),
            ForeColor = SystemColors.GrayText,
        };

        var кнопкаПродолжить = new Button
        {
            Text = "Продолжить",
            DialogResult = DialogResult.OK,
            Size = new Size(110, 28),
            Location = new Point(240, 128),
        };

        var кнопкаОтмена = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 28),
            Location = new Point(356, 128),
        };

        Controls.AddRange(new Control[] { текст, справка, _подавить, кнопкаПродолжить, кнопкаОтмена });

        AcceptButton = кнопкаПродолжить;
        CancelButton = кнопкаОтмена;
    }

    /// <summary>Признак включения галки подавления предупреждения.</summary>
    public bool ПодавитьПредупреждение => _подавить.Checked;
}
