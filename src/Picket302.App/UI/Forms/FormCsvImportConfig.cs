using System.Drawing;
using System.Windows.Forms;
using Picket302.Application.Import;
using Picket302.Domain.Contracts;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Диалог настройки параметров разбора CSV и текстового файла
/// (ТЗ §8.5, функция 73: «Интерактивное определение кодировки (ANSI/UTF-8),
/// символа разделителя, символа экранирования кавычек, строки начала данных;
/// сохранение профилей сопоставления»).
/// </summary>
/// <remarks>
/// <para>
/// Диалог показывает живой предпросмотр разбора: строки, число столбцов в
/// которых не совпадает с числом столбцов заголовка, помечаются как ошибочные
/// (ТЗ §8.5, функция 73: «Несовпадение числа столбцов в строке помечает
/// строку как ошибочную»).
/// </para>
/// <para>
/// Готовый профиль сохраняется в файл <c>*.csvprof</c> через
/// <see cref="ICsvProfileStore"/> и может быть загружен повторно, чтобы не
/// подбирать параметры разбора заново.
/// </para>
/// </remarks>
public sealed class FormCsvImportConfig : Form
{
    /// <summary>Кодировки, предлагаемые для разбора файла.</summary>
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

    private readonly ICsvProfileStore _хранилище;
    private readonly ComboBox _кодировка = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _разделитель = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _кавычки = new() { MaxLength = 1 };
    private readonly CheckBox _заголовки = new() { Text = "Первая строка файла — заголовки" };
    private readonly NumericUpDown _началоДанных = new() { Minimum = 1, Maximum = 100000 };
    private readonly TextBox _имяПрофиля = new();
    private readonly DataGridView _предпросмотр = new();
    private readonly Label _сведения = new();
    private readonly Label _ошибка = new();

    private string _текст = string.Empty;

    /// <summary>
    /// Создаёт диалог настройки разбора CSV.
    /// </summary>
    /// <param name="хранилище">
    /// Хранилище файлов профилей (*.csvprof); при отсутствии используется
    /// хранилище по умолчанию.
    /// </param>
    /// <param name="исходный">
    /// Профиль, параметры которого показываются в диалоге; при отсутствии
    /// параметры определяются по умолчанию.
    /// </param>
    public FormCsvImportConfig(
        ICsvProfileStore? хранилище = null,
        CsvProfile? исходный = null)
    {
        _хранилище = хранилище ?? ImportWizardController.CreateProfileStore();

        Text = "Настройка разбора CSV";
        AccessibleName = "Настройка разбора CSV";
        AccessibleDescription = "Параметры кодировки, разделителя и профиля импорта CSV.";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        // Нижняя панель кнопок имеет собственную строку. Прежний размер 520
        // оставлял кнопки на координатах 506..530 за пределами клиента.
        ClientSize = new Size(640, 560);

        ПостроитьПараметры();
        ПостроитьПредпросмотр();
        ПостроитьКнопки();

        ПрименитьПрофиль(исходный ?? ПостроитьПрофильПоУмолчанию());

        _кодировка.SelectedIndexChanged += (_, _) => ОбновитьПредпросмотр();
        _разделитель.SelectedIndexChanged += (_, _) => ОбновитьПредпросмотр();
        _кавычки.TextChanged += (_, _) => ОбновитьПредпросмотр();
        _заголовки.CheckedChanged += (_, _) => ОбновитьПредпросмотр();
        _началоДанных.ValueChanged += (_, _) => ОбновитьПредпросмотр();
    }

    /// <summary>
    /// Настроенный профиль сопоставления; <see langword="null"/> — оператор
    /// отказался от настройки.
    /// </summary>
    public CsvProfile? Profile { get; private set; }

    /// <summary>
    /// Текст предпросмотра файла: при изменении живой предпросмотр
    /// перестраивается по текущим параметрам разбора.
    /// </summary>
    public string PreviewText
    {
        get => _текст;
        set
        {
            _текст = value ?? string.Empty;
            ОбновитьПредпросмотр();
        }
    }

    /// <summary>
    /// Применяет параметры профиля к диалогу и перестраивает предпросмотр.
    /// </summary>
    /// <param name="профиль">Применяемый профиль.</param>
    public void ApplyProfile(CsvProfile профиль)
    {
        ArgumentNullException.ThrowIfNull(профиль);
        ПрименитьПрофиль(профиль);
    }

