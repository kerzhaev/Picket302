using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;

namespace Picket302.Application.Services;

/// <summary>
/// Дерево категорий адресатов: создание, переименование, удаление и перенос
/// записей (ТЗ §8.12, функция 154: «Категории адресатов: дерево и операции»).
/// </summary>
/// <remarks>
/// Удаление категории переносит её записи в «Неразобранные» и не удаляет
/// контрагентов. Удаление самой «Неразобранные» блокируется.
/// </remarks>
public sealed class CategoryTreeService
{
    private readonly IGroupRepository _группы;
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт службу дерева категорий.</summary>
    /// <param name="группы">Репозиторий групп.</param>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public CategoryTreeService(
        IGroupRepository группы,
        ICounterpartyRepository контрагенты,
        IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(группы);
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _группы = группы;
        _контрагенты = контрагенты;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Создаёт категорию, в том числе вложенную (ТЗ §8.12, функция 154).
    /// </summary>
    /// <param name="name">Наименование категории.</param>
    /// <param name="parentId">Родительская категория; <see langword="null"/> — корень.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданная категория либо текст нарушения.</returns>
    public async Task<Result<Category>> CreateCategoryAsync(
        string name,
        long? parentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Category>.Failure("Наименование категории не задано.");
        }

        var имя = name.Trim();
        var все = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (все.Any(группа => string.Equals(группа.Name, имя, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<Category>.Failure($"Категория «{имя}» уже существует.");
        }

        if (parentId is not null)
        {
            var родитель = все.FirstOrDefault(группа => группа.Id == parentId.Value);

            if (родитель is null)
            {
                return Result<Category>.Failure(
                    $"Родительская категория с идентификатором {parentId} не найдена.");
            }

            if (родитель.IsClosed)
            {
                return Result<Category>.Failure(
                    $"Категория «{родитель.Name}» закрыта — вложенные категории не создаются.");
            }
        }

        var группа = new Group
        {
            Name = имя,
            ParentId = parentId,
            SortOrder = все.Count,
        };

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _группы.AddAsync(группа, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Category>.Success(
            new Category(группа.Id, группа.Name, группа.ParentId, группа.IsDefault, группа.IsClosed, группа.SortOrder, 0));
    }

    /// <summary>
    /// Переименовывает категорию (ТЗ §8.12, функция 154).
    /// </summary>
    /// <param name="id">Идентификатор категории.</param>
    /// <param name="newName">Новое наименование.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Переименованная категория либо текст нарушения.</returns>
    public async Task<Result<Category>> RenameCategoryAsync(
        long id,
        string newName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Result<Category>.Failure("Наименование категории не задано.");
        }

        var группа = await _группы.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (группа is null)
        {
            return Result<Category>.Failure($"Категория с идентификатором {id} не найдена.");
        }

        if (группа.IsDefault)
        {
            return Result<Category>.Failure(
                $"Системную категорию «{группа.Name}» переименовать нельзя.");
        }

        var имя = newName.Trim();
        var все = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (все.Any(сущ =>
            сущ.Id != id && string.Equals(сущ.Name, имя, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<Category>.Failure($"Категория «{имя}» уже существует.");
        }

        группа.Name = имя;

        await _группы.UpdateAsync(группа, cancellationToken).ConfigureAwait(false);

        return Result<Category>.Success(
            new Category(группа.Id, группа.Name, группа.ParentId, группа.IsDefault, группа.IsClosed, группа.SortOrder, 0));
    }

    /// <summary>
    /// Удаляет категорию, перенося её записи в «Неразобранные»
    /// (ТЗ §8.12, функция 154).
    /// </summary>
    /// <param name="id">Идентификатор категории.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число перенесённых записей либо текст нарушения.</returns>
    public async Task<Result<int>> DeleteCategoryAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var группа = await _группы.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (группа is null)
        {
            return Result<int>.Failure($"Категория с идентификатором {id} не найдена.");
        }

        if (группа.IsDefault)
        {
            return Result<int>.Failure(
                "Удаление категории «Неразобранные» блокируется — сначала назначьте статус "
                + "категории по умолчанию другой категории.");
        }

        var поУмолчанию = await _группы.GetDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (поУмолчанию is null)
        {
            return Result<int>.Failure(
                "В справочнике отсутствует категория по умолчанию «Неразобранные».");
        }

        var все = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (все.Any(сущ => сущ.ParentId == id))
        {
            return Result<int>.Failure(
                $"Категория «{группа.Name}» содержит вложенные категории — удалите их либо перенесите.");
        }

        var записи = await _контрагенты.GetByGroupAsync(id, cancellationToken).ConfigureAwait(false);

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var запись in записи)
            {
                запись.GroupId = поУмолчанию.Id;
                await _контрагенты.UpdateAsync(запись, cancellationToken).ConfigureAwait(false);
            }

            var удалена = await _группы.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

            if (!удалена)
            {
                await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return Result<int>.Failure("Не удалось удалить категорию.");
            }

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<int>.Success(записи.Count);
    }

    /// <summary>
    /// Перемещает контрагента в категорию — отдельное действие
    /// «Переместить в категорию» (ТЗ §8.12, функция 154).
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="categoryId">Идентификатор категории.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Перемещённый контрагент либо текст нарушения.</returns>
    public async Task<Result<Counterparty>> MoveCounterpartyAsync(
        long counterpartyId,
        long categoryId,
        CancellationToken cancellationToken = default)
    {
        var контрагент = await _контрагенты.GetByIdAsync(counterpartyId, cancellationToken)
            .ConfigureAwait(false);

        if (контрагент is null)
        {
            return Result<Counterparty>.Failure(
                $"Контрагент с идентификатором {counterpartyId} не найден.");
        }

        var категория = await _группы.GetByIdAsync(categoryId, cancellationToken).ConfigureAwait(false);

        if (категория is null)
        {
            return Result<Counterparty>.Failure(
                $"Категория с идентификатором {categoryId} не найдена.");
        }

        if (категория.IsClosed)
        {
            return Result<Counterparty>.Failure(
                $"Категория «{категория.Name}» закрыта — новые записи в неё не принимаются.");
        }

        контрагент.GroupId = categoryId;

        await _контрагенты.UpdateAsync(контрагент, cancellationToken).ConfigureAwait(false);

        return Result<Counterparty>.Success(контрагент);
    }

    /// <summary>
    /// Возвращает дерево категорий — плоский перечень с указанием родителя
    /// (ТЗ §8.12, функция 154: «Дерево категорий на вкладке «Адресная книга»»).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Перечень категорий.</returns>
    public async Task<IReadOnlyList<Category>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        var группы = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var категории = new List<Category>(группы.Count);

        foreach (var группа in группы.OrderBy(группа => группа.SortOrder).ThenBy(группа => группа.Name))
        {
            var записи = await ПолучитьЧислоЗаписейAsync(группа.Id, cancellationToken).ConfigureAwait(false);

            категории.Add(new Category(
                группа.Id,
                группа.Name,
                группа.ParentId,
                группа.IsDefault,
                группа.IsClosed,
                группа.SortOrder,
                записи));
        }

        return категории;
    }

    /// <summary>
    /// Назначает категорию единственной категорией по умолчанию
    /// (ТЗ §8.11, функция 138).
    /// </summary>
    /// <param name="id">Идентификатор категории.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если назначение выполнено.</returns>
    public async Task<bool> SetDefaultCategoryAsync(long id, CancellationToken cancellationToken = default)
    {
        var группа = await _группы.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (группа is null)
        {
            return false;
        }

        return await _группы.SetDefaultAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Проверяет, что назначение родителя не создаст цикл в дереве категорий
    /// (ТЗ §8.12, функция 154: «циклы вложенности пресекаются»).
    /// </summary>
    /// <param name="id">Идентификатор категории.</param>
    /// <param name="parentId">Назначаемый родитель.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если цикл не возникнет.</returns>
    public async Task<bool> CanMoveAsync(long id, long? parentId, CancellationToken cancellationToken = default)
    {
        var группы = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return !GroupHierarchy.WouldCreateCycle(id, parentId, группы);
    }

    private async Task<int> ПолучитьЧислоЗаписейAsync(long groupId, CancellationToken cancellationToken)
    {
        var записи = await _контрагенты.GetByGroupAsync(groupId, cancellationToken).ConfigureAwait(false);
        return записи.Count;
    }
}
