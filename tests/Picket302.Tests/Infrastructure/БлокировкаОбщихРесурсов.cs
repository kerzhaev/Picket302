using System.Threading;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Межпроцессная блокировка общих ресурсов тестовой и acceptance-
/// инфраструктуры Picket302: реальный профиль
/// <c>%AppData%\Picket302</c>, sentinel-файл, шаблон
/// <c>%TEMP%\P302GuardTest_*</c>, acceptance-каталоги и общие
/// evidence-файлы.
/// </summary>
/// <remarks>
/// <para>
/// xUnit-коллекции сериализуют тесты только внутри одного процесса
/// testhost; два независимых прогона (Debug и Release, два агента CI,
/// ручной прогон и acceptance-драйвер) выполняются параллельно. Общий
/// named mutex закрывает это. Имя в пространстве <c>Local\</c> — общее
/// для всех процессов сессии пользователя (Global\ требует привилегий).
/// Тот же идентификатор используется в PowerShell-драйверах
/// <c>tools/ui-acceptance</c> (функция <c>Enter-SharedTestLock</c> в
/// <c>acceptance-lib.ps1</c>).
/// </para>
/// <para>
/// Windows-мьютекс привязан к потоку: <c>ReleaseMutex</c> допустим
/// только из потока, выполнившего <c>WaitOne</c>. Асинхронные тесты
/// после <c>await</c> продолжаются в другом потоке пула, поэтому
/// освобождение «в лоб» из <c>Dispose</c> кидает
/// <see cref="System.Threading.SynchronizationLockException"/> и
/// оставляет мьютекс навсегда за исходным потоком пула. Мьютексом
/// владеет выделенный фоновый поток: он же делает <c>WaitOne</c> и
/// <c>ReleaseMutex</c>, а вызывающий код лишь сигнализирует
/// освобождение. Аварийное завершение процесса освобождает мьютекс
/// штатным механизмом ОС; мёртвый владелец без освобождения даёт
/// <see cref="AbandonedMutexException"/>, который обрабатывается с
/// диагностикой (мьютекс при этом получен).
/// </para>
/// <para>
/// Таймаут ограничен: ждать бесконечно и продолжать после отказа
/// получения запрещено (fail-closed) — отказ даёт исключение, а не
/// тихое продолжение без блокировки.
/// </para>
/// </remarks>
internal static class БлокировкаОбщихРесурсов
{
    /// <summary>
    /// Имя именованного мьютекса; одинаково для C#-инфраструктуры и
    /// PowerShell-драйверов acceptance.
    /// </summary>
    public const string ИмяМьютекса = @"Local\Picket302.TestInfrastructure.SharedProfile";

    /// <summary>
    /// Таймаут по умолчанию: достаточен для полного reparse-теста и
    /// acceptance-сценария другого процесса, но не бесконечен.
    /// </summary>
    public static readonly TimeSpan ТаймаутПоУмолчанию = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Захватывает межпроцессную блокировку общих ресурсов. Отказ
    /// (исчерпан таймаут) — исключение: продолжение без блокировки
    /// запрещено.
    /// </summary>
    /// <param name="таймаут">Ограниченное время ожидания получения.</param>
    /// <returns>
    /// Владение блокировкой — освободить через <see cref="IDisposable.Dispose"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Таймаут не положительный.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Блокировка не получена за таймаут — общий ресурс занят другим
    /// процессом (параллельный testhost, acceptance-прогон, зависший
    /// процесс).
    /// </exception>
    public static ВладениеБлокировкой Захватить(TimeSpan? таймаут = null)
    {
        var бюджет = таймаут ?? ТаймаутПоУмолчанию;

        if (бюджет <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(таймаут), "Таймаут получения блокировки обязан быть положительным.");
        }

        var мьютекс = new Mutex(initiallyOwned: false, ИмяМьютекса, out _);
        var готово = new ManualResetEventSlim(false);
        var освободить = new ManualResetEventSlim(false);
        var отменён = new ManualResetEventSlim(false);
        var получено = false;
        var диагностика = string.Empty;

