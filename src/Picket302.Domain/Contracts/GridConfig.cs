namespace Picket302.Domain.Contracts;

/// <summary>
/// Геометрия и состав колонок таблицы (ТЗ §8.1, функция 12: «Запоминание
/// геометрии и состава колонок»).
/// </summary>
/// <param name="GridId">Идентификатор таблицы (идентификатор окна или грида).</param>
/// <param name="Columns">Описание колонок в порядке отображения.</param>
public sealed record GridConfigDto(
    string GridId,
    IReadOnlyList<GridColumnConfig> Columns);

/// <summary>
/// Описание одной колонки таблицы (ТЗ §8.1, функция 12).
/// </summary>
/// <param name="Name">Имя поля — ключ сохранения.</param>
/// <param name="Caption">Заголовок колонки.</param>
/// <param name="Width">Ширина колонки в пикселях.</param>
/// <param name="Visible">Признак видимости колонки.</param>
/// <param name="SortOrder">Порядок колонки в списке.</param>
public sealed record GridColumnConfig(
    string Name,
    string Caption,
    int Width,
    bool Visible = true,
    int SortOrder = 0);
