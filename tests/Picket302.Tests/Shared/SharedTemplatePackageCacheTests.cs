using System.Security.Cryptography;
using Picket302.App.Shared;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки локального кэша пакетов шаблонов: попадание кэша по отпечатку,
/// повторная загрузка после смены версии и отказ при несовпадении
/// содержимого с объявленным отпечатком.
/// </summary>
public sealed class SharedTemplatePackageCacheTests : IDisposable
{
    private readonly string _каталог;

    /// <summary>Создаёт тест с временным каталогом.</summary>
    public SharedTemplatePackageCacheTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "PkgCache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_каталог);
    }

    /// <inheritdoc />
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
            // Каталог остаётся: временные файлы проверки не критичны.
        }

        GC.SuppressFinalize(this);
    }

    private static string Отпечаток(byte[] содержимое) =>
        Convert.ToHexString(SHA256.HashData(содержимое)).ToLowerInvariant();

    [Fact]
    public async Task Кэш_Отдаёт_Путь_Без_Повторной_Загрузки()
    {
        var кэш = new SharedTemplatePackageCache(_каталог);
        var содержимое = "пакет версии один"u8.ToArray();

        var загрузок = 0;

        string Путь() => кэш.ПутьКВерсииAsync(
            "пакет-1",
            1,
            Отпечаток(содержимое),
            _ =>
            {
                загрузок++;
                return Task.FromResult(содержимое);
            }).GetAwaiter().GetResult();

        var первый = Путь();
        var второй = Путь();

        Assert.Equal(1, загрузок);
        Assert.Equal(первый, второй);
        Assert.True(File.Exists(первый));
    }

    [Fact]
    public async Task После_Смены_Версии_Кэш_Загружает_Заново()
    {
        var кэш = new SharedTemplatePackageCache(_каталог);

        var содержимоеПервой = "пакет версии один"u8.ToArray();
        var содержимоеВторой = "пакет версии два"u8.ToArray();

        var путьПервый = await кэш.ПутьКВерсииAsync(
            "пакет-1",
            1,
            Отпечаток(содержимоеПервой),
            _ => Task.FromResult(содержимоеПервой));

        // Сервер выдал версию 2: отпечаток в ключе кэша другой, старый файл
        // не возвращается даже при совпадающем номере идентификатора.
        var путьВторой = await кэш.ПутьКВерсииAsync(
            "пакет-1",
            2,
            Отпечаток(содержимоеВторой),
            _ => Task.FromResult(содержимоеВторой));

        Assert.NotEqual(путьПервый, путьВторой);
        Assert.Contains("v1-", путьПервый, StringComparison.Ordinal);
        Assert.Contains("v2-", путьВторой, StringComparison.Ordinal);
        Assert.Equal(содержимоеВторой, await File.ReadAllBytesAsync(путьВторой));
    }

    [Fact]
    public async Task Порченый_Кэш_Заменяется_Содержимым_Сервера()
    {
        var кэш = new SharedTemplatePackageCache(_каталог);
        var содержимое = "честный пакет"u8.ToArray();
        var отпечаток = Отпечаток(содержимое);

        var путь = await кэш.ПутьКВерсииAsync(
            "пакет-1",
            1,
            отпечаток,
            _ => Task.FromResult(содержимое));

        // Локальная копия повреждена (сбой носителя): при следующем обращении
        // отпечаток файла не совпадает с ожидаемым, и содержимое загружается
        // заново.
        await File.WriteAllTextAsync(путь, "повреждённое содержимое");

        var загрузок = 0;

        var восстановленный = await кэш.ПутьКВерсииAsync(
            "пакет-1",
            1,
            отпечаток,
            _ =>
            {
                загрузок++;
                return Task.FromResult(содержимое);
            });

        Assert.Equal(1, загрузок);
        Assert.Equal(содержимое, await File.ReadAllBytesAsync(восстановленный));
    }

    [Fact]
    public async Task Несовпадение_Содержимого_С_Отпечатком_Отклоняется()
    {
        var кэш = new SharedTemplatePackageCache(_каталог);
        var ожидаемый = Отпечаток("честный пакет"u8.ToArray());

        await Assert.ThrowsAsync<SharedModeException>(() =>
            кэш.ПутьКВерсииAsync(
                "пакет-1",
                1,
                ожидаемый,
                _ => Task.FromResult("подменённый пакет"u8.ToArray())));
    }
}
