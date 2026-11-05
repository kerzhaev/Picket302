using System.Drawing;
using System.Windows.Forms;
using Picket302.Domain.ValueObjects;

namespace Picket302.App.UI.Components;

/// <summary>
/// Редактор адреса: почтовый индекс, шесть строк и переключатель основного
/// адреса (ТЗ §7.1: «Индекс: [220050] [▼]», «Строка 1: …» … «Строка 6: …»;
/// §8.1, функция 4: «Шесть строк адреса»).
/// </summary>
/// <remarks>
/// Индекс редактируется в отдельном поле с кнопкой выбора из ранее
/// использовавшихся индексов. Шесть строк адреса нумеруются как «Строка 1» … «Строка 6».
/// </remarks>
public sealed class AddressEditor : UserControl
{
    private readonly TextBox[] _поляСтрок = new TextBox[Address.LineCount];
    private readonly TextBox _полеИндекса = new();
    private readonly Button _кнопкаИндекса = new();
    private readonly RadioButton _переключательОсновного = new();
    private readonly Label _заголовок = new();
    private readonly ComboBox _историяИндексов = new();
    private readonly ToolTip _подсказки = new();
    private readonly bool _показыватьЗаголовок;

    /// <summary>
    /// Подсказки к строкам адреса (аудит UX 2026-11-03, A3): подписи «Строка N»
    /// дополнены всплывающими пояснениями общепринятого смысла строк.
    /// </summary>
    private static readonly string[] ПодсказкиСтрок =
    [
        "Наименование организации или фамилия",
        "Подразделение, управление, отдел",
        "Улица и дом",
        "Корпус, строение, офис",
        "Город или населённый пункт",
        "Область, республика, край",
    ];

    /// <summary>
    /// Создаёт редактор адреса.
    /// </summary>
    /// <param name="заголовок">Заголовок блока адреса.</param>
    public AddressEditor(string заголовок, bool показыватьЗаголовок = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(заголовок);

        _показыватьЗаголовок = показыватьЗаголовок;
        Dock = DockStyle.Top;
        AutoSize = false;
        Height = 190;
        Padding = new Padding(4, 2, 4, 2);
        BackColor = Color.Transparent;

        _заголовок.Text = заголовок;
        _заголовок.AutoSize = true;
        _заголовок.Font = EnvelopeUiTheme.ШрифтЗаголовка(8.5f);
        _заголовок.ForeColor = EnvelopeUiTheme.MutedText;
        _заголовок.Location = new Point(4, 2);
        _заголовок.Visible = _показыватьЗаголовок;

        var сетка = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = Address.LineCount + 1,
            AutoSize = true,
            Padding = new Padding(0, _показыватьЗаголовок ? 16 : 2, 0, 0),
            BackColor = Color.Transparent,
        };

        // Длинная подпись «Индекс:» должна оставаться одной строкой и на
        // системном масштабе 100%, и на увеличенном шрифте Windows.
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        for (var строка = 0; строка <= Address.LineCount; строка++)
        {
            сетка.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        }

        var меткаИндекса = new Label
        {
            Text = "Индекс:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = EnvelopeUiTheme.MutedText,
        };

        сетка.Controls.Add(меткаИндекса, 0, 0);
        сетка.Controls.Add(_полеИндекса, 1, 0);
        сетка.Controls.Add(_кнопкаИндекса, 2, 0);

        _полеИндекса.Dock = DockStyle.Fill;
        _полеИндекса.Font = EnvelopeUiTheme.ШрифтТекста(8.5f);
        _полеИндекса.Margin = new Padding(0, 2, 2, 2);
        _полеИндекса.KeyDown += (_, аргументы) =>
        {
            if (!FieldValueAutoComplete.ТребуетсяПодбор(
                    _полеИндекса.Text.Length == 0,
                    аргументы.KeyCode))
            {
                return;
            }

            аргументы.Handled = true;
            аргументы.SuppressKeyPress = true;

            ТребуетсяПодборЗначения?.Invoke(this, EventArgs.Empty);
        };

        _кнопкаИндекса.Text = "▼";
        _кнопкаИндекса.Dock = DockStyle.Fill;
        _кнопкаИндекса.Font = EnvelopeUiTheme.ШрифтТекста(8.5f);
        _кнопкаИндекса.FlatStyle = FlatStyle.Flat;
        _кнопкаИндекса.FlatAppearance.BorderColor = EnvelopeUiTheme.Border;
        _кнопкаИндекса.BackColor = EnvelopeUiTheme.PanelBackground;
        _кнопкаИндекса.ForeColor = EnvelopeUiTheme.Text;
        _кнопкаИндекса.AccessibleName = "Выбрать индекс из истории";
        _кнопкаИндекса.AccessibleDescription = "Открывает список ранее использованных почтовых индексов";
        _кнопкаИндекса.UseVisualStyleBackColor = false;
        _кнопкаИндекса.Click += (_, _) => ПоказатьИсториюИндексов();

        for (var строка = 0; строка < Address.LineCount; строка++)
        {
            var метка = new Label
            {
                Text = $"Строка {строка + 1}:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = EnvelopeUiTheme.MutedText,
            };

            var поле = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = EnvelopeUiTheme.ШрифтТекста(8.5f),
                Margin = new Padding(0, 2, 2, 2),
                Tag = строка,
            };

