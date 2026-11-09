using System.Drawing;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Редактор рассылки: перенос получателей перетаскиванием, добавление всех
/// активных контрагентов, фильтр по категориям и счётчик конвертов
/// (ТЗ §8.11, функция 140: «Drag &amp; drop получателей в рассылку»;
/// §8.12, функция 155: «Формирование рассылки по категориям»).
/// </summary>
public sealed class FormDistributionEditor : Form
{
    private readonly AppServices _состав;
    private readonly Distribution _рассылка;
    private readonly bool _создание;

    private readonly DataGridView _доступные = new();
    private readonly ListView _получатели = new();
    private readonly ComboBox _категории = new();
    private readonly Label _счётчикКонвертов = new();
    private readonly Button _добавитьВсехАктивных = new();
    private readonly Button _удалитьВыбранных = new();
    private readonly Button _очистить = new();
    private readonly Button _сохранить = new();
    private readonly Button _отмена = new();
    private readonly TextBox _наименование = new();
    private readonly ComboBox _видРассылки = new();
    private readonly Label _подсказкаПолучателей = new();
    private readonly ToolTip _подсказки = new();

    private IReadOnlyList<Counterparty> _всеКонтрагенты = [];
    private IReadOnlyList<Category> _категорииСправочника = [];

    /// <summary>
    /// Точка нажатия кнопки в списке доступных: до смещения за размер
    /// перетаскивания системы drag не начинается (F-E2).
    /// </summary>
    private Point _точкаПеретаскивания;

    /// <summary>
    /// Создаёт редактор рассылки.
    /// </summary>
    /// <param name="состав">Состав приложения.</param>
    /// <param name="рассылка">
    /// Редактируемая рассылка; <see langword="null"/> — создаётся новая.
    /// </param>
    public FormDistributionEditor(AppServices состав, Distribution? рассылка = null)
    {
        ArgumentNullException.ThrowIfNull(состав);

        _состав = состав;
        _создание = рассылка is null;
        _рассылка = рассылка ?? new Distribution();

        Text = _создание ? "Новая рассылка" : $"Правка рассылки — {_рассылка.Name}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(940, 580);
        MinimumSize = new Size(760, 480);

        ПостроитьИнтерфейс();
        EnvelopeUiTheme.ОформитьОкно(this);
        EnvelopeUiTheme.ОформитьОсновнуюКнопку(_сохранить);

        ЗагрузитьДанныеAsync();
    }

    /// <summary>
    /// Созданная рассылка; <see langword="null"/>, если форма закрыта без
    /// сохранения.
    /// </summary>
    public Distribution? Результат { get; private set; }

