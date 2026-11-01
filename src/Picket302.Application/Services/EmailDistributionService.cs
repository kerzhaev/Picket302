using Picket302.Application.Templating;
using Picket302.Domain.Email;
using Picket302.Domain.Entities;

namespace Picket302.Application.Services;

/// <summary>
/// Делегат отправки письма: связывает прикладной слой с SMTP-клиентом
/// инфраструктуры без нарушения матрицы зависимостей (ТЗ §2.1: слой
/// Application не ссылается на Infrastructure).
/// </summary>
/// <param name="message">Письмо к отправке.</param>
/// <param name="cancellationToken">Токен отмены.</param>
/// <returns>Задача, возвращающая результат отправки с кодом ответа SMTP-сервера.</returns>
public delegate Task<SendResult> EmailSenderDelegate(MailMessage message, CancellationToken cancellationToken);

/// <summary>
/// Прогресс отправки партии писем (ТЗ §8.7, функция 92: «последовательно
/// отправляет через SMTP-клиент с прогрессом и возможностью отмены»).
/// </summary>
/// <param name="Sent">Число обработанных писем.</param>
/// <param name="Total">Общее число писем в партии.</param>
/// <param name="CurrentRecipient">Адрес текущего получателя.</param>
public sealed record EmailSendProgress(int Sent, int Total, string CurrentRecipient);

/// <summary>
/// Результат выполнения партии писем: отчёт отправки и предупреждения
/// (ТЗ §8.7, функция 92: получатели без адреса исключаются с перечислением
/// в отчёте; незаполненные маркеры полей перечисляются в предупреждениях).
/// </summary>
/// <param name="Report">Отчёт отправки.</param>
/// <param name="Warnings">Перечень предупреждений партии.</param>
public sealed record EmailBatchOutcome(SendReport Report, IReadOnlyList<string> Warnings);

/// <summary>
/// Результат подстановки маркеров полей в текст письма.
/// </summary>
/// <param name="Text">Текст с подставленными значениями.</param>
/// <param name="UnresolvedMarkers">
/// Перечень маркеров, для которых значение не найдено: такие маркеры остаются
/// в тексте и перечисляются в предупреждениях партии.
/// </param>
public sealed record EmailMarkerResolution(string Text, IReadOnlyList<string> UnresolvedMarkers);

/// <summary>
/// Подстановка маркеров полей вида <c>#Имя#</c> в тему и тело письма
/// (ТЗ §8.7, функция 92: «подставляет тему и тело (текст или HTML) с маркерами
/// полей»; синтаксис маркера — как в шаблонах документов, ТЗ §8.5, функция 66).
/// </summary>
/// <remarks>
/// <para>
/// Значения берутся из набора параметров подстановки и дополняются данными
/// получателя письма (наименование, адрес, идентификатор контрагента).
/// Незаполненный маркер не приводит к исключению: он остаётся в тексте и
/// перечисляется в предупреждениях результата.
/// </para>
/// </remarks>
public sealed class EmailMarkerResolver
{
    /// <summary>Имя маркера наименования контрагента.</summary>
    public const string НаименованиеКонтрагента = "Counterparty_Name";

    /// <summary>Имя маркера адреса электронной почты контрагента.</summary>
    public const string АдресКонтрагента = "Counterparty_Email";

    /// <summary>Имя маркера идентификатора контрагента.</summary>
    public const string ИдентификаторКонтрагента = "Counterparty_Id";

