using System.Text;

namespace Picket302.Domain.Exchange;

/// <summary>
/// Извлечение почтового индекса из текста адреса (ТЗ §9.4, функция 77:
/// «ExtractZip извлекает почтовый индекс из строки адреса при установленном флаге»).
/// </summary>
public static class ZipIndexExtractor
{
    /// <summary>Количество знаков почтового индекса (ТЗ §3.1.1: <c>VARCHAR(6)</c>).</summary>
    public const int ZipLength = 6;

    /// <summary>
    /// Извлекает почтовый индекс из текста адреса.
    /// </summary>
    /// <param name="address">
    /// Текст адреса, например <c>«123456, г. Москва»</c>.
    /// </param>
    /// <returns>
    /// Первый найденный блок ровно из шести цифр (например, <c>«123456»</c>).
    /// Если индекс в тексте отсутствует, возвращается пустая строка.
    /// </returns>
    /// <remarks>
    /// Блок должен состоять ровно из шести цифр: почтовый индекс — отдельное
    /// число. Из более длинного блока (например, из телефонного номера) индекс
    /// не извлекается, иначе адрес получателя искажался бы.
    /// </remarks>
    public static string ExtractZip(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return string.Empty;
        }

        var index = 0;

        while (index < address.Length)
        {
            if (!char.IsAsciiDigit(address[index]))
            {
                index++;
                continue;
            }

            var start = index;

            while (index < address.Length && char.IsAsciiDigit(address[index]))
            {
                index++;
            }

            if (index - start == ZipLength)
            {
                return address.Substring(start, ZipLength);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Извлекает почтовый индекс из текста адреса и сообщает, найден ли он.
    /// </summary>
    /// <param name="address">Текст адреса.</param>
    /// <param name="zip">Извлечённый индекс либо пустая строка.</param>
    /// <returns><see langword="true"/>, если индекс найден.</returns>
    public static bool TryExtractZip(string? address, out string zip)
    {
        zip = ExtractZip(address);
        return zip.Length > 0;
    }

    /// <summary>
    /// Убирает почтовый индекс из текста адреса («выкусывание» индекса из адреса).
    /// </summary>
    /// <param name="address">Текст адреса.</param>
    /// <param name="zip">
    /// Индекс, который нужно убрать. Если индекс пуст, текст возвращается без изменений.
    /// </param>
    /// <returns>
    /// Текст адреса без индекса и без оставшихся от него разделителей.
    /// </returns>
    public static string RemoveZip(string? address, string? zip)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(zip))
        {
            return address.Trim();
        }

        var position = НайтиГраницуБлока(address, zip);

        if (position < 0)
        {
            return address.Trim();
        }

        var result = string.Concat(address.AsSpan(0, position), address.AsSpan(position + zip.Length));

        // На месте вырезанного индекса остаются двойные разделители
        // («тел. 8916…, , г. Москва») — схлопываются (F-D7).
        result = СхлопнутьРазделители(result);

        return result
            .Trim()
            .TrimStart(',', ';', ':', '-', '.')
            .TrimEnd(',', ';', ':', '-', '.')
            .Trim();
    }

    /// <summary>
    /// Схлопывает повторяющиеся разделители «,» и «;», оставшиеся на месте
    /// вырезанного индекса.
    /// </summary>
    /// <param name="текст">Текст после выкусывания.</param>
    /// <returns>Текст без повторных разделителей.</returns>
    private static string СхлопнутьРазделители(string текст)
    {
        var builder = new StringBuilder(текст.Length);
        var предыдущийЗначащий = '\0';
        var предыдущийПробел = false;

        foreach (var символ in текст)
        {
            if ((символ == ',' || символ == ';')
                && (предыдущийЗначащий == ',' || предыдущийЗначащий == ';'))
            {
                continue;
            }

            // Двойные пробелы на месте вырезанного блока схлопываются.
            if (символ == ' ' && предыдущийПробел)
            {
                continue;
            }

            builder.Append(символ);

            if (!char.IsWhiteSpace(символ))
            {
                предыдущийЗначащий = символ;
            }

            предыдущийПробел = символ == ' ';
        }

        return builder.ToString();
    }

    /// <summary>
    /// Ищет вхождение индекса, ограниченное нецифровыми соседями (F-D7):
    /// прежде выкусывалось первое вхождение подстроки, и телефон
    /// «89161234567» при индексе «123456» терял часть цифр.
    /// </summary>
    /// <param name="address">Текст адреса.</param>
    /// <param name="zip">Индекс.</param>
    /// <returns>Позиция вхождения либо -1.</returns>
    private static int НайтиГраницуБлока(string address, string zip)
    {
        var start = 0;

        while (true)
        {
            var position = address.IndexOf(zip, start, StringComparison.Ordinal);

            if (position < 0)
            {
                return -1;
            }

            var left = position == 0 || !char.IsDigit(address[position - 1]);
            var right = position + zip.Length >= address.Length
                || !char.IsDigit(address[position + zip.Length]);

            if (left && right)
            {
                return position;
            }

            start = position + 1;
        }
    }
}
