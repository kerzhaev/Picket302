using System.Text.RegularExpressions;
using Picket302.Domain.Contracts;

namespace Picket302.Application.Services;

/// <summary>
/// Разбор почтового индекса из неструктурированной строки адреса
/// (ТЗ §8.1, функция 5: «Разбор индекса из строки адреса
/// (<c>parseZipIndexFromAddress</c>)»).
/// </summary>
/// <remarks>
/// Сценарий интеграции с 1С: индекс приходит одной строкой вместе с адресом.
/// Служба вырезает индекс регулярным выражением <c>\b\d{6}\b</c>, а остаток
/// разбивает по запятым на строки адреса 1..6.
/// </remarks>
public sealed partial class AddressParserService
{
    /// <summary>
    /// Имя параметра интеграции «Выкупывать почтовый индекс прямо из строки
    /// адреса» (ТЗ §8.1, функция 5).
    /// </summary>
    public const string ParseZipIndexFromAddressOption = "parseZipIndexFromAddress";

    /// <summary>Разделитель строк исходного адреса.</summary>
    public const char PartSeparator = ',';

    [GeneratedRegex(@"\b\d{6}\b", RegexOptions.CultureInvariant)]
    private static partial Regex ZipPattern();

    /// <summary>
    /// Разбирает неструктурированную строку адреса.
    /// </summary>
    /// <param name="rawAddress">Исходная строка адреса.</param>
    /// <param name="extractZip">
    /// Признак извлечения индекса из строки (флаг <c>parseZipIndexFromAddress</c>).
    /// При <see langword="false"/> индекс не вырезается и остаётся в тексте адреса.
    /// </param>
    /// <returns>
    /// Разобранный адрес: индекс, строки 1..6 и признак необходимости проверки.
    /// </returns>
    public ParsedAddress ParseRawString(string rawAddress, bool extractZip)
    {
        var исходный = rawAddress ?? string.Empty;
        var индекс = string.Empty;
        var остаток = исходный;

        if (extractZip)
        {
            var совпадение = ZipPattern().Match(исходный);

            if (совпадение.Success)
            {
                индекс = совпадение.Value;
                остаток = ZipPattern().Replace(исходный, string.Empty);
            }
        }

        var строки = Разделить(остаток);

        return new ParsedAddress(
            индекс,
            строки,
            индекс.Length == 0);
    }

    /// <summary>
    /// Разбирает строку адреса, не извлекая из неё индекс
    /// (значение флага <c>parseZipIndexFromAddress</c> — «выключен»).
    /// </summary>
    /// <param name="rawAddress">Исходная строка адреса.</param>
    /// <returns>Разобранный адрес.</returns>
    public ParsedAddress ParseRawString(string rawAddress)
        => ParseRawString(rawAddress, extractZip: false);

    /// <summary>
    /// Разбивает остаток исходного адреса на строки 1..6.
    /// </summary>
    /// <param name="text">Остаток исходной строки адреса.</param>
    /// <returns>Список строк адреса; лишние строки отбрасываются.</returns>
    private static IReadOnlyList<string> Разделить(string text)
    {
        var части = text.Split(PartSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var строки = new List<string>(ParsedAddress.MaxLines);

        foreach (var часть in части)
        {
            if (строки.Count >= ParsedAddress.MaxLines)
            {
                break;
            }

            var обрезанная = часть.Length > ParsedAddress.MaxLineLength
                ? часть[..ParsedAddress.MaxLineLength]
                : часть;

            строки.Add(обрезанная);
        }

        while (строки.Count < ParsedAddress.MaxLines)
        {
            строки.Add(string.Empty);
        }

        return строки;
    }
}
