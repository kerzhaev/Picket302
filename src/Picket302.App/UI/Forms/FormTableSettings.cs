using System.Drawing;
using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Редактор состава и свойств таблицы адресной книги.
/// Структура намеренно близка к legacy-поверхности «Список доступных
/// столбцов»: список видимых столбцов, редактирование названия/ширины,
/// порядок, свойства таблицы и сброс к умолчаниям.
/// </summary>
public sealed class FormTableSettings : Form
{
    private sealed class ColumnDraft
    {
        public required string Name { get; init; }
        public required string DefaultCaption { get; init; }
        public required int DefaultWidth { get; init; }
        public string Caption { get; set; } = string.Empty;
        public int Width { get; set; }
        public bool Visible { get; set; }
        public Color? HeaderColor { get; set; }

        public override string ToString() => Caption;
    }

    private readonly DataGridView _таблица;
    private readonly List<ColumnDraft> _черновики;
    private readonly TabControl _вкладки = new() { Dock = DockStyle.Fill };
    private readonly Panel _столбцы = new();
    private readonly List<CheckBox> _флажкиСтолбцов = new();
    private int _выбранныйИндекс = -1;
    private readonly TextBox _название = new();
    private readonly NumericUpDown _ширина = new() { Minimum = 24, Maximum = 1200 };
    private readonly NumericUpDown _кегль = new() { Minimum = 6, Maximum = 14, DecimalPlaces = 1 };
    private readonly NumericUpDown _высотаСтроки = new() { Minimum = 12, Maximum = 48, DecimalPlaces = 0 };
    private readonly Button _цветСтолбца = new();
    private readonly CheckBox _плоскийВид = new();
    private readonly CheckBox _нумерацияСтрок = new();
    private readonly CheckBox _индикаторСтроки = new();
    private readonly CheckBox _сеткаТаблицы = new();
    private readonly CheckBox _перемещениеСтолбцов = new();
    private readonly CheckBox _изменениеШирины = new();
    private readonly CheckBox _зебра = new();
    private readonly RadioButton _сбросТаблицы = new() { Text = "Сбросить настройки таблицы" };
    private readonly RadioButton _сбросГлавнойФормы = new() { Text = "Сбросить настройки главной формы" };
    private readonly Label _предупреждение = new();
    private readonly Button _сохранить = new();
    private readonly Button _отмена = new();
    private readonly Button _сброс = new();

    private readonly float _исходныйКегль;
    private readonly int _исходнаяВысотаСтроки;
    private readonly bool _исходныйПлоскийВид;
    private readonly bool _исходнаяНумерацияСтрок;
    private readonly bool _исходныйИндикаторСтроки;
    private readonly bool _исходнаяСетка;
    private readonly bool _исходноеПеремещение;
    private readonly bool _исходноеИзменениеШирины;
    private readonly bool _исходнаяЗебра;

    /// <summary>
    /// Создаёт редактор настроек конкретной таблицы.
    /// </summary>
    /// <param name="таблица">Таблица, к которой будут применены изменения.</param>
    /// <param name="начальнаяВкладка">0 — столбцы, 1 — свойства, 2 — умолчания.</param>
    public FormTableSettings(DataGridView таблица, int начальнаяВкладка = 0)
    {
        ArgumentNullException.ThrowIfNull(таблица);

        _таблица = таблица;
        _черновики = таблица.Columns
            .Cast<DataGridViewColumn>()
            .Select(столбец => new ColumnDraft
            {
                Name = столбец.Name,
                DefaultCaption = столбец.HeaderText,
                DefaultWidth = столбец.Width,
                Caption = столбец.HeaderText,
                Width = столбец.Width,
                Visible = столбец.Visible,
                HeaderColor = столбец.HeaderCell.Style.BackColor.IsEmpty
                    ? null
                    : столбец.HeaderCell.Style.BackColor,
            })
            .ToList();

        _исходныйКегль = таблица.Font.Size;
        _исходнаяВысотаСтроки = таблица.RowTemplate.Height;
        _исходныйПлоскийВид = таблица.CellBorderStyle == DataGridViewCellBorderStyle.None;
        _исходнаяНумерацияСтрок = таблица.RowHeadersVisible;
        _исходныйИндикаторСтроки = таблица.RowHeadersVisible;
        _исходнаяСетка = таблица.CellBorderStyle != DataGridViewCellBorderStyle.None;
        _исходноеПеремещение = таблица.AllowUserToOrderColumns;
        _исходноеИзменениеШирины = таблица.AllowUserToResizeColumns;
        _исходнаяЗебра = !таблица.AlternatingRowsDefaultCellStyle.BackColor.IsEmpty;

        // Легаси-референс «Настройки свойств таблиц» (ревью S04 Q4-3):
        // прежний заголовок «Список доступных столбцов» описывал только
        // первую вкладку и расходился с референсом KonvertProNet.
        Text = "Настройки свойств таблиц";
        AccessibleName = "Настройка таблицы адресной книги";
        AccessibleDescription = "Настройка состава, названий, ширины и свойств таблицы получателей.";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(430, 448);

        ПостроитьИнтерфейс();
        ЗаполнитьСписок();
        ЗаполнитьСвойства();
        _вкладки.SelectedIndex = Math.Clamp(начальнаяВкладка, 0, _вкладки.TabPages.Count - 1);
        ВыбратьСтолбец(_выбранныйИндекс);
        ОбновитьПредупреждение();
        Shown += (_, _) => НачатьОтрисовкуВыбраннойВкладки();
    }

