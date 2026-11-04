namespace Picket302.Infrastructure;

/// <summary>
/// Пути профиля приложения Picket302: каталог данных и служебные файлы в нём.
/// </summary>
/// <remarks>
/// <para>
/// Единственная точка построения путей профиля. Производственный код
/// использует <see cref="ПоУмолчанию"/> — каталог
/// <c>%AppData%\Picket302</c>; автоматические проверки создают профиль
/// вызовом <see cref="ДляКорня"/> во временном каталоге, поэтому реальные
/// файлы пользователя остаются нетронутыми (изоляция тестового профиля).
/// </para>
/// <para>
/// Отдельные классы инфраструктуры принимают конкретные пути из этого
/// объекта через конструкторы, а не обращаются к
/// <c>Environment.SpecialFolder.ApplicationData</c> самостоятельно.
/// </para>
/// </remarks>
public sealed record Picket302ProfilePaths
{
    /// <summary>Имя каталога приложения в профиле пользователя.</summary>
    public const string КаталогПриложения = "Picket302";

    /// <summary>Имя файла базы данных.</summary>
    public const string ИмяФайлаБазы = "picket302.db";

    /// <summary>Имя файла реквизитов подключения (ТЗ §8.11, F-B11).</summary>
    public const string ИмяФайлаПодключения = "connection.json";

    /// <summary>Имя файла справочника серверов БД (ТЗ §8.8, функция 101).</summary>
    public const string ИмяФайлаСерверов = "servers.ini";

    /// <summary>Имя файла аудита печати.</summary>
    public const string ИмяФайлаАудитаПечати = "print_audit.log";

    /// <summary>Имя файла лицензии (ТЗ §8.10, функция 123).</summary>
    public const string ИмяФайлаЛицензии = "license.dat";

    /// <summary>Имя файла настроек интерфейса (ТЗ §8.1, функция 9).</summary>
    public const string ИмяФайлаНастроекИнтерфейса = "ui-settings.json";

    /// <summary>Имя файла настроек почтового клиента (ТЗ §8.7, функция 93).</summary>
    public const string ИмяФайлаНастроекПочты = "mail-settings.json";

    /// <summary>Имя файла журнала COM-сервера автоматизации.</summary>
    public const string ИмяФайлаЖурналаComАвтоматизации = "com-automation.log";

    /// <summary>Имя каталога журналов приложения (ТЗ §8.11, функция 111).</summary>
    public const string КаталогЖурналов = "logs";

    /// <summary>Имя каталога защищённых секретов DPAPI.</summary>
    public const string КаталогСекретов = "Secrets";

    /// <summary>Каталог данных профиля: база, реквизиты, журналы, лицензия.</summary>
    public string DataDirectory { get; init; } = string.Empty;

    /// <summary>Каталог файлов журналов приложения.</summary>
    public string LogsDirectory { get; init; } = string.Empty;

    /// <summary>Каталог защищённых блоков DPAPI (пароли подключений и почты).</summary>
    public string SecretsDirectory { get; init; } = string.Empty;

    /// <summary>Путь к файлу базы данных по умолчанию профиля.</summary>
    public string DatabasePath { get; init; } = string.Empty;

    /// <summary>Путь к файлу реквизитов подключения.</summary>
    public string ConnectionSettingsPath { get; init; } = string.Empty;

    /// <summary>Путь к файлу справочника серверов БД.</summary>
    public string ServerRegistryPath { get; init; } = string.Empty;

    /// <summary>Путь к файлу аудита печати.</summary>
    public string PrintAuditPath { get; init; } = string.Empty;

    /// <summary>Путь к файлу лицензии.</summary>
    public string LicensePath { get; init; } = string.Empty;

    /// <summary>Путь к файлу настроек интерфейса.</summary>
    public string UiSettingsPath { get; init; } = string.Empty;

    /// <summary>Путь к файлу настроек почтового клиента.</summary>
    public string MailSettingsPath { get; init; } = string.Empty;

    /// <summary>Путь к файлу журнала COM-сервера автоматизации.</summary>
    public string ComAutomationLogPath { get; init; } = string.Empty;

    /// <summary>
    /// Возвращает пути производственного профиля: <c>%AppData%\Picket302</c>.
    /// </summary>
    /// <returns>Пути профиля по умолчанию.</returns>
    public static Picket302ProfilePaths ПоУмолчанию() => ИзКаталога(КаталогДанныхПоУмолчанию());

    /// <summary>
    /// Возвращает пути тестового профиля внутри указанного корня: структура
    /// каталогов повторяет производственную — <c>&lt;корень&gt;\Picket302\…</c>.
    /// </summary>
    /// <param name="корень">Уникальный временный корень профиля теста.</param>
    /// <returns>Пути профиля во временном корне.</returns>
    public static Picket302ProfilePaths ДляКорня(string корень)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(корень);

        return ИзКаталога(Path.Combine(корень, КаталогПриложения));
    }

    /// <summary>
    /// Возвращает пути профиля для указанного каталога данных.
    /// </summary>
    /// <param name="каталогДанных">Каталог данных профиля.</param>
    /// <returns>Пути профиля.</returns>
    public static Picket302ProfilePaths ИзКаталога(string каталогДанных)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогДанных);

        return new Picket302ProfilePaths
        {
            DataDirectory = каталогДанных,
            LogsDirectory = Path.Combine(каталогДанных, КаталогЖурналов),
            SecretsDirectory = Path.Combine(каталогДанных, КаталогСекретов),
            DatabasePath = Path.Combine(каталогДанных, ИмяФайлаБазы),
            ConnectionSettingsPath = Path.Combine(каталогДанных, ИмяФайлаПодключения),
            ServerRegistryPath = Path.Combine(каталогДанных, ИмяФайлаСерверов),
            PrintAuditPath = Path.Combine(каталогДанных, ИмяФайлаАудитаПечати),
            LicensePath = Path.Combine(каталогДанных, ИмяФайлаЛицензии),
            UiSettingsPath = Path.Combine(каталогДанных, ИмяФайлаНастроекИнтерфейса),
            MailSettingsPath = Path.Combine(каталогДанных, ИмяФайлаНастроекПочты),
            ComAutomationLogPath = Path.Combine(каталогДанных, ИмяФайлаЖурналаComАвтоматизации),
        };
    }

    /// <summary>
    /// Возвращает каталог данных производственного профиля —
    /// <c>%AppData%\Picket302</c>. Единственное обращение к
    /// <c>Environment.SpecialFolder.ApplicationData</c> для путей профиля.
    /// </summary>
    /// <returns>Полный путь к каталогу данных.</returns>
    public static string КаталогДанныхПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        КаталогПриложения);
}
