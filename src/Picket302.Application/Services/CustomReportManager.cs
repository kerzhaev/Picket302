using Picket302.Domain.Queries;

namespace Picket302.Application.Services;

/// <summary>Запись о зарегистрированном пользовательском отчёте.</summary>
/// <param name="Name">Наименование отчёта.</param>
/// <param name="TemplatePath">Путь к пакету макета (.lbk).</param>
/// <param name="QueryXmlPath">Путь к файлу query.xml проекции данных.</param>
public sealed record UserReportRegistration(string Name, string TemplatePath, string QueryXmlPath);

/// <summary>
/// Результат валидации пользовательского отчёта (ТЗ §8.4, функция 56:
/// «Синтаксическая ошибка в пользовательском шаблоне изолируется и выводится
/// в диалоге валидации»).
/// </summary>
/// <param name="IsValid">Признак корректности.</param>
/// <param name="ReportName">Наименование отчёта.</param>
/// <param name="Errors">Список ошибок валидации.</param>
public sealed record UserReportValidationResult(
    bool IsValid,
    string ReportName,
    IReadOnlyList<string> Errors);

/// <summary>
/// Пользовательские шаблоны реестров и описей (ТЗ §8.4, функция 56:
/// <c>Application.Services.CustomReportManager.RegisterUserReport</c>).
/// </summary>
/// <remarks>
/// Позволяет технологу добавлять в систему произвольные печатные отчеты на базе
/// связки «движок отчётов + query.xml»: макет берется из пользовательского
/// пакета, проекция данных — из прилагаемого query.xml. Каталог пользовательских
/// пакетов — <c>Templates/Reports/User/</c>; каждый отчёт хранится в подкаталоге
/// <c>Templates/Reports/User/&lt;ReportName&gt;/query.xml</c> (ТЗ §4.1).
/// </remarks>
public sealed class CustomReportManager
{
    /// <summary>Относительный каталог пользовательских отчетов.</summary>
    public const string UserReportsFolder = "Templates";

    /// <summary>Подкаталог пользовательских пакетов.</summary>
    public const string UserSubfolder = "Reports";

    /// <summary>Подкаталог отчетов пользователя.</summary>
    public const string UserReportsSubfolder = "User";

    private readonly ReportQueryXmlSerializer _сериализатор = new();
    private readonly QueryValidator _валидатор = new();
    private readonly string _корень;

    /// <summary>Создаёт менеджер пользовательских отчётов.</summary>
    /// <param name="rootDirectoryPath">
    /// Корневой каталог данных приложения; каталог отчётов строится относительно
    /// него. Каталог создаётся при необходимости.
    /// </param>
    public CustomReportManager(string rootDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectoryPath);

