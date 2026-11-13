using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Picket302.Domain.Email;

namespace Picket302.Infrastructure.Mail;

/// <summary>
/// Ответ SMTP-сервера (код и текст).
/// </summary>
/// <param name="Code">Код ответа (2xx — успех, 4xx — временный отказ, 5xx — постоянный отказ).</param>
/// <param name="Text">Полный текст ответа сервера.</param>
public sealed record SmtpResponse(int Code, string Text)
{
    /// <summary>Признак успешного ответа (2xx).</summary>
    public bool IsSuccess => Code is >= 200 and < 300;

    /// <summary>Признак временного отказа сервера (4xx).</summary>
    public bool IsTemporary => Code is >= 400 and < 500;

    /// <summary>Признак постоянного отказа сервера (5xx).</summary>
    public bool IsPermanent => Code is >= 500 and < 600;
}

/// <summary>
/// Собственный SMTP-клиент программы (ТЗ §8.7, функция 100: «отправка через
/// SMTP-клиент, поддержка TLS и авторизации, таймаут — одна повторная попытка»).
/// </summary>
/// <remarks>
/// <para>
/// Протокол SMTP реализован поверх сокетов (<see cref="System.Net.Sockets.TcpClient"/>)
/// без использования сторонних почтовых библиотек и без
/// <c>System.Net.Mail.SmtpClient</c>: поддерживаются EHLO/HELO, STARTTLS,
/// AUTH LOGIN/PLAIN, MAIL FROM, RCPT TO, DATA, QUIT.
/// </para>
/// <para>
/// Отказ сервера (4xx/5xx) не прерывает партию: письмо помечается ошибкой в
/// отчёте, а отправка следующих писем продолжается.
/// </para>
/// </remarks>
public sealed class SmtpClientFacade
{
    /// <summary>Текст сообщения при отклонении сертификата сервера.</summary>
    public const string CertificateRejectedMessage = "Отправка прервана: сертификат сервера отклонён";

    /// <summary>Имя узла, объявляемое серверу в команде EHLO.</summary>
    public const string ИмяХоста = "picket302";

    private readonly SmtpOptions _опции;

    /// <summary>
    /// Создаёт SMTP-клиент.
    /// </summary>
    /// <param name="настройки">Настройки учётной записи.</param>
    /// <param name="прокси">Настройки прокси-сервера.</param>
    /// <param name="опции">Набор настроек почтового клиента (защита паролей).</param>
    public SmtpClientFacade(
        SmtpSettings? настройки = null,
        ProxySettings? прокси = null,
        SmtpOptions? опции = null)
    {
        _опции = опции ?? new SmtpOptions();

        Настройки = настройки ?? new SmtpSettings();
        Прокси = прокси;

        РежимЗащиты = Настройки.SslMode;
    }

    /// <summary>Настройки SMTP-учётной записи.</summary>
    public SmtpSettings Настройки { get; set; }

    /// <summary>Настройки прокси-сервера; <see langword="null"/> — прямое соединение.</summary>
    public ProxySettings? Прокси { get; set; }

    /// <summary>Режим защищённого соединения.</summary>
    public SslMode РежимЗащиты { get; private set; }

    /// <summary>Адрес отправителя (конверт отправки).</summary>
    public string АдресОтправителя { get; private set; } = string.Empty;

    /// <summary>Обратный адрес для ответа (Reply-To).</summary>
    public string? ОбратныйАдрес { get; private set; }

    /// <summary>
    /// Обработчик записи результата отправки в журнал <c>email_log</c>
    /// (ТЗ §8.7, функция 100). Задаётся оболочкой: слой инфраструктуры не
    /// ссылается на прикладной слой.
    /// </summary>
    public Action<SendResult, DateTime>? ЖурналОтправки { get; set; }

    /// <summary>
    /// Отпечаток сертификата, отклонённого при проверке (пустая строка — отклонений не было).
    /// </summary>
    public string ОтклонённыйОтпечаток { get; private set; } = string.Empty;

    /// <summary>
    /// Задаёт режим защищённого соединения (ТЗ §8.7, функция 93).
    /// </summary>
    /// <param name="mode">Режим защиты: без защиты, STARTTLS, неявный TLS.</param>
    public void EnableSsl(SslMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Неизвестный режим защищённого соединения.");
        }

