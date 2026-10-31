namespace Picket302.Domain.Common;

/// <summary>
/// Запрос постраничной выборки (ТЗ §11.3: «интеграционные тесты БД (пагинация 100k, фильтры)»).
/// </summary>
/// <param name="Offset">Смещение от начала выборки (нумерация страниц с нуля).</param>
/// <param name="Limit">Размер страницы.</param>
public sealed record PageRequest(int Offset, int Limit)
{
    /// <summary>Размер страницы по умолчанию.</summary>
    public const int DefaultLimit = 100;

    /// <summary>
    /// Максимальный размер страницы: в память загружается только текущая
    /// отображаемая страница (50–200 записей, ТЗ §8.1, функция 11).
    /// </summary>
    public const int MaxLimit = 200;

    /// <summary>Минимальный размер страницы.</summary>
    public const int MinLimit = 50;

    /// <summary>Смещение по умолчанию.</summary>
    public const int DefaultOffset = 0;

    /// <summary>Нормализует параметры выборки.</summary>
    /// <returns>Запрос с неотрицательным смещением и размером страницы в допустимых пределах.</returns>
    public PageRequest Normalized() => new(
        Math.Max(Offset, DefaultOffset),
        Math.Clamp(Limit == 0 ? DefaultLimit : Limit, MinLimit, MaxLimit));
}

/// <summary>
/// Страница результатов постраничной выборки.
/// </summary>
/// <typeparam name="T">Тип элемента страницы.</typeparam>
/// <param name="Items">Элементы страницы.</param>
/// <param name="Offset">Смещение от начала выборки.</param>
/// <param name="Limit">Размер страницы.</param>
/// <param name="TotalCount">Общее число записей, удовлетворяющих условию отбора.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int Offset, int Limit, long TotalCount)
{
    /// <summary>Пустая страница.</summary>
    /// <param name="request">Исходный запрос выборки.</param>
    /// <returns>Страница без элементов.</returns>
    public static Page<T> Empty(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Page<T>(Array.Empty<T>(), request.Offset, request.Limit, 0);
    }
}
