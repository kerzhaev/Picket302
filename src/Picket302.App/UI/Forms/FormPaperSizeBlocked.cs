using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Решение оператора при блокировке задания по размеру бумаги (ТЗ §8.6,
/// функция 83).
/// </summary>
public enum PaperSizeBlockedDecision
{
    /// <summary>Печать отменена оператором.</summary>
    Отмена = 0,

    /// <summary>Повторить печать в режиме «Зафиксировать размер A4».</summary>
    ЗафиксироватьA4 = 1,

    /// <summary>Выбрать другой принтер.</summary>
    ВыбратьДругойПринтер = 2,
}

/// <summary>
/// Диалог проверки размера бумаги «шаблон ↔ принтер» (ТЗ §8.6, функция 83:
/// «Диалог с кнопками „Отмена“, „Зафиксировать A4“, „Выбрать другой принтер“»).
/// </summary>
/// <remarks>
/// Окно показывает размер бланка и размер печатной области носителя в
/// миллиметрах; решение оператора возвращается свойством
/// <see cref="Решение"/>.
/// </remarks>
public sealed class FormPaperSizeBlocked : Form
{
    /// <summary>
    /// Создаёт диалог проверки размера бумаги.
    /// </summary>
    /// <param name="размерБланка">Размер бланка, мм.</param>
    /// <param name="размерНосителя">Размер печатной области носителя, мм.</param>
    /// <param name="имяПринтера">Имя выбранного принтера.</param>
    public FormPaperSizeBlocked(SizeF размерБланка, SizeF размерНосителя, string имяПринтера)
    {
        Text = "Проверка размера бумаги";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(520, 190);

        var инвариант = CultureInfo.InvariantCulture;

        var текст = new Label
        {
            Text = string.Create(
                инвариант,
                $"Размер бланка: {размерБланка.Width:0.##}×{размерБланка.Height:0.##} мм; " +
                $"размер печатной области носителя: {размерНосителя.Width:0.##}×{размерНосителя.Height:0.##} мм."),
            AutoSize = false,
            Location = new Point(12, 12),
            Size = new Size(496, 36),
        };

        var принтер = new Label
        {
            Text = string.IsNullOrWhiteSpace(имяПринтера)
                ? "Принтер не выбран."
                : $"Принтер: {имяПринтера}.",
            AutoSize = false,
            Location = new Point(12, 52),
            Size = new Size(496, 20),
            ForeColor = SystemColors.GrayText,
        };

        var подсказка = new Label
        {
            Text = "Задание заблокировано: размер бланка не помещается в печатную область принтера.",
            AutoSize = false,
            Location = new Point(12, 76),
            Size = new Size(496, 32),
        };

        var кнопкаОтмена = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            Size = new Size(112, 28),
            Location = new Point(12, 144),
        };

        var кнопкаA4 = new Button
        {
            Text = "Зафиксировать A4",
            Size = new Size(140, 28),
            Location = new Point(140, 144),
        };

        кнопкаA4.Click += (_, _) =>
        {
            Решение = PaperSizeBlockedDecision.ЗафиксироватьA4;
            DialogResult = DialogResult.OK;
            Close();
        };

        var кнопкаПринтер = new Button
        {
            Text = "Выбрать другой принтер",
            Size = new Size(160, 28),
            Location = new Point(296, 144),
        };

        кнопкаПринтер.Click += (_, _) =>
        {
            Решение = PaperSizeBlockedDecision.ВыбратьДругойПринтер;
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange(new Control[] { текст, принтер, подсказка, кнопкаОтмена, кнопкаA4, кнопкаПринтер });

        AcceptButton = кнопкаA4;
        CancelButton = кнопкаОтмена;
    }

    /// <summary>Решение оператора.</summary>
    public PaperSizeBlockedDecision Решение { get; private set; } = PaperSizeBlockedDecision.Отмена;
}
