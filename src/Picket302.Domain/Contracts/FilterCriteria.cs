namespace Picket302.Domain.Contracts;

/// <summary>
/// Условия отбора и сортировки контрагентов (ТЗ §8.1, функция 9: «Фильтры и
/// сортировка по любой колонке»).
/// </summary>
public sealed class FilterCriteria
{
    /// <summary>Условие отбора по значению поля.</summary>
    public sealed class Condition
    {
        /// <summary>Имя поля — колонки таблицы.</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>Оператор сравнения.</summary>
        public FilterOperator Operator { get; set; } = FilterOperator.Contains;

        /// <summary>Сравниваемое значение.</summary>
        public string? Value { get; set; }
    }

    /// <summary>Список условий отбора; условия объединяются по «И».</summary>
    public List<Condition> Conditions { get; set; } = [];

    /// <summary>Колонка сортировки; <see langword="null"/> — без сортировки.</summary>
    public string? SortColumn { get; set; }

    /// <summary>Признак сортировки по убыванию.</summary>
    public bool SortDescending { get; set; }

    /// <summary>Признак применения встроенного фильтра «Избранное».</summary>
    public bool OnlyFavorites { get; set; }

    /// <summary>
    /// Идентификатор группы пользовательского фильтра; <see langword="null"/> —
    /// пользовательский фильтр не применён.
    /// </summary>
    public long? GroupId { get; set; }
}

/// <summary>
/// Оператор отбора (ТЗ §8.1, функция 9).
/// </summary>
public enum FilterOperator
{
    /// <summary>Содержит подстроку.</summary>
    Contains = 0,

    /// <summary>Равно.</summary>
    Equals = 1,

    /// <summary>Не содержит подстроку.</summary>
    NotContains = 2,

    /// <summary>Больше.</summary>
    GreaterThan = 3,

    /// <summary>Меньше.</summary>
    LessThan = 4,
}
