using Picket302.Infrastructure;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Production-safe гард изоляции профиля: равенство, вложенность в обе
/// стороны, регистр, разделители, sibling-префикс и reparse-ссылки.
/// Тесты используют синтетические пути — реальный производственный
/// профиль не читается и не изменяется.
/// </summary>
public sealed class ProfileIsolationGuardTests
{
    /// <summary>Синтетический производственный каталог.</summary>
    private const string Производственный = @"C:\prod\Root\Picket302";

    [Theory]
    [InlineData(@"C:\prod\Root\Picket302")]
    [InlineData(@"C:\prod\Root\Picket302\")]
    [InlineData(@"c:\PROD\Root\PICKET302")]
    [InlineData(@"C:/prod/Root/Picket302/")]
    [InlineData(@"C:\prod\Root\Picket302\\")]
    public void Кандидат_СовпадаетСПроизводственным_Отклоняется(string кандидат)
    {
        var причина = ProfileIsolationGuard.ПричинаОтказа(кандидат, Производственный);

        Assert.NotNull(причина);
        Assert.Contains("совпадает", причина, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\prod\Root\Picket302\logs")]
    [InlineData(@"C:\prod\Root\Picket302\Secrets\db")]
    [InlineData(@"C:\prod\Root\Picket302\Picket302")]
    [InlineData(@"c:\PROD\Root\picket302\LOGS\app.log")]
    public void Кандидат_ВнутриПроизводственного_Отклоняется(string кандидат)
    {
        var причина = ProfileIsolationGuard.ПричинаОтказа(кандидат, Производственный);

        Assert.NotNull(причина);
        Assert.Contains("внутри производственного", причина, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\prod\Root")]
    [InlineData(@"C:\prod")]
    [InlineData(@"C:\prod\Root\")]
    public void Кандидат_СодержитПроизводственный_Отклоняется(string кандидат)
    {
        var причина = ProfileIsolationGuard.ПричинаОтказа(кандидат, Производственный);

        Assert.NotNull(причина);
        Assert.Contains("находится внутри каталога профиля", причина, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sibling с общим текстовым префиксом не является потомком и не
    /// блокируется: строковый StartsWith здесь ложноположителен.
    /// </summary>
    [Theory]
    [InlineData(@"C:\prod\Root\Picket302-acceptance")]
    [InlineData(@"C:\prod\Root\Picket302x")]
    [InlineData(@"C:\prod\Root\Picket302_backup\inner")]
    public void Кандидат_SiblingСОбщимПрефиксом_НеБлокируется(string кандидат)
    {
        Assert.Null(ProfileIsolationGuard.ПричинаОтказа(кандидат, Производственный));
    }

    [Fact]
    public void Кандидат_ОбычныйВременныйПуть_НеБлокируется()
    {
        Assert.Null(ProfileIsolationGuard.ПричинаОтказа(
            @"C:\Temp\Picket302Acceptance\abc123\Picket302", Производственный));
    }

    /// <summary>
    /// Кандидат через junction/symlink на производственный каталог
    /// отклоняется после канонизации. Реализуемо только если среда
    /// позволяет создать переопределение; иначе тест пропускает проверку
    /// и SYMLINK_GUARD считается не измеренным.
    /// </summary>
    [Fact]
    public void Кандидат_ЧерезReparseНаПроизводственный_Отклоняется()
    {
        var база = Path.Combine(Path.GetTempPath(), "P302GuardTest_" + Guid.NewGuid().ToString("N"));
        var цель = Path.Combine(база, "prod-target");
        var ссылка = Path.Combine(база, "prod-link");
        var кореньСсылка = Path.Combine(база, "root-link");

        try
        {
            Directory.CreateDirectory(цель);

            var создано = СоздатьJunction(ссылка, цель);

            if (!создано)
            {
                // Среда не позволяет создать переопределение: проверка
                // reparse-ветви не измеряется (см. SYMLINK_GUARD_VERIFIED).
                return;
            }

            // Ссылка → цель, совпадающая с production: отказ по равенству.
            var причина = ProfileIsolationGuard.ПричинаОтказа(ссылка, цель);

            Assert.NotNull(причина);
            Assert.Contains("совпадает", причина, StringComparison.Ordinal);

            // Ссылка → цель = корень production: отказ по вложенности.
            Directory.CreateDirectory(Path.Combine(цель, "inner"));

            if (СоздатьJunction(кореньСсылка, Path.Combine(цель, "inner")))
            {
                var причина2 = ProfileIsolationGuard.ПричинаОтказа(
                    Path.Combine(кореньСсылка, "Picket302"), цель);

                Assert.NotNull(причина2);
                Assert.Contains("внутри производственного", причина2, StringComparison.Ordinal);
            }
        }
        finally
        {
            // Ссылки удаляются до рекурсивного удаления базы: рекурсивный
            // обход заходит внутрь цели ссылки и падает на ней.
            try
            {
                if (Directory.Exists(ссылка))
                {
                    Directory.Delete(ссылка, false);
                }
            }
            catch (IOException)
            {
            }

            try
            {
                if (Directory.Exists(кореньСсылка))
                {
                    Directory.Delete(кореньСсылка, false);
                }
            }
            catch (IOException)
            {
            }

            try
            {
                if (Directory.Exists(база))
                {
                    Directory.Delete(база, recursive: true);
                }
            }
            catch (IOException)
            {
                // Временная база теста: сбой удаления не влияет на результат.
            }
        }
    }

    /// <summary>
    /// Dangling ссылка канонизируется к целевому пути: запись через
    /// ссылку попадает в цель, поэтому канонизированный кандидат и есть
    /// цель (проверка разрешимости dangling-ссылок).
    /// </summary>
    [Fact]
    public void Кандидат_DanglingСсылка_КанонизируетсяКЦели()
    {
        var база = Path.Combine(Path.GetTempPath(), "P302GuardTest_" + Guid.NewGuid().ToString("N"));
        var цель = Path.Combine(база, "missing-target");
        var ссылка = Path.Combine(база, "dangling-link");

        try
        {
            Directory.CreateDirectory(база);

            if (!СоздатьСимлинк(ссылка, цель))
            {
                // Прав на symbolic link нет: ветвь остаётся NOT_MEASURED.
                return;
            }

            // Канонизированный кандидат — целевой путь, поэтому сравнение с
            // той же целью даёт «совпадает».
            var причина = ProfileIsolationGuard.ПричинаОтказа(ссылка, цель);

            Assert.NotNull(причина);
            Assert.Contains("совпадает", причина, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (File.Exists(ссылка))
                {
                    File.Delete(ссылка);
                }
                else if (Directory.Exists(ссылка))
                {
                    Directory.Delete(ссылка, false);
                }

                if (Directory.Exists(база))
                {
                    Directory.Delete(база, recursive: true);
                }
            }
            catch (IOException)
            {
                // Временная база теста: сбой удаления не влияет на результат.
            }
        }
    }

    /// <summary>
    /// Зацикленная цепочка ссылок не может быть канонизирована — путь
    /// отклоняется fail-closed вместо признания безопасным.
    /// </summary>
    [Fact]
    public void Кандидат_ЗацикленнаяЦепочкаСсылок_FailClosed()
    {
        var база = Path.Combine(Path.GetTempPath(), "P302GuardTest_" + Guid.NewGuid().ToString("N"));
        var ссылкаА = Path.Combine(база, "loop-a");
        var ссылкаБ = Path.Combine(база, "loop-b");

        try
        {
            Directory.CreateDirectory(база);

            var созданоА = СоздатьJunction(ссылкаА, ссылкаБ);
            var созданоБ = СоздатьJunction(ссылкаБ, ссылкаА);

            if (!созданоА || !созданоБ)
            {
                // Переопределения недоступны: ветвь остаётся NOT_MEASURED.
                return;
            }

            Assert.Throws<InvalidOperationException>(() =>
                ProfileIsolationGuard.ПричинаОтказа(ссылкаА, Производственный));
        }
        finally
        {
            // Ссылки удаляются до рекурсивного удаления базы.
            foreach (var ссылка in new[] { ссылкаА, ссылкаБ })
            {
                try
                {
                    if (Directory.Exists(ссылка))
                    {
                        Directory.Delete(ссылка, false);
                    }
                }
                catch (IOException)
                {
                }
            }

            try
            {
                if (Directory.Exists(база))
                {
                    Directory.Delete(база, recursive: true);
                }
            }
            catch (IOException)
            {
                // Временная база теста: сбой удаления не влияет на результат.
            }
        }
    }

    /// <summary>
    /// Создаёт junction (не требует привилегий). Возвращает успех.
    /// </summary>
    /// <param name="ссылка">Путь ссылки.</param>
    /// <param name="цель">Путь цели.</param>
    /// <returns><see langword="true"/> — junction создан.</returns>
    private static bool СоздатьJunction(string ссылка, string цель)
    {
        try
        {
            var процесс = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{ссылка}\" \"{цель}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            процесс!.WaitForExit(10000);

            return процесс.ExitCode == 0 && Directory.Exists(ссылка);
        }
        catch (Exception ошибка) when (ошибка is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Создаёт symbolic link (может требовать привилегий). Возвращает успех.
    /// </summary>
    /// <param name="ссылка">Путь ссылки.</param>
    /// <param name="цель">Путь цели.</param>
    /// <returns><see langword="true"/> — ссылка создана.</returns>
    private static bool СоздатьСимлинк(string ссылка, string цель)
    {
        try
        {
            Directory.CreateSymbolicLink(ссылка, цель);

            // Dangling-ссылка не существует как каталог: успех определяется
            // наличием переопределения, а не Exists.
            return (File.GetAttributes(ссылка) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ошибка) when (ошибка is UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
