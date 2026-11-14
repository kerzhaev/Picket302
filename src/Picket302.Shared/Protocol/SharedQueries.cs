using Picket302.Domain.Entities;
using Picket302.Shared.Dtos;

namespace Picket302.Shared.Protocol;

/// <summary>
/// Запрос постраничного списка контрагентов.
/// </summary>
/// <param name="Offset">Смещение выборки.</param>
/// <param name="Limit">Предельный размер страницы.</param>
/// <param name="Filter">Фильтр выборки.</param>
public sealed record GetCounterpartiesQuery(
    int Offset,
    int Limit,
    CounterpartyFilterDto? Filter = null);

/// <summary>
/// Запрос карточки контрагента.
/// </summary>
/// <param name="Id">Идентификатор контрагента.</param>
public sealed record GetCounterpartyQuery(long Id);

/// <summary>
/// Запрос справочника отправителей.
/// </summary>
public sealed record GetSendersQuery;

/// <summary>
/// Запрос поиска контрагента по ключу слияния.
/// </summary>
/// <param name="Inn">ИНН либо УНП.</param>
/// <param name="Name">Наименование.</param>
/// <param name="Zip">Почтовый индекс.</param>
public sealed record GetMergeKeyQuery(string? Inn, string Name, string? Zip);

/// <summary>
/// Запрос различных значений поля контрагентов.
/// </summary>
/// <param name="Field">Имя поля.</param>
public sealed record GetDistinctValuesQuery(string Field);

/// <summary>
/// Запрос пользовательских полей контрагента.
/// </summary>
/// <param name="CounterpartyId">Идентификатор контрагента.</param>
public sealed record GetCustomFieldsQuery(long CounterpartyId);

/// <summary>
/// Запрос постраничного списка отправлений.
/// </summary>
/// <param name="Offset">Смещение выборки.</param>
/// <param name="Limit">Предельный размер страницы.</param>
/// <param name="Filter">Фильтр выборки.</param>
public sealed record GetMailsQuery(
    int Offset,
    int Limit,
    MailFilterDto? Filter = null);

/// <summary>
/// Запрос отправления по идентификатору.
/// </summary>
/// <param name="Id">Идентификатор отправления.</param>
public sealed record GetMailQuery(long Id);

/// <summary>
/// Запрос счётчиков отправлений по папкам.
/// </summary>
public sealed record GetFolderCountsQuery;

/// <summary>
/// Запрос списка рассылок.
/// </summary>
/// <param name="Filter">Фильтр выборки.</param>
public sealed record GetDistributionsQuery(DistributionFilterDto? Filter = null);

/// <summary>
/// Запрос рассылки по идентификатору.
/// </summary>
/// <param name="Id">Идентификатор рассылки.</param>
public sealed record GetDistributionQuery(long Id);

/// <summary>
/// Запрос дерева категорий адресной книги.
/// </summary>
public sealed record GetCategoryTreeQuery;

/// <summary>
/// Запрос постраничного списка сессий печати.
/// </summary>
/// <param name="Offset">Смещение выборки.</param>
/// <param name="Limit">Предельный размер страницы.</param>
public sealed record GetPrintJobsQuery(int Offset, int Limit);

/// <summary>
/// Запрос постраничного списка журнала печати.
/// </summary>
/// <param name="Offset">Смещение выборки.</param>
/// <param name="Limit">Предельный размер страницы.</param>
public sealed record GetPrintHistoryQuery(int Offset, int Limit);

/// <summary>
/// Запрос журнала email-рассылок.
/// </summary>
/// <param name="Limit">Предельное число записей.</param>
public sealed record GetEmailLogQuery(int Limit);

/// <summary>
/// Запрос снимка объёма общей базы.
/// </summary>
public sealed record GetUsageQuery;

/// <summary>
/// Запрос счётчиков выборочной очистки базы.
/// </summary>
public sealed record GetScopeCountsQuery;

/// <summary>
/// Запрос списка почтовых реестров.
/// </summary>
public sealed record GetPostRegistersQuery;

