using Microsoft.Data.Sqlite;
using Picket302.Domain;

namespace Picket302.Infrastructure.Data;

/// <summary>
/// Сессия доступа к базе данных SQLite: соединение и текущая транзакция.
/// </summary>
public interface ISqliteSession
{
    /// <summary>Соединение с базой данных.</summary>
    SqliteConnection Connection { get; }

    /// <summary>Текущая транзакция; <see langword="null"/> — вне транзакции.</summary>
    SqliteTransaction? Transaction { get; }
}

/// <summary>
/// Единица работы над базой данных SQLite (ТЗ §3.1.19, §10.2).
/// </summary>
/// <remarks>
/// Репозитории работают через общую сессию: соединение открывается один раз,
/// транзакция единая для всех операций внутри неё.
/// </remarks>
public sealed class SqliteUnitOfWork : IUnitOfWork, ISqliteSession, IAsyncDisposable
{
    private readonly SqliteConnection _соединение;
    private bool _освобождено;

    /// <summary>Создаёт единицу работы с указанной строкой подключения.</summary>
    /// <param name="connectionString">Строка подключения SQLite.</param>
    public SqliteUnitOfWork(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        DapperConfiguration.Применить();

        _соединение = new SqliteConnection(connectionString);
        _соединение.Open();
    }

    /// <summary>Создаёт единицу работы с указанным соединением.</summary>
    /// <param name="connection">Открытое соединение с базой данных.</param>
    public SqliteUnitOfWork(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _соединение = connection;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }
    }

    /// <inheritdoc />
    public SqliteConnection Connection => _соединение;

    /// <inheritdoc />
    public SqliteTransaction? Transaction { get; private set; }

    /// <inheritdoc />
    public bool InTransaction => Transaction is not null;

    /// <inheritdoc />
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_освобождено, this);

        if (InTransaction)
        {
            throw new InvalidOperationException("Транзакция уже открыта.");
        }

        Transaction = (SqliteTransaction)await _соединение.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_освобождено, this);

        if (Transaction is null)
        {
            throw new InvalidOperationException("Транзакция не открыта.");
        }

        await Transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await Transaction.DisposeAsync().ConfigureAwait(false);
        Transaction = null;
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_освобождено, this);

        if (Transaction is null)
        {
            return;
        }

        await Transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        await Transaction.DisposeAsync().ConfigureAwait(false);
        Transaction = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_освобождено)
        {
            return;
        }

        _освобождено = true;

        if (Transaction is not null)
        {
            await Transaction.RollbackAsync().ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
            Transaction = null;
        }

        await _соединение.DisposeAsync().ConfigureAwait(false);
    }
}
