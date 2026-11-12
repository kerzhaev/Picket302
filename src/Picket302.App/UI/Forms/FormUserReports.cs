using System.Drawing;
using System.Windows.Forms;
using Picket302.Application.Services;
using Picket302.App.UI.Components;
using Picket302.Domain.Queries;
using Picket302.Infrastructure.Queries;
using Picket302.Printing.Reports;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог пользовательских отчётов (ТЗ §8.4, функция 56): перечень
/// зарегистрированных пакетов «макет .lbk + query.xml» из
/// <c>Templates/Reports/User</c>, регистрация нового отчёта, правка запроса
/// с валидацией и предпросмотр данных поверх текущей базы.
/// </summary>
/// <remarks>
/// Синтаксическая ошибка в пользовательском шаблоне изолируется и выводится
/// в диалоге валидации (ТЗ §8.4, функция 56): ни регистрация, ни предпросмотр
/// не прерывают работу приложения, а невалидный запрос не сохраняется при
/// правке (безопасность данных).
/// </remarks>
public sealed class FormUserReports : Form
{
    /// <summary>Ограничение строк предпросмотра данных по умолчанию.</summary>
    public const int ЛимитСтрокПредпросмотра = 50;

    private readonly CustomReportManager _менеджер;
    private readonly SqliteReportDataProvider _поставщик;

    private readonly ListView _список = new();
    private readonly Label _статус = new();
    private readonly TextBox _наименование = new();
    private readonly TextBox _пакет = new();
    private readonly TextBox _запрос = new();
    private readonly Button _зарегистрировать = new();
    private readonly Button _выбратьПакет = new();
    private readonly Button _выбратьЗапрос = new();
    private readonly Button _изменитьЗапрос = new();
    private readonly Button _проверить = new();
    private readonly Button _просмотрДанных = new();
    private readonly Button _закрыть = new();

    /// <summary>
    /// Точка подмены уведомлений в автоматических проверках: по умолчанию —
    /// <see cref="MessageBox.Show(IWin32Window, string, string)"/>; проверки
    /// передают делегат без модальных окон. Аргументы: текст, заголовок.
    /// </summary>
    internal Action<string, string>? УведомитьДляПроверок { get; set; }

    /// <summary>
    /// Создаёт диалог пользовательских отчётов.
    /// </summary>
    /// <param name="менеджер">Менеджер пользовательских отчётов.</param>
    /// <param name="поставщик">Исполнитель запросов поверх текущей базы.</param>
    public FormUserReports(CustomReportManager менеджер, SqliteReportDataProvider поставщик)
    {
        ArgumentNullException.ThrowIfNull(менеджер);
        ArgumentNullException.ThrowIfNull(поставщик);

        _менеджер = менеджер;
        _поставщик = поставщик;

        Text = "Пользовательские отчёты";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(620, 440);

        ПостроитьИнтерфейс();
        EnvelopeUiTheme.ОформитьОкно(this);
        EnvelopeUiTheme.ОформитьОсновнуюКнопку(_закрыть);

        ОбновитьСписок();
    }

    /// <summary>Число зарегистрированных отчётов в списке.</summary>
    internal int ЧислоОтчётов => _список.Items.Count;

    /// <summary>Наименование выделенного отчёта либо <see langword="null"/>.</summary>
    public string? ВыбранноеИмя =>
        _список.SelectedItems.Cast<ListViewItem>().FirstOrDefault()?.Text;

    /// <summary>Каталог пользовательских отчётов (для подсказок статуса).</summary>
    public string КаталогОтчётов => _менеджер.UserReportsDirectory;

    /// <summary>
    /// Перечитывает перечень зарегистрированных отчётов.
    /// </summary>
    /// <returns>Число отчётов в списке.</returns>
    internal int ОбновитьСписок()
    {
        _список.BeginUpdate();
        _список.Items.Clear();

        foreach (var отчёт in _менеджер.ListUserReports())
        {
            var пункт = new ListViewItem(отчёт.Name)
            {
                Tag = отчёт,
                ToolTipText = отчёт.TemplatePath.Length == 0
                    ? "Пакет макета (.lbk) в каталоге отчёта не найден"
                    : отчёт.TemplatePath,
            };

            пункт.SubItems.Add(отчёт.TemplatePath.Length == 0 ? "—" : отчёт.TemplatePath);
            пункт.SubItems.Add(отчёт.QueryXmlPath);

            _список.Items.Add(пункт);
        }

        _список.EndUpdate();
        ОбновитьДоступность();

        return _список.Items.Count;
    }

