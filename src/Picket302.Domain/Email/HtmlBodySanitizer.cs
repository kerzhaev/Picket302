using System.Text.RegularExpressions;

namespace Picket302.Domain.Email;

/// <summary>
/// Базовая санитизация HTML-разметки письма: удаление исполняемых вставок
/// (ТЗ §8.7, функция 96: «для HTML выполняется базовая санитизация разметки
/// (удаление скриптов) перед отправкой и предпросмотром»).
/// </summary>
/// <remarks>
/// <para>
/// Санитизация выполняется без внешних зависимостей: слой домена не ссылается
/// на сторонние сборки (ТЗ §2.1). Удаляются элементы <c>script</c>, <c>iframe</c>,
/// <c>object</c>, <c>embed</c>, <c>applet</c>, атрибуты обработчиков событий
/// (<c>on*</c>) и схемы <c>javascript:</c> и <c>vbscript:</c> в адресах.
/// </para>
/// <para>
/// Разметка письма не является документом Word: полное соответствие HTML
/// стандарту не требуется, требуется отсутствие исполняемого кода.
/// </para>
/// </remarks>
public static partial class HtmlBodySanitizer
{
    /// <summary>Элементы, содержимое которых удаляется целиком.</summary>
    private static readonly string[] ЗапрещённыеЭлементы =
    [
        "script",
        "iframe",
        "object",
        "embed",
        "applet",
        "frame",
        "frameset",
    ];

    /// <summary>
    /// Удаляет из HTML-разметки исполняемые вставки.
    /// </summary>
    /// <param name="html">Исходная разметка.</param>
    /// <returns>Очищенная разметка; при <see langword="null"/> — пустая строка.</returns>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var результат = html;

        foreach (var элемент in ЗапрещённыеЭлементы)
        {
            результат = УдалитьЭлемент(результат, элемент);
        }

        результат = ОбработчикСобытия().Replace(результат, string.Empty);
        результат = УбратьОпасныеСхемы(результат);
        результат = МетаПеренаправление().Replace(результат, string.Empty);

        return результат;
    }

    /// <summary>
    /// Очищает атрибуты с опасными схемами: значение проверяется после
    /// декодирования сущностей и удаления пробельных символов (F-D6).
    /// </summary>
    /// <param name="разметка">Разметка.</param>
    /// <returns>Разметка с очищенными опасными атрибутами.</returns>
    /// <remarks>
    /// Прежде обходились: значение без кавычек (<c>href=javascript:…</c>),
    /// HTML-сущности внутри имени схемы (<c>java&amp;#115;cript:</c>) и
    /// пробельные символы (<c>java\tscript:</c>). Решение о вырезании
    /// принимается по нормализованной копии значения, вырезается атрибут
    /// в оригинале по границам совпадения.
    /// </remarks>
    private static string УбратьОпасныеСхемы(string разметка) =>
        ОпаснаяСхема().Replace(
            разметка,
            совпадение =>
            {
                var значение = совпадение.Groups["значение"].Value;

                // Сущности декодируются рабочей копией значения; «&amp;»
                // декодируется в том же проходе — повторного декодирования
                // не возникает.
                var рабочее = System.Net.WebUtility.HtmlDecode(значение);

                // Браузеры игнорируют пробельные символы внутри имени схемы:
                // «java\tscript:» опасен как «javascript:».
                var нормализованное = new string(
                    рабочее.Where(символ => !char.IsWhiteSpace(символ)).ToArray());

                if (нормализованное.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                    || нормализованное.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
                    || нормализованное.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{совпадение.Groups["атрибут"].Value}=\"\"";
                }

                return совпадение.Value;
            });

    /// <summary>
    /// Проверяет разметку и сообщает о проблемных местах (ТЗ §8.7, функция 96:
    /// «Некорректный HTML — предупреждение с подсветкой места ошибки»).
    /// </summary>
    /// <param name="html">Проверяемая разметка.</param>
    /// <returns>Перечень предупреждений; пуст — замечаний нет.</returns>
    public static IReadOnlyList<string> Validate(string? html)
    {
        var предупреждения = new List<string>();

        if (string.IsNullOrWhiteSpace(html))
        {
            return предупреждения;
        }

        var позиция = 0;

        while (позиция < html.Length)
        {
            var начало = html.IndexOf('<', позиция);

            if (начало < 0)
            {
                break;
            }

            var конец = html.IndexOf('>', начало);

            if (конец < 0)
            {
                предупреждения.Add(
                    $"Незакрытый тег на позиции {начало + 1}: разметка обрывается без символа «>».");
                break;
            }

            var фрагмент = html[начало..(конец + 1)];
            var имя = ИмяТега(фрагмент);

            if (имя.Length == 0)
            {
                предупреждения.Add(
                    $"Некорректный тег на позиции {начало + 1}: «{Сократить(фрагмент)}».");
            }
            else if (ЗапрещённыеЭлементы.Contains(имя, StringComparer.OrdinalIgnoreCase))
            {
                предупреждения.Add(
                    $"Элемент «{имя}» на позиции {начало + 1} удаляется при отправке письма.");
            }

            позиция = конец + 1;
        }

        return предупреждения;
    }

    /// <summary>
    /// Удаляет элемент вместе с его содержимым.
    /// </summary>
    private static string УдалитьЭлемент(string разметка, string имяЭлемента)
    {
        var шаблон = $"<{имяЭлемента}\\b[^>]*>.*?</{имяЭлемента}\\s*>|<{имяЭлемента}\\b[^>]*/?>";

        return Regex.Replace(
            разметка,
            шаблон,
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Извлекает имя тега из фрагмента разметки.
    /// </summary>
    private static string ИмяТега(string фрагмент)
    {
        var тело = фрагмент.TrimStart('<').TrimEnd('>');
        var позиция = 0;

        while (позиция < тело.Length && (char.IsLetterOrDigit(тело[позиция]) || тело[позиция] == '_' || тело[позиция] == '-'))
        {
            позиция++;
        }

        return позиция == 0 ? string.Empty : тело[..позиция];
    }

    /// <summary>
    /// Сокращает фрагмент текста для сообщения об ошибке.
    /// </summary>
    private static string Сократить(string значение, int предел = 40)
        => значение.Length <= предел ? значение : значение[..предел] + "…";

    /// <summary>Шаблон обработчиков событий вида <c>onclick="…"</c> (перед
    /// именем допускается пробел или «/»: <c>&lt;img/src=x/onerror=…&gt;</c>,
    /// F-D6).</summary>
    [GeneratedRegex(@"[\s/]+on[a-z]+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ОбработчикСобытия();

    /// <summary>Шаблон атрибута-адреса: значение в двойных или одинарных
    /// кавычках либо без кавычек (F-D6).</summary>
    [GeneratedRegex(@"\b(?<атрибут>href|src|action)\s*=\s*(?:""(?<значение>[^""]*)""|'(?<значение>[^']*)'|(?<значение>[^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ОпаснаяСхема();

    /// <summary>Шаблон мета-тега перенаправления.</summary>
    [GeneratedRegex(@"<meta\b[^>]*http-equiv\s*=\s*[""']?refresh[""']?[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex МетаПеренаправление();
}
