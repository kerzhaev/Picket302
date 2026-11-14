using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Shared.Dtos;

namespace Picket302.Shared.Commands;

/// <summary>
/// Создание отправления в общей базе.
/// </summary>
/// <param name="Mail">Отправление.</param>
public sealed record CreateMailRequest(MailDto Mail);

/// <summary>
/// Смена статуса отправления.
/// </summary>
/// <param name="Id">Идентификатор отправления.</param>
/// <param name="NewStatus">Новый статус.</param>
/// <param name="Moment">Момент перехода; пусто — текущее время.</param>
/// <param name="ExpectedVersion">Ожидаемая версия отправления.</param>
public sealed record TransitionMailStatusRequest(
    long Id,
    MailStatus NewStatus,
    DateTime? Moment,
    long ExpectedVersion);

/// <summary>
/// Перенос отправления в папку.
/// </summary>
/// <param name="Id">Идентификатор отправления.</param>
/// <param name="FolderId">Папка назначения.</param>
/// <param name="ExpectedVersion">Ожидаемая версия отправления.</param>
public sealed record MoveMailToFolderRequest(
    long Id,
    long FolderId,
    long ExpectedVersion);

/// <summary>
/// Обновление адреса получателя в непечатанных письмах.
/// </summary>
/// <param name="CounterpartyId">Контрагент-получатель.</param>
/// <param name="Address">Новый адрес.</param>
public sealed record UpdateDraftRecipientAddressRequest(
    long CounterpartyId,
    AddressDto Address,
    long ExpectedVersion);

/// <summary>
/// Выдача очередного регистрационного номера отправлению.
/// </summary>
/// <remarks>
/// Номер выдаётся службой внутри той же транзакции, в которой номер
/// записывается в отправление: два параллельных клиента не могут получить
/// один и тот же номер.
/// </remarks>
/// <param name="MailId">Отправление.</param>
/// <param name="TemplateMask">Шаблон номера; пусто — номер по умолчанию.</param>
/// <param name="Moment">Момент выдачи; пусто — текущее время.</param>
/// <param name="ExpectedVersion">Ожидаемая версия отправления.</param>
public sealed record AssignMailNumberRequest(
    long MailId,
    string? TemplateMask,
    DateTime? Moment,
    long ExpectedVersion);

/// <summary>
/// Формирование рассылки по плану.
/// </summary>
/// <param name="Plan">План рассылки.</param>
public sealed record GenerateDistributionRequest(DistributionPlan Plan);

/// <summary>
/// Создание рассылки по выбранным контрагентам.
/// </summary>
/// <param name="CounterpartyIds">Получатели.</param>
/// <param name="SenderId">Отправитель.</param>
/// <param name="Name">Наименование рассылки.</param>
/// <param name="EnvelopeType">Формат конверта.</param>
public sealed record CreateDistributionFromSelectionRequest(
    IReadOnlyList<long> CounterpartyIds,
    long? SenderId = null,
    string? Name = null,
    string? EnvelopeType = null);

/// <summary>
/// Клонирование рассылки для повторной отправки.
/// </summary>
/// <param name="DistributionId">Исходная рассылка.</param>
public sealed record CloneDistributionRequest(long DistributionId);

/// <summary>
/// Ответ клонирования рассылки.
/// </summary>
/// <param name="Distribution">Новая рассылка.</param>
/// <param name="LettersCreated">Число созданных писем.</param>
/// <param name="SkippedCounterparties">Контрагенты, пропущенные при клонировании.</param>
public sealed record CloneDistributionResponse(
    DistributionDto Distribution,
    int LettersCreated,
    IReadOnlyList<string> SkippedCounterparties);

/// <summary>
/// Правка сохранённой рассылки.
/// </summary>
/// <param name="DistributionId">Идентификатор рассылки.</param>
/// <param name="Name">Наименование.</param>
/// <param name="Kind">Вид рассылки.</param>
/// <param name="SenderId">Отправитель.</param>
/// <param name="CounterpartyIds">Получатели.</param>
/// <param name="EnvelopeType">Формат конверта.</param>
/// <param name="ExpectedVersion">Ожидаемая версия рассылки.</param>
public sealed record UpdateDistributionRequest(
    long DistributionId,
    string Name,
    DistributionKind Kind,
    long? SenderId,
    IReadOnlyList<long> CounterpartyIds,
    string? EnvelopeType,
    long ExpectedVersion);

/// <summary>
/// Ответ правки рассылки.
/// </summary>
/// <param name="Distribution">Рассылка после правки.</param>
/// <param name="LettersAdded">Число добавленных писем.</param>
/// <param name="LettersRemoved">Число удалённых писем.</param>
/// <param name="SkippedCounterparties">Контрагенты, пропущенные при правке.</param>
public sealed record UpdateDistributionResponse(
    DistributionDto Distribution,
    int LettersAdded,
    int LettersRemoved,
    IReadOnlyList<string> SkippedCounterparties);

/// <summary>
/// Перевод рассылки из черновика в сохранённую.
/// </summary>
/// <param name="DistributionId">Идентификатор рассылки.</param>
public sealed record SaveDistributionRequest(long DistributionId, long ExpectedVersion);

/// <summary>
/// Отметка рассылки как отправленной.
/// </summary>
/// <param name="DistributionId">Идентификатор рассылки.</param>
/// <param name="Moment">Момент отправки; пусто — текущее время.</param>
public sealed record MarkDistributionDispatchedRequest(
    long DistributionId,
    DateTime? Moment,
    long ExpectedVersion);

/// <summary>
/// Пересчёт требуемого числа конвертов рассылки.
/// </summary>
/// <param name="DistributionId">Идентификатор рассылки.</param>
public sealed record RecalculateEnvelopesRequest(long DistributionId, long ExpectedVersion);

/// <summary>
/// Ответ со списком рассылок.
/// </summary>
/// <param name="Distributions">Рассылки.</param>
public sealed record DistributionListResponse(IReadOnlyList<DistributionDto> Distributions);

/// <summary>
/// Ответ со списком отправлений.
/// </summary>
/// <param name="Mails">Отправления.</param>
public sealed record MailListResponse(IReadOnlyList<MailDto> Mails);

/// <summary>
/// Ответ со счётчиками писем по папкам.
/// </summary>
/// <param name="Counts">Счётчики: идентификатор папки — число писем.</param>
public sealed record FolderMailCountsResponse(IReadOnlyDictionary<long, int> Counts);
