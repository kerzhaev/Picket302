using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Domain.Contracts;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Настройка оформления таблиц: три вкладки — цвета и заголовки столбцов;
/// атрибуты столбцов и строк с глобальной «Полосатой таблицей»; сброс к
/// умолчаниям (ТЗ §7.6; §8.11, функция 131: «Настройка вида таблиц»).
/// </summary>
/// <remarks>
/// Сброс главной формы требует перезапуска программы — предупреждение
/// выводится явно (ТЗ §8.11, функция 131).
/// </remarks>
public sealed class FormGridAppearance : Form
{
    private readonly TabControl _вкладки = new() { Dock = DockStyle.Fill };

    private readonly Button _цветФокуса = new();
    private readonly Button _цветВыделения = new();
    private readonly Button _цветПодсказки = new();
    private readonly NumericUpDown _кегль = new() { Minimum = 6, Maximum = 14, DecimalPlaces = 1 };
    private readonly NumericUpDown _высотаСтроки = new() { Minimum = 12, Maximum = 48 };
    private readonly CheckBox _показыватьЗаголовки = new();

    private readonly CheckBox _зебра = new();
    private readonly CheckBox _толькоЧтение = new();
    private readonly CheckBox _запретСортировки = new();
    private readonly CheckBox _запретИзмененияРазмера = new();

