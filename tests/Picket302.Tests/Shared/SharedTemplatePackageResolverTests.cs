using System.Security.Cryptography;
using Picket302.App.Shared;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Получение содержимого пакета шаблона (.lbk) на рабочем месте по привязке
/// строки шаблона.
/// </summary>
/// <remarks>
/// Требования: после смены версии пакета старое содержимое из кэша не
/// используется; повреждённый кэш заменяется; несовпадение отпечатка даёт
/// безопасную ошибку, а не молчаливое использование чужого макета.
/// </remarks>
public sealed class SharedTemplatePackageResolverTests : IDisposable
{
    private readonly string _каталог = Path.Combine(
        Path.GetTempPath(),
        "picket302-пакеты-" + Guid.NewGuid().ToString("N"));

    public SharedTemplatePackageResolverTests() => Directory.CreateDirectory(_каталог);

    public void Dispose()
    {
        if (Directory.Exists(_каталог))
        {
            Directory.Delete(_каталог, true);
        }
    }

    [Fact]
    public async Task ШаблонСПривязкой_СодержимоеПолучаетсяИСкладываетсяВКэш()
    {
        var получатель = new SharedTemplatePackageResolver(
            new SharedTemplatePackageCache(_каталог));

        var содержимое = new byte[] { 1, 2, 3, 4 };
        var отпечаток = Отпечаток(содержимое);
        var загрузок = 0;

        var путь = await получатель.РазрешитьAsync(
            Шаблон("пакет-1", 1, отпечаток),
            (_, _, _) =>
            {
                загрузок++;
                return Task.FromResult(содержимое);
            });

        Assert.NotNull(путь);
        Assert.True(File.Exists(путь));
        Assert.Equal(содержимое, await File.ReadAllBytesAsync(путь!));
        Assert.Equal(1, загрузок);

        // Повторный запрос той же версии содержимое не загружает заново.
        var повтор = await получатель.РазрешитьAsync(
            Шаблон("пакет-1", 1, отпечаток),
            (_, _, _) =>
            {
                загрузок++;
                return Task.FromResult(содержимое);
            });

        Assert.Equal(путь, повтор);
        Assert.Equal(1, загрузок);
    }

    [Fact]
    public async Task НоваяВерсия_СтароеСодержимоеНеИспользуется()
    {
        var получатель = new SharedTemplatePackageResolver(
            new SharedTemplatePackageCache(_каталог));

        var первое = new byte[] { 1, 1, 1 };
        var второе = new byte[] { 2, 2, 2 };

        var первыйПуть = await получатель.РазрешитьAsync(
            Шаблон("пакет-1", 1, Отпечаток(первое)),
            (_, _, _) => Task.FromResult(первое));

        var второйПуть = await получатель.РазрешитьAsync(
            Шаблон("пакет-1", 2, Отпечаток(второе)),
            (_, _, _) => Task.FromResult(второе));

        Assert.NotEqual(первыйПуть, второйПуть);
        Assert.Equal(второе, await File.ReadAllBytesAsync(второйПуть!));
    }

    [Fact]
    public async Task ПовреждённыйКэш_ЗаменяетсяСодержимымСервера()
    {
        var получатель = new SharedTemplatePackageResolver(
            new SharedTemplatePackageCache(_каталог));

        var содержимое = new byte[] { 9, 9, 9 };
        var отпечаток = Отпечаток(содержимое);

        var путь = await получатель.РазрешитьAsync(
            Шаблон("пакет-1", 1, отпечаток),
            (_, _, _) => Task.FromResult(содержимое));

        // Кэш повреждён: файл подменён содержимым, не совпадающим по
        // отпечатку.
        await File.WriteAllBytesAsync(путь!, new byte[] { 0, 0, 0 });

        var после = await получатель.РазрешитьAsync(
            Шаблон("пакет-1", 1, отпечаток),
            (_, _, _) => Task.FromResult(содержимое));

        Assert.Equal(путь, после);
        Assert.Equal(содержимое, await File.ReadAllBytesAsync(путь!));
    }

    [Fact]
    public async Task НесовпадениеОтпечатка_БезопаснаяОшибкаБезМолчаливойПодмены()
    {
        var получатель = new SharedTemplatePackageResolver(
            new SharedTemplatePackageCache(_каталог));

        var исключение = await Assert.ThrowsAsync<TemplatePackageUnavailableException>(
            () => получатель.РазрешитьAsync(
                Шаблон("пакет-1", 1, Отпечаток(new byte[] { 1, 2, 3 })),
                (_, _, _) => Task.FromResult(new byte[] { 4, 5, 6 })));

        Assert.Contains("Печать и предпросмотр не выполняются", исключение.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ШаблонБезПривязки_СодержимоеНеЗапрашивается()
    {
        var получатель = new SharedTemplatePackageResolver(
            new SharedTemplatePackageCache(_каталог));

        var путь = await получатель.РазрешитьAsync(
            new PrintTemplate { Id = 1, Name = "Без пакета" },
            (_, _, _) => throw new InvalidOperationException("Загрузка не должна выполняться."));

        Assert.Null(путь);
    }

    private static PrintTemplate Шаблон(string идентификатор, long версия, string отпечаток) => new()
    {
        Id = 1,
        Name = "Конверт C5",
        PackageId = идентификатор,
        PackageVersion = версия,
        PackageHash = отпечаток,
    };

    private static string Отпечаток(byte[] содержимое) =>
        Convert.ToHexString(SHA256.HashData(содержимое)).ToLowerInvariant();
}
