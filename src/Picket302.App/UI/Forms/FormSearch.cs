using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Форма поиска (ТЗ §7.7: «Форма поиска (Ctrl+F): последовательный перебор,
/// кнопка «Найти далее», показывает одну следующую запись, не подсвечивает все
/// совпадения»).
/// </summary>
/// <remarks>
/// Структура диалога намеренно близка к legacy-поверхности: образец, область
/// поиска, режим совпадения, область просмотра и параметры сравнения. Все
/// показанные параметры, которые доступны в текущем продукте, передаются в
/// <see cref="SearchMechanism"/>; визуальные элементы не являются макетом.
/// </remarks>
public sealed class FormSearch : Form
{
    private readonly TextBox _поле = new();
    private readonly ComboBox _искатьВ = new();
    private readonly ComboBox _совпадение = new();
    private readonly ComboBox _просмотр = new();
    private readonly CheckBox _сУчетомРегистра = new();
    private readonly CheckBox _сУчетомФормата = new();
    private readonly Button _найтиДалее = new();
    private readonly Button _найтиСНачала = new();

    /// <summary>
    /// Создаёт форму поиска.
    /// </summary>
    /// <param name="текущийЗапрос">Значение поиска, предложенное по умолчанию.</param>
    /// <param name="совпадений">Число совпадений в текущей выборке.</param>
    public FormSearch(string? текущийЗапрос = null, int совпадений = 0)
    {
        Text = "Поиск";
        AccessibleName = "Поиск по адресной книге";
        AccessibleDescription = "Диалог последовательного поиска записи в текущей таблице.";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(510, 160);
        BackColor = SystemColors.Control;

        var раскладка = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 3,
            RowCount = 5,
            BackColor = SystemColors.Control,
            AccessibleName = "Параметры поиска",
        };
        раскладка.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        раскладка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Ширина включает внутренние отступы кнопок (Margin=3 с каждой
        // стороны). 104 px оставляют контрактный запас для «Найти с начала»
        // при Segoe UI и не допускают обрезания текста на разных DPI.
        раскладка.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        for (var строка = 0; строка < 5; строка++)
        {
            раскладка.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        }

        var меткаОбразца = Метка("Образец:", "Подпись образца поиска");
        var меткаПоля = Метка("Поиск в:", "Подпись области поиска");
        var меткаСовпадения = Метка("Совпадение:", "Подпись режима совпадения");
        var меткаПросмотра = Метка("Просмотр:", "Подпись области просмотра");

        _поле.Dock = DockStyle.Fill;
        _поле.Margin = new Padding(3, 4, 3, 3);
        _поле.Text = текущийЗапрос ?? string.Empty;
        _поле.AccessibleName = "Образец поиска";
        _поле.AccessibleDescription = совпадений > 0
            ? $"Текст для последовательного поиска в адресной книге. Совпадений в выборке: {совпадений}."
            : "Текст, по которому выполняется последовательный поиск в адресной книге.";

        НастроитьСписок(_искатьВ, "Область поиска", "Искомое поле текущей таблицы.", "Наименование");
        НастроитьСписок(
            _совпадение,
            "Режим совпадения",
            "Определяет, где внутри поля должна совпасть строка.",
            "С любой части поля",
            "С начала поля",
            "Полное совпадение");
        НастроитьСписок(_просмотр, "Область просмотра", "Набор строк текущей таблицы.", "Все");

        _сУчетомРегистра.Text = "С учетом регистра";
        _сУчетомРегистра.AutoSize = true;
        _сУчетомРегистра.AccessibleName = "С учетом регистра";
        _сУчетомРегистра.AccessibleDescription = "Отличать прописные и строчные буквы при поиске.";

        _сУчетомФормата.Text = "С учетом формата";
        _сУчетомФормата.AutoSize = true;
        _сУчетомФормата.AccessibleName = "С учетом формата";
        _сУчетомФормата.AccessibleDescription = "Не удалять пробелы по краям образца поиска.";

        _найтиДалее.Text = "Найти далее";
        _найтиДалее.Dock = DockStyle.Fill;
        _найтиДалее.Margin = new Padding(3);
        _найтиДалее.AccessibleName = "Найти далее";
        _найтиДалее.AccessibleDescription = "Найти следующую запись после текущей позиции таблицы.";
        _найтиДалее.Click += (_, _) =>
        {
            НайтиСНачала = false;
            DialogResult = DialogResult.OK;
            Close();
        };

        _найтиСНачала.Text = "Найти с начала";
        _найтиСНачала.Dock = DockStyle.Fill;
        _найтиСНачала.Margin = new Padding(3);
        _найтиСНачала.AccessibleName = "Найти с начала";
        _найтиСНачала.AccessibleDescription = "Начать поиск с первой записи текущей таблицы.";
        _найтиСНачала.Click += (_, _) =>
        {
            НайтиСНачала = true;
            DialogResult = DialogResult.OK;
            Close();
        };

        var отмена = new Button
        {
            Text = "Закрыть",
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Закрыть поиск",
            AccessibleDescription = "Закрыть диалог без выполнения поиска.",
        };

        раскладка.Controls.Add(меткаОбразца, 0, 0);
        раскладка.Controls.Add(_поле, 1, 0);
        раскладка.Controls.Add(меткаПоля, 0, 1);
        раскладка.Controls.Add(_искатьВ, 1, 1);
        раскладка.Controls.Add(меткаСовпадения, 0, 2);
        раскладка.Controls.Add(_совпадение, 1, 2);
        раскладка.Controls.Add(меткаПросмотра, 0, 3);
        раскладка.Controls.Add(_просмотр, 1, 3);

        var параметры = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Margin = new Padding(0),
        };
        параметры.Controls.Add(_сУчетомРегистра);
        параметры.Controls.Add(_сУчетомФормата);
        раскладка.Controls.Add(параметры, 1, 4);
        раскладка.SetColumnSpan(параметры, 2);

        var действия = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 96,
            RowCount = 3,
            ColumnCount = 1,
            Margin = new Padding(0),
        };
        действия.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        действия.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        действия.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        действия.Controls.Add(_найтиДалее, 0, 0);
        действия.Controls.Add(_найтиСНачала, 0, 1);
        действия.Controls.Add(отмена, 0, 2);
        раскладка.Controls.Add(действия, 2, 0);
        раскладка.SetRowSpan(действия, 5);

        Controls.Add(раскладка);

        AcceptButton = _найтиДалее;
        CancelButton = отмена;
    }

    /// <summary>Искомое условие поиска.</summary>
    public string Запрос => _сУчетомФормата.Checked ? _поле.Text : _поле.Text.Trim();

    /// <summary>Выбранный режим сопоставления значения.</summary>
    public РежимСовпадения Режим => (РежимСовпадения)Math.Max(0, _совпадение.SelectedIndex);

    /// <summary>Нужно ли различать прописные и строчные буквы.</summary>
    public bool УчитыватьРегистр => _сУчетомРегистра.Checked;

    /// <summary>Нужно ли сохранять пробелы образца без обрезания.</summary>
    public bool УчитыватьФормат => _сУчетомФормата.Checked;

    /// <summary>
    /// Признак того, что поиск начат с начала таблицы, а не с текущей записи.
    /// </summary>
    public bool НайтиСНачала { get; private set; }

    private static Label Метка(string текст, string имя) => new()
    {
        Text = текст,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoSize = false,
        AccessibleName = имя,
        Margin = new Padding(3),
    };

    private static void НастроитьСписок(
        ComboBox список,
        string имя,
        string описание,
        params string[] варианты)
    {
        список.DropDownStyle = ComboBoxStyle.DropDownList;
        список.Dock = DockStyle.Fill;
        список.Margin = new Padding(3, 2, 3, 2);
        список.AccessibleName = имя;
        список.AccessibleDescription = описание;
        список.Items.AddRange(варианты);
        список.SelectedIndex = 0;
    }
}
