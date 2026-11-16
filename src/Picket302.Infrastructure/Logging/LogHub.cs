using System.Globalization;
using System.Text;
using Serilog;
using Serilog.Core;

namespace Picket302.Infrastructure.Logging;

/// <summary>
/// Журнал приложения с независимыми каналами (ТЗ §8.11, функция 111:
/// <c>public ILogger GetChannel(LogChannel channel) // App | Print | Audit | Email</c>;
/// §10.3 «Логирование и каналы»).
/// </summary>
/// <remarks>
/// <para>
/// Каналы журнала раздельны: ошибки приложения, печатные сессии, аудит
/// изменений справочников и отправка писем. Каждый канал пишется в свой файл
/// с ротацией по дню и по размеру; число хранимых файлов ограничено.
/// </para>
/// <para>
/// Файлы журналов располагаются в каталоге <c>&lt;Данные&gt;\logs</c>. Если
/// файловая система журналов недоступна, канал переходит в режим памяти:
/// записи не приводят к падению приложения, а выводятся в консоль отладки.
/// </para>
/// </remarks>
public sealed class LogHub : IDisposable
{
    /// <summary>Имя каталога журналов в каталоге данных программы.</summary>
    public const string LogFolderName = "logs";

    /// <summary>Предельный размер файла журнала, байт (ротация по размеру).</summary>
    public const long DefaultFileSizeLimitBytes = 8L * 1024 * 1024;

    /// <summary>Число хранимых файлов журнала по каждому каналу.</summary>
    public const int DefaultRetainedFileCount = 14;

    /// <summary>Шаблон строки журнала.</summary>
    private const string ШаблонСтроки =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    private readonly Dictionary<LogChannel, ILogger> _каналы = [];
    private readonly Dictionary<LogChannel, InMemoryLogSink> _память = [];
    private readonly Dictionary<LogChannel, string> _файлы = [];
    private bool _деградация;

    /// <summary>
    /// Создаёт журнал в каталоге данных программы.
    /// </summary>
    public LogHub()
        : this(ПутьЖурналовПоУмолчанию())
    {
    }

    /// <summary>
    /// Создаёт журнал в указанном каталоге.
    /// </summary>
    /// <param name="папкаЖурналов">Каталог файлов журналов.</param>
    public LogHub(string папкаЖурналов)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(папкаЖурналов);

        ПапкаЖурналов = Path.GetFullPath(папкаЖурналов);

