using System.Security;

using Microsoft.Win32;

using Реестр = Microsoft.Win32.Registry;

namespace Picket302.Infrastructure.Shell;

/// <summary>
/// Ошибка интеграции с оболочкой Windows: не удалось зарегистрировать или
/// удалить пункт контекстного меню (ТЗ §8.10, функция 106).
/// </summary>
public sealed class ShellIntegrationException : Exception
{
    /// <summary>
    /// Создаёт ошибку интеграции с оболочкой.
    /// </summary>
    /// <param name="message">Текст ошибки на русском языке.</param>
    public ShellIntegrationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Создаёт ошибку интеграции с оболочкой с указанием исходной причины.
    /// </summary>
    /// <param name="message">Текст ошибки на русском языке.</param>
    /// <param name="innerException">Исходная причина.</param>
    public ShellIntegrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Интеграция с оболочкой Windows: пункт контекстного меню Проводника
/// «Импорт в Picket302…» (ТЗ §8.10, функция 106 и §10.6, функция 135).
/// </summary>
/// <remarks>
/// <para>
/// Регистрация выполняется только в ветке пользователя
/// <c>HKCU\Software\Classes</c>, поэтому права администратора не требуются.
/// </para>
/// <para>
/// Пункт появляется для файлов форматов CSV, XLS, XLSX и XML (ограничение
/// списком расширений <c>Extensions</c>) и для папок (отдельная ветка
/// <c>Directory</c>). Команда запускает оболочку с параметром
/// <c>--import "%1"</c>, где <c>%1</c> — выбранный объект.
/// </para>
/// </remarks>
public sealed class ExplorerIntegration
{
    /// <summary>Имя пункта контекстного меню по умолчанию.</summary>
    public const string ИмяПунктаПоУмолчанию = "Picket302Import";

    /// <summary>Название пункта контекстного меню по умолчанию.</summary>
    public const string НазваниеПунктаПоУмолчанию = "Импорт в Picket302…";

    /// <summary>Корень ветки классов в профиле пользователя.</summary>
    public const string КореньКлассов = @"Software\Classes";

    /// <summary>Расширения файлов, для которых показывается пункт меню.</summary>
    public static readonly IReadOnlyList<string> РасширенияИмпорта = [".csv", ".xls", ".xlsx", ".xml"];

    /// <summary>
    /// Сообщение об отказе доступа к ветке пользователя реестра.
    /// </summary>
    public const string СообщениеОбОтказеДоступа =
        "Не удалось изменить пункт контекстного меню Проводника: нет доступа к ветке реестра HKCU. "
        + "Запустите программу от имени пользователя, которому принадлежит профиль, "
        + "и повторите операцию.";

    private readonly string _путьОболочки;
    private readonly string _имяПункта;
    private readonly string _названиеПункта;

    /// <summary>
    /// Создаёт интеграцию с оболочкой Windows.
    /// </summary>
    /// <param name="путьОболочки">Путь к исполняемому файлу оболочки Picket302.</param>
    /// <param name="имяПункта">Имя пункта в ветке реестра.</param>
    /// <param name="названиеПункта">Название пункта, показываемое пользователю.</param>
    public ExplorerIntegration(
        string путьОболочки,
        string имяПункта = ИмяПунктаПоУмолчанию,
        string названиеПункта = НазваниеПунктаПоУмолчанию)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьОболочки);
        ArgumentException.ThrowIfNullOrWhiteSpace(имяПункта);