    /// <summary>
    /// Возвращает профиль, настроенный в диалоге, по его текущим параметрам.
    /// </summary>
    /// <returns>Профиль сопоставления.</returns>
    public CsvProfile ConfigureCsvMapping() => ПостроитьПрофиль();

    /// <summary>
    /// Применяет текст предпросмотра и возвращает профиль сопоставления
    /// (ТЗ §8.5, функция 73: «public CsvProfile ConfigureCsvMapping(string
    /// previewText)»).
    /// </summary>
    /// <param name="previewText">Текст предпросмотра файла.</param>
    /// <returns>Профиль сопоставления.</returns>
    public CsvProfile ConfigureCsvMapping(string previewText)
    {
        PreviewText = previewText ?? string.Empty;

        return ПостроитьПрофиль();
    }

    private void ПостроитьПараметры()
    {
        var надписьКодировки = new Label { Text = "Кодировка:", Location = new Point(12, 15), AutoSize = true };
        _кодировка.Location = new Point(90, 12);
        _кодировка.Width = 200;
        _кодировка.AccessibleName = "Кодировка файла";
        _кодировка.AccessibleDescription = "Выберите кодировку исходного CSV-файла.";

        foreach (var (имя, подпись) in Кодировки)
        {
            _кодировка.Items.Add(new ПунктКодировки(имя, подпись));
        }

        var надписьРазделителя = new Label { Text = "Разделитель:", Location = new Point(12, 41), AutoSize = true };
        _разделитель.Location = new Point(90, 38);
        _разделитель.Width = 200;
        _разделитель.AccessibleName = "Разделитель столбцов";
        _разделитель.AccessibleDescription = "Выберите символ, разделяющий столбцы CSV-файла.";

        foreach (var (знак, подпись) in Разделители)
        {
            _разделитель.Items.Add(new ПунктРазделителя(знак, подпись));
        }

        var надписьКавычек = new Label { Text = "Кавычки:", Location = new Point(300, 41), AutoSize = true };
        _кавычки.Location = new Point(360, 38);
        _кавычки.Width = 30;
        _кавычки.AccessibleName = "Символ кавычек";
        _кавычки.AccessibleDescription = "Один символ, используемый для экранирования значения CSV.";

        _заголовки.Location = new Point(12, 66);
        _заголовки.AutoSize = true;
        _заголовки.AccessibleName = "Первая строка содержит заголовки";
        _заголовки.AccessibleDescription = "Указать, что первая строка CSV содержит названия столбцов.";

        var надписьНачала = new Label { Text = "Первая строка данных:", Location = new Point(210, 69), AutoSize = true };
        _началоДанных.Location = new Point(320, 66);
        _началоДанных.Width = 70;
        _началоДанных.AccessibleName = "Номер первой строки данных";
        _началоДанных.AccessibleDescription = "Номер строки, с которой начинается импорт данных.";

        var надписьИмени = new Label { Text = "Имя профиля:", Location = new Point(12, 96), AutoSize = true };
        _имяПрофиля.Location = new Point(90, 93);
        _имяПрофиля.Width = 300;
        _имяПрофиля.AccessibleName = "Имя профиля импорта";
        _имяПрофиля.AccessibleDescription = "Имя профиля параметров разбора CSV.";

        Controls.Add(надписьКодировки);
        Controls.Add(_кодировка);
        Controls.Add(надписьРазделителя);
        Controls.Add(_разделитель);
        Controls.Add(надписьКавычек);
        Controls.Add(_кавычки);
        Controls.Add(_заголовки);
        Controls.Add(надписьНачала);
        Controls.Add(_началоДанных);
        Controls.Add(надписьИмени);
        Controls.Add(_имяПрофиля);
    }

    private void ПостроитьПредпросмотр()
    {
        _предпросмотр.Location = new Point(12, 122);
        _предпросмотр.Size = new Size(616, 320);
        _предпросмотр.AllowUserToAddRows = false;
        _предпросмотр.AllowUserToDeleteRows = false;
        _предпросмотр.ReadOnly = true;
        _предпросмотр.RowHeadersVisible = false;
        _предпросмотр.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _предпросмотр.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _предпросмотр.AccessibleName = "Предпросмотр CSV";
        _предпросмотр.AccessibleDescription = "Предпросмотр строк CSV с результатом разбора и ошибками столбцов.";

        _сведения.Location = new Point(12, 448);
        _сведения.AutoSize = false;
        _сведения.Size = new Size(616, 32);
        _сведения.AccessibleName = "Сведения о разборе CSV";
        _сведения.AccessibleDescription = "Количество строк и столбцов в текущем предпросмотре.";

        _ошибка.Location = new Point(12, 482);
        _ошибка.AutoSize = false;
        _ошибка.Size = new Size(616, 20);
        _ошибка.ForeColor = Color.Firebrick;
        _ошибка.AccessibleName = "Ошибка разбора CSV";
        _ошибка.AccessibleDescription = "Сообщение об ошибке текущих параметров разбора CSV.";

        Controls.Add(_предпросмотр);
        Controls.Add(_сведения);
        Controls.Add(_ошибка);
    }

