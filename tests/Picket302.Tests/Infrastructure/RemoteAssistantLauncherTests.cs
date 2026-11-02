using Picket302.Infrastructure.Support;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Регрессия F-C5: загрузчик утилиты удалённого доступа получает целевой
/// путь и не оставляет частичный файл при обрыве.
/// </summary>
public sealed class RemoteAssistantLauncherTests
{
    [Fact]
    public async Task Загрузка_ОбрывПотока_ЦелевойФайлНеОстаётся()
    {
        var папка = Path.Combine(Path.GetTempPath(), "picket302-ras-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(папка);

            var путьУтилиты = Path.Combine(папка, "helper.exe");
            var запущенныйПуть = (string?)null;

            var запускатель = new RemoteAssistantLauncher(
                получитьНастройку: ключ => ключ switch
                {
                    RemoteAssistantLauncher.КлючUrl => "https://support.example.test/helper.exe",
                    RemoteAssistantLauncher.КлючПапки => папка,
                    _ => null,
                },
                скачать: async (_, целевойПуть, _) =>
                {
                    запущенныйПуть = целевойПуть;

                    // Обрыв выполняется через реальную атомарную запись:
                    // частичный файл создаётся и падает на середине.
                    var содержимое = new ОбрывающийсяПоток();

                    await RemoteAssistantLauncher.СохранитьПотокВФайлАтомарно(
                        содержимое,
                        целевойПуть,
                        CancellationToken.None);

                    return true;
                },
                запустить: путь => true,
                папкаПоУмолчанию: папка);

            var запущена = await запускатель.TryLaunchRemoteAssistantAsync();

            Assert.False(запущена);
            Assert.Contains("Не удалось загрузить", запускатель.ПоследняяОшибка, StringComparison.Ordinal);

            // Делегат получил именно целевой путь утилиты (F-C5).
            Assert.Equal(путьУтилиты, запущенныйПуть);

            Assert.False(File.Exists(путьУтилиты), "Целевой файл не должен появиться при обрыве.");
            Assert.False(File.Exists(путьУтилиты + ".part"), "Частичный файл должен удаляться.");
        }
        finally
        {
            if (Directory.Exists(папка))
            {
                Directory.Delete(папка, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Загрузка_Успех_ЦелевойФайлСодержитДанные()
    {
        var папка = Path.Combine(Path.GetTempPath(), "picket302-ras-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(папка);

            var путьУтилиты = Path.Combine(папка, "helper.exe");
            var данные = "содержимое утилиты"u8.ToArray();

            using var содержимое = new MemoryStream(данные);

            await RemoteAssistantLauncher.СохранитьПотокВФайлАтомарно(
                содержимое,
                путьУтилиты,
                CancellationToken.None);

            Assert.True(File.Exists(путьУтилиты));
            Assert.Equal(данные, await File.ReadAllBytesAsync(путьУтилиты));
            Assert.False(File.Exists(путьУтилиты + ".part"));
        }
        finally
        {
            if (Directory.Exists(папка))
            {
                Directory.Delete(папка, recursive: true);
            }
        }
    }

    /// <summary>Поток, падающий IOException при чтении (обрыв загрузки).</summary>
    private sealed class ОбрывающийсяПоток : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Соединение разорвано.");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
