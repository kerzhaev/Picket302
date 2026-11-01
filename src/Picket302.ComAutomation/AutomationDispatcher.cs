using System.Drawing;
using System.Xml;
using Picket302.Domain.Common;
using System.Drawing.Printing;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Exchange;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Repositories;
using Picket302.Infrastructure.Templates;
using Picket302.Printing;
using Picket302.Printing.Audit;
using Picket302.Printing.Devices;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Reports;
using Picket302.Printing.Rendering;
using Picket302.Printing.Validation;

namespace Picket302.ComAutomation;

/// <summary>
/// Результат исполнения команды автоматизации (ТЗ §9.2, таблица 9.2).
/// </summary>
/// <param name="WarningCode">Код результата по таблице 9.2 ТЗ.</param>
/// <param name="Succeeded">Признак успешного выполнения.</param>
/// <param name="Message">Текст результата на русском языке.</param>
/// <param name="Values">Дополнительные значения команды (списки шаблонов).</param>
public sealed record AutomationExecutionResult(
    int WarningCode,
    bool Succeeded,
    string Message,
    IReadOnlyList<string>? Values = null)
{
    /// <summary>Значения команды; по умолчанию — пустой список.</summary>
    public IReadOnlyList<string> ResultValues => Values ?? Array.Empty<string>();

    /// <summary>
    /// Создаёт успешный результат.
    /// </summary>
    /// <param name="message">Текст результата.</param>
    /// <param name="values">Дополнительные значения.</param>
    /// <returns>Успешный результат.</returns>
    public static AutomationExecutionResult Success(string message, IReadOnlyList<string>? values = null) =>
        new(AutomationWarningCodes.Success, true, message, values);

    /// <summary>
    /// Создаёт результат с кодом ошибки.
    /// </summary>
    /// <param name="warningCode">Код результата по таблице 9.2 ТЗ.</param>
    /// <param name="message">Текст ошибки на русском языке.</param>
    /// <returns>Результат с ошибкой.</returns>
    public static AutomationExecutionResult Failure(int warningCode, string message) =>
        new(warningCode, false, message);
}

/// <summary>
/// Результат печати, возвращаемый слоем окружения внутреннему исполнителю.
/// </summary>
/// <param name="Succeeded">Признак успешной печати.</param>
/// <param name="WarningCode">Код результата по таблице 9.2 ТЗ.</param>
/// <param name="Message">Текст результата на русском языке.</param>
/// <param name="MailId">Идентификатор созданного отправления (при наличии).</param>
public sealed record AutomationPrintOutcome(
    bool Succeeded,
    int WarningCode,
    string Message,
    long MailId = 0)
{
    /// <summary>
    /// Создаёт успешный результат печати.
    /// </summary>
    /// <param name="message">Текст результата.</param>
    /// <param name="mailId">Идентификатор созданного отправления.</param>
    /// <returns>Успешный результат печати.</returns>
    public static AutomationPrintOutcome Ok(string message, long mailId = 0) =>
        new(true, AutomationWarningCodes.Success, message, mailId);

    /// <summary>
    /// Создаёт результат печати с кодом ошибки.
    /// </summary>
    /// <param name="warningCode">Код результата по таблице 9.2 ТЗ.</param>
    /// <param name="message">Текст ошибки.</param>
    /// <returns>Результат печати с ошибкой.</returns>
    public static AutomationPrintOutcome Failure(int warningCode, string message) =>
        new(false, warningCode, message);
}

/// <summary>
/// Результат создания писем или импорта контрагентов (ТЗ §8.9, функция 116).
/// </summary>
/// <param name="Created">Число созданных записей.</param>
/// <param name="Succeeded">Признак успешного выполнения.</param>
/// <param name="WarningCode">Код результата по таблице 9.2 ТЗ.</param>
/// <param name="Message">Текст результата на русском языке.</param>
/// <param name="MailIds">Идентификаторы созданных отправлений.</param>
public sealed record AutomationLettersResult(
    int Created,
    bool Succeeded,
    int WarningCode,
    string Message,
    IReadOnlyList<long>? MailIds = null)
{
    /// <summary>Идентификаторы созданных отправлений.</summary>
    public IReadOnlyList<long> CreatedMailIds => MailIds ?? Array.Empty<long>();

    /// <summary>
    /// Создаёт успешный результат.
    /// </summary>
    /// <param name="created">Число созданных записей.</param>
    /// <param name="message">Текст результата.</param>
    /// <param name="mailIds">Идентификаторы созданных отправлений.</param>
    /// <returns>Успешный результат.</returns>
    public static AutomationLettersResult Ok(
        int created,
        string message,
        IReadOnlyList<long>? mailIds = null) =>
        new(created, true, AutomationWarningCodes.Success, message, mailIds);

    /// <summary>
    /// Создаёт результат с кодом ошибки.
    /// </summary>
    /// <param name="warningCode">Код результата по таблице 9.2 ТЗ.</param>
    /// <param name="message">Текст ошибки.</param>
    /// <returns>Результат с ошибкой.</returns>
    public static AutomationLettersResult Failure(int warningCode, string message) =>
        new(0, false, warningCode, message);
}

/// <summary>
/// Среда исполнения автоматизации: доступ к базе данных, печати и шаблонам
/// (внутренний слой, ТЗ §9.1, функция 113).
/// </summary>
public interface IAutomationEnvironment
{
    /// <summary>Признак загруженной базы данных.</summary>
    bool IsDatabaseLoaded { get; }

    /// <summary>
    /// Показывать ли окна печати модально (флаг <c>ShowFormsAsModal</c>,
    /// ТЗ §9.1, методы 16 и 17). По умолчанию — <see langword="false"/>:
    /// слепой запуск, вызывающий поток не блокируется.
    /// </summary>
    bool ShowFormsAsModal { get; set; }