    /// <summary>
    /// Псевдонимы маркеров, применяемые в письмах: русские имена из шаблона
    /// письма по умолчанию (<c>#Наименование#</c>, <c>#Индекс#</c>,
    /// <c>#Адрес#</c>) соответствуют полям карточки контрагента.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Псевдонимы =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Наименование"] = НаименованиеКонтрагента,
            ["Адрес"] = "Counterparty_Address_Main",
            ["Индекс"] = "to_zip",
            ["Email"] = АдресКонтрагента,
        };

    private readonly IReadOnlyDictionary<string, object> _параметры;
    private readonly DocumentMarkerCatalog _каталог;

    /// <summary>
    /// Создаёт подстановщик маркеров.
    /// </summary>
    /// <param name="параметры">
    /// Набор параметров подстановки; значения полей письма. Общие для всех
    /// получателей партии.
    /// </param>
    /// <param name="каталог">Каталог маркеров документов.</param>
    public EmailMarkerResolver(
        IReadOnlyDictionary<string, object>? параметры = null,
        DocumentMarkerCatalog? каталог = null)
    {
        _параметры = параметры ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _каталог = каталог ?? DocumentMarkerCatalog.Shared;
    }

    /// <summary>Набор параметров подстановки, общий для всех получателей.</summary>
    public IReadOnlyDictionary<string, object> Параметры => _параметры;

    /// <summary>
    /// Подставляет значения маркеров в текст.
    /// </summary>
    /// <param name="шаблон">Текст с маркерами вида <c>#Имя#</c>.</param>
    /// <param name="получатель">Получатель письма; используется для подстановки полей контрагента.</param>
    /// <returns>Текст с подставленными значениями и перечень незаполненных маркеров.</returns>
    public EmailMarkerResolution Resolve(string? шаблон, EmailRecipient? получатель = null)
        => Resolve(шаблон, null, получатель);

    /// <summary>
    /// Подставляет значения маркеров в текст.
    /// </summary>
    /// <param name="шаблон">Текст с маркерами вида <c>#Имя#</c>.</param>
    /// <param name="дополнительные">
    /// Дополнительные значения подстановки (например, реквизиты отправителя);
    /// имеют приоритет над общими параметрами.
    /// </param>
    /// <param name="получатель">Получатель письма; используется для подстановки полей контрагента.</param>
    /// <returns>Текст с подставленными значениями и перечень незаполненных маркеров.</returns>
    public EmailMarkerResolution Resolve(
        string? шаблон,
        IReadOnlyDictionary<string, object>? дополнительные,
        EmailRecipient? получатель)
    {
        if (string.IsNullOrEmpty(шаблон))
        {
            return new EmailMarkerResolution(string.Empty, Array.Empty<string>());
        }

        var значения = СобратьЗначения(дополнительные, получатель);
        var незаполненные = new List<string>();

        var результат = DocumentMarkerCatalog.MarkerPattern.Replace(
            шаблон,
            match =>
            {
                var имя = match.Groups["name"].Value;

                if (НайтиЗначение(имя, значения, out var значение))
                {
                    return значение;
                }

                if (!незаполненные.Contains(имя, StringComparer.OrdinalIgnoreCase))
                {
                    незаполненные.Add(имя);
                }

                return match.Value;
            });

        return new EmailMarkerResolution(результат, незаполненные);
    }

    /// <summary>
    /// Формирует набор значений подстановки: общие параметры, дополнительные
    /// значения и реквизиты получателя.
    /// </summary>
    /// <param name="дополнительные">Дополнительные значения подстановки.</param>
    /// <param name="получатель">Получатель письма.</param>
    /// <returns>Набор значений подстановки.</returns>
    private Dictionary<string, object> СобратьЗначения(
        IReadOnlyDictionary<string, object>? дополнительные,
        EmailRecipient? получатель)
    {
        var значения = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var пара in _параметры)
        {
            значения[пара.Key] = пара.Value;
        }

        if (дополнительные is not null)
        {
            foreach (var пара in дополнительные)
            {
                значения[пара.Key] = пара.Value;
            }
        }

        if (получатель is not null)
        {
            значения[ИдентификаторКонтрагента] = получатель.CounterpartyId;
            значения[НаименованиеКонтрагента] = получатель.Name;
            значения[АдресКонтрагента] = получатель.Email;
        }

        return значения;
    }

    /// <summary>
    /// Ищет значение маркера в наборе значений.
    /// </summary>
    /// <param name="имя">Имя маркера без решёток.</param>
    /// <param name="значения">Набор значений подстановки.</param>
    /// <param name="значение">Найденное значение.</param>
    /// <returns><see langword="true"/>, если значение найдено.</returns>
    private bool НайтиЗначение(
        string имя,
        IReadOnlyDictionary<string, object> значения,
        out string значение)
    {
        значение = string.Empty;

        if (string.IsNullOrWhiteSpace(имя))
        {
            return false;
        }

        if (значения.TryGetValue(имя, out var прямое))
        {
            значение = DocumentMarkerCatalog.FormatValue(прямое);
            return true;
        }

        if (Псевдонимы.TryGetValue(имя, out var каноническое)
            && значения.TryGetValue(каноническое, out var поПсевдониму))
        {
            значение = DocumentMarkerCatalog.FormatValue(поПсевдониму);
            return true;
        }

        if (_каталог.TryGetParameterValue(имя, значения, out var поКаталогу))
        {
            значение = поКаталогу;
            return true;
        }

        return false;
    }
}

