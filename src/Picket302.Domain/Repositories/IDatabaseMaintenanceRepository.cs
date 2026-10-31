namespace Picket302.Domain.Repositories;

/// <summary>
/// Обслуживание базы: выборочная очистка по областям данных
/// (ТЗ §8.11, функция 137; §8.12, функция 153: «Демо-база и очистка базы»).
/// </summary>
/// <remarks>
/// Очистка выполняется по областям: «Категории контрагентов», «База рассылок»,
/// «База контрагентов», «База отправленных писем». Каждая операция возвращает
/// число затронутых записей — оно показывается оператору до подтверждения.
/// </remarks>
public interface IDatabaseMaintenanceRepository
{
    /// <summary>
    /// Очищает справочник категорий контрагентов. Группа по умолчанию
    /// «Неразобранные» создаётся заново: инвариант «ровно одна группа по
    /// умолчанию» сохраняется.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых категорий.</returns>
    Task<int> ClearCounterpartyCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Очищает базу рассылок: письма рассылок и сами рассылки. Адресная книга
    /// не затрагивается.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых рассылок.</returns>
    Task<int> ClearDistributionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Очищает базу контрагентов: письма этих контрагентов удаляются вместе с
    /// ними (у письма обязателен получатель), категории и рассылки сохраняются.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых контрагентов.</returns>
    Task<int> ClearCounterpartiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Очищает базу отправленных писем: журнал напечатанных конвертов и сами
    /// письма.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число удалённых писем.</returns>
    Task<int> ClearSentMailsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает число записей, которые затронет очистка указанной области —
    /// значение показывается оператору до подтверждения (ТЗ §8.12, функция 153:
    /// «Очистка требует подтверждения с точным числом удаляемых записей»).
    /// </summary>
    /// <param name="scope">Область очистки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число записей в области.</returns>
    Task<int> CountScopeAsync(
        Contracts.ClearScope scope,
        CancellationToken cancellationToken = default);
}
