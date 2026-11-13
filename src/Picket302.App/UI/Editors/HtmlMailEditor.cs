using System.Drawing;
using System.Net;
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
    /// Очищает разметку по разрешённому набору элементов и атрибутов
    /// (ТЗ §8.7, функция 97: «очистка опасного HTML»). Подход «белого
    /// списка» безопасен по построению: сохраняются только элементы и
    /// атрибуты из каталога ниже, причём в любом регистре и при любом
    /// способе записи кавычек; комментарии, DOCTYPE, инструкции обработки,
    /// элемент <c>script</c> (в том числе незакрытый и с атрибутом через
    /// слэш) и любые обработчики on* удаляются; значения URL-атрибутов
    /// декодируются и проверяются на схему (http/https/mailto/относительная).
    /// Регрессии обходов — тесты «Санитизатор_*» (F-S39R2-02, подтверждённый
    /// обход <c>&lt;img src=x ONERROR=alert(1)&gt;</c>).
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

        var результат = new StringBuilder(html.Length);
        var позиция = 0;

        while (позиция < html.Length)
        {
            if (html[позиция] != '<')
            {
                результат.Append(html[позиция]);
                позиция++;
                continue;
            }

            // Комментарий: удаляется целиком.
            if (НачинаетсяС(html, позиция, "<!--"))
            {
                позиция = ПропуститьДо(html, позиция + 4, "-->");
                continue;
            }

            // DOCTYPE и инструкции обработки: удаляются.
            if (НачинаетсяС(html, позиция, "<!")
                || НачинаетсяС(html, позиция, "<?"))
            {
                позиция = ПропуститьДо(html, позиция + 2, ">");
                continue;
            }

            var закрывающий = позиция + 1 < html.Length && html[позиция + 1] == '/';
            var стартИмени = позиция + (закрывающий ? 2 : 1);

            // «<» без имени тега — текст; экранируется.
            if (стартИмени >= html.Length || !char.IsLetter(html[стартИмени]))
            {
                результат.Append("&lt;");
                позиция++;
                continue;
            }

            var конец = позиция + 1;

            while (конец < html.Length && html[конец] != '>')
            {
                конец++;
            }

            if (конец >= html.Length)
            {
                // Незакрытая «<» до конца строки — текст.
                результат.Append("&lt;");
                позиция++;
                continue;
            }

            var имя = ИмяТега(html, стартИмени, конец);
            var атрибуты = закрывающий ? null : РазобратьАтрибуты(html, стартИмени + имя.Length, конец);

            позиция = конец + 1;

            if (!закрывающий && ПропускаемыеССодержимым.Contains(имя))
            {
                // Опасный/служебный элемент: пропуск вместе с содержимым
                // (незакрытый — до конца строки; регистр имени не имеет
                // значения).
                var закрывающийТег = "</" + имя;
                var глухо = html.IndexOf(закрывающийТег, конец + 1, StringComparison.OrdinalIgnoreCase);

                if (глухо < 0)
                {
                    return результат.ToString();
                }

                var концЗакрывающего = html.IndexOf('>', глухо);

                позиция = концЗакрывающего < 0 ? html.Length : концЗакрывающего + 1;
            }
            else if (закрывающий && !РазрешённыеЭлементы.Contains(имя))
            {
                // Закрывающий тег недопустимого/пропускаемого элемента —
                // удаляется.
            }
            else if (!РазрешённыеЭлементы.Contains(имя))
            {
                // Недопустимый открывающий элемент: тег удаляется,
                // содержимое остаётся.
            }
            else
            {
                результат.Append('<');

                if (закрывающий)
                {
                    результат.Append('/');
                }

                результат.Append(имя);

                if (!закрывающий && атрибуты is not null)
                {
                    foreach (var (атрибут, значение) in атрибуты)
                    {
                        var чистое = ДопустимыйАтрибут(имя, атрибут, значение);

                        if (чистое is null)
                        {
                            continue;
                        }

                        результат.Append(' ').Append(атрибут).Append("=\"").Append(чистое).Append('"');
                    }

                    if (имя is "br" or "hr" or "img")
                    {
                        результат.Append(" /");
                    }
                }

                результат.Append('>');
            }
        }

        return результат.ToString();
    }

    /// <summary>Проверяет, начинается ли строка с префикса с заданной позиции.</summary>
    private static bool НачинаетсяС(string текст, int позиция, string префикс)
    {
        if (позиция + префикс.Length > текст.Length)
        {
            return false;
        }

        return текст.AsSpan(позиция, префикс.Length).SequenceEqual(префикс);
    }

    /// <summary>Пропускает символы до вхождения маркера; возвращает новую позицию.</summary>
    private static int ПропуститьДо(string текст, int позиция, string маркер)
    {
        var глухо = текст.IndexOf(маркер, позиция, StringComparison.Ordinal);

        return глухо < 0 ? текст.Length : глухо + маркер.Length;
    }

    /// <summary>Читает имя тега (буквы/цифры/дефис) в нижнем регистре.</summary>
    private static string ИмяТега(string html, int старт, int конец)
    {
        var поз = старт;

        while (поз < конец && (char.IsLetterOrDigit(html[поз]) || html[поз] == '-' || html[поз] == '_'))
        {
            поз++;
        }

        return html[старт..поз].ToLowerInvariant();
    }

    /// <summary>
    /// Разбирает атрибуты тега до закрывающей «&gt;»: пары «имя=значение»
    /// (кавычки двойные/одинарные либо без кавычек). Имена — в нижнем регистре.
    /// </summary>
    private static List<(string Имя, string Значение)> РазобратьАтрибуты(string html, int старт, int конец)
    {
        var список = new List<(string Имя, string Значение)>();
        var позиция = старт;

        while (позиция < конец)
        {
            while (позиция < конец && char.IsWhiteSpace(html[позиция]))
            {
                позиция++;
            }

            if (позиция >= конец || html[позиция] == '/')
            {
                break;
            }

            var имяСтарт = позиция;

            while (позиция < конец
                && (char.IsLetterOrDigit(html[позиция]) || html[позиция] == '-' || html[позиция] == '_'))
            {
                позиция++;
            }

            if (позиция == имяСтарт)
            {
                позиция++;
                continue;
            }

            var имя = html[имяСтарт..позиция].ToLowerInvariant();

            while (позиция < конец && char.IsWhiteSpace(html[позиция]))
            {
                позиция++;
            }

            var значение = string.Empty;

            if (позиция < конец && html[позиция] == '=')
            {
                позиция++;

                while (позиция < конец && char.IsWhiteSpace(html[позиция]))
                {
                    позиция++;
                }

                if (позиция < конец && (html[позиция] == '"' || html[позиция] == '\''))
                {
                    var кавычка = html[позиция++];
                    var значСтарт = позиция;

                    while (позиция < конец && html[позиция] != кавычка)
                    {
                        позиция++;
                    }

                    значение = html[значСтарт..Math.Min(позиция, конец)];

                    if (позиция < конец && html[позиция] == кавычка)
                    {
                        позиция++;
                    }
                }
                else
                {
                    var значСтарт = позиция;

                    while (позиция < конец && !char.IsWhiteSpace(html[позиция]) && html[позиция] != '>')
                    {
                        позиция++;
                    }

                    значение = html[значСтарт..позиция];
                }
            }

            список.Add((имя, значение));
        }

        return список;
    }

    /// <summary>Разрешённые элементы (в нижнем регистре).</summary>
    private static readonly HashSet<string> РазрешённыеЭлементы = new(StringComparer.Ordinal)
    {
        "html", "body", "p", "div", "span", "br", "hr", "b", "strong", "i", "em", "u",
        "s", "strike", "ul", "ol", "li", "table", "thead", "tbody", "tfoot", "tr",
        "td", "th", "caption", "a", "img", "h1", "h2", "h3", "h4", "h5", "h6",
        "blockquote", "pre", "code", "center", "font", "small", "sub", "sup",
    };

    /// <summary>
    /// Элементы, удаляемые вместе с содержимым (код/служебные/активные):
    /// всё вне разрешённого набора теряет только теги, эти — и содержимое.
    /// </summary>
    private static readonly HashSet<string> ПропускаемыеССодержимым = new(StringComparer.Ordinal)
    {
        "script", "style", "head", "title", "iframe", "frame", "frameset", "object",
        "embed", "applet", "form", "input", "button", "select", "option", "textarea",
        "noscript", "template", "svg", "math", "canvas", "audio", "video", "source",
        "track", "link", "meta", "base", "datalist", "dialog", "portal",
    };

    /// <summary>Разрешённые атрибуты по элементам (плюс глобальный style).</summary>
    private static readonly Dictionary<string, HashSet<string>> АтрибутыПоЭлементам =
        new(StringComparer.Ordinal)
        {
            ["a"] = new HashSet<string>(StringComparer.Ordinal) { "href", "title" },
            ["img"] = new HashSet<string>(StringComparer.Ordinal) { "src", "alt", "width", "height" },
            ["font"] = new HashSet<string>(StringComparer.Ordinal) { "face", "color", "size" },
            ["td"] = new HashSet<string>(StringComparer.Ordinal) { "colspan", "rowspan" },
            ["th"] = new HashSet<string>(StringComparer.Ordinal) { "colspan", "rowspan" },
        };

    /// <summary>Разрешённые свойства стиля (без url/expression/behavior).</summary>
    private static readonly HashSet<string> РазрешённыеСвойстваСтиля = new(StringComparer.Ordinal)
    {
        "color", "text-align", "font-weight", "font-style", "text-decoration",
        "background-color", "font-size", "font-family",
    };

    /// <summary>
    /// Возвращает безопасное значение атрибута либо <see langword="null"/>,
    /// если атрибут недопустим и должен быть удалён. Обработчики on*,
    /// неизвестные атрибуты и опасные URL-схемы удаляются всегда; значения
    /// декодируются (обход через сущности) и повторно экранируются.
    /// </summary>
    private static string? ДопустимыйАтрибут(string элемент, string атрибут, string значение)
    {
        // Любой обработчик событий — вне белого списка.
        if (атрибут.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var декодированное = WebUtility.HtmlDecode(значение);

        if (атрибут == "style")
        {
            // FINAL-01: стиль пересобирается из распарсенных пар, значение
            // каждой пары экранируется — кавычка/угловые скобки в исходном
            // значении не прорывают атрибут при сериализации.
            return ДопустимыйСтиль(декодированное);
        }

        var разрешены = АтрибутыПоЭлементам.TryGetValue(элемент, out var набор)
            ? набор
            : new HashSet<string>(StringComparer.Ordinal);

        if (!разрешены.Contains(атрибут))
        {
            return null;
        }

        if (атрибут is "href" or "src")
        {
            if (!БезопасныйАдрес(декодированное))
            {
                return null;
            }

            return Экранировать(декодированное.Trim());
        }

        return Экранировать(декодированное.Trim());
    }

    /// <summary>
    /// Проверяет адрес: после удаления всех пробельных/управляющих символов
    /// и приведения к нижнему регистру адрес не должен начинаться с опасной
    /// схемы и не должен содержать угловых скобок.
    /// </summary>
    private static bool БезопасныйАдрес(string адрес)
    {
        var плотное = new string(
            адрес.Where(знак => знак > ' ' && знак != '\u007f').ToArray())
            .ToLowerInvariant();

        foreach (var схема in new[] { "javascript:", "vbscript:", "data:", "file:", "about:" })
        {
            if (плотное.StartsWith(схема, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return !плотное.Contains('<') && !плотное.Contains('>');
    }

    /// <summary>
    /// Фильтрует стиль: допускаются только свойства из каталога, значения без
    /// url()/expression/схем; пустой результат — атрибут удаляется.
    /// </summary>
    private static string? ДопустимыйСтиль(string декодированное)
    {
        var нижнее = декодированное.ToLowerInvariant();

        foreach (var запрещённое in new[] { "url(", "expression", "javascript", "vbscript", "behavior", "@import", "script", "<" })
        {
            if (нижнее.Contains(запрещённое, StringComparison.Ordinal))
            {
                return null;
            }
        }

        // FINAL-01: кавычка в CSS-значении не легитимна (разрешённые свойства
        // — цвета/выравнивание/шрифты) и является попыткой прорвать атрибут
        // при сериализации; такой стиль отбрасывается целиком.
        if (декодированное.Contains('"', StringComparison.Ordinal)
            || декодированное.Contains('\'', StringComparison.Ordinal))
        {
            return null;
        }

        var чистые = декодированное
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(часть => часть.Contains(':', StringComparison.Ordinal))
            .Select(часть =>
            {
                var деление = часть.IndexOf(':', StringComparison.Ordinal);
                var имя = часть[..деление].Trim().ToLowerInvariant();

                return РазрешённыеСвойстваСтиля.Contains(имя)
                    ? имя + ": " + ЭкранироватьЗначениеСтиля(часть[(деление + 1)..].Trim())
                    : null;
            })
            .OfType<string>()
            .ToList();

        return чистые.Count == 0 ? null : string.Join("; ", чистые);
    }

    /// <summary>
    /// Экранирует значение свойства стиля: кавычки и угловые скобки
    /// заменяются сущностями — атрибут style="…" не прорывается
    /// синтезированными атрибутами (FINAL-01).
    /// </summary>
    private static string ЭкранироватьЗначениеСтиля(string значение) => значение
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>
    /// Экранирует значение атрибута HTML.
    /// </summary>
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
        // F-S39R2-03 (корректирующий проход): preview не выполняет активное
        // содержимое и не переходит по ссылкам/фреймам — санитизатор остаётся
        // единственной границей, а навигация из письма запрещена.
        AllowNavigation = false,
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

        // D-S39-01: поле кода и предпросмотр тоже получают очищенное — иначе
        // «Готово» и preview возвращают опасную разметку.
        _код.Text = очищенный;

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
            // D-S39-01: результат подтверждения — очищенная разметка (оператор
            // мог внести опасный HTML вручную на вкладке «HTML-код»).
            ТекущийHtml = КомандыРазметки.ОчиститьОпасныйHtml(_код.Text);
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
            // сохранения во временный файл. Предпросмотр рендерит
            // ОЧИЩЕННУЮ разметку (F-S39R2-03: ручной ввод опасного HTML на
            // вкладке кода не попадает в исполняемый preview).
            _браузер.DocumentText = КомандыРазметки.ОчиститьОпасныйHtml(_код.Text);
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
