using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Picket302.Infrastructure.Data;
using Picket302.Shared.Protocol;
using Picket302.Service.Data;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Проверки хранилища управляемых пакетов шаблонов на сервере: отклонение
/// злонамеренных архивов и путей, версии с отпечатками, атомарная запись и
/// удаление.
/// </summary>
public sealed class SharedTemplatePackageStoreTests : IDisposable
{
    private readonly string _каталог;
    private readonly SqliteUnitOfWork _сессия;

    /// <summary>Создаёт тест с временным каталогом и сессией базы.</summary>
    public SharedTemplatePackageStoreTests()
    {
        _каталог = Path.Combine(Path.GetTempPath(), "PkgStore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_каталог);

        _сессия = new SqliteUnitOfWork(
            new SqliteConnectionStringBuilder { DataSource = Path.Combine(_каталог, "ops.db") }.ToString());

        using var команда = _сессия.Connection.CreateCommand();
        команда.CommandText =
            "CREATE TABLE IF NOT EXISTS shared_template_packages (" +
            "package_id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, version INTEGER NOT NULL DEFAULT 1, " +
            "content_hash TEXT NOT NULL, size_bytes INTEGER NOT NULL DEFAULT 0, file_name TEXT NOT NULL, " +
            "is_builtin INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);";
        команда.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        ОсвободитьСессию();

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

    private void ОсвободитьСессию()
    {
        _сессия.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static byte[] ПакетСБланком(string имяЗаписи = "blank.xml", string? содержимое = null)
    {
        using var поток = new MemoryStream();
        using (var архив = new ZipArchive(поток, ZipArchiveMode.Create, leaveOpen: true))
        {
            var запись = архив.CreateEntry(имяЗаписи);
            using var писатель = new StreamWriter(запись.Open(), Encoding.UTF8);
            писатель.Write(содержимое ?? "<blank/>");
        }

        return поток.ToArray();
    }

    [Fact]
    public async Task Пакет_Сохраняется_С_Версией_И_Отпечатком()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);
        var содержимое = ПакетСБланком();

        var dto = await хранилище.СохранитьAsync("Бланк поставки", содержимое, isBuiltin: false);

        Assert.Equal(1, dto.Version);
        Assert.Equal(64, dto.ContentHash.Length);

        var ожидаемый = Convert.ToHexString(SHA256.HashData(содержимое)).ToLowerInvariant();
        Assert.Equal(ожидаемый, dto.ContentHash);

        // Чтение конкретной версии возвращает то же содержимое.
        var прочитанное = await хранилище.ПрочитатьВерсиюAsync(dto.PackageId, 1);

        Assert.NotNull(прочитанное);
        Assert.Equal(содержимое, прочитанное);
    }

    [Fact]
    public async Task Повторная_Загрузка_Того_Же_Имени_Увеличивает_Версию()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);

        var первая = await хранилище.СохранитьAsync("Бланк", ПакетСБланком(), false);
        var вторая = await хранилище.СохранитьAsync("Бланк", ПакетСБланком("blank.xml", "<blank v2/>"), false);

        Assert.Equal(1, первая.Version);
        Assert.Equal(2, вторая.Version);
        Assert.Equal(первая.PackageId, вторая.PackageId);
        Assert.NotEqual(первая.ContentHash, вторая.ContentHash);

        // Старая версия остаётся доступной: шаблон, привязанный к версии 1,
        // продолжает её получать.
        var старая = await хранилище.ПрочитатьВерсиюAsync(первая.PackageId, 1);

        Assert.NotNull(старая);
    }

    [Fact]
    public async Task Запись_С_Обходом_Каталогов_Отклоняется()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);

        var обходКаталога = ПакетСБланком("../evil.xml");
        var абсолютныйПуть = ПакетСБланком("/etc/evil.xml");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => хранилище.СохранитьAsync("Вредный", обходКаталога, false));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => хранилище.СохранитьAsync("Вредный", абсолютныйПуть, false));
    }

    [Fact]
    public async Task Пакет_Без_Бланка_Отклоняется()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);

        using var поток = new MemoryStream();
        using (var архив = new ZipArchive(поток, ZipArchiveMode.Create, leaveOpen: true))
        {
            архив.CreateEntry("readme.txt");
        }

        var безБланка = поток.ToArray();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => хранилище.СохранитьAsync("Без бланка", безБланка, false));
    }

    [Fact]
    public async Task Не_Zip_Файл_Отклоняется()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => хранилище.СохранитьAsync("Не архив", "это не архив"u8.ToArray(), false));
    }

    [Fact]
    public async Task Распакованный_Объём_Выше_Предела_Отклоняется()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);

        // Один архив с записью, распакованный размер которой выше
        // MaxTemplatePackageExpandedBytes: сама запись сжимается хорошо,
        // поэтому предел проверяется по распакованному объёму.
        using var поток = new MemoryStream();
        using (var архив = new ZipArchive(поток, ZipArchiveMode.Create, leaveOpen: true))
        {
            var запись = архив.CreateEntry("blank.xml");
            using var писатель = запись.Open();
            var гигантский = new byte[SharedProtocol.MaxTemplatePackageExpandedBytes + 1];

            for (var i = 0; i < гигантский.Length; i++)
            {
                гигантский[i] = (byte)(i % 251);
            }

            писатель.Write(гигантский);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => хранилище.СохранитьAsync("Бомба", поток.ToArray(), false));
    }

    [Fact]
    public async Task Чтение_Несуществующей_Версии_Возвращает_Пусто()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);
        var dto = await хранилище.СохранитьAsync("Бланк", ПакетСБланком(), false);

        var отсутствующая = await хранилище.ПрочитатьВерсиюAsync(dto.PackageId, 99);

        Assert.Null(отсутствующая);
    }

    [Fact]
    public async Task Чтение_С_Недопустимым_Идентификатором_Отклоняется()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await хранилище.ПрочитатьВерсиюAsync("../../etc", 1));
    }

    [Fact]
    public async Task Удаление_Снимает_Пакет_И_Файлы()
    {
        var хранилище = new SharedTemplatePackageStore(_каталог, _сессия);
        var dto = await хранилище.СохранитьAsync("Бланк", ПакетСБланком(), false);

        var удалено = await хранилище.УдалитьAsync(dto.PackageId);

        Assert.True(удалено);
        Assert.False(Directory.Exists(Path.Combine(_каталог, dto.PackageId)));

        var список = await хранилище.ПеречислитьAsync();

        Assert.Empty(список);
    }
}
