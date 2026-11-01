namespace Picket302.ComAutomation;

/// <summary>
/// Коды результата последней операции автоматизации (ТЗ §9.2, таблица 9.2).
/// </summary>
public static class AutomationWarningCodes
{
    /// <summary>Печать реестра или получение списка шаблонов завершилось без ошибок.</summary>
    public const int Success = 0;

    /// <summary>База данных не загружена либо недоступна.</summary>
    public const int DatabaseNotLoaded = 1;

    /// <summary>XML повреждён или не соответствует контракту обмена.</summary>
    public const int InvalidXml = 2;

    /// <summary>Ошибка документа: документ не удалось сформировать.</summary>
    public const int DocumentError = 3;

    /// <summary>Ошибка шаблона: файл шаблона не найден.</summary>
    public const int TemplateNotFound = 4;

    /// <summary>
    /// Возвращает краткое описание кода результата на русском языке.
    /// </summary>
    /// <param name="code">Код результата.</param>
    /// <returns>Текст описания кода.</returns>
    public static string Describe(int code) => code switch
    {
        Success => "Ошибок нет.",
        DatabaseNotLoaded => "База данных не загружена.",
        InvalidXml => "Ошибка чтения XML-данных.",
        DocumentError => "Ошибка подготовки документа.",
        TemplateNotFound => "Файл шаблона не найден.",
        _ => "Неизвестный код результата.",
    };
}

/// <summary>
/// Вид документа, определяемый XML-контрактом обмена (ТЗ §9.4).
/// </summary>
public enum AutomationDocumentKind
{
    /// <summary>Конверты — контракт <c>EnvelopesData</c>.</summary>
    Envelopes = 0,

    /// <summary>Уведомления — контракт <c>EnvelopesData</c> с шаблоном уведомления.</summary>
    Notifications = 1,

    /// <summary>Описи — контракт <c>InventoriesData</c>.</summary>
    Inventories = 2,

    /// <summary>Реестры — контракт <c>RegistersData</c>.</summary>
    Registers = 3,
}

/// <summary>
/// Вид подготовленной команды автоматизации (ТЗ §9.1, функция 113).
/// </summary>
public enum AutomationCommandKind
{
    /// <summary>Печать из файла.</summary>
    PrintFromFile = 0,

    /// <summary>Печать из XML-строки.</summary>
    PrintFromXml = 1,

    /// <summary>Печать одного конверта.</summary>
    PrintEnvelope = 2,

    /// <summary>Печать реестра без формы с сохранением результата.</summary>
    PrintRegisterWithoutForm = 3,

    /// <summary>Получение пользовательских шаблонов реестров.</summary>
    GetRegisterTemplateNames = 4,

    /// <summary>Получение стандартных шаблонов реестров.</summary>
    GetStdRegisterTemplateNames = 5,

    /// <summary>Безопасная загрузка базы данных.</summary>
    SafeLoadDatabase = 6,
}

/// <summary>
/// Подготовленная команда автоматизации: публичный слой только формирует
/// запрос, исполнение выполняет внутренний слой (ТЗ §9.1, функция 113).
/// </summary>
public abstract record AutomationCommand
{
    /// <summary>Вид команды.</summary>
    public abstract AutomationCommandKind Kind { get; }
}

/// <summary>
/// Команда печати документа из файла.
/// </summary>
/// <param name="DocumentKind">Вид документа.</param>
/// <param name="Path">Путь к файлу с данными.</param>
public sealed record PrintFromFileCommand(AutomationDocumentKind DocumentKind, string Path) : AutomationCommand
{
    /// <inheritdoc />
    public override AutomationCommandKind Kind => AutomationCommandKind.PrintFromFile;
}

/// <summary>
/// Команда печати документа из XML-строки.
/// </summary>
/// <param name="DocumentKind">Вид документа.</param>
/// <param name="Xml">XML-текст контракта обмена.</param>
public sealed record PrintFromXmlCommand(AutomationDocumentKind DocumentKind, string Xml) : AutomationCommand
{
    /// <inheritdoc />
    public override AutomationCommandKind Kind => AutomationCommandKind.PrintFromXml;
}

