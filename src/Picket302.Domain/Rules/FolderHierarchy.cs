using Picket302.Domain.Common;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Rules;

/// <summary>
/// Проверка иерархии папок писем (ТЗ §3.1.11: «Ограничение: циклические ссылки
/// parent_id пресекаются триггером/валидатором домена»).
/// </summary>
public static class FolderHierarchy
{
    /// <summary>
    /// Проверяет, не создаст ли установка родителя циклическую ссылку.
    /// </summary>
    /// <remarks>
    /// Если в исходном дереве уже есть циклическая ссылка, перемещение
    /// отклоняется: обход цепочки родителей защищён от повторного посещения
    /// узлов, поэтому проверка всегда завершается.
    /// </remarks>
    /// <param name="folderId">Идентификатор перемещаемой папки.</param>
    /// <param name="parentId">Предполагаемый родитель; <see langword="null"/> — корень.</param>
    /// <param name="folders">Перечень папок.</param>
    /// <returns><see langword="true"/>, если цикл возникнет.</returns>
    public static bool WouldCreateCycle(long folderId, long? parentId, IEnumerable<Folder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        if (parentId is null)
        {
            return false;
        }

        if (parentId.Value == folderId)
        {
            return true;
        }

        var родители = folders.ToDictionary(папка => папка.Id, папка => папка.ParentId);
        var посещённые = new HashSet<long>();
        var текущий = parentId;

        while (текущий is not null)
        {
            if (текущий.Value == folderId)
            {
                return true;
            }

            // Повторное посещение узла означает, что в дереве уже есть цикл:
            // перемещение недопустимо, обход прерывается.
            if (!посещённые.Add(текущий.Value))
            {
                return true;
            }

            if (!родители.TryGetValue(текущий.Value, out var следующий))
            {
                return false;
            }

            текущий = следующий;
        }

        return false;
    }

    /// <summary>
    /// Проверяет, содержит ли дерево папок циклическую ссылку.
    /// </summary>
    /// <param name="folders">Перечень папок.</param>
    /// <returns><see langword="true"/>, если в дереве есть цикл.</returns>
    public static bool ContainsCycle(IEnumerable<Folder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var родители = folders.ToDictionary(папка => папка.Id, папка => папка.ParentId);

        foreach (var папка in родители.Keys)
        {
            var посещённые = new HashSet<long>();
            var текущий = (long?)папка;

            while (текущий is not null)
            {
                if (!посещённые.Add(текущий.Value))
                {
                    return true;
                }

                if (!родители.TryGetValue(текущий.Value, out var следующий))
                {
                    break;
                }

                текущий = следующий;
            }
        }

        return false;
    }

    /// <summary>
    /// Назначает папке родителя, пресекая создание циклической ссылки.
    /// </summary>
    /// <param name="folder">Перемещаемая папка.</param>
    /// <param name="parentId">Предполагаемый родитель; <see langword="null"/> — корень.</param>
    /// <param name="folders">Перечень папок.</param>
    /// <returns>Успешный результат с папкой либо текст нарушения.</returns>
    public static Result<Folder> AssignParent(Folder folder, long? parentId, IEnumerable<Folder> folders)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(folders);

        var дерево = folders as IReadOnlyList<Folder> ?? folders.ToList();

        if (ContainsCycle(дерево))
        {
            return Result<Folder>.Failure(
                "Недопустимое перемещение папки: дерево папок уже содержит циклическую ссылку.");
        }

        if (WouldCreateCycle(folder.Id, parentId, дерево))
        {
            return Result<Folder>.Failure(
                "Недопустимое перемещение папки: родитель находится в её собственном поддереве.");
        }

        folder.ParentId = parentId;

        return Result<Folder>.Success(folder);
    }
}