        РежимЗащиты = mode;
        Настройки = Настройки with { SslMode = mode };
    }

    /// <summary>
    /// Задаёт адрес отправителя и обратный адрес отдельно; формат адресов
    /// проверяется до отправки (ТЗ §8.7, функция 93).
    /// </summary>
    /// <param name="from">Адрес отправителя.</param>
    /// <param name="replyTo">Обратный адрес для ответа; пустое значение — не задаётся.</param>
    /// <exception cref="ArgumentException">Формат адреса неверный.</exception>
    public void SetEnvelopeFrom(string from, string replyTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);

        if (!ПроверитьАдрес(from, out var ошибкаОтправителя))
        {
            throw new ArgumentException(
                string.Create(CultureInfo.CurrentCulture, $"Неверный формат адреса отправителя: {ошибкаОтправителя}."),
                nameof(from));
        }

        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            if (!ПроверитьАдрес(replyTo, out var ошибкаОтвета))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.CurrentCulture, $"Неверный формат обратного адреса: {ошибкаОтвета}."),
                    nameof(replyTo));
            }

            ОбратныйАдрес = replyTo.Trim();
        }
        else
        {
            ОбратныйАдрес = null;
        }

        АдресОтправителя = from.Trim();
    }

    /// <summary>
    /// Проверяет, задан ли SMTP-сервер: без сервера отправка не начинается, а
    /// в отчёт попадает сообщение «SMTP-сервер не указан» (ТЗ §8.7, функция 93).
    /// </summary>
    /// <param name="settings">Настройки учётной записи.</param>
    /// <returns><see langword="true"/>, если сервер задан.</returns>
    public bool IsServerSpecified(SmtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.HasServer;
    }

    /// <summary>
    /// Фиксирует решение пользователя «Доверять» для сертификата сервера:
    /// отпечаток сохраняется в профиле настроек (ТЗ §8.7, функция 94).
    /// </summary>
    /// <param name="certificate">Сертификат сервера.</param>
    /// <param name="fingerprint">Отпечаток сертификата, подтверждённый пользователем.</param>
    /// <returns><see langword="true"/>, если отпечаток совпал и доверие сохранено.</returns>
    public bool TrustCertificate(X509Certificate2? certificate, string fingerprint)
    {
        if (certificate is null)
        {
            return false;
        }

        var отпечаток = certificate.Thumbprint ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fingerprint)
            || !string.Equals(
                НормализоватьОтпечаток(отпечаток),
                НормализоватьОтпечаток(fingerprint),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Настройки = Настройки with
        {
            TrustSelfSignedCertificate = true,
            TrustedCertificateFingerprint = отпечаток,
        };

        return true;
    }

    /// <summary>
    /// Отправляет письмо и записывает результат в журнал отправки
    /// (ТЗ §8.7, функция 100).
    /// </summary>
    /// <param name="message">Письмо.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Отчёт об отправке письма.</returns>
    public SendReport Send(MailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var результаты = ВыполнитьОтправку(message, ct);

        foreach (var результат in результаты)
        {
            ЖурналОтправки?.Invoke(результат, DateTime.UtcNow);
        }

        var успешных = результаты.Count(результат => результат.IsSuccess);

        return new SendReport
        {
            TotalCount = message.To.Count,
            SentCount = успешных,
            FailedCount = результаты.Count - успешных,
            Results = результаты,
            Error = успешных == 0 && результаты.Count > 0
                ? результаты[0].Error
                : null,
        };
    }

    /// <summary>
    /// Отправляет письмо и возвращает результат с кодом ответа SMTP-сервера.
    /// </summary>
    /// <param name="message">Письмо.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат отправки.</returns>
    public SendResult SendOne(MailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var результаты = ВыполнитьОтправку(message, ct);

        if (результаты.Count == 0)
        {
            return SendResult.Failure(string.Empty, 0, null, "Письмо не содержит получателей.");
        }

        foreach (var результат in результаты)
        {
            if (результат.IsFailure)
            {
                return результат;
            }
        }

        return результаты[0];
    }

    /// <summary>
    /// Проверяет формат адреса электронной почты.
    /// </summary>
    /// <param name="адрес">Проверяемый адрес.</param>
    /// <param name="ошибка">Текст ошибки на русском языке.</param>
    /// <returns><see langword="true"/>, если адрес корректен.</returns>
    public static bool ПроверитьАдрес(string? адрес, out string ошибка)
    {
        ошибка = string.Empty;

        if (string.IsNullOrWhiteSpace(адрес))
        {
            ошибка = "адрес не указан";
            return false;
        }

        var разобранный = РазобратьАдрес(адрес);

        if (разобранный.Адрес.Length == 0)
        {
            ошибка = "адрес не содержит почтового адреса";
            return false;
        }

        if (разобранный.Адрес.Contains(' ', StringComparison.Ordinal)
            || разобранный.Адрес.Contains(',', StringComparison.Ordinal)
            || разобранный.Адрес.Contains('<', StringComparison.Ordinal)
            || разобранный.Адрес.Contains('>', StringComparison.Ordinal)
            || разобранный.Адрес.Contains('\r', StringComparison.Ordinal)
            || разобранный.Адрес.Contains('\n', StringComparison.Ordinal))
        {
            ошибка = "адрес содержит недопустимые символы";
            return false;
        }

        var части = разобранный.Адрес.Split('@');

        if (части.Length != 2)
        {
            ошибка = "адрес должен содержать единственный символ @";
            return false;
        }

        if (части[0].Length == 0)
        {
            ошибка = "не задан локальный адрес";
            return false;
        }

        if (части[1].Length == 0)
        {
            ошибка = "не задан домен адреса";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Выполняет пробное соединение с сервером: устанавливает соединение и
    /// получает приветствие сервера, не отправляя письма (ТЗ §8.7, функция 93).
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат пробного соединения.</returns>
    public SmtpProbeResult ПроверитьСоединение(CancellationToken ct = default)
    {
        try
        {
            using var сессия = СоздатьСоединение();

            сессия.Подключиться(ct);

            var приветствие = сессия.ПрочитатьОтвет();

            if (!приветствие.IsSuccess)
            {
                return new SmtpProbeResult(
                    false,
                    приветствие.Code,
                    приветствие.Text,
                    SmtpProbeResult.ConnectionFailedMessage,
                    string.Create(CultureInfo.CurrentCulture, $"сервер ответил кодом {приветствие.Code}"));
            }

            сессия.Выполнить($"EHLO {ИмяХоста}");
            сессия.Выполнить("QUIT");

            return new SmtpProbeResult(true, приветствие.Code, приветствие.Text, null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            return ОшибкаСоединения(ex);
        }
        catch (SocketException ex)
        {
            return ОшибкаСоединения(ex);
        }
        catch (TimeoutException ex)
        {
            return ОшибкаСоединения(ex);
        }
        catch (InvalidOperationException ex)
        {
            return ОшибкаСоединения(ex);
        }
        catch (AuthenticationException ex)
        {
            return ОшибкаСоединения(ex);
        }
        catch (CryptographicException ex)
        {
            return ОшибкаСоединения(ex);
        }
    }

    /// <summary>
    /// Формирует результат неудачного пробного соединения.
    /// </summary>
    /// <param name="ошибка">Исключение.</param>
    /// <returns>Результат с текстом ошибки и детализацией.</returns>
    private static SmtpProbeResult ОшибкаСоединения(Exception ошибка)
        => new(
            false,
            0,
            null,
            SmtpProbeResult.ConnectionFailedMessage,
            SmtpOptions.ОписатьОшибку(ошибка));

    /// <summary>
    /// Выполняет отправку письма с одной повторной попыткой при таймауте
    /// (ТЗ §8.7, функция 100).
    /// </summary>
    /// <param name="message">Письмо.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результаты отправки по каждому получателю.</returns>
    private IReadOnlyList<SendResult> ВыполнитьОтправку(MailMessage message, CancellationToken ct)
    {
        var адреса = message.To
            .Where(адрес => !string.IsNullOrWhiteSpace(адрес))
            .Select(адрес => адрес.Trim())
            .ToList();

        if (!Настройки.HasServer)
        {
            return ОшибкаДляВсех(адреса, 0, null, SmtpSettings.ServerNotSpecifiedMessage);
        }

        if (адреса.Count == 0)
        {
            return [SendResult.Failure(string.Empty, 0, null, "Не задан получатель письма.")];
        }

        // Валидация адресов до открытия SMTP-сессии (F-B8): невалидные адреса
        // (включая переводы строк — защиту от SMTP-инъекции) дают отказ по
        // каждому такому адресу без соединения с сервером.
        var невалидные = new List<string>();
        var валидные = new List<string>();

        foreach (var адрес in адреса)
        {
            if (ПроверитьАдрес(адрес, out var ошибкаАдреса))
            {
                // Нормализация: адрес приводится к виду без имени в угловых
                // скобках (метод есть в ПроверитьАдрес — повторяем разбор).
                валидные.Add(РазобратьАдрес(адрес).Адрес);
            }
            else
            {
                невалидные.Add(адрес);
            }
        }

        if (невалидные.Count > 0)
        {
            var список = string.Join(
                ", ",
                невалидные.Select(адрес => $"'{адрес}'"));

            List<SendResult> отказы = невалидные
                .Select(адрес => SendResult.Failure(
                    адрес,
                    0,
                    null,
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"Неверный формат адреса получателя '{адрес}'.")))
                .ToList();

            if (валидные.Count == 0)
            {
                return отказы;
            }

            // Сессия открывается только для валидных адресов; отказы по
            // невалидным добавляются к итогам.
            var результатыВалидных = ОтправитьВалидным(message, валидные, ct);

            return [.. отказы, .. результатыВалидных];
        }

        return ОтправитьВалидным(message, валидные, ct);
    }

    /// <summary>
    /// Открывает SMTP-сессию и отправляет письмо проверенным получателям
    /// (F-B8): отделяет сетевой диалог от валидации адресов.
    /// </summary>
    /// <param name="message">Письмо.</param>
    /// <param name="адреса">Проверенные адреса получателей.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результаты отправки.</returns>
    private IReadOnlyList<SendResult> ОтправитьВалидным(
        MailMessage message,
        IReadOnlyList<string> адреса,
        CancellationToken ct)
    {
        var отправитель = string.IsNullOrWhiteSpace(message.From) ? АдресОтправителя : message.From.Trim();

        if (!ПроверитьАдрес(отправитель, out var ошибкаОтправителя))
        {
            return ОшибкаДляВсех(
                адреса,
                0,
                null,
                string.Create(CultureInfo.CurrentCulture, $"Неверный формат адреса отправителя: {ошибкаОтправителя}"));
        }

        var обратныйАдрес = string.IsNullOrWhiteSpace(message.ReplyTo) ? ОбратныйАдрес : message.ReplyTo.Trim();

        if (!string.IsNullOrWhiteSpace(обратныйАдрес)
            && !ПроверитьАдрес(обратныйАдрес, out var ошибкаОтвета))
        {
            return ОшибкаДляВсех(
                адреса,
                0,
                null,
                string.Create(CultureInfo.CurrentCulture, $"Неверный формат обратного адреса: {ошибкаОтвета}"));
        }

        Exception? последняяОшибка = null;

        for (var попытка = 1; попытка <= 2; попытка++)
        {
            try
            {
                return ОтправитьПисьмо(message, отправитель, обратныйАдрес, адреса, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                // Таймаут — единственная причина для повторной попытки
                // (ТЗ §8.7, функция 100). Если тело письма уже отправлено —
                // повтор ЗАПРЕЩЁН (дублирование, корректирующий проход №100).
                последняяОшибка = ex;

                if (_телоОтправленоВСессии || попытка == 2)
                {
                    break;
                }
            }
            catch (IOException ex)
            {
                последняяОшибка = ex;

                if (!ЭтоТаймаут(ex) || попытка == 2)
                {
                    break;
                }
            }
            catch (SocketException ex)
            {
                последняяОшибка = ex;

                if (!ЭтоТаймаут(ex) || попытка == 2)
                {
                    break;
                }
            }
            catch (InvalidOperationException ex)
            {
                последняяОшибка = ex;
                break;
            }
            catch (AuthenticationException ex)
            {
                последняяОшибка = ex;
                break;
            }
            catch (CryptographicException ex)
            {
                последняяОшибка = ex;
                break;
            }
        }

        var детализация = последняяОшибка is null
            ? "Неизвестная ошибка отправки."
            : SmtpOptions.ОписатьОшибку(последняяОшибка);

        return ОшибкаДляВсех(адреса, 0, null, детализация);
    }

    /// <summary>
    /// Выполняет SMTP-транзакцию отправки письма.
    /// </summary>
    /// <param name="message">Письмо.</param>
    /// <param name="отправитель">Адрес отправителя.</param>
    /// <param name="обратныйАдрес">Обратный адрес для ответа.</param>
    /// <param name="адреса">Адреса получателей.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результаты отправки по каждому получателю.</returns>
    /// <summary>
    /// Признак «тело письма с терминалом уже отправлено в текущей сессии»:
    /// после него повторная попытка ЗАПРЕЩЕНА — письмо могло быть принято
    /// сервером, и повтор продублировал бы его (корректирующий проход №100).
    /// </summary>
    private bool _телоОтправленоВСессии;

    private IReadOnlyList<SendResult> ОтправитьПисьмо(
        MailMessage message,
        string отправитель,
        string? обратныйАдрес,
        IReadOnlyList<string> адреса,
        CancellationToken ct)
    {
        // Сброс следа отклонённого сертификата от предыдущей сессии (F-B9).
        ОтклонённыйОтпечаток = string.Empty;
        _телоОтправленоВСессии = false;

        using var сессия = СоздатьСоединение();

        сессия.Подключиться(ct);

        // ImplicitTls (ТЗ §8.7, функция 94 — порт 465, режим «SSL»):
        // TLS-рукопожатие выполняется ДО первого SMTP-обмена, иначе EHLO
        // уходит в TLS-порт открытым текстом и сервер разрывает соединение.
        if (РежимЗащиты == SslMode.ImplicitTls)
        {
            if (!ПерейтиВЗащищённыйРежимПоУмолчанию(сессия, адреса, out var ошибкаСертификата))
            {
                return ошибкаСертификата;
            }
        }

        var приветствие = сессия.ПрочитатьОтвет();

        if (!приветствие.IsSuccess)
        {
            return ОшибкаДляВсех(
                адреса,
                приветствие.Code,
                приветствие.Text,
                string.Create(CultureInfo.CurrentCulture, $"Сервер отклонил соединение: {приветствие.Text}"));
        }

        var возможности = ВыполнитьEHLO(сессия, адреса);

        if (РежимЗащиты == SslMode.StartTls)
        {
            if (!возможности.Contains("STARTTLS", StringComparer.OrdinalIgnoreCase))
            {
                return ОшибкаДляВсех(
                    адреса,
                    0,
                    null,
                    "Сервер не поддерживает защищённое соединение STARTTLS.");
            }

            var ответStartTls = сессия.Выполнить("STARTTLS");

            if (!ответStartTls.IsSuccess)
            {
                return ОшибкаДляВсех(
                    адреса,
                    ответStartTls.Code,
                    ответStartTls.Text,
                    string.Create(CultureInfo.CurrentCulture, $"Сервер отклонил STARTTLS: {ответStartTls.Text}"));
            }

            // Отклонение сертификата фиксируется AuthenticationException из
            // рукопожатия с отпечатком в ОтклонённыйОтпечаток (F-B9).
            if (!ПерейтиВЗащищённыйРежимПоУмолчанию(сессия, адреса, out var ошибкаСертификата))
            {
                return ошибкаСертификата;
            }

            возможности = ВыполнитьEHLO(сессия, адреса);
        }

        if (!string.IsNullOrWhiteSpace(Настройки.Login))
        {
            var пароль = _опции.GetPassword(Настройки);

            if (пароль is null)
            {
                return ОшибкаДляВсех(
                    адреса,
                    0,
                    null,
                    "Не удалось расшифровать пароль учётной записи: защищённый блок недоступен.");
            }

            var ответАвторизации = Авторизоваться(сессия, возможности, Настройки.Login ?? string.Empty, пароль);

            if (!ответАвторизации.IsSuccess)
            {
                return ОшибкаДляВсех(
                    адреса,
                    ответАвторизации.Code,
                    ответАвторизации.Text,
                    string.Create(CultureInfo.CurrentCulture, $"Ошибка авторизации на сервере: {ответАвторизации.Text}"));
            }
        }

        var адресОтправителя = РазобратьАдрес(отправитель).Адрес;

        var ответОтправителя = сессия.Выполнить($"MAIL FROM:<{адресОтправителя}>");

        if (!ответОтправителя.IsSuccess)
        {
            return ОшибкаДляВсех(
                адреса,
                ответОтправителя.Code,
                ответОтправителя.Text,
                string.Create(CultureInfo.CurrentCulture, $"Сервер отклонил адрес отправителя: {ответОтправителя.Text}"));
        }

        var результаты = new List<SendResult>();
        var принятые = new List<string>();

        foreach (var адрес in адреса)
        {
            var ответПолучателя = сессия.Выполнить($"RCPT TO:<{адрес}>");

            if (ответПолучателя.IsSuccess)
            {
                принятые.Add(адрес);
            }
            else
            {
                // Отказ сервера по одному получателю не прерывает партию:
                // письмо помечается ошибкой, отправка продолжается.
                результаты.Add(SendResult.Failure(
                    адрес,
                    ответПолучателя.Code,
                    ответПолучателя.Text,
                    string.Create(CultureInfo.CurrentCulture, $"Сервер отклонил получателя: {ответПолучателя.Text}")));
            }
        }

        if (принятые.Count == 0)
        {
            сессия.Выполнить("RSET");

            return результаты;
        }

        var ответДанных = сессия.Выполнить("DATA");

        // Ответ 354 — сервер готов принять тело письма (F-B1): промежуточный
        // код не является отказом; любой другой ответ прерывает передачу.
        if (ответДанных.Code != 354)
        {
            return [
                .. результаты,
                .. ОшибкаДляВсех(
                    принятые,
                    ответДанных.Code,
                    ответДанных.Text,
                    string.Create(CultureInfo.CurrentCulture, $"Сервер отклонил передачу данных: {ответДанных.Text}"))];
        }

        сессия.ОтправитьТело(ПостроитьПисьмо(message, отправитель, обратныйАдрес, принятые));

        // С момента отправки терминала письма оно могло быть ПРИНЯТО сервером
        // (корректирующий проход №100): любой сбой далее — не причина для
        // повторной попытки (дублирование письма). Таймаут чтения финального
        // 250 трактуется как постановка в очередь; QUIT — безусловно
        // наилучшим образом; исход получателям — «письмо могло быть принято,
        // проверьте журнал сервера».
        _телоОтправленоВСессии = true;

        try
        {
            var ответПисьма = сессия.ПрочитатьОтвет();

            if (!ответПисьма.IsSuccess)
            {
                return [
                    .. результаты,
                    .. ОшибкаДляВсех(
                        принятые,
                        ответПисьма.Code,
                        ответПисьма.Text,
                        string.Create(CultureInfo.CurrentCulture, $"Сервер отклонил письмо: {ответПисьма.Text}"))];
            }

            try
            {
                сессия.Выполнить("QUIT");
            }
            catch (TimeoutException)
            {
                // QUIT — наилучшим образом: письмо уже поставлено в очередь.
            }

            foreach (var адрес in принятые)
            {
                результаты.Add(SendResult.Success(адрес, ответПисьма.Code, ответПисьма.Text));
            }

            return результаты;
        }
        catch (Exception ошибка) when (ошибка is TimeoutException or IOException)
        {
            // Письмо могло быть принято — повтор ЗАПРЕЩЁН (№100):
            // любая ошибка после отправки тела (таймаут чтения ответа,
            // обрыв соединения) не приводит к дублирующей повторной
            // отправке; результат помечается специальным текстом.
            return [
                .. результаты,
                .. ОшибкаДляВсех(
                    принятые,
                    0,
                    null,
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"Письмо могло быть принято, но ответ сервера не получен ({ошибка.GetType().Name}: {ошибка.Message}) — повтор отправки запрещён; проверьте журнал сервера"))];
        }
    }

    /// <summary>
    /// Переводит сессию в защищённый режим и формирует отказ по отклонённому
    /// сертификату (F-B9): AuthenticationException от рукопожатия перехватывается,
    /// отпечаток отклонённого сертификата читается из
    /// <see cref="ОтклонённыйОтпечаток"/>.
    /// </summary>
    /// <param name="сессия">SMTP-сессия.</param>
    /// <param name="адреса">Адреса получателей (для сообщения об ошибке).</param>
    /// <param name="ошибка">Готовый отказ; пусто — переход выполнен.</param>
    /// <returns><see langword="true"/>, если переход выполнен.</returns>
    private bool ПерейтиВЗащищённыйРежимПоУмолчанию(
        SmtpSession сессия,
        IReadOnlyList<string> адреса,
        out IReadOnlyList<SendResult> ошибка)
    {
        try
        {
            сессия.ПерейтиВЗащищённыйРежим(ПроверитьСертификат);
        }
        catch (AuthenticationException)
        {
            var отпечаток = string.IsNullOrEmpty(ОтклонённыйОтпечаток)
                ? "отпечаток не определён"
                : ОтклонённыйОтпечаток;

            ошибка = ОшибкаДляВсех(
                адреса,
                0,
                null,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{CertificateRejectedMessage} (отпечаток {отпечаток})."));

            return false;
        }

        if (!string.IsNullOrEmpty(ОтклонённыйОтпечаток))
        {
            ошибка = ОшибкаДляВсех(
                адреса,
                0,
                null,
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"{CertificateRejectedMessage} (отпечаток {ОтклонённыйОтпечаток})."));

            return false;
        }

        ошибка = Array.Empty<SendResult>();

        return true;
    }

    /// <summary>
    /// Выполняет команду EHLO (с откатом на HELO) и возвращает перечень
    /// поддерживаемых сервером возможностей.
    /// </summary>
    /// <param name="сессия">SMTP-сессия.</param>
    /// <param name="адреса">Адреса получателей (для сообщения об ошибке).</param>
    /// <returns>Набор возможностей сервера.</returns>
    private static IReadOnlySet<string> ВыполнитьEHLO(SmtpSession сессия, IReadOnlyList<string> адреса)
    {
        var ответ = сессия.Выполнить($"EHLO {ИмяХоста}");

        if (!ответ.IsSuccess)
        {
            ответ = сессия.Выполнить($"HELO {ИмяХоста}");
        }

        var возможности = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var строка in ответ.Text.Split('\n'))
        {
            var очищенная = строка.Trim();

            if (очищенная.Length >= 4 && int.TryParse(
                    очищенная.AsSpan(0, 3),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                очищенная = очищенная[4..].Trim();
            }

            if (очищенная.Length > 0)
            {
                возможности.Add(очищенная.Split(' ')[0]);
            }
        }

        return возможности;
    }

    /// <summary>
    /// Выполняет авторизацию на сервере: AUTH LOGIN либо AUTH PLAIN — в
    /// зависимости от поддерживаемых сервером механизмов.
    /// </summary>
    /// <param name="сессия">SMTP-сессия.</param>
    /// <param name="возможности">Возможности сервера.</param>
    /// <param name="логин">Логин учётной записи.</param>
    /// <param name="пароль">Открытый текст пароля.</param>
    /// <returns>Ответ сервера на авторизацию.</returns>
    private static SmtpResponse Авторизоваться(
        SmtpSession сессия,
        IReadOnlySet<string> возможности,
        string логин,
        string пароль)
    {
        if (возможности.Contains("AUTH", StringComparer.OrdinalIgnoreCase))
        {
            var ответ = сессия.Выполнить("AUTH PLAIN");

            // Ответ 334 — сервер ждёт токен авторизации (F-B1): промежуточный
            // код не считается отказом и не уводит сессию в фолбэк LOGIN.
            if (ответ.Code == 334)
            {
                var токен = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\0{логин}\0{пароль}")));

                return сессия.Выполнить(токен);
            }

            if (ответ.IsSuccess)
            {
                return ответ;
            }

            // Фолбэк AUTH LOGIN (ТЗ §8.7, функции 93/100): часть серверов
            // объявляет AUTH, но поддерживает только механизм LOGIN — отказ
            // PLAIN не завершает авторизацию попыткой LOGIN.
        }

        var ответLogin = сессия.Выполнить("AUTH LOGIN");

        // Ответ 334 — сервер запросил имя пользователя (F-B1); готовый успех
        // (235) или отказ возвращаются как есть.
        if (ответLogin.Code != 334)
        {
            return ответLogin;
        }

        var логин64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(логин));
        var ответЛогина = сессия.Выполнить(логин64);

        // Ответ 334 — сервер запросил пароль (F-B1).
        if (ответЛогина.Code != 334)
        {
            return ответЛогина;
        }

        var пароль64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(пароль));

        return сессия.Выполнить(пароль64);
    }

    /// <summary>
    /// Формирует одинаковый результат ошибки для всех получателей письма.
    /// </summary>
    /// <param name="адреса">Адреса получателей.</param>
    /// <param name="код">Код ответа сервера.</param>
    /// <param name="ответ">Текст ответа сервера.</param>
    /// <param name="ошибка">Текст ошибки на русском языке.</param>
    /// <returns>Перечень результатов.</returns>
    private static IReadOnlyList<SendResult> ОшибкаДляВсех(
        IReadOnlyList<string> адреса,
        int код,
        string? ответ,
        string ошибка)
    {
        if (адреса.Count == 0)
        {
            return [SendResult.Failure(string.Empty, код, ответ, ошибка)];
        }

        return адреса.Select(адрес => SendResult.Failure(адрес, код, ответ, ошибка)).ToList();
    }

    /// <summary>
    /// Определяет, является ли ошибка таймаутом (для повторной попытки).
    /// </summary>
    /// <param name="ошибка">Исключение.</param>
    /// <returns><see langword="true"/>, если это таймаут.</returns>
    private static bool ЭтоТаймаут(Exception ошибка)
    {
        for (var текущий = ошибка; текущий is not null; текущий = текущий.InnerException)
        {
            if (текущий is TimeoutException)
            {
                return true;
            }

            if (текущий is SocketException socket && socket.SocketErrorCode == SocketError.TimedOut)
            {
                return true;
            }

            if (текущий is IOException
                && текущий.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Приводит отпечаток сертификата к сравнимому виду: убирает разделители.
    /// </summary>
    /// <param name="отпечаток">Отпечаток сертификата.</param>
    /// <returns>Нормализованный отпечаток.</returns>
    private static string НормализоватьОтпечаток(string отпечаток)
        => отпечаток.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();

    /// <summary>
    /// Проверяет сертификат сервера: самоподписанные сертификаты по умолчанию
    /// не принимаются; доверие выдаётся решением пользователя (ТЗ §8.7, функция 94).
    /// </summary>
    /// <param name="отправитель">Источник проверки.</param>
    /// <param name="сертификат">Сертификат сервера.</param>
    /// <param name="цепочка">Цепочка сертификатов.</param>
    /// <param name="ошибки">Ошибки проверки.</param>
    /// <returns><see langword="true"/>, если сертификат принят.</returns>
    private bool ПроверитьСертификат(
        object отправитель,
        X509Certificate? сертификат,
        X509Chain? цепочка,
        SslPolicyErrors ошибки)
    {
        if (ошибки == SslPolicyErrors.None)
        {
            return true;
        }

        if (сертификат is not X509Certificate2 сертификат2)
        {
            ОтклонённыйОтпечаток = string.Empty;
            return false;
        }

        var отпечаток = сертификат2.Thumbprint ?? string.Empty;

        if (Настройки.TrustSelfSignedCertificate
            && !string.IsNullOrWhiteSpace(Настройки.TrustedCertificateFingerprint)
            && string.Equals(
                НормализоватьОтпечаток(отпечаток),
                НормализоватьОтпечаток(Настройки.TrustedCertificateFingerprint),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Решение пользователя «Отклонить»: отправка прерывается с понятным
        // сообщением.
        ОтклонённыйОтпечаток = отпечаток;

        return false;
    }

    /// <summary>
    /// Создаёт SMTP-сессию с учётом режима защиты и настроек прокси.
    /// </summary>
    /// <returns>Готовая сессия.</returns>
    private SmtpSession СоздатьСоединение()
    {
        var таймаут = TimeSpan.FromSeconds(
            Настройки.TimeoutSeconds > 0 ? Настройки.TimeoutSeconds : 30);

        return new SmtpSession(таймаут, Настройки.Server, Настройки.Port, Прокси, _опции);
    }

    /// <summary>
    /// Разбирает адрес вида «Отображаемое имя &lt;адрес@сервер&gt;».
    /// </summary>
    /// <param name="значение">Разбираемое значение.</param>
    /// <returns>Отображаемое имя и почтовый адрес.</returns>
    private static (string Имя, string Адрес) РазобратьАдрес(string значение)
    {
        var текст = значение.Trim();
        var начало = текст.LastIndexOf('<');
        var конец = текст.LastIndexOf('>');

        if (начало >= 0 && конец > начало)
        {
            var имя = текст[..начало].Trim().Trim('"').Trim();
            var адрес = текст[(начало + 1)..конец].Trim();

            return (имя, адрес);
        }

        return (string.Empty, текст);
    }

    /// <summary>
    /// Оформляет адрес для заголовка письма: отображаемое имя кодируется по RFC 2047.
    /// </summary>
    /// <param name="имя">Отображаемое имя.</param>
    /// <param name="адрес">Почтовый адрес.</param>
    /// <returns>Значение заголовка <c>From</c> или <c>To</c>.</returns>
    private static string ОформитьАдрес(string имя, string адрес)
    {
        if (string.IsNullOrWhiteSpace(имя))
        {
            return адрес;
        }

        var закодированное = КодироватьЗначениеЗаголовка(имя);

        if (закодированное.StartsWith("=?", StringComparison.Ordinal))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{закодированное} <{адрес}>");
        }

        return string.Create(CultureInfo.InvariantCulture, $"\"{имя.Replace("\"", "'", StringComparison.Ordinal)}\" <{адрес}>");
    }

    /// <summary>
    /// Кодирует значение заголовка по RFC 2047 при наличии не-ASCII символов.
    /// </summary>
    /// <param name="значение">Значение заголовка.</param>
    /// <returns>Закодированное значение.</returns>
    private static string КодироватьЗначениеЗаголовка(string значение)
    {
        if (string.IsNullOrEmpty(значение))
        {
            return string.Empty;
        }

        foreach (var символ in значение)
        {
            if (символ > 127)
            {
                return "=?UTF-8?B?"
                    + Convert.ToBase64String(Encoding.UTF8.GetBytes(значение))
                    + "?=";
            }
        }

        return значение;
    }

    /// <summary>
    /// Формирует текст письма (заголовки и тело) в формате MIME.
    /// </summary>
    /// <param name="письмо">Письмо.</param>
    /// <param name="отправитель">Адрес отправителя.</param>
    /// <param name="обратныйАдрес">Обратный адрес для ответа.</param>
    /// <param name="адреса">Адреса получателей.</param>
    /// <returns>Текст письма с окончаниями строк CRLF.</returns>
    private static string ПостроитьПисьмо(
        MailMessage письмо,
        string отправитель,
        string? обратныйАдрес,
        IReadOnlyList<string> адреса)
    {
        var разобранный = РазобратьАдрес(отправитель);
        var html = письмо.BodyFormat == BodyFormat.Html;

        var текст = new StringBuilder();

        текст.Append("From: ").Append(ОформитьАдрес(разобранный.Имя, разобранный.Адрес)).Append("\r\n");
        текст.Append("To: ")
            .Append(string.Join(", ", адреса.Select(адрес => ОформитьАдрес(string.Empty, адрес))))
            .Append("\r\n");

        if (!string.IsNullOrWhiteSpace(обратныйАдрес))
        {
            var разобранныйОтвет = РазобратьАдрес(обратныйАдрес);
            текст.Append("Reply-To: ").Append(ОформитьАдрес(разобранныйОтвет.Имя, разобранныйОтвет.Адрес)).Append("\r\n");
        }

        текст.Append("Subject: ").Append(КодироватьЗначениеЗаголовка(письмо.Subject)).Append("\r\n");
        текст.Append("Date: ").Append(DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture)).Append("\r\n");
        текст.Append("MIME-Version: 1.0").Append("\r\n");
        текст.Append("X-Mailer: Picket302").Append("\r\n");

        var вложения = письмо.Attachments.Where(File.Exists).ToList();

        if (вложения.Count == 0)
        {
            текст.Append("Content-Type: ")
                .Append(html ? "text/html" : "text/plain")
                .Append("; charset=\"utf-8\"")
                .Append("\r\n");
            текст.Append("Content-Transfer-Encoding: ")
                .Append(html ? "base64" : "quoted-printable")
                .Append("\r\n");
            текст.Append("\r\n");
            текст.Append(html ? ЗакодироватьBase64(письмо.Body) : ЗакодироватьQuotedPrintable(письмо.Body));

            return текст.ToString();
        }

        var граница = "=_Picket302_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        текст.Append("Content-Type: multipart/mixed; boundary=\"")
            .Append(граница)
            .Append("\"")
            .Append("\r\n");
        текст.Append("\r\n");
        текст.Append("--").Append(граница).Append("\r\n");
        текст.Append("Content-Type: ")
            .Append(html ? "text/html" : "text/plain")
            .Append("; charset=\"utf-8\"")
            .Append("\r\n");
        текст.Append("Content-Transfer-Encoding: ")
            .Append(html ? "base64" : "quoted-printable")
            .Append("\r\n");
        текст.Append("\r\n");
        текст.Append(html ? ЗакодироватьBase64(письмо.Body) : ЗакодироватьQuotedPrintable(письмо.Body));
        текст.Append("\r\n");

        foreach (var вложение in вложения)
        {
            var имя = Path.GetFileName(вложение);

            // Имя файла уходит в заголовок: не-ASCII (кириллица) кодируется по
            // RFC 2047, иначе ASCII-писатель заменяет символы на «?»
            // (ТЗ §8.7, функция 98; §10.2 — Unicode/UTF-8 везде).
            var имяЗаголовка = КодироватьЗначениеЗаголовка(имя);

            текст.Append("--").Append(граница).Append("\r\n");
            текст.Append("Content-Type: application/octet-stream; name=\"")
                .Append(имяЗаголовка)
                .Append("\"")
                .Append("\r\n");
            текст.Append("Content-Disposition: attachment; filename=\"")
                .Append(имяЗаголовка)
                .Append("\"")
                .Append("\r\n");
            текст.Append("Content-Transfer-Encoding: base64").Append("\r\n");
            текст.Append("\r\n");
            текст.Append(ЗакодироватьBase64(File.ReadAllBytes(вложение)));
            текст.Append("\r\n");
        }

        текст.Append("--").Append(граница).Append("--").Append("\r\n");

        return текст.ToString();
    }

    /// <summary>
    /// Кодирует текст в quoted-printable с учётом UTF-8 (ТЗ §8.7, функция 100).
    /// </summary>
    /// <param name="значение">Кодируемый текст.</param>
    /// <returns>Текст в кодировке quoted-printable.</returns>
    private static string ЗакодироватьQuotedPrintable(string значение)
    {
        if (string.IsNullOrEmpty(значение))
        {
            return string.Empty;
        }

        var байты = Encoding.UTF8.GetBytes(значение.Replace("\r\n", "\n", StringComparison.Ordinal));
        var текст = new StringBuilder(байты.Length + 16);
        var вСтроке = 0;

        foreach (var байт in байты)
        {
            if (байт == (byte)'\n')
            {
                текст.Append("\r\n");
                вСтроке = 0;
                continue;
            }

            string часть;

            if (байт == (byte)'=')
            {
                часть = "=3D";
            }
            else if (байт is 32 or 9)
            {
                часть = ((char)байт).ToString();
            }
            else if (байт is >= 33 and <= 126)
            {
                часть = ((char)байт).ToString();
            }
            else
            {
                часть = "=" + байт.ToString("X2", CultureInfo.InvariantCulture);
            }

            if (вСтроке + часть.Length > 75)
            {
                текст.Append("=\r\n");
                вСтроке = 0;
            }

            текст.Append(часть);
            вСтроке += часть.Length;
        }

        return текст.ToString();
    }

    /// <summary>
    /// Кодирует данные в base64 с разбивкой на строки длиной 76 символов
    /// (требование RFC 2045 к телу письма).
    /// </summary>
    /// <param name="значение">Кодируемый текст в UTF-8.</param>
    /// <returns>Текст в кодировке base64.</returns>
    private static string ЗакодироватьBase64(string значение)
        => ЗакодироватьBase64(Encoding.UTF8.GetBytes(значение ?? string.Empty));

    /// <summary>
    /// Кодирует данные в base64 с разбивкой на строки длиной 76 символов.
    /// </summary>
    /// <param name="данные">Кодируемые данные.</param>
    /// <returns>Текст в кодировке base64.</returns>
    private static string ЗакодироватьBase64(byte[] данные)
    {
        var base64 = Convert.ToBase64String(данные);
        var текст = new StringBuilder(base64.Length + 64);

        for (var позиция = 0; позиция < base64.Length; позиция += 76)
        {
            текст.Append(base64, позиция, Math.Min(76, base64.Length - позиция));
            текст.Append("\r\n");
        }

        return текст.ToString();
    }
}