    /// <summary>
    /// Выполняет идемпотентную безопасную загрузку базы данных.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/> — база загружена.</returns>
    Task<bool> SafeLoadDatabaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Создаёт исходящие письма по данным контракта <c>EnvelopesData</c> и при
    /// необходимости печатает их (ТЗ §8.9, функция 116).
    /// </summary>
    /// <param name="envelopes">Конверты к обработке.</param>
    /// <param name="print">Печатать ли созданные письма.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат создания писем.</returns>
    Task<AutomationLettersResult> CreateLettersAsync(
        IReadOnlyList<PrintableEnvelope> envelopes,
        bool print,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Импортирует контрагентов: дедупликация и тонкое слияние с базой
    /// (ТЗ §8.9, функция 116; ТЗ §8.12, функции 74–75).
    /// </summary>
    /// <param name="drafts">Черновики контрагентов.</param>
    /// <param name="updateExisting">Обновлять ли существующих контрагентов.</param>
    /// <param name="writeNote">Записывать ли примечание при импорте.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат импорта.</returns>
    Task<AutomationLettersResult> ImportCounterpartiesAsync(
        IReadOnlyList<CounterpartyDraft> drafts,
        bool updateExisting,
        bool writeNote,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает имена пользовательских шаблонов реестров.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Имена шаблонов.</returns>
    Task<IReadOnlyList<string>> GetUserRegisterTemplateNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает имена стандартных шаблонов реестров из встроенного каталога.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Имена шаблонов.</returns>
    Task<IReadOnlyList<string>> GetStandardRegisterTemplateNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Печатает конверты или уведомления, создавая учётные записи отправлений.
    /// </summary>
    /// <param name="envelopes">Конверты к печати.</param>
    /// <param name="notification">Признак печати уведомлений.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат печати.</returns>
    Task<AutomationPrintOutcome> PrintEnvelopesAsync(
        IReadOnlyList<PrintableEnvelope> envelopes,
        bool notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Печатает один конверт, создавая учётную запись отправления.
    /// </summary>
    /// <param name="envelope">Конверт к печати.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Result печати.</returns>
    Task<AutomationPrintOutcome> PrintEnvelopeAsync(
        PrintableEnvelope envelope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Печатает реестры, создавая учётные записи отправлений по пакетам.
    /// </summary>
    /// <param name="registers">Реестры к печати.</param>
    /// <param name="templateName">Имя шаблона реестра; пусто — шаблон по умолчанию.</param>
    /// <param name="resultFullPath">Путь сохранения результата; пусто — печать на принтер.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат печати.</returns>
    Task<AutomationPrintOutcome> PrintRegistersAsync(
        IReadOnlyList<RegisterData> registers,
        string templateName,
        string resultFullPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Печатает описи, создавая учётные записи описей и вложений.
    /// </summary>
    /// <param name="inventories">Описи к печати.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат печати.</returns>
    Task<AutomationPrintOutcome> PrintInventoriesAsync(
        IReadOnlyList<InventoryData> inventories,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Внутренний исполнитель команд автоматизации (ТЗ §9.1, функция 113).
/// </summary>
/// <remarks>
/// <para>
/// Публичный слой (COM-методы) лишь готовит команду вызовом <c>Prepare*</c> и
/// передаёт её исполнителю; вся работа (разбор XML, работа с базой, печать)
/// выполняется здесь. Коды результата соответствуют таблице 9.2 ТЗ:
/// 0 — успех, 1 — база не загружена, 2 — ошибка XML, 3 — ошибка документа,
/// 4 — не найден шаблон.
/// </para>
/// </remarks>
public sealed class AutomationDispatcher
{
    private readonly IAutomationEnvironment _environment;
    private readonly Func<bool> _parseZipIndexFromAddress;

    /// <summary>
    /// Создаёт исполнителя команд.
    /// </summary>
    /// <param name="environment">Среда исполнения (база данных, печать, шаблоны).</param>
    /// <param name="parseZipIndexFromAddress">
    /// Функция чтения флага «выкусывать индекс из строки адреса»; по умолчанию —
    /// индекс не выкусывается.
    /// </param>
    public AutomationDispatcher(IAutomationEnvironment environment, Func<bool>? parseZipIndexFromAddress = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _parseZipIndexFromAddress = parseZipIndexFromAddress ?? (() => false);
    }

    /// <summary>Признак загруженной базы данных.</summary>
    public bool IsDatabaseLoaded => _environment.IsDatabaseLoaded;

    /// <summary>
    /// Исполняет подготовленную команду.
    /// </summary>
    /// <param name="command">Подготовленная команда.</param>
    /// <returns>Результат исполнения с кодом по таблице 9.2 ТЗ.</returns>
    public AutomationExecutionResult Execute(AutomationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return command switch
            {
                PrintFromFileCommand file => ExecutePrintFromFile(file),
                PrintFromXmlCommand xml => ExecutePrintFromXml(xml),
                PrintEnvelopeCommand envelope => ExecutePrintEnvelope(envelope),
                PrintRegisterWithoutFormCommand register => ExecutePrintRegisterWithoutForm(register),
                GetRegisterTemplateNamesCommand => ExecuteGetRegisterTemplateNames(),
                GetStdRegisterTemplateNamesCommand => ExecuteGetStdRegisterTemplateNames(),
                SafeLoadDatabaseCommand => ExecuteSafeLoadDatabase(),
                _ => AutomationExecutionResult.Failure(
                    AutomationWarningCodes.DocumentError,
                    "Неизвестная команда автоматизации."),
            };
        }
        catch (Exception error)
        {
            return AutomationExecutionResult.Failure(
                AutomationWarningCodes.DocumentError,
                "Ошибка выполнения команды: " + error.Message);
        }
    }

    private AutomationExecutionResult ExecutePrintFromFile(PrintFromFileCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Path))
        {
            return AutomationExecutionResult.Failure(
                AutomationWarningCodes.DocumentError,
                "Не указан путь к файлу.");
        }

        string xml;

        try
        {
            xml = File.ReadAllText(command.Path);
        }
        catch (Exception error)
        {
            return AutomationExecutionResult.Failure(
                AutomationWarningCodes.DocumentError,
                $"Не удалось прочитать файл «{command.Path}»: {error.Message}");
        }

        return ExecutePrintFromXml(new PrintFromXmlCommand(command.DocumentKind, xml));
    }

    private AutomationExecutionResult ExecutePrintFromXml(PrintFromXmlCommand command)
    {
        var extractZip = _parseZipIndexFromAddress();

        switch (command.DocumentKind)
        {
            case AutomationDocumentKind.Envelopes:
            case AutomationDocumentKind.Notifications:
            {
                Result<IReadOnlyList<PrintableEnvelope>> чтение;

                try
                {
                    чтение = EnvelopesData.ReadListFromXmlAndFixForPrint(command.Xml, extractZip);
                }
                catch (Exception error)
                {
                    return ОшибкаРазбораXml(error);
                }

                if (чтение.IsFailure)
                {
                    return AutomationExecutionResult.Failure(
                        AutomationWarningCodes.InvalidXml,
                        чтение.Error ?? "Ошибка чтения XML-данных.");
                }

                if (!_environment.IsDatabaseLoaded)
                {
                    return AutomationExecutionResult.Failure(
                        AutomationWarningCodes.DatabaseNotLoaded,
                        "База данных не загружена: вызовите SafeLoadDatabase.");
                }

                var outcome = _environment
                    .PrintEnvelopesAsync(
                        чтение.Value,
                        command.DocumentKind == AutomationDocumentKind.Notifications)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                return FromOutcome(outcome);
            }

            case AutomationDocumentKind.Inventories:
            {
                Result<IReadOnlyList<InventoryData>> чтение;

                try
                {
                    чтение = InventoriesData.ReadListFromXml(command.Xml);
                }
                catch (Exception error)
                {
                    return ОшибкаРазбораXml(error);
                }

                if (чтение.IsFailure)
                {
                    return AutomationExecutionResult.Failure(
                        AutomationWarningCodes.InvalidXml,
                        чтение.Error ?? "Ошибка чтения XML-данных.");
                }

                if (!_environment.IsDatabaseLoaded)
                {
                    return AutomationExecutionResult.Failure(
                        AutomationWarningCodes.DatabaseNotLoaded,
                        "База данных не загружена: вызовите SafeLoadDatabase.");
                }

                var outcome = _environment
                    .PrintInventoriesAsync(чтение.Value)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                return FromOutcome(outcome);
            }

            case AutomationDocumentKind.Registers:
            {
                Result<IReadOnlyList<RegisterData>> чтение;

                try
                {
                    using var reader = ExchangeXmlReader.CreateReader(command.Xml);

                    чтение = RegistersData.ReadListFromXmlAndFixForPrint(reader, extractZip);
                }
                catch (Exception error)
                {
                    return ОшибкаРазбораXml(error);
                }

                if (чтение.IsFailure)
                {
                    return AutomationExecutionResult.Failure(
                        AutomationWarningCodes.InvalidXml,
                        чтение.Error ?? "Ошибка чтения XML-данных.");
                }

                if (!_environment.IsDatabaseLoaded)
                {
                    return AutomationExecutionResult.Failure(
                        AutomationWarningCodes.DatabaseNotLoaded,
                        "База данных не загружена: вызовите SafeLoadDatabase.");
                }

                var outcome = _environment
                    .PrintRegistersAsync(чтение.Value, string.Empty, string.Empty)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                return FromOutcome(outcome);
            }

            default:
                return AutomationExecutionResult.Failure(
                    AutomationWarningCodes.DocumentError,
                    "Неизвестный вид документа.");
        }
    }

    private static AutomationExecutionResult ОшибкаРазбораXml(Exception error) =>
        AutomationExecutionResult.Failure(
            AutomationWarningCodes.InvalidXml,
            "Ошибка чтения XML-данных: " + error.Message);
    private AutomationExecutionResult ExecutePrintEnvelope(PrintEnvelopeCommand command)
    {
        if (!_environment.IsDatabaseLoaded)
        {
            return AutomationExecutionResult.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена: вызовите SafeLoadDatabase.");
        }

        var envelope = new PrintableEnvelope
        {
            From = new EnvelopeParty
            {
                Name = command.From,
                Address = command.FromAddress,
                ZipCode = command.FromZipCode,
            },
            To = new EnvelopeParty
            {
                Name = command.To,
                Address = command.ToAddress,
                ZipCode = command.ToZipCode,
            },
        };

        var outcome = _environment
            .PrintEnvelopeAsync(envelope)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        return FromOutcome(outcome);
    }

    private AutomationExecutionResult ExecutePrintRegisterWithoutForm(PrintRegisterWithoutFormCommand command)
    {
        Result<IReadOnlyList<RegisterData>> чтение;

        try
        {
            using var reader = ExchangeXmlReader.CreateReader(command.Xml);

            чтение = RegistersData.ReadListFromXmlAndFixForPrint(reader, _parseZipIndexFromAddress());
        }
        catch (XmlException error)
        {
            return AutomationExecutionResult.Failure(
                AutomationWarningCodes.InvalidXml,
                "Ошибка чтения XML-данных: " + error.Message);
        }

        if (чтение.IsFailure)
        {
            return AutomationExecutionResult.Failure(
                AutomationWarningCodes.InvalidXml,
                чтение.Error ?? "Ошибка чтения XML-данных.");
        }

        if (!_environment.IsDatabaseLoaded)
        {
            return AutomationExecutionResult.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена: вызовите SafeLoadDatabase.");
        }

        var outcome = _environment
            .PrintRegistersAsync(чтение.Value, command.TemplateName, command.ResultFullPath)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        return FromOutcome(outcome);
    }

    private AutomationExecutionResult ExecuteGetRegisterTemplateNames()
    {
        if (!_environment.IsDatabaseLoaded)
        {
            return AutomationExecutionResult.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена: вызовите SafeLoadDatabase.");
        }

        var names = _environment
            .GetUserRegisterTemplateNamesAsync()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        return AutomationExecutionResult.Success(
            $"Получено шаблонов реестров: {names.Count}.",
            names);
    }

    private AutomationExecutionResult ExecuteGetStdRegisterTemplateNames()
    {
        var names = _environment
            .GetStandardRegisterTemplateNamesAsync()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        return AutomationExecutionResult.Success(
            $"Получено стандартных шаблонов реестров: {names.Count}.",
            names);
    }

    private AutomationExecutionResult ExecuteSafeLoadDatabase()
    {
        var loaded = _environment
            .SafeLoadDatabaseAsync()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        return loaded
            ? AutomationExecutionResult.Success("База данных загружена.")
            : AutomationExecutionResult.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "Не удалось загрузить базу данных.");
    }

    private static AutomationExecutionResult FromOutcome(AutomationPrintOutcome outcome) =>
        outcome.Succeeded
            ? AutomationExecutionResult.Success(outcome.Message)
            : AutomationExecutionResult.Failure(outcome.WarningCode, outcome.Message);
}

/// <summary>
/// Среда исполнения автоматизации поверх локальной базы данных SQLite и слоя
/// печати (ТЗ §9.1, функция 113).
/// </summary>
/// <remarks>
/// Среда является единственным местом, где автоматизация работает с данными:
/// создание отправлений (учётный факт печати), печать конвертов через
/// контроллер печатных сессий и печать реестров и описей через канал тихой
/// печати.
/// </remarks>
public sealed class ComAutomationEnvironment : IAutomationEnvironment, IDisposable
{
    /// <summary>Каталог профиля приложения.</summary>
    public const string ApplicationDirectoryName = "Picket302";

    /// <summary>Имя файла базы данных.</summary>
    public const string DatabaseFileName = "picket302.db";

    /// <summary>Формат конверта по умолчанию.</summary>
    public const string DefaultEnvelopeType = "C6";

    /// <summary>Шаблон реестра по умолчанию.</summary>
    public const string DefaultRegisterTemplateName = "Реестр почтовых отправлений";

    private readonly object _sync = new();
    private readonly string _databasePath;
    private readonly PrintEnvelopeSettings _settings;
    private SqliteUnitOfWork? _session;
    private PrintSessionController? _printing;

    /// <summary>
    /// Создаёт среду исполнения.
    /// </summary>
    /// <param name="databasePath">Путь к файлу базы данных; пусто — профиль пользователя.</param>
    /// <param name="settings">Настройки печати из реестра.</param>
    public ComAutomationEnvironment(string? databasePath = null, PrintEnvelopeSettings? settings = null)
    {
        _databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? DefaultDatabasePath()
            : Path.GetFullPath(databasePath);

        _settings = settings ?? new PrintEnvelopeSettings();
    }

    /// <summary>
    /// Возвращает путь к файлу базы данных по умолчанию —
    /// <c>%AppData%\Picket302\picket302.db</c>.
    /// </summary>
    /// <returns>Путь к файлу базы данных.</returns>
    public static string DefaultDatabasePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ApplicationDirectoryName,
        DatabaseFileName);

    /// <inheritdoc />
    public bool IsDatabaseLoaded
    {
        get
        {
            lock (_sync)
            {
                return _session is not null;
            }
        }
    }

    /// <inheritdoc />
    public bool ShowFormsAsModal { get; set; }

    /// <inheritdoc />
    public async Task<bool> SafeLoadDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (IsDatabaseLoaded)
        {
            return true;
        }

        if (!File.Exists(_databasePath))
        {
            return false;
        }

        var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
            Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Default,
        }.ToString();

        var session = new SqliteUnitOfWork(connection);

        try
        {
            await new MigrationRunner()
                .ApplyAsync(session.Connection, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            return false;
        }

        lock (_sync)
        {
            _session = session;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUserRegisterTemplateNamesAsync(
        CancellationToken cancellationToken = default)
    {
        var session = Session;

        if (session is null)
        {
            return Array.Empty<string>();
        }

        var templates = await new PrintTemplateRepository(session)
            .GetAllAsync(PrintTemplateKind.Report, cancellationToken)
            .ConfigureAwait(false);

        return templates
            .Select(template => template.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetStandardRegisterTemplateNamesAsync(
        CancellationToken cancellationToken = default)
    {
        var names = new BuiltinTemplatesCatalog()
            .BuildLibrary()
            .Select(entry => entry.PackageName)
            .Where(IsRegisterTemplateName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    /// <inheritdoc />
    public async Task<AutomationPrintOutcome> PrintEnvelopesAsync(
        IReadOnlyList<PrintableEnvelope> envelopes,
        bool notification,
        CancellationToken cancellationToken = default)
    {
        var session = Session;

        if (session is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена.");
        }

        var template = await НайтиШаблонПечатиAsync(
                session,
                notification ? PrintTemplateKind.Notification : PrintTemplateKind.Envelope,
                cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.TemplateNotFound,
                notification
                    ? "Файл шаблона уведомления не найден."
                    : "Файл шаблона конверта не найден.");
        }

        var создано = await CreateLettersAsync(envelopes, print: false, cancellationToken)
            .ConfigureAwait(false);

        if (!создано.Succeeded)
        {
            return AutomationPrintOutcome.Failure(создано.WarningCode, создано.Message);
        }

        if (создано.CreatedMailIds.Count == 0)
        {
            return AutomationPrintOutcome.Ok("Нет данных для печати.");
        }

        return await ВыполнитьПечатьAsync(создано.CreatedMailIds, template.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AutomationLettersResult> CreateLettersAsync(
        IReadOnlyList<PrintableEnvelope> envelopes,
        bool print,
        CancellationToken cancellationToken = default)
    {
        var session = Session;

        if (session is null)
        {
            return AutomationLettersResult.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена.");
        }

        var template = print
            ? await НайтиШаблонПечатиAsync(session, PrintTemplateKind.Envelope, cancellationToken)
                .ConfigureAwait(false)
            : null;

        if (print && template is null)
        {
            return AutomationLettersResult.Failure(
                AutomationWarningCodes.TemplateNotFound,
                "Файл шаблона конверта не найден.");
        }

        var ids = new List<long>();

        foreach (var envelope in envelopes)
        {
            var outcome = await СоздатьОтправлениеAsync(
                    session,
                    envelope,
                    template?.Format ?? DefaultEnvelopeType,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!outcome.Succeeded)
            {
                return AutomationLettersResult.Failure(outcome.WarningCode, outcome.Message);
            }

            ids.Add(outcome.MailId);
        }

        if (print && template is not null && ids.Count > 0)
        {
            var печать = await ВыполнитьПечатьAsync(ids, template.Id, cancellationToken).ConfigureAwait(false);

            if (!печать.Succeeded)
            {
                return AutomationLettersResult.Failure(печать.WarningCode, печать.Message);
            }
        }

        return AutomationLettersResult.Ok(ids.Count, $"Создано писем: {ids.Count}.", ids);
    }

    /// <inheritdoc />
    public Task<AutomationLettersResult> ImportCounterpartiesAsync(
        IReadOnlyList<CounterpartyDraft> drafts,
        bool updateExisting,
        bool writeNote,
        CancellationToken cancellationToken = default)
    {
        var session = Session;

        if (session is null)
        {
            return Task.FromResult(AutomationLettersResult.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена."));
        }

        if (drafts.Count == 0)
        {
            return Task.FromResult(AutomationLettersResult.Ok(0, "Нет данных для импорта."));
        }

        var контрагенты = new CounterpartyRepository(session);
        var политика = updateExisting ? MergePolicy.Overwrite : MergePolicy.SkipExisting;

        var дедупликатор = new ImportDeduplicator(контрагенты);
        var дедупликация = дедупликатор.FilterDuplicates(drafts, политика);

        var записи = new List<ImportRecord>();
        var номер = 1;

        foreach (var черновик in дедупликация.Accepted)
        {
            if (writeNote && string.IsNullOrWhiteSpace(черновик.Note))
            {
                черновик.Note = "Импорт из внешней системы";
            }

            записи.Add(new ImportRecord
            {
                RowNumber = номер++,
                Draft = черновик,
                Status = ImportRecordStatus.Verified,
            });
        }

        var пакет = new ImportBatch
        {
            SourceFormat = "COM-автоматизация (EconomicMembers)",
            SourceFile = "ApiLettersFacade",
            ImportedAt = DateTime.Now,
            Records = записи,
        };

        var слияние = new ImportMergeService(контрагенты);
        var отчёт = слияние.MergeImported(пакет, политика, cancellationToken);

        return Task.FromResult(AutomationLettersResult.Ok(
            отчёт.Created + отчёт.Updated,
            $"Импортировано контрагентов: создано {отчёт.Created}, обновлено {отчёт.Updated}, " +
            $"пропущено {отчёт.Skipped}."));
    }

    /// <inheritdoc />
    public async Task<AutomationPrintOutcome> PrintEnvelopeAsync(
        PrintableEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var session = Session;

        if (session is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена.");
        }

        var template = await НайтиШаблонПечатиAsync(session, PrintTemplateKind.Envelope, cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.TemplateNotFound,
                "Файл шаблона конверта не найден.");
        }

        var created = await СоздатьОтправлениеAsync(session, envelope, template.Format, cancellationToken)
            .ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return created;
        }

        return await ВыполнитьПечатьAsync(new[] { created.MailId }, template.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AutomationPrintOutcome> PrintRegistersAsync(
        IReadOnlyList<RegisterData> registers,
        string templateName,
        string resultFullPath,
        CancellationToken cancellationToken = default)
    {
        var session = Session;

        if (session is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена.");
        }

        var template = await НайтиШаблонРеестраAsync(session, templateName, cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.TemplateNotFound,
                $"Файл шаблона реестра «{НазваниеШаблона(templateName)}» не найден.");
        }

        foreach (var register in registers)
        {
            foreach (var package in register.Packages)
            {
                var envelope = new PrintableEnvelope
                {
                    From = register.From,
                    To = package.To,
                    Number = register.Number,
                    Note = package.Note,
                };

                var outcome = await СоздатьОтправлениеAsync(session, envelope, DefaultEnvelopeType, cancellationToken)
                    .ConfigureAwait(false);

                if (!outcome.Succeeded)
                {
                    return outcome;
                }
            }
        }

        var documents = registers
            .Select(register => AutomationReportBuilder.BuildRegister(register, template.Name))
            .ToArray();

        if (documents.Length == 0)
        {
            return AutomationPrintOutcome.Ok("Нет данных для печати реестра.");
        }

        if (!string.IsNullOrWhiteSpace(resultFullPath))
        {
            var сохранено = СохранитьДокументы(documents, resultFullPath);

            return сохранено
                ? AutomationPrintOutcome.Ok($"Реестр сохранён: {resultFullPath}")
                : AutomationPrintOutcome.Failure(
                    AutomationWarningCodes.DocumentError,
                    $"Не удалось сохранить реестр по пути «{resultFullPath}».");
        }

        return ВыполнитьПечатьДокументов(documents);
    }

    /// <inheritdoc />
    public async Task<AutomationPrintOutcome> PrintInventoriesAsync(
        IReadOnlyList<InventoryData> inventories,
        CancellationToken cancellationToken = default)
    {
        var session = Session;

        if (session is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DatabaseNotLoaded,
                "База данных не загружена.");
        }

        var template = await НайтиШаблонПечатиAsync(session, PrintTemplateKind.Report, cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.TemplateNotFound,
                "Файл шаблона описи не найден.");
        }

        var склады = new InventoryRepository(session);
        var позиции = new InventoryItemRepository(session);

        foreach (var inventory in inventories)
        {
            var запись = new Inventory
            {
                Name = inventory.FromName,
                Destination = inventory.ToName,
                CreatedAt = DateTime.Now,
            };

            var id = await склады.AddAsync(запись, cancellationToken).ConfigureAwait(false);

            foreach (var item in inventory.Items)
            {
                await позиции.AddAsync(
                    new Picket302.Domain.Entities.InventoryItem
                    {
                        InventoryId = id,
                        Name = item.ItemName,
                        Quantity = 1,
                        CreatedAt = DateTime.Now,
                    },
                    cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var documents = inventories
            .Select(inventory => AutomationReportBuilder.BuildInventory(inventory))
            .ToArray();

        if (documents.Length == 0)
        {
            return AutomationPrintOutcome.Ok("Нет данных для печати описи.");
        }

        return ВыполнитьПечатьДокументов(documents);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteUnitOfWork? session;

        lock (_sync)
        {
            session = _session;
            _session = null;
        }

        if (session is not null)
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static bool IsRegisterTemplateName(string name) =>
        name.Contains("реестр", StringComparison.OrdinalIgnoreCase);

    private static string НазваниеШаблона(string templateName) =>
        string.IsNullOrWhiteSpace(templateName) ? DefaultRegisterTemplateName : templateName;

    private SqliteUnitOfWork? Session
    {
        get
        {
            lock (_sync)
            {
                return _session;
            }
        }
    }

    private async Task<PrintTemplate?> НайтиШаблонПечатиAsync(
        SqliteUnitOfWork session,
        string kind,
        CancellationToken cancellationToken)
    {
        var templates = await new PrintTemplateRepository(session)
            .GetAllAsync(kind, cancellationToken)
            .ConfigureAwait(false);

        foreach (var template in templates)
        {
            if (string.IsNullOrWhiteSpace(template.FilePath))
            {
                continue;
            }

            if (!File.Exists(template.FilePath))
            {
                continue;
            }

            return template;
        }

        return null;
    }

    private async Task<PrintTemplate?> НайтиШаблонРеестраAsync(
        SqliteUnitOfWork session,
        string templateName,
        CancellationToken cancellationToken)
    {
        var имя = НазваниеШаблона(templateName);

        var templates = await new PrintTemplateRepository(session)
            .GetAllAsync(PrintTemplateKind.Report, cancellationToken)
            .ConfigureAwait(false);

        foreach (var template in templates)
        {
            if (!string.Equals(template.Name, имя, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(template.FilePath) || !File.Exists(template.FilePath))
            {
                return null;
            }

            return template;
        }

        // Шаблон может быть встроенным — тогда печать идёт по встроенной форме
        // и отдельный файл шаблона не требуется.
        var стандартные = new BuiltinTemplatesCatalog().BuildLibrary();

        foreach (var (packageName, _) in стандартные)
        {
            if (string.Equals(packageName, имя, StringComparison.OrdinalIgnoreCase))
            {
                return new PrintTemplate
                {
                    Name = packageName,
                    Kind = PrintTemplateKind.Report,
                    Format = "A4",
                    IsBuiltin = true,
                };
            }
        }

        return null;
    }

    private async Task<AutomationPrintOutcome> СоздатьОтправлениеAsync(
        SqliteUnitOfWork session,
        PrintableEnvelope envelope,
        string envelopeType,
        CancellationToken cancellationToken)
    {
        var контрагенты = new CounterpartyRepository(session);

        var отправитель = await НайтиОтправителяAsync(session, контрагенты, cancellationToken)
            .ConfigureAwait(false);

        if (отправитель is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DocumentError,
                "Отправитель не задан: заполните настройки печати «FromName», «FromAddr», «FromIndex».");
        }

        var получатель = await НайтиИлиСоздатьКонтрагентаAsync(контрагенты, envelope.To, false, cancellationToken)
            .ConfigureAwait(false);

        if (получатель is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DocumentError,
                "Не удалось определить получателя отправления.");
        }

        var адрес = Address.Create(
            envelope.To.ZipCode,
            envelope.To.AddressLines);

        if (адрес.IsFailure)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DocumentError,
                "Некорректный адрес получателя: " +
                (адрес.Error ?? "неверный почтовый индекс или адрес."));
        }

        var формат = string.IsNullOrWhiteSpace(envelopeType) ? DefaultEnvelopeType : envelopeType;

        var отправление = MailFactory.CreateOutcoming(
            получатель.Id,
            отправитель.Id,
            формат,
            адрес.Value,
            получатель.IsVip);

        if (!string.IsNullOrWhiteSpace(envelope.Number))
        {
            отправление.OutgoingNumber = envelope.Number;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Note))
        {
            отправление.Note = envelope.Note;
        }

        if (string.IsNullOrWhiteSpace(отправление.OutgoingNumber))
        {
            // Сквозной номер исходящего письма назначается по правилам функции 25
            // (ТЗ §8.1): нумерация хранится в таблице параметров.
            var нумерация = new RegistrationNumberGenerator(
                new Picket302.Infrastructure.Persistence.ParameterRegistrationNumberStore(session));

            отправление.OutgoingNumber = await нумерация
                .GenerateNextOutgoingNumberAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        var служба = new MailService(
            new MailRepository(session),
            контрагенты,
            new PrintHistoryRepository(session),
            session);

        var созданное = await служба.CreateAsync(отправление, cancellationToken).ConfigureAwait(false);

        if (созданное.IsFailure)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DocumentError,
                созданное.Error ?? "Не удалось создать отправление.");
        }

        return AutomationPrintOutcome.Ok("Отправление создано.", отправление.Id);
    }

    private async Task<Counterparty?> НайтиОтправителяAsync(
        SqliteUnitOfWork session,
        ICounterpartyRepository контрагенты,
        CancellationToken cancellationToken)
    {
        var настройки = _settings.Read();

        if (!string.IsNullOrWhiteSpace(настройки.FromName))
        {
            var отправитель = await НайтиИлиСоздатьКонтрагентаAsync(
                    контрагенты,
                    new EnvelopeParty
                    {
                        Name = настройки.FromName,
                        Address = настройки.FromAddr,
                        ZipCode = настройки.FromIndex,
                    },
                    true,
                    cancellationToken)
                .ConfigureAwait(false);

            if (отправитель is not null)
            {
                return отправитель;
            }
        }

        var отправители = await контрагенты.GetSendersAsync(cancellationToken).ConfigureAwait(false);

        return отправители.Count > 0 ? отправители[0] : null;
    }

    private async Task<Counterparty?> НайтиИлиСоздатьКонтрагентаAsync(
        ICounterpartyRepository контрагенты,
        EnvelopeParty party,
        bool isSender,
        CancellationToken cancellationToken)
    {
        var имя = (party.Name ?? string.Empty).Trim();

        if (имя.Length == 0)
        {
            return null;
        }

        var индекс = (party.ZipCode ?? string.Empty).Trim();

        var существующий = await контрагенты
            .FindByMergeKeyAsync(null, имя, индекс.Length > 0 ? индекс : null, cancellationToken)
            .ConfigureAwait(false);

        if (существующий is not null)
        {
            if (isSender && !существующий.IsSender)
            {
                существующий.IsSender = true;
                await контрагенты.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);
            }

            return существующий;
        }

        var контрагент = new Counterparty
        {
            Name = имя,
            IsSender = isSender,
            IsActive = true,
            Source = "COM-автоматизация",
        };

        if (!string.IsNullOrWhiteSpace(party.Address))
        {
            var адрес = Address.Create(
                индекс,
                party.AddressLines);

            if (адрес.IsSuccess)
            {
                контрагент.PostalAddress = адрес.Value;
            }
        }

        var id = await контрагенты.AddAsync(контрагент, cancellationToken).ConfigureAwait(false);
        контрагент.Id = id;

        return контрагент;
    }

    private async Task<AutomationPrintOutcome> ВыполнитьПечатьAsync(
        IReadOnlyList<long> mailIds,
        long templateId,
        CancellationToken cancellationToken)
    {
        var принтер = new PrinterManager().GetDefaultPrinter();

        if (принтер is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DocumentError,
                PrinterManager.NoDefaultPrinterMessage);
        }

        var request = new PrintRequest
        {
            MailIds = mailIds,
            TemplateId = templateId,
            PrinterName = принтер.Name,
            PrintMode = PrintModes.BatchDirect,
            Copies = 1,

            // Окно предпросмотра показывается только при флаге
            // ShowFormsAsModal: по умолчанию печать выполняется вслепую и
            // вызывающий поток не блокируется (ТЗ §9.2).
            ShowPreview = ShowFormsAsModal,
        };

        try
        {
            var результат = await Printing
                .PrintSelectionDirectAsync(mailIds, request, cancellationToken)
                .ConfigureAwait(false);

            if (!результат.IsSuccess)
            {
                return AutomationPrintOutcome.Failure(
                    AutomationWarningCodes.DocumentError,
                    результат.ErrorMessage ?? "Не удалось напечатать конверты.");
            }

            return AutomationPrintOutcome.Ok(
                $"Напечатано конвертов: {mailIds.Count}.");
        }
        catch (Exception error)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DocumentError,
                "Ошибка печати: " + error.Message);
        }
    }

    private static AutomationPrintOutcome ВыполнитьПечатьДокументов(IReadOnlyList<ReportDocument> documents)
    {
        var принтер = new PrinterManager().GetDefaultPrinter();

        if (принтер is null)
        {
            return AutomationPrintOutcome.Failure(
                AutomationWarningCodes.DocumentError,
                PrinterManager.NoDefaultPrinterMessage);
        }

        var канал = new ReportPrintChannel();

        foreach (var document in documents)
        {
            var ошибка = канал.SendToSpooler(document, принтер.Name, 1);

            if (ошибка is not null)
            {
                return AutomationPrintOutcome.Failure(
                    AutomationWarningCodes.DocumentError,
                    "Ошибка печати: " + ошибка);
            }
        }

        return AutomationPrintOutcome.Ok($"Напечатано документов: {documents.Count}.");
    }

    private static bool СохранитьДокументы(IReadOnlyList<ReportDocument> documents, string resultFullPath)
    {
        try
        {
            var каталог = Path.GetDirectoryName(Path.GetFullPath(resultFullPath));

            if (!string.IsNullOrEmpty(каталог))
            {
                Directory.CreateDirectory(каталог);
            }

            var текст = new System.Text.StringBuilder();

            foreach (var document in documents)
            {
                текст.AppendLine(AutomationReportBuilder.ToPlainText(document));
            }

            File.WriteAllText(resultFullPath, текст.ToString(), new System.Text.UTF8Encoding(false));

            return true;
        }
        catch
        {
            return false;
        }
    }

    private PrintSessionController Printing
    {
        get
        {
            if (_printing is not null)
            {
                return _printing;
            }

            var session = Session
                ?? throw new InvalidOperationException("База данных не загружена.");

            var каталог = Path.GetDirectoryName(Path.GetFullPath(_databasePath)) ?? AppContext.BaseDirectory;

            var контрольШрифта = new Picket302.Infrastructure.Fonts.ZipcodeFontGuard();
            var рендерер = new EnvelopeRenderer(new GdiTextMeasurer());

            var контроллер = new PrintSessionController(
                new PrintTemplateRepository(session),
                new BlankFieldRepository(session),
                new PrintJobRepository(session),
                new PrintDataSource(new MailRepository(session), new CounterpartyRepository(session)),
                new PrintAuditWriter(каталог),
                new SystemDrawingPrintSpooler(),
                рендерер,
                new PaperSizeValidator(),
                new PostIndexRenderer(контрольШрифта),
                new QrCodeRenderer(),
                new LogoRenderer());

            _printing = контроллер;

            return контроллер;
        }
    }
}

/// <summary>
/// Построитель печатных документов реестров и описей для автоматизации
/// (ТЗ §9.1, функция 113; форматы обмена — ТЗ §9.4).
/// </summary>
public static class AutomationReportBuilder
{
    /// <summary>
    /// Строит документ реестра по данным контракта <c>RegistersData</c>.
    /// </summary>
    /// <param name="register">Данные реестра.</param>
    /// <param name="templateName">Наименование шаблона реестра.</param>
    /// <returns>Печатный документ реестра.</returns>
    public static ReportDocument BuildRegister(RegisterData register, string templateName)
    {
        ArgumentNullException.ThrowIfNull(register);

        var заголовок = string.IsNullOrWhiteSpace(templateName)
            ? "Реестр почтовых отправлений"
            : templateName;

        var документ = new ReportDocument
        {
            Title = заголовок,
            QueryId = "qrReport_RegistersData",
        };

        var блок = new ReportBlock
        {
            Caption = string.IsNullOrWhiteSpace(register.Number)
                ? заголовок
                : $"{заголовок} № {register.Number}",
        };

        блок.HeaderLines.Add("№ пакета");
        блок.HeaderLines.Add("Получатель");
        блок.HeaderLines.Add("Адрес");
        блок.HeaderLines.Add("Индекс");
        блок.HeaderLines.Add("Примечание");

        блок.Columns.Add(new ReportColumn("Номер", 30m));
        блок.Columns.Add(new ReportColumn("Получатель", 60m));
        блок.Columns.Add(new ReportColumn("Адрес", 70m));
        блок.Columns.Add(new ReportColumn("Индекс", 25m));
        блок.Columns.Add(new ReportColumn("Примечание", 55m));

        foreach (var package in register.Packages)
        {
            блок.Rows.Add(new ReportRow(
                package.Number,
                package.To.Name,
                package.To.Address,
                package.To.ZipCode,
                package.Note));
        }

        блок.Totals.Add($"Всего пакетов: {register.Packages.Count}");

        if (!string.IsNullOrWhiteSpace(register.From.Name))
        {
            блок.FooterLines.Add($"Отправитель: {register.From.Name}");
        }

        документ.Blocks.Add(блок);

        return документ;
    }

