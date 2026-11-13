using System.Text;
using Picket302.Infrastructure.Update;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// S47 (ТЗ §8.12, функция 163, остаток): целостность пакета обновления —
/// SHA-256 из манифеста updates.txt проверяется после загрузки; повреждённый
/// пакет не ставится (удаляется с откатом). Загрузка через инъекцию —
/// реальная сеть не используется.
/// </summary>
public sealed class UpdatePackageTests
{
    /// <summary>Содержимое пакета и его точный SHA-256.</summary>
    private static readonly byte[] Пакет = Encoding.UTF8.GetBytes("Пакет обновления Picket302 v1.1.0");

    private static string Хеш(byte[] данные)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(данные));

    /// <summary>Манифест updates.txt с указанным хешем.</summary>
    private static string Манифест(string хеш) =>
        "version = 1.1.0\r\n" +
        "url = https://updates.example.test/picket302-update.bin\r\n" +
        $"sha256 = {хеш}\r\n" +
        "notes = Тестовый пакет";

    /// <summary>Загрузчик-инъекция: отдаёт поток пакета (вместо HTTP).</summary>
    private static Func<Uri, CancellationToken, Task<Stream>> ЗагрузчикПакета(byte[] содержимое)
        => (_, _) => Task.FromResult<Stream>(new MemoryStream(содержимое, writable: false));

    private static void ОчиститьКаталог()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "Picket302Update");

        if (Directory.Exists(каталог))
        {
            Directory.Delete(каталог, recursive: true);
        }
    }

    [Fact]
    public async Task ЦелыйПакет_ХешСовпал_ФайлСкачанЦелым()
    {
        ОчиститьКаталог();

        var журнал = new List<string>();

        var проверятель = new UpdateChecker(
            "https://updates.example.test/updates.txt",
            "1.0.0",
            запрос: (_, _) => Task.FromResult(Манифест(Хеш(Пакет))),
            журнал: журнал.Add,
            загрузчик: ЗагрузчикПакета(Пакет));

        var информация = await проверятель.CheckForUpdatesAsync();

        Assert.True(информация.IsUpdateAvailable);

        // Манифест отдал SHA-256 в отдельном поле обновления.
        Assert.Equal(Хеш(Пакет), информация.ExpectedSha256);

        var путь = await проверятель.DownloadUpdateAsync(информация);

        Assert.NotNull(путь);
        Assert.Equal(Пакет, await File.ReadAllBytesAsync(путь!));

        File.Delete(путь!);
    }

    /// <summary>
    /// Критерий приёмки №163 «повреждённый пакет не ставится»: хеш из
    /// манифеста не совпадает → файл удалён (откат), путь null.
    /// </summary>
    [Fact]
    public async Task ПовреждённыйПакет_ХешНеСовпал_ФайлУдалён()
    {
        ОчиститьКаталог();

        var журнал = new List<string>();

        var проверятель = new UpdateChecker(
            "https://updates.example.test/updates.txt",
            "1.0.0",
            запрос: (_, _) => Task.FromResult(Манифест(new string('A', 64))),
            журнал: журнал.Add,
            загрузчик: ЗагрузчикПакета(Пакет));

        var информация = await проверятель.CheckForUpdatesAsync();

        Assert.True(информация.IsUpdateAvailable);
        Assert.Equal(new string('A', 64), информация.ExpectedSha256);

        var путь = await проверятель.DownloadUpdateAsync(информация);

        Assert.Null(путь);

        // Откат: повреждённый файл удалён, журнал содержит причину.
        Assert.Contains(
            журнал,
            запись => запись.Contains("хеш", StringComparison.OrdinalIgnoreCase));

        var каталог = Path.Combine(Path.GetTempPath(), "Picket302Update");

        Assert.False(Directory.Exists(каталог) && Directory.GetFiles(каталог, "*.bin").Length > 0);
    }

    /// <summary>Манифест без sha256 — обратная совместимость (проверка пропускается).</summary>
    [Fact]
    public async Task МанифестБезХеша_ОбратнаяСовместимость()
    {
        ОчиститьКаталог();

        var проверятель = new UpdateChecker(
            "https://updates.example.test/updates.txt",
            "1.0.0",
            запрос: (_, _) => Task.FromResult("version = 1.1.0\r\nurl = https://x.test/p.bin"),
            загрузчик: ЗагрузчикПакета(Пакет));

        var информация = await проверятель.CheckForUpdatesAsync();

        Assert.True(информация.IsUpdateAvailable);
        Assert.Null(информация.ExpectedSha256);

        var путь = await проверятель.DownloadUpdateAsync(информация);

        Assert.NotNull(путь);

        File.Delete(путь!);
    }

    /// <summary>Манифест отдаёт sha256 в поле обновления (функция 163).</summary>
    [Fact]
    public async Task МанифестОтдаётХешВПолеОбновления()
    {
        ОчиститьКаталог();

        var проверятель = new UpdateChecker(
            "https://updates.example.test/updates.txt",
            "1.0.0",
            запрос: (_, _) => Task.FromResult(Манифест(Хеш(Пакет))),
            загрузчик: ЗагрузчикПакета(Пакет));

        var информация = await проверятель.CheckForUpdatesAsync();

        Assert.Equal(Хеш(Пакет), информация.ExpectedSha256);
    }
}