/// <summary>
/// SMTP-сессия: соединение с сервером (напрямую или через прокси), переход в
/// защищённый режим и обмен командами с сервером.
/// </summary>
internal sealed class SmtpSession : IDisposable
{
    private readonly TimeSpan _таймаут;
    private readonly string _сервер;
    private readonly int _порт;
    private readonly ProxySettings? _прокси;
    private readonly SmtpOptions _опции;

    private TcpClient? _клиент;
    private NetworkStream? _поток;
    private SslStream? _ssl;
    private StreamReader? _читатель;
    private StreamWriter? _писатель;

    /// <summary>
    /// Создаёт SMTP-сессию.
    /// </summary>
    /// <param name="таймаут">Таймаут соединения и операций.</param>
    /// <param name="сервер">Адрес SMTP-сервера.</param>
    /// <param name="порт">Порт SMTP-сервера.</param>
    /// <param name="прокси">Настройки прокси-сервера; <see langword="null"/> — прямое соединение.</param>
    /// <param name="опции">Набор настроек почтового клиента.</param>
    public SmtpSession(
        TimeSpan таймаут,
        string сервер,
        int порт,
        ProxySettings? прокси,
        SmtpOptions опции)
    {
        _таймаут = таймаут;
        _сервер = сервер;
        _порт = порт;
        _прокси = прокси;
        _опции = опции;
    }