    /// <summary>
    /// Строит документ описи по данным контракта <c>InventoriesData</c>.
    /// </summary>
    /// <param name="inventory">Данные описи.</param>
    /// <returns>Печатный документ описи.</returns>
    public static ReportDocument BuildInventory(InventoryData inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var документ = new ReportDocument
        {
            Title = "Опись вложений",
            QueryId = "qrReport_InventoriesData",
        };

        var блок = new ReportBlock
        {
            Caption = "Опись вложений",
        };

        блок.HeaderLines.Add("№");
        блок.HeaderLines.Add("Наименование предмета");

        блок.Columns.Add(new ReportColumn("Номер", 20m));
        блок.Columns.Add(new ReportColumn("Предмет", 160m));

        var номер = 1;

        foreach (var item in inventory.Items)
        {
            блок.Rows.Add(new ReportRow(номер.ToString(System.Globalization.CultureInfo.InvariantCulture), item.ItemName));
            номер++;
        }

        блок.Totals.Add($"Всего предметов: {inventory.Items.Count}");

        if (!string.IsNullOrWhiteSpace(inventory.FromName))
        {
            блок.FooterLines.Add($"Отправитель: {inventory.FromName}");
        }

        if (!string.IsNullOrWhiteSpace(inventory.ToName))
        {
            блок.FooterLines.Add($"Получатель: {inventory.ToName}");
        }

        документ.Blocks.Add(блок);

        return документ;
    }

