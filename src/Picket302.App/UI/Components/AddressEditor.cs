using System.Drawing;
using System.Windows.Forms;
using Picket302.Domain.ValueObjects;

namespace Picket302.App.UI.Components;

/// <summary>
/// Редактор адреса: почтовый индекс, шесть строк и переключатель основного
/// адреса (ТЗ §7.1: «Индекс: [220050] [▼]», «Стр 1: …» … «Стр 6: …»;
/// §8.1, функция 4: «Шесть строк адреса»).
/// </summary>
/// <remarks>
/// Индекс редактируется в отдельном поле с кнопкой выбора из ранее
/// использовавшихся индексов. Шесть строк адреса нумеруются как «Стр 1» … «Стр 6».
/// </remarks>
public sealed class AddressEditor : UserControl
{
    private readonly TextBox[] _поляСтрок = new TextBox[Address.LineCount];
    private readonly TextBox _полеИндекса = new();
    private readonly Button _кнопкаИндекса = new();
    private readonly RadioButton _переключательОсновного = new();
    private readonly Label _заголовок = new();
    private readonly ComboBox _историяИндексов = new();

    /// <summary>
    /// Создаёт редактор адреса.
    /// </summary>
    /// <param name="заголовок">Заголовок блока адреса.</param>
    public AddressEditor(string заголовок)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(заголовок);

        Dock = DockStyle.Top;
        AutoSize = false;
        Height = 150;
        Padding = new Padding(2);

        _заголовок.Text = заголовок;
        _заголовок.AutoSize = true;
        _заголовок.Font = new Font("Tahoma", 8f, FontStyle.Bold);
        _заголовок.Location = new Point(4, 2);

        var сетка = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = Address.LineCount + 1,
            AutoSize = true,
            Padding = new Padding(0, 16, 0, 0),
        };

        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));

        var меткаИндекса = new Label
        {
            Text = "Индекс:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };

        сетка.Controls.Add(меткаИндекса, 0, 0);
        сетка.Controls.Add(_полеИндекса, 1, 0);
        сетка.Controls.Add(_кнопкаИндекса, 2, 0);

        _полеИндекса.Dock = DockStyle.Fill;
        _полеИндекса.Font = new Font("Tahoma", 8f);
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
        _кнопкаИндекса.Font = new Font("Tahoma", 8f);
        _кнопкаИндекса.FlatStyle = FlatStyle.System;
        _кнопкаИндекса.Click += (_, _) => ПоказатьИсториюИндексов();

        for (var строка = 0; строка < Address.LineCount; строка++)
        {
            var метка = new Label
            {
                Text = $"Стр {строка + 1}:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };

            var поле = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Tahoma", 8f),
                Tag = строка,
            };

            _поляСтрок[строка] = поле;

            сетка.Controls.Add(метка, 0, строка + 1);
            сетка.Controls.Add(поле, 1, строка + 1);
        }

        _переключательОсновного.Text = "основной";
        _переключательОсновного.AutoSize = true;
        _переключательОсновного.Font = new Font("Tahoma", 8f);
        _переключательОсновного.Location = new Point(60, 4);

        _историяИндексов.Visible = false;
        _историяИндексов.DropDownStyle = ComboBoxStyle.DropDownList;

        Controls.Add(сетка);
        Controls.Add(_заголовок);
        Controls.Add(_переключательОсновного);
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