    /// <summary>
    /// Устанавливает соединение с сервером; при заданном прокси соединение
    /// устанавливается через туннель HTTP CONNECT.
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
    public void Подключиться(CancellationToken ct)
    {
        _клиент = new TcpClient { NoDelay = true };

        using var ограничение = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ограничение.CancelAfter(_таймаут);

        // При включённом прокси TCP-соединение устанавливается с самим
        // прокси-сервером, а туннель CONNECT ведёт к SMTP-серверу (F-B2):
        // прежде сокет подключался к SMTP-серверу напрямую, и CONNECT
        // уходил не по адресу.
        (string Хост, int Порт)? адресПрокси = _прокси is { Enabled: true } ? АдресПрокси() : null;
        var хостСоединения = адресПрокси?.Хост ?? _сервер;
        var портСоединения = адресПрокси?.Порт ?? _порт;

        try
        {
            _клиент.ConnectAsync(хостСоединения, портСоединения, ограничение.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"Превышено время ожидания соединения с сервером {хостСоединения}:{портСоединения}."));
        }

        _поток = _клиент.GetStream();
        _поток.ReadTimeout = (int)_таймаут.TotalMilliseconds;
        _поток.WriteTimeout = (int)_таймаут.TotalMilliseconds;

        if (адресПрокси is not null)
        {
            ВыполнитьТуннельПрокси();
        }

