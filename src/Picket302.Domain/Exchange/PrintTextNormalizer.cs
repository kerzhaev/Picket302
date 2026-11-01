using System.Xml;

using Picket302.Domain.Common;

namespace Picket302.Domain.Exchange;

/// <summary>
/// Приведение данных XML-контрактов к печатному виду (ТЗ §9.4, функция 77:
/// «вариант чтения ReadListFromXmlAndFixForPrint приводит данные к печатному виду
/// (усечение строк до 255 символов, нормализация индексов)»).
/// </summary>
public static class PrintTextNormalizer
{
    /// <summary>
    /// Предельная длина текстового поля (ТЗ §3.1.1: <c>VARCHAR(255)</c>).
    /// </summary>
    public const int MaxFieldLength = 255;

    /// <summary>
    /// Усекает значение до предельной длины, не разрывая пары суррогатов.
    /// </summary>
    /// <param name="value">Исходное значение; может быть <see langword="null"/>.</param>
    /// <param name="maxLength">Предельная длина значения.</param>
    /// <returns>Усечённое значение; для пустого входа — пустая строка.</returns>
    public static string Truncate(string? value, int maxLength = MaxFieldLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (maxLength <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;

        if (char.IsLowSurrogate(value[length])
            && length > 0
            && char.IsHighSurrogate(value[length - 1]))
        {
            // Разрез между суррогатной парой недопустим — отступаем на знак.
            length--;
        }

        return value[..length];
    }

    /// <summary>
    /// Проверяет, что значение длиннее предельной длины поля.
    /// </summary>
    /// <param name="value">Проверяемое значение.</param>
    /// <param name="maxLength">Предельная длина значения.</param>
    /// <returns><see langword="true"/>, если значение требует усечения.</returns>
    public static bool IsTooLong(string? value, int maxLength = MaxFieldLength)
        => value is not null && value.Length > maxLength;

    /// <summary>
    /// Нормализует почтовый индекс: убирает пробелы и знаки-разделители, оставляя
    /// шесть цифр.
    /// </summary>
    /// <param name="value">Исходное значение индекса.</param>
    /// <returns>
    /// Нормализованный индекс. Если значение не содержит шести цифр, возвращается
    /// обрезанное исходное значение без изменения его содержимого.
    /// </returns>
    public static string NormalizeZipCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());

        if (digits.Length >= ZipIndexExtractor.ZipLength)
        {
            return digits[..ZipIndexExtractor.ZipLength];
        }

        return value.Trim();
    }

    /// <summary>
    /// Извлекает почтовый индекс из текста адреса и убирает его из самого адреса.
    /// </summary>
    /// <param name="address">Текст адреса.</param>
    /// <param name="zip">Извлечённый индекс либо пустая строка.</param>
    /// <returns>Текст адреса без индекса.</returns>
    public static string ExtractZipFromAddress(string? address, out string zip)
    {
        zip = ZipIndexExtractor.ExtractZip(address);

        return ZipIndexExtractor.RemoveZip(address, zip);
    }

    /// <summary>
    /// Число адресных строк бланка (ТЗ §8.5, функция 77: «выравнивание 6
    /// адресных строк»).
    /// </summary>
    public const int AddressLineCount = 6;

    /// <summary>
    /// Раскладывает единый текст адреса по строкам адресного блока бланка.
    /// </summary>
    /// <remarks>
    /// Текст адреса разбивается по переводам строк и запятым; пустые части
    /// отбрасываются, лишние части не помещаются в блок. Незаполненные строки
    /// остаются пустыми, поэтому блок всегда содержит ровно шесть строк.
    /// </remarks>
    /// <param name="address">Текст адреса; может быть <see langword="null"/>.</param>
    /// <param name="lineCount">Число строк адресного блока.</param>
    /// <returns>Строки адресного блока.</returns>
    public static string[] SplitAddressLines(string? address, int lineCount = AddressLineCount)
    {
        var строки = new string[Math.Max(0, lineCount)];

        for (var индекс = 0; индекс < строки.Length; индекс++)
        {
            строки[индекс] = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            return строки;
        }

        var части = address
            .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(часть => часть.Trim())
            .Where(часть => часть.Length > 0)
            .ToArray();

        for (var индекс = 0; индекс < части.Length && индекс < строки.Length; индекс++)
        {
            строки[индекс] = Truncate(части[индекс]);
        }

        return строки;
    }

    /// <summary>
    /// Читает список элементов XML-контракта и приводит его к печатному виду.
    /// </summary>
    /// <typeparam name="T">Тип элемента списка.</typeparam>
    /// <param name="reader">Читатель XML.</param>
    /// <param name="rootElementName">Имя корневого элемента контейнера.</param>
    /// <param name="itemElementNames">Имена элементов списка.</param>
    /// <param name="readItem">Чтение одного элемента списка.</param>
    /// <param name="fixForPrint">Приведение одного элемента к печатному виду.</param>
    /// <returns>
    /// Успешный результат со списком печатных элементов либо текст ошибки разбора XML.
    /// </returns>
    public static Result<IReadOnlyList<T>> ReadListFromXmlAndFixForPrint<T>(
        XmlReader reader,
        string rootElementName,
        IReadOnlyList<string> itemElementNames,
        Func<XmlReader, T> readItem,
        Func<T, T> fixForPrint)
    {
        ArgumentNullException.ThrowIfNull(fixForPrint);

        var read = ExchangeXmlReader.ReadList(reader, rootElementName, itemElementNames, readItem);

        if (read.IsFailure)
        {
            return read;
        }

        var items = new List<T>(read.Value.Count);

        foreach (var item in read.Value)
        {
            items.Add(fixForPrint(item));
        }

        return Result<IReadOnlyList<T>>.Success(items);
    }
}
