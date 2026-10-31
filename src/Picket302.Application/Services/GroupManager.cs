using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;

namespace Picket302.Application.Services;

/// <summary>
/// Управление группами (категориями) адресной книги
/// (ТЗ §8.1, функция 8; §8.11, функция 138: «Категория «Неразобранные»»).
/// </summary>
public sealed class GroupManager
{
    private readonly IGroupRepository _группы;
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт менеджер групп.</summary>
    /// <param name="группы">Репозиторий групп.</param>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public GroupManager(
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
    /// Гарантирует наличие ровно одной группы по умолчанию «Неразобранные»
    /// (ТЗ §8.11, функция 138: «На чистой базе группа создаётся автоматически»).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Группа по умолчанию.</returns>
    public async Task<Group> EnsureDefaultGroupAsync(CancellationToken cancellationToken = default)
    {
        var существующая = await _группы.GetDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (существующая is not null)
        {
            return существующая;
        }

        var группа = Group.CreateDefault();

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

        return группа;
    }

    /// <summary>
    /// Создаёт группу, обеспечивая наличие ровно одной группы по умолчанию
    /// (ТЗ §8.1, функция 8).
    /// </summary>
    /// <param name="name">Наименование группы.</param>
    /// <param name="isDefault">Создать группу как группу по умолчанию.</param>
    /// <param name="isClosed">Закрыть группу для добавления новых записей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданная группа либо текст нарушения.</returns>
    public async Task<Result<Group>> CreateGroupAsync(
        string name,
        bool isDefault = false,
        bool isClosed = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Group>.Failure("Наименование группы не задано.");
        }

        var имя = name.Trim();
        var все = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (все.Any(группа => string.Equals(группа.Name, имя, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<Group>.Failure($"Группа «{имя}» уже существует.");
        }

        var группа = new Group
        {
            Name = имя,
            IsClosed = isClosed,
            SortOrder = все.Count,
        };

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _группы.AddAsync(группа, cancellationToken).ConfigureAwait(false);

            if (isDefault)
            {
                var назначение = DefaultGroupPolicy.ApplyDefault(группа, все);

                if (назначение.IsFailure)
                {
                    await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return Result<Group>.Failure(назначение.Error!);
                }

                foreach (var сущ in назначение.Value)
                {
                    await _группы.UpdateAsync(сущ, cancellationToken).ConfigureAwait(false);
                }
            }

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Group>.Success(группа);
    }

    /// <summary>
    /// Удаляет группу; удаление группы по умолчанию блокируется
    /// (ТЗ §8.1, функция 8: «Попытка удаления группы «Неразобранные»
    /// блокируется»).
    /// </summary>
    /// <param name="id">Идентификатор группы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если группа удалена.</returns>
    public async Task<Result<bool>> DeleteGroupAsync(long id, CancellationToken cancellationToken = default)
    {
        var группа = await _группы.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (группа is null)
        {
            return Result<bool>.Failure($"Группа с идентификатором {id} не найдена.");
        }

        var проверка = DefaultGroupPolicy.ValidateRemoval(группа);

        if (проверка.IsFailure)
        {
            return Result<bool>.Failure(проверка.Error!);
        }

        var записи = await _контрагенты.GetByGroupAsync(id, cancellationToken).ConfigureAwait(false);
        var поУмолчанию = await _группы.GetDefaultAsync(cancellationToken).ConfigureAwait(false);

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (поУмолчанию is not null)
            {
                foreach (var запись in записи)
                {
                    запись.GroupId = поУмолчанию.Id;
                    await _контрагенты.UpdateAsync(запись, cancellationToken).ConfigureAwait(false);
                }
            }

            var удалена = await _группы.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result<bool>.Success(удалена);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
