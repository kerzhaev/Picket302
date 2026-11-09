using System.Drawing;
using System.Windows.Forms;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Мастер импорта адресной книги — семь экранов (ТЗ §8.12, функция 159:
/// «Мастер импорта адресной книги (7 экранов)»).
/// </summary>
/// <remarks>
/// <para>
/// Экраны мастера: выбор файла и листа книги; «Начать импорт с» и категория
/// адресной книги; сопоставление столбцов файла с полями; пошаговый
/// предпросмотр записей с правкой и знаком «&lt;&lt; &gt;&gt;»; фильтр
/// «Все записи / Записи с ошибками»; итоги проверки с тремя политиками
/// импорта; применение импорта и итоговые счётчики.
/// </para>
/// <para>
/// Форма не содержит собственной логики разбора: всё решение принимает
/// <see cref="ImportWizardController"/>, поэтому поведение мастера проверяется
/// модульными тестами без открытия окна.
/// </para>
/// </remarks>
public sealed class FormImportWizard : Form
{
    /// <summary>Названия полей адресной книги для сопоставления столбцов (шаг 3).</summary>
    private static readonly (ImportFieldSlot Слот, string Название)[] Поля =
    [
        (ImportFieldSlot.FullName, "ФИО"),
        (ImportFieldSlot.OrganizationName, "Название организации"),
        (ImportFieldSlot.PostalAddress, "Почтовый адрес"),
        (ImportFieldSlot.LegalAddress, "Юридический адрес"),
        (ImportFieldSlot.ActualAddress, "Фактический адрес"),
        (ImportFieldSlot.Other, "Прочее"),
        (ImportFieldSlot.Name, "Название"),
        (ImportFieldSlot.ShortName, "Сокращенное название"),
    ];

    /// <summary>Подпись столбца, не сопоставленного ни с одним полем.</summary>
    private const string БезСопоставления = "— не сопоставлять —";

    /// <summary>Кодировки, предлагаемые для разбора текстового файла.</summary>
    private static readonly (string Имя, string Подпись)[] Кодировки =
    [
        ("utf-8", "UTF-8"),
        ("windows-1251", "Windows-1251 (ANSI)"),
    ];

    /// <summary>Разделители столбцов, поддерживаемые программой.</summary>
    private static readonly (char Знак, string Подпись)[] Разделители =
    [
        (',', "Запятая (,)"),
        (';', "Точка с запятой (;)"),
        ('\t', "Табуляция"),
    ];

    /// <summary>
    /// Подпись элемента списка категорий, создающего новую категорию
    /// (ТЗ §8.12, функция 159, шаг 2: «выбор категории назначения или
    /// создание новой»).
    /// </summary>
    private const string СоздатьКатегорию = "Создать категорию…";

    private readonly ImportWizardController _контроллер;

    private readonly Label _заголовок = new();
    private readonly Label _описание = new();
    private readonly Label _состояние = new();
    private readonly Label _сведения = new();

    private readonly Panel _экранФайл = new();
    private readonly Panel _экранНачало = new();
    private readonly Panel _экранСопоставление = new();
    private readonly Panel _экранПредпросмотр = new();
    private readonly Panel _экранФильтр = new();
    private readonly Panel _экранИтоги = new();
    private readonly Panel _экранПрименение = new();

    private readonly TextBox _путьФайла = new() { ReadOnly = true };
    private readonly ComboBox _лист = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _кодировка = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _разделитель = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly NumericUpDown _началоСтроки = new() { Minimum = 1, Maximum = 100000, Value = 1 };
    private readonly ComboBox _категория = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _заголовки = new() { Text = "Первая строка файла — заголовки", Checked = true };

    private readonly DataGridView _сопоставление = new();

    private readonly Label _номерЗаписи = new();
    private readonly Label _статусЗаписи = new();
    private readonly Label _ошибкиЗаписи = new();
    private readonly Label _предупрежденияЗаписи = new();
    private readonly TextBox _имя = new();
    private readonly TextBox _сокращённое = new();
    private readonly TextBox _инн = new();
    private readonly TextBox _кпп = new();
    private readonly TextBox _огрн = new();
    private readonly TextBox _телефон = new();
    private readonly TextBox _почта = new();
    private readonly TextBox _примечание = new();
    private readonly TextBox _индекс = new() { MaxLength = CounterpartyDraft.MaxAddressLineLength };
    private readonly TextBox[] _адресныеСтроки = new TextBox[CounterpartyDraft.AddressLineCount];

    private readonly RadioButton _показыватьВсе = new() { Text = "Все записи", Checked = true };
    private readonly RadioButton _показыватьОшибки = new() { Text = "Записи с ошибками" };
    private readonly Label _счётчикФильтра = new();

    private readonly Label _итогиПроверки = new();
    private readonly RadioButton _политикаПроверенных = new() { Text = "Добавить только проверенных", Checked = true };
    private readonly RadioButton _политикаБезОшибок = new() { Text = "Добавить только записи без ошибок" };
    private readonly RadioButton _политикаВсе = new() { Text = "Добавить все записи" };
    private readonly RadioButton _слияниеПропустить = new() { Text = "Пропустить существующие", Checked = true };
    private readonly RadioButton _слияниеДополнить = new() { Text = "Дополнить пустые поля" };
    private readonly RadioButton _слияниеПерезаписать = new() { Text = "Перезаписать существующие" };

    private readonly Label _итогИмпорта = new();

    private readonly Button _назад = new();
    private readonly Button _далее = new();
    private readonly Button _отмена = new();

    private bool _обновление;

    /// <summary>
    /// Создаёт мастер импорта адресной книги.
    /// </summary>
    /// <param name="контроллер">
    /// Контроллер мастера; при отсутствии создаётся контроллер без хранилища.
    /// </param>
    public FormImportWizard(ImportWizardController? контроллер = null)
    {
        _контроллер = контроллер ?? new ImportWizardController();

        Text = "Мастер импорта адресной книги";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(700, 540);

        ПостроитьШапку();
        ПостроитьЭкранФайла();
        ПостроитьЭкранНачала();
        ПостроитьЭкранСопоставления();
        ПостроитьЭкранПредпросмотра();
        ПостроитьЭкранФильтра();
        ПостроитьЭкранИтогов();
        ПостроитьЭкранПрименения();
        ПостроитьКнопки();

        ПоказатьШаг();
    }

    /// <summary>Контроллер мастера импорта.</summary>
    public ImportWizardController Контроллер => _контроллер;

    /// <summary>Текущий экран мастера.</summary>
    public ImportWizardStep Шаг => _контроллер.CurrentStep;

    /// <summary>Заголовок текущего экрана мастера.</summary>
    public string ЗаголовокШага => _заголовок.Text;

    /// <summary>
    /// Пункты списка категорий на шаге «Начало импорта и категория»:
    /// точка доступа автоматических проверок (F-E4).
    /// </summary>
    public IReadOnlyList<object> ПунктыКатегорий => _категория.Items
        .Cast<object>()
        .ToList();

    /// <summary>
    /// Подписи трёх политик импорта финального экрана — их набор зафиксирован
    /// ТЗ и не расширяется.
    /// </summary>
    public IReadOnlyList<string> ПолитикиИмпорта =>
        [_политикаПроверенных.Text, _политикаБезОшибок.Text, _политикаВсе.Text];

    /// <summary>
    /// Число записей, показанных на экране предпросмотра.
    /// </summary>
    public int ЧислоПоказанныхЗаписей => _контроллер.FilteredRecords.Count;

    /// <summary>
    /// Текст итогов проверки, показанный на финальном экране: числа
    /// непроверенных и ошибочных записей.
    /// </summary>
    public string ИтогиПроверки => _итогиПроверки.Text;

    /// <summary>
    /// Текст предупреждений текущей записи предпросмотра, в том числе
    /// сообщения об усечении значений длиннее 255 символов.
    /// </summary>
    public string ПредупрежденияПредпросмотра => _предупрежденияЗаписи.Text;

    /// <summary>
    /// Текст ошибок текущей записи предпросмотра.
    /// </summary>
    public string ОшибкиПредпросмотра => _ошибкиЗаписи.Text;

    /// <summary>
    /// Выбирает файл импорта и читает его содержимое.
    /// </summary>
    /// <param name="путь">Путь к файлу импорта.</param>
    /// <param name="лист">Имя листа книги для файлов XLSX.</param>
    /// <returns><see langword="true"/> — файл прочитан.</returns>
    public bool ВыбратьФайл(string путь, string? лист = null)
    {
        var удалось = _контроллер.LoadFile(путь, лист);

        ОбновитьСведения();
        ПоказатьШаг();

        return удалось;
    }

    /// <summary>
    /// Задаёт параметры разбора файла: разделитель, кодировку, строку начала
    /// импорта и наличие строки заголовков (шаги 1–2 мастера).
    /// </summary>
    /// <param name="разделитель">Разделитель столбцов.</param>
    /// <param name="кодировка">Имя кодировки файла.</param>
    /// <param name="началоСтроки">Номер строки файла, с которой начинается импорт.</param>
    /// <param name="заголовки">Признак наличия строки заголовков.</param>
    public void УстановитьПараметрыРазбора(
        char разделитель,
        string кодировка,
        int началоСтроки,
        bool заголовки)
    {
        _разделитель.SelectedIndex = НайтиРазделитель(разделитель);
        _кодировка.SelectedIndex = НайтиКодировку(кодировка);
        _началоСтроки.Value = Math.Max(1, началоСтроки);
        _заголовки.Checked = заголовки;

        СохранитьНастройкиШага();
    }

    /// <summary>
    /// Переходит к следующей записи пошагового предпросмотра (знак
    /// «&gt;&gt;», ТЗ §8.12, функция 159, шаг 4).
    /// </summary>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool ПерейтиКСледующейЗаписи()
    {
        var переход = _контроллер.MoveNextRecord();

        ПоказатьШаг();

        return переход;
    }

    /// <summary>
    /// Возвращается к предыдущей записи пошагового предпросмотра (знак
    /// «&lt;&lt;», ТЗ §8.12, функция 159, шаг 4).
    /// </summary>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool ПерейтиКПредыдущейЗаписи()
    {
        var переход = _контроллер.MovePreviousRecord();

        ПоказатьШаг();

        return переход;
    }

    /// <summary>
    /// Сопоставляет столбец файла с полем адресной книги (шаг 3 мастера).
    /// </summary>
    /// <param name="индекс">Номер столбца файла.</param>
    /// <param name="слот">Поле адресной книги; <see langword="null"/> — не сопоставлять.</param>
    public void СопоставитьСтолбец(int индекс, ImportFieldSlot? слот)
    {
        _контроллер.SetSlot(индекс, слот);

        if (индекс >= 0 && индекс < _сопоставление.Rows.Count)
        {
            _сопоставление.Rows[индекс].Cells[1].Value = НазваниеПоля(слот);
        }
    }

    /// <summary>
    /// Переносит значения полей предпросмотра в текущую запись и
    /// перепроверяет её.
    /// </summary>
    /// <returns>Результат применения правки.</returns>
    public ImportEditResult ПрименитьПравку()
    {
        var результат = _контроллер.ApplyCurrentEdit(СобратьЧерновик());

        ПоказатьШаг();

        return результат;
    }

    /// <summary>
    /// Отмечает текущую запись как проверенную оператором.
    /// </summary>
    /// <returns><see langword="true"/> — запись отмечена.</returns>
    public bool ОтметитьПроверенной()
    {
        var отмечено = _контроллер.MarkCurrentVerified();

        ПоказатьШаг();

        return отмечено;
    }

    /// <summary>
    /// Пропускает текущую запись знаком «&lt;&lt; &gt;&gt;».
    /// </summary>
    /// <returns><see langword="true"/> — запись пропущена.</returns>
    public bool ПропуститьЗапись()
    {
        var пропущена = _контроллер.MarkCurrentSkipped();

        ПоказатьШаг();

        return пропущена;
    }

    /// <summary>
    /// Возвращает мастер к экрану редактирования записей, сохраняя сессию
    /// импорта (четвёртый выход финального экрана).
    /// </summary>
    /// <returns><see langword="true"/> — возврат выполнен.</returns>
    public bool ВернутьсяКРедактированию()
    {
        var возвращено = _контроллер.ReturnToEditing();

        ПоказатьШаг();

        return возвращено;
    }

    /// <summary>
    /// Применяет импорт по выбранной политике.
    /// </summary>
    /// <returns>Итог импорта либо <see langword="null"/> — импорт не применён.</returns>
    public ImportResult? ВыполнитьИмпорт()
    {
        СохранитьПолитики();

        var итог = _контроллер.Commit();

        ОбновитьИтогИмпорта(итог);
        ПоказатьШаг();

        return итог;
    }

    /// <summary>
    /// Переходит к следующему экрану мастера.
    /// </summary>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool ПерейтиВперёд()
    {
        СохранитьНастройкиШага();

        if (!_контроллер.GoNext())
        {
            ОбновитьСостояние();
            ПоказатьШаг();

            return false;
        }

        ПоказатьШаг();

        return true;
    }

    /// <summary>
    /// Возвращается к предыдущему экрану мастера.
    /// </summary>
    /// <returns><see langword="true"/> — переход выполнен.</returns>
    public bool ВернутьсяНазад()
    {
        var возвращено = _контроллер.GoBack();

        ПоказатьШаг();

        return возвращено;
    }

    private void ПостроитьШапку()
    {
        _заголовок.Location = new Point(12, 10);
        _заголовок.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _заголовок.AutoSize = true;

        _описание.Location = new Point(12, 32);
        _описание.Size = new Size(660, 40);
        _описание.ForeColor = SystemColors.GrayText;

        _состояние.Location = new Point(12, 470);
        _состояние.Size = new Size(660, 34);
        _состояние.ForeColor = Color.Firebrick;

        _сведения.Location = new Point(12, 502);
        _сведения.Size = new Size(660, 30);
        _сведения.ForeColor = SystemColors.GrayText;

        Controls.Add(_заголовок);
        Controls.Add(_описание);
        Controls.Add(_состояние);
        Controls.Add(_сведения);
    }

    private void ПостроитьЭкранФайла()
    {
        _экранФайл.Location = new Point(10, 76);
        _экранФайл.Size = new Size(676, 380);

        var меткаФайла = new Label { Text = "Файл импорта:", AutoSize = true, Location = new Point(12, 26) };
        _путьФайла.Location = new Point(120, 23);
        _путьФайла.Width = 420;

        var обзор = new Button
        {
            Text = "Выбрать файл…",
            AccessibleName = "Выбрать файл импорта",
            AccessibleDescription = "Открыть выбор файла для импорта адресной книги.",
            Location = new Point(548, 22),
            Size = new Size(112, 23),
        };
        обзор.Click += (_, _) => ВыбратьФайлДиалогом();

        var меткаЛиста = new Label { Text = "Лист книги:", AutoSize = true, Location = new Point(12, 60) };
        _лист.Location = new Point(120, 57);
        _лист.Width = 240;
        _лист.SelectedIndexChanged += (_, _) => СменитьЛист();

        var меткаКодировки = new Label { Text = "Кодировка:", AutoSize = true, Location = new Point(12, 94) };
        _кодировка.Location = new Point(120, 91);
        _кодировка.Width = 180;

        foreach (var (имя, подпись) in Кодировки)
        {
            _кодировка.Items.Add(подпись);
        }

        _кодировка.SelectedIndex = 0;
        _кодировка.SelectedIndexChanged += (_, _) => СохранитьНастройкиШага();

        var меткаРазделителя = new Label { Text = "Разделитель:", AutoSize = true, Location = new Point(320, 94) };
        _разделитель.Location = new Point(400, 91);
        _разделитель.Width = 180;

        foreach (var (_, подпись) in Разделители)
        {
            _разделитель.Items.Add(подпись);
        }

        _разделитель.SelectedIndex = 0;
        _разделитель.SelectedIndexChanged += (_, _) => СохранитьНастройкиШага();

        var сохранениеПрофиля = new Button
        {
            Text = "Сохранить профиль…",
            AccessibleName = "Сохранить профиль импорта",
            AccessibleDescription = "Сохранить параметры текущего шага импорта в файл.",
            Location = new Point(12, 128),
            Size = new Size(140, 25),
        };
        сохранениеПрофиля.Click += (_, _) => СохранитьПрофиль();

        var загрузкаПрофиля = new Button
        {
            Text = "Загрузить профиль…",
            AccessibleName = "Загрузить профиль импорта",
            AccessibleDescription = "Загрузить параметры шага импорта из файла.",
            Location = new Point(160, 128),
            Size = new Size(140, 25),
        };
        загрузкаПрофиля.Click += (_, _) => ЗагрузитьПрофиль();

        var параметрыCsv = new Button
        {
            Text = "Параметры CSV…",
            AccessibleName = "Параметры CSV",
            AccessibleDescription = "Открыть настройки разбора CSV-файла.",
            Location = new Point(308, 128),
            Size = new Size(140, 25),
        };
        параметрыCsv.Click += (_, _) => ОткрытьПараметрыCsv();

        var пояснение = new Label
        {
            Text = "Форматы файлов: CSV, TXT, XLSX, HTML. При ошибке чтения XLSX "
                + "программа предлагает выгрузку в CSV или HTML.",
            Location = new Point(12, 166),
            Size = new Size(650, 32),
            ForeColor = SystemColors.GrayText,
        };

        _экранФайл.Controls.Add(меткаФайла);
        _экранФайл.Controls.Add(_путьФайла);
        _экранФайл.Controls.Add(обзор);
        _экранФайл.Controls.Add(меткаЛиста);
        _экранФайл.Controls.Add(_лист);
        _экранФайл.Controls.Add(меткаКодировки);
        _экранФайл.Controls.Add(_кодировка);
        _экранФайл.Controls.Add(меткаРазделителя);
        _экранФайл.Controls.Add(_разделитель);
        _экранФайл.Controls.Add(сохранениеПрофиля);
        _экранФайл.Controls.Add(загрузкаПрофиля);
        _экранФайл.Controls.Add(параметрыCsv);
        _экранФайл.Controls.Add(пояснение);

        Controls.Add(_экранФайл);
    }

    private void ПостроитьЭкранНачала()
    {
        _экранНачало.Location = new Point(10, 76);
        _экранНачало.Size = new Size(676, 380);

        var меткаНачала = new Label
        {
            Text = "Начать импорт с (номер строки файла):",
            AutoSize = true,
            Location = new Point(12, 26),
        };
        _началоСтроки.Location = new Point(240, 23);
        _началоСтроки.Width = 100;
        _началоСтроки.ValueChanged += (_, _) => СохранитьНастройкиШага();

        var меткаКатегории = new Label
        {
            Text = "Категория адресной книги:",
            AutoSize = true,
            Location = new Point(12, 62),
        };
        _категория.Location = new Point(240, 59);
        _категория.Width = 320;
        _категория.SelectedIndexChanged += (_, _) => ОбработатьВыборКатегории();

        _заголовки.Location = new Point(12, 98);
        _заголовки.AutoSize = true;
        _заголовки.CheckedChanged += (_, _) => СохранитьНастройкиШага();

        var пояснение = new Label
        {
            Text = "Номер строки указывается с учётом строки заголовков: если заголовки "
                + "есть, данные начинаются со следующей строки.",
            Location = new Point(12, 132),
            Size = new Size(650, 32),
            ForeColor = SystemColors.GrayText,
        };

        _экранНачало.Controls.Add(меткаНачала);
        _экранНачало.Controls.Add(_началоСтроки);
        _экранНачало.Controls.Add(меткаКатегории);
        _экранНачало.Controls.Add(_категория);
        _экранНачало.Controls.Add(_заголовки);
        _экранНачало.Controls.Add(пояснение);

        Controls.Add(_экранНачало);
    }

    private void ПостроитьЭкранСопоставления()
    {
        _экранСопоставление.Location = new Point(10, 76);
        _экранСопоставление.Size = new Size(676, 380);

        var пояснение = new Label
        {
            Text = "Каждый столбец файла сопоставляется ровно с одним полем адресной книги.",
            Location = new Point(12, 6),
            Size = new Size(650, 20),
            ForeColor = SystemColors.GrayText,
        };

        _сопоставление.Location = new Point(12, 30);
        _сопоставление.Size = new Size(650, 320);
        _сопоставление.AllowUserToAddRows = false;
        _сопоставление.AllowUserToDeleteRows = false;
        _сопоставление.ReadOnly = false;
        _сопоставление.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _сопоставление.AutoGenerateColumns = false;
        _сопоставление.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

        var столбецИмени = new DataGridViewTextBoxColumn
        {
            HeaderText = "Столбец файла",
            ReadOnly = true,
            Width = 380,
        };
        var столбецПоля = new DataGridViewComboBoxColumn
        {
            HeaderText = "Поле адресной книги",
            Width = 250,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
        };
        столбецПоля.Items.Add(БезСопоставления);

        foreach (var (_, название) in Поля)
        {
            столбецПоля.Items.Add(название);
        }

        _сопоставление.Columns.Add(столбецИмени);
        _сопоставление.Columns.Add(столбецПоля);
        _сопоставление.CellValueChanged += (_, _) => СохранитьСопоставление();

        _экранСопоставление.Controls.Add(пояснение);
        _экранСопоставление.Controls.Add(_сопоставление);

        Controls.Add(_экранСопоставление);
    }

    private void ПостроитьЭкранПредпросмотра()
    {
        _экранПредпросмотр.Location = new Point(10, 76);
        _экранПредпросмотр.Size = new Size(676, 380);

        _номерЗаписи.Location = new Point(12, 8);
        _номерЗаписи.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        _номерЗаписи.AutoSize = true;

        _статусЗаписи.Location = new Point(300, 8);
        _статусЗаписи.AutoSize = true;

        _ошибкиЗаписи.Location = new Point(12, 30);
        _ошибкиЗаписи.Size = new Size(650, 20);
        _ошибкиЗаписи.ForeColor = Color.Firebrick;

        _предупрежденияЗаписи.Location = new Point(12, 50);
        _предупрежденияЗаписи.Size = new Size(650, 20);
        _предупрежденияЗаписи.ForeColor = Color.DarkOrange;

        var поля = new TableLayoutPanel
        {
            Location = new Point(12, 70),
            Size = new Size(650, 236),
            ColumnCount = 4,
            RowCount = 10,
        };
        поля.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        поля.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        поля.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        поля.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var пары = new (string Подпись, TextBox Поле)[]
        {
            ("Наименование / ФИО:", _имя),
            ("Сокращённое название:", _сокращённое),
            ("ИНН / УНП:", _инн),
            ("КПП:", _кпп),
            ("ОГРН / ОГРНИП:", _огрн),
            ("Телефон:", _телефон),
            ("E-mail:", _почта),
            ("Примечание:", _примечание),
            ("Почтовый индекс:", _индекс),
        };

        var ряд = 0;

        foreach (var (подпись, поле) in пары)
        {
            поля.Controls.Add(new Label { Text = подпись, AutoSize = true, Anchor = AnchorStyles.Left }, 0, ряд);
            поля.Controls.Add(поле, 1, ряд);
            ряд++;
        }

        var меткаАдреса = new Label { Text = "Адрес (до 6 строк):", AutoSize = true, Anchor = AnchorStyles.Left };
        поля.Controls.Add(меткаАдреса, 0, ряд);

        var адреса = new FlowLayoutPanel
        {
            Location = new Point(0, 0),
            Size = new Size(480, 190),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };

        for (var номер = 0; номер < _адресныеСтроки.Length; номер++)
        {
            var строка = new TextBox
            {
                MaxLength = CounterpartyDraft.MaxAddressLineLength,
                Width = 460,
            };

            _адресныеСтроки[номер] = строка;
            адреса.Controls.Add(строка);
        }

        поля.Controls.Add(адреса, 1, ряд);
        поля.SetColumnSpan(адреса, 3);

        var назадЗапись = new Button
        {
            Text = "<< Назад",
            AccessibleName = "Назад к предыдущей записи",
            AccessibleDescription = "Перейти к предыдущей записи предпросмотра.",
            Location = new Point(12, 320),
            Size = new Size(100, 26),
        };
        назадЗапись.Click += (_, _) =>
        {
            _контроллер.MovePreviousRecord();
            ПоказатьШаг();
        };

        var пропустить = new Button
        {
            Text = "Пропустить",
            AccessibleName = "Пропустить запись",
            AccessibleDescription = "Пропустить текущую запись импорта.",
            Location = new Point(120, 320),
            Size = new Size(100, 26),
        };
        пропустить.Click += (_, _) => ПропуститьЗапись();

        var проверить = new Button
        {
            Text = "Отметить проверенным",
            AccessibleName = "Отметить запись проверенной",
            AccessibleDescription = "Пометить текущую запись как проверенную.",
            Location = new Point(228, 320),
            Size = new Size(160, 26),
        };
        проверить.Click += (_, _) => ОтметитьПроверенной();

        var снять = new Button
        {
            Text = "Снять отметку",
            AccessibleName = "Снять отметку проверки",
            AccessibleDescription = "Снять отметку проверки с текущей записи.",
            Location = new Point(396, 320),
            Size = new Size(120, 26),
        };
        снять.Click += (_, _) =>
        {
            _контроллер.MarkCurrentUnverified();
            ПоказатьШаг();
        };

        var сохранить = new Button
        {
            Text = "Сохранить правку",
            AccessibleName = "Сохранить правку записи",
            AccessibleDescription = "Сохранить изменения текущей записи.",
            Location = new Point(524, 320),
            Size = new Size(140, 26),
        };
        сохранить.Click += (_, _) => ПрименитьПравку();

        var следующая = new Button
        {
            Text = "Следующая запись >>",
            AccessibleName = "Следующая запись",
            AccessibleDescription = "Перейти к следующей записи предпросмотра.",
            Location = new Point(524, 286),
            Size = new Size(140, 26),
        };
        следующая.Click += (_, _) =>
        {
            _контроллер.MoveNextRecord();
            ПоказатьШаг();
        };

        _экранПредпросмотр.Controls.Add(_номерЗаписи);
        _экранПредпросмотр.Controls.Add(_статусЗаписи);
        _экранПредпросмотр.Controls.Add(_ошибкиЗаписи);
        _экранПредпросмотр.Controls.Add(_предупрежденияЗаписи);
        _экранПредпросмотр.Controls.Add(поля);
        _экранПредпросмотр.Controls.Add(назадЗапись);
        _экранПредпросмотр.Controls.Add(пропустить);
        _экранПредпросмотр.Controls.Add(проверить);
        _экранПредпросмотр.Controls.Add(снять);
        _экранПредпросмотр.Controls.Add(сохранить);
        _экранПредпросмотр.Controls.Add(следующая);

        Controls.Add(_экранПредпросмотр);
    }

    private void ПостроитьЭкранФильтра()
    {
        _экранФильтр.Location = new Point(10, 76);
        _экранФильтр.Size = new Size(676, 380);

        var надпись = new Label
        {
            Text = "Показывать:",
            AutoSize = true,
            Location = new Point(12, 26),
        };

        _показыватьВсе.Location = new Point(120, 23);
        _показыватьВсе.AutoSize = true;
        _показыватьВсе.CheckedChanged += (_, _) => СменитьФильтр();

        _показыватьОшибки.Location = new Point(120, 52);
        _показыватьОшибки.AutoSize = true;
        _показыватьОшибки.CheckedChanged += (_, _) => СменитьФильтр();

        _счётчикФильтра.Location = new Point(12, 92);
        _счётчикФильтра.AutoSize = true;
        _счётчикФильтра.ForeColor = SystemColors.GrayText;

        _экранФильтр.Controls.Add(надпись);
        _экранФильтр.Controls.Add(_показыватьВсе);
        _экранФильтр.Controls.Add(_показыватьОшибки);
        _экранФильтр.Controls.Add(_счётчикФильтра);

        Controls.Add(_экранФильтр);
    }

    private void ПостроитьЭкранИтогов()
    {
        _экранИтоги.Location = new Point(10, 76);
        _экранИтоги.Size = new Size(676, 380);

        _итогиПроверки.Location = new Point(12, 8);
        _итогиПроверки.Size = new Size(650, 90);
        _итогиПроверки.ForeColor = SystemColors.GrayText;

        var группаПолитик = new GroupBox
        {
            Text = "Политика импорта",
            Location = new Point(12, 104),
            Size = new Size(400, 130),
        };

        _политикаПроверенных.Location = new Point(12, 24);
        _политикаПроверенных.AutoSize = true;
        _политикаБезОшибок.Location = new Point(12, 50);
        _политикаБезОшибок.AutoSize = true;
        _политикаВсе.Location = new Point(12, 76);
        _политикаВсе.AutoSize = true;

        группаПолитик.Controls.Add(_политикаПроверенных);
        группаПолитик.Controls.Add(_политикаБезОшибок);
        группаПолитик.Controls.Add(_политикаВсе);

        var группаСлияния = new GroupBox
        {
            Text = "Политика слияния с существующей базой",
            Location = new Point(424, 104),
            Size = new Size(238, 130),
        };

        _слияниеПропустить.Location = new Point(12, 24);
        _слияниеПропустить.AutoSize = true;
        _слияниеДополнить.Location = new Point(12, 50);
        _слияниеДополнить.AutoSize = true;
        _слияниеПерезаписать.Location = new Point(12, 76);
        _слияниеПерезаписать.AutoSize = true;

        группаСлияния.Controls.Add(_слияниеПропустить);
        группаСлияния.Controls.Add(_слияниеДополнить);
        группаСлияния.Controls.Add(_слияниеПерезаписать);

        var вернуться = new Button
        {
            Text = "Вернуться к редактированию",
            AccessibleName = "Вернуться к редактированию",
            AccessibleDescription = "Вернуться к экрану правки импортируемых записей.",
            Location = new Point(12, 248),
            Size = new Size(200, 28),
        };
        вернуться.Click += (_, _) => ВернутьсяКРедактированию();

        _экранИтоги.Controls.Add(_итогиПроверки);
        _экранИтоги.Controls.Add(группаПолитик);
        _экранИтоги.Controls.Add(группаСлияния);
        _экранИтоги.Controls.Add(вернуться);

        Controls.Add(_экранИтоги);
    }

    private void ПостроитьЭкранПрименения()
    {
        _экранПрименение.Location = new Point(10, 76);
        _экранПрименение.Size = new Size(676, 380);

        var пояснение = new Label
        {
            Text = "Импорт выполняется по политике, выбранной на предыдущем экране.",
            Location = new Point(12, 12),
            Size = new Size(650, 20),
            ForeColor = SystemColors.GrayText,
        };

        var импортировать = new Button
        {
            Text = "Импортировать",
            AccessibleName = "Импортировать записи",
            AccessibleDescription = "Запустить импорт по выбранным политикам.",
            Location = new Point(12, 44),
            Size = new Size(160, 30),
        };
        импортировать.Click += (_, _) => ВыполнитьИмпорт();

        _итогИмпорта.Location = new Point(12, 90);
        _итогИмпорта.Size = new Size(650, 120);

        _экранПрименение.Controls.Add(пояснение);
        _экранПрименение.Controls.Add(импортировать);
        _экранПрименение.Controls.Add(_итогИмпорта);

        Controls.Add(_экранПрименение);
    }

    private void ПостроитьКнопки()
    {
        _назад.Text = "<< Назад";
        _назад.AccessibleName = "Назад";
        _назад.AccessibleDescription = "Вернуться к предыдущему шагу мастера импорта.";
        _назад.Location = new Point(10, 500);
        _назад.Size = new Size(100, 26);
        _назад.Click += (_, _) => ВернутьсяНазад();

        _далее.Text = "Далее >>";
        _далее.AccessibleName = "Далее";
        _далее.AccessibleDescription = "Перейти к следующему шагу мастера импорта.";
        _далее.Location = new Point(118, 500);
        _далее.Size = new Size(100, 26);
        _далее.Click += (_, _) => ПерейтиВперёд();

        _отмена.Text = "Отмена";
        _отмена.AccessibleName = "Отмена импорта";
        _отмена.AccessibleDescription = "Закрыть мастер без импорта записей.";
        _отмена.Location = new Point(586, 500);
        _отмена.Size = new Size(100, 26);
        _отмена.DialogResult = DialogResult.Cancel;
        _отмена.Click += (_, _) => Close();

        Controls.Add(_назад);
        Controls.Add(_далее);
        Controls.Add(_отмена);
    }

    private void ПоказатьШаг()
    {
        var шаг = _контроллер.CurrentStep;

        _обновление = true;

        _заголовок.Text = $"Шаг {_контроллер.StepNumber} из {_контроллер.TotalSteps}. {ImportWizardSteps.Title(шаг)}";
        _описание.Text = ImportWizardSteps.Description(шаг);

        _экранФайл.Visible = шаг == ImportWizardStep.File;
        _экранНачало.Visible = шаг == ImportWizardStep.StartRowAndGroup;
        _экранСопоставление.Visible = шаг == ImportWizardStep.ColumnMapping;
        _экранПредпросмотр.Visible = шаг == ImportWizardStep.RecordPreview;
        _экранФильтр.Visible = шаг == ImportWizardStep.ErrorFilter;
        _экранИтоги.Visible = шаг == ImportWizardStep.Summary;
        _экранПрименение.Visible = шаг == ImportWizardStep.Commit;

        _назад.Enabled = шаг != ImportWizardStep.File;
        _далее.Enabled = шаг != ImportWizardStep.Commit;
        _далее.Text = шаг == ImportWizardStep.Summary ? "Далее >>" : "Далее >>";

        switch (шаг)
        {
            case ImportWizardStep.File:
                ОбновитьСведения();
                break;

            case ImportWizardStep.StartRowAndGroup:
                // Принудительное обновление: шаг показывается под флагом
                // _обновление, и guard без force пропускал загрузку (F-E4).
                ОбновитьКатегории(force: true);
                break;

            case ImportWizardStep.ColumnMapping:
                ПостроитьСопоставление();
                break;

            case ImportWizardStep.RecordPreview:
                ОбновитьПредпросмотр();
                break;

            case ImportWizardStep.ErrorFilter:
                ОбновитьФильтр();
                break;

            case ImportWizardStep.Summary:
                ОбновитьИтоги();
                break;

            case ImportWizardStep.Commit:
                ОбновитьИтогИмпорта(_контроллер.Result);
                break;

            default:
                break;
        }

        ОбновитьСостояние();

        _обновление = false;
    }

    private void ОбновитьСостояние()
    {
        _состояние.Text = _контроллер.ErrorMessage ?? string.Empty;
    }

    private void ОбновитьСведения()
    {
        _путьФайла.Text = _контроллер.FilePath;

        var предупреждения = _контроллер.FileWarnings;

        _сведения.Text = _контроллер.HasSession
            ? $"Листов: {_контроллер.SheetCount}. Записей: {_контроллер.RecordCount}. Столбцов: {_контроллер.ColumnCount}."
            : "Файл импорта не выбран.";

        if (предупреждения.Count > 0)
        {
            _состояние.Text = string.Join(Environment.NewLine, предупреждения.Take(3));
        }
    }

    /// <summary>
    /// Загружает категории адресной книги в выбор шага «Начало и категория»
    /// (ТЗ §8.12, функция 159).
    /// </summary>
    /// <param name="force">Принудительное обновление при активном флаге
    /// <c>_обновление</c> (показ шага из <see cref="ПоказатьШаг"/>).</param>
    private async void ОбновитьКатегории(bool force = false)
    {
        if (_обновление && !force)
        {
            return;
        }

        var группы = await _контроллер.LoadCategoriesAsync().ConfigureAwait(true);

        // Принудительное обновление выполняется внутри показа шага: загрузка
        // нередко завершается синхронно, когда флаг _обновление ещё активен
        // (F-E4), поэтому пост-проверка на force не распространяется.
        if ((!force && _обновление) || IsDisposed)
        {
            return;
        }

        _категория.Items.Clear();
        _категория.Items.Add("(без категории)");
        _категория.Items.Add(СоздатьКатегорию);
        _категория.SelectedIndex = 0;

        foreach (var группа in группы)
        {
            _категория.Items.Add(группа);
        }

        _категория.SelectedIndex = НайтиИндексКатегории(_контроллер.GroupId);
    }

    /// <summary>
    /// Создаёт новую категорию адресной книги по введённому оператором
    /// наименованию и выбирает её (ТЗ §8.12, функция 159, шаг 2).
    /// </summary>
    /// <param name="название">Наименование новой категории.</param>
    /// <returns>Созданная категория либо <see langword="null"/>.</returns>
    public async Task<Group?> СоздатьКатегориюAsync(string название)
    {
        var группа = await _контроллер.CreateCategoryAsync(название).ConfigureAwait(true);

        if (группа is null)
        {
            return null;
        }

        _категория.Items.Add(группа);
        _категория.SelectedItem = группа;

        СохранитьНастройкиШага();

        return группа;
    }

    private int НайтиИндексКатегории(long? идентификатор)
    {
        if (идентификатор is null)
        {
            return 0;
        }

        for (var индекс = 0; индекс < _категория.Items.Count; индекс++)
        {
            if (_категория.Items[индекс] is Group группа && группа.Id == идентификатор)
            {
                return индекс;
            }
        }

        return 0;
    }

    private void ПостроитьСопоставление()
    {
        _сопоставление.Rows.Clear();

        var столбцы = _контроллер.ColumnNames;

        for (var индекс = 0; индекс < столбцы.Count; индекс++)
        {
            var номер = _сопоставление.Rows.Add(столбцы[индекс], НазваниеПоля(_контроллер.GetSlot(индекс)));
            _сопоставление.Rows[номер].Cells[0].ReadOnly = true;
        }
    }

    private string НазваниеПоля(ImportFieldSlot? слот)
    {
        if (слот is null)
        {
            return БезСопоставления;
        }

        foreach (var (значение, название) in Поля)
        {
            if (значение == слот)
            {
                return название;
            }
        }

        return БезСопоставления;
    }

    private ImportFieldSlot? СлотПоля(string название)
    {
        foreach (var (слот, текст) in Поля)
        {
            if (текст == название)
            {
                return слот;
            }
        }

        return null;
    }

    private void ОбновитьПредпросмотр()
    {
        var записи = _контроллер.FilteredRecords;
        var запись = _контроллер.CurrentRecord;

        _номерЗаписи.Text = записи.Count == 0
            ? "Нет записей для предпросмотра"
            : $"Запись {_контроллер.CurrentIndex} из {записи.Count} (строка файла {запись?.RowNumber ?? 0})";

        if (запись is null)
        {
            _статусЗаписи.Text = string.Empty;
            _ошибкиЗаписи.Text = string.Empty;
            _предупрежденияЗаписи.Text = string.Empty;

            ОчиститьПоляПравки();

            return;
        }

        _статусЗаписи.Text = ОписаниеСтатуса(запись.Status);

        _ошибкиЗаписи.Text = запись.Errors.Count == 0
            ? string.Empty
            : "Ошибки: " + string.Join("; ", запись.Errors);

        // Предупреждения (в том числе об усечении значений длиннее 255
        // символов) показываются оператору на шаге предпросмотра.
        _предупрежденияЗаписи.Text = запись.Warnings.Count == 0
            ? string.Empty
            : "Предупреждения: " + string.Join("; ", запись.Warnings);

        var черновик = запись.Draft;

        _имя.Text = черновик.Name;
        _сокращённое.Text = черновик.ShortName ?? string.Empty;
        _инн.Text = черновик.Inn ?? string.Empty;
        _кпп.Text = черновик.Kpp ?? string.Empty;
        _огрн.Text = черновик.Ogrn ?? string.Empty;
        _телефон.Text = черновик.Phone ?? string.Empty;
        _почта.Text = черновик.Email ?? string.Empty;
        _примечание.Text = черновик.Note ?? string.Empty;
        _индекс.Text = черновик.PostalZip;

        for (var номер = 0; номер < _адресныеСтроки.Length; номер++)
        {
            _адресныеСтроки[номер].Text = черновик.PostalLines[номер] ?? string.Empty;
        }
    }

    private static string ОписаниеСтатуса(ImportRecordStatus статус) => статус switch
    {
        ImportRecordStatus.Verified => "Запись проверена оператором",
        ImportRecordStatus.HasErrors => "Запись содержит ошибки",
        ImportRecordStatus.Skipped => "Запись пропущена",
        _ => "Запись не проверена",
    };

    private void ОчиститьПоляПравки()
    {
        _имя.Clear();
        _сокращённое.Clear();
        _инн.Clear();
        _кпп.Clear();
        _огрн.Clear();
        _телефон.Clear();
        _почта.Clear();
        _примечание.Clear();
        _индекс.Clear();

        foreach (var строка in _адресныеСтроки)
        {
            строка.Clear();
        }
    }

    private CounterpartyDraft СобратьЧерновик()
    {
        var черновик = _контроллер.CurrentRecord?.Draft.Clone() ?? new CounterpartyDraft();

        черновик.Name = _имя.Text;
        черновик.ShortName = _сокращённое.Text;
        черновик.Inn = _инн.Text;
        черновик.Kpp = _кпп.Text;
        черновик.Ogrn = _огрн.Text;
        черновик.Phone = _телефон.Text;
        черновик.Email = _почта.Text;
        черновик.Note = _примечание.Text;
        черновик.PostalZip = _индекс.Text;

        for (var номер = 0; номер < _адресныеСтроки.Length; номер++)
        {
            черновик.PostalLines[номер] = _адресныеСтроки[номер].Text;
        }

        return черновик;
    }

    private void ОбновитьФильтр()
    {
        _счётчикФильтра.Text = $"Показано записей: {_контроллер.FilteredRecords.Count} из {_контроллер.RecordCount}.";
    }

    private void ОбновитьИтоги()
    {
        var сводка = _контроллер.Summary;

        _итогиПроверки.Text =
            $"Всего записей: {сводка.TotalCount}." + Environment.NewLine +
            $"Проверено оператором: {сводка.VerifiedCount}." + Environment.NewLine +
            $"Непроверенных записей: {сводка.UnverifiedCount}." + Environment.NewLine +
            $"Записей с ошибками: {сводка.ErrorCount}." + Environment.NewLine +
            $"Пропущено записей: {сводка.SkippedCount}.";
    }

    private void ОбновитьИтогИмпорта(ImportResult? итог)
    {
        if (итог is null)
        {
            _итогИмпорта.Text = "Импорт ещё не выполнялся.";

            return;
        }

        _итогИмпорта.Text =
            $"Добавлено записей: {итог.Created}." + Environment.NewLine +
            $"Обновлено записей: {итог.Updated}." + Environment.NewLine +
            $"Пропущено записей: {итог.Skipped}." + Environment.NewLine +
            $"Конфликтов слияния: {итог.Conflicts}." + Environment.NewLine +
            $"Импортировано записей с ошибками: {итог.Errors}." + Environment.NewLine +
            $"Импортировано непроверенных записей: {итог.Unverified}.";
    }

    private void СохранитьНастройкиШага()
    {
        if (_обновление)
        {
            return;
        }

        switch (_контроллер.CurrentStep)
        {
            case ImportWizardStep.File:
                if (_кодировка.SelectedIndex >= 0)
                {
                    _контроллер.SetEncoding(Кодировки[_кодировка.SelectedIndex].Имя);
                }

                if (_разделитель.SelectedIndex >= 0)
                {
                    _контроллер.SetDelimiter(Разделители[_разделитель.SelectedIndex].Знак);
                }

                break;

            case ImportWizardStep.StartRowAndGroup:
                _контроллер.SetStartRow((int)_началоСтроки.Value);
                _контроллер.SetHasHeaderRow(_заголовки.Checked);
                _контроллер.SetGroupId(ИдентификаторКатегории());
                break;

            case ImportWizardStep.ErrorFilter:
                _контроллер.SetFilter(_показыватьОшибки.Checked
                    ? ImportRecordFilter.WithErrors
                    : ImportRecordFilter.All);
                break;

            default:
                break;
        }
    }

    private long? ИдентификаторКатегории()
    {
        if (_категория.SelectedIndex <= 0)
        {
            return null;
        }

        return _категория.SelectedItem is Group группа ? группа.Id : null;
    }

    /// <summary>
    /// Обрабатывает выбор категории: пункт «Создать категорию…» открывает
    /// ввод наименования новой категории (ТЗ §8.12, функция 159, шаг 2).
    /// </summary>
    private async void ОбработатьВыборКатегории()
    {
        if (_обновление)
        {
            return;
        }

        if (_категория.SelectedItem as string == СоздатьКатегорию)
        {
            var название = ВвестиНаименованиеКатегории();

            if (string.IsNullOrWhiteSpace(название))
            {
                _категория.SelectedIndex = НайтиИндексКатегории(_контроллер.GroupId);

                return;
            }

            var группа = await СоздатьКатегориюAsync(название).ConfigureAwait(true);

            if (группа is null)
            {
                _категория.SelectedIndex = НайтиИндексКатегории(_контроллер.GroupId);

                return;
            }

            _состояние.Text = $"Создана категория «{группа.Name}».";

            return;
        }

        СохранитьНастройкиШага();
    }

    /// <summary>
    /// Запрашивает наименование новой категории адресной книги.
    /// </summary>
    /// <returns>Введённое наименование либо пустая строка.</returns>
    private string ВвестиНаименованиеКатегории()
    {
        using var диалог = new Form
        {
            Text = "Новая категория адресной книги",
            Font = new Font("Segoe UI", 8f),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(380, 120),
        };

        var надпись = new Label { Text = "Наименование категории:", AutoSize = true, Location = new Point(12, 18) };
        var поле = new TextBox { Location = new Point(12, 40), Width = 350 };

        var создать = new Button
        {
            Text = "Создать",
            AccessibleName = "Создать категорию",
            AccessibleDescription = "Создать новую категорию адресной книги.",
            Location = new Point(172, 74),
            Size = new Size(90, 26),
        };
        var отмена = new Button
        {
            Text = "Отмена",
            AccessibleName = "Отменить создание категории",
            AccessibleDescription = "Закрыть диалог без создания категории.",
            Location = new Point(272, 74),
            Size = new Size(90, 26),
            DialogResult = DialogResult.Cancel,
        };

        создать.Click += (_, _) => диалог.DialogResult = DialogResult.OK;
        отмена.Click += (_, _) => диалог.Close();

        диалог.Controls.Add(надпись);
        диалог.Controls.Add(поле);
        диалог.Controls.Add(создать);
        диалог.Controls.Add(отмена);

        диалог.AcceptButton = создать;
        диалог.CancelButton = отмена;

        return диалог.ShowDialog(this) == DialogResult.OK
            ? поле.Text
            : string.Empty;
    }

    private void СохранитьСопоставление()
    {
        if (_обновление)
        {
            return;
        }

        for (var индекс = 0; индекс < _сопоставление.Rows.Count; индекс++)
        {
            var значение = (string?)_сопоставление.Rows[индекс].Cells[1].Value;

            _контроллер.SetSlot(индекс, СлотПоля(значение ?? string.Empty));
        }
    }

    /// <summary>
    /// Открывает диалог настройки разбора CSV с живым предпросмотром и
    /// применяет выбранные параметры к сессии импорта (ТЗ §8.5, функция 73).
    /// </summary>
    /// <returns>Применённый профиль либо <see langword="null"/>.</returns>
    public CsvProfile? ОткрытьПараметрыCsv()
    {
        if (string.IsNullOrWhiteSpace(_контроллер.FilePath) || !File.Exists(_контроллер.FilePath))
        {
            _состояние.Text = "Сначала выберите файл импорта.";

            return null;
        }

        using var диалог = new FormCsvImportConfig(null, _контроллер.BuildProfile());

        диалог.PreviewText = ПрочитатьТекстФайла(_контроллер.FilePath, _контроллер.BuildProfile().EncodingName);

        if (диалог.ShowDialog(this) != DialogResult.OK || диалог.Profile is null)
        {
            return null;
        }

        var профиль = диалог.Profile;

        _контроллер.SetEncoding(профиль.EncodingName);
        _контроллер.SetDelimiter(профиль.Delimiter);
        _контроллер.SetHasHeaderRow(профиль.HasHeaderRow);
        _контроллер.SetStartRow(Math.Max(1, профиль.FirstDataRow));

        _кодировка.SelectedIndex = НайтиКодировку(профиль.EncodingName);
        _разделитель.SelectedIndex = НайтиРазделитель(профиль.Delimiter);
        _заголовки.Checked = профиль.HasHeaderRow;
        _началоСтроки.Value = Math.Max(1, профиль.FirstDataRow);

        СохранитьНастройкиШага();

        return профиль;
    }

    /// <summary>
    /// Читает текст файла импорта для предпросмотра разбора.
    /// </summary>
    /// <param name="путь">Путь к файлу.</param>
    /// <param name="кодировка">Имя кодировки.</param>
    /// <returns>Текст файла либо пустая строка.</returns>
    private static string ПрочитатьТекстФайла(string путь, string кодировка)
    {
        try
        {
            var кодировкаФайла = string.Equals(кодировка, "windows-1251", StringComparison.OrdinalIgnoreCase)
                ? System.Text.Encoding.GetEncoding(1251)
                : new System.Text.UTF8Encoding(false);

            return кодировкаФайла.GetString(File.ReadAllBytes(путь));
        }
        catch (Exception ошибка)
        {
            _ = ошибка;

            return string.Empty;
        }
    }

    private void СохранитьПолитики()
    {
        _контроллер.ImportPolicy = _политикаПроверенных.Checked
            ? ImportPolicy.VerifiedOnly
            : _политикаБезОшибок.Checked
                ? ImportPolicy.WithoutErrors
                : ImportPolicy.All;

        _контроллер.MergePolicy = _слияниеПропустить.Checked
            ? MergePolicy.SkipExisting
            : _слияниеДополнить.Checked
                ? MergePolicy.FillEmpty
                : MergePolicy.Overwrite;
    }

    private void СменитьФильтр()
    {
        if (_обновление)
        {
            return;
        }

        _контроллер.SetFilter(_показыватьОшибки.Checked
            ? ImportRecordFilter.WithErrors
            : ImportRecordFilter.All);

        ОбновитьФильтр();
    }

    private void СменитьЛист()
    {
        if (_обновление || _лист.SelectedIndex < 0)
        {
            return;
        }

        var имя = _лист.Items[_лист.SelectedIndex] as string;

        if (string.IsNullOrWhiteSpace(имя))
        {
            return;
        }

        _контроллер.LoadFile(_контроллер.FilePath, имя);

        ОбновитьСведения();
    }

    private void ВыбратьФайлДиалогом()
    {
        using var диалог = new OpenFileDialog
        {
            Title = "Файл импорта",
            Filter = "Файлы импорта (*.csv;*.txt;*.xlsx;*.html;*.htm)|*.csv;*.txt;*.xlsx;*.html;*.htm"
                + "|Текст с разделителями (*.csv)|*.csv|Текстовый файл (*.txt)|*.txt"
                + "|Книга Excel (*.xlsx)|*.xlsx|HTML (*.html;*.htm)|*.html;*.htm",
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var удалось = ВыбратьФайл(диалог.FileName);

        if (!удалось)
        {
            MessageBox.Show(
                this,
                _контроллер.ErrorMessage ?? "Не удалось прочитать файл импорта.",
                "Мастер импорта адресной книги",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        ЗаполнитьЛисты();
    }

    private void ЗаполнитьЛисты()
    {
        _обновление = true;

        var листы = ImportWizardController.ReadSheetNames(_контроллер.FilePath);

        _лист.Items.Clear();

        if (листы.Count == 0)
        {
            _лист.SelectedIndex = -1;
        }
        else
        {
            foreach (var лист in листы)
            {
                _лист.Items.Add(лист);
            }

            _лист.SelectedIndex = 0;
        }

        _обновление = false;
    }

    private void СохранитьПрофиль()
    {
        using var диалог = new SaveFileDialog
        {
            Title = "Сохранить профиль сопоставления",
            Filter = "Профиль сопоставления (*.csvprof)|*.csvprof",
            FilterIndex = 1,
            DefaultExt = "csvprof",
            FileName = string.IsNullOrWhiteSpace(_контроллер.FilePath)
                ? "Профиль импорта.csvprof"
                : Path.GetFileNameWithoutExtension(_контроллер.FilePath) + ".csvprof",
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        СохранитьНастройкиШага();
        СохранитьСопоставление();

        if (!_контроллер.SaveProfile(диалог.FileName))
        {
            MessageBox.Show(
                this,
                _контроллер.ErrorMessage ?? "Не удалось сохранить профиль сопоставления.",
                "Мастер импорта адресной книги",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        MessageBox.Show(
            this,
            "Профиль сопоставления сохранён.",
            "Мастер импорта адресной книги",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ЗагрузитьПрофиль()
    {
        using var диалог = new OpenFileDialog
        {
            Title = "Загрузить профиль сопоставления",
            Filter = "Профиль сопоставления (*.csvprof)|*.csvprof",
            FilterIndex = 1,
            DefaultExt = "csvprof",
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!_контроллер.LoadProfile(диалог.FileName))
        {
            MessageBox.Show(
                this,
                _контроллер.ErrorMessage ?? "Не удалось загрузить профиль сопоставления.",
                "Мастер импорта адресной книги",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        _кодировка.SelectedIndex = НайтиКодировку(_контроллер.BuildProfile().EncodingName);
        _разделитель.SelectedIndex = НайтиРазделитель(_контроллер.BuildProfile().Delimiter);
        _заголовки.Checked = _контроллер.HasHeaderRow;
        _началоСтроки.Value = Math.Max(1, _контроллер.StartRow);

        ПостроитьСопоставление();
        ОбновитьСведения();
    }

    private int НайтиКодировку(string имя)
    {
        for (var индекс = 0; индекс < Кодировки.Length; индекс++)
        {
            if (string.Equals(Кодировки[индекс].Имя, имя, StringComparison.OrdinalIgnoreCase))
            {
                return индекс;
            }
        }

        return 0;
    }

    private int НайтиРазделитель(char знак)
    {
        for (var индекс = 0; индекс < Разделители.Length; индекс++)
        {
            if (Разделители[индекс].Знак == знак)
            {
                return индекс;
            }
        }

        return 0;
    }
}