    /// <summary>
    /// Представляет печатный документ текстом — формат сохранения результата
    /// печати реестра без формы (ТЗ §9.1, метод 21).
    /// </summary>
    /// <param name="document">Печатный документ.</param>
    /// <returns>Текст документа.</returns>
    public static string ToPlainText(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var текст = new System.Text.StringBuilder();

        текст.AppendLine(document.Title);
        текст.AppendLine(new string('=', Math.Max(1, document.Title.Length)));
        текст.AppendLine();

        foreach (var блок in document.Blocks)
        {
            if (!string.IsNullOrWhiteSpace(блок.Caption))
            {
                текст.AppendLine(блок.Caption);
            }

            foreach (var строка in блок.HeaderLines)
            {
                текст.AppendLine(строка);
            }

            текст.AppendLine();

            foreach (var row in блок.Rows)
            {
                текст.AppendLine(string.Join(" | ", row.Cells.Select(ячейка => ячейка ?? string.Empty)));
            }

            foreach (var итог in блок.Totals)
            {
                текст.AppendLine(итог);
            }

            foreach (var строка in блок.FooterLines)
            {
                текст.AppendLine(строка);
            }

            текст.AppendLine();
        }

        return текст.ToString();
    }
}

/// <summary>
/// Канал тихой печати документов реестров и описей: отправляет документ в
/// спулер Windows без показа окон (ТЗ §9.2 — «слепой запуск»).
/// </summary>
public sealed class ReportPrintChannel : ISilentPrintChannel
{
    /// <inheritdoc />
    public string? SendToSpooler(ReportDocument document, string printerName, int copies)
    {
        ArgumentNullException.ThrowIfNull(document);

        try
        {
            using var printDocument = new PrintDocument();

            if (!string.IsNullOrWhiteSpace(printerName))
            {
                printDocument.PrinterSettings.PrinterName = printerName;
            }

            printDocument.DocumentName = string.IsNullOrWhiteSpace(document.Title)
                ? "Picket302"
                : document.Title;

            printDocument.PrinterSettings.Copies = (short)Math.Clamp(copies, 1, 99);
            printDocument.DefaultPageSettings.Landscape = document.PageWidthMm > document.PageHeightMm;
            printDocument.PrintPage += (_, args) => ReportRenderer.Render(document, args);

            printDocument.Print();

            return null;
        }
        catch (Exception error)
        {
            return error.Message;
        }
    }
}

/// <summary>
/// Отрисовка печатного документа на странице (GDI+).
/// </summary>
internal static class ReportRenderer
{
    private const float MillimetersToPixel = 96f / 25.4f;

