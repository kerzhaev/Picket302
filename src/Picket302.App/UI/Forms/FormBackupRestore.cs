using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

using Picket302.Domain.Backup;
using Picket302.Infrastructure.Backup;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог «Архивация и восстановление базы адресов» — бэкап и восстановление
/// в одной форме (ТЗ §10.4: «Вход: Сервис → Архивация базы адресов; бэкап и
/// восстановление — одна форма»; §10.5: «Из той же формы: выбор архива, пароль,
/// предпросмотр (дата, версия схемы, объем), автоматическая страховочная копия
/// текущего состояния, атомарная замена, предложение перезапуска»).
/// </summary>
/// <remarks>
/// <para>
/// Форма не обращается к базе данных в конструкторе: она переиспользует
/// переданные зависимости (путь к базе, службу архивирования, планировщик),
/// поэтому её можно создать в тесте без запущенной базы.
/// </para>
/// <para>
/// Пароль архива необязателен и показывается кнопкой «глаз»; содержимое журнала
/// и отчёта пароль не включает.
/// </para>
/// </remarks>
public sealed class FormBackupRestore : Form
{
    /// <summary>Имя файла настроек расписания по умолчанию.</summary>
    public const string ИмяФайлаНастроек = "backup-schedule.json";

    /// <summary>Каталог приложения в профиле пользователя.</summary>
    public const string КаталогПриложения = "Picket302";

    private readonly string _путьБазы;
    private readonly IBackupService _службаАрхива;
    private readonly RestoreService _службаВосстановления;
    private readonly ScheduledBackupService _планировщик;

    private readonly ComboBox _режимХранения = new();
    private readonly NumericUpDown _срокХранения = new() { Minimum = 1, Maximum = 3650, Value = 30 };
    private readonly TextBox _целеваяПапка = new();
    private readonly TextBox _пароль = new() { UseSystemPasswordChar = true };
    private readonly Button _глаз = new();
    private readonly DateTimePicker _точка1 = new();
    private readonly DateTimePicker _точка2 = new();
    private readonly DateTimePicker _точка3 = new();

    private readonly Button _создатьАрхив = new();
    private readonly Button _восстановить = new();
    private readonly Button _предпросмотр = new();
    private readonly Button _обзор = new();
    private readonly Button _закрыть = new();

    private readonly DataGridView _архивы = new();
    private readonly ProgressBar _прогресс = new();
    private readonly Label _состояние = new();
    private readonly TextBox _отчёт = new();

    /// <summary>
    /// Создаёт диалог архивирования и восстановления базы адресов.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <param name="службаАрхива">
    /// Служба архивирования; при отсутствии создаётся штатная служба для
    /// указанного пути к базе.
    /// </param>
    /// <param name="планировщик">
    /// Служба архивирования по расписанию; при отсутствии создаётся штатная
    /// служба с настройками по умолчанию.
    /// </param>
    /// <param name="службаВосстановления">
    /// Служба восстановления; при отсутствии создаётся штатная служба.
    /// </param>
    public FormBackupRestore(
        string путьБазы,
        IBackupService? службаАрхива = null,
        ScheduledBackupService? планировщик = null,
        RestoreService? службаВосстановления = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьБазы);

        _путьБазы = Path.GetFullPath(путьБазы);
        _службаАрхива = службаАрхива ?? new BackupService(_путьБазы);
        _службаВосстановления = службаВосстановления ?? new RestoreService(_путьБазы, _службаАрхива);
        _планировщик = планировщик ?? new ScheduledBackupService(
            _путьБазы,
            _службаАрхива,
            ПолитикаПоУмолчанию(),
            ПутьНастроекПоУмолчанию());

        Text = "Архивация и восстановление базы адресов";
        Font = new Font("Tahoma", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(760, 620);

        ПостроитьНастройки();
        ПостроитьРасписание();
        ПостроитьСписокАрхивов();
        ПостроитьОтчёт();
        ПостроитьКнопки();

        ПрименитьРасписаниеИзПланировщика();
        ОбновитьСписокАрхивов();
    }

    /// <summary>
    /// Путь к файлу базы данных.
    /// </summary>
    public string ПутьБазы => _путьБазы;

    /// <summary>
    /// Служба архивирования по расписанию, используемая формой.
    /// </summary>
    public ScheduledBackupService Планировщик => _планировщик;

    /// <summary>
    /// Служба восстановления, используемая формой.
    /// </summary>
    public RestoreService СлужбаВосстановления => _службаВосстановления;

