using System.Text.Json;
using Picket302.Application.Services;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Mail;
using Picket302.Shared.Commands;
using Picket302.Shared.Protocol;

namespace Picket302.App.Shared;

/// <summary>
/// Журнал email-рассылок в локальной базе.
/// </summary>
/// <remarks>
/// Реализация поверх <see cref="SqliteEmailLogWriter"/>: та же запись в
/// таблицу <c>email_log</c>, что и до появления общего режима.
/// </remarks>
public sealed class LocalEmailLogWriter : IEmailLogWriter
{
    private readonly SqliteEmailLogWriter _писатель;

    /// <summary>
    /// Создаёт писатель журнала над локальной сессией базы.
    /// </summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public LocalEmailLogWriter(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _писатель = new SqliteEmailLogWriter(сессия);
    }

    /// <inheritdoc />
    public Task WriteAsync(EmailLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _писатель.WriteAsync(
            entry.Recipient,
            entry.Status,
            entry.SmtpCode,
            entry.ServerResponse,
            entry.Error,
            entry.SentAtUtc,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailLogEntry>> ReadAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var записи = await _писатель.ReadAsync(limit, cancellationToken).ConfigureAwait(false);

        return записи
            .Select(запись => new EmailLogEntry(
                запись.Recipient,
                запись.Status,
                запись.SmtpCode,
                запись.ServerResponse,
                запись.Error,
                запись.SentAtUtc))
            .ToArray();
    }
}

/// <summary>
/// Журнал email-рассылок в общей базе через серверную службу.
/// </summary>
/// <remarks>
/// Запись в журнал выполняется как операция с защитой от дубля: потерянный
/// ответ не приводит к повторной записи журнала с новым идентификатором.
/// </remarks>
public sealed class RemoteEmailLogWriter : IEmailLogWriter
{
    private readonly SharedTransport _транспорт;
    private readonly PendingOperationsStore _незавершённые;

    /// <summary>
    /// Создаёт писатель журнала поверх серверной службы.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    /// <param name="незавершённые">Хранилище незавершённых операций.</param>
    public RemoteEmailLogWriter(SharedTransport транспорт, PendingOperationsStore незавершённые)
    {
        ArgumentNullException.ThrowIfNull(транспорт);
        ArgumentNullException.ThrowIfNull(незавершённые);

        _транспорт = транспорт;
        _незавершённые = незавершённые;
    }

    /// <inheritdoc />
    public async Task WriteAsync(EmailLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var тело = JsonSerializer.Serialize(
            new WriteEmailLogRequest(new[]
            {
                new EmailLogEntryDto(
                    entry.Recipient,
                    entry.Status,
                    entry.SmtpCode,
                    entry.ServerResponse,
                    entry.Error,
                    entry.SentAtUtc),
            }),
            SharedJson.Options);

        await _незавершённые
            .ВыполнитьAsync<WriteEmailLogResponse>(_транспорт, "email-log.write", тело, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailLogEntry>> ReadAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var записи = await _транспорт
            .ВыполнитьЗапросAsync<EmailLogEntryDto[]>(
                "/api/v1/queries/email-log?limit=" + limit,
                cancellationToken)
            .ConfigureAwait(false);

        return записи
            .Select(запись => new EmailLogEntry(
                запись.Recipient,
                запись.Status,
                запись.SmtpCode,
                запись.ServerResponse,
                запись.Error,
                запись.SentAtUtc))
            .ToArray();
    }
}
