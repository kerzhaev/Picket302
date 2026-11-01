using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Repositories;

/// <summary>
/// Хранилище настроек отображения интерфейса (ТЗ §8.1, функция 12: «Запоминание
/// геометрии и состава колонок»).
/// </summary>
public interface IUiSettingsStore
{
    /// <summary>
    /// Читает сохранённую конфигурацию таблицы.
    /// </summary>
    /// <param name="gridId">Идентификатор таблицы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Конфигурация либо <see langword="null"/>, если она не сохранена.</returns>
    Task<GridConfigDto?> LoadGridAsync(string gridId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет конфигурацию таблицы.
    /// </summary>
    /// <param name="config">Сохраняемая конфигурация.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task SaveGridAsync(GridConfigDto config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает оформление таблиц (цвета фокуса, выделения и подсказки, «зебра»).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Оформление; при отсутствии настроек — значения по умолчанию.</returns>
    Task<GridAppearance> LoadAppearanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет оформление таблиц.
    /// </summary>
    /// <param name="appearance">Сохраняемое оформление.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task SaveAppearanceAsync(GridAppearance appearance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает сохранённые пользовательские фильтры (ТЗ §8.1, функция 9:
    /// «сохранённые пользовательские фильтры»).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список сохранённых фильтров; при отсутствии — пустой список.</returns>
    Task<IReadOnlyList<SavedFilter>> LoadSavedFiltersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет пользовательский фильтр; фильтр с таким же именем обновляется.
    /// </summary>
    /// <param name="filter">Сохраняемый фильтр.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task SaveSavedFilterAsync(SavedFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет пользовательский фильтр по имени.
    /// </summary>
    /// <param name="name">Имя удаляемого фильтра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если фильтр был удалён.</returns>
    Task<bool> DeleteSavedFilterAsync(string name, CancellationToken cancellationToken = default);
}
