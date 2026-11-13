using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// S32 (ТЗ §8.2, функция 29): журнал входящей корреспонденции — создание,
/// поиск, просмотр, удаление; путь скана в профиле пользователя и
/// сохранность скана при ошибке регистрации.
/// </summary>
public sealed class IncomingMailServiceTests : IAsyncLifetime
{
    private SqliteTestDatabase _база = null!;
    private string _профиль = null!;

    public async Task InitializeAsync()
    {
        _база = await SqliteTestDatabase.CreateAsync();
        _профиль = Path.Combine(Path.GetTempPath(), "p302-s32-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_профиль);
    }

    public async Task DisposeAsync()
    {
        await _база.DisposeAsync();

        try
        {
            Directory.Delete(_профиль, recursive: true);
        }
        catch (IOException)
        {
            // Каталог профиля может быть занят на мгновение.
        }
    }

    private IncomingMailService СоздатьСлужбу() => new(
        new IncomingMailRepository(_база.UnitOfWork),
        _профиль);

    [Fact]
    public async Task Регистрация_Просмотр_Поиск_Удаление()
    {
        var служба = СоздатьСлужбу();

        var первая = await служба.ЗарегистрироватьAsync(
            "ВХ-2026/001",
            new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc),
            "ООО «Отправитель»",
            "Запрос документов",
            IncomingMailType.Letter);

        var вторая = await служба.ЗарегистрироватьAsync(
            "ВХ-2026/002",
            new DateTime(2026, 11, 13, 11, 0, 0, DateTimeKind.Utc),
            "ИП Петров",
            "Претензия о недоставке",
            IncomingMailType.Courier);

        Assert.True(первая.Id > 0);
        Assert.True(вторая.Id > первая.Id);

        // Просмотр по идентификатору.
        var прочитанная = await служба.ПрочитатьAsync(первая.Id);
        Assert.NotNull(прочитанная);
        Assert.Equal("ВХ-2026/001", прочитанная!.RegNumber);
        Assert.Equal("ООО «Отправитель»", прочитанная.Sender);

        // Поиск по подстроке номера и отправителя.
        Assert.Single(await служба.НайтиAsync("ВХ-2026/002"));
        Assert.Single(await служба.НайтиAsync("Петров"));

        // Поиск по содержанию (без учёта падежа — LIKE-подстрока).
        Assert.Single(await служба.НайтиAsync("недоставке"));

        // Пустой запрос — весь журнал, свежие сверху.
        var весь = await служба.НайтиAsync(null);
        Assert.Equal(2, весь.Count);
        Assert.Equal("ВХ-2026/002", весь[0].RegNumber);

        // Удаление.
        Assert.True(await служба.УдалитьAsync(первая.Id));
        Assert.Null(await служба.ПрочитатьAsync(первая.Id));
        Assert.False(await служба.УдалитьAsync(первая.Id));
    }

    [Fact]
    public async Task ПовторныйНомер_Отклоняется()
    {
        var служба = СоздатьСлужбу();

        await служба.ЗарегистрироватьAsync(
            "ВХ-100", DateTime.UtcNow, "Отправитель", "Тема", IncomingMailType.Other);

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            служба.ЗарегистрироватьAsync(
                "ВХ-100", DateTime.UtcNow, "Другой", "Тема", IncomingMailType.Other));
    }

    /// <summary>
    /// Критерий приёмки №29: путь скана в профиле — файл копируется в
    /// <c>scans/</c>, запись ссылается на копию в профиле.
    /// </summary>
    [Fact]
    public async Task Скан_КопируетсяВПрофиль_ПутьУказываетТуда()
    {
        var служба = СоздатьСлужбу();

        var исходныйСкан = Path.Combine(_профиль, "исходный-scan.pdf");
        File.WriteAllText(исходныйСкан, "%PDF-скан конверта");

        var запись = await служба.ЗарегистрироватьAsync(
            "ВХ-СКАН/1",
            DateTime.UtcNow,
            "Суд",
            "Повестка",
            IncomingMailType.Letter,
            исходныйСкан);

        Assert.NotNull(запись.ScanPath);
        Assert.StartsWith(служба.КаталогСканов, запись.ScanPath, StringComparison.Ordinal);
        Assert.True(File.Exists(запись.ScanPath));

        // Содержимое скопировано, исходник не тронут.
        Assert.Contains("%PDF", File.ReadAllText(запись.ScanPath!));
        Assert.True(File.Exists(исходныйСкан));
    }

    /// <summary>
    /// Критерий приёмки №29 «путь скана в профиле, не теряется при ошибке»:
    /// сбой записи в базу не удаляет скан из профиля — регистрацию можно
    /// повторить с тем же файлом.
    /// </summary>
    [Fact]
    public async Task ОшибкаРегистрации_СканВПрофилеНеТеряется()
    {
        var служба = СоздатьСлужбу();

        var скан = Path.Combine(_профиль, "scan-letter.pdf");
        File.WriteAllText(скан, "%PDF-1.4");

        await служба.ЗарегистрироватьAsync(
            "ВХ-ДУБЛЬ", DateTime.UtcNow, "Первый", "Тема", IncomingMailType.Letter, скан);

        // Повтор с занятым номером: вставка падает (UNIQUE).
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            служба.ЗарегистрироватьAsync(
                "ВХ-ДУБЛЬ", DateTime.UtcNow, "Второй", "Тема", IncomingMailType.Letter, скан));

        // Скан при этом жив в профиле (обе копии: первая и «неудачной» попытки).
        var копии = Directory.GetFiles(служба.КаталогСканов, "*.pdf");
        Assert.Equal(2, копии.Length);
        Assert.All(копии, файл => Assert.Contains("%PDF", File.ReadAllText(файл)));
    }

    [Fact]
    public async Task ОтсутствующийСкан_ОшибкаБезЗаписиВЖурнал()
    {
        var служба = СоздатьСлужбу();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            служба.ЗарегистрироватьAsync(
                "ВХ-БИТЫЙ", DateTime.UtcNow, "Отправитель", "Тема", IncomingMailType.Letter,
                Path.Combine(_профиль, "нет-такого.pdf")));

        // Журнал не записан.
        Assert.Empty(await служба.НайтиAsync(null));
    }

    [Fact]
    public async Task РегистрацияБезСкана_ПутьПустой()
    {
        var служба = СоздатьСлужбу();

        var запись = await служба.ЗарегистрироватьAsync(
            "ВХ-БЕЗ/1", DateTime.UtcNow, "Отправитель", "Тема", IncomingMailType.Parcel);

        Assert.Null(запись.ScanPath);

        var прочитанная = await служба.ПрочитатьAsync(запись.Id);
        Assert.NotNull(прочитанная);
        Assert.Null(прочитанная!.ScanPath);
        Assert.Equal(IncomingMailType.Parcel, прочитанная.DocType);
    }
}