/// <summary>
/// Электронная рассылка — отдельный канал отправки, не связанный с бумажными
/// отправлениями (ТЗ §8.7, функция 92:
/// <c>public SendReport SendBatch(EmailDistribution distribution)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Служба формирует партию писем по выборке контрагентов, подставляет тему и
/// тело с маркерами полей и отправляет письма последовательно, с прогрессем и
/// возможностью отмены. Электронная рассылка не создаёт записей таблицы
/// <c>mails</c>: она лишь пишет журнал отправки.
/// </para>
/// <para>
/// Связь со SMTP-клиентом инфраструктуры выполняется через делегат
/// <see cref="EmailSenderDelegate"/>: слой Application не ссылается на слой
/// Infrastructure (матрица зависимостей ТЗ §2.1).
/// </para>
/// </remarks>
public sealed class EmailDistributionService
{
    /// <summary>
    /// Пауза между письмами по умолчанию, миллисекунд (защита от блокировки
    /// сервером, ТЗ §8.7, функция 100).
    /// </summary>
    public const int ПаузаМеждуПисьмамиПоУмолчаниюМс = 1000;

    private readonly EmailSenderDelegate _отправить;
    private readonly IEmailLogWriter _журнал;
    private readonly EmailMarkerResolver _маркеры;

    /// <summary>
    /// Создаёт службу электронной рассылки.
    /// </summary>
    /// <param name="отправить">Делегат отправки письма (SMTP-клиент инфраструктуры).</param>
    /// <param name="журнал">Журнал отправки.</param>
    /// <param name="маркеры">Подстановщик маркеров полей.</param>
    public EmailDistributionService(
        EmailSenderDelegate отправить,
        IEmailLogWriter журнал,
        EmailMarkerResolver маркеры)
        : this(отправить, журнал, маркеры, null, ПаузаМеждуПисьмамиПоУмолчаниюМс)
    {
    }

    /// <summary>
    /// Создаёт службу электронной рассылки.
    /// </summary>
    /// <param name="отправить">Делегат отправки письма (SMTP-клиент инфраструктуры).</param>
    /// <param name="журнал">Журнал отправки.</param>
    /// <param name="маркеры">Подстановщик маркеров полей.</param>
    /// <param name="настройки">
    /// Настройки SMTP-учётной записи; используются для проверки «сервер
    /// указан» до начала отправки (ТЗ §8.7, функция 93).
    /// </param>
    /// <param name="паузаМеждуПисьмамиМс">Пауза между письмами, миллисекунд.</param>
    public EmailDistributionService(
        EmailSenderDelegate отправить,
        IEmailLogWriter журнал,
        EmailMarkerResolver маркеры,
        SmtpSettings? настройки,
        int паузаМеждуПисьмамиМс = ПаузаМеждуПисьмамиПоУмолчаниюМс)
    {
        ArgumentNullException.ThrowIfNull(отправить);
        ArgumentNullException.ThrowIfNull(журнал);
        ArgumentNullException.ThrowIfNull(маркеры);

        _отправить = отправить;
        _журнал = журнал;
        _маркеры = маркеры;

        Настройки = настройки;
        ПаузаМеждуПисьмамиМс = паузаМеждуПисьмамиМс < 0 ? 0 : паузаМеждуПисьмамиМс;
    }

