using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Domain.Contracts;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Настройка оформления таблиц: фокус, выделение, подсказка и «зебра»
/// (ТЗ §7.10: «Три независимых настраиваемых цвета»; §8.11, функция 131:
/// «Настройка вида таблиц»).
/// </summary>
public sealed class FormGridAppearance : Form
{
    private readonly Button _цветФокуса = new();
    private readonly Button _цветВыделения = new();
    private readonly Button _цветПодсказки = new();
    private readonly CheckBox _зебра = new();
    private readonly Button _сохранить = new();
    private readonly Button _отмена = new();
    private readonly Button _сброс = new();

    private int _фокус;
    private int _выделение;
    private int _подсказка;

    /// <summary>
    /// Создаёт диалог настройки оформления таблиц.
    /// </summary>
    /// <param name="оформление">Исходное оформление.</param>
    public FormGridAppearance(GridAppearance оформление)
    {
        ArgumentNullException.ThrowIfNull(оформление);

        _фокус = оформление.FocusColor;
        _выделение = оформление.SelectedColor;
        _подсказка = оформление.HintColor;

        Text = "Настройка вида таблиц";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 210);

        ПостроитьИнтерфейс();
        ОбновитьКнопки();
    }

    /// <summary>
    /// Настроенное оформление таблиц.
    /// </summary>
    public GridAppearance Оформление => new()
    {
        FocusColor = _фокус,
        SelectedColor = _выделение,
        HintColor = _подсказка,
        ZebraEnabled = _зебра.Checked,
    };

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var панель = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 120,
            ColumnCount = 3,
        };

        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

        ДобавитьСтроку(панель, 0, "Фокус:", _цветФокуса);
        ДобавитьСтроку(панель, 1, "Выделение:", _цветВыделения);
        ДобавитьСтроку(панель, 2, "Подсказка:", _цветПодсказки);

        _зебра.Text = "«Зебра» (чередование строк)";
        _зебра.AutoSize = true;
        _зебра.Location = new Point(10, 128);

        _сохранить.Text = "Сохранить";
        _сохранить.Size = new Size(90, 24);
        _сохранить.Location = new Point(186, 166);
        _сохранить.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        _сброс.Text = "Сбросить";
        _сброс.Size = new Size(90, 24);
        _сброс.Location = new Point(10, 166);
        _сброс.Click += (_, _) =>
        {
            _фокус = GridAppearance.DefaultFocusColor;
            _выделение = GridAppearance.DefaultSelectedColor;
            _подсказка = GridAppearance.DefaultHintColor;

            ОбновитьКнопки();
        };

        _отмена.Text = "Отмена";
        _отмена.Size = new Size(90, 24);
        _отмена.Location = new Point(282, 166);
        _отмена.DialogResult = DialogResult.Cancel;

        Controls.Add(_сохранить);
        Controls.Add(_сброс);
        Controls.Add(_отмена);
        Controls.Add(_зебра);
        Controls.Add(панель);

        AcceptButton = _сохранить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Добавляет строку выбора цвета в таблицу формы.
    /// </summary>
    private void ДобавитьСтроку(TableLayoutPanel таблица, int строка, string текст, Button кнопка)
    {
        var метка = new Label
        {
            Text = текст,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };

        var образец = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 20,
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.FixedSingle,
        };

        кнопка.Dock = DockStyle.Fill;
        кнопка.FlatStyle = FlatStyle.Flat;
        кнопка.Click += (_, _) => ВыбратьЦвет(кнопка);

        таблица.Controls.Add(метка, 0, строка);
        таблица.Controls.Add(образец, 1, строка);
        таблица.Controls.Add(кнопка, 2, строка);
    }

    /// <summary>
    /// Открывает диалог выбора цвета.
    /// </summary>
    private void ВыбратьЦвет(Button кнопка)
    {
        using var диалог = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var цвет = диалог.Color.ToArgb();

        switch (кнопка.Name)
        {
            case nameof(_цветФокуса):
                _фокус = цвет;
                break;
            case nameof(_цветВыделения):
                _выделение = цвет;
                break;
            default:
                _подсказка = цвет;
                break;
        }

        ОбновитьКнопки();
    }

    /// <summary>
    /// Обновляет цвета образцов в кнопках выбора.
    /// </summary>
    private void ОбновитьКнопки()
    {
        _цветФокуса.BackColor = GridAppearanceManager.Цвет(_фокус);
        _цветВыделения.BackColor = GridAppearanceManager.Цвет(_выделение);
        _цветПодсказки.BackColor = GridAppearanceManager.Цвет(_подсказка);
    }
}