        _корень = rootDirectoryPath;
        Directory.CreateDirectory(UserReportsDirectory);
    }

    /// <summary>Каталог Templates/Reports/User.</summary>
    public string UserReportsDirectory =>
        Path.Combine(_корень, UserReportsFolder, UserSubfolder, UserReportsSubfolder);

    /// <summary>
    /// Регистрирует пользовательский отчёт: копирует пакет макета и query.xml
    /// в каталог пользовательских отчётов (ТЗ §8.4, функция 56).
    /// </summary>
    /// <param name="reportName">Наименование отчёта.</param>
    /// <param name="templatePath">Путь к исходному пакету макета (.lbk).</param>
    /// <param name="queryXmlPath">Путь к файлу query.xml.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат валидации.</returns>
    /// <exception cref="FileNotFoundException">Файл макета или запроса не найден.</exception>
    /// <exception cref="InvalidOperationException">Наименование уже занято.</exception>
    public async Task<UserReportValidationResult> RegisterUserReport(
        string reportName,
        string templatePath,
        string queryXmlPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportName);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryXmlPath);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("Пакет макета пользовательского отчёта не найден.", templatePath);
        }

        if (!File.Exists(queryXmlPath))
        {
            throw new FileNotFoundException("Файл query.xml пользовательского отчёта не найден.", queryXmlPath);
        }

        if (Exists(reportName))
        {
            throw new InvalidOperationException($"Пользовательский отчёт «{reportName}» уже зарегистрирован.");
        }

        // Синтаксическая ошибка в пользовательском шаблоне изолируется и
        // выводится в диалоге валидации (ТЗ §8.4, ф.56).
        var ошибки = new List<string>();
        var текстЗапроса = await File.ReadAllTextAsync(queryXmlPath, cancellationToken).ConfigureAwait(false);
        ReportQueryDefinition? определение = null;

        try
        {
            определение = _сериализатор.Deserialize(текстЗапроса);
            _валидатор.ValidateDefinition(определение);
        }
        catch (Exception ошибка) when (ошибка is ReportQueryXmlException or QueryValidationException or System.Xml.XmlException)
        {
            ошибки.Add(ошибка.Message);
        }

        var каталог = КаталогОтчёта(reportName);
        Directory.CreateDirectory(каталог);

        var целевойПакет = Path.Combine(каталог, Path.GetFileName(templatePath));
        var целевойЗапрос = Path.Combine(каталог, "query.xml");

        File.Copy(templatePath, целевойПакет, overwrite: true);
        await File.WriteAllTextAsync(целевойЗапрос, текстЗапроса, cancellationToken).ConfigureAwait(false);

        return new UserReportValidationResult(ошибки.Count == 0, reportName, ошибки);
    }

    /// <summary>
    /// Возвращает перечень зарегистрированных пользовательских отчётов.
    /// </summary>
    /// <returns>Список регистраций.</returns>
    public IReadOnlyList<UserReportRegistration> ListUserReports()
    {
        var перечень = new List<UserReportRegistration>();

        if (!Directory.Exists(UserReportsDirectory))
        {
            return перечень;
        }

        foreach (var каталог in Directory.GetDirectories(UserReportsDirectory))
        {
            var запрос = Path.Combine(каталог, "query.xml");

            if (!File.Exists(запрос))
            {
                continue;
            }

            var пакеты = Directory.GetFiles(каталог, "*.lbk");

            перечень.Add(new UserReportRegistration(
                Path.GetFileName(каталог),
                пакеты.Length > 0 ? пакеты[0] : string.Empty,
                запрос));
        }

        return перечень;
    }

    /// <summary>Проверяет, зарегистрирован ли отчёт с таким наименованием.</summary>
    /// <param name="reportName">Наименование.</param>
    /// <returns><see langword="true"/>, если отчёт зарегистрирован.</returns>
    public bool Exists(string reportName) =>
        File.Exists(Path.Combine(КаталогОтчёта(reportName), "query.xml"));

    /// <summary>
    /// Загружает определение запроса зарегистрированного отчёта.
    /// </summary>
    /// <param name="reportName">Наименование отчёта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Определение запроса.</returns>
    /// <exception cref="FileNotFoundException">Отчёт не зарегистрирован.</exception>
    public async Task<ReportQueryDefinition> LoadUserQueryAsync(
        string reportName,
        CancellationToken cancellationToken = default)
    {
        var путь = Path.Combine(КаталогОтчёта(reportName), "query.xml");

        if (!File.Exists(путь))
        {
            throw new FileNotFoundException($"Пользовательский отчёт «{reportName}» не зарегистрирован.", путь);
        }

        var текст = await File.ReadAllTextAsync(путь, cancellationToken).ConfigureAwait(false);

        return _сериализатор.Deserialize(текст);
    }

    /// <summary>
    /// Редактирование запроса пользователем (ТЗ §4.1): сохраняет новый текст
    /// query.xml после валидации.
    /// </summary>
    /// <param name="reportName">Наименование отчёта.</param>
    /// <param name="queryXml">Новый текст query.xml.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат валидации.</returns>
    public async Task<UserReportValidationResult> EditUserQueryAsync(
        string reportName,
        string queryXml,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryXml);

        var ошибки = new List<string>();
        ReportQueryDefinition? определение = null;

        try
        {
            определение = _сериализатор.Deserialize(queryXml);
            _валидатор.ValidateDefinition(определение);
        }
        catch (Exception ошибка) when (ошибка is ReportQueryXmlException or QueryValidationException or System.Xml.XmlException)
        {
            ошибки.Add(ошибка.Message);
        }

        if (ошибки.Count == 0 && определение is not null)
        {
            var путь = Path.Combine(КаталогОтчёта(reportName), "query.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(путь)!);
            await File.WriteAllTextAsync(путь, queryXml, cancellationToken).ConfigureAwait(false);
        }

        return new UserReportValidationResult(ошибки.Count == 0, reportName, ошибки);
    }

    private string КаталогОтчёта(string reportName)
    {
        // Имя отчёта не должно выходить за пределы каталога отчётов.
        foreach (var недопустимый in Path.GetInvalidFileNameChars())
        {
            if (reportName.Contains(недопустимый))
            {
                throw new ArgumentException(
                    $"Наименование отчёта «{reportName}» содержит недопустимые знаки пути.",
                    nameof(reportName));
            }
        }

        return Path.Combine(UserReportsDirectory, reportName);
    }
}