        foreach (var канал in Enum.GetValues<LogChannel>())
        {
            СоздатьКанал(канал);
        }
    }

    /// <summary>Каталог файлов журналов.</summary>
    public string ПапкаЖурналов { get; }

    /// <summary>
    /// Признак того, что журнал работает в режиме памяти: файловая система
    /// журналов недоступна (ТЗ §8.11, функция 111).
    /// </summary>
    public bool IsDegraded => _деградация;

    /// <summary>
    /// Каталог данных программы по умолчанию.
    /// </summary>
    /// <returns>Путь к каталогу данных.</returns>
    public static string ПутьДанныхПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Picket302");

    /// <summary>
    /// Каталог файлов журналов по умолчанию: <c>&lt;Данные&gt;\logs</c>.
    /// </summary>
    /// <returns>Путь к каталогу журналов.</returns>
    public static string ПутьЖурналовПоУмолчанию()
        => Path.Combine(ПутьДанныхПоУмолчанию(), LogFolderName);

    /// <summary>
    /// Возвращает журнал указанного канала (ТЗ §8.11, функция 111:
    /// <c>public ILogger GetChannel(LogChannel channel)</c>).
    /// </summary>
    /// <param name="канал">Канал журнала.</param>
    /// <returns>Журнал канала.</returns>
    public ILogger GetChannel(LogChannel канал)
        => _каналы.TryGetValue(канал, out var журнал)
            ? журнал
            : _каналы[LogChannel.App];

    /// <summary>
    /// Путь к файлу журнала указанного канала.
    /// </summary>
    /// <param name="канал">Канал журнала.</param>
    /// <returns>Путь к файлу журнала; пустая строка — канал работает в памяти.</returns>
    public string GetLogFilePath(LogChannel канал)
        => _файлы.TryGetValue(канал, out var путь) ? путь : string.Empty;

    /// <summary>
    /// Записывает исключение в канал ошибок приложения.
    /// </summary>
    /// <param name="исключение">Записываемое исключение.</param>
    /// <param name="сообщение">Пояснение для оператора.</param>
    public void LogError(Exception исключение, string? сообщение = null)
    {
        ArgumentNullException.ThrowIfNull(исключение);

        GetChannel(LogChannel.App).Error(исключение, "{Message}", сообщение ?? исключение.Message);
    }

    /// <summary>
    /// Записывает сообщение об ошибке без исключения.
    /// </summary>
    /// <param name="сообщение">Текст ошибки.</param>
    /// <remarks>
    /// Уровень Error остаётся уровнем Error: исключение не придумывается ради
    /// того, чтобы попасть в тот же файл. Так в журнале нет фиктивных
    /// <c>InvalidOperationException</c>, а реальные сбои никуда не понижаются.
    /// </remarks>
    public void LogError(string сообщение)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(сообщение);

        GetChannel(LogChannel.App).Error("{Message}", сообщение);
    }

    /// <summary>
    /// Записывает предупреждение: событие требует внимания, но работа
    /// продолжается.
    /// </summary>
    /// <param name="сообщение">Текст предупреждения.</param>
    public void LogWarning(string сообщение)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(сообщение);

        GetChannel(LogChannel.App).Warning("{Message}", сообщение);
    }

    /// <summary>
    /// Записывает информационное событие: штатное действие без отклонений.
    /// </summary>
    /// <param name="сообщение">Текст события.</param>
    public void LogInfo(string сообщение)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(сообщение);

        GetChannel(LogChannel.App).Information("{Message}", сообщение);
    }

    /// <summary>
    /// Записывает печатную сессию в канал печати в формате ТЗ §6.5.
    /// </summary>
    /// <param name="сессия">Сведения о печатной сессии.</param>
    public void LogPrintSession(PrintSessionLogRecord сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        GetChannel(LogChannel.Print).Information("{PrintSession}", сессия.Форматировать());
    }

    /// <summary>
    /// Записывает событие аудита: изменение справочника, вход в программу.
    /// </summary>
    /// <param name="действие">Описание действия.</param>
    /// <param name="объект">Объект действия.</param>
    /// <param name="пользователь">Пользователь программы.</param>
    public void LogAudit(string действие, string объект, string? пользователь = null)
    {
        GetChannel(LogChannel.Audit).Information(
            "{Action} {Object} {User}",
            действие,
            объект,
            пользователь ?? Environment.UserName);
    }

    /// <summary>
    /// Записывает отправку письма с кодом ответа SMTP в канал почты.
    /// </summary>
    /// <param name="адресат">Адрес получателя.</param>
    /// <param name="статус">Статус отправки.</param>
    /// <param name="кодОтвета">Код ответа SMTP-сервера.</param>
    public void LogEmail(string адресат, string статус, int? кодОтвета = null)
    {
        GetChannel(LogChannel.Email).Information(
            "{Recipient} {Status} {Code}",
            адресат,
            статус,
            кодОтвета);
    }

    /// <summary>
    /// Читает последние записи журнала указанного канала.
    /// </summary>
    /// <param name="канал">Канал журнала.</param>
    /// <param name="предел">Предельное число записей.</param>
    /// <returns>Перечень записей; начиная с самой старой из выбранных.</returns>
    public IReadOnlyList<string> ReadRecent(LogChannel канал, int предел = 500)
    {
        if (предел <= 0)
        {
            return Array.Empty<string>();
        }

        var изПамяти = _память.TryGetValue(канал, out var приёмник)
            ? приёмник.Read(предел)
            : Array.Empty<string>();

        var путь = GetLogFilePath(канал);
        var фактический = НайтиТекущийФайл(путь);

        if (фактический is null)
        {
            return изПамяти;
        }

        try
        {
            // Файл удерживается открытым приёмником Serilog, поэтому чтение
            // идёт с разделением доступа (FileShare.ReadWrite).
            using var поток = new FileStream(фактический, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var читатель = new StreamReader(поток, Encoding.UTF8);

            var строки = new List<string>();

            while (читатель.ReadLine() is { } строка)
            {
                строки.Add(строка);
            }

            return строки.Count == 0
                ? изПамяти
                : строки.TakeLast(предел).ToArray();
        }
        catch (IOException)
        {
            return изПамяти;
        }
        catch (UnauthorizedAccessException)
        {
            return изПамяти;
        }
    }

    /// <summary>
    /// Формирует текст записей журнала для передачи службе поддержки
    /// (ТЗ §8.11, функция 111: кнопка «Скопировать для службы поддержки»).
    /// </summary>
    /// <param name="канал">Канал журнала.</param>
    /// <param name="предел">Предельное число записей.</param>
    /// <returns>Текст для вставки в письмо службе поддержки.</returns>
    public string BuildSupportText(LogChannel канал, int предел = 500)
    {
        var записи = ReadRecent(канал, предел);

        var строки = new List<string>
        {
            "Журнал: " + ИмяКанала(канал),
            "Программа: Picket302",
            "Дата формирования: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            $"Канал в режиме памяти: {(_деградация ? "да" : "нет")}",
            string.Empty,
        };

        строки.AddRange(записи);

        return string.Join(Environment.NewLine, строки);
    }

    /// <summary>
    /// Русское имя канала журнала.
    /// </summary>
    /// <param name="канал">Канал журнала.</param>
    /// <returns>Название канала.</returns>
    public static string ИмяКанала(LogChannel канал) => канал switch
    {
        LogChannel.Print => "Печать",
        LogChannel.Audit => "Аудит",
        LogChannel.Email => "Почта",
        _ => "Ошибки приложения",
    };

    /// <summary>
    /// Имя файла журнала канала (ТЗ §8.11, функция 111: logs/app-.log,
    /// logs/print-.log, logs/audit-.log).
    /// </summary>
    /// <param name="канал">Канал журнала.</param>
    /// <returns>Имя файла журнала.</returns>
    public static string ИмяФайла(LogChannel канал) => канал switch
    {
        LogChannel.Print => "print-.log",
        LogChannel.Audit => "audit-.log",
        LogChannel.Email => "email-.log",
        _ => "app-.log",
    };

    /// <summary>
    /// Находит фактический файл журнала: приёмник Serilog с ротацией по дню
    /// пишет в файл <c>app-ГГГГММДД.log</c>, а шаблон имени — <c>app-.log</c>.
    /// </summary>
    /// <param name="шаблонПути">Шаблон пути журнала канала.</param>
    /// <returns>Путь к самому свежему файлу журнала канала либо null.</returns>
    private static string? НайтиТекущийФайл(string шаблонПути)
    {
        if (шаблонПути.Length == 0)
        {
            return null;
        }

        var каталог = Path.GetDirectoryName(шаблонПути);

        if (каталог is null || !Directory.Exists(каталог))
        {
            return null;
        }

        // Завершающий дефис шаблона (app-) срезается (F-C4): маска строится
        // как «app-*», иначе двойной дефис «app--*» не совпадает с фактическим
        // именем файла Serilog «app-20261102.log».
        var имяБезРасширения = Path.GetFileNameWithoutExtension(шаблонПути).TrimEnd('-');
        var расширение = Path.GetExtension(шаблонПути);

        try
        {
            return Directory.GetFiles(каталог, имяБезРасширения + "-*" + расширение)
                .OrderByDescending(имя => имя, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ошибка) when (ошибка is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var журнал in _каналы.Values)
        {
            (журнал as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Создаёт журнал канала: файловый либо, при недоступности файловой
    /// системы, в памяти.
    /// </summary>
    private void СоздатьКанал(LogChannel канал)
    {
        var приёмник = new InMemoryLogSink();
        _память[канал] = приёмник;

        var имяФайла = ИмяФайла(канал);
        var путь = Path.Combine(ПапкаЖурналов, имяФайла);

        try
        {
            Directory.CreateDirectory(ПапкаЖурналов);

            var конфигурация = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    путь,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: DefaultFileSizeLimitBytes,
                    retainedFileCountLimit: DefaultRetainedFileCount,
                    outputTemplate: ШаблонСтроки,
                    formatProvider: CultureInfo.InvariantCulture,
                    shared: false)
                .WriteTo.Sink(приёмник);

            _каналы[канал] = конфигурация.CreateLogger();
            _файлы[канал] = путь;
        }
        catch (Exception ошибка) when (ошибка is IOException
            or UnauthorizedAccessException
            or DirectoryNotFoundException
            or ArgumentException
            or NotSupportedException)
        {
            _деградация = true;

            var конфигурация = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Sink(приёмник);

            _каналы[канал] = конфигурация.CreateLogger();
        }
    }
}
