using System.Windows.Forms;
using Picket302.Infrastructure.Persistence;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Режим выбора подключения в диалоге «Подключение к базе данных»
/// (ТЗ §8.8, функция 101: «выбор „Этот компьютер / Сервер / Новый“»).
/// </summary>
public enum ConnectionChoice
{
    /// <summary>Локальный режим: файл базы данных на этом компьютере.</summary>
    ThisComputer = 0,

    /// <summary>Сервер из справочника servers.ini.</summary>
    KnownServer = 1,

    /// <summary>Новый сервер: реквизиты вводятся вручную.</summary>
    NewServer = 2,
}

/// <summary>
/// Диалог «Подключение к базе данных» и «Смены подключения»
/// (ТЗ §8.8, функции 101 и 102; §8.11, функция 134).
/// </summary>
/// <remarks>
/// Диалог открывается и до главного окна — это скрытый вход администратора
/// при недоступности сервера.
/// </remarks>
public sealed class FormConnectionSettings : Form
{
    private readonly RadioButton _этотКомпьютер = new() { Text = "Этот компьютер" };
    private readonly RadioButton _серверИзСписка = new() { Text = "Сервер из списка" };
    private readonly RadioButton _новыйСервер = new() { Text = "Новый сервер" };

    private readonly ComboBox _списокСерверов = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _хост = new();
    private readonly NumericUpDown _порт = new() { Minimum = 1, Maximum = 65535, Value = 5432 };
    private readonly TextBox _имяБазы = new();
    private readonly TextBox _пользователь = new();
    private readonly TextBox _пароль = new() { UseSystemPasswordChar = true };
    private readonly TextBox _путьКБазе = new();
    private readonly Label _состояние = new();
    private readonly Button _проверить = new();
    private readonly Button _применить = new();
    private readonly Button _отмена = new();

    private readonly ConnectionManager _менеджер;
    private readonly ServerRegistry _реестрСерверов;

    /// <summary>
    /// Создаёт диалог подключения.
    /// </summary>
    /// <param name="менеджер">Менеджер подключения.</param>
    /// <param name="реестрСерверов">Справочник серверов.</param>
    public FormConnectionSettings(ConnectionManager менеджер, ServerRegistry реестрСерверов)
    {
        ArgumentNullException.ThrowIfNull(менеджер);
        ArgumentNullException.ThrowIfNull(реестрСерверов);

        _менеджер = менеджер;
        _реестрСерверов = реестрСерверов;

        Text = "Подключение к базе данных";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 330);

