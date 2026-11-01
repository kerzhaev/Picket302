using System.Drawing;
using System.Windows.Forms;
using Picket302.Domain.Entities;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Карточка отправления: категория отправления, вид уведомления о вручении
/// и признак «нужно уведомление» (ТЗ §8.1, функции 23 и 32).
/// </summary>
/// <remarks>
/// <para>
/// Карточка вызывается перед печатью: оператор выбирает классификацию
/// отправления (письмо, бандероль, 1-й класс, заказное, заказное с
/// уведомлением) и вид уведомления (ф.119, украинская форма, 112-эп).
/// </para>
/// <para>
/// Выбранные значения записываются в карточку отправления до сохранения в
/// базе, поэтому журнал печати и реестр ф.103 содержат фактические реквизиты
/// отправления.
/// </para>
/// </remarks>
public sealed class FormMailEdit : Form
{
    private readonly ComboBox _категория = new();
    private readonly ComboBox _уведомление = new();
    private readonly ComboBox _формат = new();
    private readonly CheckBox _нужноУведомление = new();
    private readonly NumericUpDown _копий = new();
    private readonly Label _подсказка = new();

    /// <summary>
    /// Создаёт карточку отправления.
    /// </summary>
    /// <param name="значения">Начальные значения полей карточки.</param>
    public FormMailEdit(MailCardValues? значения = null)
    {
        Text = "Карточка отправления";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(470, 320);

        var начальные = значения ?? new MailCardValues();

        _категория.DropDownStyle = ComboBoxStyle.DropDownList;
        _категория.Location = new Point(180, 24);
        _категория.Width = 250;
        _категория.Items.AddRange(КатегорияОпции.Список());
        _категория.SelectedIndex = _категория.FindStringExact(КатегорияОпции.Название(начальные.Категория));
        if (_категория.SelectedIndex < 0)
        {
            _категория.SelectedIndex = 0;
        }

        _уведомление.DropDownStyle = ComboBoxStyle.DropDownList;
        _уведомление.Location = new Point(180, 62);
        _уведомление.Width = 250;
        _уведомление.Items.AddRange(УведомлениеОпции.Список());
        _уведомление.SelectedIndex = _уведомление.FindStringExact(начальные.ВидУведомления.Название());
        if (_уведомление.SelectedIndex < 0)
        {
            _уведомление.SelectedIndex = 0;
        }

        _формат.DropDownStyle = ComboBoxStyle.DropDownList;
        _формат.Location = new Point(180, 100);
        _формат.Width = 250;
        _формат.Items.AddRange(ФорматОпции.Список());
        _формат.SelectedIndex = _формат.FindStringExact(начальные.ФорматКонверта);
        if (_формат.SelectedIndex < 0)
        {
            _формат.SelectedIndex = 0;
        }

        _нужноУведомление.Text = "Нужно уведомление о вручении";
        _нужноУведомление.AutoSize = true;
        _нужноУведомление.Location = new Point(180, 142);
        _нужноУведомление.Checked = начальные.НужноУведомление;

        _копий.Location = new Point(180, 180);
        _копий.Width = 80;
        _копий.Minimum = Mail.MinimumCopyCount;
        _копий.Maximum = 99;
        _копий.Value = Math.Clamp(начальные.ЧислоКопий, Mail.MinimumCopyCount, 99);

        _подсказка.Text = "Категория и вид уведомления записываются в карточку отправления "
            + "и попадают в журнал печати и реестр ф.103.";
        _подсказка.AutoSize = false;
        _подсказка.Location = new Point(24, 226);
        _подсказка.Size = new Size(422, 32);
        _подсказка.ForeColor = SystemColors.GrayText;

        var кнопкаОк = new Button
        {
            Text = "ОК",
            DialogResult = DialogResult.OK,
            Size = new Size(110, 28),
            Location = new Point(244, 272),
        };

        var кнопкаОтмена = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            Size = new Size(110, 28),
            Location = new Point(360, 272),
        };

        Controls.AddRange(new Control[]
        {
            Метка("Категория отправления:", 24),
            _категория,
            Метка("Вид уведомления:", 62),
            _уведомление,
            Метка("Формат конверта:", 100),
            _формат,
            _нужноУведомление,
            Метка("Число экземпляров:", 184),
            _копий,
            _подсказка,
            кнопкаОк,
            кнопкаОтмена,
        });