    private void ПостроитьКнопки()
    {
        var сохранить = new Button
        {
            Text = "Сохранить профиль…",
            Size = new Size(140, 24),
            AccessibleName = "Сохранить профиль импорта",
            AccessibleDescription = "Сохранить текущие параметры разбора в профиль CSV.",
        };

        сохранить.Click += (_, _) => СохранитьПрофиль();

        var загрузить = new Button
        {
            Text = "Загрузить профиль…",
            Size = new Size(140, 24),
            AccessibleName = "Загрузить профиль импорта",
            AccessibleDescription = "Загрузить параметры разбора из сохранённого профиля CSV.",
        };

        загрузить.Click += (_, _) => ЗагрузитьПрофиль();

        var применить = new Button
        {
            Text = "Применить",
            Size = new Size(100, 24),
            AccessibleName = "Применить параметры разбора CSV",
            AccessibleDescription = "Принять параметры разбора и продолжить импорт.",
        };

        применить.Click += (_, _) =>
        {
            Profile = ПостроитьПрофиль();
            DialogResult = DialogResult.OK;
            Close();
        };

        var отмена = new Button
        {
            Text = "Отмена",
            Size = new Size(90, 24),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Отменить настройку разбора CSV",
            AccessibleDescription = "Закрыть диалог без применения параметров разбора.",
        };

        var панельКнопок = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(8, 6, 8, 4),
            AccessibleName = "Действия настройки разбора CSV",
            AccessibleDescription = "Сохранение, загрузка, применение или отмена параметров импорта.",
        };

        панельКнопок.Controls.Add(отмена);
        панельКнопок.Controls.Add(применить);
        панельКнопок.Controls.Add(загрузить);
        панельКнопок.Controls.Add(сохранить);
        Controls.Add(панельКнопок);

