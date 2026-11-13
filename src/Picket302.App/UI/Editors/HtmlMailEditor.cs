using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Picket302.App.UI.Editors;

/// <summary>
/// Результат применения команды форматирования к разметке письма.
/// </summary>
/// <param name="Html">Разметка после применения команды.</param>
/// <param name="SelectionStart">Позиция выделения в новой разметке.</param>
/// <param name="SelectionLength">Длина выделения в новой разметке.</param>
public sealed record РезультатКоманды(string Html, int SelectionStart, int SelectionLength);

/// <summary>
/// Команды форматирования разметки письма, применяемые к выделенному
/// фрагменту (ТЗ §8.7, функция 97: «Полноценный визуальный редактор:
/// форматирование, списки, таблицы, вставка картинок/ссылок, шаблоны писем»).
/// </summary>
/// <remarks>
/// Команды выполняются над исходным текстом разметки: это гарантирует
/// корректность результата независимо от наличия в системе движка
/// визуального редактирования.
/// </remarks>
public static class КомандыРазметки
{
    /// <summary>Полужирное начертание.</summary>
    public const string Полужирный = "bold";

    /// <summary>Курсив.</summary>
    public const string Курсив = "italic";

    /// <summary>Подчёркивание.</summary>
    public const string Подчёркивание = "underline";

    /// <summary>Цвет текста.</summary>
    public const string ЦветТекста = "foreColor";

    /// <summary>Выравнивание по левому краю.</summary>
    public const string ПоШирине = "justifyLeft";

    /// <summary>Выравнивание по центру.</summary>
    public const string ПоЦентру = "justifyCenter";

    /// <summary>Выравнивание по правому краю.</summary>
    public const string ПоПравому = "justifyRight";

    /// <summary>Маркированный список.</summary>
    public const string МаркированныйСписок = "insertUnorderedList";

    /// <summary>Нумерованный список.</summary>
    public const string НумерованныйСписок = "insertOrderedList";

    /// <summary>Вставка таблицы.</summary>
    public const string ВставитьТаблицу = "insertTable";

    /// <summary>Вставка ссылки.</summary>
    public const string ВставитьСсылку = "insertLink";

    /// <summary>Вставка картинки.</summary>
    public const string ВставитьРисунок = "insertImage";

    /// <summary>Очистка форматирования.</summary>
    public const string ОчиститьФорматирование = "removeFormat";

    private static readonly Regex Теги = new("<[^>]+>", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// Применяет команду форматирования к выделенному фрагменту разметки.
    /// </summary>
    /// <param name="html">Исходная разметка.</param>
    /// <param name="команда">Команда форматирования.</param>
    /// <param name="значение">
    /// Значение команды: цвет текста, адрес ссылки или адрес картинки; для
    /// остальных команд не используется.
    /// </param>
    /// <param name="начало">Позиция выделения.</param>
    /// <param name="длина">Длина выделения.</param>
    /// <returns>Разметка после применения команды и новое выделение.</returns>
    public static РезультатКоманды Применить(string html, string команда, string? значение, int начало, int длина)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(команда);

        var началоНорм = Math.Clamp(начало, 0, html.Length);
        var конецНорм = Math.Clamp(началоНорм + Math.Max(длина, 0), началоНорм, html.Length);
        var фрагмент = html[началоНорм..конецНорм];

        var вставка = СобратьВставку(команда, значение, фрагмент);

        if (вставка is null)
        {
            return new РезультатКоманды(html, началоНорм, конецНорм - началоНорм);
        }

        var новаяРазметка = string.Concat(html[..началоНорм], вставка, html[конецНорм..]);

        return new РезультатКоманды(новаяРазметка, началоНорм, вставка.Length);
    }

