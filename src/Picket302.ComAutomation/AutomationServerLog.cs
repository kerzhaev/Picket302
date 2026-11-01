using System.Text;

namespace Picket302.ComAutomation;

/// <summary>
/// Журнал работы локального COM-сервера автоматизации.
/// </summary>
/// <remarks>
/// Журнал используется для диагностики запуска сервера и ошибок
/// автоматизации: файл создаётся в каталоге профиля приложения, запись
/// ошибки не должна прерывать работу сервера (ТЗ §10.3).
/// </remarks>
internal static class AutomationServerLog
{
    /// <summary>Имя файла журнала запуска сервера.</summary>
    public const string FileName = "com-automation.log";

    /// <summary>
    /// Записывает сообщение в журнал сервера.
    /// </summary>
    /// <param name="сообщение">Текст сообщения.</param>
    public static void Write(string сообщение)
    {
        foreach (var каталог in Каталоги())
        {
            ЗаписатьВКаталог(каталог, сообщение);
        }
    }

    /// <summary>
    /// Возвращает каталоги журнала: профиль приложения и временный каталог.
    /// </summary>
    /// <returns>Список каталогов журнала.</returns>
    private static IEnumerable<string> Каталоги()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ComAutomationEnvironment.ApplicationDirectoryName);

        yield return Path.Combine(Path.GetTempPath(), ComAutomationEnvironment.ApplicationDirectoryName);
    }

    private static void ЗаписатьВКаталог(string каталог, string сообщение)
    {
        try
        {
            Directory.CreateDirectory(каталог);

            File.AppendAllText(
                Path.Combine(каталог, FileName),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {сообщение}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // Журнал — вспомогательный канал: сбой записи не должен влиять
            // на работу сервера.
        }
    }
}