        ПостроитьИнтерфейс();
        ЗаполнитьТекущиеЗначения();
        ОбновитьДоступность();
    }

    /// <summary>
    /// Выбранный режим подключения.
    /// </summary>
    public ConnectionChoice Choice =>
        _этотКомпьютер.Checked ? ConnectionChoice.ThisComputer
        : _серверИзСписка.Checked ? ConnectionChoice.KnownServer
        : ConnectionChoice.NewServer;

    /// <summary>
    /// Реквизиты подключения, составленные по полям диалога.
    /// </summary>
    public ConnectionDescriptor Descriptor
    {
        get
        {
            if (Choice == ConnectionChoice.ThisComputer)
            {
                return ConnectionDescriptor.Local(_путьКБазе.Text);
            }

            var сервер = Choice == ConnectionChoice.KnownServer
                ? _списокСерверов.SelectedItem as ServerEntry
                : null;

            return new ConnectionDescriptor(
                DatabaseKind.ServerPostgres,
                сервер is not null ? сервер.Host : _хост.Text.Trim(),
                сервер is not null ? сервер.Port : (int)_порт.Value,
                _имяБазы.Text.Trim(),
                _пользователь.Text.Trim(),
                _менеджер.Current.ProtectedPassword,
                string.Empty);
        }
    }

    /// <summary>
    /// Текст последней ошибки проверки подключения; пустая строка — ошибок нет.
    /// </summary>
    public string StatusText => _состояние.Text;

    /// <summary>
    /// Применяет выбранные реквизиты подключения.
    /// </summary>
    /// <returns>Текст ошибки; пустая строка — подключение применено.</returns>
    public string Apply()
    {
        var реквизиты = Descriptor;

        try
        {
            _менеджер.SwitchConnectionWithPassword(реквизиты, _пароль.Text);
        }
        catch (InvalidOperationException ошибка)
        {
            _состояние.Text = ошибка.Message;

            return ошибка.Message;
        }

        _состояние.Text = "Реквизиты подключения сохранены. Изменения применяются после перезапуска программы.";

        return string.Empty;
    }

    /// <summary>
    /// Проверяет доступность выбранного сервера.
    /// </summary>
    /// <returns>Текст результата проверки.</returns>
    public string CheckServer()
    {
        if (Choice == ConnectionChoice.ThisComputer)
        {
            _состояние.Text = "Локальный режим: проверка сети не требуется.";

            return _состояние.Text;
        }

        var сервер = Choice == ConnectionChoice.KnownServer
            ? _списокСерверов.SelectedItem as ServerEntry
            : new ServerEntry("Указанный сервер", _хост.Text.Trim(), (int)_порт.Value, string.Empty);

        if (сервер is null)
        {
            _состояние.Text = "Сервер не выбран.";

            return _состояние.Text;
        }

        var результат = _реестрСерверов.CheckServer(сервер);

        _состояние.Text = результат.IsSuccess
            ? $"Сервер «{сервер.Name}:{сервер.Port}» доступен. Задержка: {результат.LatencyMilliseconds} мс."
            : результат.Error ?? "Сервер недоступен.";

        return _состояние.Text;
    }

    /// <summary>
    /// Строит элементы формы.
    /// </summary>
    private void ПостроитьИнтерфейс()
    {
        var выбор = new GroupBox
        {
            Text = "Подключение",
            Dock = DockStyle.Top,
            Height = 66,
        };

        _этотКомпьютер.Location = new Point(12, 22);
        _серверИзСписка.Location = new Point(130, 22);
        _новыйСервер.Location = new Point(250, 22);

        выбор.Controls.Add(_этотКомпьютер);
        выбор.Controls.Add(_серверИзСписка);
        выбор.Controls.Add(_новыйСервер);

        var таблица = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 196,
            ColumnCount = 2,
        };

        таблица.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        таблица.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _списокСерверов.Width = 280;

        ДобавитьСтроку(таблица, 0, "Сервер из списка:", _списокСерверов);
        ДобавитьСтроку(таблица, 1, "Адрес сервера:", _хост);
        ДобавитьСтроку(таблица, 2, "Порт:", _порт);
        ДобавитьСтроку(таблица, 3, "Имя базы данных:", _имяБазы);
        ДобавитьСтроку(таблица, 4, "Пользователь:", _пользователь);
        ДобавитьСтроку(таблица, 5, "Пароль:", _пароль);
        ДобавитьСтроку(таблица, 6, "Файл базы данных:", _путьКБазе);

        _состояние.Dock = DockStyle.Top;
        _состояние.Height = 32;
        _состояние.AutoEllipsis = true;

        _проверить.Text = "Проверить соединение";
        _проверить.AccessibleName = "Проверить соединение";
        _проверить.AccessibleDescription = "Проверить доступность выбранного подключения.";
        _проверить.Size = new Size(140, 24);
        _проверить.Location = new Point(12, 296);
        _проверить.Click += (_, _) => CheckServer();

        _применить.Text = "Применить";
        _применить.AccessibleName = "Применить настройки подключения";
        _применить.AccessibleDescription = "Сохранить выбранные реквизиты подключения.";
        _применить.Size = new Size(90, 24);
        _применить.Location = new Point(266, 296);
        _применить.Click += (_, _) =>
        {
            if (Apply().Length == 0)
            {
                DialogResult = DialogResult.OK;
            }
        };

        _отмена.Text = "Отмена";
        _отмена.AccessibleName = "Отмена";
        _отмена.AccessibleDescription = "Закрыть диалог без применения изменений.";
        _отмена.Size = new Size(90, 24);
        _отмена.Location = new Point(362, 296);
        _отмена.DialogResult = DialogResult.Cancel;

        Controls.Add(_отмена);
        Controls.Add(_применить);
        Controls.Add(_проверить);
        Controls.Add(_состояние);
        Controls.Add(таблица);
        Controls.Add(выбор);

        _этотКомпьютер.CheckedChanged += (_, _) => ОбновитьДоступность();
        _серверИзСписка.CheckedChanged += (_, _) => ОбновитьДоступность();
        _новыйСервер.CheckedChanged += (_, _) => ОбновитьДоступность();

        AcceptButton = _применить;
        CancelButton = _отмена;
    }

    /// <summary>
    /// Добавляет строку формы в таблицу.
    /// </summary>
    private static void ДобавитьСтроку(TableLayoutPanel таблица, int строка, string текст, Control поле)
    {
        таблица.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var метка = new Label
        {
            Text = текст,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };

        поле.Dock = DockStyle.Fill;

        таблица.Controls.Add(метка, 0, строка);
        таблица.Controls.Add(поле, 1, строка);
    }

    /// <summary>
    /// Заполняет поля текущими реквизитами подключения.
    /// </summary>
    private void ЗаполнитьТекущиеЗначения()
    {
        foreach (var сервер in _реестрСерверов.ListServers())
        {
            _списокСерверов.Items.Add(сервер);
        }

        var текущий = _менеджер.Current;

        _путьКБазе.Text = текущий.DatabasePath;
        _хост.Text = текущий.Server;
        _порт.Value = текущий.Port > 0 ? текущий.Port : 5432;
        _имяБазы.Text = текущий.DatabaseName;
        _пользователь.Text = текущий.User;

        if (текущий.IsLocal)
        {
            _этотКомпьютер.Checked = true;
        }
        else
        {
            _серверИзСписка.Checked = true;
        }
    }

    /// <summary>
    /// Включает и отключает поля по выбранному режиму.
    /// </summary>
    private void ОбновитьДоступность()
    {
        var локальный = _этотКомпьютер.Checked;
        var изСписка = _серверИзСписка.Checked;

        _путьКБазе.Enabled = локальный;
        _списокСерверов.Enabled = изСписка;
        _хост.Enabled = !локальный && !изСписка;
        _порт.Enabled = !локальный && !изСписка;
        _имяБазы.Enabled = !локальный;
        _пользователь.Enabled = !локальный;
        _пароль.Enabled = !локальный;
    }
}