        AcceptButton = применить;
        CancelButton = отмена;
    }

    private void ПрименитьПрофиль(CsvProfile профиль)
    {
        _кодировка.SelectedItem = _кодировка.Items
            .Cast<ПунктКодировки>()
            .FirstOrDefault(пункт => string.Equals(пункт.Имя, профиль.EncodingName, StringComparison.OrdinalIgnoreCase))
            ?? _кодировка.Items[0];

        _разделитель.SelectedItem = _разделитель.Items
            .Cast<ПунктРазделителя>()
            .FirstOrDefault(пункт => пункт.Знак == профиль.Delimiter)
            ?? _разделитель.Items[0];

        _кавычки.Text = профиль.QuoteChar == '\0' ? "\"" : профиль.QuoteChar.ToString();
        _заголовки.Checked = профиль.HasHeaderRow;
        _началоДанных.Value = Math.Clamp(профиль.FirstDataRow, 1, 100000);
        _имяПрофиля.Text = string.IsNullOrWhiteSpace(профиль.Name) ? "Профиль импорта" : профиль.Name;
    }

    private CsvProfile ПостроитьПрофильПоУмолчанию() => new()
    {
        Name = "Профиль импорта",
        EncodingName = "utf-8",
        Delimiter = ';',
        QuoteChar = CsvProfileService.СимволКавычкиПоУмолчанию,
        HasHeaderRow = true,
        FirstDataRow = 1,
    };

    private CsvProfile ПостроитьПрофиль() => new()
    {
        Name = string.IsNullOrWhiteSpace(_имяПрофиля.Text) ? "Профиль импорта" : _имяПрофиля.Text.Trim(),
        EncodingName = ВыбраннаяКодировка(),
        Delimiter = ВыбранныйРазделитель(),
        QuoteChar = string.IsNullOrWhiteSpace(_кавычки.Text) ? '"' : _кавычки.Text[0],
        HasHeaderRow = _заголовки.Checked,
        FirstDataRow = (int)_началоДанных.Value,
    };

    private string ВыбраннаяКодировка()
        => (_кодировка.SelectedItem as ПунктКодировки)?.Имя ?? "utf-8";

    private char ВыбранныйРазделитель()
        => (_разделитель.SelectedItem as ПунктРазделителя)?.Знак ?? ';';

    private void ОбновитьПредпросмотр()
    {
        _ошибка.Text = string.Empty;
        _предпросмотр.Columns.Clear();
        _предпросмотр.Rows.Clear();

        if (string.IsNullOrWhiteSpace(_текст))
        {
            _сведения.Text = "Нет текста предпросмотра: параметры разбора применятся при чтении файла.";
            return;
        }

        var профиль = ПостроитьПрофиль();
        var таблица = new DelimitedTextReader().Read(_текст, профиль);

        var столбцы = таблица.Headers.Count > 0
            ? таблица.Headers
            : Enumerable.Range(1, МаксимальноеЧислоСтолбцов(таблица)).Select(номер => $"Столбец {номер}").ToList();

        foreach (var столбец in столбцы)
        {
            _предпросмотр.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = столбец,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        }

        var показано = 0;

        foreach (var строка in таблица.Rows)
        {
            if (показано >= 200)
            {
                break;
            }

            var индекс = _предпросмотр.Rows.Add();

            for (var номер = 0; номер < строка.Cells.Count && номер < _предпросмотр.Columns.Count; номер++)
            {
                _предпросмотр.Rows[индекс].Cells[номер].Value = строка.Cells[номер];
            }

            if (строка.HasError)
            {
                _предпросмотр.Rows[индекс].DefaultCellStyle.BackColor = Color.MistyRose;
                _предпросмотр.Rows[индекс].DefaultCellStyle.ForeColor = Color.Firebrick;
            }

            показано++;
        }

        var ошибки = таблица.ErrorCount;

        _сведения.Text = $"Строк в предпросмотре: {таблица.Rows.Count}; показано: {показано}; "
            + $"столбцов: {столбцы.Count}; ошибочных строк: {ошибки}.";
    }

    private static int МаксимальноеЧислоСтолбцов(DelimitedTextTable таблица)
        => таблица.Rows.Count == 0 ? 1 : таблица.Rows.Max(строка => строка.Cells.Count);

    private void СохранитьПрофиль()
    {
        using var диалог = new SaveFileDialog
        {
            Title = "Сохранить профиль сопоставления",
            Filter = "Файлы профиля (*.csvprof)|*.csvprof|Все файлы (*.*)",
            DefaultExt = "csvprof",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_имяПрофиля.Text) ? "Профиль импорта" : _имяПрофиля.Text.Trim(),
            InitialDirectory = НачальныйКаталог(),
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _хранилище.Save(ПостроитьПрофиль(), диалог.FileName);
            _ошибка.Text = string.Empty;
            _сведения.Text = $"Профиль сохранён: {диалог.FileName}";
        }
        catch (Exception ошибка) when (ошибка is IOException or InvalidOperationException or ArgumentException)
        {
            _ошибка.Text = $"Профиль не сохранён: {ошибка.Message}";
        }
    }

    private void ЗагрузитьПрофиль()
    {
        using var диалог = new OpenFileDialog
        {
            Title = "Загрузить профиль сопоставления",
            Filter = "Файлы профиля (*.csvprof)|*.csvprof|Все файлы (*.*)",
            CheckFileExists = true,
            InitialDirectory = НачальныйКаталог(),
        };

        if (диалог.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var профиль = _хранилище.Load(диалог.FileName);
            ПрименитьПрофиль(профиль);
            _ошибка.Text = string.Empty;
        }
        catch (Exception ошибка) when (ошибка is IOException or InvalidOperationException or ArgumentException)
        {
            _ошибка.Text = $"Профиль не загружен: {ошибка.Message}";
        }
    }

    private static string НачальныйКаталог()
    {
        var каталог = ImportWizardController.ProfilesDirectory();

        return Directory.Exists(каталог) ? каталог : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    /// <summary>Пункт списка кодировок.</summary>
    /// <param name="Имя">Имя кодировки.</param>
    /// <param name="Подпись">Подпись для интерфейса.</param>
    private sealed record ПунктКодировки(string Имя, string Подпись)
    {
        /// <inheritdoc />
        public override string ToString() => Подпись;
    }

    /// <summary>Пункт списка разделителей.</summary>
    /// <param name="Знак">Символ разделителя.</param>
    /// <param name="Подпись">Подпись для интерфейса.</param>
    private sealed record ПунктРазделителя(char Знак, string Подпись)
    {
        /// <inheritdoc />
        public override string ToString() => Подпись;
    }
}