    /// <summary>Количество столбцов, отображённых в редакторе.</summary>
    public int КоличествоСтолбцов => _черновики.Count;

    /// <summary>Текущие подписи столбцов в порядке редактора.</summary>
    public IReadOnlyList<string> ПодписиСтолбцов => _черновики.Select(столбец => столбец.Caption).ToArray();

    /// <summary>Применяет отредактированное состояние к исходной таблице.</summary>
    public void Применить()
    {
        ОбновитьВыбранныйСтолбец();

        foreach (var черновик in _черновики)
        {
            var столбец = _таблица.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(кандидат => string.Equals(кандидат.Name, черновик.Name, StringComparison.Ordinal));

            if (столбец is null)
            {
                continue;
            }

            столбец.Visible = черновик.Visible;
            столбец.HeaderText = черновик.Caption;
            столбец.Width = Math.Clamp(черновик.Width, 24, 1200);

            if (черновик.HeaderColor is { } цвет)
            {
                столбец.HeaderCell.Style.BackColor = цвет;
            }
        }

        _таблица.RowTemplate.Height = (int)_высотаСтроки.Value;
        _таблица.Font = new Font(_таблица.Font.FontFamily, (float)_кегль.Value, _таблица.Font.Style);
        _таблица.RowHeadersVisible = _нумерацияСтрок.Checked || _индикаторСтроки.Checked;
        _таблица.AllowUserToOrderColumns = _перемещениеСтолбцов.Checked;
        _таблица.AllowUserToResizeColumns = _изменениеШирины.Checked;
        _таблица.CellBorderStyle = _плоскийВид.Checked
            ? DataGridViewCellBorderStyle.None
            : (_сеткаТаблицы.Checked ? DataGridViewCellBorderStyle.Single : DataGridViewCellBorderStyle.None);
        _таблица.AlternatingRowsDefaultCellStyle.BackColor = _зебра.Checked
            ? Color.FromArgb(248, 249, 250)
            : Color.Empty;
    }

    /// <summary>Сбрасывает редактор к состоянию, с которым он был открыт.</summary>
    public void СброситьКУмолчаниям()
    {
        foreach (var столбец in _черновики)
        {
            столбец.Caption = столбец.DefaultCaption;
            столбец.Width = столбец.DefaultWidth;
            столбец.Visible = true;
            столбец.HeaderColor = null;
        }

        _кегль.Value = (decimal)_исходныйКегль;
        _высотаСтроки.Value = _исходнаяВысотаСтроки;
        _плоскийВид.Checked = _исходныйПлоскийВид;
        _нумерацияСтрок.Checked = _исходнаяНумерацияСтрок;
        _индикаторСтроки.Checked = _исходныйИндикаторСтроки;
        _сеткаТаблицы.Checked = _исходнаяСетка;
        _перемещениеСтолбцов.Checked = _исходноеПеремещение;
        _изменениеШирины.Checked = _исходноеИзменениеШирины;
        _зебра.Checked = _исходнаяЗебра;
        ЗаполнитьСписок();
        ВыбратьСтолбец(_выбранныйИндекс);
        _предупреждение.Text = string.Empty;
    }

