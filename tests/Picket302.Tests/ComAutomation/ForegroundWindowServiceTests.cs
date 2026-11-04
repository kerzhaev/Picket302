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
        // реально существует после запуска службы (ранее FindWindow по этому
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

    [Fact]
    public void ПовторныйЗапуск_ВторойПроцесс_ЗавершаетШтатноБезВторойФабрики()
    {
        Assert.True(File.Exists(ПутьСервера), "EXE COM-сервера не поставляется с тестами.");

        var журнал = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ComAutomationEnvironment.ApplicationDirectoryName,
            "com-automation.log");

        Directory.CreateDirectory(Path.GetDirectoryName(журнал)!);

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

            // Отметка длины журнала: доказательства читаются только после неё.
            var доВторого = File.Exists(журнал) ? new FileInfo(журнал).Length : 0;

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

            // Доказательства по журналу (только новые записи после отметки).
            Assert.True(File.Exists(журнал), "Журнал сервера не создан.");

            using var читатель = new FileStream(журнал, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            читатель.Seek((long)доВторого, SeekOrigin.Begin);

            using var текст = new StreamReader(читатель, System.Text.Encoding.UTF8);

            var новые = текст.ReadToEnd();

            // Второй нашёл чужой helper и доставил активацию.
            Assert.Contains("Активация работающего экземпляра", новые, StringComparison.Ordinal);

            // Второй НЕ регистрировал вторую фабрику классов.
            Assert.DoesNotContain(
                $"CoRegisterClassObject: hresult=0x00000000, cookie=16, pid={второй.Id}",
                новые,
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                первый!.Kill(entireProcessTree: true);
                первый.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // Процесс уже завершён.
            }
        }
    }
}
