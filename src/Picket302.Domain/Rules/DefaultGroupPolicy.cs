using Picket302.Domain.Common;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Rules;

/// <summary>
/// Правило «ровно одна группа по умолчанию» (ТЗ §3.1.2: «Ровно одна системная
/// группа по умолчанию («Неразобранные»)»; §3.1.19).
/// </summary>
public static class DefaultGroupPolicy
{
    /// <summary>
    /// Проверяет, что среди групп ровно одна отмечена как группа по умолчанию.
    /// </summary>
    /// <param name="groups">Перечень групп.</param>
    /// <returns>Успешный результат либо текст нарушения.</returns>
    public static Result<IReadOnlyList<Group>> Validate(IEnumerable<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var список = groups.ToList();
        var количество = список.Count(группа => группа.IsDefault);

        if (количество == 1)
        {
            return Result<IReadOnlyList<Group>>.Success(список);
        }

        return Result<IReadOnlyList<Group>>.Failure(
            количество == 0
                ? "Должна существовать ровно одна группа по умолчанию — сейчас таких групп нет."
                : $"Должна существовать ровно одна группа по умолчанию — сейчас их {количество}.");
    }

    /// <summary>
    /// Проверяет, что удаление группы не нарушит правило «ровно одна группа по
    /// умолчанию» (ТЗ §3.1.2).
    /// </summary>
    /// <remarks>
    /// Группу по умолчанию удалить нельзя: единственный способ сменить группу
    /// по умолчанию — назначить статус группы по умолчанию другой группе.
    /// </remarks>
    /// <param name="group">Удаляемая группа.</param>
    /// <returns>Успешный результат с удаляемой группой либо текст нарушения.</returns>
    public static Result<Group> ValidateRemoval(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group.IsDefault
            ? Result<Group>.Failure(
                "Группу по умолчанию удалить нельзя — сначала назначьте статус группы по умолчанию другой группе.")
            : Result<Group>.Success(group);
    }

    /// <summary>
    /// Проверяет, что изменение группы не снимает признак группы по умолчанию
    /// (ТЗ §3.1.2).
    /// </summary>
    /// <remarks>
    /// Смена группы по умолчанию выполняется только назначением
    /// (<see cref="ApplyDefault"/>): обычное изменение карточки группы не должно
    /// оставлять справочник без группы по умолчанию.
    /// </remarks>
    /// <param name="persisted">Группа, сохранённая в базе данных.</param>
    /// <param name="updated">Группа с сохраняемыми изменениями.</param>
    /// <returns>Успешный результат с изменяемой группой либо текст нарушения.</returns>
    public static Result<Group> ValidateDefaultFlag(Group persisted, Group updated)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        ArgumentNullException.ThrowIfNull(updated);

        if (persisted.IsDefault && !updated.IsDefault)
        {
            return Result<Group>.Failure(
                "Нельзя снять признак группы по умолчанию обычным изменением — "
                + "назначьте статус группы по умолчанию другой группе.");
        }

        return Result<Group>.Success(updated);
    }

    /// <summary>
    /// Назначает группу единственной группой по умолчанию: снимает признак со
    /// всех остальных групп и выставляет его у указанной.
    /// </summary>
    /// <param name="target">Группа, становимая группой по умолчанию.</param>
    /// <param name="groups">Перечень групп справочника.</param>
    /// <returns>
    /// Успешный результат с новым перечнем групп либо текст нарушения.
    /// </returns>
    public static Result<IReadOnlyList<Group>> ApplyDefault(Group target, IEnumerable<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(groups);

        var список = groups.ToList();

        if (!список.Any(группа => группа.Id == target.Id))
        {
            return Result<IReadOnlyList<Group>>.Failure(
                "Указанная группа не найдена в справочнике групп.");
        }

        foreach (var группа in список)
        {
            группа.IsDefault = группа.Id == target.Id;
        }

        target.IsDefault = true;

        return Result<IReadOnlyList<Group>>.Success(список);
    }
}
