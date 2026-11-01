using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Mail;

/// <summary>
/// Запись журнала отправки электронной почты, прочитанная из базы данных
/// (ТЗ §8.7, функция 100: «Результат каждой отправки пишется в журнал
/// <c>email_log</c> (адрес, статус, код ответа сервера, дата)»).
/// </summary>
/// <param name="Recipient">Адрес получателя.</param>
/// <param name="Status">Статус отправки на русском языке.</param>
/// <param name="SmtpCode">Код ответа SMTP-сервера.</param>
/// <param name="ServerResponse">Текст ответа сервера.</param>
/// <param name="Error">Текст ошибки отправки.</param>
/// <param name="SentAtUtc">Момент отправки (UTC).</param>
public sealed record SqliteEmailLogRow(
    string Recipient,
    string Status,
    int? SmtpCode,
    string? ServerResponse,
    string? Error,
    DateTime SentAtUtc);

/// <summary>
/// Журнал отправки электронной почты на SQLite (ТЗ §8.7, функция 100;
/// ТЗ §10.3: канал <c>email</c> — «отправки с кодами SMTP»).
/// </summary>
/// <remarks>
/// Класс реализует запись и чтение таблицы <c>email_log</c> непосредственно
/// через сессию базы данных, как и остальные репозитории слоя инфраструктуры.
/// Контракт прикладного слоя <c>IEmailLogWriter</c> реализуется оболочкой:
/// слой инфраструктуры не ссылается на прикладной (матрица зависимостей
/// ТЗ §2.1).
/// </remarks>
public sealed class SqliteEmailLogWriter
{
    private readonly ISqliteSession _сессия;

    /// <summary>
    /// Создаёт журнал отправки.
    /// </summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public SqliteEmailLogWriter(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        _сессия = сессия;
    }

    /// <summary>
    /// Записывает результат отправки в журнал.
    /// </summary>
    /// <param name="recipient">Адрес получателя.</param>
    /// <param name="status">Статус отправки на русском языке.</param>
    /// <param name="smtpCode">Код ответа SMTP-сервера.</param>
    /// <param name="serverResponse">Текст ответа сервера.</param>
    /// <param name="error">Текст ошибки отправки.</param>
    /// <param name="sentAtUtc">Момент отправки (UTC).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения записи.</returns>
    public async Task WriteAsync(
        string recipient,
        string status,
        int? smtpCode,
        string? serverResponse,
        string? error,
        DateTime sentAtUtc,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO email_log (recipient, status, smtp_code, server_response, error, sent_at)
            VALUES (@Recipient, @Status, @SmtpCode, @ServerResponse, @Error, @SentAt);
            """;

        await _сессия.ExecuteAsync(
            sql,
            new
            {
                Recipient = recipient,
                Status = status,
                SmtpCode = smtpCode,
                ServerResponse = serverResponse,
                Error = error,
                SentAt = sentAtUtc,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Читает последние записи журнала.
    /// </summary>
    /// <param name="limit">Максимальное число записей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Записи журнала, начиная с последней.</returns>
    public async Task<IReadOnlyList<SqliteEmailLogRow>> ReadAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT recipient, status, smtp_code, server_response, error, sent_at
            FROM email_log
            ORDER BY id DESC
            LIMIT @Limit;
            """;

        var строки = await _сессия.QueryAsync<СтрокаЖурнала>(
            sql,
            new { Limit = limit < 1 ? 1 : limit },
            cancellationToken).ConfigureAwait(false);

        return строки
            .Select(строка => new SqliteEmailLogRow(
                строка.Recipient,
                строка.Status,
                строка.SmtpCode,
                строка.ServerResponse,
                строка.Error,
                строка.SentAt))
            .ToList();
    }
}

/// <summary>Строка таблицы <c>email_log</c>.</summary>
internal sealed class СтрокаЖурнала
{
    /// <summary>Адрес получателя.</summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>Статус отправки.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Код ответа SMTP-сервера.</summary>
    public int? SmtpCode { get; set; }

    /// <summary>Текст ответа сервера.</summary>
    public string? ServerResponse { get; set; }

    /// <summary>Текст ошибки отправки.</summary>
    public string? Error { get; set; }

    /// <summary>Момент отправки (UTC).</summary>
    public DateTime SentAt { get; set; }
}