    /// <summary>
    /// Регистрирует пользовательский отчёт из указанных файлов (ТЗ §8.4,
    /// функция 56). Ошибки регистрации (занятое наименование, отсутствующие
    /// файлы) изолируются и возвращаются как результат валидации.
    /// </summary>
    /// <param name="имя">Наименование отчёта.</param>
    /// <param name="пакет">Путь к пакету макета (.lbk).</param>
    /// <param name="запрос">Путь к файлу query.xml.</param>
    /// <returns>Результат валидации.</returns>
    internal async Task<UserReportValidationResult> ЗарегистрироватьAsync(
        string имя,
        string пакет,
        string запрос)
    {
        UserReportValidationResult результат;

        try
        {
            результат = await _менеджер
                .RegisterUserReport(имя, пакет, запрос)
                .ConfigureAwait(true);
        }
        catch (Exception ошибка) when (ошибка is ArgumentException
            or FileNotFoundException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            результат = new UserReportValidationResult(
                false,
                имя,
                [ошибка.Message]);
        }

        ОбновитьСписок();
        ПоказатьВалидацию(результат);

        return результат;
    }

    /// <summary>
    /// Проверяет и сохраняет новый текст query.xml отчёта (правка запроса,
    /// ТЗ §4.1). Невалидный текст не сохраняется — данные отчёта не
    /// повреждаются; любые ошибки (включая пустой текст и блокировки файлов)
    /// изолируются в результате валидации, наружу не бросаются
    /// (D-S27-01, D-S27-04).
    /// </summary>
    /// <param name="имя">Наименование отчёта.</param>
    /// <param name="текстЗапроса">Новый текст query.xml.</param>
    /// <returns>Результат валидации.</returns>
    internal async Task<UserReportValidationResult> СохранитьЗапросAsync(string имя, string текстЗапроса)
    {
        UserReportValidationResult результат;

        try
        {
            результат = await _менеджер
                .EditUserQueryAsync(имя, текстЗапроса)
                .ConfigureAwait(true);
        }
        catch (Exception ошибка) when (ошибка is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            результат = new UserReportValidationResult(
                false,
                имя,
                [ошибка.Message]);
        }

        ПоказатьВалидацию(результат);

        return результат;
    }

    /// <summary>
    /// Проверяет зарегистрированный отчёт: разбор и валидация query.xml.
    /// Синтаксическая ошибка изолируется и возвращается текстом.
    /// </summary>
    /// <param name="имя">Наименование отчёта.</param>
    /// <returns>Текст результата валидации.</returns>
    internal async Task<string> ПроверитьТекстомAsync(string имя)
    {
        try
        {
            var определение = await _менеджер
                .LoadUserQueryAsync(имя)
                .ConfigureAwait(true);

            var валидатор = new QueryValidator();
            валидатор.ValidateDefinition(определение);

            return $"Отчёт «{имя}»: запрос корректен (id «{определение.Id}»).";
        }
        catch (Exception ошибка) when (ошибка is ReportQueryXmlException
            or QueryValidationException
            or System.Xml.XmlException
            or FileNotFoundException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            return $"Отчёт «{имя}»: ошибка валидации — {ошибка.Message}";
        }
    }

