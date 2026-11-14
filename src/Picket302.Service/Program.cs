using System.Globalization;
using Picket302.Service.Configuration;
using Picket302.Service.Data;
using Picket302.Service.Hosting;
using Picket302.Service.Security;

namespace Picket302.Service;

/// <summary>
/// Точка входа серверной службы совместного режима.
/// </summary>
/// <remarks>
/// <para>
/// Служба запускается либо диспетчером служб Windows (тогда она работает в
/// фоне без окна и переживает выход сотрудника из учётной записи), либо как
/// обычное консольное приложение — второй режим нужен для проверок и запуска
/// вручную.
/// </para>
/// <para>
/// <b>Служба работает только по защищённому каналу.</b> Без сертификата
/// сервера служба не запускается: секрет устройства не должен передаваться
/// по открытой сети.
/// </para>
/// <para>
/// Аргументы командной строки:
/// <list type="bullet">
/// <item><description><c>--data &lt;каталог&gt;</c> — каталог данных службы;</description></item>
/// <item><description><c>--port &lt;номер&gt;</c> — порт TCP;</description></item>
/// </list>
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Запускает службу.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <returns>Код завершения.</returns>
    private static async Task<int> Main(string[] args)
    {
        var параметры = ПрочитатьПараметры(args);

        Directory.CreateDirectory(параметры.DataDirectory);
        Directory.CreateDirectory(параметры.LogsDirectory);

        var журнал = СоздатьЖурнал(параметры.LogsDirectory);

        var сертификат = SharedServiceFactory.НайтиСертификат(параметры.CertificateThumbprint);

        if (сертификат is null)
        {
            журнал(
                "Служба не запущена: не задан сертификат сервера. Секрет устройства "
                + "не должен передаваться по открытому каналу.");

            Console.Error.WriteLine(
                "Служба Picket302 не может работать без сертификата сервера. "
                + "Запустите установку серверной части.");

            return 2;
        }

        var база = new ServerDatabase(параметры.DatabasePath);

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

        SharedDataDirectoryProtection.Применить(параметры.DataDirectory, журнал);

        // Журнал идемпотентности и просроченные коды сопряжения чистятся при
        // старте: иначе они растут без ограничения.
        await ОчиститьЖурналы(база, журнал).ConfigureAwait(false);

        журнал(
            $"Служба совместного режима запущена: порт {параметры.Port}, "
            + $"каталог данных {параметры.DataDirectory}.");

        // Ежедневные копии общей базы: механизм тот же, что и в одиночном
        // режиме (VACUUM INTO), копии — в каталоге backups данных службы.
        var копии = new Hosting.ServerBackupScheduler(
            параметры.DatabasePath,
            параметры.BackupsDirectory,
            журнал,
            сразу: true);

        var отмена = new CancellationTokenSource();
        var приложение = SharedServiceFactory.Создать(параметры, журнал, сертификат);

        var подSCM = WindowsServiceRunner.ЗапуститьКакСлужбы(
            WindowsServiceRunner.ИмяСлужбы,
            async токен =>
            {
                await приложение.StartAsync(токен).ConfigureAwait(false);

                токен.Register(() => _ = приложение.StopAsync());
            },
            () => приложение.WaitForShutdownAsync());

        if (!подSCM)
        {
            Console.WriteLine(
                $"Служба Picket302 (совместный режим): порт {параметры.Port}, "
                + $"данные {параметры.DataDirectory}.");

            Console.WriteLine("Для остановки нажмите Ctrl+C.");

            using var регистрация = System.Runtime.InteropServices.PosixSignalRegistration
                .Create(System.Runtime.InteropServices.PosixSignal.SIGINT, _ => отмена.Cancel());

            await приложение.StartAsync(отмена.Token).ConfigureAwait(false);

            отмена.Token.Register(() => _ = приложение.StopAsync());

            await приложение.WaitForShutdownAsync().ConfigureAwait(false);
        }

        await приложение.DisposeAsync().ConfigureAwait(false);
        копии.Dispose();

        журнал("Служба совместного режима остановлена.");

        return 0;
    }

    /// <summary>
    /// Удаляет завершённые операции старше месяца и коды сопряжения, срок
    /// которых истёк либо которые были использованы более часа назад.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    /// <param name="журнал">Приёмник записей журнала службы.</param>
    /// <returns>Задача завершения.</returns>
    private static async Task ОчиститьЖурналы(ServerDatabase база, Action<string> журнал)
    {
        try
        {
            var сессия = база.СоздатьСессию();

            await using var освобождение = сессия.ConfigureAwait(false);

            var удалено = await new Picket302.Service.Data.SharedOperationStore(сессия)
                .УдалитьУстаревшиеAsync(TimeSpan.FromDays(30))
                .ConfigureAwait(false);

            if (удалено > 0)
            {
                журнал($"Из журнала идемпотентности удалено завершённых записей: {удалено}.");
            }

            var удаленоКодов = await new Picket302.Service.Security.SharedPairingStore(сессия)
                .УдалитьПросроченныеAsync()
                .ConfigureAwait(false);

            if (удаленоКодов > 0)
            {
                журнал($"Просроченных кодов сопряжения удалено: {удаленоКодов}.");
            }
        }
#pragma warning disable CA1031 // Неудачная очистка журнала не должна останавливать службу.
        catch (Exception ошибка)
        {
            журнал($"Не удалось очистить журнал идемпотентности: {ошибка}");
        }
#pragma warning restore CA1031
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