    /// <summary>
    /// Настройки SMTP-учётной записи; при значении <see langword="null"/> проверка
    /// сервера не выполняется.
    /// </summary>
    public SmtpSettings? Настройки { get; set; }

    /// <summary>
    /// Пауза между письмами партии, миллисекунд (защита от блокировки сервером).
    /// </summary>
    public int ПаузаМеждуПисьмамиМс { get; set; }

    /// <summary>
    /// Формирует и отправляет партию писем (ТЗ §8.7, функция 92).
    /// </summary>
    /// <param name="distribution">Рассылка: выборка получателей, тема и тело письма.</param>
    /// <returns>Отчёт отправки партии.</returns>
    public SendReport SendBatch(EmailDistribution distribution)
        => SendBatch(distribution, null, CancellationToken.None);

    /// <summary>
    /// Формирует и отправляет партию писем с прогрессом и возможностью отмены.
    /// </summary>
    /// <param name="distribution">Рассылка: выборка получателей, тема и тело письма.</param>
    /// <param name="progress">Отчёт о ходе отправки.</param>
    /// <param name="ct">Токен отмены партии.</param>
    /// <returns>Отчёт отправки партии.</returns>
    public SendReport SendBatch(EmailDistribution distribution, IProgress<EmailSendProgress>? progress = null, CancellationToken ct = default)
        => SendBatchDetailed(distribution, progress, ct).Report;