    /// <summary>
    /// Строит элементы формы: шапка параметров и кнопки на гибких контейнерах,
    /// поэтому поля не перекрываются и кнопки не исчезают при изменении
    /// размера окна (аудит UX 2026-11-03, G2: отказ от координатной компоновки).
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        // Шапка: подпись/поле «Наименование», «Вид» и «Категория» тянутся
        // пропорционально ширине окна.
        var верх = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8, 8, 8, 4),
        };

        верх.ColumnCount = 6;
        верх.RowCount = 1;
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));

        var меткаНаименования = new Label
        {
            Text = "Наименование:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 4, 0),
        };

        _наименование.Dock = DockStyle.Fill;
        _наименование.Margin = new Padding(0, 0, 8, 0);
        _наименование.AccessibleName = "Название рассылки";
        _наименование.AccessibleDescription = "Название, под которым будет сохранена рассылка.";

        var меткаВида = new Label
        {
            Text = "Вид:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 4, 0),
        };

        _видРассылки.DropDownStyle = ComboBoxStyle.DropDownList;
        _видРассылки.Dock = DockStyle.Fill;
        _видРассылки.Margin = new Padding(0, 0, 8, 0);
        _видРассылки.AccessibleName = "Вид рассылки";
        _видРассылки.AccessibleDescription = "Выбор направления рассылки: один ко многим или многие к одному.";
        _видРассылки.Items.Add("Один ко многим");
        _видРассылки.Items.Add("Многие к одному");
        _видРассылки.SelectedIndex = 0;

        // Вид существующей рассылки не меняется: роли писем («один ко многим»
        // и «многие к одному») имеют разную семантику, и смена вида без
        // пересоздания состава молча переориентировала бы письма. Для другого
        // вида создаётся новая рассылка.
        if (!_создание)
        {
            _видРассылки.Enabled = false;
            _подсказки.SetToolTip(
                _видРассылки,
                "Вид существующей рассылки нельзя изменить без пересоздания состава писем. Создайте новую рассылку нужного вида.");
        }

        var меткаКатегории = new Label
        {
            Text = "Категория:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 4, 0),
        };

        _категории.DropDownStyle = ComboBoxStyle.DropDownList;
        _категории.Dock = DockStyle.Fill;
        _категории.AccessibleName = "Фильтр по категории";
        _категории.AccessibleDescription = "Ограничить список доступных контрагентов выбранной категорией.";
        _категории.SelectedIndexChanged += (_, _) => ОбновитьДоступные();

        верх.Controls.Add(меткаНаименования, 0, 0);
        верх.Controls.Add(_наименование, 1, 0);
        верх.Controls.Add(меткаВида, 2, 0);
        верх.Controls.Add(_видРассылки, 3, 0);
        верх.Controls.Add(меткаКатегории, 4, 0);
        верх.Controls.Add(_категории, 5, 0);

        var разделитель = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 520,
        };

        _доступные.Dock = DockStyle.Fill;
        _доступные.AccessibleName = "Доступные контрагенты";
        _доступные.AccessibleDescription = "Список контрагентов, которых можно добавить в рассылку.";
        _доступные.ReadOnly = true;
        _доступные.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _доступные.MultiSelect = true;
        _доступные.RowHeadersVisible = false;
        _доступные.AllowUserToAddRows = false;
        _доступные.Columns.Add("Наименование", "Наименование");
        _доступные.Columns.Add("Индекс", "Индекс");
        _доступные.Columns.Add("Город", "Город");
        _доступные.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _доступные.Columns[1].Width = 70;
        _доступные.Columns[2].Width = 140;
        _доступные.MouseDown += ЗапомнитьТочкуПеретаскивания;
        _доступные.MouseMove += ОтправитьПеретаскивание;
        _доступные.CellDoubleClick += (_, _) => ДобавитьВыделенные();

        _получатели.Dock = DockStyle.Fill;
        _получатели.AccessibleName = "Получатели рассылки";
        _получатели.AccessibleDescription = "Список контрагентов, включённых в текущую рассылку.";
        _получатели.View = View.Details;
        _получатели.FullRowSelect = true;
        _получатели.AllowDrop = true;
        _получатели.Columns.Add("Наименование", "Наименование");
        _получатели.Columns.Add("Индекс", "Индекс");
        _получатели.Columns.Add("Копий", "Копий");
        _получатели.Columns[0].Width = 260;
        _получатели.Columns[1].Width = 70;
        _получатели.Columns[2].Width = 50;
        _получатели.DragEnter += ПринятьПеретаскивание;
        _получатели.DragDrop += РазместитьПеретаскивание;

        // Подсказка пустого списка получателей (аудит UX 2026-11-03, G5):
        // вместо пустого поля объясняется первое действие.
        _подсказкаПолучателей.Text = "Получатели не выбраны." + Environment.NewLine
            + "Выделите контрагентов слева и дважды щёлкните," + Environment.NewLine
            + "перетащите их сюда или нажмите «Добавить всех активных».";
        _подсказкаПолучателей.Dock = DockStyle.Fill;
        _подсказкаПолучателей.TextAlign = ContentAlignment.MiddleCenter;
        _подсказкаПолучателей.Visible = false;

        var кнопкиДобавления = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 2, 4, 2),
            WrapContents = false,
        };

        _добавитьВсехАктивных.Text = "Добавить всех активных";
        _добавитьВсехАктивных.AutoSize = true;
        _добавитьВсехАктивных.AccessibleName = "Добавить всех активных контрагентов";
        _добавитьВсехАктивных.AccessibleDescription = "Добавить в рассылку всех активных контрагентов, которых ещё нет в списке.";
        _добавитьВсехАктивных.Click += (_, _) => ДобавитьВсехАктивных();

        _удалитьВыбранных.Text = "Удалить выбранные";
        _удалитьВыбранных.AutoSize = true;
        _удалитьВыбранных.AccessibleName = "Удалить выбранных получателей";
        _удалитьВыбранных.AccessibleDescription = "Удалить выбранные строки из состава рассылки.";
        _удалитьВыбранных.Click += (_, _) => УдалитьВыбранных();

        _очистить.Text = "Очистить";
        _очистить.AutoSize = true;
        _очистить.AccessibleName = "Очистить получателей рассылки";
        _очистить.AccessibleDescription = "Удалить всех получателей из текущей рассылки.";
        _очистить.Click += (_, _) =>
        {
            _получатели.Items.Clear();
            ОбновитьСчётчик();
        };

        кнопкиДобавления.Controls.Add(_добавитьВсехАктивных);
        кнопкиДобавления.Controls.Add(_удалитьВыбранных);
        кнопкиДобавления.Controls.Add(_очистить);

        разделитель.Panel1.Controls.Add(_доступные);
        разделитель.Panel1.Controls.Add(кнопкиДобавления);
        разделитель.Panel2.Controls.Add(_получатели);
        разделитель.Panel2.Controls.Add(_подсказкаПолучателей);

        // Низ: счётчик слева, кнопки прижаты к правому краю потоком — при
        // узком окне кнопки не уходят за границу (аудит UX 2026-11-03, G2).
        var низ = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 0, 8, 0) };

        _счётчикКонвертов.Dock = DockStyle.Fill;
        _счётчикКонвертов.AccessibleName = "Количество конвертов в рассылке";
        _счётчикКонвертов.TextAlign = ContentAlignment.MiddleLeft;
        _счётчикКонвертов.Font = EnvelopeUiTheme.ШрифтЗаголовка(8.5f);
        _счётчикКонвертов.Text = "Всего необходимо конвертов: 0";

        var кнопкиИтога = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };

        _сохранить.Text = "Сохранить";
        _сохранить.AutoSize = true;
        _сохранить.AccessibleName = "Сохранить рассылку";
        _сохранить.AccessibleDescription = "Сохранить рассылку и сформировать её отправления.";
        _сохранить.Click += (_, _) => Сохранить();

        _отмена.Text = "Отмена";
        _отмена.AutoSize = true;
        _отмена.AccessibleName = "Отменить редактирование рассылки";
        _отмена.AccessibleDescription = "Закрыть редактор без сохранения изменений.";
        _отмена.DialogResult = DialogResult.Cancel;

        кнопкиИтога.Controls.Add(_отмена);
        кнопкиИтога.Controls.Add(_сохранить);

        низ.Controls.Add(_счётчикКонвертов);
        низ.Controls.Add(кнопкиИтога);

        Controls.Add(разделитель);
        Controls.Add(низ);
        Controls.Add(верх);

        AcceptButton = _сохранить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Загружает контрагентов и категории, затем обновляет список доступных.
    /// </summary>
    private async void ЗагрузитьДанныеAsync()
    {
        try
        {
            var страница = await _состав.Контрагенты
                .GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 500))
                .ConfigureAwait(true);

            _всеКонтрагенты = страница.Items;

            _категорииСправочника = await _состав.СлужбаКатегорий.GetTreeAsync().ConfigureAwait(true);

            _категории.Items.Clear();
            _категории.Items.Add("Все категории");
            _категории.Items.Add("Только активные");

            foreach (var категория in _категорииСправочника)
            {
                _категории.Items.Add(категория.Name);
            }

            _категории.SelectedIndex = 0;

            if (!_создание)
            {
                _наименование.Text = _рассылка.Name;
                _видРассылки.SelectedIndex = _рассылка.Kind == DistributionKind.ManyToOne ? 1 : 0;

                var письма = await _состав.Письма
                    .GetByDistributionAsync(_рассылка.Id)
                    .ConfigureAwait(true);

                foreach (var письмо in письма)
                {
                    // Участник письма зависит от вида рассылки: в «один ко
                    // многим» перечисляются получатели, в «многие к одному» —
                    // отправители писем.
                    var участник = _рассылка.Kind == DistributionKind.ManyToOne
                        ? письмо.SenderId
                        : письмо.CounterpartyId;

                    var контрагент = _всеКонтрагенты.FirstOrDefault(сущ => сущ.Id == участник);

                    if (контрагент is not null)
                    {
                        ДобавитьПолучателя(контрагент, письмо.CopyCount);
                    }
                }
            }

            ОбновитьДоступные();
            ОбновитьСчётчик();
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                $"Не удалось загрузить данные рассылки: {исключение.Message}",
                "Рассылка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Обновляет список доступных контрагентов с учётом фильтра категорий.
    /// </summary>
    private void ОбновитьДоступные()
    {
        _доступные.Rows.Clear();

        var выбранный = _категории.SelectedItem as string;

        foreach (var контрагент in _всеКонтрагенты)
        {
            if (контрагент.IsDeleted)
            {
                continue;
            }

            if (выбранный == "Только активные" && !контрагент.IsActive)
            {
                continue;
            }

            if (выбранный is not null
                && выбранный != "Все категории"
                && выбранный != "Только активные")
            {
                var категория = _категорииСправочника.FirstOrDefault(сущ =>
                    string.Equals(сущ.Name, выбранный, StringComparison.OrdinalIgnoreCase));

                if (категория is not null && контрагент.GroupId != категория.Id)
                {
                    continue;
                }
            }

            var адрес = контрагент.GetAddress(контрагент.AddressMainType);

            var индекс = _доступные.Rows.Add(
                контрагент.Name,
                адрес?.Zip ?? string.Empty,
                адрес?.Lines.ElementAtOrDefault(4) ?? string.Empty);

            // Идентификатор хранится в Tag строки: первый столбец —
            // наименование, разбирать его как число нельзя (F-E2).
            _доступные.Rows[индекс].Tag = контрагент.Id;
        }
    }

    /// <summary>
    /// Добавляет в рассылку всех активных контрагентов, ещё не добавленных.
    /// </summary>
    private void ДобавитьВсехАктивных()
    {
        foreach (var контрагент in _всеКонтрагенты)
        {
            if (контрагент.IsDeleted || !контрагент.IsActive)
            {
                continue;
            }

            if (УжеДобавлен(контрагент.Id))
            {
                continue;
            }

            ДобавитьПолучателя(контрагент, 1);
        }

        ОбновитьСчётчик();
    }

    /// <summary>
    /// Добавляет выделенные в списке доступных контрагентов.
    /// </summary>
    private void ДобавитьВыделенные()
    {
        foreach (DataGridViewRow строка in _доступные.SelectedRows)
        {
            // Идентификатор читается из Tag строки (F-E2).
            var идентификатор = строка.Tag as long? ?? 0L;
            var контрагент = _всеКонтрагенты.FirstOrDefault(сущ => сущ.Id == идентификатор);

            if (контрагент is null || УжеДобавлен(контрагент.Id))
            {
                continue;
            }

            ДобавитьПолучателя(контрагент, 1);
        }

        ОбновитьСчётчик();
    }

    /// <summary>
    /// Определяет, добавлен ли контрагент в рассылку.
    /// </summary>
    private bool УжеДобавлен(long идентификатор)
        => _получатели.Items.Cast<ListViewItem>()
            .Any(элемент => элемент.Tag is long id && id == идентификатор);

    /// <summary>
    /// Добавляет контрагента в список получателей рассылки.
    /// </summary>
    private void ДобавитьПолучателя(Counterparty контрагент, int копий)
    {
        var адрес = контрагент.GetAddress(контрагент.AddressMainType);

        var элемент = new ListViewItem(контрагент.Name)
        {
            Tag = контрагент.Id,
        };

        элемент.SubItems.Add(адрес?.Zip ?? string.Empty);
        элемент.SubItems.Add(копий.ToString(CultureInfo.InvariantCulture));

        _получатели.Items.Add(элемент);
    }

    /// <summary>
    /// Удаляет выбранных получателей из рассылки.
    /// </summary>
    private void УдалитьВыбранных()
    {
        foreach (var элемент in _получатели.SelectedItems.Cast<ListViewItem>().ToList())
        {
            _получатели.Items.Remove(элемент);
        }

        ОбновитьСчётчик();
    }

    /// <summary>
    /// Пересчитывает и показывает счётчик «Всего необходимо конвертов»
    /// и подсказку пустого списка получателей.
    /// </summary>
    private void ОбновитьСчётчик()
    {
        var конвертов = 0;

        foreach (var элемент in _получатели.Items.Cast<ListViewItem>())
        {
            if (int.TryParse(элемент.SubItems[2].Text, out var копий))
            {
                конвертов += копий;
            }
        }

        _счётчикКонвертов.Text = $"Всего необходимо конвертов: {конвертов}";
        _подсказкаПолучателей.Visible = _получатели.Items.Count == 0;
        _получатели.Visible = _получатели.Items.Count > 0;
    }

    /// <summary>
    /// Запоминает точку нажатия кнопки: перетаскивание начнётся только после
    /// смещения курсора за размер перетаскивания системы (F-E2).
    /// </summary>
    private void ЗапомнитьТочкуПеретаскивания(object? отправитель, MouseEventArgs аргументы)
    {
        if (аргументы.Button == MouseButtons.Left)
        {
            _точкаПеретаскивания = аргументы.Location;
        }
    }

    /// <summary>
    /// Начинает перетаскивание выделенных строк списка доступных контрагентов,
    /// когда курсор сместился за <see cref="SystemInformation.DragSize"/> от
    /// точки нажатия.
    /// </summary>
    private void ОтправитьПеретаскивание(object? отправитель, MouseEventArgs аргументы)
    {
        if (аргументы.Button != MouseButtons.Left || _доступные.SelectedRows.Count == 0)
        {
            return;
        }

        var смещениеПоX = аргументы.X - _точкаПеретаскивания.X;
        var смещениеПоY = аргументы.Y - _точкаПеретаскивания.Y;
        var размер = SystemInformation.DragSize;

        // Обычный щелчок без смещения перетаскиванием не считается (F-E2).
        if (Math.Abs(смещениеПоX) < размер.Width && Math.Abs(смещениеПоY) < размер.Height)
        {
            return;
        }

        var идентификаторы = _доступные.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(строка => строка.Tag as long? ?? 0L)
            .ToList();

        _доступные.DoDragDrop(идентификаторы, DragDropEffects.Copy);
    }

    /// <summary>
    /// Разрешает приём перетаскиваемых получателей.
    /// </summary>
    private void ПринятьПеретаскивание(object? отправитель, DragEventArgs аргументы)
    {
        аргументы.Effect = аргументы.Data?.GetDataPresent(typeof(List<long>)) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    /// <summary>
    /// Размещает перетащенных получателей в рассылке.
    /// </summary>
    private void РазместитьПеретаскивание(object? отправитель, DragEventArgs аргументы)
    {
        if (аргументы.Data?.GetData(typeof(List<long>)) is not List<long> идентификаторы)
        {
            return;
        }

        foreach (var идентификатор in идентификаторы)
        {
            var контрагент = _всеКонтрагенты.FirstOrDefault(сущ => сущ.Id == идентификатор);

            if (контрагент is null || УжеДобавлен(контрагент.Id))
            {
                continue;
            }

            ДобавитьПолучателя(контрагент, 1);
        }

        ОбновитьСчётчик();
    }

    /// <summary>
    /// Сохраняет рассылку: новая создаётся с письмами по составу получателей,
    /// существующая изменяется без создания дублирующей записи (ТЗ §8.12,
    /// функция 156).
    /// </summary>
    /// <remarks>
    /// Отправитель: для новой рассылки обязателен текущий глобальный
    /// отправитель; для правки значение по умолчанию — сохранённый отправитель
    /// рассылки (<c>_рассылка.SenderId</c>), и только при его отсутствии
    /// берётся текущий. Случайная замена сохранённого отправителя текущим
    /// глобальным не выполняется.
    /// </remarks>
    private async void Сохранить()
    {
        try
        {
            if (_получатели.Items.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "В рассылке нет ни одного получателя.",
                    "Рассылка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var идентификаторы = _получатели.Items.Cast<ListViewItem>()
                .Select(элемент => (long)элемент.Tag!)
                .ToList();

            var имя = string.IsNullOrWhiteSpace(_наименование.Text)
                ? "Рассылка"
                : _наименование.Text.Trim();

            var вид = _видРассылки.SelectedIndex == 1
                ? DistributionKind.ManyToOne
                : DistributionKind.OneToMany;

            if (_создание)
            {
                var текущий = _состав.ТекущийОтправитель.CurrentSenderId;

                if (текущий is null or <= 0)
                {
                    MessageBox.Show(
                        this,
                        "Не выбран текущий отправитель — укажите его в верхней части главного окна.",
                        "Рассылка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var план = new DistributionPlan
                {
                    Name = имя,
                    Kind = вид,
                    SenderId = текущий.Value,
                    CounterpartyIds = идентификаторы,
                };

                var создание = await _состав.СлужбаРассылок
                    .GenerateDistributionAsync(план)
                    .ConfigureAwait(true);

                if (создание.IsFailure)
                {
                    MessageBox.Show(this, создание.Error!, "Рассылка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Результат = создание.Value;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            // Правка: сохранённый отправитель рассылки — значение по
            // умолчанию; текущий глобальный используется только если
            // сохранённого нет.
            var отправительРассылки = _рассылка.SenderId is > 0
                ? _рассылка.SenderId
                : _состав.ТекущийОтправитель.CurrentSenderId;

            if (отправительРассылки is null or <= 0)
            {
                MessageBox.Show(
                    this,
                    "У рассылки не указан отправитель, и текущий отправитель не выбран — укажите отправителя в верхней части главного окна.",
                    "Рассылка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Вид заблокирован при правке (см. ПостроитьИнтерфейс); в запрос
            // передаётся вид самой рассылки, а не прочитанное из
            // заблокированного списка значение.
            var запрос = new DistributionUpdateRequest(
                _рассылка.Id,
                имя,
                _рассылка.Kind,
                отправительРассылки,
                идентификаторы);

            var правка = await _состав.СлужбаРассылок
                .UpdateDistributionAsync(запрос)
                .ConfigureAwait(true);

            if (правка.IsFailure)
            {
                MessageBox.Show(this, правка.Error!, "Рассылка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (правка.Value.SkippedCounterparties.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Пропущены контрагенты без основного адреса: "
                    + string.Join(", ", правка.Value.SkippedCounterparties) + ".",
                    "Рассылка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            Результат = правка.Value.Distribution;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception исключение) when (исключение is SqliteException or InvalidOperationException or IOException)
        {
            MessageBox.Show(
                this,
                $"Не удалось сохранить рассылку: {исключение.Message}",
                "Рассылка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
