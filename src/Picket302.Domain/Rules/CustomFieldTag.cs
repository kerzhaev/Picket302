using System.Text.RegularExpressions;

namespace Picket302.Domain.Rules;

/// <summary>
/// Правило тегов пользовательских полей контрагента
/// (ТЗ §8.1, функция 7: «Пользовательские поля контрагента»).
/// </summary>
/// <remarks>
/// Пользовательское поле контрагента публикуется в печатных макетах под тегом
/// вида <c>[Custom_КодКлиента]</c>. Тег формируется из имени поля: префикс
/// <c>Custom_</c> дописывается к имени без пробелов.
/// </remarks>
public static partial class CustomFieldTag
{
    /// <summary>Префикс тега пользовательского поля.</summary>
    public const string Prefix = "Custom_";

    /// <summary>Открывающая часть тега.</summary>
    public const string Open = "[";

    /// <summary>Закрывающая часть тега.</summary>
    public const string Close = "]";

    /// <summary>Разделитель слов внутри имени тега.</summary>
    public const char WordSeparator = '_';

    [GeneratedRegex(@"^\[Custom_[^\[\]]+\]$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    /// <summary>
    /// Формирует тег пользовательского поля по его имени.
    /// </summary>
    /// <param name="fieldName">Имя пользовательского поля.</param>
    /// <returns>Тег вида <c>[Custom_ИмяПоля]</c>.</returns>
    /// <exception cref="ArgumentException">
    /// Выбрасывается, если имя поля пустое либо содержит недопустимые символы.
    /// </exception>
    public static string Format(string fieldName)
    {
        if (!TryFormat(fieldName, out var тег))
        {
            throw new ArgumentException(
                $"Имя пользовательского поля «{fieldName}» недопустимо: допускаются буквы, цифры и пробелы.",
                nameof(fieldName));
        }

        return тег;
    }

    /// <summary>
    /// Формирует тег пользовательского поля по его имени; при ошибке возвращает
    /// <see langword="false"/> вместо исключения.
    /// </summary>
    /// <param name="fieldName">Имя пользовательского поля.</param>
    /// <param name="tag">Сформированный тег.</param>
    /// <returns><see langword="true"/>, если тег сформирован.</returns>
    public static bool TryFormat(string? fieldName, out string tag)
    {
        tag = string.Empty;

        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        var очищенное = fieldName.Trim();

        if (очищенное.Any(символ => символ is '[' or ']' || char.IsControl(символ)))
        {
            return false;
        }

        tag = Open + Prefix + очищенное.Replace(' ', WordSeparator) + Close;
        return true;
    }

    /// <summary>
    /// Проверяет, что строка является корректным тегом пользовательского поля.
    /// </summary>
    /// <param name="tag">Проверяемая строка.</param>
    /// <returns><see langword="true"/>, если строка — тег вида <c>[Custom_…]</c>.</returns>
    public static bool IsTag(string? tag)
        => !string.IsNullOrWhiteSpace(tag) && TagPattern().IsMatch(tag.Trim());

    /// <summary>
    /// Извлекает имя пользовательского поля из тега.
    /// </summary>
    /// <param name="tag">Тег вида <c>[Custom_ИмяПоля]</c>.</param>
    /// <returns>Имя поля либо <see langword="null"/>, если тег не распознан.</returns>
    public static string? ParseName(string? tag)
    {
        if (!IsTag(tag))
        {
            return null;
        }

        var обрезанный = tag!.Trim()[Open.Length..^Close.Length];
        return обрезанный[Prefix.Length..];
    }
}