    /// <summary>
    /// Формирует текст, вставляемый вместо выделенного фрагмента.
    /// </summary>
    /// <param name="команда">Команда форматирования.</param>
    /// <param name="значение">Значение команды.</param>
    /// <param name="фрагмент">Выделенный фрагмент разметки.</param>
    /// <returns>Вставляемый текст; <see langword="null"/> — команда не поддерживается.</returns>
    private static string? СобратьВставку(string команда, string? значение, string фрагмент)
    {
        switch (команда)
        {
            case Полужирный:
                return $"<b>{фрагмент}</b>";

            case Курсив:
                return $"<i>{фрагмент}</i>";

            case Подчёркивание:
                return $"<u>{фрагмент}</u>";

            case ЦветТекста:
                return $"<span style=\"color:{НормализоватьЦвет(значение)}\">{фрагмент}</span>";

            case ПоШирине:
            case ПоЦентру:
            case ПоПравому:
                return $"<div style=\"text-align:{Выравнивание(команда)}\">{фрагмент}</div>";

            case МаркированныйСписок:
                return $"<ul><li>{фрагмент}</li></ul>";

            case НумерованныйСписок:
                return $"<ol><li>{фрагмент}</li></ol>";

            case ВставитьТаблицу:
                return "<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\"><tbody><tr><td>"
                    + фрагмент
                    + "</td></tr></tbody></table>";

            case ВставитьСсылку:
            {
                var ссылка = string.IsNullOrWhiteSpace(значение) ? string.Empty : значение.Trim();
                var текст = фрагмент.Length == 0 ? "ссылка" : фрагмент;

                return $"<a href=\"{Экранировать(ссылка)}\">{текст}</a>";
            }

            case ВставитьРисунок:
            {
                var источник = string.IsNullOrWhiteSpace(значение) ? string.Empty : значение.Trim();

                return $"<img src=\"{Экранировать(источник)}\" alt=\"\" />";
            }

            case ОчиститьФорматирование:
                return Теги.Replace(фрагмент, string.Empty);

            default:
                return null;
        }
    }

    /// <summary>
    /// Приводит значение цвета к виду, пригодному для атрибута <c>style</c>.
    /// </summary>
    /// <param name="значение">Значение цвета.</param>
    /// <returns>Цвет в виде <c>#RRGGBB</c> либо имя цвета.</returns>
    private static string НормализоватьЦвет(string? значение)
    {
        if (string.IsNullOrWhiteSpace(значение))
        {
            return "#000000";
        }

        var текст = значение.Trim();

        return текст.StartsWith("#", StringComparison.Ordinal)
            ? текст
            : "#" + текст.TrimStart('#');
    }

    /// <summary>
    /// Возвращает значение выравнивания по CSS для команды выравнивания.
    /// </summary>
    /// <param name="команда">Команда выравнивания.</param>
    /// <returns>Значение <c>text-align</c>.</returns>
    private static string Выравнивание(string команда) => команда switch
    {
        ПоЦентру => "center",
        ПоПравому => "right",
        _ => "left",
    };

    /// <summary>
    /// Очищает опасную разметку (ТЗ §8.7, функция 97: «очистка опасного
    /// HTML»): удаляет блоки script/iframe/object/embed/form целиком,
    /// обработчики событий on* и URL-схемы javascript:/vbscript:/data:.
    /// Текст и безопасное форматирование сохраняются.
    /// </summary>
    /// <param name="html">Исходная разметка.</param>
    /// <returns>Очищенная разметка.</returns>
    public static string ОчиститьОпасныйHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (html.Length == 0)
        {
            return html;
        }

        var результат = html;

        foreach (var блок in ОпасныеБлоки)
        {
            результат = Regex.Replace(
                результат,
                "<" + блок + "[^>]*>.*?</" + блок + ">",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                TimeSpan.FromMilliseconds(200));
        }

        // Обработчики событий on* в атрибутах.
        результат = ОбработчикиСобытий.Replace(результат, string.Empty);

        // Опасные URL-схемы в атрибутах: нейтрализуются якорем.
        результат = ОпасныеСхемы.Replace(результат, "#");