/// <summary>
/// Запрос документов отправления.
/// </summary>
/// <param name="MailId">Идентификатор отправления.</param>
public sealed record GetMailDocumentsQuery(long MailId);

/// <summary>
/// Запрос истории движения документа.
/// </summary>
/// <param name="MailId">Идентификатор отправления.</param>
public sealed record GetDocumentMovementsQuery(long MailId);

/// <summary>
/// Запрос списка описей.
/// </summary>
public sealed record GetInventoriesQuery;

/// <summary>
/// Запрос позиций описи.
/// </summary>
/// <param name="InventoryId">Идентификатор описи.</param>
public sealed record GetInventoryItemsQuery(long InventoryId);

/// <summary>
/// Категория адресной книги в общей базе.
/// </summary>
/// <param name="Id">Идентификатор категории.</param>
/// <param name="Name">Наименование.</param>
/// <param name="ParentId">Родительская категория.</param>
/// <param name="IsDefault">Признак группы по умолчанию.</param>
/// <param name="IsClosed">Признак закрытой группы.</param>
/// <param name="SortOrder">Порядок сортировки.</param>
/// <param name="CounterpartyCount">Число контрагентов в категории.</param>
/// <param name="Version">Маркер версии группы.</param>
public sealed record CategoryDto(
    long Id,
    string Name,
    long? ParentId,
    bool IsDefault,
    bool IsClosed,
    int SortOrder,
    int CounterpartyCount,
    long Version);

/// <summary>
/// Почтовый реестр в общей базе.
/// </summary>
/// <param name="Id">Идентификатор реестра.</param>
/// <param name="RegisterNumber">Номер реестра.</param>
/// <param name="BatchNumber">Номер партии.</param>
/// <param name="SendingDate">Дата отправки.</param>
/// <param name="ShippingPoint">Пункт отправки.</param>
/// <param name="CreatedAt">Момент создания (UTC).</param>
public sealed record PostRegisterDto(
    long Id,
    string RegisterNumber,
    string? BatchNumber,
    DateTime? SendingDate,
    string? ShippingPoint,
    DateTime CreatedAt);

/// <summary>
/// Документ отправления в общей базе.
/// </summary>
/// <param name="Id">Идентификатор документа.</param>
/// <param name="MailId">Отправление.</param>
/// <param name="DocNumber">Номер документа.</param>
public sealed record MailDocumentDto(
    long Id,
    long MailId,
    string DocNumber);

/// <summary>
/// Движение документа в общей базе.
/// </summary>
/// <param name="Id">Идентификатор движения.</param>
/// <param name="MailId">Отправление.</param>
/// <param name="MovedAt">Момент движения (UTC).</param>
/// <param name="TargetDepartment">Отдел назначения.</param>
/// <param name="Executor">Исполнитель.</param>
/// <param name="Action">Вид движения.</param>
public sealed record DocumentMovementDto(
    long Id,
    long MailId,
    DateTime MovedAt,
    string TargetDepartment,
    string? Executor,
    DocumentMovementAction? Action);

/// <summary>
/// Опись в общей базе.
/// </summary>
/// <param name="Id">Идентификатор описи.</param>
/// <param name="Name">Наименование описи.</param>
/// <param name="CounterpartyId">Контрагент.</param>
/// <param name="Destination">Назначение.</param>
/// <param name="Note">Примечание.</param>
/// <param name="CreatedAt">Момент создания (UTC).</param>
public sealed record InventoryDto(
    long Id,
    string Name,
    long? CounterpartyId,
    string? Destination,
    string? Note,
    DateTime CreatedAt);

/// <summary>
/// Позиция описи в общей базе.
/// </summary>
/// <param name="Id">Идентификатор позиции.</param>
/// <param name="InventoryId">Опись.</param>
/// <param name="Name">Наименование позиции.</param>
/// <param name="Quantity">Количество.</param>
/// <param name="Cost">Стоимость.</param>
/// <param name="Note">Примечание.</param>
/// <param name="CreatedAt">Момент создания (UTC).</param>
public sealed record InventoryItemDto(
    long Id,
    long InventoryId,
    string Name,
    int Quantity,
    decimal Cost,
    string? Note,
    DateTime CreatedAt);
