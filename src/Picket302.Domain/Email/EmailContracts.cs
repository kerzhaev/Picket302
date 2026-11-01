using System.Globalization;

namespace Picket302.Domain.Email;

/// <summary>
/// Формат тела электронного письма (ТЗ §8.7, функция 96: «Формат письма:
/// простой текст / HTML»).
/// </summary>
public enum BodyFormat
{
    /// <summary>Простой текст без разметки.</summary>
    PlainText = 0,

    /// <summary>Размеченный HTML.</summary>
    Html = 1,
}

/// <summary>
/// Режим защищённого соединения с SMTP-сервером (ТЗ §8.7, функция 94).
/// </summary>
public enum SslMode
{
    /// <summary>Защищённого канала нет.</summary>
    None = 0,

    /// <summary>Переход в защищённый режим командой STARTTLS.</summary>
    StartTls = 1,

    /// <summary>Защищённый канал с момента подключения (SMTPS, порт 465).</summary>
    ImplicitTls = 2,
}

/// <summary>
/// Результат отправки одного письма (ТЗ §8.7, функция 100).
/// </summary>
/// <param name="IsSuccess">Признак принятия письма сервером.</param>
/// <param name="Recipient">Адрес получателя.</param>
/// <param name="SmtpCode">Код ответа SMTP-сервера (например, 250).</param>
/// <param name="ServerResponse">Текст ответа сервера.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
public sealed record SendResult(
    bool IsSuccess,
    string Recipient,
    int SmtpCode,
    string? ServerResponse,
    string? Error)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Создаёт успешный результат.</summary>
    /// <param name="recipient">Адрес получателя.</param>
    /// <param name="smtpCode">Код ответа SMTP-сервера.</param>
    /// <param name="serverResponse">Текст ответа сервера.</param>
    /// <returns>Успешный результат.</returns>
    public static SendResult Success(string recipient, int smtpCode, string? serverResponse)
        => new(true, recipient, smtpCode, serverResponse, null);

    /// <summary>Создаёт неуспешный результат.</summary>
    /// <param name="recipient">Адрес получателя.</param>
    /// <param name="smtpCode">Код ответа SMTP-сервера.</param>
    /// <param name="serverResponse">Текст ответа сервера.</param>
    /// <param name="error">Текст ошибки.</param>
    /// <returns>Неуспешный результат.</returns>
    public static SendResult Failure(string recipient, int smtpCode, string? serverResponse, string error)
        => new(false, recipient, smtpCode, serverResponse, error);
}

/// <summary>
/// Отчёт о выполнении партии электронных писем (ТЗ §8.7, функция 92:
/// <c>public SendReport SendBatch(EmailDistribution distribution)</c>).
/// </summary>
/// <remarks>
/// Отчёт содержит итоги по каждому письму партии и перечень получателей, у
/// которых адрес отсутствовал: такие получатели исключаются из отправки, но
/// перечисляются в отчёте.
/// </remarks>
public sealed record SendReport
{
    /// <summary>Признак признака отмены партии пользователем.</summary>
    public bool IsCanceled { get; init; }

    /// <summary>Общее число получателей в партии.</summary>
    public int TotalCount { get; init; }

    /// <summary>Число отправленных писем.</summary>
    public int SentCount { get; init; }

    /// <summary>Число писем, не принятых сервером.</summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Получатели без адреса электронной почты: исключены из отправки
    /// (ТЗ §8.7, функция 92: «Отсутствие адреса у части получателей — они
    /// исключаются с перечислением в отчёте»).
    /// </summary>
    public IReadOnlyList<string> SkippedRecipients { get; init; } = Array.Empty<string>();

    /// <summary>Результаты отправки по каждому адресу.</summary>
    public IReadOnlyList<SendResult> Results { get; init; } = Array.Empty<SendResult>();

    /// <summary>Текст ошибки, прервавшей партию целиком; <see langword="null"/> при успехе.</summary>
    public string? Error { get; init; }

    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => Error is not null;

    /// <summary>
    /// Число получателей, исключённых из-за отсутствия адреса.
    /// </summary>
    public int SkippedCount => SkippedRecipients.Count;

    /// <summary>
    /// Формирует текст отчёта для показа оператору.
    /// </summary>
    /// <returns>Текст отчёта на русском языке.</returns>
    public string BuildText()
    {
        var строки = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"Всего получателей: {TotalCount}; отправлено: {SentCount}; ошибок: {FailedCount}; пропущено: {SkippedCount}."),
        };

        if (SkippedRecipients.Count > 0)
        {
            строки.Add("Пропущены (нет адреса электронной почты):");

            foreach (var получатель in SkippedRecipients)
            {
                строки.Add("  — " + получатель);
            }
        }

        if (IsCanceled)
        {
            строки.Add("Партия прервана пользователем.");
        }

        if (Error is not null)
        {
            строки.Add("Ошибка: " + Error);
        }

        return string.Join(Environment.NewLine, строки);
    }
}

