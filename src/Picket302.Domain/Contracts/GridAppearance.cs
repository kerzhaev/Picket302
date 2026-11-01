namespace Picket302.Domain.Contracts;

/// <summary>
/// Настраиваемое оформление таблиц (ТЗ §7.10: «Правила цветовой схемы и
/// плотности»; §8.11, функция 131: «Настройка вида таблиц»).
/// </summary>
/// <remarks>
/// Три независимых настраиваемых цвета: фокус (<see cref="FocusColor"/>),
/// выделение (<see cref="SelectedColor"/>) и подсказка (<see cref="HintColor"/>).
/// Значения по умолчанию — 4227327, 6205183 и 3723516 соответственно.
/// «Зебра» (чередование строк) выключается глобально; заголовки столбцов
/// отображаются (<see cref="HeadersVisible"/>).
/// </remarks>
public sealed record GridAppearance
{
    /// <summary>Цвет фокуса по умолчанию (ТЗ §7.10).</summary>
    public const int DefaultFocusColor = 4227327;

    /// <summary>Цвет выделения по умолчанию (ТЗ §7.10).</summary>
    public const int DefaultSelectedColor = 6205183;

    /// <summary>Цвет подсказки по умолчанию (ТЗ §7.10).</summary>
    public const int DefaultHintColor = 3723516;

    /// <summary>Кегль шрифта таблиц (ТЗ §7.10: «кегль таблиц 8pt»).</summary>
    public const float DefaultFontSize = 8f;

    /// <summary>Высота строки таблицы в пикселях (ТЗ §7.10: «высота строки ~17px»).</summary>
    public const int DefaultRowHeight = 17;

    /// <summary>Цвет фокуса (ARGB).</summary>
    public int FocusColor { get; init; } = DefaultFocusColor;

    /// <summary>Цвет выделения строки (ARGB).</summary>
    public int SelectedColor { get; init; } = DefaultSelectedColor;

    /// <summary>Цвет подсказки (ARGB).</summary>
    public int HintColor { get; init; } = DefaultHintColor;

    /// <summary>Признак включённой «зелбры» — чередования строк.</summary>
    public bool ZebraEnabled { get; init; }

    /// <summary>
    /// Признак отображения заголовков столбцов — переключатель заголовков
    /// (ТЗ §8.11, функция 131: «заголовки столбцов»).
    /// </summary>
    /// <remarks>
    /// В файле настроек, сохранённом до появления переключателя, поле
    /// отсутствует — тогда действует значение по умолчанию (заголовки видны).
    /// </remarks>
    public bool HeadersVisible { get; init; } = true;

    /// <summary>Кегль шрифта таблиц.</summary>
    public float FontSize { get; init; } = DefaultFontSize;

    /// <summary>Высота строки таблицы в пикселях.</summary>
    public int RowHeight { get; init; } = DefaultRowHeight;

    /// <summary>
    /// Значения по умолчанию: фокус 4227327, выделение 6205183, подсказка 3723516,
    /// «зебра» выключена, кегль 8pt, высота строки 17px.
    /// </summary>
    public static GridAppearance Default => new();
}
