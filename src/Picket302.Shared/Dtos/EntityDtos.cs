using Picket302.Domain.Entities;

namespace Picket302.Shared.Dtos;

/// <summary>
/// Контрагент в общей базе.
/// </summary>
/// <remarks>
/// <para>
/// Версия (<see cref="Version"/>) — проверяемый маркер изменений: клиент
/// предъявляет её при сохранении, и служба отклоняет сохранение с устаревшей
/// версией (код <see cref="Protocol.SharedErrorCodes.Conflict"/>), не записывая
/// половину изменения.
/// </para>
/// <para>
/// Значение 0 означает «версия ещё не фиксировалась»: запись существовала до
/// включения общего режима. Первое сохранение такой записи создаёт версию 1.
/// </para>
/// </remarks>
/// <param name="Id">Идентификатор записи.</param>
/// <param name="Type">Тип контрагента.</param>
/// <param name="Name">Полное наименование или ФИО.</param>
/// <param name="ShortName">Сокращённое наименование.</param>
/// <param name="Inn">ИНН либо УНП.</param>
/// <param name="Kpp">КПП.</param>
/// <param name="Ogrn">ОГРН либо ОГРНИП.</param>
/// <param name="Okfs">ОКФС.</param>
/// <param name="Okpo">ОКПО.</param>
/// <param name="Okved">ОКВЭД.</param>
/// <param name="Bik">БИК банка.</param>
/// <param name="Ks">Корреспондентский счёт.</param>
/// <param name="Rs">Расчётный счёт.</param>
/// <param name="Rkc">Наименование расчётно-кассового центра.</param>
/// <param name="BankName">Наименование обслуживающего банка.</param>
/// <param name="PassportSeries">Серия паспорта.</param>
/// <param name="PassportNumber">Номер паспорта.</param>
/// <param name="PassportIssuedBy">Кем выдан паспорт.</param>
/// <param name="PassportIssuedDate">Дата выдачи паспорта.</param>
/// <param name="BirthDate">Дата рождения.</param>
/// <param name="BirthPlace">Место рождения.</param>
/// <param name="Position">Должность.</param>
/// <param name="DirectorFio">ФИО первого лица.</param>
/// <param name="Email">Адрес электронной почты.</param>
/// <param name="Phone">Телефон.</param>
/// <param name="SpecialNotes">Особые отметки.</param>
/// <param name="Source">Источник появления записи.</param>
/// <param name="Logo">Логотип компании.</param>
/// <param name="Addresses">Три среза адресации.</param>
/// <param name="GroupId">Группа (категория).</param>
/// <param name="IsVip">Признак «избранный адрес».</param>
/// <param name="IsSender">Признак доступности в качестве отправителя.</param>
/// <param name="IsActive">Признак активности записи.</param>
/// <param name="IsDeleted">Признак мягкого удаления.</param>
/// <param name="CreatedAt">Момент создания записи (UTC).</param>
/// <param name="UpdatedAt">Момент последнего изменения записи (UTC).</param>
/// <param name="Version">Маркер версии записи.</param>
public sealed record CounterpartyDto(
    long Id,
    CounterpartyType Type,
    string Name,
    string? ShortName,
    string? Inn,
    string? Kpp,
    string? Ogrn,
    string? Okfs,
    string? Okpo,
    string? Okved,
    string? Bik,
    string? Ks,
    string? Rs,
    string? Rkc,
    string? BankName,
    string? PassportSeries,
    string? PassportNumber,
    string? PassportIssuedBy,
    DateTime? PassportIssuedDate,
    DateTime? BirthDate,
    string? BirthPlace,
    string? Position,
    string? DirectorFio,
    string? Email,
    string? Phone,
    string? SpecialNotes,
    string? Source,
    byte[]? Logo,
    AddressSetDto Addresses,
    long? GroupId,
    bool IsVip,
    bool IsSender,
    bool IsActive,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long Version);

/// <summary>
/// Пользовательское поле контрагента в общей базе.
/// </summary>
/// <param name="Id">Идентификатор поля.</param>
/// <param name="CounterpartyId">Идентификатор контрагента.</param>
/// <param name="FieldName">Имя поля.</param>
/// <param name="FieldValue">Значение поля.</param>
/// <param name="FieldType">Тип поля.</param>
/// <param name="UpdatedAt">Момент последнего изменения (UTC).</param>
/// <param name="Version">Маркер версии поля.</param>
public sealed record CounterpartyFieldDto(
    long Id,
    long CounterpartyId,
    string FieldName,
    string? FieldValue,
    CounterpartyFieldType FieldType,
    DateTime UpdatedAt,
    long Version);

