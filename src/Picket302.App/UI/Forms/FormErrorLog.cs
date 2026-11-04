using System.Windows.Forms;
using Picket302.Infrastructure.Logging;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Окно «Журнал ошибок» с фильтром по уровню и дате и кнопкой «Скопировать
/// для службы поддержки» (ТЗ §8.11, функция 111; §10.3).
/// </summary>
public sealed class FormErrorLog : Form
{
    private readonly ListBox _записи = new();
    private readonly ComboBox _уровень = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _сДаты = new() { Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _поДату = new() { Format = DateTimePickerFormat.Short };
    private readonly TextBox _фильтр = new();
    private readonly LogHub _журнал;
    private readonly LogChannel _канал;

    /// <summary>
    /// Создаёт окно журнала ошибок.
    /// </summary>
    /// <param name="журнал">Журнал приложения.</param>
    /// <param name="канал">Канал журнала; по умолчанию — ошибки приложения.</param>
    public FormErrorLog(LogHub журнал, LogChannel канал = LogChannel.App)
    {
        ArgumentNullException.ThrowIfNull(журнал);

        _журнал = журнал;
        _канал = канал;

        Text = "Журнал ошибок";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ClientSize = new Size(820, 460);

        ПостроитьИнтерфейс();
        ОбновитьЗаписи();
    }

    /// <summary>
    /// Текст записей журнала, отобранных фильтром.
    /// </summary>
    public string FilteredText => string.Join(Environment.NewLine, _записи.Items.Cast<string>());

    /// <summary>
    /// Текст, который помещается в буфер обмена по кнопке «Скопировать для
    /// службы поддержки».
    /// </summary>
    public string BuildSupportText()
        => _журнал.BuildSupportText(_канал);

    /// <summary>
    /// Применяет фильтр по уровню, датам и тексту.
    /// </summary>
    /// <returns>Число отобранных записей.</returns>
    public int ApplyFilter()
    {
        var уровень = _уровень.SelectedItem as string ?? "Все";
        var сДаты = _сДаты.Value.Date;
        var поДату = _поДату.Value.Date;
        var текст = _фильтр.Text.Trim();

        _записи.Items.Clear();

        foreach (var запись in _журнал.ReadRecent(_канал, 2000))
        {
            if (!СоответствуетУровню(запись, уровень))
            {
                continue;
            }

            if (!СодержитДату(запись, сДаты, поДату))
            {
                continue;
            }

            if (текст.Length > 0
                && !запись.Contains(текст, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            _записи.Items.Add(запись);
        }

        return _записи.Items.Count;
    }

    /// <summary>
    /// Копирует текст журнала в буфер обмена для службы поддержки.
    /// </summary>
    /// <returns>Текст, помещённый в буфер обмена.</returns>
    public string CopyForSupport()
    {
        var текст = BuildSupportText();

        try
        {
            Clipboard.SetText(текст);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Буфер обмена занят другим процессом: текст остаётся в окне,
            // оператор может скопировать его вручную.
        }

        return текст;
    }

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var панельФильтра = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _уровень.Items.AddRange(["Все", "Error", "Warning", "Information", "Debug"]);
        _уровень.SelectedIndex = 0;
        _уровень.Width = 110;

        _фильтр.Width = 200;

        var меткаУровня = new Label { Text = "Уровень:", AutoSize = true, Margin = new Padding(3, 6, 0, 0) };
        var меткаСДаты = new Label { Text = "С:", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
        var меткаПоДату = new Label { Text = "По:", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
        var меткаФильтра = new Label { Text = "Поиск:", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };

        _сДаты.Value = DateTime.Today.AddDays(-7);
        _поДату.Value = DateTime.Today;

        var применить = new Button { Text = "Применить", Width = 90 };
        применить.Click += (_, _) => ApplyFilter();

        панельФильтра.Controls.AddRange(
        [
            меткаУровня,
            _уровень,
            меткаСДаты,
            _сДаты,
            меткаПоДату,
            _поДату,
            меткаФильтра,
            _фильтр,
            применить,
        ]);

        _записи.Dock = DockStyle.Fill;
        _записи.Font = new Font("Consolas", 8f);
        _записи.HorizontalScrollbar = true;

        var копировать = new Button
        {
            Text = "Скопировать для службы поддержки",
            Size = new Size(220, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };

        копировать.Click += (_, _) => CopyForSupport();

        var закрыть = new Button
        {
            Text = "Закрыть",
            Size = new Size(90, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel,
        };

        var нижняяПанель = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        нижняяПанель.Controls.Add(копировать);
        нижняяПанель.Controls.Add(закрыть);
        копировать.Location = new Point(нижняяПанель.Width - 320, 4);
        закрыть.Location = new Point(нижняяПанель.Width - 100, 4);

        Controls.Add(_записи);
        Controls.Add(нижняяПанель);
        Controls.Add(панельФильтра);

        CancelButton = закрыть;
    }

    /// <summary>
    /// Обновляет перечень записей с применением фильтра.
    /// </summary>
    private void ОбновитьЗаписи() => ApplyFilter();

    /// <summary>
    /// Определяет, соответствует ли запись выбранному уровню.
    /// </summary>
    private static bool СоответствуетУровню(string запись, string уровень)
    {
        if (уровень == "Все")
        {
            return true;
        }

        return запись.Contains($"[{уровень}]", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Определяет, попадает ли запись в заданный диапазон дат.
    /// </summary>
    private static bool СодержитДату(string запись, DateTime сДаты, DateTime поДату)
    {
        if (запись.Length < 10)
        {
            return true;
        }

        var фрагмент = запись.Substring(0, 10);

        if (!DateTime.TryParseExact(
                фрагмент,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var дата))
        {
            return true;
        }

        return дата >= сДаты && дата <= поДату;
    }
}
