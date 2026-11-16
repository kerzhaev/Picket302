using Picket302.Infrastructure.Logging;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Уровни журнала (дефект D3 приёмки 16.11.2026): штатные события не должны
/// выглядеть ошибками, а ошибки — не должны понижаться до Info.
/// </summary>
public sealed class LogHubLevelsTests : IDisposable
{
    private readonly string _каталог = Path.Combine(
        Path.GetTempPath(),
        "p302-log-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Информационное_событие_пишется_без_исключения()
    {
        using var журнал = new LogHub(_каталог);

        журнал.LogInfo("Устройство сопряжено с общей базой 192.168.1.42:30242.");

        var записи = журнал.ReadRecent(LogChannel.App, 20);

        Assert.Contains(записи, строка => строка.Contains("[INF]", StringComparison.Ordinal));
        Assert.Contains(записи, строка => строка.Contains("сопряжено", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(записи, строка => строка.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public void Предупреждение_пишется_уровнем_Warning()
    {
        using var журнал = new LogHub(_каталог);

        журнал.LogWarning("Общая база недоступна: таймаут.");

        var записи = журнал.ReadRecent(LogChannel.App, 20);

        Assert.Contains(записи, строка => строка.Contains("[WRN]", StringComparison.Ordinal));
        Assert.DoesNotContain(записи, строка => строка.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public void Ошибка_без_исключения_остаётся_ошибкой()
    {
        using var журнал = new LogHub(_каталог);

        журнал.LogError("Не удалось прочитать профиль устройства.");

        var записи = журнал.ReadRecent(LogChannel.App, 20);

        Assert.Contains(записи, строка => строка.Contains("[ERR]", StringComparison.Ordinal));
        Assert.Contains(записи, строка => строка.Contains("профиль устройства", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ошибка_с_исключением_сохраняет_исключение()
    {
        using var журнал = new LogHub(_каталог);

        журнал.LogError(new InvalidOperationException("сбой чтения"), "Не удалось прочитать базу.");

        var записи = журнал.ReadRecent(LogChannel.App, 20);

        Assert.Contains(записи, строка => строка.Contains("[ERR]", StringComparison.Ordinal));
        Assert.Contains(записи, строка => строка.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
