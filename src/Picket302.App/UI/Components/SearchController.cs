namespace Picket302.App.UI.Components;

/// <summary>
/// Результат сброса условий поиска по клавише Esc (ТЗ §7.7).
/// </summary>
public enum СбросПоиска
{
    /// <summary>Сбрасывать нечего — все условия сняты.</summary>
    НичегоНеСброшено = 0,

    /// <summary>Сброшена строка прямого набора с клавиатуры.</summary>
    СброшенПрямойНабор = 1,

    /// <summary>Сброшена строка формы поиска (Ctrl+F).</summary>
    СброшенаФормаПоиска = 2,

    /// <summary>Сброшен встроенный фильтр «Избранное».</summary>
    СброшенФильтрИзбранного = 3,

    /// <summary>Сброшен пользовательский фильтр по группам.</summary>
    СброшенФильтрГрупп = 4,
}

/// <summary>
/// Состояние трёх механизмов поиска главного окна
/// (ТЗ §7.7: «Три механизма поиска (все обязательны)»).
/// </summary>
/// <remarks>
/// <para>
/// Механизмы независимы и не сливаются:
/// </para>
/// <list type="number">
/// <item>форма поиска (Ctrl+F) — последовательный перебор, кнопка «Найти далее»;</item>
/// <item>прямой набор с клавиатуры — по мере набора отображаются только совпавшие;</item>
/// <item>фильтры — встроенный «Избранное» (VIP) и пользовательские группы.</item>
/// </list>
/// <para>
/// Сброс выполняется клавишей Esc; поскольку условий поиска может быть несколько,
/// сброс последовательный (ТЗ §7.7).
/// </para>
/// </remarks>
public sealed class SearchSession
{
    /// <summary>Строка прямого набора с клавиатуры.</summary>
    public string ПрямойНабор { get; private set; } = string.Empty;

    /// <summary>Строка формы поиска (Ctrl+F).</summary>
    public string ФормаПоиска { get; private set; } = string.Empty;

    /// <summary>Признак включённого встроенного фильтра «Избранное» (VIP).</summary>
    public bool ТолькоИзбранное { get; private set; }

    /// <summary>Идентификатор группы пользовательского фильтра.</summary>
    public long? Группа { get; private set; }

    /// <summary>
    /// Признак наличия хотя бы одного действующего условия поиска.
    /// </summary>
    public bool ЕстьУсловия => ПрямойНабор.Length > 0
        || ФормаПоиска.Length > 0
        || ТолькоИзбранное
        || Группа is not null;

    /// <summary>
    /// Добавляет символ к строке прямого набора с клавиатуры.
    /// </summary>
    /// <param name="символ">Набранный символ.</param>
    public void ДобавитьВПрямойНабор(char символ)
    {
        if (char.IsControl(символ))
        {
            return;
        }

        ПрямойНабор += символ;
    }

    /// <summary>
    /// Удаляет последний символ строки прямого набора.
    /// </summary>
    public void УдалитьИзПрямогоНабора()
    {
        if (ПрямойНабор.Length > 0)
        {
            ПрямойНабор = ПрямойНабор[..^1];
        }
    }

    /// <summary>
    /// Задаёт строку формы поиска (Ctrl+F).
    /// </summary>
    /// <param name="значение">Искомая строка.</param>
    public void ЗадатьФормуПоиска(string? значение) => ФормаПоиска = значение ?? string.Empty;

    /// <summary>
    /// Включает или выключает встроенный фильтр «Избранное» (VIP).
    /// </summary>
    /// <param name="включён">Признак включения фильтра.</param>
    public void ЗадатьФильтрИзбранного(bool включён) => ТолькоИзбранное = включён;

    /// <summary>
    /// Задаёт пользовательский фильтр по группам.
    /// </summary>
    /// <param name="группа">Идентификатор группы; <see langword="null"/> — фильтр снят.</param>
    public void ЗадатьФильтрГрупп(long? группа) => Группа = группа;