/// <summary>
/// Команда печати одного конверта из шести строковых параметров.
/// </summary>
/// <param name="From">Наименование отправителя.</param>
/// <param name="FromAddress">Адрес отправителя.</param>
/// <param name="FromZipCode">Почтовый индекс отправителя.</param>
/// <param name="To">Наименование получателя.</param>
/// <param name="ToAddress">Адрес получателя.</param>
/// <param name="ToZipCode">Почтовый индекс получателя.</param>
public sealed record PrintEnvelopeCommand(
    string From,
    string FromAddress,
    string FromZipCode,
    string To,
    string ToAddress,
    string ToZipCode) : AutomationCommand
{
    /// <inheritdoc />
    public override AutomationCommandKind Kind => AutomationCommandKind.PrintEnvelope;
}

/// <summary>
/// Команда печати реестра без показа формы с сохранением результата.
/// </summary>
/// <param name="Xml">XML-текст формата <c>RegistersData</c>.</param>
/// <param name="TemplateName">Имя шаблона реестра.</param>
/// <param name="ResultFullPath">Полный путь сохраняемого результата.</param>
public sealed record PrintRegisterWithoutFormCommand(
    string Xml,
    string TemplateName,
    string ResultFullPath) : AutomationCommand
{
    /// <inheritdoc />
    public override AutomationCommandKind Kind => AutomationCommandKind.PrintRegisterWithoutForm;
}

/// <summary>
/// Команда получения пользовательских шаблонов реестров.
/// </summary>
public sealed record GetRegisterTemplateNamesCommand : AutomationCommand
{
    /// <inheritdoc />
    public override AutomationCommandKind Kind => AutomationCommandKind.GetRegisterTemplateNames;
}

/// <summary>
/// Команда получения стандартных шаблонов реестров.
/// </summary>
public sealed record GetStdRegisterTemplateNamesCommand : AutomationCommand
{
    /// <inheritdoc />
    public override AutomationCommandKind Kind => AutomationCommandKind.GetStdRegisterTemplateNames;
}

/// <summary>
/// Команда безопасной загрузки базы данных.
/// </summary>
public sealed record SafeLoadDatabaseCommand : AutomationCommand
{
    /// <inheritdoc />
    public override AutomationCommandKind Kind => AutomationCommandKind.SafeLoadDatabase;
}

/// <summary>
/// Подготовка команд автоматизации (публичный слой контракта, ТЗ §9.1).
/// </summary>
/// <remarks>
/// Подготовка не выполняет никаких действий: она лишь формирует объект
/// запроса, который затем исполняет внутренний слой
/// (<see cref="AutomationDispatcher"/>). Это делает подготовку наблюдаемой и
/// безопасной: вызов <c>Prepare*</c> не печатает и не меняет данные.
/// </remarks>
public sealed class AutomationCommandPreparer
{
    /// <summary>
    /// Подготавливает команду печати из файла.
    /// </summary>
    /// <param name="kind">Вид документа: <c>Envelopes</c>, <c>Notifications</c>, <c>Inventories</c> или <c>Registers</c>.</param>
    /// <param name="path">Путь к файлу с данными.</param>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PreparePrintFromFile(string kind, string path) =>
        PreparePrintFromFile(ParseDocumentKind(kind), path);

    /// <summary>
    /// Подготавливает команду печати из файла.
    /// </summary>
    /// <param name="kind">Вид документа.</param>
    /// <param name="path">Путь к файлу с данными.</param>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PreparePrintFromFile(AutomationDocumentKind kind, string path) =>
        new PrintFromFileCommand(kind, path ?? string.Empty);

    /// <summary>
    /// Подготавливает команду печати из XML-строки.
    /// </summary>
    /// <param name="kind">Вид документа: <c>Envelopes</c>, <c>Notifications</c>, <c>Inventories</c> или <c>Registers</c>.</param>
    /// <param name="xml">XML-текст контракта обмена.</param>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PreparePrintFromXml(string kind, string xml) =>
        PreparePrintFromXml(ParseDocumentKind(kind), xml);

