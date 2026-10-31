namespace Picket302.Printing.Codes;

/// <summary>
/// Уровень коррекции ошибок QR-кода (ГОСТ Р 56042-2014, таблица коррекции).
/// </summary>
public enum QrErrorCorrectionLevel
{
    /// <summary>Уровень L — около 7 % восстановления.</summary>
    Low = 0,

    /// <summary>Уровень M — около 15 % восстановления.</summary>
    Medium = 1,

    /// <summary>Уровень Q — около 25 % восстановления.</summary>
    Quartile = 2,

    /// <summary>Уровень H — около 30 % восстановления.</summary>
    High = 3,
}
