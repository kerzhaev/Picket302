using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Domain.Common;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Integration;

namespace Picket302.Infrastructure.Mail;

/// <summary>
/// Результат пробного соединения с SMTP-сервером (ТЗ §8.7, функция 93:
/// «проверка связи с сервером — по кнопке в настройках»).
/// </summary>
/// <param name="IsSuccess">Признак успешного соединения.</param>
/// <param name="SmtpCode">Код ответа SMTP-сервера.</param>
/// <param name="ServerResponse">Текст ответа сервера.</param>
/// <param name="Error">Текст ошибки на русском языке.</param>
/// <param name="Detail">Детализация ошибки: таймаут, отказ сервера, отказ в соединении.</param>
public sealed record SmtpProbeResult(
    bool IsSuccess,
    int SmtpCode,
    string? ServerResponse,
    string? Error,
    string? Detail)
{
    /// <summary>
    /// Текст ошибки при невозможности соединения с сервером.
    /// </summary>
    public const string ConnectionFailedMessage = "Не удалось соединиться с SMTP-сервером";

    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Полный текст ошибки: базовая формулировка и детализация.
    /// </summary>
    public string Message
        => Error is null
            ? string.Empty
            : Detail is null ? Error : string.Concat(Error, ": ", Detail);
}

/// <summary>
/// Хранение паролей в защищённом виде (DPAPI) в текстовом представлении,
/// пригодном для файла настроек (ТЗ §8.7, функция 93: «пароль хранится в
/// DPAPI»; ТЗ §8.5, функция 76 — тот же механизм, что и для внешней базы).
/// </summary>
/// <remarks>
/// Защита и расшифровка выполняются только через
/// <see cref="DpapiSecretStore"/>: используется тот же обёрнутый вызов
/// <c>CryptProtectData</c>/<c>CryptUnprotectData</c>. В текстовом виде
/// защищённый блок хранится в Base64 — это кодирование, а не иной формат
/// защиты. Открытый текст пароля в настройках не сохраняется.
/// </remarks>
public static class ЗащищённыйПароль
{
    /// <summary>
    /// Защищает пароль средствами Windows (DPAPI).
    /// </summary>
    /// <param name="секреты">Хранилище секретов.</param>
    /// <param name="пароль">Открытый текст пароля.</param>
    /// <returns>Защищённый блок в текстовом представлении Base64.</returns>
    public static string Защитить(DpapiSecretStore секреты, string пароль)
    {
        ArgumentNullException.ThrowIfNull(секреты);
        ArgumentNullException.ThrowIfNull(пароль);

        return Convert.ToBase64String(секреты.Protect(пароль));
    }

    /// <summary>
    /// Расшифровывает защищённый пароль.
    /// </summary>
    /// <param name="секреты">Хранилище секретов.</param>
    /// <param name="защищённыйПароль">Защищённый блок в текстовом представлении Base64.</param>
    /// <returns>Открытый текст пароля; при пустом значении — <see langword="null"/>.</returns>
    public static string? Расшифровать(DpapiSecretStore секреты, string? защищённыйПароль)
    {
        ArgumentNullException.ThrowIfNull(секреты);

        if (string.IsNullOrWhiteSpace(защищённыйПароль))
        {
            return null;
        }

        byte[] блоб;

        try
        {
            блоб = Convert.FromBase64String(защищённыйПароль);
        }
        catch (FormatException)
        {
            return null;
        }

        return секреты.Unprotect(блоб);
    }
}

/// <summary>
/// Настройки почтового клиента: проверка учётной записи, пробное соединение
/// с сервером и хранение настроек (ТЗ §8.7, функция 93).
/// </summary>
/// <remarks>
/// <para>
/// Настройки хранятся в файле <c>%AppData%\Picket302\mail-settings.json</c> —
/// рядом с прочими настройками приложения. Пароль учётной записи и пароль
/// прокси записываются только в защищённом виде (DPAPI), см.
/// <see cref="ЗащищённыйПароль"/>.
/// </para>
/// </remarks>
public sealed class SmtpOptions
{
    /// <summary>Имя папки приложения в профиле пользователя.</summary>
    public const string КаталогПриложения = "Picket302";

