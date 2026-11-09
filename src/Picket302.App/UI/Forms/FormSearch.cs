using System.Drawing;
using System.Windows.Forms;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Форма поиска (ТЗ §7.7: «Форма поиска (Ctrl+F): последовательный перебор,
/// кнопка «Найти далее», показывает одну следующую запись, не подсвечивает все
/// совпадения»).
/// </summary>
/// <remarks>
/// Форма показывает одну следующую запись: совпадения не подсвечиваются все
/// сразу. Поиск ведётся по текущей позиции таблицы, перебор выполняется по
/// кнопке «Найти далее».
/// </remarks>
public sealed class FormSearch : Form
{
    private readonly TextBox _поле = new();
    private readonly Label _подсказка = new();
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
        ClientSize = new Size(360, 130);

        var метка = new Label
        {
            Text = "Найти:",
            AutoSize = true,
            Location = new Point(10, 12),
            AccessibleName = "Подпись условия поиска",
        };

        _поле.Location = new Point(70, 9);
        _поле.Width = 270;
        _поле.Text = текущийЗапрос ?? string.Empty;
        _поле.AccessibleName = "Условие поиска";
        _поле.AccessibleDescription = "Текст, по которому выполняется последовательный поиск в адресной книге.";

        _подсказка.AutoSize = false;
        _подсказка.Location = new Point(10, 40);
        _подсказка.Size = new Size(330, 32);
        _подсказка.Text = совпадений > 0
            ? $"Совпадений в выборке: {совпадений}"
            : "Введите условие поиска и нажмите «Найти далее».";
        _подсказка.AccessibleName = "Подсказка поиска";
        _подсказка.AccessibleDescription = "Сведения о текущем результате и способе запуска поиска.";

        _найтиДалее.Text = "Найти далее";
        _найтиДалее.Size = new Size(100, 24);
        _найтиДалее.Location = new Point(10, 92);
        _найтиДалее.AccessibleName = "Найти далее";
        _найтиДалее.AccessibleDescription = "Найти следующую запись после текущей позиции таблицы.";
        _найтиДалее.Click += (_, _) =>
        {
            НайтиСНачала = false;
            DialogResult = DialogResult.OK;
            Close();
        };

        _найтиСНачала.Text = "Найти с начала";
        _найтиСНачала.Size = new Size(110, 24);
        _найтиСНачала.Location = new Point(120, 92);
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
            Size = new Size(90, 24),
            Location = new Point(240, 92),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Закрыть поиск",
            AccessibleDescription = "Закрыть диалог без выполнения поиска.",
        };

        Controls.Add(метка);
        Controls.Add(_поле);
        Controls.Add(_подсказка);
        Controls.Add(_найтиДалее);
        Controls.Add(_найтиСНачала);
        Controls.Add(отмена);

        AcceptButton = _найтиДалее;
        CancelButton = отмена;
    }

    /// <summary>Искомое условие поиска.</summary>
    public string Запрос => _поле.Text.Trim();

    /// <summary>
    /// Признак того, что поиск начат с начала таблицы, а не с текущей записи.
    /// </summary>
    public bool НайтиСНачала { get; private set; }
}
