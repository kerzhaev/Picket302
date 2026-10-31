namespace Picket302.Domain.Repositories;

/// <summary>
/// Хранилище сквозного счётчика регистрационных номеров
/// (ТЗ §8.1, функция 25: «Сквозная регистрационная нумерация»).
/// </summary>
/// <remarks>
/// Инкремент счётчика обязан быть атомарным: два параллельных запроса должны
/// получить разные последовательные номера без коллизий.
/// </remarks>
public interface IRegistrationNumberStore
{
    /// <summary>
    /// Атомарно инкрементирует счётчик и возвращает новое значение.
    /// </summary>
    /// <param name="counterKey">Ключ счётчика (например, имя маски номера).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Новое значение счётчика.</returns>
    Task<long> NextAsync(string counterKey, CancellationToken cancellationToken = default);
}
