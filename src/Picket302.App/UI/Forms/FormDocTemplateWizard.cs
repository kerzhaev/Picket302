using System.Drawing;
using System.Windows.Forms;
using Picket302.Application.Templating;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Мастер редактирования шаблонов документов: предпросмотр структуры
/// документа, вставка доступных маркеров БД и проверка структуры получаемого
/// документа (ТЗ §8.5, функция 66: «Мастер редактирования шаблонов документов»).
/// </summary>
/// <remarks>
/// <para>
/// Форма только отображает состояние <see cref="DocTemplateWizardController"/>:
/// вся логика шага (открытие шаблона, предпросмотр маркеров, вставка маркера в
/// позицию курсора, проверка структуры, сохранение) вынесена в контроллер и
/// проверяется модульными тестами без создания окна.
/// </para>
/// <para>
/// Двойной клик по маркеру в списке доступных маркеров вставляет его текст в
/// позицию курсора редактора (ТЗ §8.5, функция 66).
/// </para>
/// </remarks>
public sealed class FormDocTemplateWizard : Form
{
    /// <summary>Шаблон фильтра файлов шаблонов документов.</summary>
    private const string ФильтрШаблонов =
        "Шаблоны документов (*.txt;*.htm;*.html;*.xml;*.xslt;*.xsl;*.docx)|" +
        "*.txt;*.htm;*.html;*.xml;*.xslt;*.xsl;*.docx|" +
        "Текст (*.txt)|*.txt|" +
        "HTML (*.htm;*.html)|*.htm;*.html|" +
        "XML и XSLT (*.xml;*.xslt;*.xsl)|*.xml;*.xslt;*.xsl|" +
        "Документ OpenXML (*.docx)|*.docx|" +
        "Все файлы (*.*)|*.*";

    private readonly DocTemplateWizardController _контроллер;

    private readonly TextBox _путь = new() { ReadOnly = true };
    private readonly Button _открыть = new();
    private readonly Button _сохранитьКак = new();

    private readonly TextBox _фильтр = new();
    private readonly ListBox _маркеры = new();
    private readonly Button _вставить = new();

