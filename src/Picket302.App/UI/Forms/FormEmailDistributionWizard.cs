using System.Drawing;
using System.Windows.Forms;
using Picket302.Application.Services;
using Picket302.Domain.Email;
using Picket302.Domain.Entities;
using Picket302.App.UI.Editors;
using Picket302.Infrastructure.Mail;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Источник получателей электронной рассылки (ТЗ §8.7, функция 92: «выборка
/// контрагентов — по категории, по фильтру или по выделению в таблице»).
/// </summary>
public enum ИсточникПолучателей
{
    /// <summary>Выделенные в таблице контрагенты.</summary>
    Выделение,

    /// <summary>Контрагенты выбранной категории.</summary>
    Категория,

    /// <summary>Контрагенты, отобранные фильтром.</summary>
    Фильтр,
}

/// <summary>
/// Контроллер мастера электронной рассылки: сборка рассылки и запуск отправки.
/// </summary>
/// <remarks>
/// Вынесен в отдельный публичный класс, чтобы отчёт можно было получить без
/// запуска SMTP: отправка выполняется через внедряемый в службу делегат.
/// </remarks>
public sealed class EmailDistributionWizardController
{
    private readonly EmailDistributionService _служба;

    /// <summary>
    /// Создаёт контроллер мастера.
    /// </summary>
    /// <param name="служба">Служба электронной рассылки.</param>
    /// <param name="маркеры">Подстановщик маркеров полей.</param>
    /// <param name="получатели">Получатели рассылки (выборка контрагентов).</param>
    public EmailDistributionWizardController(
        EmailDistributionService служба,
        EmailMarkerResolver маркеры,
        IReadOnlyList<EmailRecipient>? получатели = null)
    {
        ArgumentNullException.ThrowIfNull(служба);
        ArgumentNullException.ThrowIfNull(маркеры);

        _служба = служба;
        Маркеры = маркеры;
        Получатели = получатели ?? [];
    }

    /// <summary>Подстановщик маркеров полей.</summary>
    public EmailMarkerResolver Маркеры { get; }

    /// <summary>Получатели рассылки.</summary>
    public IReadOnlyList<EmailRecipient> Получатели { get; }

    /// <summary>
    /// Формирует рассылку по данным мастера.
    /// </summary>
    /// <param name="тема">Тема письма с маркерами полей.</param>
    /// <param name="тело">Тело письма с маркерами полей.</param>
    /// <param name="формат">Формат тела письма.</param>
    /// <param name="отправитель">Адрес отправителя.</param>
    /// <param name="обратныйАдрес">Обратный адрес для ответа.</param>
    /// <param name="получатели">Получатели; при значении <see langword="null"/> берутся из контроллера.</param>
    /// <param name="вложения">Пути к вложениям.</param>
    /// <returns>Готовая рассылка.</returns>
    public EmailDistribution СобратьРассылку(
        string тема,
        string тело,
        BodyFormat формат,
        string отправитель,
        string? обратныйАдрес,
        IEnumerable<EmailRecipient>? получатели = null,
        IEnumerable<string>? вложения = null)
    {
        var рассылка = new EmailDistribution
        {
            Name = $"Рассылка {DateTime.Now:yyyy-MM-dd HH:mm}",
            Subject = тема ?? string.Empty,
            From = отправитель ?? string.Empty,
            ReplyTo = string.IsNullOrWhiteSpace(обратныйАдрес) ? null : обратныйАдрес.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        рассылка.SetBodyFormat(формат, тело ?? string.Empty);

        foreach (var получатель in получатели ?? Получатели)
        {
            рассылка.Recipients.Add(получатель);
        }

        foreach (var вложение in вложения ?? [])
        {
            рассылка.Attachments.Add(вложение);
        }

        return рассылка;
    }

    /// <summary>
    /// Отправляет рассылку и возвращает отчёт с перечнем предупреждений.
    /// </summary>
    /// <param name="рассылка">Рассылка.</param>
    /// <param name="прогресс">Отчёт о ходе отправки.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Отчёт отправки и перечень предупреждений.</returns>
    public EmailBatchOutcome Отправить(
        EmailDistribution рассылка,
        IProgress<EmailSendProgress>? прогресс = null,
        CancellationToken ct = default)
        => _служба.SendBatchDetailed(рассылка, прогресс, ct);
}

/// <summary>
/// Реализация контракта журнала прикладного слоя поверх журнала SQLite слоя
/// инфраструктуры (матрица зависимостей ТЗ §2.1: инфраструктура не ссылается
/// на прикладной слой).
/// </summary>
public sealed class EmailLogWriterAdapter : IEmailLogWriter
{
    private readonly SqliteEmailLogWriter _писатель;

    /// <summary>
    /// Создаёт адаптер журнала.
    /// </summary>
    /// <param name="писатель">Журнал отправки на SQLite.</param>
    public EmailLogWriterAdapter(SqliteEmailLogWriter писатель)
    {
        ArgumentNullException.ThrowIfNull(писатель);

        _писатель = писатель;
    }

    /// <inheritdoc />
    public Task WriteAsync(EmailLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _писатель.WriteAsync(
            entry.Recipient,
            entry.Status,
            entry.SmtpCode,
            entry.ServerResponse,
            entry.Error,
            entry.SentAtUtc,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailLogEntry>> ReadAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var строки = await _писатель.ReadAsync(limit, cancellationToken).ConfigureAwait(false);

        return строки
            .Select(строка => new EmailLogEntry(
                строка.Recipient,
                строка.Status,
                строка.SmtpCode,
                строка.ServerResponse,
                строка.Error,
                строка.SentAtUtc))
            .ToList();
    }
}

/// <summary>
/// Мастер «Электронная рассылка» (ТЗ §8.7, функция 92: выбор получателей,
/// подготовка письма, вложения, отправка с прогрессом и отчёт).
/// </summary>
/// <remarks>
/// <para>
/// Форма не запускает SMTP самостоятельно: отправка выполняется службой
/// <see cref="EmailDistributionService"/>, переданной в контроллер, поэтому
/// мастер пригоден для проверки без реальной отправки писем.
/// </para>
/// </remarks>
public sealed class FormEmailDistributionWizard : Form
{
    private readonly EmailDistributionWizardController _контроллер;
    private readonly AttachmentService _вложения;

    private readonly ComboBox _источник = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 200,
    };

    private readonly ListView _списокПолучателей = new()
    {
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HideSelection = false,
        Dock = DockStyle.Fill,
    };

    private readonly TextBox _тема = new() { Dock = DockStyle.Fill };

    private readonly TextBox _текстПисьма = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill,
    };

    private readonly RadioButton _форматТекст = new() { Text = "Простой текст", AutoSize = true, Checked = true };
    private readonly RadioButton _форматHtml = new() { Text = "HTML", AutoSize = true };

    private readonly ListBox _списокВложений = new() { Dock = DockStyle.Fill };

    private readonly TextBox _отправитель = new() { Dock = DockStyle.Fill };
    private readonly TextBox _обратныйАдрес = new() { Dock = DockStyle.Fill };

    private readonly Button _кнопкаОтправить = new() { Text = "Отправить", AutoSize = true };
    private readonly Button _кнопкаПрервать = new() { Text = "Прервать", AutoSize = true, Enabled = false };

    private readonly ProgressBar _прогресс = new() { Dock = DockStyle.Fill, Height = 18 };

    private readonly TextBox _отчет = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9f),
    };

    private CancellationTokenSource? _отмена;

    /// <summary>
    /// Создаёт мастер электронной рассылки.
    /// </summary>
    /// <param name="контроллер">Контроллер мастера.</param>
    /// <param name="вложения">
    /// Служба вложений; при значении <see langword="null"/> используется
    /// стандартная папка профиля пользователя.
    /// </param>
    public FormEmailDistributionWizard(
        EmailDistributionWizardController контроллер,
        AttachmentService? вложения = null)
    {
        ArgumentNullException.ThrowIfNull(контроллер);

        _контроллер = контроллер;
        _вложения = вложения ?? new AttachmentService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Picket302",
                AttachmentService.ИмяПапкиХранилища));

        Text = "Электронная рассылка";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 700);
        MinimumSize = new Size(760, 560);

        ПостроитьПанели();
        ЗаполнитьПолучателей();
    }

    /// <summary>
    /// Текст отчёта, сформированный мастером (для проверки без запуска SMTP).
    /// </summary>
    public string ТекстОтчета
    {
        get => _отчет.Text;
        set => _отчет.Text = value;
    }

    /// <summary>
    /// Формирует рассылку по текущему состоянию полей мастера.
    /// </summary>
    /// <returns>Готовая рассылка.</returns>
    public EmailDistribution СобратьРассылку() => _контроллер.СобратьРассылку(
        _тема.Text,
        _текстПисьма.Text,
        _форматHtml.Checked ? BodyFormat.Html : BodyFormat.PlainText,
        _отправитель.Text,
        _обратныйАдрес.Text,
        ОтобранныеПолучатели(),
        _списокВложений.Items.Cast<string>());

    /// <summary>
    /// Возвращает отмеченных получателей мастера.
    /// </summary>
    /// <returns>Список отмеченных получателей.</returns>
    public IReadOnlyList<EmailRecipient> ОтобранныеПолучатели()
    {
        var отмеченные = new List<EmailRecipient>();
        var получатели = _контроллер.Получатели;

        for (var индекс = 0; индекс < _списокПолучателей.Items.Count && индекс < получатели.Count; индекс++)
        {
            if (_списокПолучателей.Items[индекс] is not ListViewItem элемент)
            {
                continue;
            }

            if (элемент.Checked)
            {
                отмеченные.Add(получатели[индекс]);
            }
        }

        return отмеченные;
    }

    /// <summary>
    /// Строит панели мастера.
    /// </summary>
    private void ПостроитьПанели()
    {
        var корень = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8),
        };

        корень.RowStyles.Add(new RowStyle(SizeType.Percent, 34f));
        корень.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
        корень.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
        корень.RowStyles.Add(new RowStyle(SizeType.Percent, 26f));

        корень.Controls.Add(ПостроитьПанельПолучателей(), 0, 0);
        корень.Controls.Add(ПостроитьПанельПисьма(), 0, 1);
        корень.Controls.Add(ПостроитьПанельВложений(), 0, 2);
        корень.Controls.Add(ПостроитьПанельОтправки(), 0, 3);

        Controls.Add(корень);
    }

    /// <summary>
    /// Строит панель выбора получателей.
    /// </summary>
    /// <returns>Панель выбора получателей.</returns>
    private GroupBox ПостроитьПанельПолучателей()
    {
        var панель = new GroupBox
        {
            Text = "Получатели",
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
        };

        _источник.Items.AddRange(
        [
            new EnumItem(ИсточникПолучателей.Выделение, "Выделение в таблице"),
            new EnumItem(ИсточникПолучателей.Категория, "Категория"),
            new EnumItem(ИсточникПолучателей.Фильтр, "Фильтр"),
        ]);

        _источник.SelectedIndex = 0;

        var верх = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            FlowDirection = FlowDirection.LeftToRight,
        };

        верх.Controls.Add(new Label { Text = "Источник:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        верх.Controls.Add(_источник);

        _списокПолучателей.Columns.Add("Наименование", 320);
        _списокПолучателей.Columns.Add("Адрес", 320);
        _списокПолучателей.Columns.Add("№", 80);

        панель.Controls.Add(_списокПолучателей);
        панель.Controls.Add(верх);

        return панель;
    }

    /// <summary>
    /// Строит панель подготовки письма.
    /// </summary>
    /// <returns>Панель подготовки письма.</returns>
    private GroupBox ПостроитьПанельПисьма()
    {
        var панель = new GroupBox
        {
            Text = "Письмо",
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
        };

        var таблица = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
        };

        таблица.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        таблица.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        таблица.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));

        таблица.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        таблица.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        таблица.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        таблица.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        таблица.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        таблица.Controls.Add(new Label { Text = "Тема:", AutoSize = true }, 0, 0);
        таблица.Controls.Add(_тема, 1, 0);
        таблица.Controls.Add(new Label { Text = "", AutoSize = true }, 2, 0);

        таблица.Controls.Add(new Label { Text = "Формат:", AutoSize = true }, 0, 1);

        var форматы = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        форматы.Controls.Add(_форматТекст);
        форматы.Controls.Add(_форматHtml);
        таблица.Controls.Add(форматы, 1, 1);

        таблица.Controls.Add(new Label { Text = "Отправитель:", AutoSize = true }, 0, 2);
        таблица.Controls.Add(_отправитель, 1, 2);
        таблица.Controls.Add(new Label { Text = "", AutoSize = true }, 2, 2);

        таблица.Controls.Add(new Label { Text = "Обратный адрес:", AutoSize = true }, 0, 3);
        таблица.Controls.Add(_обратныйАдрес, 1, 3);
        таблица.Controls.Add(new Label { Text = "", AutoSize = true }, 2, 3);

        таблица.Controls.Add(new Label { Text = "Текст письма:", AutoSize = true }, 0, 4);
        таблица.Controls.Add(_текстПисьма, 1, 4);

        var кнопкаРедактор = new Button
        {
            Text = "Редактировать HTML",
            AutoSize = true,
            Dock = DockStyle.Fill,
        };

        кнопкаРедактор.Click += (_, _) => ОткрытьРедактор();

        таблица.Controls.Add(кнопкаРедактор, 2, 4);

        панель.Controls.Add(таблица);

        return панель;
    }

    /// <summary>
    /// Строит панель вложений.
    /// </summary>
    /// <returns>Панель вложений.</returns>
    private GroupBox ПостроитьПанельВложений()
    {
        var панель = new GroupBox
        {
            Text = "Вложения",
            Dock = DockStyle.Fill,
            Padding = new Padding(6),
        };

        var кнопки = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
        };

        var кнопкаДобавить = new Button { Text = "Добавить", AutoSize = true };
        var кнопкаУдалить = new Button { Text = "Удалить", AutoSize = true };

        кнопкаДобавить.Click += (_, _) => ДобавитьВложение();
        кнопкаУдалить.Click += (_, _) => УдалитьВложение();

        кнопки.Controls.Add(кнопкаДобавить);
        кнопки.Controls.Add(кнопкаУдалить);

        панель.Controls.Add(_списокВложений);
        панель.Controls.Add(кнопки);

        return панель;
    }

    /// <summary>
    /// Строит панель отправки и отчёта.
    /// </summary>
    /// <returns>Панель отправки.</returns>
    private Panel ПостроитьПанельОтправки()
    {
        var панель = new Panel { Dock = DockStyle.Fill };

        var верх = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
        };

        верх.Controls.Add(_кнопкаОтправить);
        верх.Controls.Add(_кнопкаПрервать);

        var индикатор = new Panel { Dock = DockStyle.Top, Height = 24, Padding = new Padding(0, 2, 0, 2) };
        индикатор.Controls.Add(_прогресс);

        _кнопкаОтправить.Click += ОтправитьПисьма;
        _кнопкаПрервать.Click += (_, _) => _отмена?.Cancel();

        панель.Controls.Add(_отчет);
        панель.Controls.Add(индикатор);
        панель.Controls.Add(верх);

        return панель;
    }

    /// <summary>
    /// Заполняет список получателей рассылки.
    /// </summary>
    private void ЗаполнитьПолучателей()
    {
        _списокПолучателей.Items.Clear();

        foreach (var получатель in _контроллер.Получатели)
        {
            var элемент = new ListViewItem(получатель.Name)
            {
                Tag = получатель,
                Checked = true,
            };

            элемент.SubItems.Add(получатель.Email);
            элемент.SubItems.Add(получатель.CounterpartyId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            _списокПолучателей.Items.Add(элемент);
        }
    }

    /// <summary>
    /// Открывает визуальный редактор письма и забирает отредактированную разметку.
    /// </summary>
    private void ОткрытьРедактор()
    {
        using var редактор = new HtmlMailEditor();

        var результат = редактор.EditHtml(_текстПисьма.Text);

        if (редактор.Подтверждено)
        {
            _текстПисьма.Text = результат;
            _форматHtml.Checked = true;
        }
    }

    /// <summary>
    /// Копирует выбранный файл в хранилище вложений и добавляет его в рассылку.
    /// </summary>
    private void ДобавитьВложение()
    {
        using var диалог = new OpenFileDialog
        {
            Title = "Выбор файла вложения",
            Multiselect = true,
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var рассылка = new EmailDistribution();
        var прежнееЧислоПредупреждений = _вложения.Warnings.Count;

        foreach (var файл in диалог.FileNames)
        {
            try
            {
                _вложения.AddAttachment(рассылка, файл);
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show(
                    this,
                    $"Файл «{файл}» не найден.",
                    "Электронная рассылка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        foreach (var путь in рассылка.Attachments)
        {
            _списокВложений.Items.Add(путь);
        }

        var новыеПредупреждения = _вложения.Warnings.Skip(прежнееЧислоПредупреждений).ToList();

        if (новыеПредупреждения.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, новыеПредупреждения),
                "Электронная рассылка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Удаляет выбранное вложение из списка мастера.
    /// </summary>
    private void УдалитьВложение()
    {
        if (_списокВложений.SelectedItem is not string путь)
        {
            return;
        }

        _списокВложений.Items.Remove(путь);
    }

    /// <summary>
    /// Отправляет рассылку: партия уходит последовательно, с прогрессом и
    /// возможностью прерывания; по завершении выводится отчёт.
    /// </summary>
    /// <param name="отправитель">Источник события.</param>
    /// <param name="аргументы">Аргументы события.</param>
    private async void ОтправитьПисьма(object? отправитель, EventArgs аргументы)
    {
        if (!ПроверитьАдреса())
        {
            return;
        }

        var рассылка = СобратьРассылку();

        if (рассылка.Recipients.Count == 0)
        {
            MessageBox.Show(
                this,
                "Не выбрано ни одного получателя.",
                "Электронная рассылка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        var предупреждения = _вложения.CollectWarnings(рассылка);

        if (предупреждения.Count > 0
            && MessageBox.Show(
                this,
                string.Join(Environment.NewLine, предупреждения) + Environment.NewLine
                    + "Продолжить отправку?",
                "Электронная рассылка",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _кнопкаОтправить.Enabled = false;
        _кнопкаПрервать.Enabled = true;
        _отчет.Clear();

        using var отмена = new CancellationTokenSource();
        _отмена = отмена;

        var прогресс = new Progress<EmailSendProgress>(значение =>
        {
            _прогресс.Maximum = Math.Max(значение.Total, 1);
            _прогресс.Value = Math.Min(значение.Sent, _прогресс.Maximum);
        });

        try
        {
            var исход = await Task.Run(() => _контроллер.Отправить(рассылка, прогресс, отмена.Token));

            var строки = new List<string> { исход.Report.BuildText() };

            if (исход.Warnings.Count > 0)
            {
                строки.Add("Предупреждения:");
                строки.AddRange(исход.Warnings.Select(предупреждение => "  — " + предупреждение));
            }

            _отчет.Text = string.Join(Environment.NewLine, строки);
        }
        finally
        {
            _отмена = null;
            _кнопкаОтправить.Enabled = true;
            _кнопкаПрервать.Enabled = false;
        }
    }

    /// <summary>
    /// Проверяет адреса отправителя и обратного адреса до начала отправки.
    /// </summary>
    /// <returns><see langword="true"/>, если адреса корректны.</returns>
    private bool ПроверитьАдреса()
    {
        if (!SmtpClientFacade.ПроверитьАдрес(_отправитель.Text, out var ошибкаОтправителя))
        {
            MessageBox.Show(
                this,
                $"Неверный формат адреса отправителя: {ошибкаОтправителя}.",
                "Электронная рассылка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }

        if (!string.IsNullOrWhiteSpace(_обратныйАдрес.Text)
            && !SmtpClientFacade.ПроверитьАдрес(_обратныйАдрес.Text, out var ошибкаОтвета))
        {
            MessageBox.Show(
                this,
                $"Неверный формат обратного адреса: {ошибкаОтвета}.",
                "Электронная рассылка",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Пункт списка с источником получателей.
    /// </summary>
    /// <param name="значение">Значение источника.</param>
    /// <param name="текст">Отображаемый текст.</param>
    private sealed record EnumItem(ИсточникПолучателей Значение, string Текст)
    {
        /// <inheritdoc />
        public override string ToString() => Текст;
    }
}
