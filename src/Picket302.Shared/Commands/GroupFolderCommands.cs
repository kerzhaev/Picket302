using Picket302.Domain.Entities;
using Picket302.Shared.Dtos;

namespace Picket302.Shared.Commands;

/// <summary>
/// Создание группы адресной книги.
/// </summary>
/// <param name="Name">Наименование группы.</param>
/// <param name="Note">Примечание.</param>
/// <param name="IsDefault">Сделать группой по умолчанию.</param>
/// <param name="IsClosed">Закрытая группа.</param>
/// <param name="ParentId">Родительская группа.</param>
public sealed record CreateGroupRequest(
    string Name,
    string? Note = null,
    bool IsDefault = false,
    bool IsClosed = false,
    long? ParentId = null);

/// <summary>
/// Переименование группы.
/// </summary>
/// <param name="Id">Идентификатор группы.</param>
/// <param name="Name">Новое наименование.</param>
/// <param name="ExpectedVersion">Ожидаемая версия группы.</param>
public sealed record RenameGroupRequest(
    long Id,
    string Name,
    long ExpectedVersion);

/// <summary>
/// Удаление группы.
/// </summary>
/// <param name="Id">Идентификатор группы.</param>
/// <param name="ExpectedVersion">Ожидаемая версия группы.</param>
public sealed record DeleteGroupRequest(
    long Id,
    long ExpectedVersion);

/// <summary>
/// Назначение группы по умолчанию.
/// </summary>
/// <param name="Id">Идентификатор группы.</param>
/// <param name="ExpectedVersion">Ожидаемая версия группы.</param>
public sealed record SetDefaultGroupRequest(
    long Id,
    long ExpectedVersion);

/// <summary>
/// Создание категории дерева адресной книги.
/// </summary>
/// <param name="Name">Наименование категории.</param>
/// <param name="ParentId">Родительская категория.</param>
public sealed record CreateCategoryRequest(
    string Name,
    long? ParentId = null);

/// <summary>
/// Переименование категории.
/// </summary>
/// <param name="Id">Идентификатор категории.</param>
/// <param name="Name">Новое наименование.</param>
/// <param name="ExpectedVersion">Ожидаемая версия группы.</param>
public sealed record RenameCategoryRequest(
    long Id,
    string Name,
    long ExpectedVersion);

/// <summary>
/// Удаление категории.
/// </summary>
/// <param name="Id">Идентификатор категории.</param>
/// <param name="ExpectedVersion">Ожидаемая версия группы.</param>
public sealed record DeleteCategoryRequest(
    long Id,
    long ExpectedVersion);

/// <summary>
/// Перенос контрагента в другую категорию.
/// </summary>
/// <param name="CounterpartyId">Контрагент.</param>
/// <param name="CategoryId">Категория назначения.</param>
/// <param name="ExpectedVersion">Ожидаемая версия контрагента.</param>
public sealed record MoveCounterpartyRequest(
    long CounterpartyId,
    long CategoryId,
    long ExpectedVersion);

/// <summary>
/// Создание папки отправлений.
/// </summary>
/// <param name="Name">Наименование папки.</param>
/// <param name="ParentId">Родительская папка.</param>
/// <param name="Note">Примечание.</param>
public sealed record CreateFolderRequest(
    string Name,
    long? ParentId = null,
    string? Note = null);

/// <summary>
/// Переименование папки.
/// </summary>
/// <param name="Id">Идентификатор папки.</param>
/// <param name="Name">Новое наименование.</param>
/// <param name="ExpectedVersion">Ожидаемая версия папки.</param>
public sealed record RenameFolderRequest(
    long Id,
    string Name,
    long ExpectedVersion);

/// <summary>
/// Перенос папки.
/// </summary>
/// <param name="Id">Идентификатор папки.</param>
/// <param name="ParentId">Новая родительская папка.</param>
/// <param name="ExpectedVersion">Ожидаемая версия папки.</param>
public sealed record MoveFolderRequest(
    long Id,
    long? ParentId,
    long ExpectedVersion);

/// <summary>
/// Удаление папки.
/// </summary>
/// <param name="Id">Идентификатор папки.</param>
/// <param name="ExpectedVersion">Ожидаемая версия папки.</param>
public sealed record DeleteFolderRequest(
    long Id,
    long ExpectedVersion);

/// <summary>
/// Ответ с деревом категорий.
/// </summary>
/// <param name="Id">Идентификатор группы.</param>
/// <param name="Name">Наименование.</param>
/// <param name="ParentId">Родительская группа.</param>
/// <param name="IsDefault">Признак группы по умолчанию.</param>
/// <param name="IsClosed">Признак закрытой группы.</param>
/// <param name="Children">Вложенные категории.</param>
public sealed record CategoryNodeResponse(
    long Id,
    string Name,
    long? ParentId,
    bool IsDefault,
    bool IsClosed,
    IReadOnlyList<CategoryNodeResponse> Children);

/// <summary>
/// Ответ со списком групп.
/// </summary>
/// <param name="Groups">Группы.</param>
public sealed record GroupListResponse(IReadOnlyList<GroupDto> Groups);

/// <summary>
/// Ответ со списком папок.
/// </summary>
/// <param name="Folders">Папки.</param>
public sealed record FolderListResponse(IReadOnlyList<FolderDto> Folders);

/// <summary>
/// Ответ со счётчиком отправлений в папке.
/// </summary>
/// <param name="FolderId">Идентификатор папки.</param>
/// <param name="Count">Число отправлений.</param>
public sealed record FolderMailCountResponse(long FolderId, int Count);