    /// <summary>
    /// Рисует документ на графическом контексте страницы.
    /// </summary>
    /// <param name="document">Печатный документ.</param>
    /// <param name="args">Параметры события печати страницы.</param>
    public static void Render(ReportDocument document, PrintPageEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(args);

        var graphics = args.Graphics;

        if (graphics is null)
        {
            return;
        }

        using var заголовок = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var текст = new Font("Segoe UI", 9f);
        using var шапка = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var перо = new Pen(Color.Black);

        var отступ = 12f * MillimetersToPixel;
        var ширина = Math.Max(1f, args.PageBounds.Width - 2 * отступ);
        var y = отступ;

        y = НарисоватьТекст(graphics, заголовок, document.Title, отступ, y, ширина);
        y += 4f;

        foreach (var блок in document.Blocks)
        {
            if (!string.IsNullOrWhiteSpace(блок.Caption))
            {
                y = НарисоватьТекст(graphics, заголовок, блок.Caption, отступ, y, ширина);
            }

            foreach (var строка in блок.HeaderLines)
            {
                y = НарисоватьТекст(graphics, шапка, строка, отступ, y, ширина);
            }

            var ширины = РаспределитьШирины(блок, ширина);
            var yШапки = y;

            for (var i = 0; i < ширины.Count && i < блок.HeaderLines.Count; i++)
            {
                y = НарисоватьТекст(graphics, шапка, блок.HeaderLines[i], отступ + Смещение(ширины, i), y, ширины[i]);
            }

            y = yШапки + текст.GetHeight(graphics) + 2f;

            foreach (var row in блок.Rows)
            {
                var yСтроки = y;

                for (var i = 0; i < ширины.Count; i++)
                {
                    var ячейка = i < row.Count ? row.Cells[i] : string.Empty;

                    НарисоватьТекст(graphics, текст, ячейка ?? string.Empty, отступ + Смещение(ширины, i), yСтроки, ширины[i]);
                }

                y = yСтроки + текст.GetHeight(graphics) + 1f;
            }

            y += 4f;

            foreach (var итог in блок.Totals)
            {
                y = НарисоватьТекст(graphics, шапка, итог, отступ, y, ширина);
            }

            foreach (var строка in блок.FooterLines)
            {
                y = НарисоватьТекст(graphics, текст, строка, отступ, y, ширина);
            }

            y += 6f;
        }

        graphics.DrawRectangle(перо, отступ, отступ, ширина, Math.Max(1f, y - отступ));
    }

