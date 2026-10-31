namespace Picket302.Domain.Common;

/// <summary>
/// Падеж русского языка (ТЗ §8.1, функция 6: «Дательный для «Кому:»,
/// Родительный для «От кого:»»).
/// </summary>
public enum GrammaticalCase
{
    /// <summary>Именительный падеж (Кто? Что?).</summary>
    Nominative = 0,

    /// <summary>Родительный падеж (Чей? Чья?).</summary>
    Genitive = 1,

    /// <summary>Дательный падеж (Кому? Чему?).</summary>
    Dative = 2,

    /// <summary>Винительный падеж (Кого? Что?).</summary>
    Accusative = 3,

    /// <summary>Творительный падеж (Кем? Чем?).</summary>
    Instrumental = 4,

    /// <summary>Предложный падеж (О ком? О чём?).</summary>
    Prepositional = 5,
}
