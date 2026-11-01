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

        var position = address.IndexOf(zip, StringComparison.Ordinal);

        if (position < 0)
        {
            return address.Trim();
        }

        var result = string.Concat(address.AsSpan(0, position), address.AsSpan(position + zip.Length));

        return result
            .Trim()
            .TrimStart(',', ';', ':', '-', '.')
            .TrimEnd(',', ';', ':', '-', '.')
            .Trim();
    }
}
