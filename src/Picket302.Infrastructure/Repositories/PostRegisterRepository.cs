using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий почтовых реестров на SQLite (ТЗ §3.1.13).
/// </summary>
public sealed class PostRegisterRepository : IPostRegisterRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий почтовых реестров.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public PostRegisterRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(PostRegister register, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(register);

        register.CreatedAt = register.CreatedAt == default ? DateTime.UtcNow : register.CreatedAt;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO post_registers (register_number, batch_number, sending_date, shipping_point,
                track_login, track_password_protected, track_token, mail_ids, created_at)
            VALUES (@RegisterNumber, @BatchNumber, @SendingDate, @ShippingPoint, @TrackLogin,
                @TrackPasswordProtected, @TrackToken, @MailIds, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                register.RegisterNumber,
                register.BatchNumber,
                register.SendingDate,
                register.ShippingPoint,
                register.TrackLogin,
                register.TrackPasswordProtected,
                register.TrackToken,
                register.MailIds,
                register.CreatedAt,
            },
            cancellationToken).ConfigureAwait(false);

        register.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<PostRegister?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => await _сессия.QueryFirstOrDefaultAsync<PostRegister>(
            "SELECT * FROM post_registers WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostRegister>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var реестры = await _сессия.QueryAsync<PostRegister>(
            "SELECT * FROM post_registers ORDER BY created_at, id;",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return реестры.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(PostRegister register, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(register);

        return await _сессия.ExecuteAsync(
            """
            UPDATE post_registers SET register_number = @RegisterNumber, batch_number = @BatchNumber,
                sending_date = @SendingDate, shipping_point = @ShippingPoint, track_login = @TrackLogin,
                track_password_protected = @TrackPasswordProtected, track_token = @TrackToken, mail_ids = @MailIds
            WHERE id = @Id;
            """,
            new
            {
                register.RegisterNumber,
                register.BatchNumber,
                register.SendingDate,
                register.ShippingPoint,
                register.TrackLogin,
                register.TrackPasswordProtected,
                register.TrackToken,
                register.MailIds,
                register.Id,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM post_registers WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
