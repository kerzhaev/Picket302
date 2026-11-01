using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Проверки дедупликации импорта и признака источника данных контрагента
/// (ТЗ §8.12, функция 75: «Дедупликация импорта и источник данных каждого
/// контрагента»).
/// </summary>
public sealed class ImportDeduplicatorTests
{
    private const string ОписаниеИсточника = "Импорт CSV от 17.05.2024 из export.csv";

    private static CounterpartyDraft Черновик(string имя, string инн, string? источник = null) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = имя,
        Inn = инн,
        Source = источник,
    };

    [Fact]
    public async Task ДубликатВнутриПакета_Пропускается()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var дедупликатор = new ImportDeduplicator(new CounterpartyRepository(база.Session));

        var результат = дедупликатор.FilterDuplicates(
            [
                Черновик("ООО «Ромашка»", "7707083893"),
                Черновик("ООО «Ромашка»", "7707083893"),
                Черновик("ООО «Ромашка»", string.Empty),
            ],
            MergePolicy.SkipExisting,
            ОписаниеИсточника);

        Assert.Equal(2, результат.Accepted.Count);
        Assert.Single(результат.SkippedDuplicates);
        Assert.Equal("ООО «Ромашка»", результат.SkippedDuplicates[0].Name);
    }

    [Fact]
    public async Task ПринятаяЗапись_ПолучаетПризнакИсточникаДанных()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var дедупликатор = new ImportDeduplicator(new CounterpartyRepository(база.Session));

        var результат = дедупликатор.FilterDuplicates(
            [Черновик("ООО «Ромашка»", "7707083893")],
            MergePolicy.SkipExisting,
            ОписаниеИсточника);

        var запись = Assert.Single(результат.Accepted);

        Assert.Equal(ОписаниеИсточника, запись.Source);
        Assert.Matches(@"^Импорт .+ от \d{2}\.\d{2}\.\d{4} из .+$", запись.Source!);
    }

    [Fact]
    public void ОписаниеИсточника_ФормируетсяПоФорматуДатеИФайлу()
    {
        var описание = ImportDeduplicator.BuildSourceDescription(
            "XLSX",
            new DateTime(2024, 5, 17, 9, 0, 0),
            "адреса.xlsx");

        Assert.Equal("Импорт XLSX от 17.05.2024 из адреса.xlsx", описание);
    }

    [Fact]
    public async Task ЗаписьУжеВБазе_ПропускаетсяПриПолитикеПропуститьСуществующие()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        await репозиторий.AddAsync(new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Ромашка»",
            Inn = "7707083893",
            IsActive = true,
        });

        var дедупликатор = new ImportDeduplicator(репозиторий);

        var результат = дедупликатор.FilterDuplicates(
            [Черновик("ООО «Ромашка»", "7707083893")],
            MergePolicy.SkipExisting,
            ОписаниеИсточника);

        Assert.Empty(результат.Accepted);
        Assert.Single(результат.SkippedDuplicates);
        Assert.Equal(0, результат.UpdatedCount);
    }

    [Fact]
    public async Task ЗаписьУжеВБазе_ПринимаетсяПриПолитикеОбновления()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var репозиторий = new CounterpartyRepository(база.Session);

        await репозиторий.AddAsync(new Counterparty
        {
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Ромашка»",
            Inn = "7707083893",
            IsActive = true,
        });

        var дедупликатор = new ImportDeduplicator(репозиторий);

        var результат = дедупликатор.FilterDuplicates(
            [Черновик("ООО «Ромашка»", "7707083893")],
            MergePolicy.Overwrite,
            ОписаниеИсточника);

        Assert.Single(результат.Accepted);
        Assert.Empty(результат.SkippedDuplicates);
        Assert.Equal(1, результат.UpdatedCount);
    }

    [Fact]
    public async Task ПовторВПакете_СверяетсяПоНормализованномуНаименованиюИИндексу()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var дедупликатор = new ImportDeduplicator(new CounterpartyRepository(база.Session));

        var первый = Черновик("ООО «Ромашка»", string.Empty);
        первый.PostalZip = "101000";

        var второй = Черновик("ООО  РОМАШКА", string.Empty);
        второй.PostalZip = "101000";

        var результат = дедупликатор.FilterDuplicates(
            [первый, второй],
            MergePolicy.SkipExisting,
            ОписаниеИсточника);

        Assert.Single(результат.Accepted);
        Assert.Single(результат.SkippedDuplicates);
    }
}
