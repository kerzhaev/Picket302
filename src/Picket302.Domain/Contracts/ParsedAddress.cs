namespace Picket302.Domain.Contracts;

/// <summary>
/// Результат разбора неструктурированной строки адреса
/// (ТЗ §8.1, функция 5: «Разбор индекса из строки адреса»).
/// </summary>
/// <param name="Zip">
/// Почтовый индекс, выделенный из строки; пустая строка, если индекс не найден.
/// </param>
/// <param name="Lines">
/// Строки адреса 1..6, полученные разбором остатка текста.
/// </param>
/// <param name="NeedsVerification">
/// Признак необходимости проверки адреса оператором: выставляется, когда почтовый
/// индекс не найден в исходной строке.
/// </param>
public sealed record ParsedAddress(
    string Zip,
    IReadOnlyList<string> Lines,
    bool NeedsVerification)
{
    /// <summary>Максимальное число строк в адресе (ТЗ §8.1, функция 4).</summary>
    public const int MaxLines = 6;

    /// <summary>Максимальная длина одной строки адреса.</summary>
    public const int MaxLineLength = 255;
}
