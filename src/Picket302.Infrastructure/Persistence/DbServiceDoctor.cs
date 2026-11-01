using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text;

namespace Picket302.Infrastructure.Persistence;

/// <summary>
/// Сведения о зарегистрированной службе СУБД (ТЗ §8.11, функция 136).
/// </summary>
/// <param name="ServiceName">Системное имя службы.</param>
/// <param name="DisplayName">Отображаемое имя службы.</param>
/// <param name="ImagePath">Путь к исполняемому файлу службы.</param>
/// <param name="State">Состояние службы.</param>
/// <param name="IsOrphaned">
/// Признак осиротевшей службы: исполняемый файл службы отсутствует на диске.
/// </param>
public sealed record ServiceInfo(
    string ServiceName,
    string DisplayName,
    string ImagePath,
    string State,
    bool IsOrphaned);

/// <summary>
/// Отчёт диагностики службы СУБД (ТЗ §8.11, функция 136:
/// <c>public DiagnosisReport DiagnoseDbService()</c>).
/// </summary>
/// <param name="IsHealthy">Признак отсутствия проблем со службой.</param>
/// <param name="OrphanedServices">Перечень осиротевших служб.</param>
/// <param name="Message">Пояснение для оператора.</param>
/// <param name="RequiresAdministrator">
/// Признак того, что удаление службы требует прав администратора.
/// </param>
public sealed record DiagnosisReport(
    bool IsHealthy,
    IReadOnlyList<ServiceInfo> OrphanedServices,
    string Message,
    bool RequiresAdministrator)
{
    /// <summary>Признак наличия осиротевших служб.</summary>
    public bool HasOrphanedServices => OrphanedServices.Count > 0;
}

/// <summary>
/// Диагностика типовой причины отказа запуска сервера БД — остатков
/// предыдущей установки (ТЗ §8.11, функция 136).
/// </summary>
/// <remarks>
/// <para>
/// Диагностика читает перечень служб Windows средствами <c>sc.exe</c> и ищет
/// службы, исполняемый файл которых относится к прежней установке (Picket302
/// или PostgreSQL) и отсутствует на диске. Такая служба остаётся в
/// диспетчере после удаления программы и блокирует запуск новой.
/// </para>
/// <para>
/// Удаление выполняется штатной командой <c>sc delete</c> и требует прав
/// администратора; при их отсутствии выдаётся пояснение с предложением
/// повысить права.
/// </para>
/// </remarks>
public sealed class DbServiceDoctor
{
    /// <summary>
    /// Признаки службы СУБД в имени или в пути к исполняемому файлу: такие
    /// службы считаются относящимися к базе данных программы.
    /// </summary>
    private static readonly string[] ПризнакиСубд =
    [
        "picket302",
        "postgres",
        "postgre",
        "pgsql",
    ];

    private readonly Func<string, string> _запускКоманды;

    /// <summary>
    /// Создаёт диагност с запуском команды через <c>sc.exe</c>.
    /// </summary>
    public DbServiceDoctor()
        : this(null)
    {
    }

    /// <summary>
    /// Создаёт диагност с подменённым исполнителем команды (для проверок).
    /// </summary>
    /// <param name="запускКоманды">
    /// Функция запуска команды: принимает аргументы команды и возвращает её
    /// вывод. При <see langword="null"/> используется <c>sc.exe</c>.
    /// </param>
    public DbServiceDoctor(Func<string, string>? запускКоманды)
    {
        _запускКоманды = запускКоманды ?? ЗапуститьКоманду;
    }

