using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Domain.Entities;
using Picket302.Domain.Rules;
using Picket302.Domain.ValueObjects;
namespace Picket302.App.UI.Forms;

/// <summary>
/// Карточка контрагента: вкладки «Реквизиты», «Банк», «Паспорт», три адреса
/// и пользовательские поля (ТЗ §8.1, функции 1, 2, 3 и 7).
/// </summary>
/// <remarks>
/// <para>
/// Реквизиты проверяются по контрольным суммам: некорректный ИНН или БИК
/// блокирует кнопку «Сохранить» и подсвечивает поле (ТЗ §8.1, функция 1:
/// «Некорректная контрольная сумма ИНН/БИК» — диалоговое предупреждение с
/// подсветкой поля).
/// </para>
/// <para>
/// Юридические и физические лица разделяются переключателем типа контрагента:
/// для физического лица карточка банковских реквизитов не заполняется, вместо
/// неё указываются паспортные данные (ТЗ §8.1, функция 2).
/// </para>
/// </remarks>
public sealed class FormCounterpartyEdit : Form
{
    private readonly Counterparty _контрагент;
    private readonly bool _создание;

    private readonly TabControl _вкладки = new();
    private readonly TabPage _страницаРеквизитов = new("Реквизиты");
    private readonly TabPage _страницаБанка = new("Банк");
    private readonly TabPage _страницаПаспорта = new("Паспорт");

    private readonly RadioButton _типЮридическое = new() { Text = "Юридическое лицо" };
    private readonly RadioButton _типФизическое = new() { Text = "Физическое лицо" };

    private readonly TextBox _наименование = new();
    private readonly TextBox _сокращённое = new();
    private readonly TextBox _инн = new();
    private readonly TextBox _кпп = new();
    private readonly TextBox _огрн = new();
    private readonly TextBox _окфс = new();
    private readonly TextBox _окпо = new();
    private readonly TextBox _оквед = new();
    private readonly TextBox _телефон = new();
    private readonly TextBox _почта = new();
    private readonly TextBox _примечание = new();

    private readonly TextBox _бик = new();
    private readonly TextBox _корсчет = new();
    private readonly TextBox _счет = new();
    private readonly TextBox _наименованиеБанка = new();

    private readonly TextBox _серияПаспорта = new();
    private readonly TextBox _номерПаспорта = new();
    private readonly TextBox _кемВыдан = new();
    private readonly DateTimePicker _датаВыдачи = new() { Checked = false, ShowUpDown = true };
    private readonly DateTimePicker _датаРождения = new() { Checked = false, ShowUpDown = true };
    private readonly TextBox _местоРождения = new();
    private readonly TextBox _должность = new();
    private readonly TextBox _директор = new();

    private readonly AddressEditor _юридическийАдрес = new("ЮРИДИЧЕСКИЙ АДРЕС:");
    private readonly AddressEditor _фактическийАдрес = new("ФАКТИЧЕСКИЙ АДРЕС:");
    private readonly AddressEditor _почтовыйАдрес = new("ПОЧТОВЫЙ АДРЕС:");

    private readonly DataGridView _пользовательскиеПоля = new();
    private readonly Button _сохранить = new();
    private readonly Button _отмена = new();

    /// <summary>
    /// Создаёт карточку контрагента.
    /// </summary>
    /// <param name="контрагент">
    /// Редактируемая карточка; <see langword="null"/> — создаётся новая запись.
    /// </param>
    public FormCounterpartyEdit(Counterparty? контрагент = null)
    {
        _создание = контрагент is null;
        _контрагент = контрагент ?? new Counterparty();

        Text = _создание ? "Новый контрагент" : "Карточка контрагента";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(760, 560);

        ПостроитьРеквизиты();
        ПостроитьБанк();
        ПостроитьПаспорт();
        ПостроитьКнопки();

        _вкладки.TabPages.Add(_страницаРеквизитов);
        _вкладки.TabPages.Add(_страницаБанка);
        _вкладки.TabPages.Add(_страницаПаспорта);
        _вкладки.Dock = DockStyle.Fill;

        Controls.Add(_вкладки);

        ЗагрузитьДанные();
        ПодписатьСобытия();
        ОбновитьСостояние();
    }

    /// <summary>
    /// Подписывает обработчики изменения реквизитов: доступность кнопки
    /// «Сохранить» пересчитывается при каждом изменении поля.
    /// </summary>
    private void ПодписатьСобытия()
    {
        _инн.TextChanged += (_, _) => ОбновитьСостояние();
        _кпп.TextChanged += (_, _) => ОбновитьСостояние();
        _бик.TextChanged += (_, _) => ОбновитьСостояние();
        _наименование.TextChanged += (_, _) => ОбновитьСостояние();
        _типЮридическое.CheckedChanged += (_, _) => ОбновитьСостояние();
        _типФизическое.CheckedChanged += (_, _) => ОбновитьСостояние();
    }