    /// <summary>
    /// Текст отчёта о последней операции.
    /// </summary>
    public string ТекстОтчёта => _отчёт.Text;

    /// <summary>
    /// Признак показа пароля архива.
    /// </summary>
    public bool ПарольВидим { get; private set; }

    /// <summary>
    /// Возвращает путь к файлу настроек расписания по умолчанию —
    /// <c>%AppData%\Picket302\backup-schedule.json</c>.
    /// </summary>
    /// <returns>Путь к файлу настроек.</returns>
    public static string ПутьНастроекПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        КаталогПриложения,
        ИмяФайлаНастроек);

    /// <summary>
    /// Создаёт архив по текущим настройкам формы и применяет политику хранения.
    /// </summary>
    /// <returns>Результат создания архива.</returns>
    public BackupResult СоздатьАрхив()
    {
        var политика = СобратьПолитику();

        _планировщик.ConfigurePolicy(политика);
        ПрименитьРасписаниеВПланировщик();

        _прогресс.Style = ProgressBarStyle.Marquee;
        _состояние.Text = "Создание архива…";
        Update();

        var результат = _планировщик.RunNow();

        _прогресс.Style = ProgressBarStyle.Continuous;
        _прогресс.Value = результат.IsSuccess ? 100 : 0;

        var отчёт = new System.Text.StringBuilder();

        if (результат.IsSuccess)
        {
            _состояние.Text = $"Архив создан: {Path.GetFileName(результат.ArchivePath!)}";
            отчёт.AppendLine(CultureInfo.InvariantCulture, $"Архив создан: {результат.ArchivePath}");

            if (_планировщик.ПоследниеУдалённыеАрхивы.Count > 0)
            {
                отчёт.AppendLine();
                отчёт.AppendLine("Удалено по политике хранения:");

                foreach (var файл in _планировщик.ПоследниеУдалённыеАрхивы)
                {
                    отчёт.AppendLine("— " + файл);
                }
            }
        }
        else
        {
            _состояние.Text = "Архив не создан.";
            отчёт.AppendLine(CultureInfo.InvariantCulture, $"Не удалось создать архив: {результат.Error}");

            if (_планировщик.NextRetryAt is not null)
            {
                отчёт.AppendLine();
                отчёт.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"Повтор назначен на {_планировщик.NextRetryAt.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}.");
            }
        }

        _отчёт.Text = отчёт.ToString();

        ОбновитьСписокАрхивов();

        return результат;
    }

    /// <summary>
    /// Восстанавливает базу данных из выбранного архива.
    /// </summary>
    /// <param name="путьАрхива">Путь к архиву.</param>
    /// <returns>Результат восстановления.</returns>
    public RestoreOutcome Восстановить(string? путьАрхива)
    {
        var путь = путьАрхива ?? ВыбранныйАрхив();

        if (string.IsNullOrWhiteSpace(путь))
        {
            var отказ = new RestoreOutcome(false, "Не выбран архив для восстановления.", null, null, false);

            _состояние.Text = "Не выбран архив.";
            _отчёт.Text = отказ.Error;

            return отказ;
        }

        _прогресс.Style = ProgressBarStyle.Marquee;
        _состояние.Text = "Восстановление базы…";
        Update();

        var итог = _службаВосстановления.Restore(путь, _пароль.Text);

        _прогресс.Style = ProgressBarStyle.Continuous;
        _прогресс.Value = итог.IsSuccess ? 100 : 0;

        var отчёт = new System.Text.StringBuilder();

        if (итог.IsSuccess)
        {
            _состояние.Text = "База восстановлена.";
            отчёт.AppendLine(CultureInfo.InvariantCulture, $"База восстановлена из архива: {путь}");

            if (итог.SchemaVersion is not null)
            {
                отчёт.AppendLine();
                отчёт.AppendLine(CultureInfo.InvariantCulture, $"Версия схемы: {итог.SchemaVersion}");
            }

            if (итог.SafetyCopyPath is not null)
            {
                отчёт.AppendLine();
                отчёт.AppendLine(CultureInfo.InvariantCulture, $"Страховочная копия прежнего состояния: {итог.SafetyCopyPath}");
            }

            отчёт.AppendLine();
            отчёт.AppendLine("Требуется перезапуск программы.");
        }
        else
        {
            _состояние.Text = "Восстановление не выполнено.";
            отчёт.AppendLine(CultureInfo.InvariantCulture, $"Не удалось восстановить базу: {итог.Error}");
        }

        _отчёт.Text = отчёт.ToString();

        return итог;
    }

    /// <summary>
    /// Возвращает сведения о предпросмотре выбранного архива
    /// (ТЗ §10.5: «предпросмотр (дата, версия схемы, объем)»).
    /// </summary>
    /// <param name="путьАрхива">Путь к архиву; пустое значение — выбранный в списке.</param>
    /// <returns>Сведения об архиве либо <see langword="null"/>.</returns>
    public ArchivePreview? Предпросмотр(string? путьАрхива = null)
    {
        var путь = путьАрхива ?? ВыбранныйАрхив();

        if (string.IsNullOrWhiteSpace(путь))
        {
            _состояние.Text = "Не выбран архив.";
            _отчёт.Text = "Не выбран архив для предпросмотра.";

            return null;
        }

        var сведения = _службаВосстановления.Preview(путь);

        if (сведения is null)
        {
            _состояние.Text = "Архив не читается.";
            _отчёт.Text = $"Не удалось прочитать архив: {путь}";

            return null;
        }

        _состояние.Text = $"Предпросмотр: {сведения.FileName}";
        _отчёт.Text = string.Join(
            Environment.NewLine,
            $"Архив: {сведения.FileName}",
            $"Создан: {сведения.CreatedAtLocal?.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) ?? "неизвестно"}",
            $"Версия схемы: {сведения.SchemaVersion ?? "не определена"}",
            $"Объём: {сведения.SizeInBytes} байт",
            $"Пароль: {(сведения.IsPasswordProtected ? "установлен" : "не установлен")}");

        return сведения;
    }

    /// <summary>
    /// Собирает политику хранения по значениям элементов формы.
    /// </summary>
    /// <returns>Политика хранения архивов.</returns>
    public BackupPolicy СобратьПолитику() => new(
        BackupPolicyService.ParseMode(_режимХранения.SelectedItem as string ?? BackupPolicyService.ОписаниеЗаКаждыйДень),
        (int)_срокХранения.Value,
        _целеваяПапка.Text.Trim(),
        _пароль.Text.Length == 0 ? null : _пароль.Text);

    /// <summary>
    /// Обновляет перечень архивов в целевой папке.
    /// </summary>
    /// <returns>Сведения об архивах.</returns>
    public IReadOnlyList<BackupArchiveInfo> ОбновитьСписокАрхивов()
    {
        var папка = _целеваяПапка.Text.Trim();
        var архивы = string.IsNullOrWhiteSpace(папка)
            ? Array.Empty<BackupArchiveInfo>()
            : _службаАрхива.ListArchives(папка);

        _архивы.Rows.Clear();

        foreach (var архив in архивы)
        {
            _архивы.Rows.Add(
                архив.FileName,
                архив.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                архив.SizeInBytes.ToString(CultureInfo.InvariantCulture),
                архив.SchemaVersion ?? "—");
        }

        return архивы;
    }

    /// <summary>
    /// Показывает или скрывает пароль архива.
    /// </summary>
    public void ПереключитьПоказПароля()
    {
        ПарольВидим = !ПарольВидим;

        if (ПарольВидим)
        {
            // Показ пароля: символ маскирования снимается.
            _пароль.UseSystemPasswordChar = false;
            _пароль.PasswordChar = '\0';
        }
        else
        {
            // Скрытие пароля: маскирование системным символом.
            _пароль.UseSystemPasswordChar = true;
        }

        _глаз.Text = ПарольВидим ? "Скрыть" : "Показать";
    }

    /// <summary>
    /// Строит блок настроек хранения: режим, срок, папка и пароль.
    /// </summary>
    private void ПостроитьНастройки()
    {
        var группа = new GroupBox
        {
            Text = "Способ копирования и хранения архивов",
            Location = new Point(10, 8),
            Size = new Size(735, 120),
        };

        var меткаРежима = new Label
        {
            Text = "Хранение копий:",
            AutoSize = true,
            Location = new Point(12, 24),
        };

        _режимХранения.DropDownStyle = ComboBoxStyle.DropDownList;
        _режимХранения.Location = new Point(130, 21);
        _режимХранения.Width = 200;
        _режимХранения.Items.Add(BackupPolicyService.ОписаниеЗаКаждыйДень);
        _режимХранения.Items.Add(BackupPolicyService.ОписаниеТолькоПоследняя);
        _режимХранения.SelectedIndex = 0;

        var меткаСрока = new Label
        {
            Text = "Срок хранения, дней:",
            AutoSize = true,
            Location = new Point(350, 24),
        };

        _срокХранения.Location = new Point(460, 21);
        _срокХранения.Width = 70;

        var меткаПапки = new Label
        {
            Text = "Целевая папка:",
            AutoSize = true,
            Location = new Point(12, 54),
        };

        _целеваяПапка.Location = new Point(130, 51);
        _целеваяПапка.Width = 470;
        _целеваяПапка.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _обзор.Text = "Обзор…";
        _обзор.Size = new Size(80, 23);
        _обзор.Location = new Point(610, 50);
        _обзор.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _обзор.Click += (_, _) => ВыбратьПапку();

        var меткаПароля = new Label
        {
            Text = "Пароль архива:",
            AutoSize = true,
            Location = new Point(12, 84),
        };

        _пароль.Location = new Point(130, 81);
        _пароль.Width = 400;
        _пароль.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _глаз.Text = "Показать";
        _глаз.Size = new Size(80, 23);
        _глаз.Location = new Point(540, 80);
        _глаз.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _глаз.Click += (_, _) => ПереключитьПоказПароля();

        группа.Controls.Add(меткаРежима);
        группа.Controls.Add(_режимХранения);
        группа.Controls.Add(меткаСрока);
        группа.Controls.Add(_срокХранения);
        группа.Controls.Add(меткаПапки);
        группа.Controls.Add(_целеваяПапка);
        группа.Controls.Add(_обзор);
        группа.Controls.Add(меткаПароля);
        группа.Controls.Add(_пароль);
        группа.Controls.Add(_глаз);

        Controls.Add(группа);
    }

    /// <summary>
    /// Строит блок расписания архивирования (до трёх точек в сутки).
    /// </summary>
    private void ПостроитьРасписание()
    {
        var группа = new GroupBox
        {
            Text = "Расписание архивирования (до трёх точек в сутки)",
            Location = new Point(10, 134),
            Size = new Size(735, 70),
        };

        var поля = new[]
        {
            НастроитьТочку(_точка1, "Первая точка:", 12),
            НастроитьТочку(_точка2, "Вторая точка:", 250),
            НастроитьТочку(_точка3, "Третья точка:", 490),
        };

        foreach (var поле in поля)
        {
            группа.Controls.Add(поле);
        }

        Controls.Add(группа);
    }

    /// <summary>
    /// Настраивает поле ввода времени точки расписания.
    /// </summary>
    /// <param name="поле">Поле ввода времени.</param>
    /// <param name="подпись">Подпись поля.</param>
    /// <param name="смещение">Горизонтальное смещение подписи.</param>
    /// <returns>Подпись поля — она добавляется в контейнер формы.</returns>
    private static Label НастроитьТочку(DateTimePicker поле, string подпись, int смещение)
    {
        var метка = new Label
        {
            Text = подпись,
            AutoSize = true,
            Location = new Point(смещение, 26),
        };

        поле.Format = DateTimePickerFormat.Time;
        поле.ShowUpDown = true;
        поле.CustomFormat = "HH:mm";
        поле.Location = new Point(смещение + 100, 20);
        поле.Width = 80;

        return метка;
    }

    /// <summary>
    /// Строит перечень архивов.
    /// </summary>
    private void ПостроитьСписокАрхивов()
    {
        var метка = new Label
        {
            Text = "Архивы в целевой папке:",
            AutoSize = true,
            Location = new Point(12, 210),
        };

        _архивы.Location = new Point(10, 228);
        _архивы.Size = new Size(735, 180);
        _архивы.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _архивы.ReadOnly = true;
        _архивы.AllowUserToAddRows = false;
        _архивы.AllowUserToDeleteRows = false;
        _архивы.RowHeadersVisible = false;
        _архивы.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _архивы.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _архивы.Columns.Add("Имя архива", "Архив");
        _архивы.Columns.Add("Дата", "Создан");
        _архивы.Columns.Add("Размер", "Размер, байт");
        _архивы.Columns.Add("Схема", "Версия схемы");

        Controls.Add(метка);
        Controls.Add(_архивы);
    }

    /// <summary>
    /// Строит блок отчёта и индикатор выполнения.
    /// </summary>
    private void ПостроитьОтчёт()
    {
        _прогресс.Location = new Point(10, 414);
        _прогресс.Size = new Size(735, 16);
        _прогресс.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _состояние.Location = new Point(10, 434);
        _состояние.Size = new Size(735, 20);
        _состояние.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _состояние.Text = "Готово к работе.";

        _отчёт.Location = new Point(10, 458);
        _отчёт.Size = new Size(735, 110);
        _отчёт.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _отчёт.Multiline = true;
        _отчёт.ReadOnly = true;
        _отчёт.ScrollBars = ScrollBars.Vertical;

        Controls.Add(_прогресс);
        Controls.Add(_состояние);
        Controls.Add(_отчёт);
    }

    /// <summary>
    /// Строит блок кнопок.
    /// </summary>
    private void ПостроитьКнопки()
    {
        _создатьАрхив.Text = "Создать архив";
        _создатьАрхив.Size = new Size(130, 26);
        _создатьАрхив.Location = new Point(10, 578);
        _создатьАрхив.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _создатьАрхив.Click += (_, _) => СоздатьАрхив();

        _восстановить.Text = "Восстановить";
        _восстановить.Size = new Size(130, 26);
        _восстановить.Location = new Point(150, 578);
        _восстановить.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _восстановить.Click += (_, _) => Восстановить(null);

        _предпросмотр.Text = "Предпросмотр";
        _предпросмотр.Size = new Size(120, 26);
        _предпросмотр.Location = new Point(290, 578);
        _предпросмотр.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _предпросмотр.Click += (_, _) => Предпросмотр();

        _закрыть.Text = "Закрыть";
        _закрыть.Size = new Size(100, 26);
        _закрыть.Location = new Point(645, 578);
        _закрыть.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _закрыть.DialogResult = DialogResult.Cancel;

        Controls.Add(_создатьАрхив);
        Controls.Add(_восстановить);
        Controls.Add(_предпросмотр);
        Controls.Add(_закрыть);

        CancelButton = _закрыть;
    }

    /// <summary>
    /// Выбирает целевую папку хранения архивов.
    /// </summary>
    private void ВыбратьПапку()
    {
        using var диалог = new FolderBrowserDialog
        {
            Description = "Папка хранения архивов базы адресов",
            UseDescriptionForTitle = true,
            SelectedPath = _целеваяПапка.Text,
        };

        if (диалог.ShowDialog(this) == DialogResult.OK)
        {
            _целеваяПапка.Text = диалог.SelectedPath;
            ОбновитьСписокАрхивов();
        }
    }

    /// <summary>
    /// Возвращает путь к выбранному в перечне архиву.
    /// </summary>
    /// <returns>Путь к архиву либо пустая строка.</returns>
    private string ВыбранныйАрхив()
    {
        if (_архивы.CurrentRow is null)
        {
            return string.Empty;
        }

        var имя = _архивы.CurrentRow.Cells[0].Value as string;

        if (string.IsNullOrWhiteSpace(имя))
        {
            return string.Empty;
        }

        return Path.Combine(_целеваяПапка.Text.Trim(), имя);
    }

    /// <summary>
    /// Переносит расписание планировщика в элементы формы.
    /// </summary>
    private void ПрименитьРасписаниеИзПланировщика()
    {
        var точки = _планировщик.Schedule.Точки;
        var поля = new[] { _точка1, _точка2, _точка3 };

        for (var индекс = 0; индекс < поля.Length; индекс++)
        {
            var значение = индекс < точки.Count
                ? точки[индекс]
                : ScheduledBackupService.ТочкиПоУмолчанию[индекс];

            поля[индекс].Value = DateTime.Today.Add(значение);
        }
    }

    /// <summary>
    /// Переносит значения элементов формы в расписание планировщика.
    /// </summary>
    private void ПрименитьРасписаниеВПланировщик()
    {
        var точки = new[] { _точка1, _точка2, _точка3 }
            .Select(поле => поле.Value.TimeOfDay)
            .ToList();

        try
        {
            _планировщик.ConfigureSchedule(new BackupSchedule(точки));
        }
        catch (ArgumentException ошибка)
        {
            _состояние.Text = ошибка.Message;
        }
    }

    /// <summary>
    /// Формирует политику хранения по умолчанию.
    /// </summary>
    /// <returns>Политика хранения архивов.</returns>
    private static BackupPolicy ПолитикаПоУмолчанию() => new(
        BackupRetentionMode.DailyHistory,
        30,
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Picket302",
            "backup"),
        null);
}
