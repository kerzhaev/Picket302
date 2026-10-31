using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using ТипАдреса = Picket302.Domain.Entities.AddressMainType;

namespace Picket302.Infrastructure.Data.Rows;

/// <summary>
/// Плоская строка таблицы <c>counterparties</c> (ТЗ §3.1.1).
/// </summary>
/// <remarks>
/// Адресные составляющие хранятся отдельными колонками: индекс, шесть строк и
/// исходная строка импорта. Структурированные адреса собираются при чтении
/// записи — адрес никогда не хранится одной строкой (ТЗ §3.1.19).
/// </remarks>
internal sealed class CounterpartyRow
{
    public long Id { get; set; }

    public int Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ShortName { get; set; }

    public string? Inn { get; set; }

    public string? Kpp { get; set; }

    public string? Ogrn { get; set; }

    public string? Okfs { get; set; }

    public string? Okpo { get; set; }

    public string? Okved { get; set; }

    public string? Bik { get; set; }

    public string? Ks { get; set; }

    public string? Rs { get; set; }

    public string? Rkc { get; set; }

    public string? BankName { get; set; }

    public string? PassportSeries { get; set; }

    public string? PassportNumber { get; set; }

    public string? PassportIssuedBy { get; set; }

    public DateTime? PassportIssuedDate { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? BirthPlace { get; set; }

    public string? Position { get; set; }

    public string? DirectorFio { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? SpecialNotes { get; set; }

    /// <summary>Источник появления записи (ТЗ §8.12, функция 75).</summary>
    public string? Source { get; set; }

    /// <summary>Логотип компании — растровый файл (ТЗ §8.1, функция 47).</summary>
    public byte[]? Logo { get; set; }

    public string? LegalZip { get; set; }

    public string? LegalStr1 { get; set; }

    public string? LegalStr2 { get; set; }

    public string? LegalStr3 { get; set; }

    public string? LegalStr4 { get; set; }

    public string? LegalStr5 { get; set; }

    public string? LegalStr6 { get; set; }

    public string? LegalRaw { get; set; }

    public string? PostZip { get; set; }

    public string? PostStr1 { get; set; }

    public string? PostStr2 { get; set; }

    public string? PostStr3 { get; set; }

    public string? PostStr4 { get; set; }

    public string? PostStr5 { get; set; }

    public string? PostStr6 { get; set; }

    public string? PostRaw { get; set; }

    public string? FactZip { get; set; }

    public string? FactStr1 { get; set; }

    public string? FactStr2 { get; set; }

    public string? FactStr3 { get; set; }

    public string? FactStr4 { get; set; }

    public string? FactStr5 { get; set; }

    public string? FactStr6 { get; set; }

    public string? FactRaw { get; set; }

    public int AddressMainType { get; set; }

    public long? GroupId { get; set; }

    public bool IsVip { get; set; }

    public bool IsSender { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Собирает доменную сущность из строки таблицы.</summary>
    /// <returns>Контрагент с тремя структурированными адресами.</returns>
    public Counterparty ToEntity()
    {
        var контрагент = new Counterparty
        {
            Id = Id,
            Type = (CounterpartyType)Type,
            Name = Name,
            ShortName = ShortName,
            Inn = Inn,
            Kpp = Kpp,
            Ogrn = Ogrn,
            Okfs = Okfs,
            Okpo = Okpo,
            Okved = Okved,
            Bik = Bik,
            Ks = Ks,
            Rs = Rs,
            Rkc = Rkc,
            BankName = BankName,
            PassportSeries = PassportSeries,
            PassportNumber = PassportNumber,
            PassportIssuedBy = PassportIssuedBy,
            PassportIssuedDate = PassportIssuedDate,
            BirthDate = BirthDate,
            BirthPlace = BirthPlace,
            Position = Position,
            DirectorFio = DirectorFio,
            Email = Email,
            Phone = Phone,
            SpecialNotes = SpecialNotes,
            Source = Source,
            Logo = Logo,
            AddressMainType = (ТипАдреса)AddressMainType,
            GroupId = GroupId,
            IsVip = IsVip,
            IsSender = IsSender,
            IsActive = IsActive,
            IsDeleted = IsDeleted,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };

        контрагент.SetAddress(ТипАдреса.Legal, СобратьАдрес(LegalZip, LegalRaw,
            LegalStr1, LegalStr2, LegalStr3, LegalStr4, LegalStr5, LegalStr6));
        контрагент.SetAddress(ТипАдреса.Postal, СобратьАдрес(PostZip, PostRaw,
            PostStr1, PostStr2, PostStr3, PostStr4, PostStr5, PostStr6));
        контрагент.SetAddress(ТипАдреса.Actual, СобратьАдрес(FactZip, FactRaw,
            FactStr1, FactStr2, FactStr3, FactStr4, FactStr5, FactStr6));

        return контрагент;
    }

    /// <summary>
    /// Собирает адрес из отдельных колонок. Адрес не хранится одной строкой:
    /// индекс, шесть строк и исходная строка импорта лежат раздельно.
    /// </summary>
    /// <param name="zip">Почтовый индекс.</param>
    /// <param name="raw">Исходная строка импорта.</param>
    /// <param name="строки">Шесть адресных строк.</param>
    /// <returns>Адрес либо <see langword="null"/>, если индекс не задан.</returns>
    private static Address? СобратьАдрес(string? zip, string? raw, params string?[] строки)
    {
        if (string.IsNullOrEmpty(zip))
        {
            return null;
        }

        var результат = Address.Create(
            zip,
            [
                строки.ElementAtOrDefault(0) ?? string.Empty,
                строки.ElementAtOrDefault(1) ?? string.Empty,
                строки.ElementAtOrDefault(2) ?? string.Empty,
                строки.ElementAtOrDefault(3) ?? string.Empty,
                строки.ElementAtOrDefault(4) ?? string.Empty,
                строки.ElementAtOrDefault(5) ?? string.Empty,
            ],
            raw);

        return результат.IsSuccess ? результат.Value : null;
    }
}