        AcceptButton = кнопкаОк;
        CancelButton = кнопкаОтмена;
    }

    /// <summary>Выбранная категория отправления.</summary>
    public MailCategory Категория =>
        _категория.SelectedIndex >= 0
            ? КатегорияОпции.Значение(_категория.SelectedIndex)
            : MailCategory.Simple;

    /// <summary>Выбранный вид уведомления о вручении.</summary>
    public NotificationType ВидУведомления =>
        _уведомление.SelectedIndex >= 0
            ? УведомлениеОпции.Значение(_уведомление.SelectedIndex)
            : NotificationType.None;

    /// <summary>Выбранный формат конверта.</summary>
    public string ФорматКонверта =>
        _формат.SelectedIndex >= 0 ? _формат.SelectedItem?.ToString() ?? "C6" : "C6";

    /// <summary>Выпадающий список категорий отправления.</summary>
    public ComboBox ПереченьКатегорий => _категория;

    /// <summary>Выпадающий список видов уведомления.</summary>
    public ComboBox ПереченьУведомлений => _уведомление;

    /// <summary>Выпадающий список форматов конверта.</summary>
    public ComboBox ПереченьФорматов => _формат;

    /// <summary>Флажок «нужно уведомление о вручении».</summary>
    public CheckBox ФлажокУведомления => _нужноУведомление;

    /// <summary>Поле числа экземпляров.</summary>
    public NumericUpDown ПолеКопий => _копий;

    /// <summary>Признак «нужно уведомление о вручении».</summary>
    public bool НужноУведомление => _нужноУведомление.Checked;

    /// <summary>Число экземпляров отправления.</summary>
    public int ЧислоКопий => (int)_копий.Value;

    /// <summary>
    /// Переносит выбранные значения в карточку отправления.
    /// </summary>
    /// <param name="письмо">Карточка отправления.</param>
    /// <exception cref="ArgumentNullException">Карточка не задана.</exception>
    public void Применить(Mail письмо)
    {
        ArgumentNullException.ThrowIfNull(письмо);

        письмо.MailCategory = Категория;
        письмо.NeedNotification = НужноУведомление;
        письмо.CopyCount = ЧислоКопий;

        if (!string.IsNullOrWhiteSpace(ФорматКонверта))
        {
            письмо.EnvelopeType = ФорматКонверта;
        }

        var вид = ВидУведомления;

        // Свойство NotificationType вычисляется из NoticeType, поэтому
        // записывается только строковый код вида уведомления.
        письмо.NoticeType = вид == NotificationType.None ? null : вид.Ключ();
    }

    /// <summary>
    /// Формирует набор подписей категорий отправления.
    /// </summary>
    /// <returns>Подписи категорий.</returns>
    private static Label Метка(string текст, int позиция) => new()
    {
        Text = текст,
        AutoSize = true,
        Location = new Point(24, позиция + 4),
    };
}

/// <summary>
/// Значения карточки отправления (ТЗ §8.1, функции 23 и 32).
/// </summary>
/// <param name="Категория">Категория отправления.</param>
/// <param name="ВидУведомления">Вид уведомления о вручении.</param>
/// <param name="НужноУведомление">Признак «нужно уведомление о вручении».</param>
/// <param name="ФорматКонверта">Формат конверта.</param>
/// <param name="ЧислоКопий">Число экземпляров.</param>
public sealed record MailCardValues(
    MailCategory Категория = MailCategory.Simple,
    NotificationType ВидУведомления = NotificationType.None,
    bool НужноУведомление = false,
    string ФорматКонверта = "C6",
    int ЧислоКопий = Mail.MinimumCopyCount);

/// <summary>
/// Подписи категорий отправления для выпадающего списка карточки
/// (ТЗ §8.1, функция 23).
/// </summary>
public static class КатегорияОпции
{
    /// <summary>
    /// Возвращает подписи категорий отправления в порядке перечисления.
    /// </summary>
    /// <returns>Подписи категорий.</returns>
    public static string[] Список() =>
    [
        "Простое письмо",
        "Заказное письмо",
        "Бандероль с объявленной ценностью",
        "1-й класс",
    ];

    /// <summary>
    /// Возвращает подпись категории отправления.
    /// </summary>
    /// <param name="категория">Категория отправления.</param>
    /// <returns>Подпись категории.</returns>
    public static string Название(MailCategory категория) => категория switch
    {
        MailCategory.Registered => "Заказное письмо",
        MailCategory.DeclaredValue => "Бандероль с объявленной ценностью",
        MailCategory.FirstClass => "1-й класс",
        _ => "Простое письмо",
    };

    /// <summary>
    /// Возвращает категорию отправления по индексу в списке.
    /// </summary>
    /// <param name="индекс">Индекс элемента списка.</param>
    /// <returns>Категория отправления.</returns>
    public static MailCategory Значение(int индекс) => индекс switch
    {
        1 => MailCategory.Registered,
        2 => MailCategory.DeclaredValue,
        3 => MailCategory.FirstClass,
        _ => MailCategory.Simple,
    };
}

/// <summary>
/// Подписи видов уведомления о вручении для выпадающего списка карточки
/// (ТЗ §8.1, функция 32).
/// </summary>
public static class УведомлениеОпции
{
    /// <summary>
    /// Возвращает подписи видов уведомления в порядке перечисления.
    /// </summary>
    /// <returns>Подписи видов уведомления.</returns>
    public static string[] Список() =>
    [
        NotificationType.None.Название(),
        NotificationType.Form119.Название(),
        NotificationType.National.Название(),
        NotificationType.Form112Ep.Название(),
    ];

    /// <summary>
    /// Возвращает вид уведомления по индексу в списке.
    /// </summary>
    /// <param name="индекс">Индекс элемента списка.</param>
    /// <returns>Вид уведомления.</returns>
    public static NotificationType Значение(int индекс) => индекс switch
    {
        1 => NotificationType.Form119,
        2 => NotificationType.National,
        3 => NotificationType.Form112Ep,
        _ => NotificationType.None,
    };
}

/// <summary>
/// Подписи форматов конверта для выпадающего списка карточки отправления.
/// </summary>
public static class ФорматОпции
{
    /// <summary>
    /// Возвращает перечень форматов конверта, поддерживаемых программой.
    /// </summary>
    /// <returns>Подписи форматов.</returns>
    public static string[] Список() => ["C6", "DL", "C5", "C4"];
}
