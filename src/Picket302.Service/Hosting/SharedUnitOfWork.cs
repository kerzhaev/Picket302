using Microsoft.Data.Sqlite;
using Picket302.Domain;
using Picket302.Infrastructure.Data;

namespace Picket302.Service.Hosting;

/// <summary>
/// Единица работы с вложенными транзакциями для серверной службы.
/// </summary>
/// <remarks>
/// <para>
/// Серверная команда исполняется <b>одной</b> короткой транзакцией: команда
/// открывает транзакцию, а прикладные службы, работающие внутри неё, своими
/// транзакциями не управляют. Без этого обёртка фиксация результата
/// операции в журнале идемпотентности оказалась бы вне транзакции команды —
/// повтор прерванного запроса мог бы выполнить письмо второй раз.
/// </para>
/// <para>
/// Поведение вложенности:
/// <list type="bullet">
/// <item><description>вложенный <c>Begin</c> не открывает вторую транзакцию —
/// SQLite их не поддерживает;</description></item>
/// <item><description>вложенный <c>Commit</c> не фиксирует: фиксация
/// выполняется внешней транзакцией;</description></item>
/// <item><description><c>Rollback</c> откатывает всю транзакцию: прикладная
/// служба откатывает всю команду, а не её часть.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class SharedUnitOfWork : ISqliteSession
{
    private readonly SqliteUnitOfWork _сессия;
    private int _глубина;
    private bool _транзакцияОткрытаОбёрткой;

    /// <summary>
    /// Создаёт обёртку над сессией базы данных.
    /// </summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public SharedUnitOfWork(SqliteUnitOfWork сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _сессия = сессия;
    }

    /// <inheritdoc />
    public SqliteConnection Connection => _сессия.Connection;

    /// <inheritdoc />
    public SqliteTransaction? Transaction => _сессия.Transaction;

    /// <inheritdoc />
    public bool InTransaction => _сессия.InTransaction;

    /// <inheritdoc />
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_сессия.InTransaction)
        {
            // Транзакцию открыла команда: прикладная служба вкладывается в неё
            // и не управляет фиксацией.
            _глубина++;
            return;
        }

        await _сессия.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        _транзакцияОткрытаОбёрткой = true;
        _глубина = 1;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_глубина > 1)
        {
            _глубина--;
            return;
        }

        _глубина = 0;

        if (!_транзакцияОткрытаОбёрткой)
        {
            // Фиксацию выполняет внешняя транзакция команды.
            return;
        }

        _транзакцияОткрытаОбёрткой = false;

        if (!_сессия.InTransaction)
        {
            return;
        }

        await _сессия.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        _глубина = 0;
        _транзакцияОткрытаОбёрткой = false;

        if (!_сессия.InTransaction)
        {
            return;
        }

        await _сессия.RollbackAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает внешнюю транзакцию команды: прикладные службы внутри неё
    /// своими транзакциями не управляют.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения открытия транзакции.</returns>
    public Task ОткрытьТранзакциюКомандыAsync(CancellationToken cancellationToken = default)
        => _сессия.BeginTransactionAsync(cancellationToken);

    /// <summary>
    /// Фиксирует внешнюю транзакцию команды.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения фиксации.</returns>
    public Task ЗафиксироватьКомандуAsync(CancellationToken cancellationToken = default)
        => _сессия.CommitAsync(cancellationToken);

    /// <summary>
    /// Откатывает внешнюю транзакцию команды.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения отката.</returns>
    public Task ОткатитьКомандуAsync(CancellationToken cancellationToken = default)
        => _сессия.RollbackAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _сессия.DisposeAsync();
}