    private readonly TextBox _редактор = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
    };

    private readonly ListBox _структура = new();
    private readonly ListBox _проблемы = new();
    private readonly Button _проверить = new();
    private readonly Label _подсказка = new();

    private readonly Button _готово = new();
    private readonly Button _закрыть = new();

    /// <summary>Имена маркеров списка; для заголовка группы — <see langword="null"/>.</summary>
    private readonly List<string?> _именаМаркеров = [];

    /// <summary>Признак обновления редактора из контроллера.</summary>
    private bool _обновлениеРедактора;

    /// <summary>
    /// Создаёт мастер редактирования шаблонов документов.
    /// </summary>
    /// <param name="путьШаблона">
    /// Путь к шаблону, открываемому сразу; при отсутствии шаблон выбирается
    /// кнопкой «Открыть...».
    /// </param>
    /// <param name="значения">
    /// Значения маркеров для предпросмотра структуры (например, реквизиты
    /// выбранного контрагента); при отсутствии значения маркеров не
    /// разрешаются.
    /// </param>
    public FormDocTemplateWizard(
        string? путьШаблона = null,
        IReadOnlyDictionary<string, object>? значения = null)
    {
        _контроллер = new DocTemplateWizardController(parameters: значения);

        Text = "Мастер редактирования шаблонов документов";
        Font = new Font("Segoe UI", 8f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(1080, 640);

        ПостроитьПолеШаблона();
        ПостроитьСписокМаркеров();
        ПостроитьРедактор();
        ПостроитьСтруктуру();
        ПостроитьПроверку();
        ПостроитьКнопки();

        AcceptButton = _готово;
        CancelButton = _закрыть;

        ЗаполнитьМаркеры(string.Empty);
        ОбновитьСтруктуру();

        if (!string.IsNullOrWhiteSpace(путьШаблона))
        {
            ОткрытьШаблон(путьШаблона);
        }
    }

    /// <summary>Контроллер мастера: состояние шаблона.</summary>
    public DocTemplateWizardController Контроллер => _контроллер;

    /// <summary>Путь к открытому шаблону; пустая строка — шаблон не выбран.</summary>
    public string ПутьШаблона => _контроллер.TemplatePath;

    /// <summary>Признак несохранённых изменений шаблона.</summary>
    public bool Изменён => _контроллер.IsDirty;

    /// <summary>
    /// Открывает шаблон для редактирования маркеров
    /// (ТЗ §8.5, функция 66: <c>public void EditTemplateMarkers(string templatePath)</c>).
    /// </summary>
    /// <param name="templatePath">Путь к файлу шаблона.</param>
    public void EditTemplateMarkers(string templatePath) => ОткрытьШаблон(templatePath);

    /// <summary>
    /// Проверяет структуру получаемого документа и показывает найденные
    /// проблемы.
    /// </summary>
    /// <returns>Перечень проблем; пустой перечень — структура корректна.</returns>
    public IReadOnlyList<string> ПроверитьСтруктуру()
    {
        var проблемы = _контроллер.CheckStructure();

        _проблемы.BeginUpdate();
        _проблемы.Items.Clear();

        if (проблемы.Count == 0)
        {
            _проблемы.Items.Add("Структура документа корректна");
            _подсказка.Text = "Структура документа корректна.";
        }
        else
        {
            foreach (var проблема in проблемы)
            {
                _проблемы.Items.Add(проблема);
            }

            _подсказка.Text = $"Найдено проблем структуры: {проблемы.Count}.";
        }

        _проблемы.EndUpdate();

        return проблемы;
    }

    /// <summary>
    /// Сохраняет отредактированный шаблон; при отсутствии пути у оператора
    /// запрашивается имя файла.
    /// </summary>
    /// <returns><see langword="true"/>, если шаблон сохранён.</returns>
    public bool СохранитьШаблон()
    {
        var путь = _контроллер.TemplatePath;

        if (путь.Length == 0)
        {
            using var диалог = new SaveFileDialog
            {
                Title = "Сохранение шаблона документа",
                Filter = ФильтрШаблонов,
                FileName = "template.txt",
                OverwritePrompt = true,
            };

            if (диалог.ShowDialog(this) != DialogResult.OK)
            {
                _подсказка.Text = "Сохранение отменено.";

                return false;
            }

            путь = диалог.FileName;
        }

        try
        {
            _контроллер.Save(путь);

            _путь.Text = _контроллер.TemplatePath;
            _подсказка.Text = $"Шаблон сохранён: {_контроллер.TemplatePath}";

            return true;
        }
        catch (ArgumentException ошибка)
        {
            _подсказка.Text = $"Шаблон не сохранён: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер шаблонов документов", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }
        catch (InvalidOperationException ошибка)
        {
            _подсказка.Text = $"Шаблон не сохранён: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер шаблонов документов", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }
        catch (IOException ошибка)
        {
            _подсказка.Text = $"Шаблон не сохранён: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер шаблонов документов", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }
        catch (UnauthorizedAccessException ошибка)
        {
            _подсказка.Text = $"Шаблон не сохранён: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер шаблонов документов", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }
    }

    /// <summary>
    /// Строит поле выбора файла шаблона.
    /// </summary>
    private void ПостроитьПолеШаблона()
    {
        var метка = new Label { Text = "Шаблон:", AutoSize = true, Location = new Point(10, 16) };

        _путь.Location = new Point(70, 13);
        _путь.Width = 680;

        _открыть.Text = "Открыть...";
        _открыть.Location = new Point(760, 11);
        _открыть.Size = new Size(100, 24);
        _открыть.Click += (_, _) => ВыбратьШаблон();

        _сохранитьКак.Text = "Сохранить как...";
        _сохранитьКак.Location = new Point(868, 11);
        _сохранитьКак.Size = new Size(120, 24);
        _сохранитьКак.Click += (_, _) => СохранитьШаблон();

        Controls.Add(метка);
        Controls.Add(_путь);
        Controls.Add(_открыть);
        Controls.Add(_сохранитьКак);
    }

    /// <summary>
    /// Строит список доступных маркеров БД.
    /// </summary>
    private void ПостроитьСписокМаркеров()
    {
        var группа = new GroupBox
        {
            Text = "Маркеры БД (двойной клик — вставить в позицию курсора)",
            Location = new Point(10, 50),
            Size = new Size(280, 520),
        };

        _фильтр.Location = new Point(12, 22);
        _фильтр.Width = 250;

        _маркеры.Location = new Point(12, 50);
        _маркеры.Size = new Size(250, 400);
        _маркеры.DoubleClick += (_, _) => ВставитьМаркер();

        _вставить.Text = "Вставить";
        _вставить.Location = new Point(12, 458);
        _вставить.Size = new Size(100, 24);
        _вставить.Click += (_, _) => ВставитьМаркер();

        группа.Controls.Add(_фильтр);
        группа.Controls.Add(_маркеры);
        группа.Controls.Add(_вставить);

        Controls.Add(группа);

        _фильтр.TextChanged += (_, _) => ЗаполнитьМаркеры(_фильтр.Text);
    }

    /// <summary>
    /// Строит редактор текста шаблона.
    /// </summary>
    private void ПостроитьРедактор()
    {
        var группа = new GroupBox
        {
            Text = "Текст шаблона",
            Location = new Point(300, 50),
            Size = new Size(470, 520),
        };

        _редактор.Location = new Point(12, 22);
        _редактор.Size = new Size(444, 482);
        _редактор.TextChanged += (_, _) => ТекстИзменён();

        группа.Controls.Add(_редактор);

        Controls.Add(группа);
    }

    /// <summary>
    /// Строит предпросмотр структуры документа.
    /// </summary>
    private void ПостроитьСтруктуру()
    {
        var группа = new GroupBox
        {
            Text = "Структура документа (маркеры в порядке появления)",
            Location = new Point(780, 50),
            Size = new Size(290, 270),
        };

        _структура.Location = new Point(12, 22);
        _структура.Size = new Size(264, 232);

        группа.Controls.Add(_структура);

        Controls.Add(группа);
    }

    /// <summary>
    /// Строит блок проверки структуры получаемого документа.
    /// </summary>
    private void ПостроитьПроверку()
    {
        var группа = new GroupBox
        {
            Text = "Проверка структуры",
            Location = new Point(780, 330),
            Size = new Size(290, 240),
        };

        _проверить.Text = "Проверить структуру";
        _проверить.Location = new Point(12, 22);
        _проверить.Size = new Size(150, 24);
        _проверить.Click += (_, _) => ПроверитьСтруктуру();

        _проблемы.Location = new Point(12, 54);
        _проблемы.Size = new Size(264, 168);

        группа.Controls.Add(_проверить);
        группа.Controls.Add(_проблемы);

        Controls.Add(группа);
    }

    /// <summary>
    /// Строит кнопки мастера и строку состояния.
    /// </summary>
    private void ПостроитьКнопки()
    {
        _подсказка.Location = new Point(10, 600);
        _подсказка.Size = new Size(700, 18);
        _подсказка.AutoSize = false;

        _готово.Text = "Сохранить и закрыть";
        _готово.Location = new Point(720, 598);
        _готово.Size = new Size(160, 28);
        _готово.Click += (_, _) =>
        {
            if (!СохранитьШаблон())
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        _закрыть.Text = "Закрыть";
        _закрыть.Location = new Point(890, 598);
        _закрыть.Size = new Size(160, 28);
        _закрыть.DialogResult = DialogResult.Cancel;

        Controls.Add(_подсказка);
        Controls.Add(_готово);
        Controls.Add(_закрыть);
    }

    /// <summary>
    /// Заполняет список доступных маркеров с группировкой по группам.
    /// </summary>
    /// <param name="фильтр">Фильтр по имени, описанию или группе маркера.</param>
    private void ЗаполнитьМаркеры(string? фильтр)
    {
        _маркеры.BeginUpdate();
        _маркеры.Items.Clear();
        _именаМаркеров.Clear();

        string? текущаяГруппа = null;

        foreach (var маркер in _контроллер.FindMarkers(фильтр))
        {
            if (маркер.Group != текущаяГруппа)
            {
                текущаяГруппа = маркер.Group;
                _маркеры.Items.Add($"— {текущаяГруппа} —");
                _именаМаркеров.Add(null);
            }

            _маркеры.Items.Add($"{маркер.Text}  {маркер.Description}");
            _именаМаркеров.Add(маркер.Name);
        }

        if (_маркеры.Items.Count == 0)
        {
            _маркеры.Items.Add("Маркеры не найдены");
            _именаМаркеров.Add(null);
        }

        _маркеры.EndUpdate();
    }

    /// <summary>
    /// Вставляет текст выбранного маркера в позицию курсора редактора.
    /// </summary>
    private void ВставитьМаркер()
    {
        var индекс = _маркеры.SelectedIndex;

        if (индекс < 0 || индекс >= _именаМаркеров.Count)
        {
            return;
        }

        var имя = _именаМаркеров[индекс];

        if (имя is null)
        {
            _подсказка.Text = "Выберите маркер из списка.";

            return;
        }

        _контроллер.SetCursorPosition(_редактор.SelectionStart);

        var текст = _контроллер.InsertMarker(имя);

        ОбновитьРедактор();
        ОбновитьСтруктуру();

        _подсказка.Text = $"Маркер {текст} вставлен в позицию {_контроллер.CursorPosition}.";
    }

    /// <summary>
    /// Открывает шаблон и показывает его в редакторе.
    /// </summary>
    /// <param name="путь">Путь к файлу шаблона.</param>
    private void ОткрытьШаблон(string путь)
    {
        try
        {
            _контроллер.OpenTemplate(путь);
        }
        catch (FileNotFoundException ошибка)
        {
            _подсказка.Text = ошибка.Message;
            MessageBox.Show(this, ошибка.Message, "Мастер шаблонов документов", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }
        catch (InvalidOperationException ошибка)
        {
            _подсказка.Text = ошибка.Message;
            MessageBox.Show(this, ошибка.Message, "Мастер шаблонов документов", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }
        catch (IOException ошибка)
        {
            _подсказка.Text = $"Шаблон не открыт: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер шаблонов документов", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }
        catch (UnauthorizedAccessException ошибка)
        {
            _подсказка.Text = $"Шаблон не открыт: {ошибка.Message}";
            MessageBox.Show(this, _подсказка.Text, "Мастер шаблонов документов", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        _путь.Text = _контроллер.TemplatePath;

        ОбновитьРедактор();
        ОбновитьСтруктуру();

        _подсказка.Text = $"Открыт шаблон: {_контроллер.TemplatePath}";
    }

    /// <summary>
    /// Открывает диалог выбора файла шаблона.
    /// </summary>
    private void ВыбратьШаблон()
    {
        using var диалог = new OpenFileDialog
        {
            Title = "Открытие шаблона документа",
            Filter = ФильтрШаблонов,
            CheckFileExists = true,
        };

        var текущий = _контроллер.TemplatePath;

        if (текущий.Length > 0 && File.Exists(текущий))
        {
            диалог.FileName = Path.GetFileName(текущий);
            диалог.InitialDirectory = Path.GetDirectoryName(текущий);
        }

        if (диалог.ShowDialog(this) == DialogResult.OK)
        {
            ОткрытьШаблон(диалог.FileName);
        }
    }

    /// <summary>
    /// Переносит изменение текста редактора в контроллер и обновляет
    /// предпросмотр структуры.
    /// </summary>
    private void ТекстИзменён()
    {
        if (_обновлениеРедактора)
        {
            return;
        }

        _контроллер.SetTemplateText(_редактор.Text);

        ОбновитьСтруктуру();
    }

    /// <summary>
    /// Обновляет содержимое редактора по состоянию контроллера.
    /// </summary>
    private void ОбновитьРедактор()
    {
        _обновлениеРедактора = true;

        try
        {
            _редактор.Text = _контроллер.TemplateText;

            var позиция = Math.Min(_контроллер.CursorPosition, _редактор.Text.Length);

            _редактор.SelectionStart = позиция;
            _редактор.SelectionLength = 0;
        }
        finally
        {
            _обновлениеРедактора = false;
        }
    }

    /// <summary>
    /// Обновляет предпросмотр структуры документа по состоянию контроллера.
    /// </summary>
    private void ОбновитьСтруктуру()
    {
        _структура.BeginUpdate();
        _структура.Items.Clear();

        foreach (var маркер in _контроллер.Markers)
        {
            var значение = маркер.IsResolved
                ? маркер.Value ?? string.Empty
                : "значение не задано";

            _структура.Items.Add($"{маркер.Order + 1}. {маркер.Marker} — стр. {маркер.Line} — {значение}");
        }

        if (_структура.Items.Count == 0)
        {
            _структура.Items.Add("Маркеры не найдены");
        }

        _структура.EndUpdate();
    }
}
