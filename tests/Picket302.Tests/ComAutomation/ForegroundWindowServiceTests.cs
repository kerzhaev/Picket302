using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Picket302.ComAutomation;
using Xunit;

namespace Picket302.Tests.ComAutomation;

/// <summary>
/// Регрессия на Win32Exception 1411 «Class does not exist»: вспомогательное
/// окно службы переднего плана обязано реально создаваться (Win32-класс
/// регистрируется и окно строится напрямую, а не через пользовательский
/// CreateParams.ClassName в WinForms, который падал).
/// </summary>
/// <remarks>
/// Тесты работают с глобальным COM-состоянием (ключи HKCU\Software\Classes,
/// mutex и ShutdownEventName сервера, файл журнала профиля), поэтому
/// выполняются последовательно с COM-приёмкой в коллекции
/// «ПрофильПриложения».
/// </remarks>
[Collection("ПрофильПриложения")]
public sealed class ForegroundWindowServiceTests
{
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string message);

    /// <summary>Путь к исполняемому файлу COM-сервера рядом с тестами.</summary>
    private static string ПутьСервера => Path.Combine(
        AppContext.BaseDirectory,
        ComRegistration.ServerFileName);

    [Fact]
    public void Служба_Создаёт_РеальноеСкрытоеОкно_И_ОсвобождаетЕго()
    {
        IntPtr дескриптор;

        using (var служба = new ForegroundWindowService())
        {
            // Контролируемый отказ: ошибка инициализации должна отсутствовать
            // (прежняя реализация падала Win32Exception 1411 и молчала).
            Assert.Null(служба.ОшибкаИнициализации);

            дескриптор = служба.HelperWindowHandle;

            Assert.NotEqual(IntPtr.Zero, дескриптор);
            Assert.True(IsWindow(дескриптор), "HWND вспомогательного окна не существует.");

            // Скрытое окно: не видно на экране при обычном старте.
            Assert.False(
                IsWindowVisible(дескриптор),
                "Вспомогательное окно должно быть невидимым при обычной COM-активации.");

            // Регистрируемые сообщения доставляются в окно без ошибок.
            var активация = RegisterWindowMessage(ForegroundWindowService.ActivationMessageName);

            Assert.True(
                PostMessage(дескриптор, активация, IntPtr.Zero, IntPtr.Zero),
                "PostMessage зарегистрированного сообщения активации не доставлен.");

            служба.Dispose();
        }

        // HWND уничтожен в собственном потоке службы — утечки окна нет.
        Assert.False(IsWindow(дескриптор), "HWND не уничтожен после Dispose.");
    }

    [Fact]
    public void МеханизмПоискаРаботающегоЭкземпляра_НаходитОкноКлассаПоПолитикеКода()
    {
        // Класс окна службы и механизм его поиска согласованы: окно класса
        // реально существует после запуска службы (раньше FindWindow по этому
        // классу всегда возвращал ноль — класс не регистрировался).
        using var служба = new ForegroundWindowService();

        Assert.Null(служба.ОшибкаИнициализации);

        var найдено = FindWindow(ForegroundWindowService.HelperWindowClassName, null);

        Assert.NotEqual(IntPtr.Zero, найдено);
        Assert.True(
            IsWindow(найдено),
            $"FindWindow по классу «{ForegroundWindowService.HelperWindowClassName}» " +
            "не находит окно, которое служба создала.");

        служба.Dispose();
    }

    /// <summary>
    /// Регрессия порядка запуска: второй процесс обязан найти работающий
    /// экземпляр, доставить сообщение активации и завершиться, не регистрируя
    /// вторую фабрику; первый процесс останавливается штатно через
    /// <c>ShutdownEventName</c> и выполняет <c>CoRevokeClassObject</c> —
    /// жёсткий Kill оставляет в SCM запись о «мёртвом» сервере и ломает
    /// следующую холодную активацию (0x80040154).
    /// </summary>
    [Fact]
    public void ПовторныйЗапуск_ВторойПроцесс_ЗавершаетШтатноБезВторойФабрики()
    {
        Assert.True(File.Exists(ПутьСервера), "EXE COM-сервера не поставляется с тестами.");

        var журнал = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ComAutomationEnvironment.ApplicationDirectoryName,
            "com-automation.log");

        Directory.CreateDirectory(Path.GetDirectoryName(журнал)!);

        var запущенШтатно = false;

        // Отметка длины журнала ДО запуска процессов: доказательства читаются
        // только из записей текущего эксперимента.
        var доЭксперимента = File.Exists(журнал) ? new FileInfo(журнал).Length : 0;

        // Первый экземпляр: отдельный процесс сервера (mutex и helper-окно).
        using var первый = Process.Start(new ProcessStartInfo(ПутьСервера)
        {
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        Assert.NotNull(первый);

        try
        {
            // Дожидаемся появления helper-окна первого экземпляра.
            var крайнийСрок = DateTime.UtcNow.AddSeconds(5);
            IntPtr helperПервого;

            do
            {
                Thread.Sleep(100);
                helperПервого = FindWindow(ForegroundWindowService.HelperWindowClassName, null);
            }
            while (helperПервого == IntPtr.Zero && DateTime.UtcNow < крайнийСрок);

            Assert.NotEqual(IntPtr.Zero, helperПервого);

            // Второй экземпляр: обязан найти первый, отправить активацию и
            // завершиться, не регистрируя вторую фабрику классов.
            using var второй = Process.Start(new ProcessStartInfo(ПутьСервера)
            {
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            Assert.NotNull(второй);

            Assert.True(
                второй!.WaitForExit(TimeSpan.FromSeconds(15)),
                "Второй экземпляр сервера не завершился за 15 секунд.");

            Assert.Equal(0, второй.ExitCode);

            // Первый останавливается штатно: событие завершения уникально для
            // процесса — ждём появления события именно первого PID.
            using var сигнал = ДождатьсяСобытияЗавершения(первый!.Id, TimeSpan.FromSeconds(5));

            Assert.NotNull(сигнал);

            сигнал!.Set();

            Assert.True(
                первый.WaitForExit(TimeSpan.FromSeconds(5)),
                "Первый сервер не завершился через ShutdownEventName за 5 секунд — " +
                "graceful shutdown не сработал.");

            запущенШтатно = true;

            Assert.Equal(0, первый.ExitCode);

            // Доказательства по журналу (только записи после отметки).
            Assert.True(File.Exists(журнал), "Журнал сервера не создан.");

            var новые = ПрочитатьЖурналС(журнал, доЭксперимента);

            // Второй нашёл чужой helper и доставил активацию.
            Assert.Contains("Активация работающего экземпляра", новые, StringComparison.Ordinal);

            // Второй НЕ регистрировал вторую фабрику классов.
            Assert.DoesNotContain(
                $"CoRegisterClassObject: hresult=0x00000000, cookie=16, pid={второй.Id}",
                новые,
                StringComparison.Ordinal);

            // Первый выполнил CoRevokeClassObject перед завершением.
            Assert.True(
                ПодсчитатьСтроки(новые, "CoRevokeClassObject выполнен", $"pid={первый.Id}") >= 1,
                "В журнале нет CoRevokeClassObject для первого сервера: " +
                "фабрика не отозвана при штатном завершении.");

            // В процессе сервера ровно одно helper-окно: дубль службы хоста
            // устранён (регрессия на два helper HWND на процесс).
            Assert.Equal(
                1,
                ПодсчитатьСтроки(новые, "Helper: hwnd=", $"pid={первый.Id}"));

            // Ни один из процессов не оставил живых helper-окон.
            Assert.Equal(IntPtr.Zero, FindWindow(ForegroundWindowService.HelperWindowClassName, null));

            // Не осталось ни первого, ни второго процесса сервера.
            Assert.Empty(Process.GetProcessesByName("Picket302.ComAutomation"));
        }
        finally
        {
            if (!запущенШтатно)
            {
                // Аварийная уборка уже провалившегося теста: сначала штатный
                // сигнал, затем Kill. Провал фиксировался выше; cleanup
                // предотвращает загрязнение SCM для соседних тестов.
                try
                {
                    using var сигнал = EventWaitHandle.OpenExisting(
                        ComServerHost.ShutdownEventNameFor(первый!.Id));

                    сигнал.Set();

                    первый.WaitForExit(3000);
                }
                catch (Exception)
                {
                    // События нет или сервер не ответил — остаётся Kill.
                }

                try
                {
                    if (!первый!.HasExited)
                    {
                        первый.Kill(entireProcessTree: true);
                        первый.WaitForExit(3000);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Процесс уже завершён.
                }
            }
        }

        // Контроль: следующий тест может выполнить холодную активацию —
        // серверных процессов и их окон не осталось.
        Assert.Empty(Process.GetProcessesByName("Picket302.ComAutomation"));
    }

    /// <summary>
    /// Регрессия гонки быстрого перезапуска: сигнальный ManualReset-объект
    /// умершего сервера не должен убивать следующий сервер. Имя события
    /// уникально для процесса, поэтому новый сервер получает чистый
    /// несигнальный канал, даже когда тестовый держатель события первого
    /// процесса ещё открыт (до исправления новый сервер умирал сразу после
    /// CoRegisterClassObject от унаследованного WM_QUIT, и SCM отказывал
    /// следующим активациям с 0x80040154 без запуска процесса).
    /// </summary>
    [Fact]
    public void ГонкаПерезапуска_СигналСтарогоСервера_НеУбиваетНовыйСервер()
    {
        Assert.True(File.Exists(ПутьСервера), "EXE COM-сервера не поставляется с тестами.");

        var журнал = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ComAutomationEnvironment.ApplicationDirectoryName,
            "com-automation.log");

        Directory.CreateDirectory(Path.GetDirectoryName(журнал)!);

        var доЭксперимента = File.Exists(журнал) ? new FileInfo(журнал).Length : 0;
        Process? второй = null;

        try
        {
            // Первый сервер: штатный запуск и остановка; тестовый держатель его
            // события остаётся ОТКРЫТЫМ и сигнальным — худший случай гонки.
            using var первый = Process.Start(new ProcessStartInfo(ПутьСервера)
            {
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            Assert.NotNull(первый);

            var событиеПервого = ДождатьсяСобытияЗавершения(первый!.Id, TimeSpan.FromSeconds(5));

            Assert.NotNull(событиеПервого);

            using (событиеПервого!)
            {
                событиеПервого.Set();

                Assert.True(
                    первый.WaitForExit(TimeSpan.FromSeconds(5)),
                    "Первый сервер не завершился через ShutdownEventName.");

                Assert.Equal(0, первый.ExitCode);

                // Держатель события первого процесса жив, и событие сигнально.
                Assert.True(событиеПервого.WaitOne(0), "Событие первого процесса должно оставаться сигнальным.");

                // Новый сервер в худших условиях: без unregister, сразу.
                второй = Process.Start(new ProcessStartInfo(ПутьСервера)
                {
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });

                Assert.NotNull(второй);

                // Новый сервер обязан ЖИТЬ: унаследованный сигнал не доставляется.
                Thread.Sleep(TimeSpan.FromSeconds(2));

                Assert.False(
                    второй!.HasExited,
                    "Новый сервер умер сразу после запуска: унаследованный сигнал предыдущего " +
                    "процесса достиг нового сервера (гонка быстрого перезапуска).");

                var новые = ПрочитатьЖурналС(журнал, доЭксперимента);

                // Доказательство отсутствия самопроизвольного выхода: фабрика
                // зарегистрирована, но не отозвана до адресного сигнала.
                Assert.True(
                    ПодсчитатьСтроки(новые, "CoRegisterClassObject", $"pid={второй.Id}") == 1,
                    "Новый сервер не зарегистрировал фабрику.");

                Assert.True(
                    ПодсчитатьСтроки(новые, "CoRevokeClassObject", $"pid={второй.Id}") == 0,
                    "Новый сервер отозвал фабрику самопроизвольно: немедленный выход " +
                    "сразу после запуска (до адресного сигнала завершения).");

                // Штатная остановка нового сервера его собственным событием.
                using var событиеВторого = ДождатьсяСобытияЗавершения(второй.Id, TimeSpan.FromSeconds(5));

                Assert.NotNull(событиеВторого);

                событиеВторого!.Set();

                Assert.True(
                    второй.WaitForExit(TimeSpan.FromSeconds(5)),
                    "Новый сервер не завершился через собственное ShutdownEventName.");

                Assert.Equal(0, второй.ExitCode);
            }
        }
        finally
        {
            // Аварийная уборка провалившегося теста: сначала адресные события,
            // затем Kill — чтобы соседние COM-тесты получили чистое состояние.
            foreach (var процесс in Process.GetProcessesByName("Picket302.ComAutomation"))
            {
                using (процесс)
                {
                    try
                    {
                        using var сигнал = EventWaitHandle.OpenExisting(
                            ComServerHost.ShutdownEventNameFor(процесс.Id));

                        сигнал.Set();

                        процесс.WaitForExit(3000);
                    }
                    catch (Exception)
                    {
                        // События нет — остаётся Kill ниже.
                    }

                    try
                    {
                        if (!процесс.HasExited)
                        {
                            процесс.Kill(entireProcessTree: true);
                            процесс.WaitForExit(3000);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Процесс уже завершён.
                    }
                }
            }
        }

        Assert.Empty(Process.GetProcessesByName("Picket302.ComAutomation"));
    }

    /// <summary>
    /// Дожидается создания сервером именованного события завершения
    /// указанного процесса.
    /// </summary>
    /// <param name="pid">Идентификатор процесса сервера.</param>
    /// <param name="ожидание">Полное время ожидания.</param>
    /// <returns>Открытое событие либо <see langword="null"/>.</returns>
    private static EventWaitHandle? ДождатьсяСобытияЗавершения(int pid, TimeSpan ожидание)
    {
        var дедлайн = DateTime.UtcNow + ожидание;

        while (DateTime.UtcNow < дедлайн)
        {
            try
            {
                return EventWaitHandle.OpenExisting(ComServerHost.ShutdownEventNameFor(pid));
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Сервер ещё не дошёл до создания события — повторяем.
            }
            catch (UnauthorizedAccessException)
            {
                // Событие другого вида доступа — повторная попытка бессмысленна.
                return null;
            }

            Thread.Sleep(100);
        }

        return null;
    }

    /// <summary>
    /// Читает записи журнала, записанные начиная с указанного смещения:
    /// исторические строки прежних прогонов не участвуют в проверках.
    /// </summary>
    /// <param name="путь">Путь к журналу.</param>
    /// <param name="смещение">Смещение в байтах.</param>
    /// <returns>Текст новой части журнала.</returns>
    private static string ПрочитатьЖурналС(string путь, long смещение)
    {
        using var поток = new FileStream(путь, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        поток.Seek(смещение, SeekOrigin.Begin);

        using var читатель = new StreamReader(поток, System.Text.Encoding.UTF8);

        return читатель.ReadToEnd();
    }

    /// <summary>Считает строки, содержащие обе подстроки.</summary>
    /// <param name="текст">Текст.</param>
    /// <param name="первая">Первая подстрока.</param>
    /// <param name="вторая">Вторая подстрока.</param>
    /// <returns>Число совпавших строк.</returns>
    private static int ПодсчитатьСтроки(string текст, string первая, string вторая)
    {
        var счёт = 0;

        foreach (var строка in текст.Split('\n'))
        {
            if (строка.Contains(первая, StringComparison.Ordinal)
                && строка.Contains(вторая, StringComparison.Ordinal))
            {
                счёт++;
            }
        }

        return счёт;
    }
}
