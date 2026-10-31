using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий папок писем на SQLite (ТЗ §3.1.11).
/// </summary>
public sealed class FolderRepository : IFolderRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий папок.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public FolderRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        await ПроверитьРодителяAsync(folder, null, cancellationToken).ConfigureAwait(false);

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO folders (name, parent_id, note, sort_order)
            VALUES (@Name, @ParentId, @Note, @SortOrder);
            SELECT last_insert_rowid();
            """,
            new
            {
                folder.Name,
                folder.ParentId,
                folder.Note,
                folder.SortOrder,
            },
            cancellationToken).ConfigureAwait(false);

        folder.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<Folder?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<Folder>(
            "SELECT * FROM folders WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Folder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var папки = await _сессия.QueryAsync<Folder>(
            "SELECT * FROM folders ORDER BY sort_order, name, id;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return папки.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        await ПроверитьРодителяAsync(folder, folder.Id, cancellationToken).ConfigureAwait(false);

        return await _сессия.ExecuteAsync(
            "UPDATE folders SET name = @Name, parent_id = @ParentId, note = @Note, sort_order = @SortOrder WHERE id = @Id;",
            new
            {
                folder.Name,
                folder.ParentId,
                folder.Note,
                folder.SortOrder,
                folder.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM folders WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <inheritdoc />
    public async Task<int> CountMailsAsync(long id, CancellationToken cancellationToken = default)
    {
        var количество = await _сессия.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM mails WHERE folder_id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return (int)количество;
    }

    /// <summary>
    /// Проверяет корректность родителя папки: родитель должен существовать, а
    /// циклическая ссылка (папка внутри самой себя либо внутри собственного
    /// потомка) пресекается валидатором домена (ТЗ §3.1.11).
    /// </summary>
    /// <param name="folder">Сохраняемая папка.</param>
    /// <param name="идентификатор">Идентификатор сохраняемой папки; <see langword="null"/> — папка создаётся.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <exception cref="InvalidOperationException">
    /// Родитель не найден либо его назначение создаёт циклическую ссылку.
    /// </exception>
    private async Task ПроверитьРодителяAsync(
        Folder folder,
        long? идентификатор,
        CancellationToken cancellationToken)
    {
        if (folder.ParentId is null)
        {
            return;
        }

        if (идентификатор is not null && folder.ParentId.Value == идентификатор.Value)
        {
            throw new InvalidOperationException(
                "Недопустимое перемещение папки: родитель находится в её собственном поддереве.");
        }

        var все = await GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (!все.Any(папка => папка.Id == folder.ParentId!.Value))
        {
            throw new InvalidOperationException(
                $"Родительская папка с идентификатором {folder.ParentId.Value} не найдена.");
        }

        if (идентификатор is not null
            && FolderHierarchy.WouldCreateCycle(идентификатор.Value, folder.ParentId, все))
        {
            throw new InvalidOperationException(
                "Недопустимое перемещение папки: родитель находится в её собственном поддереве.");
        }
    }
}