/// <summary>
/// Группа (категория) адресной книги в общей базе.
/// </summary>
/// <param name="Id">Идентификатор группы.</param>
/// <param name="Name">Наименование группы.</param>
/// <param name="Note">Примечание.</param>
/// <param name="IsDefault">Признак группы по умолчанию.</param>
/// <param name="IsClosed">Признак закрытой группы.</param>
/// <param name="ParentId">Родительская группа.</param>
/// <param name="SortOrder">Порядок сортировки.</param>
/// <param name="Version">Маркер версии группы.</param>
public sealed record GroupDto(
    long Id,
    string Name,
    string? Note,
    bool IsDefault,
    bool IsClosed,
    long? ParentId,
    int SortOrder,
    long Version);

/// <summary>
/// Папка отправлений в общей базе.
/// </summary>
/// <param name="Id">Идентификатор папки.</param>
/// <param name="Name">Наименование папки.</param>
/// <param name="ParentId">Родительская папка.</param>
/// <param name="Note">Примечание.</param>
/// <param name="SortOrder">Порядок сортировки.</param>
/// <param name="Version">Маркер версии папки.</param>
public sealed record FolderDto(
    long Id,
    string Name,
    long? ParentId,
    string? Note,
    int SortOrder,
    long Version);

/// <summary>
/// Отправление в общей базе.
/// </summary>
/// <param name="Id">Идентификатор отправления.</param>
/// <param name="CounterpartyId">Контрагент-получатель.</param>
/// <param name="SenderId">Контрагент-отправитель.</param>
/// <param name="DistributionId">Рассылка, породившая отправление.</param>
/// <param name="EnvelopeType">Формат конверта.</param>
/// <param name="NoticeType">Вид уведомления (строковое обозначение).</param>
/// <param name="NotificationType">Вид уведомления.</param>
/// <param name="CopyCount">Число экземпляров.</param>
/// <param name="IsVip">Признак «избранный адрес» на момент создания.</param>
/// <param name="Note">Примечание.</param>
/// <param name="DeclaredValue">Объявленная ценность.</param>
/// <param name="Weight">Вес.</param>
/// <param name="Fee">Пошлина.</param>
/// <param name="Status">Статус отправления.</param>
/// <param name="NeedNotification">Требуется уведомление.</param>
/// <param name="NeedReturnAddress">Требуется обратный адрес.</param>
/// <param name="MailCategory">Категория отправления.</param>
/// <param name="DateCreated">Момент создания (UTC).</param>
/// <param name="DatePrinted">Момент печати (UTC).</param>
/// <param name="DateDispatched">Момент отправки (UTC).</param>
/// <param name="DateConfirmation">Момент подтверждения (UTC).</param>
/// <param name="DateCanceled">Момент отмены (UTC).</param>
/// <param name="IncomingNumber">Входящий номер.</param>
/// <param name="OutgoingNumber">Исходящий (регистрационный) номер.</param>
/// <param name="PostRegisterId">Почтовый реестр.</param>
/// <param name="RussianPostId">Идентификатор Почты России.</param>
/// <param name="RussianPostBatch">Партия Почты России.</param>
/// <param name="InventoryId">Опись.</param>
/// <param name="RecipientAddress">Снимок адреса получателя на момент создания.</param>
/// <param name="FolderId">Папка.</param>
/// <param name="Version">Маркер версии отправления.</param>
public sealed record MailDto(
    long Id,
    long CounterpartyId,
    long SenderId,
    long? DistributionId,
    string? EnvelopeType,
    string? NoticeType,
    NotificationType NotificationType,
    int CopyCount,
    bool IsVip,
    string? Note,
    decimal DeclaredValue,
    decimal Weight,
    decimal Fee,
    MailStatus Status,
    bool NeedNotification,
    bool NeedReturnAddress,
    MailCategory MailCategory,
    DateTime DateCreated,
    DateTime? DatePrinted,
    DateTime? DateDispatched,
    DateTime? DateConfirmation,
    DateTime? DateCanceled,
    string? IncomingNumber,
    string? OutgoingNumber,
    long? PostRegisterId,
    string? RussianPostId,
    string? RussianPostBatch,
    long? InventoryId,
    AddressDto? RecipientAddress,
    long? FolderId,
    long Version);

/// <summary>
/// Рассылка в общей базе.
/// </summary>
/// <param name="Id">Идентификатор рассылки.</param>
/// <param name="Name">Наименование рассылки.</param>
/// <param name="SenderId">Контрагент-отправитель.</param>
/// <param name="Kind">Вид рассылки.</param>
/// <param name="Note">Примечание.</param>
/// <param name="State">Состояние рассылки.</param>
/// <param name="EnvelopesRequired">Требуемое число конвертов.</param>
/// <param name="CreatedAt">Момент создания (UTC).</param>
/// <param name="DispatchedAt">Момент отправки (UTC).</param>
/// <param name="RecipientCount">Число получателей (заполняется при чтении).</param>
/// <param name="Version">Маркер версии рассылки.</param>
public sealed record DistributionDto(
    long Id,
    string Name,
    long? SenderId,
    DistributionKind Kind,
    string? Note,
    DistributionState State,
    int? EnvelopesRequired,
    DateTime CreatedAt,
    DateTime? DispatchedAt,
    int RecipientCount,
    long Version);
