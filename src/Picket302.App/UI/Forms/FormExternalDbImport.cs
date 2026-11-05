using System.Data.Common;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Repositories;
using Picket302.Infrastructure.Integration;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог «Импорт из внешней БД» (ТЗ §8.5, функция 76: «Диалог «Импорт из
/// внешней БД»: строка подключения, кнопка «Проверить соединение», выбор
/// таблицы, запуск мастера соответствия полей»).
/// </summary>
/// <remarks>
/// Пароль внешней базы не хранится в открытом виде: строка подключения
/// вводится оператором, а пароль хранится средствами Windows (DPAPI) в
/// защищённом блоке и подставляется в строку подключения только на время
/// сеанса. В журнал и в сообщения об ошибках пароль не попадает.
/// </remarks>
public sealed class FormExternalDbImport : Form
{
    private readonly ICounterpartyRepository _контрагенты;
    private readonly ExternalDbImporter _импортёр;
    private readonly DpapiSecretStore _секреты = new();

    private readonly ComboBox _видБазы = new();
    private readonly TextBox _строкаПодключения = new();
    private readonly TextBox _пароль = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _запомнитьПароль = new();
    private readonly ListBox _таблицы = new();
    private readonly DataGridView _соответствие = new();
    private readonly ComboBox _политика = new();
    private readonly Label _статус = new();
    private readonly Button _проверить = new();
    private readonly Button _обновить = new();
    private readonly Button _импортировать = new();
    private readonly Button _отмена = new();

    /// <summary>
    /// Создаёт диалог импорта из внешней базы данных.
    /// </summary>
    /// <param name="контрагенты">Репозиторий контрагентов адресной книги.</param>
    /// <param name="импортёр">Импортёр внешней базы; при отсутствии — создаётся по умолчанию.</param>
    public FormExternalDbImport(
        ICounterpartyRepository контрагенты,
        ExternalDbImporter? импортёр = null)
    {
        _контрагенты = контрагенты ?? throw new ArgumentNullException(nameof(контрагенты));
        _импортёр = импортёр ?? new ExternalDbImporter();

        Text = "Импорт из внешней БД";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(720, 460);

        ПостроитьПараметры();
        ПостроитьТаблицы();
        ПостроитьСоответствие();
        ПостроитьКнопки();

        // Пароль восстанавливается из защищённого блока DPAPI при указании
        // строки подключения, к которой он сохранён.
        _строкаПодключения.TextChanged += (_, _) => ВосстановитьПароль();

        AcceptButton = _импортировать;
        CancelButton = _отмена;
    }

    /// <summary>Отчёт о выполненном импорте.</summary>
    public MergeReport? Отчёт { get; private set; }

    /// <summary>
    /// Строит блок параметров подключения.
    /// </summary>
    private void ПостроитьПараметры()
    {
        var меткаВида = new Label
        {
            Text = "Вид базы:",
            AutoSize = true,
            Location = new Point(10, 12),
        };

        _видБазы.DropDownStyle = ComboBoxStyle.DropDownList;
        _видБазы.Location = new Point(80, 9);
        _видБазы.Width = 160;
        _видБазы.Items.AddRange(
        [
            new DbKindItem(DbKind.Sqlite, "SQLite"),
            new DbKindItem(DbKind.MySql, "MySQL"),
            new DbKindItem(DbKind.SqlServer, "SQL Server"),
        ]);
        _видБазы.SelectedIndex = 0;

        var меткаСтроки = new Label
        {
            Text = "Строка подключения:",
            AutoSize = true,
            Location = new Point(10, 38),
        };

        _строкаПодключения.Location = new Point(110, 35);
        _строкаПодключения.Width = 590;
        _строкаПодключения.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var меткаПароля = new Label
        {
            Text = "Пароль:",
            AutoSize = true,
            Location = new Point(10, 60),
        };

        _пароль.Location = new Point(110, 57);
        _пароль.Width = 240;
        _пароль.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        _запомнитьПароль.Text = "Запомнить пароль (защита DPAPI)";
        _запомнитьПароль.AutoSize = true;
        _запомнитьПароль.Location = new Point(360, 59);
        _запомнитьПароль.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        _проверить.Text = "Проверить соединение";
        _проверить.Size = new Size(140, 24);
        _проверить.Location = new Point(560, 86);
        _проверить.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _проверить.Click += (_, _) => ПроверитьСоединение();

        _статус.AutoSize = false;
        _статус.Location = new Point(10, 90);
        _статус.Size = new Size(540, 20);
        _статус.Text = "Укажите строку подключения к внешней базе данных.";

        Controls.Add(меткаВида);
        Controls.Add(_видБазы);
        Controls.Add(меткаСтроки);
        Controls.Add(_строкаПодключения);
        Controls.Add(меткаПароля);
        Controls.Add(_пароль);
        Controls.Add(_запомнитьПароль);
        Controls.Add(_проверить);
        Controls.Add(_статус);
    }

    /// <summary>
    /// Строит блок выбора таблицы внешней базы.
    /// </summary>
    private void ПостроитьТаблицы()
    {
        var метка = new Label
        {
            Text = "Таблица:",
            AutoSize = true,
            Location = new Point(10, 96),
        };

        _таблицы.Location = new Point(10, 114);
        _таблицы.Size = new Size(260, 140);
        _таблицы.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
        _таблицы.SelectedIndexChanged += (_, _) => ОбновитьСоответствие();

        _обновить.Text = "Обновить список";
        _обновить.Size = new Size(140, 24);
        _обновить.Location = new Point(130, 258);
        _обновить.Click += (_, _) => ОбновитьТаблицы();

        Controls.Add(метка);
        Controls.Add(_таблицы);
        Controls.Add(_обновить);
    }

    /// <summary>
    /// Строит таблицу соответствия колонок внешней таблицы и полей.
    /// </summary>
    private void ПостроитьСоответствие()
    {
        var метка = new Label
        {
            Text = "Соответствие полей:",
            AutoSize = true,
            Location = new Point(290, 96),
        };

        _соответствие.Location = new Point(290, 114);
        _соответствие.Size = new Size(410, 140);
        _соответствие.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _соответствие.AllowUserToAddRows = false;
        _соответствие.AllowUserToDeleteRows = false;
        _соответствие.RowHeadersVisible = false;
        _соответствие.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _соответствие.Columns.Add("Колонка", "Колонка базы");

        // Поле адресной книги выбирается выпадающим списком (F-E5): прежде
        // второй столбец был текстовым, ячейки ComboBox не создавались, и
        // сопоставление полей оставалось пустым.
        var поле = new DataGridViewComboBoxColumn
        {
            Name = "Поле",
            HeaderText = "Поле адресной книги",
            FlatStyle = FlatStyle.Flat,
        };

        поле.Items.Add("— не использовать —");
        поле.Items.AddRange(ОписанияПолей());

        _соответствие.Columns.Add(поле);

        Controls.Add(метка);
        Controls.Add(_соответствие);
    }

    /// <summary>
    /// Строит нижний блок кнопок и выбор политики слияния.
    /// </summary>
    private void ПостроитьКнопки()
    {
        var меткаПолитики = new Label
        {
            Text = "Если запись уже есть:",
            AutoSize = true,
            Location = new Point(10, 296),
        };

        _политика.DropDownStyle = ComboBoxStyle.DropDownList;
        _политика.Location = new Point(140, 293);
        _политика.Width = 220;
        _политика.Items.AddRange(
        [
            new MergePolicyItem(MergePolicy.SkipExisting, "Пропустить существующего"),
            new MergePolicyItem(MergePolicy.FillEmpty, "Дополнить пустые поля"),
            new MergePolicyItem(MergePolicy.Overwrite, "Перезаписать значения"),
        ]);
        _политика.SelectedIndex = 0;

        _импортировать.Text = "Импортировать";
        _импортировать.Size = new Size(120, 24);
        _импортировать.Location = new Point(460, 420);
        _импортировать.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _импортировать.Click += (_, _) => ВыполнитьИмпорт();

        _отмена.Text = "Закрыть";
        _отмена.Size = new Size(100, 24);
        _отмена.Location = new Point(590, 420);
        _отмена.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        Controls.Add(меткаПолитики);
        Controls.Add(_политика);
        Controls.Add(_импортировать);
        Controls.Add(_отмена);
    }

    /// <summary>
    /// Проверяет подключение к внешней базе данных.
    /// </summary>
    private void ПроверитьСоединение()
    {
        var строка = СобратьСтрокуПодключения();

        if (строка.Length == 0)
        {
            _статус.Text = "Укажите строку подключения.";
            return;
        }

        _статус.Text = "Проверка соединения…";
        Refresh();

        СохранитьПароль();

        var результат = _импортёр.TestConnectionAsync(строка, ВыбранныйВидБазы());

        _статус.Text = результат.IsSuccess
            ? "Соединение установлено."
            : результат.Error;
    }

    /// <summary>
    /// Обновляет перечень таблиц внешней базы данных.
    /// </summary>
    private void ОбновитьТаблицы()
    {
        var строка = СобратьСтрокуПодключения();

        if (строка.Length == 0)
        {
            _статус.Text = "Укажите строку подключения.";
            return;
        }

        _статус.Text = "Получение списка таблиц…";
        Refresh();

        var результат = _импортёр.ListTablesAsync(строка, ВыбранныйВидБазы());

        if (!результат.IsSuccess)
        {
            _статус.Text = результат.Error;
            return;
        }

        _таблицы.Items.Clear();

        foreach (var таблица in результат.Value)
        {
            _таблицы.Items.Add(таблица);
        }

        _статус.Text = $"Таблиц в базе: {результат.Value.Count}.";

        if (_таблицы.Items.Count > 0)
        {
            _таблицы.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Перестраивает таблицу соответствия по выбранной таблице.
    /// </summary>
    private void ОбновитьСоответствие()
    {
        _соответствие.Rows.Clear();

        if (_таблицы.SelectedItem is not string таблица)
        {
            return;
        }

        var результат = _импортёр.ListColumnsAsync(
            СобратьСтрокуПодключения(),
            ВыбранныйВидБазы(),
            таблица);

        if (!результат.IsSuccess)
        {
            _статус.Text = результат.Error;
            return;
        }

        var автоматическое = ExternalDbColumnMap.CreateAuto(результат.Value);

        foreach (var колонка in результат.Value)
        {
            var индекс = _соответствие.Rows.Add(колонка);
            var строка = _соответствие.Rows[индекс];
            var поле = (DataGridViewComboBoxCell)строка.Cells[1];

            // Прогрев CreateAuto пишет выбранное поле в ячейку списка (F-E5).
            var найденное = НайтиПолеПоКолонке(автоматическое, колонка);

            поле.Value = найденное is null
                ? "— не использовать —"
                : ЗаголовокПоля(найденное.Value);
        }
    }

    /// <summary>
    /// Выполняет импорт контрагентов из внешней базы данных.
    /// </summary>
    private void ВыполнитьИмпорт()
    {
        var строка = СобратьСтрокуПодключения();

        if (строка.Length == 0)
        {
            _статус.Text = "Укажите строку подключения.";
            return;
        }

        if (_таблицы.SelectedItem is not string таблица)
        {
            _статус.Text = "Выберите таблицу с контрагентами.";
            return;
        }

        _статус.Text = "Импорт данных…";
        Refresh();

        СохранитьПароль();

        try
        {
            var соответствие = СобратьСоответствие();

            if (соответствие is null)
            {
                _статус.Text = "Не удалось определить соответствие колонок.";
                return;
            }

            var пакет = _импортёр.ImportFromExternalDb(
                строка,
                ВыбранныйВидБазы(),
                таблица,
                соответствие);

            if (пакет.IsFailure)
            {
                _статус.Text = пакет.Error;
                return;
            }

            var сервис = new ImportMergeService(_контрагенты);
            Отчёт = сервис.MergeImported(пакет.Value, ВыбраннаяПолитика());

            _статус.Text =
                $"Импортировано: создано {Отчёт.Created}, обновлено {Отчёт.Updated}, " +
                $"пропущено {Отчёт.Skipped}, конфликтов {Отчёт.Conflicts}.";

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ошибка)
        {
            _статус.Text = $"Не удалось выполнить импорт: {ошибка.Message}";
        }
    }

    /// <summary>
    /// Собирает соответствие колонок внешней таблицы и полей по таблице формы.
    /// </summary>
    /// <returns>Соответствие либо <see langword="null"/>, если сопоставлено ни одного поля.</returns>
    private ExternalDbColumnMap? СобратьСоответствие()
    {
        var соответствие = ExternalDbColumnMap.CreateEmpty();

        foreach (DataGridViewRow строка in _соответствие.Rows)
        {
            if (строка.Cells[0].Value is not string колонка)
            {
                continue;
            }

            if (строка.Cells[1].Value is not string заголовок)
            {
                continue;
            }

            var поле = НайтиПолеПоЗаголовку(заголовок);

            if (поле is null)
            {
                continue;
            }

            соответствие = соответствие.Set(поле.Value, колонка);
        }

        return соответствие.IsEmpty ? null : соответствие;
    }

    /// <summary>
    /// Ищет поле по русскому заголовку.
    /// </summary>
    /// <param name="заголовок">Заголовок поля.</param>
    /// <returns>Поле либо <see langword="null"/>.</returns>
    private static ExternalDbField? НайтиПолеПоЗаголовку(string заголовок)
    {
        var описания = ОписанияПолей();

        for (var индекс = 0; индекс < описания.Length; индекс++)
        {
            if (string.Equals(описания[индекс], заголовок, StringComparison.Ordinal))
            {
                return (ExternalDbField)индекс;
            }
        }

        return null;
    }

    /// <summary>
    /// Возвращает выбранный вид внешней базы данных.
    /// </summary>
    /// <returns>Вид базы данных.</returns>
    private DbKind ВыбранныйВидБазы() =>
        _видБазы.SelectedItem is DbKindItem элемент ? элемент.Вид : DbKind.Sqlite;

    /// <summary>
    /// Собирает строку подключения с паролем, введённым оператором.
    /// </summary>
    /// <returns>Строка подключения для импортёра.</returns>
    private string СобратьСтрокуПодключения()
    {
        var строка = _строкаПодключения.Text.Trim();

        if (строка.Length == 0 || _пароль.Text.Length == 0)
        {
            return строка;
        }

        var построитель = new DbConnectionStringBuilder { ConnectionString = строка };

        построитель["Password"] = _пароль.Text;

        return построитель.ConnectionString;
    }

    /// <summary>
    /// Сохраняет пароль в хранилище DPAPI либо удаляет ранее сохранённый —
    /// согласно отметке «Запомнить пароль» (ТЗ §8.5, функция 76: «пароль —
    /// в DPAPI»).
    /// </summary>
    private void СохранитьПароль()
    {
        var имя = ИмяСекрета();

        try
        {
            if (_запомнитьПароль.Checked && _пароль.Text.Length > 0)
            {
                _секреты.Save(имя, _пароль.Text);
            }
            else
            {
                _секреты.Delete(имя);
            }
        }
        catch (CryptographicException ошибка)
        {
            _статус.Text = $"Не удалось сохранить пароль средствами Windows (DPAPI): {ошибка.Message}";
        }
        catch (IOException ошибка)
        {
            _статус.Text = $"Не удалось сохранить пароль: {ошибка.Message}";
        }
    }

    /// <summary>
    /// Восстанавливает пароль из хранилища DPAPI для указанной строки
    /// подключения.
    /// </summary>
    private void ВосстановитьПароль()
    {
        if (_пароль.Text.Length > 0)
        {
            return;
        }

        var имя = ИмяСекрета();

        if (!_секреты.Exists(имя))
        {
            return;
        }

        try
        {
            _пароль.Text = _секреты.Load(имя);
            _запомнитьПароль.Checked = true;
        }
        catch (CryptographicException ошибка)
        {
            _статус.Text = $"Не удалось расшифровать сохранённый пароль: {ошибка.Message}";
        }
        catch (IOException ошибка)
        {
            _статус.Text = $"Сохранённый пароль не найден: {ошибка.Message}";
        }
    }

    /// <summary>
    /// Имя записи хранилища DPAPI для пароля: определяется видом базы и строкой
    /// подключения без пароля, поэтому для разных баз секреты различаются.
    /// </summary>
    /// <returns>Имя секрета.</returns>
    private string ИмяСекрета()
    {
        var основа = $"{ВыбранныйВидБазы()}|{_строкаПодключения.Text.Trim()}";
        var хеш = SHA256.HashData(Encoding.UTF8.GetBytes(основа));

        return "ExternalDb." + Convert.ToHexString(хеш)[..16];
    }

    /// <summary>
    /// Возвращает выбранную политику слияния.
    /// </summary>
    /// <returns>Политика слияния.</returns>
    private MergePolicy ВыбраннаяПолитика() =>
        _политика.SelectedItem is MergePolicyItem элемент ? элемент.Политика : MergePolicy.SkipExisting;

    /// <summary>
    /// Возвращает перечень полей с их русскими заголовками.
    /// </summary>
    /// <returns>Описания полей.</returns>
    private static string[] ОписанияПолей() =>
    [
        "Наименование",
        "Сокращённое наименование",
        "ИНН",
        "КПП",
        "ОГРН",
        "Телефон",
        "E-mail",
        "Примечание",
        "Почтовый индекс",
        "Почтовый адрес",
        "Индекс юридического адреса",
        "Юридический адрес",
        "Индекс фактического адреса",
        "Фактический адрес",
    ];

    /// <summary>
    /// Возвращает заголовок поля по его значению.
    /// </summary>
    /// <param name="поле">Поле.</param>
    /// <returns>Русский заголовок поля.</returns>
    private static string ЗаголовокПоля(ExternalDbField поле) =>
        ОписанияПолей()[(int)поле];

    /// <summary>
    /// Ищет поле, автоматически сопоставленное столбцу.
    /// </summary>
    /// <param name="соответствие">Автоматическое соответствие.</param>
    /// <param name="колонка">Имя столбца.</param>
    /// <returns>Поле либо <see langword="null"/>.</returns>
    private static ExternalDbField? НайтиПолеПоКолонке(ExternalDbColumnMap соответствие, string колонка)
    {
        foreach (var отображение in соответствие.Mappings)
        {
            if (string.Equals(отображение.Value, колонка, StringComparison.OrdinalIgnoreCase))
            {
                return отображение.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// Элемент выбора вида внешней базы данных.
    /// </summary>
    /// <param name="Вид">Вид базы данных.</param>
    /// <param name="Название">Название для интерфейса.</param>
    private sealed record DbKindItem(DbKind Вид, string Название)
    {
        /// <inheritdoc />
        public override string ToString() => Название;
    }

    /// <summary>
    /// Элемент выбора политики слияния.
    /// </summary>
    /// <param name="Политика">Политика слияния.</param>
    /// <param name="Название">Название для интерфейса.</param>
    private sealed record MergePolicyItem(MergePolicy Политика, string Название)
    {
        /// <inheritdoc />
        public override string ToString() => Название;
    }
}
