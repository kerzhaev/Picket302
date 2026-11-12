using System.Globalization;
using Picket302.Domain.Entities;
using Picket302.Domain.Postal;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Калибровочные смещения для пары «принтер + формат конверта»
/// (ТЗ §8.4, функция 45): оператор задаёт смещение с шагом 0,1 мм;
/// значения применяются в предпросмотре и печати независимо для каждой
/// пары и не смешиваются между парами.
/// </summary>
public sealed class FormPrinterCalibration : Form
{
    private readonly PrinterOffsetProfile _профиль;
    private readonly string _принтер;
    private readonly EnvelopeFormat _формат;

    private readonly NumericUpDown _смещениеХ = new()
    {
        Minimum = -50m,
        Maximum = 50m,
        Increment = PrinterOffsetProfile.StepMm,
        DecimalPlaces = 1,
        Dock = DockStyle.Fill,
    };

    private readonly NumericUpDown _смещениеУ = new()
    {
        Minimum = -50m,
        Maximum = 50m,
        Increment = PrinterOffsetProfile.StepMm,
        DecimalPlaces = 1,
        Dock = DockStyle.Fill,
    };

    /// <summary>
    /// Создаёт диалог калибровки для выбранной пары «принтер + формат».
    /// </summary>
    /// <param name="профиль">Профиль смещений (хранение).</param>
    /// <param name="принтер">Имя принтера.</param>
    /// <param name="формат">Формат конверта.</param>
    public FormPrinterCalibration(
        PrinterOffsetProfile профиль,
        string принтер,
        EnvelopeFormat формат)
    {
        ArgumentNullException.ThrowIfNull(профиль);
        ArgumentException.ThrowIfNullOrWhiteSpace(принтер);

        _профиль = профиль;
        _принтер = принтер;
        _формат = формат;

        Text = "Калибровка принтера";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(360, 190);

        var подписьПары = new Label
        {
            Text = $"Принтер: {_принтер}   Формат: {_формат}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            AccessibleName = "Пара принтер-формат",
        };

        var надписьХ = new Label { Text = "Смещение X, мм:", AutoSize = true, Anchor = AnchorStyles.Left };
        var надписьУ = new Label { Text = "Смещение Y, мм:", AutoSize = true, Anchor = AnchorStyles.Left };

        var сохранить = new Button
        {
            Text = "Сохранить",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AccessibleName = "Сохранить калибровку",
        };

        var сброс = new Button
        {
            Text = "Сбросить",
            AutoSize = true,
            AccessibleName = "Сбросить калибровку",
        };

        сброс.Click += async (_, _) =>
        {
            _смещениеХ.Value = 0m;
            _смещениеУ.Value = 0m;
            await _профиль.SaveOffsetAsync(_принтер, _формат, 0f, 0f).ConfigureAwait(true);
            MessageBox.Show(
                this,
                "Калибровка пары сброшена (0,0 мм).",
                "Калибровка принтера",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        var отмена = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };

        сохранить.Click += async (_, _) =>
        {
            await _профиль.SaveOffsetAsync(
                _принтер,
                _формат,
                (float)_смещениеХ.Value,
                (float)_смещениеУ.Value).ConfigureAwait(true);
        };

        var корень = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 2,
            RowCount = 5,
        };
        корень.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        корень.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        for (var строка = 0; строка < 5; строка++)
        {
            корень.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        }

        корень.Controls.Add(подписьПары, 0, 0);
        корень.SetColumnSpan(подписьПары, 2);
        корень.Controls.Add(надписьХ, 0, 1);
        корень.Controls.Add(_смещениеХ, 1, 1);
        корень.Controls.Add(надписьУ, 0, 2);
        корень.Controls.Add(_смещениеУ, 1, 2);
        корень.Controls.Add(сохранить, 0, 4);
        корень.Controls.Add(сброс, 1, 4);
        Controls.Add(корень);
        AcceptButton = сохранить;
        CancelButton = отмена;

        // Текущее значение пары (если сохранено) — для предсказуемого
        // восстановления оператором.
        var текущее = _профиль.GetOffsetAsync(_принтер, _формат.ToString())
            .ConfigureAwait(false).GetAwaiter().GetResult();
        _смещениеХ.Value = Math.Clamp(текущее.OffsetXMm, _смещениеХ.Minimum, _смещениеХ.Maximum);
        _смещениеУ.Value = Math.Clamp(текущее.OffsetYMm, _смещениеУ.Minimum, _смещениеУ.Maximum);
    }
}
