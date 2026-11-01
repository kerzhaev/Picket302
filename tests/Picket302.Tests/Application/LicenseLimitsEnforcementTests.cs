using Picket302.Application.Services;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Repositories;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Application;

/// <summary>
/// Применение лимитов пробного периода в точках создания записей и печати
/// партий (ТЗ §8.10, функция 119: «создание записей и печать партий
/// блокируются с понятным сообщением и оставшимися днями»).
/// </summary>
public sealed class LicenseLimitsEnforcementTests
{
    /// <summary>
    /// Ворота, запрещающие операции: используется в проверках ограничений.
    /// </summary>
    private sealed class ЗапрещающиеВорота : ILicenseGate
    {
        public LicenseStatus ТекущийСтатус { get; } = new(
            LicenseState.Expired,
            Edition.Free,
            null,
            null,
            null,
            0,
            "Пробный период закончился.");

        public LicenseGateDecision Проверить(LicenseOperation операция)
            => LicenseGateDecision.Запретить(
                LicenseState.Expired,
                0,
                "Пробный период закончился. Создание записей недоступно. Осталось дней: 0.");
    }

    /// <summary>
    /// Ворота, разрешающие операции.
    /// </summary>
    private sealed class РазрешающиеВорота : ILicenseGate
    {
        public LicenseStatus ТекущийСтатус { get; } = new(
            LicenseState.Trial,
            Edition.Free,
            null,
            null,
            null,
            7,
            null);

        public LicenseGateDecision Проверить(LicenseOperation операция)
            => LicenseGateDecision.Разрешить(LicenseState.Trial, 7);
    }

    private static Counterparty Контрагент() => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = "ООО «Техно»",
        Inn = "7707083893",
        CreatedAt = DateTime.UtcNow,
        PostalAddress = Address.Create(
            "101000",
            ["ООО «Техно»", "г. Москва", "ул. Тверская, 12", string.Empty, string.Empty, string.Empty]).Value,
    };

    [Fact]
    public async Task CounterpartyService_ПробныйПериодИстёк_НоваяКарточкаНеСоздаётся()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var служба = new CounterpartyService(контрагенты, база.UnitOfWork, new ЗапрещающиеВорота());

        var результат = await служба.SaveAsync(Контрагент());

        Assert.True(результат.IsFailure);
        Assert.Contains("Пробный период закончился", результат.Error!, StringComparison.Ordinal);
        Assert.Contains("Осталось дней: 0", результат.Error!, StringComparison.Ordinal);

        var страница = await контрагенты.GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 10));
        Assert.Equal(0, страница.TotalCount);
    }

    [Fact]
    public async Task CounterpartyService_ПробныйПериодДействует_КарточкаСоздаётся()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var служба = new CounterpartyService(контрагенты, база.UnitOfWork, new РазрешающиеВорота());

        var результат = await служба.SaveAsync(Контрагент());

        Assert.True(результат.IsSuccess, результат.Error);
        Assert.NotEqual(0, результат.Value!.Id);
    }

    [Fact]
    public async Task MailService_ПробныйПериодИстёк_ОтправлениеНеСоздаётся()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var письма = new MailRepository(база.Session);
        var журнал = new PrintHistoryRepository(база.Session);
        var служба = new MailService(письма, контрагенты, журнал, база.UnitOfWork, new ЗапрещающиеВорота());

        var отправление = new Mail
        {
            CounterpartyId = 1,
            SenderId = 1,
            CopyCount = 1,
            DateCreated = DateTime.UtcNow,
            RecipientAddress = Address.Create(
                "101000",
                ["ООО «Техно»", "г. Москва", "ул. Тверская, 12", string.Empty, string.Empty, string.Empty]).Value,
        };

        var результат = await служба.CreateAsync(отправление);

        Assert.True(результат.IsFailure);
        Assert.Contains("Пробный период закончился", результат.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportMergeService_ПробныйПериодИстёк_ИмпортНеВыполняется()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        var контрагенты = new CounterpartyRepository(база.Session);
        var служба = new ImportMergeService(контрагенты, null, new ЗапрещающиеВорота());

        var пакет = new Picket302.Domain.Contracts.ImportBatch
        {
            SourceFormat = "CSV",
            SourceFile = "данные.csv",
        };

        пакет.Records.Add(new Picket302.Domain.Contracts.ImportRecord
        {
            RowNumber = 1,
            Status = Picket302.Domain.Contracts.ImportRecordStatus.Unverified,
            Draft = new Picket302.Domain.Contracts.CounterpartyDraft { Name = "ООО «Техно»" },
        });

        var исключение = await Assert.ThrowsAsync<LicenseLimitExceededException>(
            () => служба.MergeImportedDetailed(пакет, Picket302.Domain.Contracts.MergePolicy.SkipExisting));

        Assert.Contains("Осталось дней: 0", исключение.Message, StringComparison.Ordinal);

        var страница = await контрагенты.GetPagedAsync(new Picket302.Domain.Common.PageRequest(0, 10));
        Assert.Equal(0, страница.TotalCount);
    }
}
