using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Picket302.Application.Import;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Контроллер мастера выгрузки: собирает запрос выгрузки из состояния формы
/// (ТЗ §8.12, функция 160: «Экспорт с настройками и пост-действиями»).
/// </summary>
/// <remarks>
/// Контроллер не зависит от окон: запрос выгрузки и проверка занятости файла
/// доступны без создания формы, поэтому проверяются модульными тестами.
/// </remarks>
public sealed class ExportWizardController
{
    /// <summary>Формат выгрузки.</summary>
    public ExchangeFormat Format { get; set; } = ExchangeFormat.Csv;

    /// <summary>Формат представления дат в выгрузке.</summary>
    public ExportDateFormat DateFormat { get; set; } = ExportDateFormat.Iso;

    /// <summary>Пользовательский формат даты; применяется при <see cref="ExportDateFormat.Custom"/>.</summary>
    public string? CustomDateFormat { get; set; }

    /// <summary>Действие после выгрузки.</summary>
    public ExportPostAction PostAction { get; set; } = ExportPostAction.None;

    /// <summary>Путь итогового файла выгрузки.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Разделитель столбцов (только для TXT и CSV).</summary>
    public char Delimiter { get; set; } = ';';

    /// <summary>Кодировка выгрузки (только для TXT и CSV).</summary>
    public Encoding? TextEncoding { get; set; }

    /// <summary>Признак включения строки заголовков.</summary>
    public bool IncludeHeader { get; set; } = true;

    /// <summary>
    /// Собирает запрос выгрузки из состояния формы.
    /// </summary>
    /// <param name="items">Выгружаемые контрагенты.</param>
    /// <returns>Запрос выгрузки для <see cref="ExportWizard.Export"/>.</returns>
    public ExportRequest BuildRequest(IReadOnlyList<Counterparty>? items = null) => new()
    {
        Format = Format,
        OutputPath = OutputPath,
        DateFormat = DateFormat,
        CustomDateFormat = string.IsNullOrWhiteSpace(CustomDateFormat) ? null : CustomDateFormat.Trim(),
        PostAction = PostAction,
        Items = items ?? Array.Empty<Counterparty>(),
        Delimiter = Delimiter,
        Encoding = TextEncoding,
        IncludeHeader = IncludeHeader,
    };

    /// <summary>
    /// Определяет, занят ли файл другим приложением (ТЗ §8.12, функция 160:
    /// «Файл занят — предложение альтернативного имени»).
    /// </summary>
    /// <param name="path">Путь к файлу.</param>
    /// <returns><see langword="true"/>, если файл занят.</returns>
    public static bool IsFileBusy(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var поток = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Подбирает свободное имя рядом с занятым файлом: к имени без расширения
    /// добавляется номер в скобках («address_book (2).csv»).
    /// </summary>
    /// <param name="path">Путь к занятому файлу.</param>
    /// <returns>Путь к свободному файлу.</returns>
    public static string SuggestAvailablePath(string path) => ExportWizard.SuggestAvailableFilePath(path);
}

/// <summary>
/// Мастер выгрузки данных: выбор формата файла и настроек даты, выбор
/// итогового файла и действие после сохранения
/// (ТЗ §8.12, функция 160: «Экспорт с настройками и пост-действиями»).
/// </summary>
/// <remarks>
/// <para>
/// Мастер состоит из двух шагов: параметры выгрузки (формат файла, формат дат,
/// разделитель и кодировка для текстовых форматов, итоговый файл) и действие
/// после выгрузки («Не открывать» / «Открыть файл» / «Открыть папку»).
/// </para>
/// <para>
/// Если целевой файл занят другим приложением, оператору предлагается
/// альтернативное имя файла; итоговый путь показывается в результате выгрузки.
/// </para>
/// </remarks>
public sealed class FormExportWizard : Form
{
    /// <summary>Форматы выгрузки и их названия.</summary>
    private static readonly (string Название, ExchangeFormat Формат)[] Форматы =
    {
        ("Текстовый файл (TXT)", ExchangeFormat.Txt),
        ("Текст с разделителями (CSV)", ExchangeFormat.Csv),
        ("HTML-документ (HTML)", ExchangeFormat.Html),
        ("Rich Text Format (RTF)", ExchangeFormat.Rtf),
        ("Книга Excel (XLS)", ExchangeFormat.Xls),
        ("Книга Excel (XLSX)", ExchangeFormat.Xlsx),
    };

