using System.Globalization;
using Picket302.Service.Configuration;
using Picket302.Service.Hosting;

namespace Picket302.Service;

/// <summary>
/// Точка входа серверной службы совместного режима.
/// </summary>
/// <remarks>
/// <para>
/// Служба запускается либо диспетчером служб Windows (тогда она работает в
/// фоне без окна и переживает выход сотрудника из учётной записи), либо как
/// обычное консольное приложение — второй режим нужен для проверок и
/// запуска вручную.
/// </para>
/// <para>
/// Аргументы командной строки:
/// <list type="bullet">
/// <item><description><c>--data &lt;каталог&gt;</c> — каталог данных службы;</description></item>
/// <item><description><c>--port &lt;номер&gt;</c> — порт TCP;</description></item>
/// <item><description><c>--console</c> — запуск в консольном режиме без SCM.</description></item>
/// </list>
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Запускает службу.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <returns>Задача завершения работы службы.</returns>
    private static async Task<int> Main(string[] args)
    {
        var параметры = ПрочитатьПараметры(args);

        Directory.CreateDirectory(параметры.DataDirectory);
        Directory.CreateDirectory(параметры.LogsDirectory);

        var журнал = СоздатьЖурнал(параметры.LogsDirectory);

        var сертификат = SharedServiceFactory.НайтиСертификат(параметры.CertificateThumbprint);

        if (сертификат is null && !string.IsNullOrWhiteSpace(параметры.CertificateThumbprint))
        {
            журнал($"Сертификат с отпечатком {параметры.CertificateThumbprint} не найден.");
        }

        var база = new Data.ServerDatabase(параметры.DatabasePath);

        try
        {
            await база.ПодготовитьAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Сбой запуска службы фиксируется в журнале и приводит к понятному коду возврата.
        catch (Exception ошибка)
        {
            журнал($"Не удалось подготовить базу данных службы: {ошибка}");

            return 2;
        }
#pragma warning restore CA1031

        журнал(
            $"Служба совместного режима запущена: порт {параметры.Port}, "
            + $"каталог данных {параметры.DataDirectory}.");

        var отмена = new CancellationTokenSource();

        var подSCM = WindowsServiceRunner.ЗапуститьКакСлужбу(
            WindowsServiceRunner.ИмяСлужбы,
            токен => ЗапуститьАсинхронно(параметры, сертификат, журнал, токен).GetAwaiter().GetResult());

        if (!подSCM)
        {
            Console.WriteLine(
                $"Служба Picket302 (совместный режим): порт {параметры.Port}, "
                + $"данные {параметры.DataDirectory}.");

            Console.WriteLine("Для остановки нажмите Ctrl+C.");

            using var регистрация = System.Runtime.InteropServices.PosixSignalRegistration
                .Create(System.Runtime.InteropServices.PosixSignal.SIGINT, _ => отмена.Cancel());

            await ЗапуститьАсинхронно(параметры, сертификат, журнал, отмена.Token)
                .ConfigureAwait(false);
        }

        журнал("Служба совместного режима остановлена.");

        return 0;
    }

    /// <summary>
    /// Запускает веб-приложение службы.
    /// </summary>
    /// <param name="параметры">Настройки службы.</param>
    /// <param name="сертификат">Сертификат сервера либо <see langword="null"/>.</param>
    /// <param name="журнал">Приёмник записей журнала.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Задача завершения работы.</returns>
    private static async Task ЗапуститьАсинхронно(
        ServiceOptions параметры,
        System.Security.Cryptography.X509Certificates.X509Certificate2? сертификат,
        Action<string> журнал,
        CancellationToken токен)
    {
        var приложение = SharedServiceFactory.Создать(параметры, журнал, сертификат);

        await приложение.RunAsync(токен).ConfigureAwait(false);
    }

    /// <summary>
    /// Читает настройки из файла и из аргументов командной строки.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <returns>Настройки службы.</returns>
    private static ServiceOptions ПрочитатьПараметры(string[] args)
    {
        var каталог = ServiceOptions.КаталогДанныхПоУмолчанию();
        var порт = ServiceOptions.DefaultPort;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data" when i + 1 < args.Length:
                    каталог = args[++i];
                    break;

                case "--port" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var разобранный)
                        && разобранный is > 0 and <= 65535)
                    {
                        порт = разобранный;
                    }

                    break;

                default:
                    break;
            }
        }

        var путьНастроек = Path.Combine(каталог, ServiceOptions.SettingsFileName);
        var параметры = ServiceOptions.Прочитать(путьНастроек);

        параметры.DataDirectory = каталог;
        параметры.Port = порт;

        return параметры;
    }

    /// <summary>
    /// Создаёт приёмник записей журнала службы.
    /// </summary>
    /// <param name="каталог">Каталог журналов.</param>
    /// <returns>Приёмник записей журнала.</returns>
    private static Action<string> СоздатьЖурнал(string каталог)
    {
        var путь = Path.Combine(каталог, "service.log");

        return сообщение =>
        {
            try
            {
                Directory.CreateDirectory(каталог);

                File.AppendAllText(
                    путь,
                    $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {сообщение}{Environment.NewLine}",
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (IOException)
            {
                // Журнал недоступен: работа службы не прерывается.
            }
            catch (UnauthorizedAccessException)
            {
                // Журнал недоступен: работа службы не прерывается.
            }
        };
    }
}