    private readonly RadioButton _сбросТаблицы = new() { Text = "Сбросить оформление таблицы" };
    private readonly RadioButton _сбросГлавнойФормы = new() { Text = "Сбросить оформление главной формы" };
    private readonly Label _предупреждение = new();

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
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 430);

        ПостроитьИнтерфейс();
        ЗаполнитьЗначения(оформление);
        ОбновитьКнопки();
    }

    /// <summary>
    /// Настроенное оформление.
    /// </summary>
    public GridAppearance Оформление => new()
    {
        FocusColor = _фокус,
        SelectedColor = _выделение,
        HintColor = _подсказка,
        ZebraEnabled = _зебра.Checked,
        HeadersVisible = _показыватьЗаголовки.Checked,
        FontSize = (float)_кегль.Value,
        RowHeight = (int)_высотаСтроки.Value,
    };

    /// <summary>
    /// Признак того, что выбран сброс главной формы: для него требуется
    /// перезапуск программы (ТЗ §8.11, функция 131).
    /// </summary>
    public bool RequiresRestart => _сбросГлавнойФормы.Checked;

    /// <summary>
    /// Текст предупреждения о необходимости перезапуска; пустая строка —
    /// перезапуск не требуется.
    /// </summary>
    public string RestartWarning =>
        RequiresRestart
            ? "Сброс оформления главной формы вступит в силу после перезапуска программы."
            : string.Empty;

    /// <summary>
    /// Выполняет сброс оформления к значениям по умолчанию.
    /// </summary>
    /// <returns>Результат сброса.</returns>
    public GridAppearanceManager.GridResetResult ResetToDefaults()
    {
        var результат = GridAppearanceManager.ResetToDefaults(
            _сбросГлавнойФормы.Checked
                ? GridAppearanceManager.GridAppearanceScope.MainForm
                : GridAppearanceManager.GridAppearanceScope.Grid);

        _фокус = результат.Appearance.FocusColor;
        _выделение = результат.Appearance.SelectedColor;
        _подсказка = результат.Appearance.HintColor;
        _зебра.Checked = результат.Appearance.ZebraEnabled;
        _показыватьЗаголовки.Checked = результат.Appearance.HeadersVisible;
        _кегль.Value = (decimal)результат.Appearance.FontSize;
        _высотаСтроки.Value = результат.Appearance.RowHeight;

        СброшеноКУмолчаниям = true;

        _предупреждение.Text = результат.Message;

        ОбновитьКнопки();

        return результат;
    }

    /// <summary>
    /// Признак того, что оператор выполнил сброс к умолчаниям: оболочка
    /// дополнительно сбрасывает состояние столбцов таблиц (ТЗ §8.11,
    /// функция 131: «сброс к умолчаниям»).
    /// </summary>
    public bool СброшеноКУмолчаниям { get; private set; }

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var вкладкаЦветов = new TabPage("Цвета и заголовки");
        var вкладкаАтрибутов = new TabPage("Атрибуты столбцов и строк");
        var вкладкаСброса = new TabPage("Сброс");

        вкладкаЦветов.Controls.Add(ПостроитьВкладкуЦветов());
        вкладкаАтрибутов.Controls.Add(ПостроитьВкладкуАтрибутов());
        вкладкаСброса.Controls.Add(ПостроитьВкладкуСброса());

        _вкладки.TabPages.Add(вкладкаЦветов);
        _вкладки.TabPages.Add(вкладкаАтрибутов);
        _вкладки.TabPages.Add(вкладкаСброса);

        _сохранить.Text = "Сохранить";
        _сохранить.Size = new Size(90, 24);
        _сохранить.Location = new Point(258, 396);
        _сохранить.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        _сброс.Text = "Сбросить";
        _сброс.Size = new Size(90, 24);
        _сброс.Location = new Point(10, 396);
        _сброс.Click += (_, _) => ResetToDefaults();

        _отмена.Text = "Отмена";
        _отмена.Size = new Size(90, 24);
        _отмена.Location = new Point(354, 396);
        _отмена.DialogResult = DialogResult.Cancel;

        Controls.Add(_вкладки);
        Controls.Add(_сохранить);
        Controls.Add(_сброс);
        Controls.Add(_отмена);

        AcceptButton = _сохранить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Строит вкладку «Цвета и заголовки столбцов».
    /// </summary>
    private Control ПостроитьВкладкуЦветов()
    {
        var панель = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 190,
            ColumnCount = 3,
        };

        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        ДобавитьСтроку(панель, 0, "Фокус:", _цветФокуса);
        ДобавитьСтроку(панель, 1, "Выделение:", _цветВыделения);
        ДобавитьСтроку(панель, 2, "Подсказка:", _цветПодсказки);

        var меткаКегля = new Label { Text = "Кегль шрифта, pt:", AutoSize = true, Anchor = AnchorStyles.Left };
        панель.Controls.Add(меткаКегля, 0, 3);
        панель.Controls.Add(_кегль, 1, 3);

        var меткаВысоты = new Label { Text = "Высота строки, px:", AutoSize = true, Anchor = AnchorStyles.Left };
        панель.Controls.Add(меткаВысоты, 0, 4);
        панель.Controls.Add(_высотаСтроки, 1, 4);

        _показыватьЗаголовки.Text = "Показывать заголовки столбцов";
        _показыватьЗаголовки.Checked = true;
        _показыватьЗаголовки.AutoSize = true;
        _показыватьЗаголовки.Location = new Point(12, 210);

        var пояснение = new Label
        {
            Text = "Три независимых цвета применяются ко всем таблицам программы единообразно.",
            // Текст длиннее доступной ширины вкладки. AutoSize оставлял
            // естественную ширину метки за пределами клиентской области и
            // визуально обрезал конец пояснения на реальном диалоге.
            AutoSize = false,
            Location = new Point(12, 240),
            Size = new Size(1, 38),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
        };

        var контейнер = new Panel { Dock = DockStyle.Fill };
        контейнер.Controls.Add(панель);
        контейнер.Controls.Add(_показыватьЗаголовки);
        контейнер.Controls.Add(пояснение);
        пояснение.Width = Math.Max(1, контейнер.ClientSize.Width - пояснение.Left - 12);

        return контейнер;
    }

    /// <summary>
    /// Строит вкладку «Атрибуты столбцов и строк».
    /// </summary>
    private Control ПостроитьВкладкуАтрибутов()
    {
        var панель = new Panel { Dock = DockStyle.Fill };

        _зебра.Text = "«Полосатая таблица» (глобально для всех таблиц)";
        _зебра.AutoSize = true;
        _зебра.Location = new Point(12, 12);

        _толькоЧтение.Text = "Столбцы только для чтения";
        _толькоЧтение.AutoSize = true;
        _толькоЧтение.Location = new Point(12, 44);

        _запретСортировки.Text = "Запретить сортировку по столбцам";
        _запретСортировки.AutoSize = true;
        _запретСортировки.Location = new Point(12, 72);

        _запретИзмененияРазмера.Text = "Запретить изменение размера столбцов";
        _запретИзмененияРазмера.AutoSize = true;
        _запретИзмененияРазмера.Location = new Point(12, 100);

        var пояснение = new Label
        {
            Text = "Атрибуты применяются к выбранной таблице; «Полосатая таблица» действует глобально.",
            AutoSize = true,
            Location = new Point(12, 132),
        };

        панель.Controls.Add(_зебра);
        панель.Controls.Add(_толькоЧтение);
        панель.Controls.Add(_запретСортировки);
        панель.Controls.Add(_запретИзмененияРазмера);
        панель.Controls.Add(пояснение);

        return панель;
    }

    /// <summary>
    /// Строит вкладку сброса оформления.
    /// </summary>
    private Control ПостроитьВкладкуСброса()
    {
        var панель = new Panel { Dock = DockStyle.Fill };

        _сбросТаблицы.Checked = true;
        _сбросТаблицы.Location = new Point(12, 12);
        _сбросТаблицы.AutoSize = true;

        _сбросГлавнойФормы.Location = new Point(12, 40);
        _сбросГлавнойФормы.AutoSize = true;

        _предупреждение.Text = string.Empty;
        _предупреждение.AutoSize = true;
        _предупреждение.ForeColor = Color.FromArgb(160, 0, 0);
        _предупреждение.Location = new Point(12, 76);
        _предупреждение.Size = new Size(400, 32);

        _сбросГлавнойФормы.CheckedChanged += (_, _) =>
        {
            _предупреждение.Text = _сбросГлавнойФормы.Checked
                ? "Сброс оформления главной формы вступит в силу после перезапуска программы."
                : string.Empty;
        };

        панель.Controls.Add(_сбросТаблицы);
        панель.Controls.Add(_сбросГлавнойФормы);
        панель.Controls.Add(_предупреждение);

        return панель;
    }

    /// <summary>
    /// Заполняет поля формы значениями оформления.
    /// </summary>
    private void ЗаполнитьЗначения(GridAppearance оформление)
    {
        _кегль.Value = (decimal)оформление.FontSize;
        _высотаСтроки.Value = оформление.RowHeight;
        _зебра.Checked = оформление.ZebraEnabled;
        _показыватьЗаголовки.Checked = оформление.HeadersVisible;
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

        // Сравнение по ссылке (F-E11): свойство Name у кнопок пусто, и прежнее
        // сравнение по имени всегда попадало в ветку по умолчанию.
        if (ReferenceEquals(кнопка, _цветФокуса))
        {
            _фокус = цвет;
        }
        else if (ReferenceEquals(кнопка, _цветВыделения))
        {
            _выделение = цвет;
        }
        else
        {
            _подсказка = цвет;
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
