using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Repositories;

/// <summary>
/// Условия отбора контрагентов (ТЗ §11.3: «пагинация 100k, фильтры»).
/// </summary>
/// <param name="NameFragment">Фрагмент наименования.</param>
/// <param name="Inn">ИНН (УНП).</param>
/// <param name="GroupId">Группа (категория).</param>
/// <param name="IsVip">Признак «Избранный адрес».</param>
/// <param name="IsSender">Признак доступности в качестве глобального отправителя.</param>
/// <param name="IsActive">Флаг активности записи.</param>
/// <param name="IncludeDeleted">
/// Включать мягко удалённые записи в выборку. По умолчанию исключаются
/// (ТЗ §3.1.19: «Правило мягкого удаления»).
/// </param>
/// <param name="Criteria">
/// Критерии слоя запроса: дополнительные условия отбора и сортировка
/// (ТЗ §8.1, функция 9: «Фильтры и сортировка по любой колонке»).
/// </param>
/// <param name="SortColumn">
/// Ключ колонки сортировки из белого списка адресной книги («vip», «main_zip»,
/// «main_city», «main_street», «name», «group» и другие). Неизвестный ключ
/// отвергается репозиторием — произвольный текст в запрос не попадает.
/// </param>
/// <param name="SortDescending">Признак сортировки по убыванию.</param>
public sealed record CounterpartyFilter(
    string? NameFragment = null,
    string? Inn = null,
    long? GroupId = null,
    bool? IsVip = null,
    bool? IsSender = null,
    bool? IsActive = null,
    bool IncludeDeleted = false,
    FilterCriteria? Criteria = null,
    string? SortColumn = null,
    bool SortDescending = false);

/// <summary>
/// Репозиторий контрагентов (ТЗ §3.1.1, таблица <c>counterparties</c>).
/// </summary>
public interface ICounterpartyRepository
{
    /// <summary>Добавляет контрагента и возвращает его идентификатор.</summary>
    /// <param name="counterparty">Сохраняемый контрагент.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(Counterparty counterparty, CancellationToken cancellationToken = default);

    /// <summary>Читает контрагента по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Контрагент либо <see langword="null"/>, если запись не найдена.</returns>
    Task<Counterparty?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Обновляет контрагента.</summary>
    /// <param name="counterparty">Сохраняемый контрагент.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(Counterparty counterparty, CancellationToken cancellationToken = default);

    /// <summary>Выполняет мягкое удаление контрагента (ТЗ §3.1.19).</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись помечена удалённой.</returns>
    Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает мягко удалённого контрагента в рабочее состояние.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись восстановлена.</returns>
    Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Окончательно удаляет контрагента (отдельная команда с подтверждением).</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает постраничную выборку контрагентов.</summary>
    /// <param name="request">Параметры постраничной выборки.</param>
    /// <param name="filter">Условия отбора.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница контрагентов.</returns>
    Task<Page<Counterparty>> GetPagedAsync(
        PageRequest request,
        CounterpartyFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает контрагентов группы.</summary>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список контрагентов группы.</returns>
    Task<IReadOnlyList<Counterparty>> GetByGroupAsync(
        long groupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает справочник отправителей — записи адресной книги с установленным
    /// флагом <c>is_sender</c> (ТЗ §8.1, функция 139).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список отправителей, упорядоченный по наименованию.</returns>
    Task<IReadOnlyList<Counterparty>> GetSendersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает различные значения поля адресной книги, упорядоченные по
    /// алфавиту (ТЗ §8.11, функция 133: «Подбор значения поля из имеющихся
    /// записей»).
    /// </summary>
    /// <param name="field">
    /// Имя поля: <c>name</c>, <c>inn</c>, <c>phone</c>, <c>email</c>,
    /// <c>post_zip</c>, <c>post_str5</c> (город) или <c>post_str1</c>.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список уникальных значений поля.</returns>
    Task<IReadOnlyList<string>> GetDistinctFieldValuesAsync(
        string field,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ищет контрагента по ключу слияния импорта: ИНН (УНП), а при его
    /// отсутствии — по наименованию и почтовому индексу
    /// (ТЗ §8.12, функция 74: «Тонкое слияние»).
    /// </summary>
    /// <param name="inn">ИНН (УНП) контрагента; может быть пустым.</param>
    /// <param name="name">Наименование контрагента.</param>
    /// <param name="zip">Почтовый индекс; может быть пустым.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Найденный контрагент либо <see langword="null"/>.</returns>
    Task<Counterparty?> FindByMergeKeyAsync(
        string? inn,
        string name,
        string? zip,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выполняет пакетное изменение одного поля у выделенных записей
    /// (ТЗ §8.1, функция 18: «Пакетное редактирование одного поля у выделенных
    /// записей»).
    /// </summary>
    /// <param name="ids">Идентификаторы изменяемых записей.</param>
    /// <param name="propertyName">
    /// Имя изменяемого свойства: <c>Name</c>, <c>ShortName</c>, <c>Inn</c>,
    /// <c>Kpp</c>, <c>Phone</c>, <c>Email</c>, <c>GroupId</c>, <c>IsVip</c>,
    /// <c>IsActive</c>.
    /// </param>
    /// <param name="newValue">Новое значение свойства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых записей.</returns>
    Task<int> UpdateFieldAsync(
        IReadOnlyList<long> ids,
        string propertyName,
        object? newValue,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий групп (ТЗ §3.1.2, таблица <c>groups</c>).
/// </summary>
public interface IGroupRepository
{
    /// <summary>Добавляет группу и возвращает её идентификатор.</summary>
    /// <param name="group">Сохраняемая группа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(Group group, CancellationToken cancellationToken = default);

    /// <summary>Читает группу по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Группа либо <see langword="null"/>, если запись не найдена.</returns>
    Task<Group?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает перечень всех групп.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список групп.</returns>
    Task<IReadOnlyList<Group>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Обновляет группу.</summary>
    /// <param name="group">Сохраняемая группа.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(Group group, CancellationToken cancellationToken = default);

    /// <summary>Удаляет группу.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает группу по умолчанию.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Группа по умолчанию либо <see langword="null"/>, если её нет.</returns>
    Task<Group?> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Назначает группу единственной группой по умолчанию: снимает признак со
    /// всех остальных групп и выставляет его у указанной.
    /// </summary>
    /// <param name="groupId">Идентификатор группы по умолчанию.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если назначение выполнено.</returns>
    Task<bool> SetDefaultAsync(long groupId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий пользовательских полей контрагента (ТЗ §3.1.9, таблица <c>counterparty_fields</c>).
/// </summary>
public interface ICounterpartyFieldRepository
{
    /// <summary>Добавляет пользовательское поле.</summary>
    /// <param name="field">Сохраняемое поле.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(CounterpartyField field, CancellationToken cancellationToken = default);

    /// <summary>Читает пользовательское поле по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Поле либо <see langword="null"/>, если запись не найдена.</returns>
    Task<CounterpartyField?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает пользовательские поля контрагента.</summary>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список полей контрагента.</returns>
    Task<IReadOnlyList<CounterpartyField>> GetByCounterpartyAsync(
        long counterpartyId,
        CancellationToken cancellationToken = default);

    /// <summary>Обновляет пользовательское поле.</summary>
    /// <param name="field">Сохраняемое поле.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(CounterpartyField field, CancellationToken cancellationToken = default);

    /// <summary>Удаляет пользовательское поле.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
