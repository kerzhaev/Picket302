using System.Text;

namespace Picket302.Domain.Exchange;

/// <summary>
/// Санитайзер текстовых значений XML (ТЗ §9.4, функция 77: «входной недоверенный XML
/// проходит через санитайзер»).
/// </summary>
/// <remarks>
/// Предусмотрены два уровня обработки:
/// <list type="bullet">
/// <item><see cref="CleanXmlString"/> — приведение значения к виду, безопасному для
/// вставки в текст XML без дополнительного экранирования: символы, запрещённые
/// XML 1.0, удаляются, символы разметки заменяются ссылками на сущности;</item>
/// <item><see cref="RemoveInvalidCharacters"/> — фильтр уровня символов, который
/// оставляет разметку как есть; применяется к значениям, которые сам
/// <see cref="System.Xml.XmlWriter"/> экранирует при записи.</item>
/// </list>
/// </remarks>
public static class XmlTextSanitizer
{
    /// <summary>
    /// Приводит значение к виду, безопасному для вставки в текст или значение атрибута
    /// XML без дополнительного экранирования.
    /// </summary>
    /// <param name="value">Исходное значение; может быть <see langword="null"/>.</param>
    /// <returns>
    /// Очищенное значение. Управляющие символы, запрещённые в XML 1.0, удалены,
    /// а символы разметки <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c>, <c>'</c>
    /// заменены ссылками на сущности. Для пустого значения возвращается пустая строка.
    /// </returns>
    public static string CleanXmlString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var result = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var symbol = value[index];

            if (char.IsHighSurrogate(symbol)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                // Символы вне базовой плоскости (например, эмодзи) переносятся целиком.
                result.Append(symbol).Append(value[index + 1]);
                index++;
                continue;
            }

            if (!IsValidXmlChar(symbol))
            {
                continue;
            }

            switch (symbol)
            {
                case '&':
                    result.Append("&amp;");
                    break;
                case '<':
                    result.Append("&lt;");
                    break;
                case '>':
                    result.Append("&gt;");
                    break;
                case '"':
                    result.Append("&quot;");
                    break;
                case '\'':
                    result.Append("&apos;");
                    break;
                default:
                    result.Append(symbol);
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Удаляет символы, запрещённые в XML 1.0, не изменяя разметку и пробельные символы.
    /// </summary>
    /// <param name="value">Исходное значение; может быть <see langword="null"/>.</param>
    /// <returns>
    /// Значение без недопустимых символов. Применяется к значениям, которые
    /// <see cref="System.Xml.XmlWriter"/> экранирует самостоятельно, а также к тексту
    /// недоверенного XML-документа перед его разбором.
    /// </returns>
    public static string RemoveInvalidCharacters(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var result = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var symbol = value[index];

            if (char.IsHighSurrogate(symbol)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                result.Append(symbol).Append(value[index + 1]);
                index++;
                continue;
            }

            if (IsValidXmlChar(symbol))
            {
                result.Append(symbol);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Проверяет, что символ допустим в тексте XML 1.0.
    /// </summary>
    /// <param name="value">Проверяемый символ.</param>
    /// <returns><see langword="true"/>, если символ допустим в XML 1.0.</returns>
    public static bool IsValidXmlChar(char value)
        => value is '\t' or '\n' or '\r'
            || (value >= '\u0020' && value <= '\uD7FF')
            || (value >= '\uE000' && value <= '\uFFFD');
}
