using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;

namespace Picket302.Application.Services;

/// <summary>
/// Служба ведения групп (категорий) адресной книги (ТЗ §3.1.2, §8.1 функция 21).
/// </summary>
/// <remarks>
/// Служба обеспечивает правило «ровно одна системная группа по умолчанию»: смена
/// группы по умолчанию выполняется транзакционно, а её результат проверяется
/// доменной политикой <see cref="DefaultGroupPolicy"/>.
/// </remarks>
public sealed class GroupService
{
    private readonly IGroupRepository _группы;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт службу групп.</summary>
    /// <param name="группы">Репозиторий групп.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public GroupService(IGroupRepository группы, IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(группы);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _группы = группы;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Создаёт группу. Первая созданная группа становится группой по умолчанию,
    /// если в справочнике её ещё нет.
    /// </summary>
    /// <param name="name">Наименование группы.</param>
    /// <param name="note">Пояснение к группе.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданная группа либо текст нарушения.</returns>
    public async Task<Result<Group>> CreateAsync(
        string name,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Group>.Failure("Наименование группы не задано.");
        }

        var существующие = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (существующие.Any(группа => string.Equals(группа.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result<Group>.Failure($"Группа «{name.Trim()}» уже существует.");
        }

        var группа = new Group
        {
            Name = name.Trim(),
            Note = note,
            IsDefault = существующие.Count == 0,
        };

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _группы.AddAsync(группа, cancellationToken).ConfigureAwait(false);

            if (группа.IsDefault)
            {
                await _группы.SetDefaultAsync(группа.Id, cancellationToken).ConfigureAwait(false);
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
    /// Назначает группу единственной группой по умолчанию.
    /// </summary>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Назначенная группа либо текст нарушения.</returns>
    public async Task<Result<Group>> SetDefaultAsync(long groupId, CancellationToken cancellationToken = default)
    {
        var группа = await _группы.GetByIdAsync(groupId, cancellationToken).ConfigureAwait(false);

        if (группа is null)
        {
            return Result<Group>.Failure($"Группа с идентификатором {groupId} не найдена.");
        }

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _группы.SetDefaultAsync(groupId, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        var группы = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var проверка = DefaultGroupPolicy.Validate(группы);

        if (проверка.IsFailure)
        {
            return Result<Group>.Failure(проверка.Error!);
        }

        var обновлённая = группы.First(запись => запись.Id == groupId);

        return Result<Group>.Success(обновлённая);
    }

    /// <summary>
    /// Возвращает перечень групп и проверяет правило единственной группы
    /// по умолчанию.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Перечень групп либо текст нарушения.</returns>
    public async Task<Result<IReadOnlyList<Group>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var группы = await _группы.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var проверка = DefaultGroupPolicy.Validate(группы);

        return проверка.IsSuccess
            ? Result<IReadOnlyList<Group>>.Success(группы)
            : Result<IReadOnlyList<Group>>.Failure(проверка.Error!);
    }
}
