using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Интеграционные проверки репозиториев на встроенной SQLite в памяти
/// (ТЗ §11.1: интеграционный тест; §11.3: «пагинация 100k, фильтры»).
/// </summary>
public sealed class RepositoryTests
{
    private static Counterparty НовыйКонтрагент(string имя, bool удалён = false) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = "7707083893",
        IsVip = false,
        IsSender = false,
        IsActive = true,
        IsDeleted = удалён,
        PostalAddress = Address.Create(
            "101000",
            [имя, "Управление делами", "ул. Тверская, 12", "корп. 2", "г. Москва", string.Empty]).Value,
    };

    [Fact]
    public async Task Контрагенты_СозданиеЧтениеОбновление()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        var идентификатор = await репозиторий.AddAsync(контрагент);

        Assert.True(идентификатор > 0);

        var прочитанный = await репозиторий.GetByIdAsync(идентификатор);

        Assert.NotNull(прочитанный);
        Assert.Equal("ООО «Техно»", прочитанный!.Name);
        Assert.Equal(CounterpartyType.LegalEntity, прочитанный.Type);
        Assert.NotNull(прочитанный.PostalAddress);
        Assert.Equal("101000", прочитанный.PostalAddress!.Zip);
        Assert.Equal("ООО «Техно»", прочитанный.PostalAddress.Line1);

        прочитанный.Name = "ООО «Техно Плюс»";
        Assert.Equal(1, await репозиторий.UpdateAsync(прочитанный));

        var обновлённый = await репозиторий.GetByIdAsync(идентификатор);
        Assert.Equal("ООО «Техно Плюс»", обновлённый!.Name);
    }

    /// <summary>
    /// Мягкое удаление: запись исключается из рабочих выборок, но остаётся в базе
    /// (ТЗ §3.1.19: «Правило мягкого удаления»).
    /// </summary>
    [Fact]
    public async Task Контрагенты_МягкоеУдалениеИсключаетЗаписьИзРабочихВыборок()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        var идентификатор = await репозиторий.AddAsync(НовыйКонтрагент("ООО «Техно»"));

        Assert.True(await репозиторий.SoftDeleteAsync(идентификатор));

        var выборка = await репозиторий.GetPagedAsync(new PageRequest(0, 50));

        Assert.Equal(0, выборка.TotalCount);
        Assert.Empty(выборка.Items);

        // Запись физически сохраняется в базе.
        var вБазе = await база.Connection.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM counterparties WHERE id = @Id;",
            new { Id = идентификатор });
        Assert.Equal(1, вБазе);

        // Возврат записи в рабочее состояние.
        Assert.True(await репозиторий.RestoreAsync(идентификатор));
        Assert.Equal(1, (await репозиторий.GetPagedAsync(new PageRequest(0, 50))).TotalCount);
    }

    /// <summary>
    /// Окончательное удаление — отдельная команда (ТЗ §3.1.19).
    /// </summary>
    [Fact]
    public async Task Контрагенты_ОкончательноеУдалениеУбираетЗаписьИзБазы()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        var идентификатор = await репозиторий.AddAsync(НовыйКонтрагент("ООО «Техно»"));

        Assert.True(await репозиторий.DeleteAsync(идентификатор));
        Assert.Null(await репозиторий.GetByIdAsync(идентификатор));
    }

    /// <summary>
    /// Постраничная выборка на объёме 100 000 записей (ТЗ §11.3).
    /// </summary>
    [Fact]
    public async Task Контрагенты_ПагинацияНа100000Записей()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        await база.Connection.ExecuteAsync("BEGIN;");
        await база.Connection.ExecuteAsync(
            """
            INSERT INTO counterparties (type, name, inn, address_main_type, is_vip, is_sender, is_active, is_deleted)
            SELECT 0, 'Контрагент ' || value, '7707083893', 1, 0, 0, 1, 0
            FROM (WITH RECURSIVE counter(value) AS (SELECT 1 UNION ALL SELECT value + 1 FROM counter WHERE value < 100000)
                  SELECT value FROM counter);
            """);
        await база.Connection.ExecuteAsync("COMMIT;");

        var всего = await база.Connection.QuerySingleAsync<long>("SELECT COUNT(*) FROM counterparties;");
        Assert.Equal(100_000, всего);

        var страница = await репозиторий.GetPagedAsync(new PageRequest(0, 100));

        Assert.Equal(100_000, страница.TotalCount);
        Assert.Equal(100, страница.Items.Count);
        Assert.Equal("Контрагент 1", страница.Items[0].Name);

        var вторая = await репозиторий.GetPagedAsync(new PageRequest(100, 100));

        Assert.Equal(100_000, вторая.TotalCount);
        Assert.Equal(100, вторая.Items.Count);
        Assert.Equal("Контрагент 101", вторая.Items[0].Name);

        var последняя = await репозиторий.GetPagedAsync(new PageRequest(99_900, 100));

        Assert.Equal(100, последняя.Items.Count);
        Assert.Equal("Контрагент 99901", последняя.Items[0].Name);
        Assert.Equal("Контрагент 100000", последняя.Items[^1].Name);
    }

    [Fact]
    public async Task Контрагенты_ФильтрыОтбораРаботают()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        await репозиторий.AddAsync(НовыйКонтрагент("ООО «Альфа»"));
        await репозиторий.AddAsync(НовыйКонтрагент("ООО «Бета»"));

        var поИмени = await репозиторий.GetPagedAsync(new PageRequest(0, 50), new CounterpartyFilter(NameFragment: "Альфа"));
        Assert.Equal(1, поИмени.TotalCount);
        Assert.Equal("ООО «Альфа»", поИмени.Items[0].Name);

        var поИнн = await репозиторий.GetPagedAsync(new PageRequest(0, 50), new CounterpartyFilter(Inn: "7707083893"));
        Assert.Equal(2, поИнн.TotalCount);
    }

    /// <summary>
    /// Ограничения схемы БД: уникальность, значения и неизменяемость
    /// (ТЗ §3.1.1–3.1.18, §3.1.19).
    /// </summary>
    [Fact]
    public async Task Ограничения_УникальностьПользовательскихПолей()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var поля = new CounterpartyFieldRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        await контрагенты.AddAsync(контрагент);

        await поля.AddAsync(new CounterpartyField
        {
            CounterpartyId = контрагент.Id,
            FieldName = "Договор",
            FieldValue = "Д-1",
        });

        var ошибка = await Assert.ThrowsAsync<SqliteException>(
            () => поля.AddAsync(new CounterpartyField
            {
                CounterpartyId = контрагент.Id,
                FieldName = "Договор",
                FieldValue = "Д-2",
            }));

        Assert.Contains("UNIQUE", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ограничения_УникальностьКэшаКодов()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var кэш = new QrCacheRepository(база.Session);

        var запись = QrCache.Create("101000", new byte[] { 1, 2, 3 }, DateTime.UtcNow);
        await кэш.AddAsync(запись);

        var ошибка = await Assert.ThrowsAsync<SqliteException>(
            () => кэш.AddAsync(QrCache.Create("101000", new byte[] { 4, 5, 6 }, DateTime.UtcNow)));

        Assert.Contains("UNIQUE", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ограничения_УникальностьПарыПринтерИФормат()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var профили = new PrinterProfileRepository(база.Session);

        await профили.AddAsync(new PrinterProfile { PrinterName = "HP LaserJet", EnvelopeFormat = "C5" });

        var ошибка = await Assert.ThrowsAsync<SqliteException>(
            () => профили.AddAsync(new PrinterProfile { PrinterName = "HP LaserJet", EnvelopeFormat = "C5" }));

        Assert.Contains("UNIQUE", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Число экземпляров отправления не может быть меньше единицы
    /// (ТЗ §3.1.3: <c>copy_count INT DEFAULT 1 CHECK (copy_count &gt;= 1)</c>).
    /// </summary>
    [Fact]
    public async Task Ограничения_ЧислоЭкземпляровНеМеньшеЕдиницы()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);

        var контрагент = НовыйКонтрагент("ООО «Техно»");
        await контрагенты.AddAsync(контрагент);

        var ошибка = await Assert.ThrowsAsync<SqliteException>(
            () => письма.AddAsync(new Mail
            {
                CounterpartyId = контрагент.Id,
                SenderId = контрагент.Id,
                CopyCount = 0,
                RecipientAddress = контрагент.PostalAddress,
            }));

        Assert.Contains("CHECK", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Количество позиции описи строго положительно (ТЗ §3.1.12).
    /// </summary>
    [Fact]
    public async Task Ограничения_КоличествоПозицииОписиПоложительно()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var описи = new InventoryRepository(база.Session);
        var позиции = new InventoryItemRepository(база.Session);

        var опись = new Inventory { Name = "Опись вложений" };
        await описи.AddAsync(опись);

        var ошибка = await Assert.ThrowsAsync<SqliteException>(
            () => позиции.AddAsync(new InventoryItem
            {
                InventoryId = опись.Id,
                Name = "Договор",
                Quantity = 0,
            }));

        Assert.Contains("CHECK", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Реестр сессий печати неизменяем: изменение и удаление записи запрещены
    /// (ТЗ §3.1.6).
    /// </summary>
    [Fact]
    public async Task Ограничения_РеестрСессийПечатиНеизменяем()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var реестр = new PrintJobRepository(база.Session);

        var идентификатор = await реестр.AddAsync(new PrintJob(
            id: 0,
            createdAt: DateTime.UtcNow,
            templateId: null,
            printerName: "HP LaserJet",
            paperSizeName: "Envelope C5",
            paperWidthMm: 229m,
            paperHeightMm: 162m,
            fontInfo: "Tahoma 9pt",
            copies: 150,
            printMode: PrintModes.BatchDirect,
            status: PrintJobStatus.Success,
            errorText: null,
            devmodeSnapshot: null));

        var изменение = await Assert.ThrowsAsync<SqliteException>(
            () => база.Connection.ExecuteAsync(
                "UPDATE print_jobs SET copies = 1 WHERE id = @Id;",
                new { Id = идентификатор }));
        Assert.Contains("print_jobs", изменение.Message, StringComparison.OrdinalIgnoreCase);

        var удаление = await Assert.ThrowsAsync<SqliteException>(
            () => база.Connection.ExecuteAsync("DELETE FROM print_jobs WHERE id = @Id;", new { Id = идентификатор }));
        Assert.Contains("print_jobs", удаление.Message, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(await реестр.GetByIdAsync(идентификатор));
    }

    /// <summary>
    /// Ровно одна группа по умолчанию: частичный уникальный индекс схемы не
    /// допускает двух таких групп (ТЗ §3.1.2).
    /// </summary>
    [Fact]
    public async Task Ограничения_ГруппаПоУмолчаниюМожетБытьТолькоОдна()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var группы = new GroupRepository(база.Session);

        var вторая = new Group { Name = "Поставщики" };
        await группы.AddAsync(вторая);

        var ошибка = await Assert.ThrowsAsync<SqliteException>(
            () => группы.UpdateAsync(new Group
            {
                Id = вторая.Id,
                Name = вторая.Name,
                IsDefault = true,
            }));

        Assert.Contains("UNIQUE", ошибка.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Миграция создаёт системную группу по умолчанию «Неразобранные»
    /// (ТЗ §3.1.2).
    /// </summary>
    [Fact]
    public async Task Ограничения_СистемнаяГруппаПоУмолчаниюСозданаМиграцией()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var группы = new GroupRepository(база.Session);

        var все = await группы.GetAllAsync();
        var поУмолчанию = await группы.GetDefaultAsync();

        Assert.Single(все);
        Assert.NotNull(поУмолчанию);
        Assert.Equal(Group.DefaultGroupName, поУмолчанию!.Name);
        Assert.True(поУмолчанию.IsDefault);
    }

    /// <summary>
    /// Переключение группы по умолчанию оставляет ровно одну такую группу.
    /// </summary>
    [Fact]
    public async Task Ограничения_СменаГруппыПоУмолчаниюОставляетОдну()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var группы = new GroupRepository(база.Session);

        var вторая = new Group { Name = "Поставщики" };
        await группы.AddAsync(вторая);

        Assert.True(await группы.SetDefaultAsync(вторая.Id));

        var все = await группы.GetAllAsync();
        Assert.Single(все, группа => группа.IsDefault);
        Assert.Equal(вторая.Id, (await группы.GetDefaultAsync())!.Id);
    }
}
