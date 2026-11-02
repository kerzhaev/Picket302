using System.Drawing;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Windows.Forms;
using Picket302.App.UI.Components;
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

        Text = _создание ? "Новая рассылка" : "Рассылка";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(900, 560);

        ПостроитьИнтерфейс();

        ЗагрузитьДанныеAsync();
    }

    /// <summary>
    /// Созданная рассылка; <see langword="null"/>, если форма закрыта без
    /// сохранения.
    /// </summary>
    public Distribution? Результат { get; private set; }

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var верх = new Panel { Dock = DockStyle.Top, Height = 60 };

        var меткаНаименования = new Label
        {
            Text = "Наименование:",
            AutoSize = true,
            Location = new Point(8, 8),
        };

        _наименование.Location = new Point(110, 5);
        _наименование.Width = 280;

        var меткаВида = new Label
        {
            Text = "Вид:",
            AutoSize = true,
            Location = new Point(400, 8),
        };

        _видРассылки.DropDownStyle = ComboBoxStyle.DropDownList;
        _видРассылки.Location = new Point(430, 5);
        _видРассылки.Width = 180;
        _видРассылки.Items.Add("Один ко многим");
        _видРассылки.Items.Add("Многие к одному");
        _видРассылки.SelectedIndex = 0;

        var меткаКатегории = new Label
        {
            Text = "Категория:",
            AutoSize = true,
            Location = new Point(620, 8),
        };

        _категории.DropDownStyle = ComboBoxStyle.DropDownList;
        _категории.Location = new Point(690, 5);
        _категории.Width = 190;
        _категории.SelectedIndexChanged += (_, _) => ОбновитьДоступные();

        верх.Controls.Add(меткаНаименования);
        верх.Controls.Add(_наименование);
        верх.Controls.Add(меткаВида);
        верх.Controls.Add(_видРассылки);
        верх.Controls.Add(меткаКатегории);
        верх.Controls.Add(_категории);

        var разделитель = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 520,
        };

        _доступные.Dock = DockStyle.Fill;
        _доступные.Font = new Font("Tahoma", 8f);
        _доступные.ReadOnly = true;
        _доступные.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _доступные.MultiSelect = true;
        _доступные.RowHeadersVisible = false;
        _доступные.Columns.Add("Наименование", "Наименование");
        _доступные.Columns.Add("Индекс", "Индекс");
        _доступные.Columns.Add("Город", "Город");
        _доступные.Columns[0].Width = 260;
        _доступные.Columns[1].Width = 70;
        _доступные.Columns[2].Width = 140;
        _доступные.MouseDown += ЗапомнитьТочкуПеретаскивания;
        _доступные.MouseMove += ОтправитьПеретаскивание;
        _доступные.CellDoubleClick += (_, _) => ДобавитьВыделенные();

        _получатели.Dock = DockStyle.Fill;
        _получатели.Font = new Font("Tahoma", 8f);
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

        var кнопкиДобавления = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _добавитьВсехАктивных.Text = "Добавить всех активных";
        _добавитьВсехАктивных.AutoSize = true;
        _добавитьВсехАктивных.Click += (_, _) => ДобавитьВсехАктивных();

        _удалитьВыбранных.Text = "Удалить выбранные";
        _удалитьВыбранных.AutoSize = true;
        _удалитьВыбранных.Click += (_, _) => УдалитьВыбранных();

        _очистить.Text = "Очистить";
        _очистить.AutoSize = true;
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

        var низ = new Panel { Dock = DockStyle.Bottom, Height = 52 };

        _счётчикКонвертов.AutoSize = true;
        _счётчикКонвертов.Location = new Point(8, 10);
        _счётчикКонвертов.Font = new Font("Tahoma", 8f, FontStyle.Bold);
        _счётчикКонвертов.Text = "Всего необходимо конвертов: 0";

        _сохранить.Text = "Сохранить";
        _сохранить.Size = new Size(90, 24);
        _сохранить.Location = new Point(700, 24);
        _сохранить.Click += (_, _) => Сохранить();

        _отмена.Text = "Отмена";
        _отмена.Size = new Size(90, 24);
        _отмена.Location = new Point(796, 24);
        _отмена.DialogResult = DialogResult.Cancel;

        низ.Controls.Add(_счётчикКонвертов);
        низ.Controls.Add(_сохранить);
        низ.Controls.Add(_отмена);

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
                    var контрагент = _всеКонтрагенты.FirstOrDefault(сущ => сущ.Id == письмо.CounterpartyId);

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
    /// Пересчитывает и показывает счётчик «Всего необходимо конвертов».
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
    /// Сохраняет рассылку: создаёт письма по составу получателей.
    /// </summary>
    private async void Сохранить()
    {
        try
        {
            var отправитель = _состав.ТекущийОтправитель.CurrentSenderId;

            if (отправитель is null or <= 0)
            {
                MessageBox.Show(
                    this,
                    "Не выбран текущий отправитель — укажите его в верхней части главного окна.",
                    "Рассылка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

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

            var план = new DistributionPlan
            {
                Name = string.IsNullOrWhiteSpace(_наименование.Text) ? "Рассылка" : _наименование.Text.Trim(),
                Kind = _видРассылки.SelectedIndex == 1
                    ? DistributionKind.ManyToOne
                    : DistributionKind.OneToMany,
                SenderId = отправитель.Value,
                CounterpartyIds = идентификаторы,
            };

            var результат = await _состав.СлужбаРассылок
                .GenerateDistributionAsync(план)
                .ConfigureAwait(true);

            if (результат.IsFailure)
            {
                MessageBox.Show(this, результат.Error!, "Рассылка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Результат = результат.Value;
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