    /// <summary>
    /// Предпросмотр данных отчёта: исполнение запроса поверх текущей базы
    /// с ограничением числа строк. Ошибка исполнения изолируется и
    /// возвращается текстом, приложение продолжает работу.
    /// </summary>
    /// <param name="имя">Наименование отчёта.</param>
    /// <param name="максимумСтрок">Ограничение числа строк.</param>
    /// <returns>Многострочный текст предпросмотра.</returns>
    internal async Task<string> ПросмотрДанныхТекстомAsync(string имя, int максимумСтрок)
    {
        try
        {
            var определение = await _менеджер
                .LoadUserQueryAsync(имя)
                .ConfigureAwait(true);

            var значения = new QueryParameterValues();
            var строки = await _поставщик
                .RunDefinitionAsync(определение, значения)
                .ConfigureAwait(true);

            if (строки.Count == 0)
            {
                return $"Отчёт «{имя}»: данных нет (0 строк).";
            }

            var текст = new System.Text.StringBuilder();
            текст.AppendLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Отчёт «{имя}»: {строки.Count} строк, показано {Math.Min(максимумСтрок, строки.Count)}:"));

            foreach (var строка in строки.Take(максимумСтрок))
            {
                var колонки = строка.ColumnNames
                    .Select(имя_колонки => $"{имя_колонки}={ФорматЗначения(строка.GetValue(имя_колонки))}");

                текст.AppendLine("  " + string.Join("; ", колонки));
            }

            return текст.ToString().TrimEnd();
        }
        catch (Exception ошибка) when (ошибка is ReportQueryXmlException
            or QueryValidationException
            or System.Xml.XmlException
            or FileNotFoundException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or KeyNotFoundException
            or Microsoft.Data.Sqlite.SqliteException)
        {
            return $"Отчёт «{имя}»: ошибка исполнения — {ошибка.Message}";
        }
    }

    /// <summary>
    /// Текст результата валидации для диалога и журнала (ТЗ §8.4, функция 56:
    /// «синтаксическая ошибка … выводится в диалоге валидации»).
    /// </summary>
    /// <param name="результат">Результат валидации.</param>
    /// <returns>Готовый текст сообщения.</returns>
    internal static string ТекстВалидации(UserReportValidationResult результат)
    {
        if (результат.IsValid)
        {
            return $"Отчёт «{результат.ReportName}»: ошибок нет.";
        }

        var список = string.Join(
            Environment.NewLine,
            результат.Errors.Select(ошибка => "— " + ошибка));

        return $"Отчёт «{результат.ReportName}»: ошибок {результат.Errors.Count}:"
            + Environment.NewLine
            + список;
    }

    /// <summary>Строит элементы диалога.</summary>
    private void ПостроитьИнтерфейс()
    {
        _список.Dock = DockStyle.Fill;
        _список.View = View.Details;
        _список.MultiSelect = false;
        _список.FullRowSelect = true;
        _список.HideSelection = false;
        _список.AccessibleName = "Список пользовательских отчётов";
        _список.Columns.Add("Наименование", 180);
        _список.Columns.Add("Пакет макета", 230);
        _список.Columns.Add("Запрос данных", 260);
        _список.ItemSelectionChanged += (_, _) => ОбновитьДоступность();

        // Панель регистрации нового отчёта.
        var регистрация = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 128,
            Padding = new Padding(8, 8, 8, 0),
        };

