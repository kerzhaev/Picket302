using System.Text;

namespace Picket302.Application.Import;

/// <summary>
/// Определение кодировки текстового файла: UTF-8 (с BOM и без) либо CP1251
/// (ТЗ §8.5, функция 71: «кодировки UTF-8 и CP1251»; ТЗ §10.2:
/// «импорт legacy с автоопределением cp1251/UTF-8/UTF-16»).
/// </summary>
/// <remarks>
/// Порядок проверки: маркер BOM UTF-8 выигрывает всегда; иначе содержимое
/// проверяется строгим разбором UTF-8; при негодности байтов делается
/// откат на CP1251. Откат считается неуверенным: однобайтовая кодировка
/// допускает любую последовательность байтов, поэтому уверенность в ней
/// отсутствует.
/// </remarks>
public static class TextEncodingDetector
{
    /// <summary>Имя кодировки UTF-8 в профилях импорта.</summary>
    public const string Utf8Name = "utf-8";

    /// <summary>Имя кодировки CP1251 в профилях импорта.</summary>
    public const string Cp1251Name = "windows-1251";

    /// <summary>Маркер BOM кодировки UTF-8.</summary>
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Строгий декодер UTF-8: недопустимые последовательности вызывают ошибку.</summary>
    private static readonly UTF8Encoding СтрогийUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Кодировка UTF-8 с маркером BOM — вариант выгрузки по умолчанию.</summary>
    public static Encoding Utf8WithBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Кодировка UTF-8 без маркера BOM.</summary>
    public static Encoding Utf8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Кодировка CP1251 (Windows-1251).</summary>
    public static Encoding Cp1251 { get; } = СоздатьCp1251();

    /// <summary>
    /// Определяет кодировку буфера.
    /// </summary>
    /// <param name="данные">Проверяемые байты файла.</param>
    /// <returns>Результат определения кодировки.</returns>
    public static EncodingDetectionResult Detect(byte[] данные)
    {
        ArgumentNullException.ThrowIfNull(данные);

        if (данные.Length >= Utf8Bom.Length
            && данные[0] == Utf8Bom[0]
            && данные[1] == Utf8Bom[1]
            && данные[2] == Utf8Bom[2])
        {
            return new EncodingDetectionResult(Utf8WithBom, true, Utf8Name);
        }

        if (ЯвляетсяUtf8(данные))
        {
            return new EncodingDetectionResult(Utf8, true, Utf8Name);
        }

        return new EncodingDetectionResult(Cp1251, false, Cp1251Name);
    }

    /// <summary>
    /// Определяет кодировку содержимого потока. Поток читается целиком;
    /// если он поддерживает позиционирование, позиция восстанавливается.
    /// </summary>
    /// <param name="поток">Поток с содержимым файла.</param>
    /// <returns>Результат определения кодировки.</returns>
    public static EncodingDetectionResult Detect(Stream поток)
    {
        ArgumentNullException.ThrowIfNull(поток);

        var позиция = поток.CanSeek ? поток.Position : -1;

        using var буфер = new MemoryStream();
        поток.CopyTo(буфер);

        if (позиция >= 0)
        {
            поток.Seek(позиция, SeekOrigin.Begin);
        }

        return Detect(буфер.ToArray());
    }

    /// <summary>
    /// Проверяет, является ли содержимое корректным UTF-8.
    /// </summary>
    /// <param name="данные">Проверяемые байты.</param>
    /// <returns><see langword="true"/>, если байты образуют корректный UTF-8.</returns>
    public static bool ЯвляетсяUtf8(byte[] данные)
    {
        ArgumentNullException.ThrowIfNull(данные);

        try
        {
            СтрогийUtf8.GetString(данные);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Возвращает кодировку по её имени из профиля импорта.
    /// </summary>
    /// <param name="имяКодировки">
    /// Имя кодировки (<c>utf-8</c>, <c>windows-1251</c> и т. п.). Пустое или
    /// неизвестное имя означает автоопределение.
    /// </param>
    /// <returns>
    /// Кодировка; <see langword="null"/>, если имя пустое либо неизвестно и
    /// требуется автоопределение.
    /// </returns>
    public static Encoding? ПоИмени(string? имяКодировки)
    {
        if (string.IsNullOrWhiteSpace(имяКодировки))
        {
            return null;
        }

        var имя = имяКодировки.Trim();

        if (имя.Equals(Utf8Name, StringComparison.OrdinalIgnoreCase)
            || имя.Equals("utf8", StringComparison.OrdinalIgnoreCase))
        {
            return Utf8;
        }

        if (имя.Equals(Cp1251Name, StringComparison.OrdinalIgnoreCase)
            || имя.Equals("cp1251", StringComparison.OrdinalIgnoreCase)
            || имя.Equals("windows-1251", StringComparison.OrdinalIgnoreCase))
        {
            return Cp1251;
        }

        try
        {
            return Encoding.GetEncoding(имя);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Создаёт кодировку CP1251. Используется поставщик кодовых страниц
    /// платформы; если он недоступен, применяется встроенная таблица
    /// сопоставления.
    /// </summary>
    /// <returns>Кодировка CP1251.</returns>
    private static Encoding СоздатьCp1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        return Encoding.GetEncoding(1251);
    }
}

/// <summary>
/// Результат определения кодировки текстового файла (ТЗ §8.5, функция 71).
/// </summary>
/// <param name="Encoding">Определённая кодировка.</param>
/// <param name="IsConfident">
/// Признак уверенного определения: BOM UTF-8 либо строгая проверка UTF-8.
/// Откат на CP1251 считается неуверенным.
/// </param>
/// <param name="EncodingName">Имя кодировки для профиля импорта.</param>
public sealed record EncodingDetectionResult(Encoding Encoding, bool IsConfident, string EncodingName);
