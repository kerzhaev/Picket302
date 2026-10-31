using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий связей писем с номерами первичных документов на SQLite (ТЗ §3.1.15).
/// </summary>
public sealed class MailDocumentRepository : IMailDocumentRepository
{
    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий связей писем с документами.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public MailDocumentRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(MailDocument mailDocument, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailDocument);

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            """
            INSERT INTO mail_documents (mail_id, doc_number)
            VALUES (@MailId, @DocNumber);
            SELECT last_insert_rowid();
            """,
            new
            {
                mailDocument.MailId,
                mailDocument.DocNumber,
            },
            cancellationToken).ConfigureAwait(false);

        mailDocument.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailDocument>> GetByMailAsync(
        long mailId,
        CancellationToken cancellationToken = default)
    {
        var связи = await _сессия.QueryAsync<MailDocument>(
            "SELECT * FROM mail_documents WHERE mail_id = @MailId ORDER BY id;",
            new { MailId = mailId },
            cancellationToken).ConfigureAwait(false);

        return связи.ToList();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM mail_documents WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }
}