    /// <summary>
    /// Сохранённая карточка контрагента; <see langword="null"/>, если сохранение
    /// не выполнялось.
    /// </summary>
    public Counterparty? Результат { get; private set; }

    /// <summary>
    /// Значения пользовательских полей, введённых в карточке.
    /// </summary>
    public IReadOnlyList<(string Имя, string Значение)> ПользовательскиеПоля { get; private set; } = [];

    /// <summary>Поле ИНН карточки.</summary>
    public TextBox ПолеИНН => _инн;

    /// <summary>Поле наименования контрагента.</summary>
    public TextBox ПолеНаименование => _наименование;

    /// <summary>Поле КПП карточки.</summary>
    public TextBox ПолеКПП => _кпп;

    /// <summary>Поле БИК карточки.</summary>
    public TextBox ПолеБИК => _бик;

    /// <summary>Кнопка «Сохранить».</summary>
    public Button КнопкаСохранить => _сохранить;

    /// <summary>
    /// Признак того, что реквизиты карточки корректны и карточка может быть
    /// сохранена (ТЗ §8.1, функция 1: некорректная контрольная сумма ИНН/БИК
    /// блокирует кнопку «Сохранить»).
    /// </summary>
    public bool СохранениеДоступно => _сохранить.Enabled;

    /// <summary>
    /// Строит вкладку «Реквизиты»: тип контрагента, реквизиты, три адреса и
    /// пользовательские поля.
    /// </summary>
    private void ПостроитьРеквизиты()
    {
        var панельТипа = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 24,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _типЮридическое.AutoSize = true;
        _типФизическое.AutoSize = true;
        _типЮридическое.Checked = true;

        панельТипа.Controls.Add(_типЮридическое);
        панельТипа.Controls.Add(_типФизическое);

        var реквизиты = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 150,
            ColumnCount = 4,
            AutoSize = false,
        };

        реквизиты.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        реквизиты.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        реквизиты.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        реквизиты.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        ДобавитьПоле(реквизиты, 0, 0, "Наименование:", _наименование);
        ДобавитьПоле(реквизиты, 0, 1, "Сокращённое:", _сокращённое);
        ДобавитьПоле(реквизиты, 0, 2, "ИНН:", _инн);
        ДобавитьПоле(реквизиты, 0, 3, "КПП:", _кпп);
        ДобавитьПоле(реквизиты, 0, 4, "ОГРН:", _огрн);
        ДобавитьПоле(реквизиты, 0, 5, "ОКФС:", _окфс);
        ДобавитьПоле(реквизиты, 0, 6, "ОКПО:", _окпо);
        ДобавитьПоле(реквизиты, 0, 7, "ОКВЭД:", _оквед);
        ДобавитьПоле(реквизиты, 0, 8, "Телефон:", _телефон);
        ДобавитьПоле(реквизиты, 0, 9, "Эл. почта:", _почта);

        var панельАдресов = new Panel { Dock = DockStyle.Top, Height = 470 };

        _юридическийАдрес.Location = new Point(0, 0);
        _юридическийАдрес.Width = 360;
        _фактическийАдрес.Location = new Point(370, 0);
        _фактическийАдрес.Width = 360;
        _почтовыйАдрес.Location = new Point(0, 155);
        _почтовыйАдрес.Width = 360;

        панельАдресов.Controls.Add(_юридическийАдрес);
        панельАдресов.Controls.Add(_фактическийАдрес);
        панельАдресов.Controls.Add(_почтовыйАдрес);

        _пользовательскиеПоля.Dock = DockStyle.Fill;
        _пользовательскиеПоля.Font = new Font("Tahoma", 8f);
        _пользовательскиеПоля.AllowUserToAddRows = true;
        _пользовательскиеПоля.AllowUserToDeleteRows = true;
        _пользовательскиеПоля.RowHeadersVisible = false;
        _пользовательскиеПоля.Columns.Add("Поле", "Поле");
        _пользовательскиеПоля.Columns.Add("Значение", "Значение");
        _пользовательскиеПоля.Columns.Add("Тег", "Тег");
        _пользовательскиеПоля.Columns[2].ReadOnly = true;
        _пользовательскиеПоля.Columns[0].Width = 160;
        _пользовательскиеПоля.Columns[1].Width = 240;
        _пользовательскиеПоля.CellValueChanged += (_, событие) =>
        {
            if (событие.ColumnIndex != 0 || событие.RowIndex < 0)
            {
                return;
            }

            var имя = _пользовательскиеПоля.Rows[событие.RowIndex].Cells[0].Value as string ?? string.Empty;
            _пользовательскиеПоля.Rows[событие.RowIndex].Cells[2].Value =
                CustomFieldTag.TryFormat(имя, out var тег) ? тег : string.Empty;
        };

