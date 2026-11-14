using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;

namespace Picket302.Shared.Dtos;

/// <summary>
/// Страница данных общей базы.
/// </summary>
/// <typeparam name="T">Тип элемента страницы.</typeparam>
/// <param name="Items">Элементы страницы.</param>
/// <param name="Offset">Смещение выборки.</param>
/// <param name="Limit">Предельный размер страницы.</param>
/// <param name="TotalCount">Общее число подходящих записей.</param>
public sealed record PageDto<T>(
    IReadOnlyList<T> Items,
    int Offset,
    int Limit,
    long TotalCount);

/// <summary>
/// Условие фильтра адресной книги.
/// </summary>
/// <param name="Field">Имя поля.</param>
/// <param name="Operator">Оператор сравнения.</param>
/// <param name="Value">Значение для сравнения.</param>
public sealed record FilterConditionDto(
    string Field,
    FilterOperator Operator,
    string? Value);

/// <summary>
/// Набор условий фильтра адресной книги.
/// </summary>
/// <param name="Conditions">Условия.</param>
/// <param name="SortColumn">Столбец сортировки.</param>
/// <param name="SortDescending">Признак обратной сортировки.</param>
/// <param name="OnlyFavorites">Только избранные записи.</param>
/// <param name="GroupId">Ограничение по группе.</param>
public sealed record FilterCriteriaDto(
    IReadOnlyList<FilterConditionDto> Conditions,
    string? SortColumn = null,
    bool SortDescending = false,
    bool OnlyFavorites = false,
    long? GroupId = null);

/// <summary>
/// Фильтр выборки контрагентов.
/// </summary>
/// <param name="NameFragment">Фрагмент наименования.</param>
/// <param name="Inn">ИНН.</param>
/// <param name="GroupId">Группа.</param>
/// <param name="IsVip">Признак «избранный адрес».</param>
/// <param name="IsSender">Признак отправителя.</param>
/// <param name="IsActive">Признак активности.</param>
/// <param name="IncludeDeleted">Включать мягко удалённые записи.</param>
/// <param name="Criteria">Условия фильтра.</param>
/// <param name="SortColumn">Столбец сортировки.</param>
/// <param name="SortDescending">Признак обратной сортировки.</param>
public sealed record CounterpartyFilterDto(
    string? NameFragment = null,
    string? Inn = null,
    long? GroupId = null,
    bool? IsVip = null,
    bool? IsSender = null,
    bool? IsActive = null,
    bool IncludeDeleted = false,
    FilterCriteriaDto? Criteria = null,
    string? SortColumn = null,
    bool SortDescending = false);

/// <summary>
/// Фильтр выборки отправлений.
/// </summary>
/// <param name="Status">Статус.</param>
/// <param name="CounterpartyId">Контрагент-получатель.</param>
/// <param name="SenderId">Контрагент-отправитель.</param>
/// <param name="DistributionId">Рассылка.</param>
/// <param name="PostRegisterId">Почтовый реестр.</param>
/// <param name="DateFrom">Начало периода.</param>
/// <param name="DateTo">Конец периода.</param>
public sealed record MailFilterDto(
    MailStatus? Status = null,
    long? CounterpartyId = null,
    long? SenderId = null,
    long? DistributionId = null,
    long? PostRegisterId = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null);

/// <summary>
/// Фильтр выборки рассылок.
/// </summary>
/// <param name="CreatedFrom">Начало периода создания.</param>
/// <param name="State">Состояние рассылки.</param>
/// <param name="SenderId">Контрагент-отправитель.</param>
public sealed record DistributionFilterDto(
    DateTime? CreatedFrom = null,
    DistributionState? State = null,
    long? SenderId = null);

/// <summary>
/// Снимок объёма данных общей базы (для лимитов пробного периода).
/// </summary>
/// <param name="CounterpartyCount">Число контрагентов.</param>
/// <param name="MailCount">Число отправлений.</param>
public sealed record UsageSnapshotDto(
    int CounterpartyCount,
    int MailCount);

/// <summary>
/// Счётчики очистки базы по областям.
/// </summary>
/// <param name="Counterparties">Число контрагентов.</param>
/// <param name="Distributions">Число рассылок.</param>
/// <param name="Mails">Число отправлений.</param>
public sealed record ScopeCountsDto(
    int Counterparties,
    int Distributions,
    int Mails);