        ИнициализироватьПотоки();
    }

    /// <summary>
    /// Разбирает адрес прокси-сервера из настроек: хост и порт, к которым
    /// устанавливается TCP-соединение (F-B2).
    /// </summary>
    /// <returns>Хост и порт прокси-сервера.</returns>
    private (string Хост, int Порт) АдресПрокси()
    {
        var прокси = new ProxySettingsMapper(_опции.Секреты).BuildProxy(_прокси!);

        if (прокси is null || прокси.Address is null)
        {
            throw new InvalidOperationException(ProxySettingsMapper.UnavailableMessage);
        }

        return (прокси.Address.Host, прокси.Address.Port);
    }

    /// <summary>
    /// Переводит соединение в защищённый режим (STARTTLS).
    /// </summary>
    /// <param name="проверка">Проверка сертификата сервера.</param>
    public void ПерейтиВЗащищённыйРежим(RemoteCertificateValidationCallback проверка)
    {
        if (_поток is null)
        {
            throw new InvalidOperationException("Соединение с SMTP-сервером не установлено.");
        }

        _ssl = new SslStream(_поток, leaveInnerStreamOpen: false, проверка);
        _ssl.ReadTimeout = (int)_таймаут.TotalMilliseconds;
        _ssl.WriteTimeout = (int)_таймаут.TotalMilliseconds;

        _ssl.AuthenticateAsClientAsync(_сервер).GetAwaiter().GetResult();

        ИнициализироватьПотоки();
    }

    /// <summary>
    /// Читает ответ сервера (возможно многострочный).
    /// </summary>
    /// <returns>Ответ сервера.</returns>
    public SmtpResponse ПрочитатьОтвет()
    {
        var строки = new List<string>();
        var код = 0;

        while (true)
        {
            var строка = _читатель!.ReadLine();

            if (строка is null)
            {
                throw new IOException("Соединение с SMTP-сервером закрыто.");
            }

            строки.Add(строка);

            if (строка.Length >= 4
                && int.TryParse(
                    строка.AsSpan(0, 3),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out код))
            {
                if (строка[3] != '-')
                {
                    break;
                }

                continue;
            }

            код = 0;
            break;
        }

        return new SmtpResponse(код, string.Join("\n", строки));
    }

    /// <summary>
    /// Выполняет команду сервера и читает ответ.
    /// </summary>
    /// <param name="команда">Текст команды.</param>
    /// <returns>Ответ сервера.</returns>
    public SmtpResponse Выполнить(string команда)
    {
        _писатель!.Write(команда);
        _писатель.Write("\r\n");
        _писатель.Flush();

        return ПрочитатьОтвет();
    }

    /// <summary>
    /// Передаёт тело письма и завершает его командой «.» (RFC 5321).
    /// </summary>
    /// <param name="тело">Текст письма.</param>
    /// <remarks>
    /// Строки собираются в один буфер и записываются одним вызовом с одним
    /// Flush (F-B13): построчная запись с AutoFlush на каждой строке вызывала
    /// сетевую отправку на каждую строку, что замедляло большие письма.
    /// </remarks>
    public void ОтправитьТело(string тело)
    {
        var строки = тело.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var буфер = new StringBuilder(тело.Length + 16);

        for (var индекс = 0; индекс < строки.Length; индекс++)
        {
            var строка = строки[индекс];

            if (индекс == строки.Length - 1 && строка.Length == 0)
            {
                continue;
            }

            if (строка.StartsWith(".", StringComparison.Ordinal))
            {
                строка = "." + строка;
            }

            буфер.Append(строка);
            буфер.Append("\r\n");
        }

        буфер.Append(".\r\n");

        _писатель!.Write(буфер.ToString());
        _писатель.Flush();
    }

    /// <summary>
    /// Освобождает ресурсы соединения.
    /// </summary>
    public void Dispose()
    {
        _читатель?.Dispose();
        _писатель?.Dispose();
        _ssl?.Dispose();
        _поток?.Dispose();
        _клиент?.Dispose();

        _читатель = null;
        _писатель = null;
        _ssl = null;
        _поток = null;
        _клиент = null;
    }

    /// <summary>
    /// Создаёт потоки чтения и записи поверх текущего соединения.
    /// </summary>
    private void ИнициализироватьПотоки()
    {
        var поток = (Stream?)_ssl ?? _поток;

        if (поток is null)
        {
            throw new InvalidOperationException("Соединение с SMTP-сервером не установлено.");
        }

        _читатель?.Dispose();
        _писатель?.Dispose();

        _читатель = new StreamReader(
            поток,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);

        _писатель = new StreamWriter(поток, new ASCIIEncoding(), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\r\n",
        };
    }

    /// <summary>
    /// Устанавливает туннель до SMTP-сервера через прокси (команда HTTP CONNECT).
    /// </summary>
    private void ВыполнитьТуннельПрокси()
    {
        var прокси = new ProxySettingsMapper(_опции.Секреты).BuildProxy(_прокси!);

        if (прокси is null || прокси.Address is null)
        {
            throw new InvalidOperationException(ProxySettingsMapper.UnavailableMessage);
        }

        var цель = string.Create(CultureInfo.InvariantCulture, $"{_сервер}:{_порт}");

        var запрос = string.Create(CultureInfo.InvariantCulture, $"CONNECT {цель} HTTP/1.1\r\nHost: {цель}\r\n");

        if (прокси.Credentials is NetworkCredential учётные && !string.IsNullOrEmpty(учётные.UserName))
        {
            var токен = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{учётные.UserName}:{учётные.Password}")));

            запрос += string.Create(CultureInfo.InvariantCulture, $"Proxy-Authorization: Basic {токен}\r\n");
        }

        запрос += "\r\n";

        var байты = Encoding.ASCII.GetBytes(запрос);
        _поток!.Write(байты, 0, байты.Length);
        _поток.Flush();

        var ответ = ПрочитатьОтветПрокси();

        // Статусная строка ответа прокси: «HTTP/1.1 200 …» (F-B2).
        var строкаСтатуса = ответ.Split('\r', '\n')
            .FirstOrDefault(строка => строка.Length > 0) ?? string.Empty;

        if (!строкаСтатуса.Contains(" 200 ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"Прокси-сервер отказал в туннеле: {строкаСтатуса}. {ProxySettingsMapper.UnavailableMessage}"));
        }
    }

    /// <summary>
    /// Читает заголовки ответа прокси-сервера до пустой строки.
    /// </summary>
    /// <returns>Текст ответа прокси-сервера.</returns>
    private string ПрочитатьОтветПрокси()
    {
        var буфер = new List<byte>();
        var чтение = new byte[1];

        while (true)
        {
            var получено = _поток!.Read(чтение, 0, 1);

            if (получено <= 0)
            {
                throw new IOException("Прокси-сервер закрыл соединение.");
            }

            буфер.Add(чтение[0]);

            var длина = буфер.Count;

            if (длина >= 4
                && буфер[длина - 4] == 13
                && буфер[длина - 3] == 10
                && буфер[длина - 2] == 13
                && буфер[длина - 1] == 10)
            {
                break;
            }

            if (длина > 8192)
            {
                throw new IOException("Превышен размер ответа прокси-сервера.");
            }
        }

        return Encoding.ASCII.GetString(буфер.ToArray());
    }
}