    /// <summary>Форматы представления дат и их названия.</summary>
    private static readonly (string Название, ExportDateFormat Формат)[] ФорматыДаты =
    {
        ("ISO 8601 (yyyy-MM-dd)", ExportDateFormat.Iso),
        ("ДД.ММ.ГГГГ", ExportDateFormat.DotSeparated),
        ("ДД/ММ/ГГГГ", ExportDateFormat.SlashSeparated),
        ("Только время (ЧЧ:ММ:СС)", ExportDateFormat.TimeOnly),
        ("Произвольный формат", ExportDateFormat.Custom),
    };

    /// <summary>Разделители столбцов текстовых форматов.</summary>
    private static readonly char[] Разделители = [';', ',', '\t', '|'];

    /// <summary>Названия разделителей для выпадающего списка.</summary>
    private static readonly string[] НазванияРазделителей = ["Точка с запятой", "Запятая", "Табуляция", "Вертикальная черта"];

    /// <summary>Кодировки выгрузки.</summary>
    private static readonly Encoding[] Кодировки =
    [
        TextEncodingDetector.Utf8WithBom,
        TextEncodingDetector.Utf8,
        TextEncodingDetector.Cp1251,
    ];

    /// <summary>Названия кодировок для выпадающего списка.</summary>
    private static readonly string[] НазванияКодировок =
    [
        "UTF-8 с маркером BOM",
        "UTF-8 без маркера BOM",
        "Windows-1251 (CP1251)",
    ];

    private readonly ExportWizardController _контроллер = new();

    private readonly ExportWizard _мастер;

    private readonly ComboBox _формат = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _форматДаты = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _свойФорматДаты = new() { Enabled = false };
    private readonly ComboBox _разделитель = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _кодировка = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _заголовки = new() { Text = "Включить строку заголовков", Checked = true };

    private readonly GroupBox _группаТекста = new() { Text = "Разделитель и кодировка (TXT, CSV)" };

    private readonly TextBox _путь = new();
    private readonly Button _обзор = new();

    private readonly Label _результат = new();
    private readonly Label _подсказка = new();

    private readonly Button _экспорт = new();
    private readonly Button _отмена = new();

    /// <summary>
    /// Создаёт мастер выгрузки.
    /// </summary>
    /// <param name="записи">Выгружаемые контрагенты.</param>
    /// <param name="мастер">
    /// Мастер выгрузки; при отсутствии — мастер с собственными реализациями
    /// обмена и выполнения пост-действия.
    /// </param>
    public FormExportWizard(IReadOnlyList<Counterparty>? записи = null, ExportWizard? мастер = null)
    {
        _мастер = мастер ?? new ExportWizard();

        Записи = записи ?? Array.Empty<Counterparty>();

        Text = "Мастер выгрузки данных";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(520, 462);

        ПостроитьШагПараметров();
        ПостроитьШагФайла();
        ПостроитьРезультат();
        ПостроитьКнопки();

        AcceptButton = _экспорт;
        CancelButton = _отмена;

        ОбновитьДоступность();
    }

    /// <summary>Выгружаемые контрагенты.</summary>
    public IReadOnlyList<Counterparty> Записи { get; set; }

    /// <summary>Контроллер мастера: состояние параметров выгрузки.</summary>
    public ExportWizardController Контроллер => _контроллер;

    /// <summary>Результат последней выгрузки; <see langword="null"/> — выгрузки ещё не было.</summary>
    public ExportResult? Результат { get; private set; }