        var панельПользовательских = new Panel { Dock = DockStyle.Bottom, Height = 170 };
        var заголовокПолей = new Label
        {
            Text = "Пользовательские поля (в макетах доступны по тегу вида [Custom_ИмяПоля]):",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Tahoma", 8f, FontStyle.Bold),
        };

        панельПользовательских.Controls.Add(_пользовательскиеПоля);
        панельПользовательских.Controls.Add(заголовокПолей);

        _страницаРеквизитов.Controls.Add(_пользовательскиеПоля);
        _страницаРеквизитов.Controls.Add(панельПользовательских);
        _страницаРеквизитов.Controls.Add(панельАдресов);
        _страницаРеквизитов.Controls.Add(реквизиты);
        _страницаРеквизитов.Controls.Add(панельТипа);
    }

    /// <summary>
    /// Строит вкладку «Банк».
    /// </summary>
    private void ПостроитьБанк()
    {
        var панель = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 120,
            ColumnCount = 2,
        };

        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ДобавитьПоле(панель, 0, 0, "БИК:", _бик);
        ДобавитьПоле(панель, 0, 1, "Кор. счёт:", _корсчет);
        ДобавитьПоле(панель, 0, 2, "Счёт:", _счет);
        ДобавитьПоле(панель, 0, 3, "Банк:", _наименованиеБанка);

        _страницаБанка.Controls.Add(панель);
    }

    /// <summary>
    /// Строит вкладку «Паспорт».
    /// </summary>
    private void ПостроитьПаспорт()
    {
        var панель = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 140,
            ColumnCount = 2,
        };

        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ДобавитьПоле(панель, 0, 0, "Серия:", _серияПаспорта);
        ДобавитьПоле(панель, 0, 1, "Номер:", _номерПаспорта);
        ДобавитьПоле(панель, 0, 2, "Кем выдан:", _кемВыдан);
        ДобавитьПоле(панель, 0, 3, "Дата выдачи:", _датаВыдачи);
        ДобавитьПоле(панель, 0, 4, "Дата рождения:", _датаРождения);
        ДобавитьПоле(панель, 0, 5, "Место рождения:", _местоРождения);
        ДобавитьПоле(панель, 0, 6, "Должность:", _должность);
        ДобавитьПоле(панель, 0, 7, "Директор (ФИО):", _директор);

        _страницаПаспорта.Controls.Add(панель);
    }

    /// <summary>
    /// Строит кнопки формы.
    /// </summary>
    private void ПостроитьКнопки()
    {
        var панель = new Panel { Dock = DockStyle.Bottom, Height = 32 };

        _сохранить.Text = "Сохранить";
        _сохранить.Size = new Size(90, 24);
        _сохранить.Location = new Point(8, 4);
        _сохранить.Click += (_, _) => Сохранить();

        _отмена.Text = "Отмена";
        _отмена.Size = new Size(90, 24);
        _отмена.Location = new Point(104, 4);
        _отмена.DialogResult = DialogResult.Cancel;

        панель.Controls.Add(_сохранить);
        панель.Controls.Add(_отмена);

        Controls.Add(панель);

        AcceptButton = _сохранить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Добавляет поле ввода в таблицу формы.
    /// </summary>
    private static void ДобавитьПоле(TableLayoutPanel таблица, int колонка, int строка, string текст, Control поле)
    {
        var метка = new Label
        {
            Text = текст,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };

        поле.Dock = DockStyle.Fill;
        поле.Font = new Font("Tahoma", 8f);
        поле.Margin = new Padding(0, 0, 6, 2);

        таблица.Controls.Add(метка, колонка, строка);
        таблица.Controls.Add(поле, колонка + 1, строка);
    }

    /// <summary>
    /// Загружает значения карточки в элементы формы.
    /// </summary>
    private void ЗагрузитьДанные()
    {
        _наименование.Text = _контрагент.Name;
        _сокращённое.Text = _контрагент.ShortName ?? string.Empty;
        _инн.Text = _контрагент.Inn ?? string.Empty;
        _кпп.Text = _контрагент.Kpp ?? string.Empty;
        _огрн.Text = _контрагент.Ogrn ?? string.Empty;
        _окфс.Text = _контрагент.Okfs ?? string.Empty;
        _окпо.Text = _контрагент.Okpo ?? string.Empty;
        _оквед.Text = _контрагент.Okved ?? string.Empty;
        _телефон.Text = _контрагент.Phone ?? string.Empty;
        _почта.Text = _контрагент.Email ?? string.Empty;
        _примечание.Text = _контрагент.SpecialNotes ?? string.Empty;

        _бик.Text = _контрагент.Bik ?? string.Empty;
        _корсчет.Text = _контрагент.Ks ?? string.Empty;
        _счет.Text = _контрагент.Rs ?? string.Empty;
        _наименованиеБанка.Text = _контрагент.BankName ?? string.Empty;

        _серияПаспорта.Text = _контрагент.PassportSeries ?? string.Empty;
        _номерПаспорта.Text = _контрагент.PassportNumber ?? string.Empty;
        _кемВыдан.Text = _контрагент.PassportIssuedBy ?? string.Empty;
        _датаРождения.Value = _контрагент.BirthDate ?? DateTime.Today;
        _датаРождения.Checked = _контрагент.BirthDate is not null;
        _местоРождения.Text = _контрагент.BirthPlace ?? string.Empty;
        _должность.Text = _контрагент.Position ?? string.Empty;
        _директор.Text = _контрагент.DirectorFio ?? string.Empty;

        if (_контрагент.PassportIssuedDate is not null)
        {
            _датаВыдачи.Value = _контрагент.PassportIssuedDate.Value;
            _датаВыдачи.Checked = true;
        }

        _типЮридическое.Checked = _контрагент.Type == CounterpartyType.LegalEntity;
        _типФизическое.Checked = _контрагент.Type == CounterpartyType.NaturalPerson;

        _юридическийАдрес.УстановитьАдрес(_контрагент.Addresses.Legal);
        _фактическийАдрес.УстановитьАдрес(_контрагент.Addresses.Actual);
        _почтовыйАдрес.УстановитьАдрес(_контрагент.Addresses.Postal);

        _юридическийАдрес.УстановитьОсновным(_контрагент.Addresses.MainType == AddressMainType.Legal);
        _фактическийАдрес.УстановитьОсновным(_контрагент.Addresses.MainType == AddressMainType.Actual);
        _почтовыйАдрес.УстановитьОсновным(_контрагент.Addresses.MainType == AddressMainType.Postal);

        _пользовательскиеПоля.Rows.Clear();
    }

    /// <summary>
    /// Обновляет доступность кнопки «Сохранить» и подсветку ошибочных полей.
    /// </summary>
    private void ОбновитьСостояние()
    {
        var ошибки = ПроверитьРеквизиты();

        _сохранить.Enabled = ошибки.Count == 0;

        ПодсветитьПоле(_инн, ошибки.Contains(nameof(_инн)));
        ПодсветитьПоле(_кпп, ошибки.Contains(nameof(_кпп)));
        ПодсветитьПоле(_бик, ошибки.Contains(nameof(_бик)));

        _вкладки.TabPages[_вкладки.TabPages.IndexOf(_страницаБанка)].Enabled =
            _типЮридическое.Checked;
        _вкладки.TabPages[_вкладки.TabPages.IndexOf(_страницаПаспорта)].Enabled =
            _типФизическое.Checked;
    }

    /// <summary>
    /// Проверяет реквизиты карточки по контрольным суммам.
    /// </summary>
    /// <returns>Имена ошибочных полей.</returns>
    private HashSet<string> ПроверитьРеквизиты()
    {
        var ошибки = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(_наименование.Text))
        {
            ошибки.Add(nameof(_наименование));
        }

        if (!string.IsNullOrWhiteSpace(_инн.Text)
            && InnValidator.Validate(_инн.Text.Trim(), _контрагент.Type).IsFailure)
        {
            ошибки.Add(nameof(_инн));
        }

        if (!string.IsNullOrWhiteSpace(_кпп.Text)
            && BankRequisitesValidator.ValidateKpp(_кпп.Text.Trim()).IsFailure)
        {
            ошибки.Add(nameof(_кпп));
        }

        if (!string.IsNullOrWhiteSpace(_бик.Text)
            && BankRequisitesValidator.ValidateBik(_бик.Text.Trim()).IsFailure)
        {
            ошибки.Add(nameof(_бик));
        }

        return ошибки;
    }

    /// <summary>
    /// Подсвечивает ошибочное поле цветом подсказки.
    /// </summary>
    private static void ПодсветитьПоле(TextBox поле, bool ошибочное)
    {
        поле.BackColor = ошибочное
            ? GridAppearanceManager.ЦветПодсказки(Picket302.Domain.Contracts.GridAppearance.Default)
            : SystemColors.Window;
    }

    /// <summary>
    /// Переносит значения формы в карточку контрагента.
    /// </summary>
    /// <returns>Заполненная карточка либо <see langword="null"/>, если адреса некорректны.</returns>
    private Counterparty? СобратьКарточку()
    {
        _контрагент.Type = _типФизическое.Checked
            ? CounterpartyType.NaturalPerson
            : CounterpartyType.LegalEntity;

        _контрагент.Name = _наименование.Text.Trim();
        _контрагент.ShortName = _сокращённое.Text.Trim();
        _контрагент.Inn = _инн.Text.Trim();
        _контрагент.Kpp = _кпп.Text.Trim();
        _контрагент.Ogrn = _огрн.Text.Trim();
        _контрагент.Okfs = _окфс.Text.Trim();
        _контрагент.Okpo = _окпо.Text.Trim();
        _контрагент.Okved = _оквед.Text.Trim();
        _контрагент.Phone = _телефон.Text.Trim();
        _контрагент.Email = _почта.Text.Trim();
        _контрагент.SpecialNotes = _примечание.Text.Trim();

        _контрагент.Bik = _бик.Text.Trim();
        _контрагент.Ks = _корсчет.Text.Trim();
        _контрагент.Rs = _счет.Text.Trim();
        _контрагент.BankName = _наименованиеБанка.Text.Trim();

        _контрагент.PassportSeries = _серияПаспорта.Text.Trim();
        _контрагент.PassportNumber = _номерПаспорта.Text.Trim();
        _контрагент.PassportIssuedBy = _кемВыдан.Text.Trim();
        _контрагент.PassportIssuedDate = _датаВыдачи.Checked ? _датаВыдачи.Value.Date : null;
        _контрагент.BirthDate = _датаРождения.Checked ? _датаРождения.Value.Date : null;
        _контрагент.BirthPlace = _местоРождения.Text.Trim();
        _контрагент.Position = _должность.Text.Trim();
        _контрагент.DirectorFio = _директор.Text.Trim();

        var основной = _юридическийАдрес.ПереключательОсновного.Checked
            ? AddressMainType.Legal
            : _фактическийАдрес.ПереключательОсновного.Checked
                ? AddressMainType.Actual
                : AddressMainType.Postal;

        foreach (var (редактор, тип) in new[]
        {
            (_юридическийАдрес, AddressMainType.Legal),
            (_фактическийАдрес, AddressMainType.Actual),
            (_почтовыйАдрес, AddressMainType.Postal),
        })
        {
            var адрес = редактор.ПолучитьАдрес();

            if (адрес is null && редактор.ТекстОшибки is not null)
            {
                MessageBox.Show(
                    this,
                    редактор.ТекстОшибки,
                    "Адрес",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            _контрагент.Addresses.SetAddress(тип, адрес, тип == основной);
        }

        if (основной == AddressMainType.Postal
            && !_почтовыйАдрес.ПереключательОсновного.Checked
            && !_юридическийАдрес.ПереключательОсновного.Checked
            && !_фактическийАдрес.ПереключательОсновного.Checked)
        {
            _контрагент.Addresses.SetAddress(AddressMainType.Postal, _почтовыйАдрес.ПолучитьАдрес(), true);
        }

        ПользовательскиеПоля = СобратьПользовательскиеПоля();
        return _контрагент;
    }

    /// <summary>
    /// Собирает пользовательские поля из таблицы формы.
    /// </summary>
    /// <returns>Список значений пользовательских полей.</returns>
    private IReadOnlyList<(string Имя, string Значение)> СобратьПользовательскиеПоля()
    {
        var поля = new List<(string Имя, string Значение)>();

        foreach (DataGridViewRow строка in _пользовательскиеПоля.Rows)
        {
            if (строка.IsNewRow)
            {
                continue;
            }

            var имя = (строка.Cells[0].Value as string ?? string.Empty).Trim();

            if (имя.Length == 0)
            {
                continue;
            }

            поля.Add((имя, (строка.Cells[1].Value as string ?? string.Empty).Trim()));
        }

        return поля;
    }

    /// <summary>
    /// Сохраняет карточку и закрывает форму.
    /// </summary>
    private void Сохранить()
    {
        var карточка = СобратьКарточку();

        if (карточка is null)
        {
            return;
        }

        Результат = карточка;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <inheritdoc />
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        ОбновитьСостояние();
    }
}
