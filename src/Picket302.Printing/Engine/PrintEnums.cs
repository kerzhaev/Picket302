namespace Picket302.Printing.Engine;

/// <summary>
/// Фильтр чётных и нечётных страниц задания (ТЗ §8.6, функция 87).
/// </summary>
public enum PageParity
{
    /// <summary>Печать всех страниц.</summary>
    All = 0,

    /// <summary>Печать только нечётных страниц.</summary>
    OddOnly = 1,

    /// <summary>Печать только чётных страниц.</summary>
    EvenOnly = 2,
}

/// <summary>
/// Режим двусторонней печати (ТЗ §8.6, функция 86; §6.6).
/// </summary>
public enum DuplexMode
{
    /// <summary>Односторонняя печать.</summary>
    Simplex = 0,

    /// <summary>Двусторонняя печать через DEVMODE.dmDuplex.</summary>
    Duplex = 1,

    /// <summary>Двухпроходная печать с ручным переворотом стопки.</summary>
    ManualTwoPass = 2,
}
