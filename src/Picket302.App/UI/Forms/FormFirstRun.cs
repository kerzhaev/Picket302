using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Вариант базы данных, предлагаемый при первом запуске (ТЗ §10.6: «Мастер
/// первого запуска: выбор чистой/демо-базы, настройка архивирования
/// (рекомендация сразу после установки), настройка подключения»).
/// </summary>
public enum FirstRunDatabaseChoice
{
    /// <summary>Пустая рабочая база.</summary>
    Clean = 0,

    /// <summary>База с демонстрационными данными.</summary>
    Demo = 1,
}

/// <summary>
/// Мастер первого запуска: выбор чистой или демонстрационной базы и
/// настройка архивирования (ТЗ §10.6; §8.12, функция 163: «диалоги первого
/// запуска»).
/// </summary>
public sealed class FormFirstRun : Form
{
    private readonly RadioButton _чистаяБаза = new() { Text = "Чистая база данных" };
    private readonly RadioButton _демоБаза = new() { Text = "Демонстрационная база данных" };

    private readonly CheckBox _включитьАрхивирование = new() { Text = "Включить автоматическое архивирование базы" };
    private readonly TextBox _папкаАрхивов = new() { Width = 300 };
    private readonly ComboBox _времяАрхивирования = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _предупреждение = new();

    private readonly Button _продолжить = new();
    private readonly Button _отмена = new();

    /// <summary>
    /// Создаёт мастер первого запуска.
    /// </summary>
    public FormFirstRun()
    {
        Text = "Первый запуск программы";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(520, 320);

        ПостроитьИнтерфейс();
        ОбновитьПредупреждение();
    }

    /// <summary>
    /// Выбранный вариант базы данных.
    /// </summary>
    public FirstRunDatabaseChoice DatabaseChoice =>
        _чистаяБаза.Checked ? FirstRunDatabaseChoice.Clean : FirstRunDatabaseChoice.Demo;

    /// <summary>
    /// Признак того, что автоматическое архивирование включено.
    /// </summary>
    public bool ArchivingEnabled => _включитьАрхивирование.Checked;

    /// <summary>
    /// Папка хранения архивов.
    /// </summary>
    public string ArchiveFolder => _папкаАрхивов.Text.Trim();

    /// <summary>
    /// Выбранное время ежедневного архивирования.
    /// </summary>
    public TimeSpan ArchiveTime => _времяАрхивирования.SelectedItem is string значение
        && TimeSpan.TryParse(значение, System.Globalization.CultureInfo.CurrentCulture, out var момент)
            ? момент
            : new TimeSpan(9, 0, 0);

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var группаБазы = new GroupBox
        {
            Text = "База данных",
            Dock = DockStyle.Top,
            Height = 78,
        };

        _чистаяБаза.Location = new Point(14, 26);
        _чистаяБаза.AutoSize = true;
        _демоБаза.Location = new Point(180, 26);
        _демоБаза.AutoSize = true;
        _чистаяБаза.Checked = true;

        группаБазы.Controls.Add(_чистаяБаза);
        группаБазы.Controls.Add(_демоБаза);

        var группаАрхива = new GroupBox
        {
            Text = "Архивирование (рекомендуется настроить сразу после установки)",
            Dock = DockStyle.Top,
            Height = 122,
        };

        _включитьАрхивирование.Location = new Point(14, 26);
        _включитьАрхивирование.AutoSize = true;
        _включитьАрхивирование.Checked = true;
        _включитьАрхивирование.CheckedChanged += (_, _) => ОбновитьПредупреждение();

        var меткаПапки = new Label { Text = "Папка архивов:", Location = new Point(14, 58), AutoSize = true };
        _папкаАрхивов.Location = new Point(110, 54);
        _папкаАрхивов.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Picket302Архивы");

        var обзор = new Button { Text = "…", Location = new Point(420, 52), Size = new Size(28, 24) };
        обзор.Click += (_, _) => ВыбратьПапку();

        var меткаВремени = new Label { Text = "Время архивирования:", Location = new Point(14, 88), AutoSize = true };
        _времяАрхивирования.Location = new Point(140, 84);
        _времяАрхивирования.Width = 90;
        _времяАрхивирования.Items.AddRange(["09:00", "13:05", "16:05"]);
        _времяАрхивирования.SelectedIndex = 0;

        группаАрхива.Controls.Add(_включитьАрхивирование);
        группаАрхива.Controls.Add(меткаПапки);
        группаАрхива.Controls.Add(_папкаАрхивов);
        группаАрхива.Controls.Add(обзор);
        группаАрхива.Controls.Add(меткаВремени);
        группаАрхива.Controls.Add(_времяАрхивирования);

        _предупреждение.Dock = DockStyle.Top;
        _предупреждение.Height = 30;
        _предупреждение.ForeColor = System.Drawing.Color.FromArgb(160, 0, 0);
        _предупреждение.AutoEllipsis = true;

        _продолжить.Text = "Продолжить";
        _продолжить.Size = new Size(100, 26);
        _продолжить.Location = new Point(300, 278);
        _продолжить.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        _отмена.Text = "Отмена";
        _отмена.Size = new Size(90, 26);
        _отмена.Location = new Point(406, 278);
        _отмена.DialogResult = DialogResult.Cancel;

        Controls.Add(_отмена);
        Controls.Add(_продолжить);
        Controls.Add(_предупреждение);
        Controls.Add(группаАрхива);
        Controls.Add(группаБазы);

        AcceptButton = _продолжить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Обновляет предупреждение о настройке архивирования.
    /// </summary>
    private void ОбновитьПредупреждение()
    {
        _предупреждение.Text = _включитьАрхивирование.Checked
            ? "Архивирование выполняется автоматически; папка должна быть доступна постоянно."
            : "Архивирование не настроено: резервную копию базы следует создавать вручную.";
    }

    /// <summary>
    /// Выбирает папку хранения архивов.
    /// </summary>
    private void ВыбратьПапку()
    {
        using var диалог = new FolderBrowserDialog
        {
            Description = "Папка хранения архивов базы данных",
            UseDescriptionForTitle = true,
        };

        if (Directory.Exists(_папкаАрхивов.Text))
        {
            диалог.SelectedPath = _папкаАрхивов.Text;
        }

        if (диалог.ShowDialog(this) == DialogResult.OK)
        {
            _папкаАрхивов.Text = диалог.SelectedPath;
        }
    }
}
