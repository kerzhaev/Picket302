using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;

namespace Picket302.Application.Services;

/// <summary>
/// Служба ведения папок писем (ТЗ §8.1, функция 22; §3.1.11).
/// </summary>
/// <remarks>
/// Служба обеспечивает требование §3.1.11: циклические ссылки в дереве папок
/// пресекаются валидатором домена. Проверка выполняется по всему дереву до
/// записи, поэтому перемещение папки внутрь её собственного потомка
/// отклоняется с понятным текстом нарушения.
/// </remarks>
public sealed class FolderService
{
    private readonly IFolderRepository _папки;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт службу папок.</summary>
    /// <param name="папки">Репозиторий папок.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public FolderService(IFolderRepository папки, IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(папки);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _папки = папки;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Создаёт папку. Корнем дерева становится папка без родителя.
    /// </summary>
    /// <param name="name">Наименование папки.</param>
    /// <param name="parentId">Идентификатор родительской папки.</param>
    /// <param name="note">Пояснение к папке.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданная папка либо текст нарушения.</returns>
    public async Task<Result<Folder>> CreateAsync(
        string name,
        long? parentId = null,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Folder>.Failure("Наименование папки не задано.");
        }

        var папка = new Folder
        {
            Name = name.Trim(),
            ParentId = parentId,
            Note = note,
        };

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _папки.AddAsync(папка, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Folder>.Success(папка);
    }

    /// <summary>
    /// Переименовывает папку.
    /// </summary>
    /// <param name="folderId">Идентификатор папки.</param>
    /// <param name="name">Новое наименование папки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Переименованная папка либо текст нарушения.</returns>
    public async Task<Result<Folder>> RenameAsync(
        long folderId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Folder>.Failure("Наименование папки не задано.");
        }

        var папка = await _папки.GetByIdAsync(folderId, cancellationToken).ConfigureAwait(false);

        if (папка is null)
        {
            return Result<Folder>.Failure($"Папка с идентификатором {folderId} не найдена.");
        }

        папка.Name = name.Trim();

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _папки.UpdateAsync(папка, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Folder>.Success(папка);
    }

    /// <summary>
    /// Перемещает папку в дереве: назначает ей нового родителя. Перемещение
    /// папки внутрь её собственного потомка пресекается (ТЗ §3.1.11).
    /// </summary>
    /// <param name="folderId">Идентификатор перемещаемой папки.</param>
    /// <param name="parentId">Идентификатор нового родителя; <see langword="null"/> — корень.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Перемещённая папка либо текст нарушения.</returns>
    public async Task<Result<Folder>> MoveAsync(
        long folderId,
        long? parentId,
        CancellationToken cancellationToken = default)
    {
        var папка = await _папки.GetByIdAsync(folderId, cancellationToken).ConfigureAwait(false);

        if (папка is null)
        {
            return Result<Folder>.Failure($"Папка с идентификатором {folderId} не найдена.");
        }

        var дерево = await _папки.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var назначение = FolderHierarchy.AssignParent(папка, parentId, дерево);

        if (назначение.IsFailure)
        {
            return Result<Folder>.Failure(назначение.Error!);
        }

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _папки.UpdateAsync(папка, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Folder>.Success(папка);
    }

    /// <summary>
    /// Возвращает счётчик вложенности папки — число писем, отнесённых к ней
    /// через <c>mails.folder_id</c> (ТЗ §8.1, функция 22).
    /// </summary>
    /// <param name="folderId">Идентификатор папки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число писем в папке.</returns>
    public async Task<int> CountMailsAsync(long folderId, CancellationToken cancellationToken = default)
        => await _папки.CountMailsAsync(folderId, cancellationToken).ConfigureAwait(false);
}