    private static List<float> РаспределитьШирины(ReportBlock блок, float ширинаСтраницы)
    {
        var колонок = Math.Max(1, блок.Columns.Count);
        var список = new List<float>(колонок);

        var сумма = блок.Columns.Sum(колонка => (float)Math.Max(0m, колонка.WidthMm));

        if (сумма <= 0f)
        {
            for (var i = 0; i < колонок; i++)
            {
                список.Add(ширинаСтраницы / колонок);
            }

            return список;
        }

        foreach (var колонка in блок.Columns)
        {
            список.Add(ширинаСтраницы * (float)Math.Max(0m, колонка.WidthMm) / сумма);
        }

        return список;
    }

    private static float Смещение(IReadOnlyList<float> ширины, int индекс)
    {
        var смещение = 0f;

        for (var i = 0; i < индекс && i < ширины.Count; i++)
        {
            смещение += ширины[i];
        }

        return смещение;
    }

    private static float НарисоватьТекст(
        Graphics graphics,
        Font font,
        string значение,
        float x,
        float y,
        float ширина)
    {
        var прямоугольник = new RectangleF(x, y, Math.Max(1f, ширина), font.GetHeight(graphics) + 2f);

        using var формат = new StringFormat(StringFormatFlags.NoWrap)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        graphics.DrawString(значение ?? string.Empty, font, Brushes.Black, прямоугольник, формат);

        return прямоугольник.Bottom;
    }
}

