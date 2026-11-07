using System.Diagnostics;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Доказательство межпроцессности блокировки: второй независимый процесс
/// не может получить общий мьютекс, пока первый им владеет, и получает
/// его после освобождения; аварийная смерть владельца без освобождения
/// (abandoned) обрабатывается с диагностикой и выдаёт блокировку
/// следующему.
/// </summary>
/// <remarks>
/// Дочерний процесс — PowerShell-проверщик с тем же именем мьютекса,
/// что и у acceptance-драйверов: это доказывает совместимость C#- и
/// PowerShell-сторон контракта.
/// </remarks>
[Collection("ИнфраструктурнаяУборка")]
public sealed class БлокировкаОбщихРесурсовTests
{
    /// <summary>
    /// Пока процесс-владелец держит блокировку, второй процесс не
    /// получает её за ограниченный бюджет (таймаут — диагностируемый
    /// результат); после освобождения — получает.
    /// </summary>
    [Fact]
    public void Мьютекс_БлокируетВторойПроцесс_ИОсвобождается()
    {
        var владение = БлокировкаОбщихРесурсов.Захватить(TimeSpan.FromSeconds(30));

        try
        {
            // Второй процесс с коротким бюджетом не проходит: блокировка
            // межпроцессная, а не только внутри testhost. Код 1 = таймаут.
            Assert.Equal(1, ЗапуститьПроверщика(1500));
        }
        finally
        {
            владение.Dispose();
        }

        // После освобождения второй процесс получает блокировку. Код 0.
        Assert.Equal(0, ЗапуститьПроверщика(15000));
    }

    /// <summary>
    /// Смерть владельца без <c>ReleaseMutex</c> (жёсткое убийство
    /// процесса) не оставляет блокировку навсегда: ОС помечает мьютекс
    /// abandoned, следующий владелец получает его с диагностикой.
    /// </summary>
    [Fact]
    public void Мьютекс_СмертьВладельцаБезОсвобождения_ДаётВладениеСледующему()
    {
        var маркер = Path.Combine(
            Path.GetTempPath(), "P302LockProof_" + Guid.NewGuid().ToString("N") + ".marker");

        try
        {
            // Дочерний процесс захватывает мьютекс и сообщает маркером.
            using var держатель = ЗапуститьДержателя(маркер);

            var дедлайн = DateTime.UtcNow + TimeSpan.FromSeconds(15);

            while (!File.Exists(маркер) && DateTime.UtcNow < дедлайн)
            {
                Thread.Sleep(50);
            }

            Assert.True(File.Exists(маркер), "дочерний процесс не сообщил о захвате блокировки");

            // Жёсткое убийство без ReleaseMutex: мьютекс остаётся abandoned.
            держатель.Kill();
            держатель.WaitForExit();

            // Следующий владелец получает блокировку: abandoned-владение
            // обрабатывается с диагностикой (в stderr), а не тихим сбоем.
            using var владение = БлокировкаОбщихРесурсов.Захватить(TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (File.Exists(маркер))
            {
                try
                {
                    File.Delete(маркер);
                }
                catch (IOException)
                {
                    // Маркер-диагностика: сбой удаления не влияет на результат.
                }
            }
        }
    }

    /// <summary>
    /// Запускает дочерний PowerShell-проверщик с тем же именем мьютекса.
    /// Код 0 — блокировка получена, код 1 — таймаут (диагностируемый
    /// отказ).
    /// </summary>
    /// <param name="бюджетМс">Бюджет ожидания дочернего процесса, мс.</param>
    /// <returns>Код возврата дочернего процесса.</returns>
    private static int ЗапуститьПроверщика(int бюджетМс)
    {
        var код = "$m = New-Object System.Threading.Mutex($false, '"
            + БлокировкаОбщихРесурсов.ИмяМьютекса.Replace("'", "''", StringComparison.Ordinal)
            + "'); $ok = $false; try { $ok = $m.WaitOne(" + бюджетМс
            + ") } catch [System.Threading.AbandonedMutexException] { $ok = $true }"
            + "; if ($ok) { exit 0 } else { exit 1 }";

        using var процесс = System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"" + код + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        процесс!.WaitForExit(60000);

        return процесс.ExitCode;
    }

    /// <summary>
    /// Запускает дочерний процесс-держатель: захватывает мьютекс и пишет
    /// маркер-файл; освобождения не выполняет — убивается родителем.
    /// </summary>
    /// <param name="маркер">Путь маркер-файла.</param>
    /// <returns>Процесс-держатель.</returns>
    private static Process ЗапуститьДержателя(string маркер)
    {
        var код = "$m = New-Object System.Threading.Mutex($false, '"
            + БлокировкаОбщихРесурсов.ИмяМьютекса.Replace("'", "''", StringComparison.Ordinal)
            + "'); $ok = $false; try { $ok = $m.WaitOne(15000) } catch "
            + "[System.Threading.AbandonedMutexException] { $ok = $true }; if ($ok) { "
            + "Set-Content -Path '" + маркер.Replace("'", "''", StringComparison.Ordinal)
            + "' -Value 'locked'; Start-Sleep -Seconds 60 }";

        var процесс = System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"" + код + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        return процесс!;
    }
}
