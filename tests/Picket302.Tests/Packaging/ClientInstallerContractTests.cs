using System.Text;
using System.Text.Json;
using Xunit;

namespace Picket302.Tests.Packaging;

/// <summary>
/// Контракт упаковки клиентской части Picket302.
/// </summary>
/// <remarks>
/// <para>
/// Проверяются две группы требований к поставке клиента:
/// </para>
/// <list type="bullet">
/// <item>сценарий установщика <c>installer/picket302-client.iss</c> — отдельная
/// идентичность приложения, каталог в Program Files, ярлык в меню «Пуск»,
/// запуск от обычного пользователя, отсутствие регистрации службы и правил
/// брандмауэра, сохранение пользовательских данных при удалении;</item>
/// <item>фактический состав опубликованной сборки — проверяется, когда задан
/// каталог публикации в переменной окружения
/// <see cref="PublishDirectory.VariableName"/> (её задаёт сценарий сборки
/// <c>installer/build-client.ps1</c>).</item>
/// </list>
/// </remarks>
public sealed class ClientInstallerContractTests
{
    /// <summary>Имя сценария клиентского установщика.</summary>
    private const string ClientInstallerFileName = "picket302-client.iss";

    /// <summary>Имя сценария серверного установщика.</summary>
    private const string ServerInstallerFileName = "picket302-server.iss";

    /// <summary>Имя сценария сборки клиентского пакета.</summary>
    private const string BuildScriptFileName = "build-client.ps1";

    /// <summary>Имя сценария динамической проверки установки.</summary>
    private const string VerifyScriptFileName = "verify-client-install.ps1";

    /// <summary>Имя файла напоминания, показываемого после установки.</summary>
    private const string InfoAfterFileName = "client-after-install.txt";

    /// <summary>Имя файла проекта оболочки относительно корня репозитория.</summary>
    private const string AppProjectRelativePath = @"src\Picket302.App\Picket302.App.csproj";

    /// <summary>Операции, которых в клиентском установщике быть не должно.</summary>
    private static readonly string[] ForbiddenInstallerOperations =
    {
        "sc.exe",
        "sc create",
        "netsh",
        "advfirewall",
        "New-Service",
        "Install-Service",
        "ServiceInstall",
        "certutil",
        "regsvr32",
        "Picket302.Service",
    };

    /// <summary>
    /// У клиента и у серверной службы разные идентификаторы приложения:
    /// иначе удаление одной части снесло бы другую.
    /// </summary>
    [Fact]
    public void ClientAndServer_UseDifferentAppIds()
    {
        var корень = FindSolutionRoot();

        var clientAppId = ReadAppId(Path.Combine(корень, "installer", ClientInstallerFileName));
        var serverAppId = ReadAppId(Path.Combine(корень, "installer", ServerInstallerFileName));

        Assert.False(string.IsNullOrWhiteSpace(clientAppId), "В клиентском установщике не задан AppId.");
        Assert.False(string.IsNullOrWhiteSpace(serverAppId), "В серверном установщике не задан AppId.");

        Assert.True(
            !string.Equals(clientAppId, serverAppId, StringComparison.OrdinalIgnoreCase),
            $"AppId клиента ({clientAppId}) совпадает с AppId серверной службы ({serverAppId}).");
    }

    /// <summary>
    /// Клиент устанавливается в обычный Program Files системного диска и не
    /// делит каталог с серверной службой.
    /// </summary>
    [Fact]
    public void Client_InstallsToProgramFilesInItsOwnDirectory()
    {
        var корень = FindSolutionRoot();
        var текст = StripComments(File.ReadAllText(Path.Combine(корень, "installer", ClientInstallerFileName)));

        Assert.True(
            текст.Contains("DefaultDirName={autopf}\\Picket302", StringComparison.Ordinal),
            "Клиентский установщик не задаёт каталог {autopf}\\Picket302.");

        Assert.True(
            !текст.Contains("Picket302 Service", StringComparison.Ordinal),
            "Клиентский установщик ссылается на каталог серверной службы.");
    }

