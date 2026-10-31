using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка связи писем с папками, логотипа контрагента и пресечения
/// циклических ссылок в дереве папок (ТЗ §3.1.11; §8.1, функции 22 и 47).
/// </summary>
public sealed class FolderAndLogoTests
{
    private static Counterparty НовыйКонтрагент(string имя) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = "7707083893",
        IsActive = true,
        PostalAddress = Address.Create(
            "101000",
            [имя, "Управление делами", "ул. Тверская, 12", string.Empty, "г. Москва", string.Empty]).Value,
    };

    private static Mail НовоеОтправление(long контрагент) => new()
    {
        CounterpartyId = контрагент,
        SenderId = контрагент,
        CopyCount = 1,
        Status = MailStatus.Draft,
        RecipientAddress = Address.Create(
            "101000",
            ["ООО «Техно»", string.Empty, "ул. Тверская, 12", string.Empty, "г. Москва", string.Empty]).Value,
    };

    /// <summary>
    /// ТЗ §8.1, функция 22: «Таблица folders, колонка mails.folder_id» — связь
    /// письма с папкой сохраняется и читается из базы.
    /// </summary>
    [Fact]
    public async Task Письмо_СохраняетСсылкуНаПапку()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var папки = new FolderRepository(база.Session);
        var письма = new MailRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        await контрагенты.AddAsync(контрагент);

        var папка = new Folder { Name = "Реестры" };
        var идентификаторПапки = await папки.AddAsync(папка);
        Assert.True(идентификаторПапки > 0);

        var письмо = НовоеОтправление(контрагент.Id);
        письмо.MoveToFolder(идентификаторПапки);
        await письма.AddAsync(письмо);

        var прочитанное = await письма.GetByIdAsync(письмо.Id);

        Assert.NotNull(прочитанное);
        Assert.Equal(идентификаторПапки, прочитанное!.FolderId);
    }

    /// <summary>
    /// ТЗ §8.1, функция 22: счётчик вложенности папки равен числу писем,
    /// отнесённых к ней через <c>mails.folder_id</c>.
    /// </summary>
    [Fact]
    public async Task Папка_СчитаетВложенныеПисьма()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var папки = new FolderRepository(база.Session);
        var письма = new MailRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        await контрагенты.AddAsync(контрагент);

        var папка = new Folder { Name = "Реестры" };
        await папки.AddAsync(папка);

        await письма.AddAsync(НовоеОтправление(контрагент.Id));

        var второе = НовоеОтправление(контрагент.Id);
        второе.MoveToFolder(папка.Id);
        await письма.AddAsync(второе);

        Assert.Equal(1, await папки.CountMailsAsync(папка.Id));
    }

    /// <summary>
    /// Ссылка письма на папку контролируется внешним ключом: письмо нельзя
    /// отнести к несуществующей папке.
    /// </summary>
    [Fact]
    public async Task Письмо_НельзяОтнестиКНесуществующейПапке()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        await контрагенты.AddAsync(контрагент);

        var письмо = НовоеОтправление(контрагент.Id);
        письмо.MoveToFolder(9999);

        await Assert.ThrowsAsync<SqliteException>(() => письма.AddAsync(письмо));
    }

    /// <summary>
    /// ТЗ §8.1, функция 47: «Таблица counterparties (колонка logo)» — логотип
    /// контрагента сохраняется и читается из базы.
    /// </summary>
    [Fact]
    public async Task Контрагент_СохраняетЛоготип()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        byte[] логотип = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        контрагент.Logo = логотип;

        var идентификатор = await репозиторий.AddAsync(контрагент);
        var прочитанный = await репозиторий.GetByIdAsync(идентификатор);

        Assert.NotNull(прочитанный!.Logo);
        Assert.Equal(логотип, прочитанный.Logo);

        прочитанный.Logo = null;
        Assert.Equal(1, await репозиторий.UpdateAsync(прочитанный));

        var безЛоготипа = await репозиторий.GetByIdAsync(идентификатор);
        Assert.Null(безЛоготипа!.Logo);
    }

    /// <summary>
    /// ТЗ §3.1.11: «циклические ссылки parent_id пресекаются триггером/валидатором
    /// домена» — перемещение папки внутрь собственного потомка отклоняется
    /// хранилищем.
    /// </summary>
    [Fact]
    public async Task Папка_ЦиклическаяСсылкаОтклоняетсяРепозиторием()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var папки = new FolderRepository(база.Session);

        var корень = new Folder { Name = "Корень" };
        await папки.AddAsync(корень);

        var потомок = new Folder { Name = "Потомок", ParentId = корень.Id };
        await папки.AddAsync(потомок);

        // Родителем корня назначается его собственный потомок.
        корень.ParentId = потомок.Id;

        var ошибка = await Assert.ThrowsAsync<InvalidOperationException>(() => папки.UpdateAsync(корень));

        Assert.Contains("поддереве", ошибка.Message, StringComparison.OrdinalIgnoreCase);

        var неИзменённая = await папки.GetByIdAsync(корень.Id);
        Assert.Null(неИзменённая!.ParentId);
    }

    /// <summary>
    /// ТЗ §3.1.11: то же ограничение обеспечивается триггером схемы — даже
    /// прямой SQL-запрос не может создать цикл в дереве папок.
    /// </summary>
    [Fact]
    public async Task Папка_ЦиклическаяСсылкаОтклоняетсяТриггеромСхемы()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var кореньId = await база.Connection.QuerySingleAsync<long>(
            "INSERT INTO folders (name) VALUES ('Корень'); SELECT last_insert_rowid();");

        var потомокId = await база.Connection.QuerySingleAsync<long>(
            "INSERT INTO folders (name, parent_id) VALUES ('Потомок', @ParentId); SELECT last_insert_rowid();",
            new { ParentId = кореньId });

        var ошибка = await Assert.ThrowsAsync<SqliteException>(() => база.Connection.ExecuteAsync(
            "UPDATE folders SET parent_id = @ParentId WHERE id = @Id;",
            new { ParentId = потомокId, Id = кореньId }));

        Assert.Contains("Циклическая ссылка", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ТЗ §3.1.11: цикл в дереве папок пресекается и на уровне доменной
    /// политики — служба возвращает текст нарушения, а не исключение.
    /// </summary>
    [Fact]
    public async Task СлужбаПапок_ПеремещениеВПотомкаОтклоняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new FolderRepository(база.Session);
        var служба = new FolderService(репозиторий, база.UnitOfWork);

        var корень = await служба.CreateAsync("Корень");
        Assert.True(корень.IsSuccess, корень.Error);

        var потомок = await служба.CreateAsync("Потомок", корень.Value!.Id);
        Assert.True(потомок.IsSuccess, потомок.Error);

        var перемещение = await служба.MoveAsync(корень.Value.Id, потомок.Value!.Id);

        Assert.True(перемещение.IsFailure);
        Assert.Contains("поддереве", перемещение.Error!, StringComparison.OrdinalIgnoreCase);

        var неИзменённый = await репозиторий.GetByIdAsync(корень.Value.Id);
        Assert.Null(неИзменённый!.ParentId);
    }

    /// <summary>
    /// ТЗ §3.1.11: перемещение папки в допустимый родитель выполняется.
    /// </summary>
    [Fact]
    public async Task СлужбаПапок_ПеремещениеВДопустимыйРодительВыполняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new FolderRepository(база.Session);
        var служба = new FolderService(репозиторий, база.UnitOfWork);

        var корень = await служба.CreateAsync("Корень");
        var лист = await служба.CreateAsync("Лист");

        var перемещение = await служба.MoveAsync(лист.Value!.Id, корень.Value!.Id);

        Assert.True(перемещение.IsSuccess, перемещение.Error);
        Assert.Equal(корень.Value!.Id, перемещение.Value!.ParentId);

        var переименование = await служба.RenameAsync(лист.Value!.Id, "Лист (переименован)");
        Assert.True(переименование.IsSuccess, переименование.Error);
        Assert.Equal("Лист (переименован)", переименование.Value!.Name);
    }

    /// <summary>
    /// ТЗ §8.1, функция 22: служба отправлений переносит письмо в папку.
    /// </summary>
    [Fact]
    public async Task СлужбаОтправлений_ПеремещаетПисьмоВПапку()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var папки = new FolderRepository(база.Session);
        var письма = new MailRepository(база.Session);
        var журнал = new PrintHistoryRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        await контрагенты.AddAsync(контрагент);

        var папка = new Folder { Name = "Реестры" };
        await папки.AddAsync(папка);

        var письмо = НовоеОтправление(контрагент.Id);
        await письма.AddAsync(письмо);

        var служба = new MailService(письма, контрагенты, журнал, база.UnitOfWork);
        var перемещение = await служба.MoveToFolderAsync(письмо.Id, папка.Id);

        Assert.True(перемещение.IsSuccess, перемещение.Error);
        Assert.Equal(папка.Id, перемещение.Value!.FolderId);

        var прочитанное = await письма.GetByIdAsync(письмо.Id);
        Assert.Equal(папка.Id, прочитанное!.FolderId);
    }
}
