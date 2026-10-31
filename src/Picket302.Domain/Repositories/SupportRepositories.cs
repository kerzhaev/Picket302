using Picket302.Domain.Entities;

namespace Picket302.Domain.Repositories;

/// <summary>
/// Репозиторий осей вложений (ТЗ §3.1.12, таблица <c>inventories</c>).
/// </summary>
public interface IInventoryRepository
{
    /// <summary>Добавляет опись и возвращает её идентификатор.</summary>
    /// <param name="inventory">Сохраняемая опись.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(Inventory inventory, CancellationToken cancellationToken = default);

    /// <summary>Читает опись по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Опись либо <see langword="null"/>, если запись не найдена.</returns>
    Task<Inventory?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает перечень осей.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список осей.</returns>
    Task<IReadOnlyList<Inventory>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Обновляет опись.</summary>
    /// <param name="inventory">Сохраняемая опись.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(Inventory inventory, CancellationToken cancellationToken = default);

    /// <summary>Удаляет опись вместе с позициями.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий позиций осей вложений (ТЗ §3.1.12, таблица <c>inventory_items</c>).
/// </summary>
public interface IInventoryItemRepository
{
    /// <summary>Добавляет позицию описи и возвращает её идентификатор.</summary>
    /// <param name="item">Сохраняемая позиция.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(InventoryItem item, CancellationToken cancellationToken = default);

    /// <summary>Читает позицию по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Позиция либо <see langword="null"/>, если запись не найдена.</returns>
    Task<InventoryItem?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает позиции описи.</summary>
    /// <param name="inventoryId">Идентификатор описи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список позиций описи.</returns>
    Task<IReadOnlyList<InventoryItem>> GetByInventoryAsync(
        long inventoryId,
        CancellationToken cancellationToken = default);

    /// <summary>Обновляет позицию описи.</summary>
    /// <param name="item">Сохраняемая позиция.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(InventoryItem item, CancellationToken cancellationToken = default);

    /// <summary>Удаляет позицию описи.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий хранилища параметров «ключ — значение» (ТЗ §3.1.17, таблица <c>parameters</c>).
/// </summary>
public interface IParameterRepository
{
    /// <summary>Добавляет параметр и возвращает его идентификатор.</summary>
    /// <param name="parameter">Сохраняемый параметр.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(Parameter parameter, CancellationToken cancellationToken = default);

    /// <summary>Читает параметр по имени.</summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Параметр либо <see langword="null"/>, если запись не найдена.</returns>
    Task<Parameter?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Возвращает значение параметра.</summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Значение параметра либо <see langword="null"/>, если запись не найдена.</returns>
    Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Возвращает перечень параметров.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список параметров.</returns>
    Task<IReadOnlyList<Parameter>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Обновляет параметр.</summary>
    /// <param name="parameter">Сохраняемый параметр.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(Parameter parameter, CancellationToken cancellationToken = default);

    /// <summary>Удаляет параметр.</summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий версий схемы (ТЗ §3.1.18, таблица <c>schema_version</c>).
/// </summary>
public interface ISchemaVersionRepository
{
    /// <summary>Добавляет запись о применённой миграции.</summary>
    /// <param name="schemaVersion">Сохраняемая запись.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(SchemaVersion schemaVersion, CancellationToken cancellationToken = default);

    /// <summary>Возвращает перечень применённых миграций по возрастанию версии.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список записей журнала схемы.</returns>
    Task<IReadOnlyList<SchemaVersion>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает последнюю применённую версию схемы.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Последняя версия либо <see langword="null"/>, если миграции не применялись.</returns>
    Task<SchemaVersion?> GetLatestAsync(CancellationToken cancellationToken = default);
}