    /// <summary>
    /// Подготавливает команду печати из XML-строки.
    /// </summary>
    /// <param name="kind">Вид документа.</param>
    /// <param name="xml">XML-текст контракта обмена.</param>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PreparePrintFromXml(AutomationDocumentKind kind, string xml) =>
        new PrintFromXmlCommand(kind, xml ?? string.Empty);

    /// <summary>
    /// Подготавливает команду печати одного конверта.
    /// </summary>
    /// <param name="from">Наименование отправителя.</param>
    /// <param name="fromAddress">Адрес отправителя.</param>
    /// <param name="fromZipCode">Почтовый индекс отправителя.</param>
    /// <param name="to">Наименование получателя.</param>
    /// <param name="toAddress">Адрес получателя.</param>
    /// <param name="toZipCode">Почтовый индекс получателя.</param>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PreparePrintEnvelope(
        string from,
        string fromAddress,
        string fromZipCode,
        string to,
        string toAddress,
        string toZipCode) =>
        new PrintEnvelopeCommand(
            from ?? string.Empty,
            fromAddress ?? string.Empty,
            fromZipCode ?? string.Empty,
            to ?? string.Empty,
            toAddress ?? string.Empty,
            toZipCode ?? string.Empty);

    /// <summary>
    /// Подготавливает команду печати реестра без формы.
    /// </summary>
    /// <param name="xml">XML-текст формата <c>RegistersData</c>.</param>
    /// <param name="templateName">Имя шаблона реестра.</param>
    /// <param name="resultFullPath">Полный путь сохраняемого результата.</param>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PreparePrintRegisterWithoutForm(
        string xml,
        string templateName,
        string resultFullPath) =>
        new PrintRegisterWithoutFormCommand(
            xml ?? string.Empty,
            templateName ?? string.Empty,
            resultFullPath ?? string.Empty);

    /// <summary>
    /// Подготавливает команду получения пользовательских шаблонов реестров.
    /// </summary>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PrepareGetRegisterTemplateNames() => new GetRegisterTemplateNamesCommand();

    /// <summary>
    /// Подготавливает команду получения стандартных шаблонов реестров.
    /// </summary>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PrepareGetStdRegisterTemplateNames() => new GetStdRegisterTemplateNamesCommand();

    /// <summary>
    /// Подготавливает команду безопасной загрузки базы данных.
    /// </summary>
    /// <returns>Подготовленная команда.</returns>
    public AutomationCommand PrepareSafeLoadDatabase() => new SafeLoadDatabaseCommand();

    /// <summary>
    /// Разбирает строковое наименование вида документа.
    /// </summary>
    /// <param name="kind">Наименование вида документа.</param>
    /// <returns>Вид документа.</returns>
    /// <exception cref="ArgumentException">
    /// Наименование вида документа не распознано.
    /// </exception>
    public static AutomationDocumentKind ParseDocumentKind(string kind)
    {
        var normalized = (kind ?? string.Empty).Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Не указан вид документа.", nameof(kind));
        }

        if (normalized.Equals("Envelopes", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Конверты", StringComparison.OrdinalIgnoreCase))
        {
            return AutomationDocumentKind.Envelopes;
        }

        if (normalized.Equals("Notifications", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Уведомления", StringComparison.OrdinalIgnoreCase))
        {
            return AutomationDocumentKind.Notifications;
        }

        if (normalized.Equals("Inventories", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Описи", StringComparison.OrdinalIgnoreCase))
        {
            return AutomationDocumentKind.Inventories;
        }

        if (normalized.Equals("Registers", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Реестры", StringComparison.OrdinalIgnoreCase))
        {
            return AutomationDocumentKind.Registers;
        }

        throw new ArgumentException($"Неизвестный вид документа: «{kind}».", nameof(kind));
    }
}