    /// <summary>
    /// Сбрасывает условия поиска по клавише Esc. Условия снимаются по одному:
    /// сначала строка прямого набора, затем строка формы поиска, затем фильтр
    /// «Избранное», затем фильтр по группам.
    /// </summary>
    /// <returns>Что именно сброшено.</returns>
    public СбросПоиска СброситьПоEsc()
    {
        if (ПрямойНабор.Length > 0)
        {
            ПрямойНабор = string.Empty;
            return СбросПоиска.СброшенПрямойНабор;
        }

        if (ФормаПоиска.Length > 0)
        {
            ФормаПоиска = string.Empty;
            return СбросПоиска.СброшенаФормаПоиска;
        }

        if (ТолькоИзбранное)
        {
            ТолькоИзбранное = false;
            return СбросПоиска.СброшенФильтрИзбранного;
        }

        if (Группа is not null)
        {
            Группа = null;
            return СбросПоиска.СброшенФильтрГрупп;
        }

        return СбросПоиска.НичегоНеСброшено;
    }

    /// <summary>
    /// Снимает все условия поиска разом.
    /// </summary>
    public void СброситьВсё()
    {
        ПрямойНабор = string.Empty;
        ФормаПоиска = string.Empty;
        ТолькоИзбранное = false;
        Группа = null;
    }
}

/// <summary>
/// Механизмы поиска по таблице главного окна (ТЗ §7.7).
/// </summary>
public static class SearchMechanism
{
    /// <summary>
    /// Форма поиска (Ctrl+F): последовательный перебор с кнопкой «Найти далее».
    /// Показывается одна следующая запись, совпадения не подсвечиваются все сразу.
    /// </summary>
    /// <typeparam name="T">Тип элемента таблицы.</typeparam>
    /// <param name="элементы">Элементы таблицы.</param>
    /// <param name="текст">Получение искомого текста элемента.</param>
    /// <param name="запрос">Искомая строка.</param>
    /// <param name="текущийИндекс">Индекс текущей записи; перебор начинается со следующей.</param>
    /// <returns>Индекс найденной записи либо -1, если совпадений нет.</returns>
    public static int НайтиДалее<T>(
        IReadOnlyList<T> элементы,
        Func<T, string?> текст,
        string запрос,
        int текущийИндекс)
    {
        ArgumentNullException.ThrowIfNull(элементы);
        ArgumentNullException.ThrowIfNull(текст);

        if (string.IsNullOrWhiteSpace(запрос) || элементы.Count == 0)
        {
            return -1;
        }

        var начало = текущийИндекс < 0 ? 0 : текущийИндекс + 1;

        for (var шаг = 0; шаг < элементы.Count; шаг++)
        {
            var индекс = (начало + шаг) % элементы.Count;
            var значение = текст(элементы[индекс]);

            if (значение is not null
                && значение.Contains(запрос.Trim(), StringComparison.CurrentCultureIgnoreCase))
            {
                return индекс;
            }
        }

        return -1;
    }

    /// <summary>
    /// Прямой набор с клавиатуры: по мере набора отображаются только совпавшие
    /// записи.
    /// </summary>
    /// <typeparam name="T">Тип элемента таблицы.</typeparam>
    /// <param name="элементы">Элементы таблицы.</param>
    /// <param name="текст">Получение искомого текста элемента.</param>
    /// <param name="набранное">Набранная строка.</param>
    /// <returns>Индексы совпавших записей в порядке следования.</returns>
    public static IReadOnlyList<int> Совпадения<T>(
        IReadOnlyList<T> элементы,
        Func<T, string?> текст,
        string набранное)
    {
        ArgumentNullException.ThrowIfNull(элементы);
        ArgumentNullException.ThrowIfNull(текст);

        var индексы = new List<int>();

        if (string.IsNullOrWhiteSpace(набранное))
        {
            return индексы;
        }

        var образец = набранное.Trim();

        for (var индекс = 0; индекс < элементы.Count; индекс++)
        {
            var значение = текст(элементы[индекс]);

            if (значение is not null
                && значение.StartsWith(образец, StringComparison.CurrentCultureIgnoreCase))
            {
                индексы.Add(индекс);
            }
        }

        return индексы;
    }
}