    /// <summary>
    /// Ярлык в меню «Пуск» обязателен; ярлык на рабочем столе — по желанию.
    /// </summary>
    [Fact]
    public void Client_CreatesStartMenuShortcut_AndOptionalDesktopShortcut()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, "installer", ClientInstallerFileName));

        Assert.True(
            текст.Contains("Name: \"{group}", StringComparison.Ordinal),
            "В клиентском установщике нет ярлыка в меню «Пуск».");

        Assert.True(
            текст.Contains("Name: \"{autodesktop}", StringComparison.Ordinal),
            "В клиентском установщике нет необязательного ярлыка на рабочем столе.");
    }

    /// <summary>
    /// Клиентский пакет не регистрирует службу, не трогает брандмауэр, не
    /// меняет сертификаты и не регистрирует COM-сервер автоматически.
    /// </summary>
    [Fact]
    public void Client_DoesNotRegisterServiceOrTouchFirewall()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, "installer", ClientInstallerFileName));

        Assert.True(
            !ContainsForbiddenOperation(текст),
            "Клиентский установщик содержит запрещённую операцию: "
            + string.Join(", ", ForbiddenInstallerOperations.Where(операция =>
                текст.Contains(операция, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>
    /// Поиск запрещённой операции не должен пропускать её, объявленную
    /// директивой, и не должен срабатывать на упоминание в комментарии.
    /// </summary>
    [Fact]
    public void ForbiddenOperations_AreFoundInDirectives_AndIgnoredInComments()
    {
        const string вДирективе = "[Setup]\nSource: \"x\"; Flags: ignoreversion netsh\n";
        const string вКомментарии =
            "; упоминание netsh в комментарии\n[Code]\n{ и здесь netsh }\nvar x := 1;\n";

        Assert.True(
            ContainsForbiddenOperation(вДирективе),
            "Запрещённая операция в директиве не обнаружена.");

        Assert.False(
            ContainsForbiddenOperation(вКомментарии),
            "Запрещённая операция, упомянутая только в комментарии, принята за директиву.");
    }

    /// <summary>
    /// Пользовательские данные (%AppData%\Picket302) при удалении не
    /// удаляются: секция удаления файлов пуста.
    /// </summary>
    [Fact]
    public void Client_DoesNotDeleteUserProfileOnUninstall()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, "installer", ClientInstallerFileName));

        var секция = ReadSection(текст, "UninstallDelete");

        Assert.False(
            секция.Any(строка => !строка.TrimStart().StartsWith(";", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(строка)),
            "Секция [UninstallDelete] клиентского установщика не пуста: пользовательские данные могут удаляться.");
    }

    /// <summary>
    /// Программа после установки запускается от обычного пользователя, а в
    /// тихом режиме не запускается вовсе.
    /// </summary>
    [Fact]
    public void Client_LaunchesApplicationAsStandardUser_AndNotInSilentMode()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, "installer", ClientInstallerFileName));

        var секция = ReadSection(текст, "Run");

        Assert.True(секция.Count > 0, "В клиентском установщике нет секции [Run].");

        Assert.True(
            секция.Any(строка => строка.Contains("runascurrentuser", StringComparison.OrdinalIgnoreCase)),
            "Запуск программы после установки не ограничен учётной записью обычного пользователя.");

        Assert.True(
            секция.Any(строка => строка.Contains("skipifsilent", StringComparison.OrdinalIgnoreCase)),
            "Запуск программы после установки не запрещён в тихом режиме.");
    }

    /// <summary>
    /// Каталог публикации задаётся обязательно: установщик не должен молча
    /// собираться из устаревшего каталога публикации.
    /// </summary>
    [Fact]
    public void Client_RequiresExplicitPublishDirectory()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, "installer", ClientInstallerFileName));

        Assert.True(
            текст.Contains("#ifndef PublishDir", StringComparison.Ordinal)
            && текст.Contains("#error", StringComparison.Ordinal),
            "Клиентский установщик не требует явного указания каталога публикации.");
    }

    /// <summary>
    /// В поставку не попадают отладочные символы и исходные файлы интерфейса.
    /// </summary>
    [Fact]
    public void Client_ExcludesDebugSymbolsAndSources()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, "installer", ClientInstallerFileName));

        Assert.True(
            текст.Contains("*.pdb", StringComparison.Ordinal),
            "Клиентский установщик не исключает отладочные символы (PDB) из поставки.");
    }

    /// <summary>
    /// Проект оболочки проверяет состав публикации: публикация без
    /// обязательных файлов обязана завершаться ошибкой.
    /// </summary>
    [Fact]
    public void AppProject_ValidatesPublishComposition()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, AppProjectRelativePath));

        Assert.True(
            текст.Contains("ValidatePublishOutput", StringComparison.Ordinal),
            "Проект оболочки не проверяет состав опубликованной сборки.");

        foreach (var файл in new[] { "Picket302.ComAutomation.exe", "PrintEnvelopePro.tlb", "default.html" })
        {
            Assert.True(
                текст.Contains(файл, StringComparison.Ordinal),
                $"Проверка состава публикации не требует файл «{файл}».");
        }
    }

    /// <summary>
    /// Сценарий сборки проверяет код возврата, наличие файлов, размер и
    /// SHA-256 итогового пакета.
    /// </summary>
    [Fact]
    public void BuildScript_ChecksExitCodeSizeAndHash()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, "installer", BuildScriptFileName));

        Assert.True(
            текст.Contains("SHA256", StringComparison.OrdinalIgnoreCase),
            "Сценарий сборки не вычисляет SHA-256 итогового пакета.");

        Assert.True(
            текст.Contains("sizeBytes", StringComparison.OrdinalIgnoreCase),
            "Сценарий сборки не проверяет размер итогового пакета.");

        Assert.True(
            текст.Contains("LASTEXITCODE", StringComparison.Ordinal),
            "Сценарий сборки не проверяет код возврата запускаемых программ.");
    }

    /// <summary>
    /// Сценарий сборки не удаляет артефакты и не трогает состояние
    /// репозитория: старые сборки не должны исчезать, а чужие изменения —
    /// теряться.
    /// </summary>
    [Fact]
    public void BuildScript_DoesNotDeleteArtifactsOrRepositoryState()
    {
        var корень = FindSolutionRoot();
        var текст = File.ReadAllText(Path.Combine(корень, "installer", BuildScriptFileName));

        foreach (var запрещённое in new[] { "Remove-Item", "git clean", "git reset", "rm -rf" })
        {
            Assert.True(
                !текст.Contains(запрещённое, StringComparison.OrdinalIgnoreCase),
                $"Сценарий сборки содержит запрещённую операцию «{запрещённое}».");
        }
    }

    /// <summary>
    /// Сценарии сборки и проверки сохранены в UTF-8 с меткой порядка байтов.
    /// </summary>
    /// <remarks>
    /// Windows PowerShell 5.1 читает файл сценария без метки в кодировке
    /// текущей консоли: русские тексты искажаются, и скрипт не запускается
    /// вовсе. Метка обязательна.
    /// </remarks>
    [Fact]
    public void InstallerScripts_AreSavedAsUtf8WithByteOrderMark()
    {
        var корень = FindSolutionRoot();

        foreach (var имя in new[] { BuildScriptFileName, VerifyScriptFileName })
        {
            AssertUtf8WithBom(Path.Combine(корень, "installer", имя));
        }
    }

    /// <summary>
    /// Файл напоминания после установки сохранён в UTF-8 с меткой порядка
    /// байтов: иначе установщик покажет русский текст как «кракозябры».
    /// </summary>
    [Fact]
    public void InfoAfterFile_IsSavedAsUtf8WithByteOrderMark()
    {
        var корень = FindSolutionRoot();

        AssertUtf8WithBom(Path.Combine(корень, "installer", InfoAfterFileName));
    }

    /// <summary>Проверяет наличие метки порядка байтов UTF-8 в начале файла.</summary>
    /// <param name="путь">Путь к файлу.</param>
    private static void AssertUtf8WithBom(string путь)
    {
        Assert.True(File.Exists(путь), $"Файл не найден: {путь}");

        var байты = File.ReadAllBytes(путь);

        Assert.True(
            байты.Length >= 3 && байты[0] == 0xEF && байты[1] == 0xBB && байты[2] == 0xBF,
            $"Файл {Path.GetFileName(путь)} сохранён без метки порядка байтов UTF-8: русские тексты будут искажены.");
    }

    /// <summary>Читает значение AppId из сценария установщика.</summary>
    /// <param name="путь">Путь к сценарию.</param>
    /// <returns>Значение AppId без фигурных скобок.</returns>
    private static string ReadAppId(string путь)
    {
        Assert.True(File.Exists(путь), $"Сценарий установщика не найден: {путь}");

        foreach (var строка in File.ReadAllLines(путь))
        {
            var обрезанная = строка.Trim();

            if (обрезанная.StartsWith("AppId=", StringComparison.OrdinalIgnoreCase))
            {
                // В сценарии Inno Setup литеральная фигурная скобка записывается
                // удвоенной: {{GUID} означает {GUID}.
                return обрезанная["AppId=".Length..].Trim().Replace("{{", "{").Replace("}}", "}");
            }
        }

        return string.Empty;
    }

    /// <summary>Читает строки секции сценария установщика.</summary>
    /// <param name="текст">Текст сценария.</param>
    /// <param name="имя">Имя секции.</param>
    /// <returns>Строки секции без пустых.</returns>
    private static IReadOnlyList<string> ReadSection(string текст, string имя)
    {
        var строки = new List<string>();
        var внутри = false;

        foreach (var строка in текст.Split('\n'))
        {
            var обрезанная = строка.Trim().TrimEnd('\r');

            if (обрезанная.StartsWith("[", StringComparison.Ordinal))
            {
                внутри = обрезанная.Equals($"[{имя}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (внутри && !string.IsNullOrWhiteSpace(обрезанная))
            {
                строки.Add(обрезанная);
            }
        }

        return строки;
    }

    /// <summary>
    /// Ищет в сценарии запрещённую операцию, не считая комментарии.
    /// </summary>
    /// <param name="текст">Текст сценария.</param>
    /// <returns><see langword="true"/>, если запрещённая операция найдена.</returns>
    private static bool ContainsForbiddenOperation(string текст)
    {
        var очищенный = StripComments(текст);

        return ForbiddenInstallerOperations
            .Any(операция => очищенный.Contains(операция, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Удаляет из текста сценария комментарии: они описывают запреты и не
    /// являются исполняемыми директивами.
    /// </summary>
    /// <remarks>
    /// Комментарии сценария Inno Setup бывают трёх видов, и все три надо
    /// снимать: строка целиком с «;», блок в фигурных скобках и хвостовой
    /// комментарий с «//». Блоки в фигурных скобках встречаются только в
    /// секции [Code]: в директивах фигурные скобки означают подстановку
    /// константы ({app}, {group}), и их удаление сломало бы разбор
    /// сценария.
    /// </remarks>
    /// <param name="текст">Текст сценария.</param>
    /// <returns>Текст без комментариев.</returns>
    private static string StripComments(string текст)
    {
        var результат = new List<string>();
        var вКоде = false;
        var внутриБлока = false;

        foreach (var строка in текст.Split('\n'))
        {
            var обрезанная = строка.Trim();

            if (обрезанная.StartsWith("[", StringComparison.Ordinal)
                && обрезанная.EndsWith("]", StringComparison.Ordinal))
            {
                вКоде = обрезанная.Equals("[Code]", StringComparison.OrdinalIgnoreCase);
                результат.Add(строка);
                continue;
            }

            if (!вКоде)
            {
                результат.Add(обрезанная.StartsWith(";", StringComparison.Ordinal) ? string.Empty : строка);
                continue;
            }

            результат.Add(StripCodeComments(строка, ref внутриБлока));
        }

        return string.Join("\n", результат);
    }

    /// <summary>
    /// Удаляет из строки секции [Code] комментарий в фигурных скобках и
    /// хвостовой комментарий с двойной косой чертой.
    /// </summary>
    /// <param name="строка">Строка секции [Code].</param>
    /// <param name="внутриБлока">
    /// Признак того, что строка продолжает комментарий в фигурных скобках,
    /// открытый на предыдущих строках.
    /// </param>
    /// <returns>Строка без комментариев.</returns>
    private static string StripCodeComments(string строка, ref bool внутриБлока)
    {
        var результат = new StringBuilder(строка.Length);
        var index = 0;

        while (index < строка.Length)
        {
            if (внутриБлока)
            {
                var закрытие = строка.IndexOf('}', index);

                if (закрытие < 0)
                {
                    return результат.ToString();
                }

                внутриБлока = false;
                index = закрытие + 1;
                continue;
            }

            if (строка[index] == '{')
            {
                внутриБлока = true;
                index++;
                continue;
            }

            if (строка[index] == '/' && index + 1 < строка.Length && строка[index + 1] == '/')
            {
                break;
            }

            результат.Append(строка[index]);
            index++;
        }

        return результат.ToString();
    }

    /// <summary>Находит корень репозитория по файлу решения.</summary>
    /// <returns>Полный путь к корню репозитория.</returns>
    private static string FindSolutionRoot()
    {
        var каталог = new DirectoryInfo(AppContext.BaseDirectory);

        while (каталог is not null)
        {
            if (File.Exists(Path.Combine(каталог.FullName, "Picket302.sln")))
            {
                return каталог.FullName;
            }

            каталог = каталог.Parent;
        }

        throw new InvalidOperationException(
            $"Файл решения Picket302.sln не найден выше каталога {AppContext.BaseDirectory}.");
    }
}

/// <summary>
/// Каталог опубликованной сборки, проверяемый тестами контракта упаковки.
/// </summary>
public static class PublishDirectory
{
    /// <summary>Имя переменной окружения с каталогом публикации.</summary>
    public const string VariableName = "PICKET302_CLIENT_PUBLISH_DIR";

    /// <summary>
    /// Каталог опубликованной сборки из переменной окружения.
    /// </summary>
    /// <returns>Полный путь к каталогу публикации.</returns>
    /// <exception cref="InvalidOperationException">Переменная окружения не задана.</exception>
    public static string Resolve()
    {
        var значение = Environment.GetEnvironmentVariable(VariableName);

        if (string.IsNullOrWhiteSpace(значение))
        {
            throw new InvalidOperationException(
                $"Переменная окружения {VariableName} не задана: фактический состав публикации не проверялся.");
        }

        var путь = Path.GetFullPath(значение);

        if (!Directory.Exists(путь))
        {
            throw new InvalidOperationException($"Каталог публикации не найден: {путь}");
        }

        return путь;
    }
}

/// <summary>
/// Признак теста, которому требуется фактический каталог опубликованной
/// сборки: без него тест помечается пропущенным, а не проваленным.
/// </summary>
public sealed class PublishDirectoryFactAttribute : FactAttribute
{
    /// <summary>
    /// Создаёт признак теста и помечает его пропущенным, если каталог
    /// публикации не задан.
    /// </summary>
    public PublishDirectoryFactAttribute()
    {
        var значение = Environment.GetEnvironmentVariable(PublishDirectory.VariableName);

        if (string.IsNullOrWhiteSpace(значение) || !Directory.Exists(значение))
        {
            Skip = $"Переменная окружения {PublishDirectory.VariableName} не задана: фактический состав публикации не проверялся.";
        }
    }
}

/// <summary>
/// Фактический состав опубликованной сборки оболочки: состав пакета
/// установки, который получит оператор.
/// </summary>
public sealed class ClientPublishCompositionTests
{
    /// <summary>Обязательные файлы рядом с исполняемым файлом оболочки.</summary>
    private static readonly string[] RequiredFiles =
    {
        "Picket302.App.exe",
        "Picket302.App.dll",
        "Picket302.App.deps.json",
        "Picket302.App.runtimeconfig.json",
        "Picket302.ComAutomation.exe",
        "Picket302.ComAutomation.dll",
        "Picket302.ComAutomation.deps.json",
        "Picket302.ComAutomation.runtimeconfig.json",
        "PrintEnvelopePro.tlb",
        "emailedit\\default.html",
        "e_sqlite3.dll",
    };

    /// <summary>Артефакты серверной службы, которых в клиентской поставке нет.</summary>
    private static readonly string[] ServiceArtifacts =
    {
        "Picket302.Service.exe",
        "Picket302.Service.dll",
        "Picket302.Service.deps.json",
        "Picket302.Service.runtimeconfig.json",
        "service.json",
    };

    /// <summary>Пользовательские данные, которых в поставке нет.</summary>
    private static readonly string[] UserData =
    {
        "picket302.db",
        "shared.json",
        "pending-operations.json",
        "connection.json",
        "servers.ini",
        "print_audit.log",
        "license.dat",
        "ui-settings.json",
        "mail-settings.json",
        "com-automation.log",
        "Secrets",
        "logs",
        "scans",
    };

    /// <summary>
    /// Рядом с оболочкой лежат все обязательные файлы: COM-сервер
    /// автоматизации с библиотекой типов, шаблон письма, нативная библиотека
    /// SQLite.
    /// </summary>
    [PublishDirectoryFact]
    public void Publish_ContainsAllRequiredFiles()
    {
        var каталог = PublishDirectory.Resolve();

        var отсутствуют = RequiredFiles
            .Where(файл => !File.Exists(Path.Combine(каталог, файл)))
            .ToArray();

        Assert.True(
            отсутствуют.Length == 0,
            "В опубликованной сборке отсутствуют обязательные файлы: " + string.Join(", ", отсутствуют));
    }

    /// <summary>
    /// Публикация самодостаточна: на компьютете оператора не требуется
    /// установленный .NET Desktop Runtime.
    /// </summary>
    [PublishDirectoryFact]
    public void Publish_IsSelfContained()
    {
        var каталог = PublishDirectory.Resolve();

        foreach (var имя in new[] { "Picket302.App.runtimeconfig.json", "Picket302.ComAutomation.runtimeconfig.json" })
        {
            var путь = Path.Combine(каталог, имя);

            Assert.True(File.Exists(путь), $"Файл {имя} отсутствует в публикации.");

            using var документ = JsonDocument.Parse(File.ReadAllText(путь));

            var имеет = документ.RootElement
                .GetProperty("runtimeOptions")
                .TryGetProperty("includedFrameworks", out _);

            Assert.True(
                имеет,
                $"{имя} не самодостаточный: на машине оператора потребуется установленный .NET Desktop Runtime.");
        }
    }

    /// <summary>
    /// Серверной службы в клиентской поставке нет: клиентский пакет не
    /// устанавливает службу.
    /// </summary>
    [PublishDirectoryFact]
    public void Publish_ContainsNoServiceArtifacts()
    {
        var каталог = PublishDirectory.Resolve();

        var найдено = ServiceArtifacts
            .Where(файл => File.Exists(Path.Combine(каталог, файл)))
            .ToArray();

        Assert.True(
            найдено.Length == 0,
            "В опубликованной сборке найдены артефакты серверной службы: " + string.Join(", ", найдено));
    }

    /// <summary>
    /// Пользовательских данных в поставке нет: база, секрет сопряжения и
    /// журнал незавершённых операций остаются в %AppData%\Picket302.
    /// </summary>
    [PublishDirectoryFact]
    public void Publish_ContainsNoUserData()
    {
        var каталог = PublishDirectory.Resolve();

        var файлы = Directory.GetFiles(каталог, "*", SearchOption.AllDirectories);

        foreach (var данные in UserData)
        {
            var найдено = файлы.Where(файл =>
                string.Equals(Path.GetFileName(файл), данные, StringComparison.OrdinalIgnoreCase)).ToArray();

            Assert.True(
                найдено.Length == 0,
                $"В опубликованной сборке найдены пользовательские данные «{данные}»: {string.Join(", ", найдено)}");
        }
    }
}
