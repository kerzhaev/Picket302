using Picket302.Domain.Contracts;
using Picket302.Domain.Repositories;
using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Обслуживание базы: выборочная очистка по областям данных
/// (ТЗ §8.11, функция 137; §8.12, функция 153: «Демо-база и очистка базы»).
/// </summary>
public sealed class DatabaseMaintenanceRepository : IDatabaseMaintenanceRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий обслуживания базы.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public DatabaseMaintenanceRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<int> ClearCounterpartyCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var число = await _сессия
            .ExecuteScalarAsync<long>("SELECT COUNT(*) FROM groups;", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Самоссылающийся ключ parent_id не имеет ON DELETE-правила: ссылки
        // на родительские категории обнуляются до удаления (F-B7).
        await _сессия.ExecuteAsync(
            "UPDATE groups SET parent_id = NULL WHERE parent_id IS NOT NULL;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _сессия.ExecuteAsync(
            "DELETE FROM groups;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Инвариант «ровно одна группа по умолчанию» сохраняется: «Неразобранные»
        // создаётся заново (ТЗ §8.11, функция 138).
        await _сессия.ExecuteAsync(
            """
            INSERT INTO groups (name, note, is_default, is_closed, parent_id, sort_order)
            VALUES (@Name, NULL, 1, 0, NULL, 0);
            """,
            new { Name = Domain.Entities.Group.DefaultGroupName },
            cancellationToken).ConfigureAwait(false);

        return (int)число;
    }

    /// <inheritdoc />
    public async Task<int> ClearDistributionsAsync(CancellationToken cancellationToken = default)
    {
        var число = await _сессия
            .ExecuteScalarAsync<long>("SELECT COUNT(*) FROM distributions;", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Письма рассылки удаляются вместе с рассылкой: адресная книга не
        // затрагивается (ТЗ §8.12, функция 153: «очистка «Базы рассылок» не
        // трогает адресную книгу»).
        await _сессия.ExecuteAsync(
            "DELETE FROM mails WHERE distribution_id IS NOT NULL;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _сессия.ExecuteAsync(
            "DELETE FROM distributions;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (int)число;
    }

    /// <inheritdoc />
    public async Task<int> ClearCounterpartiesAsync(CancellationToken cancellationToken = default)
    {
        var число = await _сессия
            .ExecuteScalarAsync<long>("SELECT COUNT(*) FROM counterparties;", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // У письма получатель обязателен (mails.counterparty_id NOT NULL), поэтому
        // письма удаляются вместе с контрагентами. Ссылки без ON DELETE-правила
        // обнуляются до удаления (F-B6): sender_id рассылок и counterparty_id
        // описей остаются со значениями NULL (ТЗ §8.12, функция 153: очистка
        // адресной книги не удаляет рассылки и описи — вопрос подтверждения
        // каскадного удаления см. в комментариях к промпту фикса).
        await _сессия.ExecuteAsync(
            "UPDATE distributions SET sender_id = NULL;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _сессия.ExecuteAsync(
            "UPDATE inventories SET counterparty_id = NULL;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _сессия.ExecuteAsync(
            "DELETE FROM mails;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _сессия.ExecuteAsync(
            "DELETE FROM counterparties;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ПересчитатьКонвертыAsync(cancellationToken).ConfigureAwait(false);

        return (int)число;
    }

    /// <inheritdoc />
    public async Task<int> ClearSentMailsAsync(CancellationToken cancellationToken = default)
    {
        var число = await _сессия
            .ExecuteScalarAsync<long>("SELECT COUNT(*) FROM mails;", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _сессия.ExecuteAsync(
            "DELETE FROM print_history;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await _сессия.ExecuteAsync(
            "DELETE FROM mails;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ПересчитатьКонвертыAsync(cancellationToken).ConfigureAwait(false);

        return (int)число;
    }

    /// <inheritdoc />
    public async Task<int> CountScopeAsync(
        ClearScope scope,
        CancellationToken cancellationToken = default)
    {
        var sql = scope switch
        {
            ClearScope.CounterpartyCategories => "SELECT COUNT(*) FROM groups;",
            ClearScope.Distributions => "SELECT COUNT(*) FROM distributions;",
            ClearScope.Counterparties => "SELECT COUNT(*) FROM counterparties;",
            ClearScope.SentMails => "SELECT COUNT(*) FROM mails;",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Неизвестная область очистки."),
        };

        var число = await _сессия
            .ExecuteScalarAsync<long>(sql, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (int)число;
    }

    /// <summary>
    /// Пересчитывает счётчик «Всего необходимо конвертов» у всех рассылок.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ПересчитатьКонвертыAsync(CancellationToken cancellationToken)
    {
        await _сессия.ExecuteAsync(
            """
            UPDATE distributions
            SET envelopes_required = (
                SELECT COALESCE(SUM(m.copy_count), 0) FROM mails m WHERE m.distribution_id = distributions.id
            );
            """,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