    /// <summary>
    /// Формирует и отправляет партию писем, возвращая отчёт и предупреждения.
    /// </summary>
    /// <param name="distribution">Рассылка: выборка получателей, тема и тело письма.</param>
    /// <param name="progress">Отчёт о ходе отправки.</param>
    /// <param name="ct">Токен отмены партии.</param>
    /// <returns>Отчёт отправки и перечень предупреждений.</returns>
    public async Task<EmailBatchOutcome> SendBatchDetailedAsync(
        EmailDistribution distribution,
        IProgress<EmailSendProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(distribution);

        var предупреждения = new List<string>();
        var результаты = new List<SendResult>();
        var пропущены = new List<string>();
        var получатели = distribution.Recipients.ToList();

        if (Настройки is not null && !Настройки.HasServer)
        {
            // Сервер не задан: отправка не начинается (ТЗ §8.7, функция 93).
            return new EmailBatchOutcome(
                new SendReport
                {
                    TotalCount = получатели.Count,
                    Error = SmtpSettings.ServerNotSpecifiedMessage,
                    SkippedRecipients = пропущены,
                    Results = результаты,
                },
                предупреждения);
        }

        var отправлено = 0;
        var ошибок = 0;
        var отменено = false;
        var обработано = 0;

        foreach (var получатель in получатели)
        {
            if (ct.IsCancellationRequested)
            {
                отменено = true;
                break;
            }

            if (!получатель.HasEmail)
            {
                // Получатель без адреса исключается из отправки и перечисляется
                // в отчёте (ТЗ §8.7, функция 92).
                var обозначение = string.IsNullOrWhiteSpace(получатель.Name)
                    ? $"#{получатель.CounterpartyId}"
                    : получатель.Name;

                пропущены.Add(обозначение);
                обработано++;
                continue;
            }

            var разрешениеТемы = _маркеры.Resolve(distribution.Subject, получатель);
            var разрешениеТела = _маркеры.Resolve(distribution.Body, получатель);

            предупреждения.AddRange(
                разрешениеТемы.UnresolvedMarkers.Select(маркер =>
                    $"Тема письма: маркер #{маркер}# не заполнен."));
            предупреждения.AddRange(
                разрешениеТела.UnresolvedMarkers.Select(маркер =>
                    $"Тело письма: маркер #{маркер}# не заполнен."));

            var письмо = СобратьПисьмо(distribution, получатель, разрешениеТемы.Text, разрешениеТела.Text);

            SendResult результат;

            try
            {
                результат = await _отправить(письмо, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                отменено = true;
                break;
            }

            обработано++;

            if (результат.IsSuccess)
            {
                отправлено++;
            }
            else
            {
                ошибок++;
            }

            результаты.Add(результат);

            await ЗаписатьВЖурналAsync(результат, ct).ConfigureAwait(false);

            progress?.Report(new EmailSendProgress(обработано, получатели.Count, получатель.Email));

            if (обработано < получатели.Count && ПаузаМеждуПисьмамиМс > 0)
            {
                try
                {
                    await Task.Delay(ПаузаМеждуПисьмамиМс, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    отменено = true;
                    break;
                }
            }
        }

        var отчёт = new SendReport
        {
            IsCanceled = отменено,
            TotalCount = получатели.Count,
            SentCount = отправлено,
            FailedCount = ошибок,
            SkippedRecipients = пропущены,
            Results = результаты,
        };

        return new EmailBatchOutcome(отчёт, предупреждения);
    }

    /// <summary>
    /// Формирует и отправляет партию писем, возвращая отчёт и предупреждения.
    /// </summary>
    /// <param name="distribution">Рассылка: выборка получателей, тема и тело письма.</param>
    /// <param name="progress">Отчёт о ходе отправки.</param>
    /// <param name="ct">Токен отмены партии.</param>
    /// <returns>Отчёт отправки и перечень предупреждений.</returns>
    public EmailBatchOutcome SendBatchDetailed(
        EmailDistribution distribution,
        IProgress<EmailSendProgress>? progress = null,
        CancellationToken ct = default)
        => SendBatchDetailedAsync(distribution, progress, ct)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Собирает письмо получателя из рассылки.
    /// </summary>
    /// <param name="рассылка">Рассылка.</param>
    /// <param name="получатель">Получатель письма.</param>
    /// <param name="тема">Тема с подставленными значениями.</param>
    /// <param name="тело">Тело с подставленными значениями.</param>
    /// <returns>Готовое письмо.</returns>
    private static MailMessage СобратьПисьмо(
        EmailDistribution рассылка,
        EmailRecipient получатель,
        string тема,
        string тело)
    {
        var письмо = new MailMessage([получатель.Email.Trim()])
        {
            From = рассылка.From,
            ReplyTo = рассылка.ReplyTo,
            Subject = тема,
            Body = тело,
            BodyFormat = рассылка.BodyFormat,
        };

        if (рассылка.BodyFormat == BodyFormat.Html)
        {
            // Перед отправкой выполняется базовая санитизация разметки
            // (ТЗ §8.7, функция 96).
            письмо.Body = HtmlBodySanitizer.Sanitize(тело);
        }

        foreach (var вложение in рассылка.Attachments)
        {
            письмо.Attachments.Add(вложение);
        }

        return письмо;
    }

    /// <summary>
    /// Записывает результат отправки в журнал (ТЗ §8.7, функция 100).
    /// </summary>
    /// <param name="результат">Результат отправки.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача завершения записи.</returns>
    private async Task ЗаписатьВЖурналAsync(SendResult результат, CancellationToken ct)
    {
        var запись = EmailLogEntry.Создать(
            результат.Recipient,
            результат.IsSuccess ? EmailLogEntry.StatusSent : EmailLogEntry.StatusFailed,
            результат.SmtpCode == 0 ? null : результат.SmtpCode,
            результат.ServerResponse,
            результат.Error);

        await _журнал.WriteAsync(запись, ct).ConfigureAwait(false);
    }
}