/// <summary>
/// Настройки SMTP-учётной записи (ТЗ §8.7, функция 93).
/// </summary>
/// <remarks>
/// <para>
/// Пароль хранится в защищённом виде: в свойство <see cref="ProtectedPassword"/>
/// помещается результат DPAPI (ProtectedData), а не открытый текст. Открытое
/// значение пароля в настройках не сохраняется.
/// </para>
/// </remarks>
public sealed record SmtpSettings
{
    /// <summary>Порт SMTP-сервера по умолчанию.</summary>
    public const int DefaultPort = 25;

    /// <summary>Порт защищённого SMTP-сервера (SMTPS) по умолчанию.</summary>
    public const int DefaultSecurePort = 465;

    /// <summary>Имя или адрес SMTP-сервера.</summary>
    public string Server { get; init; } = string.Empty;

    /// <summary>Порт SMTP-сервера.</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>Учётная запись (логин) на SMTP-сервере.</summary>
    public string? Login { get; init; }

    /// <summary>
    /// Пароль в защищённом виде (строка, полученная от DPAPI ProtectedData).
    /// </summary>
    public string? ProtectedPassword { get; init; }

    /// <summary>Режим защищённого соединения.</summary>
    public SslMode SslMode { get; init; } = SslMode.StartTls;

    /// <summary>
    /// Признак доверия самоподписанному сертификату сервера: решение
    /// пользователя сохраняется в профиле (ТЗ §8.7, функция 94).
    /// </summary>
    public bool TrustSelfSignedCertificate { get; init; }

    /// <summary>
    /// Отпечаток сертификата, которому выражено доверие; пустая строка —
    /// доверие не выдавалось.
    /// </summary>
    public string? TrustedCertificateFingerprint { get; init; }

    /// <summary>Таймаут соединения и операций, секунд.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Признак заданного сервера.</summary>
    public bool HasServer => !string.IsNullOrWhiteSpace(Server);

    /// <summary>
    /// Текст ошибки, выводимый при попытке отправки без заданного сервера
    /// (ТЗ §8.7, функция 93).
    /// </summary>
    public const string ServerNotSpecifiedMessage = "SMTP-сервер не указан";
}

/// <summary>
/// Параметры прокси-сервера для SMTP-клиента (ТЗ §8.7, функция 95).
/// </summary>
/// <remarks>
/// Пароль прокси хранится в защищённом виде (DPAPI), как и пароль учётной
/// записи SMTP.
/// </remarks>
public sealed record ProxySettings
{
    /// <summary>Признак использования прокси.</summary>
    public bool Enabled { get; init; }

    /// <summary>Адрес прокси-сервера.</summary>
    public string Server { get; init; } = string.Empty;

    /// <summary>Порт прокси-сервера.</summary>
    public int Port { get; init; } = 8080;

    /// <summary>Логин для авторизации на прокси.</summary>
    public string? Login { get; init; }

    /// <summary>Пароль для авторизации на прокси в защищённом виде (DPAPI).</summary>
    public string? ProtectedPassword { get; init; }
}

/// <summary>
/// Письмо электронной рассылки, подготовленное к отправке (ТЗ §8.7).
/// </summary>
public sealed class MailMessage
{
    /// <summary>Адрес отправителя (конверт отправки).</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Обратный адрес для ответа (Reply-To).</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Тема письма.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Тело письма в выбранном формате.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Формат тела письма.</summary>
    public BodyFormat BodyFormat { get; set; } = BodyFormat.PlainText;

    /// <summary>Получатели письма.</summary>
    public List<string> To { get; } = [];

    /// <summary>Копии письма.</summary>
    public List<string> Cc { get; } = [];

    /// <summary>Пути к файлам вложений.</summary>
    public List<string> Attachments { get; } = [];

    /// <summary>Создаёт пустое письмо.</summary>
    public MailMessage()
    {
    }

    /// <summary>
    /// Создаёт письмо с получателями.
    /// </summary>
    /// <param name="to">Адреса получателей.</param>
    public MailMessage(IEnumerable<string> to)
    {
        ArgumentNullException.ThrowIfNull(to);
        To.AddRange(to);
    }
}
