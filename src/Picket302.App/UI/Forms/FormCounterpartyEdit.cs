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
    private readonly TextBox _примечание = new()
    {
        Multiline = true,
        Height = 60,
    };

    /// <summary>
    /// Источник данных контрагента: «Импорт &lt;формат&gt; от &lt;дата&gt; из
    /// &lt;файл&gt;» (ТЗ §8.12, функция 75). Поле только для чтения.
    /// </summary>
    private readonly TextBox _источник = new() { ReadOnly = true };

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

    // Заголовки адресных блоков — обычными буквами: капитель допускается
    // только для технических обозначений (ИНН, БИК, КПП) и заголовков
    // столбцов таблиц (дизайн-контракт §2, правило C4).
    private readonly AddressEditor _юридическийАдрес = new("Юридический адрес:");
    private readonly AddressEditor _фактическийАдрес = new("Фактический адрес:");
    private readonly AddressEditor _почтовыйАдрес = new("Почтовый адрес:");

    /// <summary>
    /// Адаптивная сетка трёх адресных блоков вкладки «Реквизиты»: две колонки
    /// на широком клиенте, одна — при недостаточной ширине (редизайн
    /// 2026-11-08: конфликт DockStyle.Top и ручного Location устранён).
    /// </summary>
    private readonly TableLayoutPanel _адреснаяСетка = new();

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

        _вкладки.AccessibleName = "Разделы карточки контрагента";
        _страницаРеквизитов.AccessibleName = "Реквизиты контрагента";
        _страницаБанка.AccessibleName = "Банковские реквизиты контрагента";
        _страницаПаспорта.AccessibleName = "Паспортные данные контрагента";
        _типЮридическое.AccessibleName = "Юридическое лицо";
        _типЮридическое.AccessibleDescription = "Выбрать тип контрагента: юридическое лицо.";
        _типФизическое.AccessibleName = "Физическое лицо";
        _типФизическое.AccessibleDescription = "Выбрать тип контрагента: физическое лицо.";
        _адреснаяСетка.AccessibleName = "Адреса контрагента";
        _пользовательскиеПоля.AccessibleName = "Пользовательские поля контрагента";
        _пользовательскиеПоля.AccessibleDescription = "Таблица дополнительных полей, доступных в шаблонах бланков.";

        Text = _создание ? "Новый контрагент" : "Карточка контрагента";
        Font = new Font("Segoe UI", 8f);
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

    /// <summary>
    /// Поле источника данных контрагента: «Импорт &lt;формат&gt; от &lt;дата&gt;
    /// из &lt;файл&gt;» (ТЗ §8.12, функция 75).
    /// </summary>
    public TextBox ПолеИсточник => _источник;

    /// <summary>Кнопка «Сохранить».</summary>
    public Button КнопкаСохранить => _сохранить;

    /// <summary>Редактор юридического адреса.</summary>
    public AddressEditor РедакторЮридическогоАдреса => _юридическийАдрес;

    /// <summary>Редактор фактического адреса.</summary>
    public AddressEditor РедакторФактическогоАдреса => _фактическийАдрес;

    /// <summary>Редактор почтового адреса.</summary>
    public AddressEditor РедакторПочтовогоАдреса => _почтовыйАдрес;

    /// <summary>Таблица пользовательских полей («Поле», «Значение», «Тег»).</summary>
    public DataGridView ТаблицаПользовательскихПолей => _пользовательскиеПоля;

    /// <summary>Вкладка «Реквизиты» карточки.</summary>
    public TabPage СтраницаРеквизитов => _страницаРеквизитов;

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
    /// <remarks>
    /// <para>
    /// Компоновка (редизайн 2026-11-08): единая вертикальная сетка
    /// <see cref="TableLayoutPanel"/> внутри прокручиваемого хоста. Прежняя
    /// схема «DockStyle.Top редакторов адреса + ручные Location» конфликтовала:
    /// закрепление побеждало координаты, три блока складывались в столбец
    /// высотой 570 px и выталкивали панель пользовательских полей за
    /// клиентскую область (runtime-приёмка: адреса уходили до y≈927).
    /// </para>
    /// <para>
    /// Кнопки «Сохранить»/«Отмена» закреплены на самой форме
    /// (<see cref="ПостроитьКнопки"/>) и от прокрутки вкладки не зависят.
    /// </para>
    /// </remarks>
    private void ПостроитьРеквизиты()
    {
        var панельТипа = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _типЮридическое.AutoSize = true;
        _типФизическое.AutoSize = true;
        _типЮридическое.Checked = true;

        панельТипа.Controls.Add(_типЮридическое);
        панельТипа.Controls.Add(_типФизическое);

        var реквизиты = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 12,
        };

        // Ширина колонки подписей с запасом (регрессия 2026-11-08):
        // «Наименование:» не переносится, «Сокращённое:» не обрезается,
        // «Эл. почта:» видно целиком и на увеличенном системном масштабе.
        реквизиты.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        реквизиты.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        реквизиты.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        реквизиты.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        for (var строка = 0; строка < 11; строка++)
        {
            реквизиты.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        // Строка многострочного примечания — фиксированной высоты: AutoSize
        // измеряет многострочный TextBox по однострочному, и поле вытекало
        // за нижнюю границу таблицы на 4 px (редизайн 2026-11-08).
        реквизиты.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

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
        ДобавитьПоле(реквизиты, 0, 10, "Источник:", _источник);

        // Примечание выводится на форме (F-E13): прежде поле создавалось,
        // но в сетку реквизитов не попадало, и ввод не отображался.
        ДобавитьПоле(реквизиты, 0, 11, "Примечание:", _примечание);

        _адреснаяСетка.Dock = DockStyle.Fill;
        _адреснаяСетка.AutoSize = true;
        _адреснаяСетка.ColumnCount = 2;
        _адреснаяСетка.RowCount = 2;
        _адреснаяСетка.Padding = new Padding(0, 4, 0, 0);
        _адреснаяСетка.ColumnStyles.Clear();
        _адреснаяСетка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _адреснаяСетка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _адреснаяСетка.RowStyles.Clear();
        _адреснаяСетка.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _адреснаяСетка.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // Адресные редакторы добавляются один раз и остаются в дереве
        // контролов до и после показа формы. Перестроение через
        // Controls.Clear() из Resize приводило к состоянию, в котором
        // тестовый обход видел ноль AddressEditor.
        _адреснаяСетка.Controls.Add(_юридическийАдрес, 0, 0);
        _адреснаяСетка.Controls.Add(_фактическийАдрес, 1, 0);
        _адреснаяСетка.Controls.Add(_почтовыйАдрес, 0, 1);

        _пользовательскиеПоля.Dock = DockStyle.Fill;
        _пользовательскиеПоля.Font = new Font("Segoe UI", 8f);
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

        var панельПользовательских = new Panel
        {
            // Панель закреплена к НИЗУ ВКЛАДКИ (вне прокручиваемого хоста):
            // сетка пользовательских полей всегда видна внизу карточки,
            // независимо от прокрутки контента выше (редизайн 2026-11-08).
            // Внутри AutoScroll-хоста Dock=Bottom прокручивался бы вместе с
            // контентом и уходил за пределы видимой области.
            Dock = DockStyle.Bottom,
            Height = 170,
        };
        var заголовокПолей = new Label
        {
            Text = "Пользовательские поля (в макетах доступны по тегу вида [Custom_ИмяПоля]):",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        };

        панельПользовательских.Controls.Add(_пользовательскиеПоля);
        панельПользовательских.Controls.Add(заголовокПолей);

        // Прокручиваемый хост занимает вкладку над панелью пользовательских
        // полей: при высоте содержимого больше клиентской включается
        // прокрутка, и ни один блок не обрезается (редизайн 2026-11-08).
        var хостПрокрутки = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(2, 4, 2, 4),
        };

        var вертикаль = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
        };

        for (var строка = 0; строка < 3; строка++)
        {
            вертикаль.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        вертикаль.Controls.Add(панельТипа, 0, 0);
        вертикаль.Controls.Add(реквизиты, 0, 1);
        вертикаль.Controls.Add(_адреснаяСетка, 0, 2);

        хостПрокрутки.Controls.Add(вертикаль);

        // Порядок добавления = обратный порядок закрепления (WinForms
        // укладывает Dock с конца коллекции): панель пользовательских полей
        // закрепляется к низу вкладки первой, прокручиваемый хост занимает
        // остаток после.
        _страницаРеквизитов.Controls.Add(хостПрокрутки);
        _страницаРеквизитов.Controls.Add(панельПользовательских);

        // Вкладка содержит верхнюю часть реквизитов, два адресных блока в
        // первой строке и третий блок во второй. Задаём минимальную высоту
        // прокрутки после построения, чтобы пользовательские поля и адреса
        // оставались достижимыми даже до первого Layout-события.
        хостПрокрутки.AutoScrollMinSize = new Size(0, 760);
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

        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
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

        // «Место рождения:» и «Директор (ФИО):» — самые длинные подписи
        // вкладки: колонка с запасом, чтобы подписи не обрезались.
        панель.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
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

        // Доступные имена кнопок обязательны (дизайн-контракт §6, A1).
        _сохранить.AccessibleName = "Сохранить карточку контрагента";
        _сохранить.AccessibleDescription = "Сохраняет контрагента и закрывает карточку.";

        _отмена.Text = "Отмена";
        _отмена.Size = new Size(90, 24);
        _отмена.Location = new Point(104, 4);
        _отмена.DialogResult = DialogResult.Cancel;
        _отмена.AccessibleName = "Закрыть карточку контрагента без сохранения";

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
        поле.Font = new Font("Segoe UI", 8f);
        поле.Margin = new Padding(0, 0, 6, 2);
        поле.AccessibleName ??= текст.TrimEnd(':').Trim();
        поле.AccessibleDescription ??= $"Поле «{текст.TrimEnd(':').Trim()}» карточки контрагента.";

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
        _источник.Text = _контрагент.Source ?? string.Empty;

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
