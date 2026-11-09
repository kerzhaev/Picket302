using System.Drawing;
using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Модальное окно блокирующего предупреждения о слишком большом конверте
/// (ТЗ §8.6, функция 85: «Конверт слишком большой для выбранного принтера»
/// с числовым указанием обеих величин (мм) и перечнем доступных форматов
/// выбранного принтера).
/// </summary>
/// <remarks>
/// Текст предупреждения формирует слой печати
/// (<c>Printing.Validation.OversizeWarning</c>); окно выводит его без
/// изменений и закрывается кнопкой «ОК».
/// </remarks>
public sealed class FormOversizeWarning : Form
{
    /// <summary>
    /// Создаёт окно предупреждения.
    /// </summary>
    /// <param name="сообщение">Текст предупреждения.</param>
    public FormOversizeWarning(string сообщение)
    {
        Text = "Конверт слишком большой для выбранного принтера";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(480, 160);

        var текст = new Label
        {
            Text = сообщение,
            AutoSize = false,
            Location = new Point(12, 12),
            Size = new Size(456, 96),
        };

        var кнопка = new Button
        {
            Text = "ОК",
            AccessibleName = "Закрыть предупреждение о размере",
            AccessibleDescription = "Закрыть предупреждение о слишком большом конверте.",
            DialogResult = DialogResult.OK,
            Size = new Size(96, 28),
            Location = new Point(372, 116),
        };

        Controls.AddRange(new Control[] { текст, кнопка });

        AcceptButton = кнопка;
        CancelButton = кнопка;
    }
}