        // Выделенный поток-владелец: WaitOne и ReleaseMutex на одном
        // потоке; вызывающий поток (в том числе продолжение async-теста)
        // только сигнализирует освобождение.
        var потокВладелец = new Thread(() =>
        {
            try
            {
                try
                {
                    получено = мьютекс.WaitOne(бюджет);
                }
                catch (AbandonedMutexException ошибка)
                {
                    // Предыдущий владелец умер, не освободив: ОС отдала
                    // мьютекс этому потоку. Ресурс мог остаться в
                    // неопределённом состоянии — диагностика обязательна.
                    диагностика = "предыдущий владелец мьютекса умер, не освободив его "
                        + "(AbandonedMutexException): " + ошибка.Message;
                    получено = true;
                }

                if (!получено || отменён.IsSet)
                {
                    if (получено)
                    {
                        мьютекс.ReleaseMutex();
                    }

                    получено = false;
                    готово.Set();

                    return;
                }

                готово.Set();

                // Удержание до сигнала освобождения: поток фоновый, при
                // аварийном завершении процесса ОС освобождает мьютекс.
                освободить.Wait();

                мьютекс.ReleaseMutex();
            }
            catch (Exception ошибка)
            {
                диагностика = "ошибка потока-владельца блокировки: " + ошибка.Message;
                получено = false;
                готово.Set();
            }
            finally
            {
                мьютекс.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "P302-TestLock-Owner",
        };

        потокВладелец.Start();

        // Внешнее ожидание с запасом: внутренний WaitOne использует тот же
        // бюджет, запас покрывает планирование потоков.
        if (!готово.Wait(бюджет + TimeSpan.FromSeconds(5)) || !получено)
        {
            отменён.Set();
            готово.Wait(TimeSpan.FromSeconds(5));

            throw new InvalidOperationException(
                "БлокировкаОбщихРесурсов: блокировка «" + ИмяМьютекса
                + "» не получена за " + (int)бюджет.TotalSeconds
                + " с — общий ресурс занят другим процессом (параллельный "
                + "testhost, acceptance-прогон или зависший процесс). "
                + "Продолжение без блокировки запрещено (fail-closed)."
                + (диагностика.Length > 0 ? " Диагностика: " + диагностика : string.Empty));
        }

        if (диагностика.Length > 0)
        {
            Console.Error.WriteLine("БлокировкаОбщихРесурсов: " + диагностика);
        }

        return new ВладениеБлокировкой(освободить, отменён, потокВладелец);
    }

    /// <summary>
    /// Владение межпроцессной блокировкой. Освобождение допустимо из
    /// любого потока: сигнал уходит выделенному потоку-владельцу, который
    /// выполняет <c>ReleaseMutex</c>.
    /// </summary>
    public sealed class ВладениеБлокировкой : IDisposable
    {
        private readonly ManualResetEventSlim _освободить;
        private readonly ManualResetEventSlim _отменён;
        private readonly Thread _потокВладелец;
        private bool _освобождено;

        internal ВладениеБлокировкой(
            ManualResetEventSlim освободить,
            ManualResetEventSlim отменён,
            Thread потокВладелец)
        {
            _освободить = освободить;
            _отменён = отменён;
            _потокВладелец = потокВладелец;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_освобождено)
            {
                return;
            }

            _освобождено = true;
            _освободить.Set();

            if (!_потокВладелец.Join(TimeSpan.FromSeconds(10)))
            {
                Console.Error.WriteLine(
                    "БлокировкаОбщихРесурсов: поток-владелец не завершился за 10 с "
                    + "после сигнала освобождения — ReleaseMutex выполнит поток-владелец, "
                    + "либо мьютекс освободит ОС при завершении процесса.");
            }

            _освободить.Dispose();
            _отменён.Dispose();
        }
    }
}