    /// <summary>
    /// Выполняет выгрузку по текущему состоянию формы и показывает результат.
    /// </summary>
    /// <returns>Результат выгрузки либо <see langword="null"/>, если выгрузка не выполнена.</returns>
    public ExportResult? ВыполнитьВыгрузку()
    {
        var путь = _путь.Text.Trim();

        if (путь.Length == 0)
        {
            _подсказка.Text = "Укажите файл выгрузки: выгрузка не выполнялась.";
            MessageBox.Show(this, "Укажите файл выгрузки.", "Мастер выгрузки данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return null;
        }

        if (ExportWizardController.IsFileBusy(путь))
        {
            var альтернатива = ExportWizardController.SuggestAvailablePath(путь);

            var ответ = MessageBox.Show(
                this,
                $"Файл «{Path.GetFileName(путь)}» занят другим приложением." + Environment.NewLine +
                $"Сохранить выгрузку под именем «{Path.GetFileName(альтернатива)}»?",
                "Мастер выгрузки данных",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (ответ != DialogResult.Yes)
            {
                _подсказка.Text = "Выгрузка отменена: целевой файл занят другим приложением.";

                return null;
            }

            путь = альтернатива;
            _путь.Text = путь;
        }

        СобратьСостояние();

        try
        {
            // Пост-действие выбирается отдельным финальным диалогом после
            // сохранения, поэтому файл сначала записывается без действия.
            СобратьСостояние();

            _контроллер.PostAction = ExportPostAction.None;

            var итог = _мастер.Export(_контроллер.BuildRequest(Записи));

            Результат = итог;

            var описание = $"Файл: {итог.FilePath}{Environment.NewLine}" +
                $"Записей: {итог.RecordCount}{Environment.NewLine}" +
                $"Формат: {ОписаниеФормата(итог.Format)}";

            _результат.Text = описание;
            _подсказка.Text = "Выгрузка завершена.";

            var действие = ПоказатьДиалогПостДействия();

            _контроллер.PostAction = действие;

            if (действие != ExportPostAction.None)
            {
                _мастер.RunPostAction(действие, итог.FilePath);
            }

            DialogResult = DialogResult.OK;
            Close();

            return итог;
        }
        catch (ArgumentException ошибка)
        {
            _подсказка.Text = $"Выгрузка не выполнена: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер выгрузки данных", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (InvalidOperationException ошибка)
        {
            _подсказка.Text = $"Выгрузка не выполнена: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер выгрузки данных", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (IOException ошибка)
        {
            _подсказка.Text = $"Выгрузка не выполнена: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер выгрузки данных", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (UnauthorizedAccessException ошибка)
        {
            _подсказка.Text = $"Выгрузка не выполнена: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер выгрузки данных", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return null;
    }

    /// <summary>
    /// Название формата выгрузки для показа оператору.
    /// </summary>
    /// <param name="формат">Формат выгрузки.</param>
    /// <returns>Название формата.</returns>
    private static string ОписаниеФормата(ExchangeFormat формат)
    {
        foreach (var (название, значение) in Форматы)
        {
            if (значение == формат)
            {
                return название;
            }
        }

        return формат.ToString();
    }

    /// <summary>
    /// Строит первый шаг мастера: формат файла, формат дат, разделитель и
    /// кодировка.
    /// </summary>
    private void ПостроитьШагПараметров()
    {
        var группа = new GroupBox
        {
            Text = "Шаг 1. Параметры выгрузки",
            Location = new Point(10, 10),
            Size = new Size(500, 200),
        };

        var меткаФормата = new Label { Text = "Формат файла:", AutoSize = true, Location = new Point(12, 24) };
        var меткаДаты = new Label { Text = "Формат даты:", AutoSize = true, Location = new Point(12, 52) };
        var меткаСвоего = new Label { Text = "Свой формат даты:", AutoSize = true, Location = new Point(12, 80) };

        foreach (var (название, _) in Форматы)
        {
            _формат.Items.Add(название);
        }

        _формат.Location = new Point(120, 21);
        _формат.Width = 180;
        _формат.SelectedIndex = 1;

        foreach (var (название, _) in ФорматыДаты)
        {
            _форматДаты.Items.Add(название);
        }

        _форматДаты.Location = new Point(120, 49);
        _форматДаты.Width = 180;
        _форматДаты.SelectedIndex = 0;

        _свойФорматДаты.Location = new Point(120, 77);
        _свойФорматДаты.Width = 180;

        _группаТекста.Location = new Point(8, 100);
        _группаТекста.Size = new Size(480, 72);

        var меткаРазделителя = new Label { Text = "Разделитель:", AutoSize = true, Location = new Point(12, 24) };
        var меткаКодировки = new Label { Text = "Кодировка:", AutoSize = true, Location = new Point(170, 24) };

        foreach (var название in НазванияРазделителей)
        {
            _разделитель.Items.Add(название);
        }

        _разделитель.Location = new Point(85, 21);
        _разделитель.Width = 75;
        _разделитель.SelectedIndex = 0;

        foreach (var название in НазванияКодировок)
        {
            _кодировка.Items.Add(название);
        }

        _кодировка.Location = new Point(240, 21);
        _кодировка.Width = 225;
        _кодировка.SelectedIndex = 0;

        _заголовки.Location = new Point(12, 48);
        _заголовки.AutoSize = true;

        _группаТекста.Controls.Add(меткаРазделителя);
        _группаТекста.Controls.Add(меткаКодировки);
        _группаТекста.Controls.Add(_разделитель);
        _группаТекста.Controls.Add(_кодировка);
        _группаТекста.Controls.Add(_заголовки);

        var пояснение = new Label
        {
            Text = "Разделитель и кодировка применяются только к форматам TXT и CSV.",
            AutoSize = true,
            Location = new Point(12, 178),
            ForeColor = SystemColors.GrayText,
        };

        группа.Controls.Add(меткаФормата);
        группа.Controls.Add(_формат);
        группа.Controls.Add(меткаДаты);
        группа.Controls.Add(_форматДаты);
        группа.Controls.Add(меткаСвоего);
        группа.Controls.Add(_свойФорматДаты);
        группа.Controls.Add(_группаТекста);
        группа.Controls.Add(пояснение);

        Controls.Add(группа);

        _формат.SelectedIndexChanged += (_, _) => ОбновитьДоступность();
        _форматДаты.SelectedIndexChanged += (_, _) => ОбновитьДоступность();
    }

    /// <summary>
    /// Строит второй шаг мастера: выбор итогового файла выгрузки.
    /// </summary>
    private void ПостроитьШагФайла()
    {
        var группа = new GroupBox
        {
            Text = "Шаг 2. Файл выгрузки",
            Location = new Point(10, 220),
            Size = new Size(500, 100),
        };

        var метка = new Label { Text = "Файл выгрузки:", AutoSize = true, Location = new Point(12, 26) };

        _путь.Location = new Point(110, 23);
        _путь.Width = 300;

        _обзор.Text = "Обзор...";
        _обзор.AccessibleName = "Выбрать файл выгрузки";
        _обзор.AccessibleDescription = "Открыть выбор файла для сохранения выгрузки.";
        _обзор.Location = new Point(418, 22);
        _обзор.Size = new Size(70, 24);
        _обзор.Click += (_, _) => ВыбратьФайл();

        var пояснение = new Label
        {
            Text = "Если файл занят другой программой, программа предложит сохранить "
                + "выгрузку под другим именем.",
            Location = new Point(12, 58),
            Size = new Size(476, 32),
            ForeColor = SystemColors.GrayText,
        };

        группа.Controls.Add(метка);
        группа.Controls.Add(_путь);
        группа.Controls.Add(_обзор);
        группа.Controls.Add(пояснение);

        Controls.Add(группа);
    }

    /// <summary>
    /// Строит поле результата выгрузки.
    /// </summary>
    private void ПостроитьРезультат()
    {
        _результат.Location = new Point(10, 356);
        _результат.Size = new Size(500, 50);
        _результат.AutoSize = false;
        _результат.Text = "Выгрузка ещё не выполнялась.";

        _подсказка.Location = new Point(10, 408);
        _подсказка.Size = new Size(500, 16);
        _подсказка.AutoSize = false;
        _подсказка.Text = string.Empty;

        Controls.Add(_результат);
        Controls.Add(_подсказка);
    }

    /// <summary>
    /// Строит кнопки мастера.
    /// </summary>
    private void ПостроитьКнопки()
    {
        _экспорт.Text = "Экспорт";
        _экспорт.AccessibleName = "Выполнить экспорт";
        _экспорт.AccessibleDescription = "Выполнить выгрузку адресной книги в выбранный формат.";
        _экспорт.Location = new Point(10, 428);
        _экспорт.Size = new Size(120, 26);
        _экспорт.Click += (_, _) => ВыполнитьВыгрузку();

        _отмена.Text = "Закрыть";
        _отмена.AccessibleName = "Закрыть мастер выгрузки";
        _отмена.AccessibleDescription = "Закрыть мастер выгрузки.";
        _отмена.Location = new Point(390, 428);
        _отмена.Size = new Size(120, 26);
        _отмена.DialogResult = DialogResult.Cancel;

        Controls.Add(_экспорт);
        Controls.Add(_отмена);
    }

    /// <summary>
    /// Включает и отключает элементы, зависящие от выбранных форматов: свой
    /// формат даты — только для произвольного формата, разделитель и
    /// кодировка — только для текстовых форматов.
    /// </summary>
    private void ОбновитьДоступность()
    {
        var форматДаты = _форматДаты.SelectedIndex >= 0
            ? ФорматыДаты[_форматДаты.SelectedIndex].Формат
            : ExportDateFormat.Iso;

        _свойФорматДаты.Enabled = форматДаты == ExportDateFormat.Custom;

        var формат = _формат.SelectedIndex >= 0
            ? Форматы[_формат.SelectedIndex].Формат
            : ExchangeFormat.Csv;

        _группаТекста.Enabled = формат is ExchangeFormat.Txt or ExchangeFormat.Csv;
    }

    /// <summary>
    /// Открывает диалог сохранения и подставляет выбранный путь.
    /// </summary>
    private void ВыбратьФайл()
    {
        using var диалог = new SaveFileDialog
        {
            Title = "Сохранение файла выгрузки",
            Filter = ФильтрФормата(ТекущийФормат()),
            FileName = "address_book" + РасширениеФормата(ТекущийФормат()),
            OverwritePrompt = false,
        };

        var текущий = _путь.Text.Trim();

        if (текущий.Length > 0)
        {
            диалог.FileName = Path.GetFileName(текущий);
            диалог.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(текущий));
        }

        if (диалог.ShowDialog(this) == DialogResult.OK)
        {
            _путь.Text = диалог.FileName;
        }
    }

    /// <summary>
    /// Переносит состояние элементов формы в контроллер.
    /// </summary>
    private void СобратьСостояние()
    {
        _контроллер.Format = ТекущийФормат();
        _контроллер.DateFormat = _форматДаты.SelectedIndex >= 0
            ? ФорматыДаты[_форматДаты.SelectedIndex].Формат
            : ExportDateFormat.Iso;
        _контроллер.CustomDateFormat = _свойФорматДаты.Text;
        _контроллер.Delimiter = _разделитель.SelectedIndex >= 0
            ? Разделители[_разделитель.SelectedIndex]
            : ';';
        _контроллер.TextEncoding = _кодировка.SelectedIndex >= 0
            ? Кодировки[_кодировка.SelectedIndex]
            : TextEncodingDetector.Utf8WithBom;
        _контроллер.IncludeHeader = _заголовки.Checked;
        _контроллер.OutputPath = _путь.Text.Trim();
    }

    /// <summary>
    /// Показывает финальный диалог пост-действия: «Сохранение файлов
    /// завершено, что вы хотите сделать?» с кнопками «Открыть файл»,
    /// «Открыть папку» и «Закрыть» (ТЗ §8.12, функция 160).
    /// </summary>
    /// <returns>Выбранное действие после выгрузки.</returns>
    public ExportPostAction ПоказатьДиалогПостДействия()
    {
        using var диалог = new ДиалогПостДействия();

        return диалог.ShowDialog(this) switch
        {
            DialogResult.Yes => ExportPostAction.OpenFile,
            DialogResult.No => ExportPostAction.OpenFolder,
            _ => ExportPostAction.None,
        };
    }

    /// <summary>
    /// Формат, выбранный в списке форматов файла.
    /// </summary>
    /// <returns>Выбранный формат выгрузки.</returns>
    private ExchangeFormat ТекущийФормат() => _формат.SelectedIndex >= 0
        ? Форматы[_формат.SelectedIndex].Формат
        : ExchangeFormat.Csv;

    /// <summary>
    /// Расширение файла, соответствующее формату выгрузки.
    /// </summary>
    /// <param name="формат">Формат выгрузки.</param>
    /// <returns>Расширение с точкой.</returns>
    private static string РасширениеФормата(ExchangeFormat формат) => формат switch
    {
        ExchangeFormat.Txt => ".txt",
        ExchangeFormat.Csv => ".csv",
        ExchangeFormat.Html => ".html",
        ExchangeFormat.Rtf => ".rtf",
        ExchangeFormat.Xls => ".xls",
        ExchangeFormat.Xlsx => ".xlsx",
        _ => ".csv",
    };

    /// <summary>
    /// Фильтр диалога сохранения для выбранного формата.
    /// </summary>
    /// <param name="формат">Формат выгрузки.</param>
    /// <returns>Строка фильтра.</returns>
    private static string ФильтрФормата(ExchangeFormat формат) => формат switch
    {
        ExchangeFormat.Txt => "Текстовый файл (*.txt)|*.txt",
        ExchangeFormat.Csv => "Текст с разделителями (*.csv)|*.csv",
        ExchangeFormat.Html => "HTML-документ (*.html;*.htm)|*.html;*.htm",
        ExchangeFormat.Rtf => "Rich Text Format (*.rtf)|*.rtf",
        ExchangeFormat.Xls => "Книга Excel (*.xls)|*.xls",
        ExchangeFormat.Xlsx => "Книга Excel (*.xlsx)|*.xlsx",
        _ => "Все файлы (*.*)|*.*",
    };
}

/// <summary>
/// Финальный диалог пост-действия мастера выгрузки: «Сохранение файлов
/// завершено, что вы хотите сделать?» с кнопками «Открыть файл»,
/// «Открыть папку» и «Закрыть» (ТЗ §8.12, функция 160).
/// </summary>
/// <remarks>
/// Результат диалога: <see cref="DialogResult.Yes"/> — открыть файл,
/// <see cref="DialogResult.No"/> — открыть папку, иначе — закрыть.
/// </remarks>
public sealed class ДиалогПостДействия : Form
{
    /// <summary>
    /// Создаёт диалог пост-действия.
    /// </summary>
    public ДиалогПостДействия()
    {
        Text = "Сохранение файлов завершено";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(420, 140);

        var вопрос = new Label
        {
            Text = "Сохранение файлов завершено, что вы хотите сделать?",
            AutoSize = true,
            Location = new Point(16, 18),
        };

        var открытьФайл = new Button
        {
            Text = "Открыть файл",
            Location = new Point(16, 60),
            Size = new Size(120, 28),
            DialogResult = DialogResult.Yes,
        };

        var открытьПапку = new Button
        {
            Text = "Открыть папку",
            Location = new Point(148, 60),
            Size = new Size(120, 28),
            DialogResult = DialogResult.No,
        };

        var закрыть = new Button
        {
            Text = "Закрыть",
            Location = new Point(280, 60),
            Size = new Size(120, 28),
            DialogResult = DialogResult.Cancel,
        };

        закрыть.Click += (_, _) => Close();

        Controls.Add(вопрос);
        Controls.Add(открытьФайл);
        Controls.Add(открытьПапку);
        Controls.Add(закрыть);

        AcceptButton = открытьФайл;
        CancelButton = закрыть;
    }
}
