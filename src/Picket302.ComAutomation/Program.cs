using System.Text;

namespace Picket302.ComAutomation;

/// <summary>
/// Точка входа COM-сервера автоматизации (ТЗ §9.1, функция 113; §9.3,
/// функция 114).
/// </summary>
/// <remarks>
/// <para>
/// Режимы запуска:
/// </para>
/// <list type="bullet">
/// <item><c>--register</c> — регистрация COM-сервера в <c>HKCU\Software\Classes</c>;
/// права администратора не требуются;</item>
/// <item><c>--unregister</c> — удаление регистрации;</item>
/// <item>без аргументов (или с <c>-Embedding</c>, с которым сервер запускает
/// диспетчер COM) — работа локального COM-сервера: регистрация фабрики
/// классов и цикл сообщений.</item>
/// </list>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Запускает COM-сервер автоматизации либо выполняет регистрацию.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <returns>Код завершения процесса.</returns>
    [STAThread]
    private static int Main(string[] args)
    {
        var аргументы = args ?? Array.Empty<string>();

        if (Содержит(аргументы, "--register", "-register", "/register", "-RegServer"))
        {
            var регистрация = new ComRegistration();
            регистрация.Register();

            return 0;
        }

        if (Содержит(аргументы, "--unregister", "-unregister", "/unregister", "-UnRegServer"))
        {
            var регистрация = new ComRegistration();
            регистрация.Unregister();

            return 0;
        }

        try
        {
            var хост = new ComServerHost();

            return хост.Run();
        }
        catch (Exception error)
        {
            ЗаписатьВЖурнал("Сервер автоматизации завершился с ошибкой: " + error);

            return 1;
        }
    }

    private static bool Содержит(IReadOnlyList<string> аргументы, params string[] варианты)
    {
        foreach (var аргумент in аргументы)
        {
            foreach (var вариант in варианты)
            {
                if (string.Equals(аргумент?.Trim(), вариант, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ЗаписатьВЖурнал(string сообщение) => AutomationServerLog.Write(сообщение);
}
