using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;
using Microsoft.Data.Sqlite;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий групп на SQLite (ТЗ §3.1.2).
/// </summary>
/// <remarks>
/// Правило «ровно одна группа по умолчанию» обеспечивается частичным уникальным
/// индексом <c>ux_groups_single_default</c> в схеме БД, транзакционной
/// установкой признака в этом репозитории и доменной политикой
/// <see cref="DefaultGroupPolicy"/>: удалить группу по умолчанию и снять с неё
/// признак обычным изменением нельзя.
/// </remarks>
public sealed class GroupRepository : IGroupRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий групп.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public GroupRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(Group group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO groups (name, note, is_default, is_closed, sort_order, parent_id)
            VALUES (@Name, @Note, @IsDefault, @IsClosed, @SortOrder, @ParentId);
            SELECT last_insert_rowid();
            """,
            new
            {
                group.Name,
                group.Note,
                group.IsDefault,
                group.IsClosed,
                group.SortOrder,
                group.ParentId,
            },
            cancellationToken).ConfigureAwait(false);

        group.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<Group?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<Group>(
            "SELECT * FROM groups WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Group>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var группы = await _сессия.QueryAsync<Group>(
            "SELECT * FROM groups ORDER BY sort_order, name, id;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return группы.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(Group group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var сохранённая = await GetByIdAsync(group.Id, cancellationToken).ConfigureAwait(false);

        if (сохранённая is null)
        {
            return 0;
        }

        // Признак группы по умолчанию нельзя снять обычным изменением карточки:
        // смена группы по умолчанию выполняется только через SetDefaultAsync,
        // иначе в справочнике не останется ни одной группы по умолчанию (§3.1.2).
        var проверка = DefaultGroupPolicy.ValidateDefaultFlag(сохранённая, group);

        if (проверка.IsFailure)
        {
            throw new InvalidOperationException(проверка.Error!);
        }

        return await _сессия.ExecuteAsync(
            """
            UPDATE groups SET name = @Name, note = @Note, is_default = @IsDefault,
                is_closed = @IsClosed, sort_order = @SortOrder, parent_id = @ParentId
            WHERE id = @Id;
            """,
            new
            {
                group.Name,
                group.Note,
                group.IsDefault,
                group.IsClosed,
                group.SortOrder,
                group.ParentId,
                group.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var группа = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (группа is null)
        {
            return false;
        }

        // Группу по умолчанию удалить нельзя: иначе в справочнике не останется
        // ни одной группы по умолчанию (§3.1.2).
        var проверка = DefaultGroupPolicy.ValidateRemoval(группа);

        if (проверка.IsFailure)
        {
            throw new InvalidOperationException(проверка.Error!);
        }

        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM groups WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <inheritdoc />
    public async Task<Group?> GetDefaultAsync(CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<Group>(
            "SELECT * FROM groups WHERE is_default = 1 ORDER BY id LIMIT 1;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> SetDefaultAsync(long groupId, CancellationToken cancellationToken = default)
    {
        var существует = await _сессия.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM groups WHERE id = @Id;",
            new { Id = groupId },
            cancellationToken).ConfigureAwait(false);

        if (существует == 0)
        {
            return false;
        }

        // Снятие признака выполняется раньше установки: частичный уникальный
        // индекс запрещает наличие двух групп по умолчанию одновременно.
        if (_сессия.Transaction is null)
        {
            using var транзакция = _сессия.Connection.BeginTransaction();
            await ВыполнитьНазначениеAsync(groupId, транзакция, cancellationToken).ConfigureAwait(false);
            await транзакция.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        await ВыполнитьНазначениеAsync(groupId, _сессия.Transaction, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Снимает признак группы по умолчанию со всех групп и выставляет его
    /// указанной группе.
    /// </summary>
    /// <param name="groupId">Идентификатор группы по умолчанию.</param>
    /// <param name="транзакция">Транзакция выполнения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения назначения.</returns>
    private async Task ВыполнитьНазначениеAsync(
        long groupId,
        SqliteTransaction транзакция,
        CancellationToken cancellationToken)
    {
        await _сессия.ExecuteInTransactionAsync(
            "UPDATE groups SET is_default = 0 WHERE is_default = 1 AND id <> @Id;",
            new { Id = groupId },
            транзакция,
            cancellationToken).ConfigureAwait(false);

        await _сессия.ExecuteInTransactionAsync(
            "UPDATE groups SET is_default = 1 WHERE id = @Id;",
            new { Id = groupId },
            транзакция,
            cancellationToken).ConfigureAwait(false);
    }
}
