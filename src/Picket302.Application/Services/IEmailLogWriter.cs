namespace Picket302.Application.Services;

/// <summary>
/// Запись журнала отправки электронной почты (ТЗ §8.7, функция 100:
/// «Результат каждой отправки пишется в журнал <c>email_log</c> (адрес,
/// статус, код ответа сервера, дата)»).
/// </summary>
/// <param name="Recipient">Адрес получателя.</param>
/// <param name="Status">Статус отправки на русском языке.</param>
/// <param name="SmtpCode">Код ответа SMTP-сервера; <see langword="null"/> — ответа не было.</param>
/// <param name="ServerResponse">Текст ответа сервера.</param>
/// <param name="Error">Текст ошибки отправки.</param>
/// <param name="SentAtUtc">Момент отправки (UTC).</param>
public sealed record EmailLogEntry(
    string Recipient,
    string Status,
    int? SmtpCode,
    string? ServerResponse,
    string? Error,
    DateTime SentAtUtc)
{
    /// <summary>Статус успешно принятого сервером письма.</summary>
    public const string StatusSent = "Отправлено";

    /// <summary>Статус письма, отклонённого сервером или не отправленного по ошибке.</summary>
    public const string StatusFailed = "Ошибка";

    /// <summary>Статус получателя, исключённого из отправки (нет адреса).</summary>
    public const string StatusSkipped = "Пропущено";

    /// <summary>
    /// Создаёт запись журнала с текущим моментом времени.
    /// </summary>
    /// <param name="recipient">Адрес получателя.</param>
    /// <param name="status">Статус отправки.</param>
    /// <param name="smtpCode">Код ответа SMTP-сервера.</param>
    /// <param name="serverResponse">Текст ответа сервера.</param>
    /// <param name="error">Текст ошибки отправки.</param>
    /// <param name="момент">Момент отправки (UTC); по умолчанию — текущее время UTC.</param>
    /// <returns>Готовая запись журнала.</returns>
    public static EmailLogEntry Создать(
        string recipient,
        string status,
        int? smtpCode,
        string? serverResponse,
        string? error,
        DateTime? момент = null)
        => new(
            recipient,
            status,
            smtpCode,
            serverResponse,
            error,
            момент ?? DateTime.UtcNow);
}

/// <summary>
/// Журнал отправки электронной почты (ТЗ §8.7, функция 100; §10.3: канал
/// <c>email</c> — «отправки с кодами SMTP»).
/// </summary>
/// <remarks>
/// Интерфейс объявлен в прикладном слое: слой инфраструктуры реализует его
/// через класс <c>SqliteEmailLogWriter</c>, а оболочка связывает реализацию с
/// контрактом (матрица зависимостей ТЗ §2.1: Infrastructure не ссылается на
/// Application).
/// </remarks>
public interface IEmailLogWriter
{
    /// <summary>
    /// Записывает результат отправки в журнал.
    /// </summary>
    /// <param name="entry">Запись журнала.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения записи.</returns>
    Task WriteAsync(EmailLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает последние записи журнала.
    /// </summary>
    /// <param name="limit">Максимальное число записей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Записи журнала, начиная с последней.</returns>
    Task<IReadOnlyList<EmailLogEntry>> ReadAsync(int limit, CancellationToken cancellationToken = default);
}