    /// <summary>
    /// Признак того, что процесс запущен с правами администратора.
    /// </summary>
    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var идентичность = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(идентичность);

                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Диагностирует службу СУБД (ТЗ §8.11, функция 136:
    /// <c>public DiagnosisReport DiagnoseDbService()</c>).
    /// </summary>
    /// <returns>Отчёт диагностики.</returns>
    public DiagnosisReport DiagnoseDbService()
    {
        var службы = ListServices();
        var осиротевшие = службы.Where(служба => служба.IsOrphaned).ToList();

        if (осиротевшие.Count == 0)
        {
            return new DiagnosisReport(
                true,
                Array.Empty<ServiceInfo>(),
                "Осиротевших служб СУБД не обнаружено: запуск сервера базы данных не блокируется остатками прежней установки.",
                false);
        }

        var имена = string.Join(", ", осиротевшие.Select(служба => служба.ServiceName));
        var требуетсяАдминистратор = !IsAdministrator;

        var сообщение = требуетсяАдминистратор
            ? $"Обнаружены осиротевшие службы СУБД: {имена}. "
                + "Их удаление командой sc delete требует прав администратора — запустите программу с повышенными правами."
            : $"Обнаружены осиротевшие службы СУБД: {имена}. "
                + "Их остатки от прежней установки блокируют запуск сервера базы данных; удалите их командой sc delete.";

        return new DiagnosisReport(
            false,
            осиротевшие,
            сообщение,
            требуетсяАдминистратор);
    }

    /// <summary>
    /// Возвращает перечень служб, относящихся к базе данных программы.
    /// </summary>
    /// <returns>Перечень служб.</returns>
    public IReadOnlyList<ServiceInfo> ListServices()
    {
        var вывод = _запускКоманды("query type= service state= all");
        var службы = new List<ServiceInfo>();

        string? имя = null;
        string? отображаемое = null;
        string? путь = null;
        string? состояние = null;

        foreach (var сыраяСтрока in вывод.Split('\n'))
        {
            var строка = сыраяСтрока.Trim();

            if (строка.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                if (имя is not null)
                {
                    службы.Add(СобратьСлужбу(имя, отображаемое, путь, состояние));
                }

                имя = строка["SERVICE_NAME:".Length..].Trim();
                отображаемое = null;
                путь = null;
                состояние = null;
                continue;
            }

            if (строка.StartsWith("DISPLAY_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                отображаемое = строка["DISPLAY_NAME:".Length..].Trim();
                continue;
            }

            if (строка.StartsWith("BINARY_PATH_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                путь = строка["BINARY_PATH_NAME:".Length..].Trim();
                continue;
            }

            if (строка.StartsWith("STATE:", StringComparison.OrdinalIgnoreCase))
            {
                состояние = строка["STATE:".Length..].Trim();
                continue;
            }
        }

        if (имя is not null)
        {
            службы.Add(СобратьСлужбу(имя, отображаемое, путь, состояние));
        }

        return службы;
    }

    /// <summary>
    /// Удаляет службу командой <c>sc delete</c> (ТЗ §8.11, функция 136).
    /// </summary>
    /// <param name="serviceName">Системное имя удаляемой службы.</param>
    /// <returns>Результат удаления: успех либо текст ошибки.</returns>
    public ServiceRemovalResult DeleteService(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        if (!IsAdministrator)
        {
            return ServiceRemovalResult.Failure(
                serviceName,
                $"Удаление службы «{serviceName}» требует прав администратора. "
                + "Запустите программу от имени пользователя с административными правами.");
        }

        var вывод = _запускКоманды($"delete {serviceName}");

        if (вывод.Contains("DeleteSERVICE", StringComparison.OrdinalIgnoreCase)
            || вывод.Contains("УДАЛЕНИЕ СЛУЖБЫ", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceRemovalResult.Success(serviceName);
        }

        if (вывод.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
            || вывод.Contains("ОТКАЗАНО", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceRemovalResult.Failure(
                serviceName,
                $"Не удалось удалить службу «{serviceName}»: {вывод.Trim()}");
        }

        return ServiceRemovalResult.Success(serviceName);
    }

    /// <summary>
    /// Собирает сведения о службе и определяет признак осиротевшей.
    /// </summary>
    private static ServiceInfo СобратьСлужбу(
        string имя,
        string? отображаемое,
        string? путь,
        string? состояние)
    {
        var исполняемый = ИзвлечьПуть(путь);
        var относится = ПризнакиСубд.Any(признак =>
            имя.Contains(признак, StringComparison.OrdinalIgnoreCase)
            || (исполняемый?.Contains(признак, StringComparison.OrdinalIgnoreCase) ?? false));

        var осиротевшая = относится
            && !string.IsNullOrEmpty(исполняемый)
            && !ФайлСуществует(исполняемый);

        return new ServiceInfo(
            имя,
            string.IsNullOrWhiteSpace(отображаемое) ? имя : отображаемое,
            путь ?? string.Empty,
            состояние ?? string.Empty,
            осиротевшая);
    }

    /// <summary>
    /// Извлекает путь к исполняемому файлу из строки BINARY_PATH_NAME.
    /// </summary>
    private static string? ИзвлечьПуть(string? путь)
    {
        if (string.IsNullOrWhiteSpace(путь))
        {
            return null;
        }

        var обрезанный = путь.Trim().Trim('"');

        // Путь может быть заключён в кавычки целиком: "C:\путь\файл.exe" -k
        if (обрезанный.StartsWith('"'))
        {
            var закрытие = обрезанный.IndexOf('"', 1);

            return закрытие > 0 ? обрезанный[1..закрытие] : обрезанный.Trim('"');
        }

        var пробел = обрезанный.IndexOf(' ');

        return пробел > 0 ? обрезанный[..пробел] : обрезанный;
    }

    /// <summary>
    /// Проверяет существование файла исполняемого файла службы.
    /// </summary>
    private static bool ФайлСуществует(string путь)
    {
        try
        {
            return File.Exists(путь);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Запускает команду и возвращает её вывод (stdout и stderr).
    /// </summary>
    private static string ЗапуститьКоманду(string аргументы)
    {
        var информация = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = аргументы,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.Default,
            StandardErrorEncoding = Encoding.Default,
        };

        try
        {
            using var процесс = Process.Start(информация);

            if (процесс is null)
            {
                return string.Empty;
            }

            var вывод = процесс.StandardOutput.ReadToEnd();
            var ошибка = процесс.StandardError.ReadToEnd();

            процесс.WaitForExit(10_000);

            return string.Concat(вывод, Environment.NewLine, ошибка);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}

/// <summary>
/// Результат удаления службы СУБД (ТЗ §8.11, функция 136).
/// </summary>
/// <param name="IsSuccess">Признак успешного удаления.</param>
/// <param name="ServiceName">Имя удалённой службы.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
public sealed record ServiceRemovalResult(bool IsSuccess, string ServiceName, string? Error)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Создаёт успешный результат.</summary>
    /// <param name="serviceName">Имя удалённой службы.</param>
    /// <returns>Успешный результат.</returns>
    public static ServiceRemovalResult Success(string serviceName) => new(true, serviceName, null);

    /// <summary>Создаёт неуспешный результат.</summary>
    /// <param name="serviceName">Имя службы.</param>
    /// <param name="error">Текст ошибки.</param>
    /// <returns>Неуспешный результат.</returns>
    public static ServiceRemovalResult Failure(string serviceName, string error)
        => new(false, serviceName, error);
}