    /// <summary>Имя файла настроек почтового клиента.</summary>
    public const string ИмяФайлаНастроек = "mail-settings.json";

    /// <summary>Минимально допустимый порт SMTP-сервера.</summary>
    public const int МинимальныйПорт = 1;

    /// <summary>Максимально допустимый порт SMTP-сервера.</summary>
    public const int МаксимальныйПорт = 65535;

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DpapiSecretStore _секреты;

    /// <summary>
    /// Создаёт набор настроек почтового клиента.
    /// </summary>
    /// <param name="секреты">
    /// Хранилище секретов DPAPI; по умолчанию — стандартная папка профиля
    /// пользователя.
    /// </param>
    public SmtpOptions(DpapiSecretStore? секреты = null)
    {
        _секреты = секреты ?? new DpapiSecretStore();
    }

    /// <summary>Путь к файлу настроек почтового клиента.</summary>
    public string ПутьФайлаНастроек { get; set; } = ПутьПоУмолчанию();

    /// <summary>
    /// Хранилище секретов DPAPI, применяемое для защиты и расшифровки паролей.
    /// </summary>
    public DpapiSecretStore Секреты => _секреты;

    /// <summary>
    /// Возвращает путь к файлу настроек по умолчанию —
    /// <c>%AppData%\Picket302\mail-settings.json</c>.
    /// </summary>
    /// <returns>Полный путь файла настроек.</returns>
    public static string ПутьПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        КаталогПриложения,
        ИмяФайлаНастроек);

    /// <summary>
    /// Проверяет настройки SMTP-учётной записи (ТЗ §8.7, функция 93:
    /// «настройка сервера, порта, логина и пароля»).
    /// </summary>
    /// <param name="settings">Проверяемые настройки.</param>
    /// <returns>Результат проверки.</returns>
    public ValidationResult ValidateSmtpSettings(SmtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var ошибки = new List<ValidationError>();

        if (!settings.HasServer)
        {
            ошибки.Add(new ValidationError(nameof(SmtpSettings.Server), SmtpSettings.ServerNotSpecifiedMessage));
        }

        if (settings.Port < МинимальныйПорт || settings.Port > МаксимальныйПорт)
        {
            ошибки.Add(new ValidationError(
                nameof(SmtpSettings.Port),
                string.Create(
                    CultureInfo.CurrentCulture,
                    $"Порт SMTP-сервера должен быть в диапазоне {МинимальныйПорт}..{МаксимальныйПорт}.")));
        }

        if (!string.IsNullOrWhiteSpace(settings.ProtectedPassword)
            && string.IsNullOrWhiteSpace(settings.Login))
        {
            ошибки.Add(new ValidationError(
                nameof(SmtpSettings.Login),
                "Задан пароль, но не указан логин."));
        }

        return ValidationResult.Failure(ошибки);
    }

    /// <summary>
    /// Выполняет пробное соединение с SMTP-сервером: устанавливает соединение и
    /// получает приветствие сервера, не отправляя письма (ТЗ §8.7, функция 93).
    /// </summary>
    /// <param name="settings">Настройки учётной записи.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат пробного соединения.</returns>
    public SmtpProbeResult ProbeConnection(SmtpSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.HasServer)
        {
            return new SmtpProbeResult(false, 0, null, SmtpSettings.ServerNotSpecifiedMessage, null);
        }

        var фасад = new SmtpClientFacade(settings, null, this);

        try
        {
            return фасад.ПроверитьСоединение(ct);
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
            ОписатьОшибку(ошибка));

    /// <summary>
    /// Читает настройки SMTP-учётной записи из файла настроек.
    /// </summary>
    /// <param name="путь">Путь к файлу настроек; при значении <see langword="null"/> используется путь по умолчанию.</param>
    /// <returns>Настройки учётной записи; при отсутствии файла — пустые настройки.</returns>
    public SmtpSettings Load(string? путь = null)
    {
        var файл = путь ?? ПутьФайлаНастроек;

        if (!File.Exists(файл))
        {
            return new SmtpSettings();
        }

        var json = File.ReadAllText(файл);
        var раздел = Разобрать(json);

        return new SmtpSettings
        {
            Server = раздел.Server ?? string.Empty,
            Port = раздел.Port ?? SmtpSettings.DefaultPort,
            Login = раздел.Login,
            ProtectedPassword = раздел.ProtectedPassword,
            SslMode = раздел.SslMode ?? SslMode.StartTls,
            TrustSelfSignedCertificate = раздел.TrustSelfSignedCertificate ?? false,
            TrustedCertificateFingerprint = раздел.TrustedCertificateFingerprint,
            TimeoutSeconds = раздел.TimeoutSeconds ?? 30,
        };
    }

    /// <summary>
    /// Сохраняет настройки SMTP-учётной записи в файл настроек. Пароль
    /// записывается только в защищённом виде.
    /// </summary>
    /// <param name="settings">Настройки учётной записи.</param>
    /// <param name="путь">Путь к файлу настроек; при значении <see langword="null"/> используется путь по умолчанию.</param>
    public void Save(SmtpSettings settings, string? путь = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var файл = путь ?? ПутьФайлаНастроек;

        var папка = Path.GetDirectoryName(файл);

        if (!string.IsNullOrEmpty(папка))
        {
            Directory.CreateDirectory(папка);
        }

        var существующий = Разобрать(File.Exists(файл) ? File.ReadAllText(файл) : "{}");

        существующий.Server = settings.Server;
        существующий.Port = settings.Port;
        существующий.Login = settings.Login;
        существующий.ProtectedPassword = settings.ProtectedPassword;
        существующий.SslMode = settings.SslMode;
        существующий.TrustSelfSignedCertificate = settings.TrustSelfSignedCertificate;
        существующий.TrustedCertificateFingerprint = settings.TrustedCertificateFingerprint;
        существующий.TimeoutSeconds = settings.TimeoutSeconds;

        File.WriteAllText(файл, JsonSerializer.Serialize(существующий, ПараметрыJson));
    }

    /// <summary>
    /// Читает настройки прокси-сервера из файла настроек.
    /// </summary>
    /// <param name="путь">Путь к файлу настроек; при значении <see langword="null"/> используется путь по умолчанию.</param>
    /// <returns>Настройки прокси; при отсутствии файла — выключенный прокси.</returns>
    public ProxySettings LoadProxy(string? путь = null)
    {
        var файл = путь ?? ПутьФайлаНастроек;

        if (!File.Exists(файл))
        {
            return new ProxySettings();
        }

        var раздел = Разобрать(File.ReadAllText(файл)).Proxy;

        if (раздел is null)
        {
            return new ProxySettings();
        }

        return new ProxySettings
        {
            Enabled = раздел.Enabled ?? false,
            Server = раздел.Server ?? string.Empty,
            Port = раздел.Port ?? 8080,
            Login = раздел.Login,
            ProtectedPassword = раздел.ProtectedPassword,
        };
    }

    /// <summary>
    /// Сохраняет настройки прокси-сервера в файл настроек. Пароль записывается
    /// только в защищённом виде.
    /// </summary>
    /// <param name="settings">Настройки прокси.</param>
    /// <param name="путь">Путь к файлу настроек; при значении <see langword="null"/> используется путь по умолчанию.</param>
    public void SaveProxy(ProxySettings settings, string? путь = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var файл = путь ?? ПутьФайлаНастроек;

        var папка = Path.GetDirectoryName(файл);

        if (!string.IsNullOrEmpty(папка))
        {
            Directory.CreateDirectory(папка);
        }

        var существующий = Разобрать(File.Exists(файл) ? File.ReadAllText(файл) : "{}");

        существующий.Proxy = new РазделПрокси
        {
            Enabled = settings.Enabled,
            Server = settings.Server,
            Port = settings.Port,
            Login = settings.Login,
            ProtectedPassword = settings.ProtectedPassword,
        };

        File.WriteAllText(файл, JsonSerializer.Serialize(существующий, ПараметрыJson));
    }

    /// <summary>
    /// Защищает пароль учётной записи средствами Windows (DPAPI).
    /// </summary>
    /// <param name="пароль">Открытый текст пароля.</param>
    /// <returns>Защищённый блок в текстовом представлении Base64.</returns>
    public string ProtectPassword(string пароль) => ЗащищённыйПароль.Защитить(_секреты, пароль);

    /// <summary>
    /// Расшифровывает защищённый пароль учётной записи.
    /// </summary>
    /// <param name="защищённыйПароль">Защищённый блок в текстовом представлении Base64.</param>
    /// <returns>Открытый текст пароля; при пустом значении — <see langword="null"/>.</returns>
    public string? UnprotectPassword(string? защищённыйПароль)
        => ЗащищённыйПароль.Расшифровать(_секреты, защищённыйПароль);

    /// <summary>
    /// Расшифровывает защищённый пароль учётной записи для передачи серверу.
    /// </summary>
    /// <param name="settings">Настройки учётной записи.</param>
    /// <returns>Открытый текст пароля; при отсутствии защищённого блока — <see langword="null"/>.</returns>
    public string? GetPassword(SmtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return UnprotectPassword(settings.ProtectedPassword);
    }

    /// <summary>
    /// Формирует текст детализации ошибки соединения.
    /// </summary>
    /// <param name="ошибка">Исключение.</param>
    /// <returns>Детализация на русском языке.</returns>
    internal static string ОписатьОшибку(Exception ошибка)
    {
        ArgumentNullException.ThrowIfNull(ошибка);

        return ошибка switch
        {
            TimeoutException => "превышено время ожидания ответа сервера",
            System.Net.Sockets.SocketException socket => socket.SocketErrorCode switch
            {
                System.Net.Sockets.SocketError.TimedOut => "превышено время ожидания ответа сервера",
                System.Net.Sockets.SocketError.ConnectionRefused => "сервер отклонил соединение",
                System.Net.Sockets.SocketError.HostNotFound => "сервер не найден",
                System.Net.Sockets.SocketError.NetworkUnreachable => "сеть недоступна",
                _ => socket.Message,
            },
            System.IO.IOException => "соединение прервано: " + ошибка.Message,
            _ => ошибка.Message,
        };
    }

    /// <summary>
    /// Разбирает содержимое файла настроек.
    /// </summary>
    /// <param name="json">Содержимое файла настроек.</param>
    /// <returns>Разобранные разделы настроек.</returns>
    private static РазделНастроек Разобрать(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new РазделНастроек();
        }

        try
        {
            return JsonSerializer.Deserialize<РазделНастроек>(json, ПараметрыJson) ?? new РазделНастроек();
        }
        catch (JsonException)
        {
            // Повреждённый файл настроек не должен приводить к отказу программы:
            // настройки считаются пустыми.
            return new РазделНастроек();
        }
    }

    /// <summary>Раздел файла настроек с данными SMTP-учётной записи.</summary>
    private sealed class РазделНастроек
    {
        /// <summary>Имя или адрес SMTP-сервера.</summary>
        public string? Server { get; set; }

        /// <summary>Порт SMTP-сервера.</summary>
        public int? Port { get; set; }

        /// <summary>Логин учётной записи.</summary>
        public string? Login { get; set; }

        /// <summary>Пароль в защищённом виде.</summary>
        public string? ProtectedPassword { get; set; }

        /// <summary>Режим защищённого соединения.</summary>
        public SslMode? SslMode { get; set; }

        /// <summary>Признак доверия самоподписанному сертификату.</summary>
        public bool? TrustSelfSignedCertificate { get; set; }

        /// <summary>Отпечаток доверенного сертификата.</summary>
        public string? TrustedCertificateFingerprint { get; set; }

        /// <summary>Таймаут соединения, секунд.</summary>
        public int? TimeoutSeconds { get; set; }

        /// <summary>Раздел настроек прокси-сервера.</summary>
        public РазделПрокси? Proxy { get; set; }
    }

    /// <summary>Раздел файла настроек с параметрами прокси-сервера.</summary>
    private sealed class РазделПрокси
    {
        /// <summary>Признак использования прокси.</summary>
        public bool? Enabled { get; set; }

        /// <summary>Адрес прокси-сервера.</summary>
        public string? Server { get; set; }

        /// <summary>Порт прокси-сервера.</summary>
        public int? Port { get; set; }

        /// <summary>Логин для авторизации на прокси.</summary>
        public string? Login { get; set; }

        /// <summary>Пароль в защищённом виде.</summary>
        public string? ProtectedPassword { get; set; }
    }
}