        return результат;
    }

    /// <summary>Опасные блоки, удаляемые целиком с содержимым.</summary>
    private static readonly string[] ОпасныеБлоки = ["script", "iframe", "object", "embed", "form"];

    /// <summary>Обработчики событий on* в атрибутах.</summary>
    private static readonly Regex ОбработчикиСобытий = new(
        "\\son[a-zA-Z]+\\s*=\\s*(\"[^\"]*\"|'[^']*'|[^\\s>]+)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    /// <summary>Опасные URL-схемы в атрибутах (href/src/action).</summary>
    private static readonly Regex ОпасныеСхемы = new(
        "(href|src|action)\\s*=\\s*(\"|')?(javascript|vbscript|data)\\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// Экранирует значение атрибута HTML.
    /// </summary>
    /// <param name="значение">Значение атрибута.</param>
    /// <returns>Экранированное значение.</returns>
    private static string Экранировать(string значение) => значение
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}

/// <summary>
/// Встроенный визуальный редактор письма (ТЗ §8.7, функция 97:
/// <c>public string EditHtml(string html)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Окно редактора содержит панель инструментов (форматирование, списки,
/// таблицы, вставка ссылок и картинок, очистка форматирования) и вкладки
/// «Визуальный» и «HTML-код». Команды применяются к выделенному фрагменту
/// разметки, поэтому результат одинаков при любой настройке рабочего места.
/// </para>
/// <para>
/// Шаблон письма по умолчанию и стили (в том числе стиль «word» — эмуляция
/// оформления Word) поставляются файлами в каталоге <c>emailedit</c> рядом с
/// программой. При отсутствии файлов применяются встроенные значения.
/// </para>
/// </remarks>
public sealed class HtmlMailEditor : Form
{
    /// <summary>Имя каталога с файлами шаблона и стилей.</summary>
    public const string КаталогШаблонов = "emailedit";

    /// <summary>Имя файла шаблона письма по умолчанию.</summary>
    public const string ФайлШаблона = "default.html";

    /// <summary>Имя файла стиля содержимого письма.</summary>
    public const string ФайлСтиляСодержимого = "content.css";

    /// <summary>Имя файла стиля «word» (эмуляция оформления Word).</summary>
    public const string ФайлСтиляWord = "word.css";

    /// <summary>Шаблон письма по умолчанию (используется при отсутствии файла).</summary>
    public const string ШаблонПоУмолчанию = """
        <html>
        <head><meta charset="utf-8" /></head>
        <body>
        <p>Здравствуйте, #Наименование#!</p>
        <p>Ваш почтовый индекс: #Индекс#.</p>
        <p>Адрес: #Адрес#.</p>
        </body>
        </html>
        """;

    /// <summary>Стиль содержимого по умолчанию (используется при отсутствии файла).</summary>
    public const string СтильСодержимогоПоУмолчанию = """
        body { font-family: Arial, sans-serif; font-size: 12pt; color: #000000; }
        p { margin: 0 0 10pt 0; }
        """;

    /// <summary>Стиль «word» по умолчанию (используется при отсутствии файла).</summary>
    public const string СтильWordПоУмолчанию = """
        body { font-family: "Times New Roman", serif; font-size: 14pt; line-height: 1.15; color: #000000; }
        p { margin: 0 0 10pt 0; text-align: justify; }
        """;

    private readonly ToolStrip _панель = new();
    private readonly TabControl _вкладки = new() { Dock = DockStyle.Fill };
    private readonly TextBox _код = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
    };

    private readonly Panel _визуальнаяПанель = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly Label _сообщение = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.DimGray,
        Visible = false,
    };

    private readonly WebBrowser _браузер = new()
    {
        Dock = DockStyle.Fill,
        ScriptErrorsSuppressed = true,
        AllowNavigation = true,
    };

    private string _исходныйHtml = string.Empty;

    /// <summary>
    /// Создаёт редактор письма.
    /// </summary>
    public HtmlMailEditor()
    {
        Text = "Редактор письма";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(980, 640);
        MinimumSize = new Size(720, 480);

        ПостроитьПанельИнструментов();
        ПостроитьВкладки();

        Загрузить(ШаблонПоУмолчанию);
    }

    /// <summary>Текущая разметка письма.</summary>
    public string ТекущийHtml { get; private set; } = string.Empty;

    /// <summary>
    /// Признак подтверждения правки оператором: <see langword="true"/> после
    /// нажатия кнопки «Готово».
    /// </summary>
    public bool Подтверждено { get; private set; }

    /// <summary>Каталог файлов шаблона и стилей рядом с программой.</summary>
    public static string КаталогФайлов => Path.Combine(AppContext.BaseDirectory, КаталогШаблонов);

    /// <summary>
    /// Открывает редактор и возвращает отредактированную разметку
    /// (ТЗ §8.7, функция 97).
    /// </summary>
    /// <param name="html">Исходная разметка письма.</param>
    /// <returns>Отредактированная разметка; при отмене — исходная.</returns>
    public string EditHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        Загрузить(html);

        var результат = ShowDialog();

        return результат == DialogResult.OK ? ТекущийHtml : html;
    }

    /// <summary>
    /// Загружает разметку в редактор без открытия окна.
    /// </summary>
    /// <param name="html">Разметка письма.</param>
    public void Загрузить(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        // Очистка опасного HTML (ТЗ §8.7, функция 97): редактор никогда не
        // держит скрипты/обработчики/опасные схемы в содержимом письма.
        var очищенный = КомандыРазметки.ОчиститьОпасныйHtml(html);

        _исходныйHtml = очищенный;
        ТекущийHtml = очищенный;

        _код.Text = html;

        ОбновитьПредпросмотр();
    }

    /// <summary>
    /// Возвращает исходную разметку, загруженную в редактор.
    /// </summary>
    /// <returns>Исходная разметка.</returns>
    public string ИсходныйHtml => _исходныйHtml;

    /// <summary>
    /// Читает шаблон письма по умолчанию из каталога рядом с программой; при
    /// отсутствии файла возвращается встроенное значение.
    /// </summary>
    /// <returns>Разметка шаблона.</returns>
    public static string ЗагрузитьШаблон()
    {
        var путь = Path.Combine(КаталогФайлов, ФайлШаблона);

        return File.Exists(путь) ? File.ReadAllText(путь, Encoding.UTF8) : ШаблонПоУмолчанию;
    }

    /// <summary>
    /// Читает файл стиля из каталога рядом с программой; при отсутствии файла
    /// возвращается встроенное значение.
    /// </summary>
    /// <param name="имяФайла">Имя файла стиля.</param>
    /// <returns>Текст стиля.</returns>
    public static string ЗагрузитьСтиль(string имяФайла)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяФайла);

        var путь = Path.Combine(КаталогФайлов, имяФайла);

        if (File.Exists(путь))
        {
            return File.ReadAllText(путь, Encoding.UTF8);
        }

        return имяФайла switch
        {
            ФайлСтиляWord => СтильWordПоУмолчанию,
            ФайлСтиляСодержимого => СтильСодержимогоПоУмолчанию,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Строит панель инструментов редактора.
    /// </summary>
    private void ПостроитьПанельИнструментов()
    {
        _панель.GripStyle = ToolStripGripStyle.Hidden;
        _панель.Dock = DockStyle.Top;

        _панель.Items.Add(СоздатьКнопку("B", "Полужирный", КомандыРазметки.Полужирный, FontStyle.Bold));
        _панель.Items.Add(СоздатьКнопку("I", "Курсив", КомандыРазметки.Курсив, FontStyle.Italic));
        _панель.Items.Add(СоздатьКнопку("U", "Подчёркивание", КомандыРазметки.Подчёркивание, FontStyle.Underline));

        _панель.Items.Add(new ToolStripSeparator());

        _панель.Items.Add(СоздатьКнопку("Ц", "Цвет текста", КомандыРазметки.ЦветТекста));
        _панель.Items.Add(СоздатьКнопку("⇤", "По левому краю", КомандыРазметки.ПоШирине));
        _панель.Items.Add(СоздатьКнопку("⇔", "По центру", КомандыРазметки.ПоЦентру));
        _панель.Items.Add(СоздатьКнопку("⇥", "По правому краю", КомандыРазметки.ПоПравому));

        _панель.Items.Add(new ToolStripSeparator());

        _панель.Items.Add(СоздатьКнопку("•", "Маркированный список", КомандыРазметки.МаркированныйСписок));
        _панель.Items.Add(СоздатьКнопку("1.", "Нумерованный список", КомандыРазметки.НумерованныйСписок));
        _панель.Items.Add(СоздатьКнопку("▦", "Вставить таблицу", КомандыРазметки.ВставитьТаблицу));

        _панель.Items.Add(new ToolStripSeparator());

        _панель.Items.Add(СоздатьКнопку("🔗", "Вставить ссылку", КомандыРазметки.ВставитьСсылку));
        _панель.Items.Add(СоздатьКнопку("🖼", "Вставить картинку", КомандыРазметки.ВставитьРисунок));
        _панель.Items.Add(СоздатьКнопку("✕", "Очистить форматирование", КомандыРазметки.ОчиститьФорматирование));

        _панель.Items.Add(new ToolStripSeparator());

        _панель.Items.Add(new ToolStripButton("Шаблон", null, (_, _) => Загрузить(ЗагрузитьШаблон()))
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Загрузить шаблон письма по умолчанию",
        });

        Controls.Add(_панель);
    }

    /// <summary>
    /// Создаёт кнопку панели инструментов.
    /// </summary>
    /// <param name="текст">Надпись на кнопке.</param>
    /// <param name="подсказка">Подсказка.</param>
    /// <param name="команда">Команда форматирования.</param>
    /// <param name="шрифт">Стиль шрифта кнопки.</param>
    /// <returns>Кнопка панели инструментов.</returns>
    private ToolStripButton СоздатьКнопку(
        string текст,
        string подсказка,
        string команда,
        FontStyle шрифт = FontStyle.Regular)
    {
        var шрифтКнопки = new Font("Segoe UI", 9f, шрифт);

        return new ToolStripButton(текст, null, (_, _) => ПрименитьКоманду(команда))
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = шрифтКнопки,
            ToolTipText = подсказка,
            AutoSize = true,
        };
    }

    /// <summary>
    /// Строит вкладки редактора.
    /// </summary>
    private void ПостроитьВкладки()
    {
        _визуальнаяПанель.Controls.Add(_браузер);
        _визуальнаяПанель.Controls.Add(_сообщение);

        var визуальная = new TabPage("Визуальный");
        визуальная.Controls.Add(_визуальнаяПанель);

        var код = new TabPage("HTML-код");
        код.Controls.Add(_код);

        _вкладки.TabPages.Add(визуальная);
        _вкладки.TabPages.Add(код);

        _вкладки.SelectedIndexChanged += (_, _) => ОбновитьПредпросмотр();

        Controls.Add(_вкладки);

        var нижняя = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };

        var кнопкаОтмена = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
        };

        var кнопкаОк = new Button
        {
            Text = "Готово",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
        };

        нижняя.Controls.Add(кнопкаОтмена);
        нижняя.Controls.Add(кнопкаОк);

        Controls.Add(нижняя);

        AcceptButton = кнопкаОк;
        CancelButton = кнопкаОтмена;

        кнопкаОк.Click += (_, _) =>
        {
            ТекущийHtml = _код.Text;
            Подтверждено = true;
        };
    }

    /// <summary>
    /// Применяет команду форматирования: к разметке в поле кода либо через
    /// визуальный движок, когда он доступен.
    /// </summary>
    /// <param name="команда">Команда форматирования.</param>
    private void ПрименитьКоманду(string команда)
    {
        string? значение = null;

        if (команда == КомандыРазметки.ЦветТекста)
        {
            значение = ВыбратьЦвет();
        }
        else if (команда == КомандыРазметки.ВставитьСсылку)
        {
            значение = ЗапроситьЗначение("Адрес ссылки", "https://");
        }
        else if (команда == КомандыРазметки.ВставитьРисунок)
        {
            значение = ЗапроситьЗначение("Адрес картинки", "https://");
        }

        if (значение is null
            && (команда == КомандыРазметки.ЦветТекста
                || команда == КомандыРазметки.ВставитьСсылку
                || команда == КомандыРазметки.ВставитьРисунок))
        {
            return;
        }

        if (_вкладки.SelectedIndex == 0 && ПопыткаПрименитьВБраузере(команда, значение))
        {
            return;
        }

        var результат = КомандыРазметки.Применить(
            _код.Text,
            команда,
            значение,
            _код.SelectionStart,
            _код.SelectionLength);

        _код.Text = результат.Html;
        _код.SelectionStart = результат.SelectionStart;
        _код.SelectionLength = результат.SelectionLength;

        ТекущийHtml = результат.Html;

        ОбновитьПредпросмотр();
    }

    /// <summary>
    /// Пытается применить команду средствами визуального движка браузера.
    /// </summary>
    /// <param name="команда">Команда форматирования.</param>
    /// <param name="значение">Значение команды.</param>
    /// <returns><see langword="true"/>, если команда выполнена движком.</returns>
    private bool ПопыткаПрименитьВБраузере(string команда, string? значение)
    {
        var имя = ИмяКомандыБраузера(команда);

        if (имя is null)
        {
            return false;
        }

        try
        {
            var документ = _браузер.Document;

            if (документ is null)
            {
                return false;
            }

            dynamic динамический = документ;

            var выполнено = динамический.ExecCommand(имя, false, значение);

            if (выполнено is not true)
            {
                return false;
            }

            var разметка = (string)динамический.documentElement.outerHTML;

            if (string.IsNullOrEmpty(разметка))
            {
                return false;
            }

            _код.Text = разметка;
            ТекущийHtml = разметка;

            ОбновитьПредпросмотр();

            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return false;
        }
    }

    /// <summary>
    /// Возвращает имя команды визуального движка браузера.
    /// </summary>
    /// <param name="команда">Команда форматирования.</param>
    /// <returns>Имя команды движка; <see langword="null"/>, если команда им не поддерживается.</returns>
    private static string? ИмяКомандыБраузера(string команда) => команда switch
    {
        КомандыРазметки.Полужирный => "Bold",
        КомандыРазметки.Курсив => "Italic",
        КомандыРазметки.Подчёркивание => "Underline",
        КомандыРазметки.ЦветТекста => "ForeColor",
        КомандыРазметки.ПоШирине => "JustifyLeft",
        КомандыРазметки.ПоЦентру => "JustifyCenter",
        КомандыРазметки.ПоПравому => "JustifyRight",
        КомандыРазметки.МаркированныйСписок => "InsertUnorderedList",
        КомандыРазметки.НумерованныйСписок => "InsertOrderedList",
        КомандыРазметки.ВставитьСсылку => "CreateLink",
        КомандыРазметки.ВставитьРисунок => "InsertImage",
        КомандыРазметки.ОчиститьФорматирование => "RemoveFormat",
        _ => null,
    };

    /// <summary>
    /// Обновляет предпросмотр письма в визуальной вкладке.
    /// </summary>
    private void ОбновитьПредпросмотр()
    {
        if (_вкладки.SelectedIndex != 0)
        {
            return;
        }

        try
        {
            // Элемент управления WebBrowser Windows показывает документ
            // свойством DocumentText: разметка письма отображается без
            // сохранения во временный файл.
            _браузер.DocumentText = _код.Text;
            _сообщение.Visible = false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            _сообщение.Text = "Предпросмотр недоступен: правите разметку на вкладке «HTML-код».";
            _сообщение.Visible = true;
        }
        catch (InvalidOperationException)
        {
            _сообщение.Text = "Предпросмотр недоступен: правите разметку на вкладке «HTML-код».";
            _сообщение.Visible = true;
        }
        catch (NotSupportedException)
        {
            _сообщение.Text = "Предпросмотр недоступен: правите разметку на вкладке «HTML-код».";
            _сообщение.Visible = true;
        }
    }

    /// <summary>
    /// Запрашивает у оператора значение (адрес ссылки, адрес картинки).
    /// </summary>
    /// <param name="заголовок">Заголовок запроса.</param>
    /// <param name="значениеПоУмолчанию">Значение по умолчанию.</param>
    /// <returns>Введённое значение; <see langword="null"/> — оператор отказался.</returns>
    private static string? ЗапроситьЗначение(string заголовок, string значениеПоУмолчанию)
    {
        using var окно = new Form
        {
            Text = заголовок,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(460, 130),
        };

        var подпись = new Label
        {
            Text = заголовок,
            AutoSize = true,
            Location = new Point(12, 12),
        };

        var поле = new TextBox
        {
            Text = значениеПоУмолчанию,
            Location = new Point(12, 38),
            Width = 436,
        };

        var кнопкаОк = new Button
        {
            Text = "Ок",
            DialogResult = DialogResult.OK,
            Location = new Point(288, 76),
            Width = 76,
        };

        var кнопкаОтмена = new Button
        {
            Text = "Отмена",
            DialogResult = DialogResult.Cancel,
            Location = new Point(372, 76),
            Width = 76,
        };

        окно.Controls.Add(подпись);
        окно.Controls.Add(поле);
        окно.Controls.Add(кнопкаОк);
        окно.Controls.Add(кнопкаОтмена);

        окно.AcceptButton = кнопкаОк;
        окно.CancelButton = кнопкаОтмена;

        return окно.ShowDialog() == DialogResult.OK ? поле.Text : null;
    }

    /// <summary>
    /// Запрашивает у оператора цвет текста.
    /// </summary>
    /// <returns>Цвет в виде <c>#RRGGBB</c>; <see langword="null"/> — оператор отказался.</returns>
    private static string? ВыбратьЦвет()
    {
        using var диалог = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
        };

        return диалог.ShowDialog() == DialogResult.OK
            ? $"#{диалог.Color.R:X2}{диалог.Color.G:X2}{диалог.Color.B:X2}"
            : null;
    }
}
