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

    /// <summary>Изолированный каталог проверок (FINAL-10): производственный
    /// %TEMP%/Picket302Update пользователя не затрагивается.</summary>
    private static string КаталогПроверки { get; } = Path.Combine(
        Path.GetTempPath(),
        "Picket302UpdateTests-" + Guid.NewGuid().ToString("N"));

    private static void ОчиститьКаталог()
    {
        UpdateChecker.КаталогЗагрузкиДляПроверок = КаталогПроверки;

        if (Directory.Exists(КаталогПроверки))
        {
            Directory.Delete(КаталогПроверки, recursive: true);
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

        Assert.False(Directory.Exists(КаталогПроверки) && Directory.GetFiles(КаталогПроверки, "*.bin").Length > 0);
    }

    /// <summary>
    /// Корректирующий проход: доказательство целостности ОБЯЗАТЕЛЬНО —
    /// манифест без sha256 отклоняется (загрузчик не вызывается).
    /// </summary>
    [Fact]
    public async Task ОтсутствуетХеш_ЗагрузкаЗапрещена_ЗагрузчикНеВызван()
    {
        ОчиститьКаталог();

        var вызовыЗагрузчика = 0;

        var проверятель = new UpdateChecker(
            "https://updates.example.test/updates.txt",
            "1.0.0",
            запрос: (_, _) => Task.FromResult("version = 1.1.0\r\nurl = https://x.test/p.bin"),
            загрузчик: (_, _) =>
            {
                вызовыЗагрузчика++;

                return Task.FromResult<Stream>(new MemoryStream(Пакет, writable: false));
            });

        var информация = await проверятель.CheckForUpdatesAsync();

        Assert.True(информация.IsUpdateAvailable);
        Assert.Null(информация.ExpectedSha256);

        var путь = await проверятель.DownloadUpdateAsync(информация);

        Assert.Null(путь);
        Assert.Equal(0, вызовыЗагрузчика);
    }

    /// <summary>Корректирующий проход: HTTP-адрес отклоняется (только HTTPS).</summary>
    [Fact]
    public async Task HttpАдрес_Отклоняется()
    {
        ОчиститьКаталог();

        var вызовыЗагрузчика = 0;

        var проверятель = new UpdateChecker(
            "https://updates.example.test/updates.txt",
            "1.0.0",
            запрос: (_, _) => Task.FromResult(Манифест(Хеш(Пакет))),
            загрузчик: (_, _) =>
            {
                вызовыЗагрузчика++;

                return Task.FromResult<Stream>(new MemoryStream(Пакет, writable: false));
            });

        var информация = await проверятель.CheckForUpdatesAsync();

        Assert.True(информация.IsUpdateAvailable);

        // Подменяем URL в поле обновления на HTTP.
        var httpИнформация = информация with { DownloadUrl = "http://updates.example.test/p.bin" };

        var путь = await проверятель.DownloadUpdateAsync(httpИнформация);

        Assert.Null(путь);
        Assert.Equal(0, вызовыЗагрузчика);
    }

    /// <summary>
    /// Отмена загрузки: частичный файл удаляется (откат), отмена
    /// пробрасывается вызывающему коду.
    /// </summary>
    [Fact]
    public async Task ОтменаЗагрузки_ЧастичныйФайлУдаляется()
    {
        ОчиститьКаталог();

        var медленный = new SlowStream(Пакет);

        var проверятель = new UpdateChecker(
            "https://updates.example.test/updates.txt",
            "1.0.0",
            запрос: (_, _) => Task.FromResult(Манифест(Хеш(Пакет))),
            загрузчик: (_, _) => Task.FromResult<Stream>(медленный));

        var информация = await проверятель.CheckForUpdatesAsync();

        Assert.True(информация.IsUpdateAvailable);

        using var отмена = new CancellationTokenSource();
        var задача = проверятель.DownloadUpdateAsync(информация, отмена.Token);

        await Task.Delay(80);
        отмена.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => задача);

        // Частичный файл удалён.
        Assert.False(Directory.Exists(КаталогПроверки) && Directory.GetFiles(КаталогПроверки, "*.bin").Length > 0);
    }

    /// <summary>Медленный поток: отдаёт пакет с паузами, отменяемый.</summary>
    private sealed class SlowStream : Stream
    {
        private readonly byte[] _данные;
        private int _позиция;

        public SlowStream(byte[] данные)
        {
            _данные = данные;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _данные.Length;
        public override long Position { get => _позиция; set => throw new NotSupportedException(); }

        public override async Task<int> ReadAsync(byte[] буфер, int смещение, int счёт, CancellationToken маркер)
        {
            await Task.Delay(30, маркер).ConfigureAwait(false);

            if (_позиция >= _данные.Length)
            {
                return 0;
            }

            // Отдаём по 1 байту: отмена гарантированно срабатывает
            // в середине загрузки.
            var счётРеальный = Math.Min(1, _данные.Length - _позиция);

            if (счётРеальный == 0)
            {
                return 0;
            }

            буфер[0] = _данные[_позиция];
            _позиция += 1;

            return счётРеальный;
        }

        public override int Read(byte[] буфер, int смещение, int счёт)
            => ReadAsync(буфер, смещение, счёт, CancellationToken.None).GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long смещение, SeekOrigin происхождение) => throw new NotSupportedException();
        public override void SetLength(long значение) => throw new NotSupportedException();
        public override void Write(byte[] буфер, int смещение, int счёт) => throw new NotSupportedException();
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
