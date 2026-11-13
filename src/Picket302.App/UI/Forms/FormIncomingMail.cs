using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог входящей корреспонденции и движения документов (ТЗ §8.2,
/// функции 29 и 30): журнал регистрации с поиском, приём/передача/архив,
/// история движений.
/// </summary>
public sealed class FormIncomingMail : Form
{
    private readonly IncomingMailService _служба;
    private readonly IncomingMovementTracker _трекер;

    private readonly TextBox _поиск = new();
    private readonly ListView _список = new();
    private readonly Label _статус = new();
    private readonly Button _обновить = new();
    private readonly Button _зарегистрировать = new();
    private readonly Button _приём = new();
    private readonly Button _передать = new();
    private readonly Button _вАрхив = new();
    private readonly Button _история = new();
    private readonly Button _удалить = new();
    private readonly Button _закрыть = new();

    /// <summary>Выделенная запись (поле состояния: ListView без показа формы
    /// не наполняет SelectedItems).</summary>
    private long? _выбранныйId;

    /// <summary>Местонахождение по идентификатору записи (для точек проверок).</summary>
    private readonly Dictionary<long, string> _местонахождениеПоИд = new();

    /// <summary>Точка подмены уведомлений в автоматических проверках.</summary>
    internal Action<string, string>? УведомитьДляПроверок { get; set; }

    /// <summary>
    /// Создаёт диалог входящей корреспонденции.
    /// </summary>
    /// <param name="служба">Служба журнала входящей корреспонденции.</param>
    /// <param name="трекер">Трекер движения документов.</param>
    public FormIncomingMail(IncomingMailService служба, IncomingMovementTracker трекер)
    {
        ArgumentNullException.ThrowIfNull(служба);
        ArgumentNullException.ThrowIfNull(трекер);

        _служба = служба;
        _трекер = трекер;

        Text = "Входящая корреспонденция";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(700, 440);

        ПостроитьИнтерфейс();
        EnvelopeUiTheme.ОформитьОкно(this);
        EnvelopeUiTheme.ОформитьОсновнуюКнопку(_закрыть);

        ОбновитьСписок();
    }

    /// <summary>Число записей в списке (точка автопроверок).</summary>
    internal int ЧислоЗаписей => _список.Items.Count;

    /// <summary>Идентификатор выделенного документа.</summary>
    internal long? ВыбранныйId => _выбранныйId;

    /// <summary>
    /// Местонахождение выделенного документа — точка автопроверок.
    /// </summary>
    internal string? СведенияДляПроверок =>
        _выбранныйId is { } id && _местонахождениеПоИд.TryGetValue(id, out var место) ? место : null;

    /// <summary>
    /// Выделяет запись по идентификатору программно — точка автопроверок
    /// (форма не показывается, выделение мышью недоступно).
    /// </summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <returns><see langword="true"/>, если запись найдена и выделена.</returns>
    internal bool ВыбратьДляПроверок(long id)
    {
        foreach (ListViewItem пункт in _список.Items)
        {
            if (пункт.Tag is long значение && значение == id)
            {
                пункт.Selected = true;
                _выбранныйId = id;
                ОбновитьДоступность();

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Перечитывает журнал (с текущим поисковым фильтром). Выделение
    /// сохраняется, иначе выделяется первая запись — естественный UX.
    /// </summary>
    /// <returns>Число записей.</returns>
    internal int ОбновитьСписок()
    {
        var сохранённыйId = _выбранныйId;

        _список.BeginUpdate();
        _список.Items.Clear();
        _местонахождениеПоИд.Clear();

        var записи = _служба.НайтиAsync(_поиск.Text).GetAwaiter().GetResult();

        foreach (var запись in записи)
        {
            var местонахождение = _трекер.ТекущееAsync(запись.Id).GetAwaiter().GetResult();
            _местонахождениеПоИд[запись.Id] = местонахождение.Местонахождение;

            var пункт = new ListViewItem(запись.RegNumber)
            {
                Tag = запись.Id,
                ToolTipText = запись.Subject,
            };

            пункт.SubItems.Add(запись.RegDate.ToLocalTime().ToString("dd.MM.yyyy"));
            пункт.SubItems.Add(string.IsNullOrWhiteSpace(запись.Sender) ? "—" : запись.Sender);
            пункт.SubItems.Add(НазваниеТипа(запись.DocType));
            пункт.SubItems.Add(запись.ScanPath is null ? "—" : "есть");
            пункт.SubItems.Add(местонахождение.Местонахождение);

            _список.Items.Add(пункт);
        }

        _список.EndUpdate();

        // Восстановление выделения; при потоке — первая запись.
        if (_список.Items.Count > 0)
        {
            var выделен = сохранённыйId is not null && ВыбратьДляПроверок(сохранённыйId.Value);

            if (!выделен)
            {
                _список.Items[0].Selected = true;
                _выбранныйId = _список.Items[0].Tag as long?;
            }
        }

        ОбновитьДоступность();

        return _список.Items.Count;
    }

    /// <summary>
    /// Регистрирует входящее отправление из готовых полей — точка
    /// автопроверок без файловых диалогов.
    /// </summary>
    internal async Task<IncomingMail> ЗарегистрироватьДляПроверокAsync(
        string regNumber,
        string sender,
        string subject,
        IncomingMailType тип,
        string? scanPath)
    {
        var запись = await _служба.ЗарегистрироватьAsync(
            regNumber,
            DateTime.UtcNow,
            sender,
            subject,
            тип,
            scanPath).ConfigureAwait(true);

        ОбновитьСписок();
        ВыбратьДляПроверок(запись.Id);
        ПоказатьСтатус($"Зарегистрировано: {запись.RegNumber}.");

        return запись;
    }

    /// <summary>
    /// Регистрирует приём выделенного документа — точка автопроверок.
    /// </summary>
    internal async Task<IncomingMovement> ПриёмДляПроверокAsync(string department, string? executor = null)
    {
        var движение = await ВыполнитьДвижениеAsync(
            () => _трекер.ПриёмAsync(ТребоватьВыбор(), department, executor)).ConfigureAwait(true);

        ПоказатьСтатус($"Приём зарегистрирован: {движение.Department}.");

        return движение;
    }

    /// <summary>
    /// Передаёт выделенный документ подразделению — точка автопроверок.
    /// </summary>
    internal async Task<IncomingMovement> ПередачаДляПроверокAsync(string department, string? executor = null)
    {
        var движение = await ВыполнитьДвижениеAsync(
            () => _трекер.ПередатьAsync(ТребоватьВыбор(), department, executor)).ConfigureAwait(true);

        ПоказатьСтатус($"Передано: {движение.Department}.");

        return движение;
    }

    /// <summary>
    /// Архивирует выделенный документ — точка автопроверок.
    /// </summary>
    internal async Task<IncomingMovement> АрхивДляПроверокAsync()
    {
        var движение = await ВыполнитьДвижениеAsync(
            () => _трекер.АрхивироватьAsync(ТребоватьВыбор())).ConfigureAwait(true);

        ПоказатьСтатус("Документ передан в архив.");

        return движение;
    }

    /// <summary>
    /// Удаляет выделенную запись журнала (скан в профиле сохраняется) —
    /// точка автопроверок.
    /// </summary>
    internal async Task<bool> УдалитьДляПроверокAsync()
    {
        var id = ТребоватьВыбор();
        var удалено = await _служба.УдалитьAsync(id).ConfigureAwait(true);

        if (удалено)
        {
            ПоказатьСтатус("Запись удалена (скан в профиле сохранён).");
            ОбновитьСписок();
        }

        return удалено;
    }

    /// <summary>Строит элементы диалога.</summary>
    private void ПостроитьИнтерфейс()
    {
        var верх = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(8, 6, 8, 0),
            ColumnCount = 2,
            AutoSize = true,
        };

        _поиск.Dock = DockStyle.Fill;
        _поиск.AccessibleName = "Поиск по журналу входящей корреспонденции";
        _поиск.AccessibleDescription = "Подстрока номера, отправителя или содержания.";
        _поиск.TextChanged += (_, _) => ОбновитьСписок();

        var надписьПоиска = new Label { Text = "Поиск:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) };

        верх.Controls.Add(надписьПоиска, 0, 0);
        верх.Controls.Add(_поиск, 1, 0);

        верх.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        верх.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _список.Dock = DockStyle.Fill;
        _список.View = View.Details;
        _список.FullRowSelect = true;
        _список.HideSelection = false;
        _список.MultiSelect = false;
        _список.AccessibleName = "Журнал входящей корреспонденции";
        _список.Columns.Add("Номер", 130);
        _список.Columns.Add("Дата", 90);
        _список.Columns.Add("Отправитель", 200);
        _список.Columns.Add("Тип", 90);
        _список.Columns.Add("Скан", 60);
        _список.Columns.Add("Местонахождение", 200);
        _список.ItemSelectionChanged += (_, _) =>
        {
            _выбранныйId = _список.SelectedItems.Cast<ListViewItem>()
                .FirstOrDefault()?.Tag as long?;
            ОбновитьДоступность();
        };

        var низ = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(8, 4, 8, 0) };

        _обновить.Text = "Обновить";
        _обновить.AutoSize = true;
        _обновить.AccessibleName = "Обновить журнал входящей корреспонденции";
        _обновить.Click += (_, _) => ОбновитьСписок();

        _зарегистрировать.Text = "Зарегистрировать…";
        _зарегистрировать.AutoSize = true;
        _зарегистрировать.AccessibleName = "Зарегистрировать входящее отправление";
        _зарегистрировать.Click += async (_, _) => await ЗарегистрироватьAsync().ConfigureAwait(true);

        _приём.Text = "Приём";
        _приём.AutoSize = true;
        _приём.Enabled = false;
        _приём.AccessibleName = "Зарегистрировать приём документа";
        _приём.Click += async (_, _) => await ИзДвиженияAsync(
            "Приём документа", "Подразделение-приёмник:",
            async (подразделение, исполнитель) => await ПриёмДляПроверокAsync(подразделение, исполнитель)
                .ConfigureAwait(true)).ConfigureAwait(true);

        _передать.Text = "Передать…";
        _передать.AutoSize = true;
        _передать.Enabled = false;
        _передать.AccessibleName = "Передать документ на исполнение";
        _передать.Click += async (_, _) => await ИзДвиженияAsync(
            "Передача документа", "Подразделение-получатель:",
            async (подразделение, исполнитель) => await ПередачаДляПроверокAsync(подразделение, исполнитель)
                .ConfigureAwait(true)).ConfigureAwait(true);

        _вАрхив.Text = "В архив";
        _вАрхив.AutoSize = true;
        _вАрхив.Enabled = false;
        _вАрхив.AccessibleName = "Передать документ в архив";
        _вАрхив.Click += async (_, _) =>
        {
            try
            {
                await АрхивДляПроверокAsync().ConfigureAwait(true);
                ОбновитьСписок();
            }
            catch (Exception ошибка) when (ошибка is InvalidOperationException or IOException)
            {
                Сообщить(ошибка.Message, "Движение документа");
            }
        };

        _история.Text = "История…";
        _история.AutoSize = true;
        _история.Enabled = false;
        _история.AccessibleName = "Показать историю движения документа";
        _история.Click += (_, _) => ПоказатьИсторию();

        _удалить.Text = "Удалить";
        _удалить.AutoSize = true;
        _удалить.Enabled = false;
        _удалить.AccessibleName = "Удалить запись журнала";
        _удалить.Click += async (_, _) =>
        {
            try
            {
                await УдалитьДляПроверокAsync().ConfigureAwait(true);
            }
            catch (Exception ошибка) when (ошибка is InvalidOperationException or IOException)
            {
                Сообщить(ошибка.Message, "Удаление записи");
            }
        };

        _закрыть.Text = "Закрыть";
        _закрыть.AutoSize = true;
        _закрыть.DialogResult = DialogResult.Cancel;
        _закрыть.AccessibleName = "Закрыть входящую корреспонденцию";

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 6, 0, 0),
        };

        кнопки.Controls.AddRange(new Control[]
        {
            _обновить,
            _зарегистрировать,
            _приём,
            _передать,
            _вАрхив,
            _история,
            _удалить,
            _закрыть,
        });

        низ.Controls.Add(кнопки);

        _статус.Dock = DockStyle.Bottom;
        _статус.Height = 30;
        _статус.TextAlign = ContentAlignment.MiddleLeft;
        _статус.ForeColor = EnvelopeUiTheme.MutedText;
        _статус.AccessibleName = "Статус входящей корреспонденции";

        Controls.Add(_список);
        Controls.Add(верх);
        Controls.Add(_статус);
        Controls.Add(низ);

        AcceptButton = null;
        CancelButton = _закрыть;
    }

    /// <summary>Включает кнопки движения при наличии выделения.</summary>
    private void ОбновитьДоступность()
    {
        var есть = ВыбранныйId is not null;

        _приём.Enabled = есть;
        _передать.Enabled = есть;
        _вАрхив.Enabled = есть;
        _история.Enabled = есть;
        _удалить.Enabled = есть;
    }

    /// <summary>Требует выделенную запись.</summary>
    private long ТребоватьВыбор()
    {
        var id = _выбранныйId
            ?? throw new InvalidOperationException("Выберите запись журнала входящей корреспонденции.");

        return id;
    }

    /// <summary>Выполняет движение с изоляцией ошибок и обновлением списка.</summary>
    private async Task<IncomingMovement> ВыполнитьДвижениеAsync(
        Func<Task<IncomingMovement>> действие)
    {
        IncomingMovement движение;

        try
        {
            движение = await действие().ConfigureAwait(true);
        }
        catch (Exception ошибка) when (ошибка is InvalidOperationException or IOException)
        {
            Сообщить(ошибка.Message, "Движение документа");
            throw;
        }

        ОбновитьСписок();

        return движение;
    }

    /// <summary>Диалог ввода подразделения/исполнителя и выполнение движения.</summary>
    private async Task ИзДвиженияAsync(
        string заголовок,
        string подписьПодразделения,
        Func<string, string?, Task> действие)
    {
        try
        {
            ТребоватьВыбор();
        }
        catch (InvalidOperationException ошибка)
        {
            Сообщить(ошибка.Message, заголовок);

            return;
        }

        using var диалог = new ФормаДвижения(заголовок, подписьПодразделения);

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await действие(диалог.Подразделение, диалог.Исполнитель).ConfigureAwait(true);
        }
        catch (Exception ошибка) when (ошибка is InvalidOperationException or IOException)
        {
            Сообщить(ошибка.Message, заголовок);
        }
    }

    /// <summary>Регистрация нового входящего (диалог с полями журнала).</summary>
    private async Task ЗарегистрироватьAsync()
    {
        using var диалог = new ФормаРегистрации();

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await ЗарегистрироватьДляПроверокAsync(
                диалог.Номер,
                диалог.Отправитель,
                диалог.Содержание,
                диалог.Тип,
                диалог.Скан).ConfigureAwait(true);
        }
        catch (Exception ошибка) when (ошибка is InvalidOperationException or IOException)
        {
            // Дубликат номера приходит как InvalidOperationException
            // из репозитория (D-S32-01: драйверное исключение транслируется).
            Сообщить(ошибка.Message, "Регистрация входящего");
            ОбновитьСписок();
        }
    }

    /// <summary>История движения выделенного документа.</summary>
    private void ПоказатьИсторию()
    {
        var id = ВыбранныйId;

        if (id is null)
        {
            return;
        }

        var история = _трекер.ИсторияAsync(id.Value).GetAwaiter().GetResult();

        using var диалог = new ФормаИстории(история);
        диалог.ShowDialog(this);
    }

    /// <summary>Обновляет строку статуса.</summary>
    private void ПоказатьСтатус(string текст)
    {
        _статус.Text = текст.Length > 200 ? текст[..200] + "…" : текст;
    }

    /// <summary>Уведомление пользователя (подменяется в проверках).</summary>
    private void Сообщить(string текст, string заголовок)
    {
        if (УведомитьДляПроверок is { } уведомить)
        {
            уведомить(текст, заголовок);
        }
        else
        {
            MessageBox.Show(this, текст, заголовок);
        }
    }

    /// <summary>Русское название типа корреспонденции.</summary>
    private static string НазваниеТипа(IncomingMailType тип) => тип switch
    {
        IncomingMailType.Letter => "Письмо",
        IncomingMailType.Parcel => "Бандероль",
        IncomingMailType.Courier => "Курьер",
        _ => "Прочее",
    };

    /// <summary>Диалог регистрации входящего отправления.</summary>
    private sealed class ФормаРегистрации : Form
    {
        private readonly TextBox _номер = new();
        private readonly TextBox _отправитель = new();
        private readonly TextBox _содержание = new();
        private readonly ComboBox _тип = new() { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _скан = new() { ReadOnly = true };

        /// <summary>Создаёт диалог регистрации.</summary>
        public ФормаРегистрации()
        {
            Text = "Регистрация входящего отправления";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(520, 320);

            var сетка = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(8),
            };
            сетка.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            ДобавитьСтроку(сетка, 0, "Номер:", _номер, "Регистрационный номер входящего");
            ДобавитьСтроку(сетка, 1, "Отправитель:", _отправитель, "Отправитель входящего отправления");
            ДобавитьСтроку(сетка, 2, "Содержание:", _содержание, "Краткое содержание документа");

            _тип.AccessibleName = "Тип входящей корреспонденции";
            foreach (var (значение, название) in new[]
                     {
                         (IncomingMailType.Letter, "Письмо"),
                         (IncomingMailType.Parcel, "Бандероль"),
                         (IncomingMailType.Courier, "Курьер"),
                         (IncomingMailType.Other, "Прочее"),
                     })
            {
                _тип.Items.Add(new ПунктТипа(значение, название));
            }

            _тип.SelectedIndex = 0;
            ДобавитьСтроку(сетка, 3, "Тип:", _тип, "Тип входящей корреспонденции");

            _скан.AccessibleName = "Путь к файлу скана";
            var выбрать = new Button
            {
                Text = "Выбрать скан…",
                AutoSize = true,
                AccessibleName = "Выбрать файл скана",
            };
            выбрать.Click += (_, _) =>
            {
                using var диалог = new OpenFileDialog { CheckFileExists = true };

                if (диалог.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    _скан.Text = диалог.FileName;
                }
            };

            var панельСкан = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            панельСкан.Controls.Add(_скан);
            панельСкан.Controls.Add(выбрать);
            _скан.Width = 330;
            ДобавитьСтрокуКонтрол(сетка, 4, "Скан:", панельСкан);

            var сохранить = new Button
            {
                Text = "Зарегистрировать",
                AutoSize = true,
                DialogResult = DialogResult.OK,
                AccessibleName = "Зарегистрировать входящее отправление",
            };

            var отмена = new Button
            {
                Text = "Отмена",
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Отменить регистрацию входящего",
            };

            var низ = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 0),
            };

            низ.Controls.Add(отмена);
            низ.Controls.Add(сохранить);

            сетка.SetColumnSpan(панельСкан, 1);
            Controls.Add(сетка);
            Controls.Add(низ);

            AcceptButton = сохранить;
            CancelButton = отмена;

            EnvelopeUiTheme.ОформитьОкно(this);
            EnvelopeUiTheme.ОформитьОсновнуюКнопку(сохранить);
        }

        /// <summary>Регистрационный номер.</summary>
        public string Номер => _номер.Text.Trim();

        /// <summary>Отправитель.</summary>
        public string Отправитель => _отправитель.Text.Trim();

        /// <summary>Содержание.</summary>
        public string Содержание => _содержание.Text.Trim();

        /// <summary>Тип корреспонденции.</summary>
        public IncomingMailType Тип =>
            (_тип.SelectedItem as ПунктТипа)?.Значение ?? IncomingMailType.Letter;

        /// <summary>Путь к скану (пустой — без скана).</summary>
        public string? Скан => string.IsNullOrWhiteSpace(_скан.Text) ? null : _скан.Text.Trim();

        /// <summary>Добавляет строку «метка — контрол».</summary>
        private static void ДобавитьСтроку(
            TableLayoutPanel сетка,
            int строка,
            string подпись,
            Control контрол,
            string доступноеИмя)
        {
            контрол.AccessibleName = доступноеИмя;
            ДобавитьСтрокуКонтрол(сетка, строка, подпись, контрол);
        }

        /// <summary>Добавляет строку сетки без установки AccessibleName.</summary>
        private static void ДобавитьСтрокуКонтрол(
            TableLayoutPanel сетка,
            int строка,
            string подпись,
            Control контрол)
        {
            сетка.Controls.Add(new Label
            {
                Text = подпись,
                AutoSize = true,
                Padding = new Padding(0, 6, 8, 0),
            }, 0, строка);
            сетка.Controls.Add(контрол, 1, строка);
        }

        /// <summary>Пункт комбинированного списка типов.</summary>
        private sealed class ПунктТипа(IncomingMailType значение, string название)
        {
            /// <summary>Значение типа.</summary>
            public IncomingMailType Значение { get; } = значение;

            /// <summary>Название типа.</summary>
            public override string ToString() => название;
        }
    }

    /// <summary>Диалог движения (подразделение + исполнитель).</summary>
    private sealed class ФормаДвижения : Form
    {
        private readonly TextBox _подразделение = new();
        private readonly TextBox _исполнитель = new();

        /// <summary>Создаёт диалог движения.</summary>
        /// <param name="заголовок">Заголовок окна.</param>
        /// <param name="подписьПодразделения">Подпись поля подразделения.</param>
        public ФормаДвижения(string заголовок, string подписьПодразделения)
        {
            Text = заголовок;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(460, 160);

            var сетка = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(8),
            };
            сетка.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _подразделение.AccessibleName = подписьПодразделения;
            _исполнитель.AccessibleName = "Исполнитель (необязательно)";

            сетка.Controls.Add(new Label
            {
                Text = подписьПодразделения,
                AutoSize = true,
                Padding = new Padding(0, 6, 8, 0),
            }, 0, 0);
            сетка.Controls.Add(_подразделение, 1, 0);

            сетка.Controls.Add(new Label
            {
                Text = "Исполнитель:",
                AutoSize = true,
                Padding = new Padding(0, 6, 8, 0),
            }, 0, 1);
            сетка.Controls.Add(_исполнитель, 1, 1);

            var сохранить = new Button
            {
                Text = "Выполнить",
                AutoSize = true,
                DialogResult = DialogResult.OK,
                AccessibleName = "Выполнить движение документа",
            };

            var отмена = new Button
            {
                Text = "Отмена",
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Отменить движение документа",
            };

            var низ = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 0),
            };

            низ.Controls.Add(отмена);
            низ.Controls.Add(сохранить);

            Controls.Add(сетка);
            Controls.Add(низ);

            AcceptButton = сохранить;
            CancelButton = отмена;

            EnvelopeUiTheme.ОформитьОкно(this);
            EnvelopeUiTheme.ОформитьОсновнуюКнопку(сохранить);
        }

        /// <summary>Подразделение.</summary>
        public string Подразделение => _подразделение.Text.Trim();

        /// <summary>Исполнитель.</summary>
        public string? Исполнитель => string.IsNullOrWhiteSpace(_исполнитель.Text) ? null : _исполнитель.Text.Trim();
    }

    /// <summary>Диалог истории движения (только чтение).</summary>
    private sealed class ФормаИстории : Form
    {
        /// <summary>Создаёт диалог истории.</summary>
        /// <param name="история">Движения документа.</param>
        public ФормаИстории(IReadOnlyList<IncomingMovement> история)
        {
            Text = "История движения документа";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(640, 380);

            var список = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = "История движения документа",
            };

            foreach (var движение in история)
            {
                var исполнитель = движение.Executor is null
                    ? string.Empty
                    : $" — {движение.Executor}";

                список.Items.Add(
                    $"{движение.MovedAt.ToLocalTime():dd.MM.yyyy HH:mm} — {НазваниеДействия(движение.Action)}"
                    + $" — {движение.Department}{исполнитель}");
            }

            if (история.Count == 0)
            {
                список.Items.Add("Движений не зарегистрировано (документ не принят).");
            }

            var закрыть = new Button
            {
                Text = "Закрыть",
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Закрыть историю движения",
            };

            var низ = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 0),
            };

            низ.Controls.Add(закрыть);

            Controls.Add(список);
            Controls.Add(низ);

            CancelButton = закрыть;

            EnvelopeUiTheme.ОформитьОкно(this);
            EnvelopeUiTheme.ОформитьОсновнуюКнопку(закрыть);
        }

        /// <summary>Русское название действия.</summary>
        private static string НазваниеДействия(DocumentMovementAction действие) => действие switch
        {
            DocumentMovementAction.Reception => "Приём",
            DocumentMovementAction.Execution => "Передача на исполнение",
            _ => "В архив",
        };
    }
}