        _путьОболочки = путьОболочки;
        _имяПункта = имяПункта;
        _названиеПункта = названиеПункта;
    }

    /// <summary>Путь к исполняемому файлу оболочки.</summary>
    public string ПутьОболочки => _путьОболочки;

    /// <summary>Имя пункта в ветке реестра.</summary>
    public string ИмяПункта => _имяПункта;

    /// <summary>Название пункта, показываемое пользователю.</summary>
    public string НазваниеПункта => _названиеПункта;

    /// <summary>
    /// Регистрирует пункт контекстного меню Проводника в ветке пользователя
    /// <c>HKCU\Software\Classes</c>.
    /// </summary>
    /// <exception cref="ShellIntegrationException">
    /// Требуется право записи в ветку <c>HKCU</c>: текст ошибки объясняет,
    /// от имени какого пользователя следует запустить программу.
    /// </exception>
    public void RegisterShellMenu()
    {
        try
        {
            using var корень = Реестр.CurrentUser.CreateSubKey(КореньКлассов, writable: true)
                ?? throw new ShellIntegrationException(
                    $"Не удалось открыть ветку реестра HKCU\\{КореньКлассов}.");

            foreach (var путь in BuildRegistryPaths(_имяПункта))
            {
                using var пункт = корень.CreateSubKey(путь, writable: true)
                    ?? throw new ShellIntegrationException(
                        $"Не удалось создать ключ реестра HKCU\\{КореньКлассов}\\{путь}.");

                пункт.SetValue("MUIVerb", _названиеПункта, RegistryValueKind.String);
                пункт.SetValue("Icon", _путьОболочки, RegistryValueKind.String);
                пункт.SetValue("Extensions", BuildExtensionsValue(), RegistryValueKind.String);

                using var команда = пункт.CreateSubKey("command", writable: true)
                    ?? throw new ShellIntegrationException(
                        $"Не удалось создать ключ реестра HKCU\\{КореньКлассов}\\{путь}\\command.");

                команда.SetValue(string.Empty, BuildCommand(_путьОболочки), RegistryValueKind.String);
            }
        }
        catch (UnauthorizedAccessException ошибка)
        {
            throw new ShellIntegrationException(СообщениеОбОтказеДоступа, ошибка);
        }
        catch (SecurityException ошибка)
        {
            throw new ShellIntegrationException(СообщениеОбОтказеДоступа, ошибка);
        }
    }

    /// <summary>
    /// Удаляет зарегистрированный пункт контекстного меню Проводника.
    /// </summary>
    /// <exception cref="ShellIntegrationException">
    /// Требуется право записи в ветку <c>HKCU</c>: текст ошибки объясняет,
    /// от имени какого пользователя следует запустить программу.
    /// </exception>
    public void UnregisterShellMenu()
    {
        try
        {
            using var корень = Реестр.CurrentUser.CreateSubKey(КореньКлассов, writable: true)
                ?? throw new ShellIntegrationException(
                    $"Не удалось открыть ветку реестра HKCU\\{КореньКлассов}.");

            foreach (var путь in BuildRegistryPaths(_имяПункта))
            {
                корень.DeleteSubKeyTree(путь, throwOnMissingSubKey: false);
            }
        }
        catch (UnauthorizedAccessException ошибка)
        {
            throw new ShellIntegrationException(СообщениеОбОтказеДоступа, ошибка);
        }
        catch (SecurityException ошибка)
        {
            throw new ShellIntegrationException(СообщениеОбОтказеДоступа, ошибка);
        }
    }

    /// <summary>
    /// Проверяет наличие зарегистрированного пункта контекстного меню.
    /// </summary>
    /// <returns>
    /// <see langword="true"/>, если пункт зарегистрирован в ветке пользователя;
    /// иначе <see langword="false"/>.
    /// </returns>
    public bool IsRegistered()
    {
        var путь = BuildRegistryPaths(_имяПункта)[0];

        try
        {
            using var пункт = Реестр.CurrentUser.OpenSubKey($"{КореньКлассов}\\{путь}");

            return пункт is not null;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// Формирует команду запуска оболочки для выбранного объекта Проводника.
    /// </summary>
    /// <param name="путьОболочки">Путь к исполняемому файлу оболочки.</param>
    /// <returns>Команда вида <c>"путь" --import "%1"</c>.</returns>
    public static string BuildCommand(string путьОболочки)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьОболочки);

        return $"\"{путьОболочки}\" --import \"%1\"";
    }

    /// <summary>
    /// Формирует перечень путей ветки реестра относительно
    /// <c>HKCU\Software\Classes</c>, в которых регистрируется пункт меню.
    /// </summary>
    /// <param name="имяПункта">Имя пункта.</param>
    /// <returns>Пути: для файлов и для папок.</returns>
    public static IReadOnlyList<string> BuildRegistryPaths(string имяПункта)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяПункта);

        return
        [
            $@"*\shell\{имяПункта}",
            $@"Directory\shell\{имяПункта}",
        ];
    }

    /// <summary>
    /// Формирует значение списка расширений для ветки <c>Extensions</c>.
    /// </summary>
    /// <returns>Список расширений через точку с запятой.</returns>
    private static string BuildExtensionsValue()
        => string.Join(';', РасширенияИмпорта.Select(расширение => расширение.TrimStart('.')));
}
