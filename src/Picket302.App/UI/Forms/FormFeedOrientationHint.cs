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
        AccessibleName = "Больше не показывать предупреждение о подаче",
        AccessibleDescription = "Не показывать это предупреждение при следующих запусках.",
        AutoSize = true,
        Location = new Point(16, 132),
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
        ClientSize = new Size(460, 204);

        var текст = new Label
        {
            Text = FeedOrientationHint.WarningText,
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(428, 52),
        };

        var справка = new Label
        {
            Text = FeedOrientationHint.HelpText,
            AutoSize = false,
            Location = new Point(16, 72),
            Size = new Size(428, 52),
            ForeColor = SystemColors.GrayText,
        };

        var кнопкаПродолжить = new Button
        {
            Text = "Продолжить",
            AccessibleName = "Продолжить печать",
            AccessibleDescription = "Продолжить после подтверждения ориентации подачи конверта.",
            DialogResult = DialogResult.OK,
            Size = new Size(110, 28),
            Location = new Point(240, 160),
        };

        var кнопкаОтмена = new Button
        {
            Text = "Отмена",
            AccessibleName = "Отменить печать",
            AccessibleDescription = "Отменить печать и закрыть предупреждение.",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 28),
            Location = new Point(356, 160),
        };

        Controls.AddRange(new Control[] { текст, справка, _подавить, кнопкаПродолжить, кнопкаОтмена });

        AcceptButton = кнопкаПродолжить;
        CancelButton = кнопкаОтмена;
    }

    /// <summary>Признак включения галки подавления предупреждения.</summary>
    public bool ПодавитьПредупреждение => _подавить.Checked;
}
