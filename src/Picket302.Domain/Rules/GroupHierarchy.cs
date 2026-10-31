using Picket302.Domain.Common;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Rules;

/// <summary>
/// Правила иерархии категорий адресатов (ТЗ §8.12, функция 154: «Категории
/// адресатов: дерево и операции»).
/// </summary>
/// <remarks>
/// Категории образуют дерево через ссылку <c>groups.parent_id</c>. Циклическая
/// ссылка (категория внутри самой себя либо внутри собственного потомка)
/// пресекается: обход родителей защищён от повторного посещения узлов, поэтому
/// проверка завершается отказом и при уже существующем цикле в дереве.
/// </remarks>
public static class GroupHierarchy
{
    /// <summary>
    /// Определяет, создаст ли назначение родителя цикл в дереве категорий.
    /// </summary>
    /// <param name="groupId">Идентификатор категории.</param>
    /// <param name="parentId">Предполагаемый родитель; <see langword="null"/> — корень.</param>
    /// <param name="groups">Категории справочника.</param>
    /// <returns><see langword="true"/>, если цикл возникнет.</returns>
    public static bool WouldCreateCycle(long groupId, long? parentId, IEnumerable<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        if (parentId is null)
        {
            return false;
        }

        if (parentId.Value == groupId)
        {
            return true;
        }

        var поИдентификатору = groups.ToDictionary(группа => группа.Id);

        var текущий = parentId.Value;
        var посещено = new HashSet<long> { groupId };

        while (true)
        {
            if (!посещено.Add(текущий))
            {
                // Узел посещён повторно — в дереве уже есть цикл.
                return true;
            }

            if (!поИдентификатору.TryGetValue(текущий, out var группа))
            {
                return false;
            }

            if (группа.ParentId is null)
            {
                return false;
            }

            текущий = группа.ParentId.Value;
        }
    }

    /// <summary>
    /// Назначает родителя категории, отклоняя попытку создать цикл.
    /// </summary>
    /// <param name="group">Изменяемая категория.</param>
    /// <param name="parentId">Назначаемый родитель; <see langword="null"/> — корень.</param>
    /// <param name="groups">Категории справочника.</param>
    /// <returns>
    /// Успешный результат с изменённой категорией либо текст нарушения.
    /// </returns>
    public static Result<Group> AssignParent(Group group, long? parentId, IEnumerable<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(groups);

        if (WouldCreateCycle(group.Id, parentId, groups))
        {
            return Result<Group>.Failure(
                "Циклическая ссылка в дереве категорий запрещена — категория не может быть вложена в саму себя.");
        }

        group.ParentId = parentId;

        return Result<Group>.Success(group);
    }

    /// <summary>
    /// Проверяет наличие цикла в дереве категорий.
    /// </summary>
    /// <param name="groups">Категории справочника.</param>
    /// <returns><see langword="true"/>, если цикл есть.</returns>
    public static bool ContainsCycle(IEnumerable<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var поИдентификатору = groups.ToDictionary(группа => группа.Id);

        foreach (var группа in поИдентификатору.Values)
        {
            var посещено = new HashSet<long>();
            long? текущий = группа.Id;

            while (текущий is not null)
            {
                if (!посещено.Add(текущий.Value))
                {
                    return true;
                }

                if (!поИдентификатору.TryGetValue(текущий.Value, out var узел) || узел.ParentId is null)
                {
                    break;
                }

                текущий = узел.ParentId.Value;
            }
        }

        return false;
    }
}