    private void ПостроитьИнтерфейс()
    {
        _вкладки.TabPages.Add(new TabPage("Цвета, названия") { Controls = { ПостроитьВкладкуСтолбцов() } });
        _вкладки.TabPages.Add(new TabPage("Другие свойства") { Controls = { ПостроитьВкладкуСвойств() } });
        _вкладки.TabPages.Add(new TabPage("Умолчания") { Controls = { ПостроитьВкладкуУмолчаний() } });

        var корень = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            RowCount = 2,
            ColumnCount = 1,
        };
        корень.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        корень.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        корень.Controls.Add(_вкладки, 0, 0);

        var кнопки = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
        };
        кнопки.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        кнопки.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        // D-S06-1 (ревью S04 Q4-2): фиксированные 96 px обрезали метку
        // «По умолчанию» до «По» (кадр 07b, S03). Ширина вычисляется по
        // фактическому тексту с запасом на хром кнопки и ячейки.
        кнопки.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ШиринаКнопки("По умолчанию")));
        кнопки.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ШиринаКнопки("ОК")));
        кнопки.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ШиринаКнопки("Отмена")));

        var справка = new Button
        {
            Text = "?",
            Dock = DockStyle.Fill,
            AccessibleName = "Справка по настройке таблицы",
            AccessibleDescription = "Показать описание настроек таблицы.",
        };
        справка.Click += (_, _) => MessageBox.Show(
            this,
            "Отметьте столбцы, которые должны быть видимы. Название и ширина сохраняются вместе с видом таблицы.",
            "Настройка таблицы",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        _сброс.Text = "По умолчанию";
        _сброс.Dock = DockStyle.Fill;
        _сброс.AccessibleName = "Восстановить настройки таблицы по умолчанию";
        _сброс.Click += (_, _) => СброситьКУмолчаниям();

        _сохранить.Text = "ОК";
        _сохранить.Dock = DockStyle.Fill;
        _сохранить.DialogResult = DialogResult.OK;
        _сохранить.AccessibleName = "Сохранить настройки таблицы";

        _отмена.Text = "Отмена";
        _отмена.Dock = DockStyle.Fill;
        _отмена.DialogResult = DialogResult.Cancel;
        _отмена.AccessibleName = "Закрыть настройку таблицы без сохранения";

        кнопки.Controls.Add(справка, 1, 0);
        кнопки.Controls.Add(_сброс, 2, 0);
        кнопки.Controls.Add(_сохранить, 3, 0);
        кнопки.Controls.Add(_отмена, 4, 0);
        корень.Controls.Add(кнопки, 0, 1);

        Controls.Add(корень);
        AcceptButton = _сохранить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Ширина колонки кнопки, при которой метка гарантированно не обрезается:
    /// измеренный текст + запас на рамку/внутренние отступы кнопки и ячейки
    /// таблицы (D-S06-1). Не уже прежних фиксированных ширин.
    /// </summary>
    private int ШиринаКнопки(string текст) =>
        Math.Max(86, TextRenderer.MeasureText(текст, Font).Width + 24);

    private Control ПостроитьВкладкуСтолбцов()
    {
        var корень = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            RowCount = 3,
            ColumnCount = 1,
        };
        корень.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        корень.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        корень.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var группа = new Panel
        {
            Text = "Столбцы таблицы: (редактировать — Enter)",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 28, 8, 8),
            BorderStyle = BorderStyle.FixedSingle,
        };
        var заголовокГруппы = new Label
        {
            Text = "Столбцы таблицы: (редактировать — Enter)",
            AutoSize = true,
            Location = new Point(10, 4),
            BackColor = SystemColors.Control,
            AccessibleName = "Заголовок списка столбцов",
        };
        var содержимое = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        содержимое.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        содержимое.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));

        _столбцы.Dock = DockStyle.Fill;
        _столбцы.AutoScroll = true;
        _столбцы.BorderStyle = BorderStyle.Fixed3D;
        _столбцы.BackColor = SystemColors.Window;
        _столбцы.ForeColor = SystemColors.WindowText;
        _столбцы.AccessibleName = "Доступные столбцы таблицы";
        _столбцы.AccessibleDescription = "Отметьте столбцы, которые должны быть видимы; порядок можно изменить кнопками справа.";
        _столбцы.Paint += (_, событие) => НарисоватьСписокСтолбцов(событие.Graphics);

        var справа = new Panel
        {
            Dock = DockStyle.Top,
            Height = 128,
            TabStop = true,
            AccessibleName = "Команды списка столбцов",
            AccessibleDescription = "Кнопки порядка, показа всех столбцов и восстановления названий.",
        };
        справа.Paint += (_, событие) => НарисоватьКомандыСтолбцов(событие.Graphics);

        var вверх = КнопкаПорядка("▲", "Переместить столбец вверх", -1);
        вверх.Visible = false;
        справа.Controls.Add(вверх);

        var вниз = КнопкаПорядка("▼", "Переместить столбец вниз", 1);
        вниз.Visible = false;
        справа.Controls.Add(вниз);

        var показатьВсе = new Button
        {
            Text = string.Empty,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            AccessibleName = "Показать все столбцы",
            AccessibleDescription = "Сделать все столбцы видимыми.",
        };
        показатьВсе.FlatAppearance.BorderSize = 1;
        ДобавитьИконку(показатьВсе, "✓");
        показатьВсе.Visible = false;
        показатьВсе.Click += (_, _) =>
        {
            for (var индекс = 0; индекс < _черновики.Count; индекс++)
            {
                _черновики[индекс].Visible = true;
            }

            ЗаполнитьСписок(_выбранныйИндекс);
        };
        справа.Controls.Add(показатьВсе);

        var названия = new Button
        {
            Text = string.Empty,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            AccessibleName = "Восстановить названия столбцов",
            AccessibleDescription = "Вернуть исходные подписи всех столбцов.",
        };
        названия.FlatAppearance.BorderSize = 1;
        ДобавитьИконку(названия, "↺");
        названия.Visible = false;
        названия.Click += (_, _) =>
        {
            foreach (var столбец in _черновики)
            {
                столбец.Caption = столбец.DefaultCaption;
            }

            ЗаполнитьСписок(_выбранныйИндекс);
        };
        справа.Controls.Add(названия);
        справа.MouseClick += (_, событие) =>
        {
            var индекс = Math.Clamp(событие.Y / 30, 0, 3);
            switch (индекс)
            {
                case 0: ПереместитьСтолбец(-1); break;
                case 1: ПереместитьСтолбец(1); break;
                case 2: показатьВсе.PerformClick(); break;
                case 3: названия.PerformClick(); break;
            }
        };

        содержимое.Controls.Add(_столбцы, 0, 0);
        содержимое.Controls.Add(справа, 1, 0);
        группа.Controls.Add(содержимое);
        группа.Controls.Add(заголовокГруппы);
        заголовокГруппы.BringToFront();
        корень.Controls.Add(группа, 0, 0);

        var редактор = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
        };
        редактор.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        редактор.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        редактор.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        редактор.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        редактор.Controls.Add(Метка("Название:", "Подпись названия столбца"), 0, 0);
        _название.Dock = DockStyle.Fill;
        _название.AccessibleName = "Название выбранного столбца";
        _название.KeyDown += (_, событие) =>
        {
            if (событие.KeyCode == Keys.Enter)
            {
                ОбновитьВыбранныйСтолбец();
                событие.Handled = true;
                событие.SuppressKeyPress = true;
            }
        };
        редактор.Controls.Add(_название, 1, 0);
        редактор.Controls.Add(Метка("Ширина:", "Подпись ширины столбца"), 2, 0);
        _ширина.Dock = DockStyle.Fill;
        _ширина.AccessibleName = "Ширина выбранного столбца";
        _ширина.ValueChanged += (_, _) => ОбновитьВыбранныйСтолбец();
        редактор.Controls.Add(_ширина, 3, 0);
        корень.Controls.Add(редактор, 0, 1);

        var цветоваяСтрока = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
        };
        цветоваяСтрока.Controls.Add(Метка("Цвет столбца:", "Подпись цвета столбца"));
        _цветСтолбца.Text = "Выбрать";
        _цветСтолбца.AutoSize = true;
        _цветСтолбца.AccessibleName = "Выбрать цвет заголовка столбца";
        _цветСтолбца.Click += (_, _) => ВыбратьЦветСтолбца();
        цветоваяСтрока.Controls.Add(_цветСтолбца);
        var цветПоУмолчанию = new Button
        {
            Text = "По умолчанию",
            AutoSize = true,
            AccessibleName = "Сбросить цвет столбца",
        };
        цветПоУмолчанию.Click += (_, _) =>
        {
            if (_выбранныйИндекс >= 0)
            {
                _черновики[_выбранныйИндекс].HeaderColor = null;
                _цветСтолбца.BackColor = SystemColors.Control;
            }
        };
        цветоваяСтрока.Controls.Add(цветПоУмолчанию);
        корень.Controls.Add(цветоваяСтрока, 0, 2);

        return корень;
    }

    private Control ПостроитьВкладкуСвойств()
    {
        var корень = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 10,
        };
        корень.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        корень.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        for (var индекс = 0; индекс < 10; индекс++)
        {
            корень.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        }

        ДобавитьЧисловуюСтроку(корень, 0, "Высота строки:", _высотаСтроки, 12, 48, 1);
        ДобавитьЧисловуюСтроку(корень, 1, "Шрифт таблицы:", _кегль, 6, 14, 1);
        ДобавитьФлажок(корень, 2, _плоскийВид, "Плоский вид таблицы");
        ДобавитьФлажок(корень, 3, _нумерацияСтрок, "Нумерация строк");
        ДобавитьФлажок(корень, 4, _индикаторСтроки, "Индикатор строки");
        ДобавитьФлажок(корень, 5, _сеткаТаблицы, "Сетка таблицы");
        ДобавитьФлажок(корень, 6, _перемещениеСтолбцов, "Перемещение столбцов таблицы");
        ДобавитьФлажок(корень, 7, _изменениеШирины, "Изменение ширины столбцов таблицы");
        ДобавитьФлажок(корень, 8, _зебра, "Полосатая таблица");

        var пояснение = new Label
        {
            Text = "Свойства применяются к выбранной таблице и сохраняются вместе с её видом.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        };
        корень.Controls.Add(пояснение, 0, 9);
        корень.SetColumnSpan(пояснение, 2);
        return корень;
    }

    private Control ПостроитьВкладкуУмолчаний()
    {
        var панель = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _сбросТаблицы.AutoSize = true;
        _сбросТаблицы.Location = new Point(8, 8);
        _сбросГлавнойФормы.AutoSize = true;
        _сбросГлавнойФормы.Location = new Point(8, 38);
        _сбросГлавнойФормы.CheckedChanged += (_, _) => ОбновитьПредупреждение();

        _предупреждение.AutoSize = false;
        _предупреждение.Location = new Point(8, 76);
        _предупреждение.Size = new Size(370, 54);
        _предупреждение.ForeColor = Color.FromArgb(160, 0, 0);

        панель.Controls.Add(_сбросТаблицы);
        панель.Controls.Add(_сбросГлавнойФормы);
        панель.Controls.Add(_предупреждение);
        return панель;
    }

    private void ЗаполнитьСписок(int? выбрать = null)
    {
        var индексДляВыбора = выбрать ?? _выбранныйИндекс;
        _столбцы.SuspendLayout();
        try
        {
            foreach (var флажок in _флажкиСтолбцов)
            {
                флажок.Dispose();
            }

            _флажкиСтолбцов.Clear();
            _столбцы.Controls.Clear();

            for (var индекс = 0; индекс < _черновики.Count; индекс++)
            {
                var индексСтолбца = индекс;
                var столбец = _черновики[индекс];
                var флажок = new CheckBox
                {
                    Text = string.Empty,
                    Checked = столбец.Visible,
                    AutoSize = false,
                    Height = 22,
                    Width = 300,
                    Location = new Point(2, 2 + индекс * 24),
                    Margin = new Padding(2, 1, 2, 1),
                    BackColor = Color.Transparent,
                    ForeColor = Color.Transparent,
                    FlatStyle = FlatStyle.Flat,
                    AccessibleName = $"Столбец: {столбец.Caption}",
                    AccessibleDescription = "Отметка определяет видимость столбца; щелчок выбирает его для редактирования.",
                };
                флажок.FlatAppearance.BorderSize = 0;
                флажок.CheckedChanged += (_, _) =>
                {
                    if (индексСтолбца < _черновики.Count)
                    {
                        _черновики[индексСтолбца].Visible = флажок.Checked;
                        _столбцы.Invalidate();
                    }
                };
                флажок.Click += (_, _) =>
                {
                    ВыбратьСтолбец(индексСтолбца);
                    _столбцы.Invalidate();
                };
                флажок.KeyDown += (_, событие) =>
                {
                    if (событие.KeyCode == Keys.Enter)
                    {
                        ВыбратьСтолбец(индексСтолбца);
                        _название.Focus();
                        событие.Handled = true;
                        событие.SuppressKeyPress = true;
                    }
                };

                _флажкиСтолбцов.Add(флажок);
                _столбцы.Controls.Add(флажок);
            }

            _столбцы.AutoScrollMinSize = new Size(0, Math.Max(0, _черновики.Count * 24 + 4));
        }
        finally
        {
            _столбцы.ResumeLayout(true);
        }

        if (_флажкиСтолбцов.Count > 0)
        {
            ВыбратьСтолбец(Math.Clamp(индексДляВыбора < 0 ? 0 : индексДляВыбора, 0, _флажкиСтолбцов.Count - 1));
        }
        else
        {
            ВыбратьСтолбец(-1);
        }
    }

    private void НарисоватьСписокСтолбцов(Graphics graphics)
    {
        var ширинаСтроки = Math.Max(1, _столбцы.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 5);

        for (var индекс = 0; индекс < _черновики.Count; индекс++)
        {
            var строка = new Rectangle(2, 2 + индекс * 24, ширинаСтроки, 22);
            if (индекс == _выбранныйИндекс)
            {
                using var выделение = new SolidBrush(Color.FromArgb(225, 236, 250));
                graphics.FillRectangle(выделение, строка);
            }

            var квадрат = new Rectangle(6, строка.Top + 3, 13, 13);
            ControlPaint.DrawCheckBox(
                graphics,
                квадрат,
                _черновики[индекс].Visible ? ButtonState.Checked : ButtonState.Normal);

            TextRenderer.DrawText(
                graphics,
                _черновики[индекс].Caption,
                Font,
                new Rectangle(25, строка.Top, Math.Max(1, строка.Width - 27), строка.Height),
                SystemColors.WindowText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private void ЗаполнитьСвойства()
    {
        _кегль.Value = Math.Clamp((decimal)_исходныйКегль, _кегль.Minimum, _кегль.Maximum);
        _высотаСтроки.Value = Math.Clamp(_исходнаяВысотаСтроки, (int)_высотаСтроки.Minimum, (int)_высотаСтроки.Maximum);
        _плоскийВид.Checked = _исходныйПлоскийВид;
        _нумерацияСтрок.Checked = _исходнаяНумерацияСтрок;
        _индикаторСтроки.Checked = _исходныйИндикаторСтроки;
        _сеткаТаблицы.Checked = _исходнаяСетка;
        _перемещениеСтолбцов.Checked = _исходноеПеремещение;
        _изменениеШирины.Checked = _исходноеИзменениеШирины;
        _зебра.Checked = _исходнаяЗебра;
    }

    private void ВыбратьСтолбец(int индекс)
    {
        if (_выбранныйИндекс >= 0 && _выбранныйИндекс < _флажкиСтолбцов.Count)
        {
            _флажкиСтолбцов[_выбранныйИндекс].BackColor = SystemColors.Window;
        }

        _выбранныйИндекс = индекс;

        if (индекс < 0 || индекс >= _черновики.Count)
        {
            _название.Text = string.Empty;
            _ширина.Value = _ширина.Minimum;
            return;
        }

        _флажкиСтолбцов[индекс].BackColor = Color.FromArgb(225, 236, 250);
        var столбец = _черновики[индекс];
        _название.Text = столбец.Caption;
        _ширина.Value = Math.Clamp(столбец.Width, (int)_ширина.Minimum, (int)_ширина.Maximum);
        _цветСтолбца.BackColor = столбец.HeaderColor ?? SystemColors.Control;
    }

    private void ОбновитьВыбранныйСтолбец()
    {
        if (_выбранныйИндекс < 0 || _выбранныйИндекс >= _черновики.Count)
        {
            return;
        }

        var столбец = _черновики[_выбранныйИндекс];
        if (!string.IsNullOrWhiteSpace(_название.Text))
        {
            столбец.Caption = _название.Text.Trim();
            _флажкиСтолбцов[_выбранныйИндекс].Text = столбец.Caption;
            _флажкиСтолбцов[_выбранныйИндекс].AccessibleName = $"Столбец: {столбец.Caption}";
        }

        столбец.Width = (int)_ширина.Value;
    }

    private void ПереместитьСтолбец(int смещение)
    {
        ОбновитьВыбранныйСтолбец();
        var исходный = _выбранныйИндекс;
        var целевой = исходный + смещение;
        if (исходный < 0 || целевой < 0 || целевой >= _черновики.Count)
        {
            return;
        }

        (_черновики[исходный], _черновики[целевой]) = (_черновики[целевой], _черновики[исходный]);
        ЗаполнитьСписок(целевой);
    }

    private void ВыбратьЦветСтолбца()
    {
        if (_выбранныйИндекс < 0)
        {
            return;
        }

        using var диалог = new ColorDialog { FullOpen = true, AnyColor = true };
        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _черновики[_выбранныйИндекс].HeaderColor = диалог.Color;
        _цветСтолбца.BackColor = диалог.Color;
    }

    private void ОбновитьПредупреждение()
    {
        _предупреждение.Text = _сбросГлавнойФормы.Checked
            ? "Сброс настройки главной формы вступит в силу после перезапуска программы."
            : string.Empty;
    }

    /// <summary>
    /// Принудительно создаёт HWND и инвалидирует содержимое выбранной вкладки.
    /// Это нужно для non-activating PrintWindow: отложенный TabPage иногда
    /// создаёт дочерние кнопки только после первого переключения вкладки.
    /// </summary>
    private void НачатьОтрисовкуВыбраннойВкладки()
    {
        var целеваяВкладка = _вкладки.SelectedIndex;
        if (целеваяВкладка < 0)
        {
            return;
        }

        _вкладки.SelectedIndex = (целеваяВкладка + 1) % _вкладки.TabPages.Count;
        _вкладки.SelectedIndex = целеваяВкладка;
        _вкладки.SelectedTab?.CreateControl();
        _вкладки.SelectedTab?.PerformLayout();
        _вкладки.SelectedTab?.Invalidate(true);
        _вкладки.SelectedTab?.Update();
        Invalidate(true);
        Update();
    }

    private Button КнопкаПорядка(string текст, string имя, int смещение)
    {
        var кнопка = new Button
        {
            Text = string.Empty,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            AccessibleName = имя,
        };
        кнопка.FlatAppearance.BorderSize = 1;
        ДобавитьИконку(кнопка, текст);
        кнопка.Click += (_, _) => ПереместитьСтолбец(смещение);
        return кнопка;
    }

    private static void ДобавитьИконку(Button кнопка, string символ)
    {
        кнопка.Paint += (_, событие) =>
        {
            using var шрифт = new Font("Segoe UI Symbol", 10f, FontStyle.Regular, GraphicsUnit.Point);
            TextRenderer.DrawText(
                событие.Graphics,
                символ,
                шрифт,
                кнопка.ClientRectangle,
                SystemColors.ControlText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
    }

    private void НарисоватьКомандыСтолбцов(Graphics graphics)
    {
        var символы = new[] { "▲", "▼", "✓", "↺" };
        var ширина = Math.Max(1, (int)Math.Round(graphics.VisibleClipBounds.Width));
        for (var индекс = 0; индекс < символы.Length; индекс++)
        {
            var область = new Rectangle(0, индекс * 30, ширина, 26);
            ControlPaint.DrawButton(graphics, область, ButtonState.Normal);
            using var шрифт = new Font("Segoe UI Symbol", 10f, FontStyle.Regular, GraphicsUnit.Point);
            TextRenderer.DrawText(
                graphics,
                символы[индекс],
                шрифт,
                область,
                SystemColors.ControlText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private static Label Метка(string текст, string имя) => new()
    {
        Text = текст,
        Dock = DockStyle.Fill,
        TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
        AutoSize = false,
        AccessibleName = имя,
    };

    private static void ДобавитьФлажок(TableLayoutPanel панель, int строка, CheckBox флажок, string текст)
    {
        флажок.Text = текст;
        флажок.AutoSize = true;
        флажок.Dock = DockStyle.Fill;
        флажок.AccessibleName = текст;
        панель.Controls.Add(флажок, 0, строка);
        панель.SetColumnSpan(флажок, 2);
    }

    private static void ДобавитьЧисловуюСтроку(
        TableLayoutPanel панель,
        int строка,
        string текст,
        NumericUpDown поле,
        decimal минимум,
        decimal максимум,
        int десятичныеЗнаки)
    {
        поле.Minimum = минимум;
        поле.Maximum = максимум;
        поле.DecimalPlaces = десятичныеЗнаки;
        поле.Dock = DockStyle.Fill;
        поле.AccessibleName = текст.TrimEnd(':');
        панель.Controls.Add(Метка(текст, текст), 0, строка);
        панель.Controls.Add(поле, 1, строка);
    }
}
