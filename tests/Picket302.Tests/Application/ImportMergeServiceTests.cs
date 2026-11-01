using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверки тонкого слияния импортируемых данных с существующей базой
/// (ТЗ §8.12, функция 74: «Тонкое слияние импортируемых данных с существующей
/// базой»).
/// </summary>
public sealed class ImportMergeServiceTests
{
    private static ImportBatch Пакет(params CounterpartyDraft[] черновики)
    {
        var пакет = new ImportBatch
        {
            SourceFormat = "CSV",
            SourceFile = "export.csv",
            ImportedAt = new DateTime(2024, 5, 17, 10, 30, 0),
        };

        var номер = 1;

        foreach (var черновик in черновики)
        {
            пакет.Records.Add(new ImportRecord
            {
                RowNumber = номер++,
                Draft = черновик,
            });
        }

        return пакет;
    }

    private static CounterpartyDraft Черновик(
        string имя,
        string инн,
        string? телефон = null,
        string? почта = null,
        string индекс = "101000")
        => new()
        {
            Type = CounterpartyType.LegalEntity,
            Name = имя,
            Inn = инн,
            Phone = телефон,
            Email = почта,
            PostalZip = индекс,
            PostalLines =
            [
                имя,
                string.Empty,
                "ул. Тверская, 12",
                string.Empty,
                "г. Москва",
                string.Empty,
            ],
        };

    [Fact]
    public async Task ПовторныйИмпортСПолитикойПропуститьСуществующие_НеУвеличиваетЧислоЗаписей()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);
        var служба = new ImportMergeService(репозиторий);

        var первый = служба.MergeImported(
            Пакет(Черновик("ООО «Ромашка»", "7707083893")),
            MergePolicy.SkipExisting);

        Assert.Equal(1, первый.Created);
        Assert.Equal(0, первый.Skipped);

        var второй = служба.MergeImported(
            Пакет(Черновик("ООО «Ромашка»", "7707083893")),
            MergePolicy.SkipExisting);

        Assert.Equal(0, второй.Created);
        Assert.Equal(1, второй.Skipped);
        Assert.Equal(1, второй.Conflicts);

        var выборка = await репозиторий.GetPagedAsync(new PageRequest(0, 50));

        Assert.Equal(1, выборка.TotalCount);
    }

    [Fact]
    public async Task ПолитикаДополнитьПустые_ЗаполняетТолькоПустыеПоляИФиксируетКонфликт()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        var существующий = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Ромашка»",
            Inn = "7707083893",
            Phone = "+7-495-111-11-11",
            IsActive = true,
        };

        await репозиторий.AddAsync(существующий);

        var служба = new ImportMergeService(репозиторий);

        var отчёт = служба.MergeImported(
            Пакет(Черновик(
                "ООО «Ромашка»",
                "7707083893",
                телефон: "+7-495-222-22-22",
                почта: "info@romashka.ru")),
            MergePolicy.FillEmpty);

        Assert.Equal(0, отчёт.Created);
        Assert.Equal(1, отчёт.Updated);
        Assert.Equal(1, отчёт.Conflicts);
        Assert.Contains(отчёт.ConflictDetails, деталь => деталь.Contains("Телефон"));

        var прочитанный = await репозиторий.FindByMergeKeyAsync("7707083893", "ООО «Ромашка»", null);

        Assert.NotNull(прочитанный);
        Assert.Equal("+7-495-111-11-11", прочитанный!.Phone);
        Assert.Equal("info@romashka.ru", прочитанный.Email);
    }

    [Fact]
    public async Task ПолитикаПерезаписать_ЗаменяетНепустоеЗначение()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        var существующий = new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Ромашка»",
            Inn = "7707083893",
            Phone = "+7-495-111-11-11",
            IsActive = true,
        };

        await репозиторий.AddAsync(существующий);

        var служба = new ImportMergeService(репозиторий);

        var отчёт = служба.MergeImported(
            Пакет(Черновик(
                "ООО «Ромашка»",
                "7707083893",
                телефон: "+7-495-222-22-22",
                почта: "info@romashka.ru")),
            MergePolicy.Overwrite);

        Assert.Equal(0, отчёт.Created);
        Assert.Equal(1, отчёт.Updated);

        var прочитанный = await репозиторий.FindByMergeKeyAsync("7707083893", "ООО «Ромашка»", null);

        Assert.NotNull(прочитанный);
        Assert.Equal("+7-495-222-22-22", прочитанный!.Phone);
        Assert.Equal("info@romashka.ru", прочитанный.Email);
    }

    [Fact]
    public async Task СозданнаяЗапись_ПолучаетИсточникДанныхПакета()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);
        var служба = new ImportMergeService(репозиторий);

        var пакет = Пакет(Черновик("ООО «Ромашка»", "7707083893"));

        служба.MergeImported(пакет, MergePolicy.SkipExisting);

        var прочитанный = await репозиторий.FindByMergeKeyAsync("7707083893", "ООО «Ромашка»", null);

        Assert.NotNull(прочитанный);
        Assert.Equal(пакет.SourceDescription, прочитанный!.Source);
        Assert.Equal("Импорт CSV от 17.05.2024 из export.csv", прочитанный.Source);
    }

    [Fact]
    public void КлючСлияния_СтроитсяПоИннИлиНаименованиюСИндексом()
    {
        Assert.Equal(
            "ИНН:7707083893",
            ImportMergeService.BuildMergeKey(Черновик("ООО «Ромашка»", "7707083893")));

        Assert.Equal(
            "НАИМЕНОВАНИЕ:ООО РОМАШКА|ИНДЕКС:101000",
            ImportMergeService.BuildMergeKey(Черновик("ООО  «Ромашка»", string.Empty)));
    }
}