        var сетка = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            AutoSize = true,
        };
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        сетка.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _наименование.AccessibleName = "Наименование отчёта";
        _наименование.Dock = DockStyle.Fill;
        _пакет.AccessibleName = "Пакет макета отчёта";
        _пакет.Dock = DockStyle.Fill;
        _пакет.ReadOnly = true;
        _запрос.AccessibleName = "Файл запроса отчёта";
        _запрос.Dock = DockStyle.Fill;
        _запрос.ReadOnly = true;

        _выбратьПакет.Text = "Выбрать пакет…";
        _выбратьПакет.AutoSize = true;
        _выбратьПакет.AccessibleName = "Выбрать пакет макета";
        _выбратьПакет.Click += (_, _) => ВыбратьФайл(_пакет, "Пакет макета (*.lbk)|*.lbk");

        _выбратьЗапрос.Text = "Выбрать запрос…";
        _выбратьЗапрос.AutoSize = true;
        _выбратьЗапрос.AccessibleName = "Выбрать файл запроса";
        _выбратьЗапрос.Click += (_, _) => ВыбратьФайл(_запрос, "Запрос данных (query.xml)|query.xml");

        _зарегистрировать.Text = "Зарегистрировать";
        _зарегистрировать.AutoSize = true;
        _зарегистрировать.AccessibleName = "Зарегистрировать пользовательский отчёт";
        _зарегистрировать.Click += async (_, _) =>
            await ЗарегистрироватьИзПолейAsync().ConfigureAwait(true);

        сетка.Controls.Add(new Label
        {
            Text = "Наименование:",
            AutoSize = true,
            Padding = new Padding(0, 6, 8, 0),
        }, 0, 0);
        сетка.Controls.Add(_наименование, 1, 0);
        сетка.Controls.Add(new Label { Text = string.Empty }, 2, 0);

        сетка.Controls.Add(new Label
        {
            Text = "Пакет макета:",
            AutoSize = true,
            Padding = new Padding(0, 6, 8, 0),
        }, 0, 1);
        сетка.Controls.Add(_пакет, 1, 1);
        сетка.Controls.Add(_выбратьПакет, 2, 1);

        сетка.Controls.Add(new Label
        {
            Text = "Запрос данных:",
            AutoSize = true,
            Padding = new Padding(0, 6, 8, 0),
        }, 0, 2);
        сетка.Controls.Add(_запрос, 1, 2);
        сетка.Controls.Add(_выбратьЗапрос, 2, 2);

        var строкаРегистрации = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 4, 0, 0),
        };
        строкаРегистрации.Controls.Add(_зарегистрировать);

        регистрация.Controls.Add(строкаРегистрации);
        регистрация.Controls.Add(сетка);

        // Строка статуса с последним результатом операции.
        _статус.Dock = DockStyle.Bottom;
        _статус.Height = 34;
        _статус.TextAlign = ContentAlignment.MiddleLeft;
        _статус.ForeColor = EnvelopeUiTheme.MutedText;
        _статус.AccessibleName = "Статус последней операции";

        // Кнопки действий над выделенным отчётом.
        var низ = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8, 0, 8, 0) };

        _изменитьЗапрос.Text = "Изменить запрос…";
        _изменитьЗапрос.AutoSize = true;
        _изменитьЗапрос.Enabled = false;
        _изменитьЗапрос.AccessibleName = "Изменить запрос выбранного отчёта";
        _изменитьЗапрос.Click += async (_, _) =>
            await ИзменитьЗапросВыделенногоAsync().ConfigureAwait(true);

        _проверить.Text = "Проверить";
        _проверить.AutoSize = true;
        _проверить.Enabled = false;
        _проверить.AccessibleName = "Проверить запрос выбранного отчёта";
        _проверить.Click += async (_, _) =>
        {
            if (ВыбранноеИмя is { } имя)
            {
                ПоказатьСтатус(await ПроверитьТекстомAsync(имя).ConfigureAwait(true));
            }
        };

        _просмотрДанных.Text = "Данные (предпросмотр)…";
        _просмотрДанных.AutoSize = true;
        _просмотрДанных.Enabled = false;
        _просмотрДанных.AccessibleName = "Предпросмотр данных выбранного отчёта";
        _просмотрДанных.Click += async (_, _) =>
        {
            if (ВыбранноеИмя is { } имя)
            {
                ПоказатьСтатус(await ПросмотрДанныхТекстомAsync(
                    имя, ЛимитСтрокПредпросмотра).ConfigureAwait(true));
            }
        };

        _закрыть.Text = "Закрыть";
        _закрыть.AutoSize = true;
        _закрыть.DialogResult = DialogResult.Cancel;
        _закрыть.AccessibleName = "Закрыть пользовательские отчёты";

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };

        кнопки.Controls.Add(_закрыть);
        кнопки.Controls.Add(_просмотрДанных);
        кнопки.Controls.Add(_проверить);
        кнопки.Controls.Add(_изменитьЗапрос);

        низ.Controls.Add(кнопки);

        Controls.Add(_список);
        Controls.Add(регистрация);
        Controls.Add(_статус);
        Controls.Add(низ);

        CancelButton = _закрыть;
    }

    /// <summary>Включает кнопки действий при наличии выделения.</summary>
    private void ОбновитьДоступность()
    {
        var есть = ВыбранноеИмя is not null;

        _изменитьЗапрос.Enabled = есть;
        _проверить.Enabled = есть;
        _просмотрДанных.Enabled = есть;
    }

    /// <summary>Открывает диалог выбора файла и помещает путь в поле.</summary>
    private void ВыбратьФайл(TextBox поле, string фильтр)
    {
        using var диалог = new OpenFileDialog
        {
            Filter = фильтр,
            CheckFileExists = true,
        };

        if (диалог.ShowDialog(this) == DialogResult.OK)
        {
            поле.Text = диалог.FileName;
        }
    }

    /// <summary>Регистрация из полей формы (кнопка «Зарегистрировать»).</summary>
    private async Task ЗарегистрироватьИзПолейAsync()
    {
        var имя = _наименование.Text.Trim();
        var пакет = _пакет.Text.Trim();
        var запрос = _запрос.Text.Trim();

        if (имя.Length == 0)
        {
            Сообщить("Укажите наименование отчёта.", "Регистрация отчёта");
            _наименование.Focus();

            return;
        }

        if (пакет.Length == 0 || запрос.Length == 0)
        {
            Сообщить(
                "Выберите пакет макета (.lbk) и файл запроса (query.xml).",
                "Регистрация отчёта");

            return;
        }

        await ЗарегистрироватьAsync(имя, пакет, запрос).ConfigureAwait(true);

        _наименование.Text = string.Empty;
        _пакет.Text = string.Empty;
        _запрос.Text = string.Empty;
    }

    /// <summary>
    /// Правка запроса выделенного отчёта: диалог с текстом query.xml,
    /// сохранение только после успешной валидации.
    /// </summary>
    private async Task ИзменитьЗапросВыделенногоAsync()
    {
        if (ВыбранноеИмя is not { } имя)
        {
            return;
        }

        string текущий;

        try
        {
            var путь = Path.Combine(
                _менеджер.UserReportsDirectory,
                имя,
                "query.xml");

            текущий = File.Exists(путь) ? await File.ReadAllTextAsync(путь).ConfigureAwait(true) : string.Empty;
        }
        catch (IOException ошибка)
        {
            Сообщить(ошибка.Message, "Изменение запроса");

            return;
        }

        using var диалог = new FormПравкиЗапроса(имя, текущий);

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await СохранитьЗапросAsync(имя, диалог.ТекстЗапроса).ConfigureAwait(true);
    }

    /// <summary>Показывает текст валидации и дублирует в строку статуса.</summary>
    private void ПоказатьВалидацию(UserReportValidationResult результат)
    {
        var текст = ТекстВалидации(результат);

        Сообщить(
            текст,
            результат.IsValid ? "Валидация отчёта" : "Ошибки валидации отчёта");
        ПоказатьСтатус(текст);
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

    /// <summary>Форматирует значение ячейки для текстового предпросмотра.</summary>
    private static string ФорматЗначения(object? значение) => значение switch
    {
        null => "—",
        DateTime момент => момент.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture),
        byte[] данные => $"<двоичные данные, {данные.Length} Б>",
        _ => значение.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Диалог правки текста query.xml: сохранение возвращает OK только при
    /// нажатии «Проверить и сохранить»; валидация выполняется вызывающей
    /// формой (см. <see cref="СохранитьЗапросAsync"/>).
    /// </summary>
    private sealed class FormПравкиЗапроса : Form
    {
        private readonly TextBox _текст = new();

        /// <summary>Создаёт диалог правки запроса.</summary>
        /// <param name="имя">Наименование отчёта (в заголовке).</param>
        /// <param name="текущий">Текущий текст query.xml.</param>
        public FormПравкиЗапроса(string имя, string текущий)
        {
            Text = $"Запрос данных — {имя}";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(560, 400);

            _текст.Dock = DockStyle.Fill;
            _текст.Multiline = true;
            _текст.ScrollBars = ScrollBars.Both;
            _текст.WordWrap = false;
            _текст.Font = new Font(FontFamily.GenericMonospace, 9f);
            _текст.Text = текущий;
            _текст.AccessibleName = "Текст запроса данных";

            var сохранить = new Button
            {
                Text = "Проверить и сохранить",
                AutoSize = true,
                DialogResult = DialogResult.OK,
                AccessibleName = "Проверить и сохранить запрос",
            };

            var отмена = new Button
            {
                Text = "Отмена",
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Закрыть правку запроса без сохранения",
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

            Controls.Add(_текст);
            Controls.Add(низ);

            AcceptButton = сохранить;
            CancelButton = отмена;

            EnvelopeUiTheme.ОформитьОкно(this);
            EnvelopeUiTheme.ОформитьОсновнуюКнопку(сохранить);
        }

        /// <summary>Текст запроса из диалога.</summary>
        public string ТекстЗапроса => _текст.Text;
    }
}
