using System.Drawing;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Windows.Forms;
using Picket302.Domain.Contracts;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Экран «Очистка базы»: выборочная очистка по четырём областям данных
/// (ТЗ §8.11, функция 137; §8.12, функция 153: «Демо-база и очистка базы»).
/// </summary>
/// <remarks>
/// Очистка требует подтверждения: оператору показывается точное число
/// удаляемых записей по каждой области.
/// </remarks>
public sealed class FormClearDatabase : Form
{
    private readonly AppServices _состав;

    private readonly CheckBox _категории = new();
    private readonly CheckBox _рассылки = new();
    private readonly CheckBox _контрагенты = new();
    private readonly CheckBox _отправленныеПисьма = new();
    private readonly Button _очистить = new();
    private readonly Button _отмена = new();
    private readonly Label _сводка = new();

    private IReadOnlyDictionary<ClearScope, int> _численность = new Dictionary<ClearScope, int>();

    /// <summary>
    /// Создаёт экран очистки базы.
    /// </summary>
    /// <param name="состав">Состав приложения.</param>
    public FormClearDatabase(AppServices состав)
    {
        ArgumentNullException.ThrowIfNull(состав);
        _состав = состав;

        Text = "Очистка базы";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 260);

        ПостроитьИнтерфейс();
        ОбновитьЧисленностьAsync();
    }

    /// <summary>
    /// Области, очищенные по результату работы формы.
    /// </summary>
    public IReadOnlyList<ClearScope> ОчищенныеОбласти { get; private set; } = [];

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var панель = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 150,
            ColumnCount = 2,
        };

        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        НастроитьФлажок(_категории, "Категории контрагентов");
        НастроитьФлажок(_рассылки, "База рассылок");
        НастроитьФлажок(_контрагенты, "База контрагентов");
        НастроитьФлажок(_отправленныеПисьма, "База отправленных писем");

        панель.Controls.Add(_категории, 0, 0);
        панель.Controls.Add(_численностьLabel(ClearScope.CounterpartyCategories), 1, 0);
        панель.Controls.Add(_рассылки, 0, 1);
        панель.Controls.Add(_численностьLabel(ClearScope.Distributions), 1, 1);
        панель.Controls.Add(_контрагенты, 0, 2);
        панель.Controls.Add(_численностьLabel(ClearScope.Counterparties), 1, 2);
        панель.Controls.Add(_отправленныеПисьма, 0, 3);
        панель.Controls.Add(_численностьLabel(ClearScope.SentMails), 1, 3);

        _сводка.Dock = DockStyle.Top;
        _сводка.Height = 40;
        _сводка.Text = "Выберите области очистки. Очистка необратима.";
        _сводка.TextAlign = ContentAlignment.MiddleLeft;

        _очистить.Text = "Очистить";
        _очистить.Size = new Size(90, 24);
        _очистить.Location = new Point(258, 212);
        _очистить.Click += (_, _) => ОчиститьAsync();

        _отмена.Text = "Закрыть";
        _отмена.Size = new Size(90, 24);
        _отмена.Location = new Point(356, 212);
        _отмена.DialogResult = DialogResult.Cancel;

        Controls.Add(_очистить);
        Controls.Add(_отмена);
        Controls.Add(_сводка);
        Controls.Add(панель);

        CancelButton = _отмена;
    }

    /// <summary>
    /// Настраивает флажок области очистки.
    /// </summary>
    private static void НастроитьФлажок(CheckBox флажок, string текст)
    {
        флажок.Text = текст;
        флажок.AutoSize = true;
        флажок.Anchor = AnchorStyles.Left;
    }

    /// <summary>
    /// Создаёт метку с численностью области.
    /// </summary>
    private Label _численностьLabel(ClearScope область)
    {
        var метка = new Label
        {
            Tag = область,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };

        return метка;
    }

    /// <summary>
    /// Обновляет численность записей по областям.
    /// </summary>
    private async void ОбновитьЧисленностьAsync()
    {
        try
        {
            _численность = await _состав.СлужбаСидов.GetScopeCountsAsync().ConfigureAwait(true);
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                $"Не удалось получить сведения о базе: {исключение.Message}",
                "Очистка базы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        foreach (Control элемент in Controls)
        {
            ОбновитьМетки(элемент);
        }
    }

    /// <summary>
    /// Обновляет значения меток численности во вложенных контейнерах.
    /// </summary>
    private void ОбновитьМетки(Control контейнер)
    {
        foreach (Control элемент in контейнер.Controls)
        {
            if (элемент is Label метка && метка.Tag is ClearScope область)
            {
                _численность.TryGetValue(область, out var число);
                метка.Text = число.ToString(CultureInfo.InvariantCulture);
            }
            else if (элемент is not Button)
            {
                ОбновитьМетки(элемент);
            }
        }
    }

    /// <summary>
    /// Очищает выбранные области с подтверждением.
    /// </summary>
    private async void ОчиститьAsync()
    {
        var области = new List<ClearScope>();

        if (_категории.Checked)
        {
            области.Add(ClearScope.CounterpartyCategories);
        }

        if (_рассылки.Checked)
        {
            области.Add(ClearScope.Distributions);
        }

        if (_контрагенты.Checked)
        {
            области.Add(ClearScope.Counterparties);
        }

        if (_отправленныеПисьма.Checked)
        {
            области.Add(ClearScope.SentMails);
        }

        if (области.Count == 0)
        {
            MessageBox.Show(
                this,
                "Не выбрана ни одна область очистки.",
                "Очистка базы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var сводка = new System.Text.StringBuilder();
        сводка.AppendLine("Будет удалено:");

        foreach (var область in области)
        {
            _численность.TryGetValue(область, out var число);
            сводка.AppendLine(CultureInfo.InvariantCulture, $"— {НазваниеОбласти(область)}: {число}");
        }

        сводка.AppendLine();
        сводка.Append("Операция необратима. Продолжить?");

        var подтверждение = MessageBox.Show(
            this,
            сводка.ToString(),
            "Очистка базы",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (подтверждение != DialogResult.Yes)
        {
            return;
        }

        var очищено = new List<ClearScope>();

        try
        {
            foreach (var область in области)
            {
                var результат = await _состав.СлужбаСидов.ClearDatabaseAsync(область).ConfigureAwait(true);

                if (результат.IsFailure)
                {
                    MessageBox.Show(
                        this,
                        $"Очистка области «{НазваниеОбласти(область)}» не выполнена: {результат.Error}",
                        "Очистка базы",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    очищено.Add(область);
                }
            }
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                $"Не удалось очистить базу: {исключение.Message}",
                "Очистка базы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        ОчищенныеОбласти = очищено;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Возвращает отображаемое наименование области очистки.
    /// </summary>
    /// <param name="область">Область очистки.</param>
    /// <returns>Наименование области.</returns>
    public static string НазваниеОбласти(ClearScope область) => область switch
    {
        ClearScope.CounterpartyCategories => "Категории контрагентов",
        ClearScope.Distributions => "База рассылок",
        ClearScope.Counterparties => "База контрагентов",
        ClearScope.SentMails => "База отправленных писем",
        _ => область.ToString(),
    };
}