            _подсказки.SetToolTip(поле, ПодсказкиСтрок[строка]);
            поле.AccessibleName = $"Строка {строка + 1} адреса — {ПодсказкиСтрок[строка]}";

            _поляСтрок[строка] = поле;

            сетка.Controls.Add(метка, 0, строка + 1);
            сетка.Controls.Add(поле, 1, строка + 1);
        }

        // Переключатель основного адреса живёт в таблице рядом с индексом:
        // координатное размещение накладывалось на поле индекса (аудит UX
        // 2026-11-03, G2).
        _переключательОсновного.Text = "основной";
        _переключательОсновного.AutoSize = true;
        _переключательОсновного.Font = EnvelopeUiTheme.ШрифтТекста(8.5f);
        _переключательОсновного.ForeColor = EnvelopeUiTheme.MutedText;
        _переключательОсновного.Anchor = AnchorStyles.Left;
        _переключательОсновного.Margin = new Padding(6, 2, 0, 2);
        _подсказки.SetToolTip(_переключательОсновного, "Считать этот адрес основным для контрагента");
        сетка.Controls.Add(_переключательОсновного, 3, 0);

        _подсказки.SetToolTip(_полеИндекса, "Почтовый индекс (6 цифр)");

        _историяИндексов.Visible = false;
        _историяИндексов.DropDownStyle = ComboBoxStyle.DropDownList;

        Controls.Add(сетка);
        Controls.Add(_заголовок);
        Controls.Add(_историяИндексов);
    }

    /// <summary>
    /// Событие выбора индекса из списка ранее использованных индексов.
    /// </summary>
    public event EventHandler<string>? ИндексВыбран;

    /// <summary>
    /// Событие запроса подбора значения поля из имеющихся записей: курсор стоит
    /// в пустом поле, нажата клавиша BackSpace (ТЗ §7.7; §8.1, функция 133).
    /// </summary>
    public event EventHandler? ТребуетсяПодборЗначения;

    /// <summary>Поле почтового индекса.</summary>
    public TextBox ПолеИндекса => _полеИндекса;

    /// <summary>Кнопка выбора индекса из истории.</summary>
    public Button КнопкаИндекса => _кнопкаИндекса;

    /// <summary>Поля строк адреса 1..6.</summary>
    public IReadOnlyList<TextBox> ПоляСтрок => _поляСтрок;

    /// <summary>Переключатель основного адреса.</summary>
    public RadioButton ПереключательОсновного => _переключательОсновного;

    /// <summary>
    /// Задаёт список индексов, доступных для выбора из кнопки «▼».
    /// </summary>
    /// <param name="индексы">Индексы.</param>
    public void ЗадатьИсториюИндексов(IEnumerable<string> индексы)
    {
        ArgumentNullException.ThrowIfNull(индексы);

        _историяИндексов.Items.Clear();

        foreach (var индекс in индексы.Where(значение => !string.IsNullOrWhiteSpace(значение)).Distinct())
        {
            _историяИндексов.Items.Add(индекс);
        }
    }

    /// <summary>
    /// Заполняет редактор значениями адреса.
    /// </summary>
    /// <param name="адрес">Адрес; <see langword="null"/> — очистить редактор.</param>
    public void УстановитьАдрес(Address? адрес)
    {
        _полеИндекса.Text = адрес?.Zip ?? string.Empty;

        for (var строка = 0; строка < Address.LineCount; строка++)
        {
            _поляСтрок[строка].Text = адрес?.Lines.ElementAtOrDefault(строка) ?? string.Empty;
        }
    }

    /// <summary>
    /// Возвращает адрес, заданный в редакторе.
    /// </summary>
    /// <returns>
    /// Адрес либо <see langword="null"/>, если редактор пуст; при некорректном
    /// индексе — <see langword="null"/> и текст ошибки в
    /// <see cref="ТекстОшибки"/>.
    /// </returns>
    public Address? ПолучитьАдрес()
    {
        ТекстОшибки = null;

        var строки = new string[Address.LineCount];

        for (var номер = 0; номер < Address.LineCount; номер++)
        {
            строки[номер] = _поляСтрок[номер].Text.Trim();
        }

        var индекс = _полеИндекса.Text.Trim();

        if (индекс.Length == 0 && строки.All(строка => строка.Length == 0))
        {
            return null;
        }

        var результат = Address.Create(индекс, строки);

        if (результат.IsFailure)
        {
            ТекстОшибки = результат.Error;
            return null;
        }

        return результат.Value;
    }

    /// <summary>
    /// Текст ошибки последней попытки получить адрес.
    /// </summary>
    public string? ТекстОшибки { get; private set; }

    /// <summary>
    /// Устанавливает признак основного адреса.
    /// </summary>
    /// <param name="основной">Признак основного адреса.</param>
    public void УстановитьОсновным(bool основной) => _переключательОсновного.Checked = основной;

    /// <summary>
    /// Показывает список ранее использованных индексов.
    /// </summary>
    private void ПоказатьИсториюИндексов()
    {
        if (_историяИндексов.Items.Count == 0)
        {
            return;
        }

        var выбранное = _историяИндексов.SelectedItem as string ?? _историяИндексов.Items[0]?.ToString();

        if (выбранное is null)
        {
            return;
        }

        _полеИндекса.Text = выбранное;
        ИндексВыбран?.Invoke(this, выбранное);
    }
}
