using Dapper;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Data.Rows;

using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Repositories;

/// <summary>
/// Репозиторий контрагентов на SQLite (ТЗ §3.1.1).
/// </summary>
public sealed class CounterpartyRepository : ICounterpartyRepository
{
    private const string КолонкиВставки = """
        type, name, short_name, inn, kpp, ogrn, okfs, okpo, okved, bik, ks, rs, rkc, bank_name,
        passport_series, passport_number, passport_issued_by, passport_issued_date,
        birth_date, birth_place, position, director_fio, email, phone, special_notes,
        legal_zip, legal_str1, legal_str2, legal_str3, legal_str4, legal_str5, legal_str6, legal_raw,
        post_zip, post_str1, post_str2, post_str3, post_str4, post_str5, post_str6, post_raw,
        fact_zip, fact_str1, fact_str2, fact_str3, fact_str4, fact_str5, fact_str6, fact_raw,
        address_main_type, group_id, is_vip, is_sender, is_active, is_deleted, created_at, updated_at, logo
        """;

    private readonly ISqliteSession _сессия;

    /// <summary>Создаёт репозиторий контрагентов.</summary>
    /// <param name="сессия">Сессия базы данных.</param>
    public CounterpartyRepository(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);
        _сессия = сессия;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(Counterparty counterparty, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(counterparty);

        var момент = DateTime.UtcNow;
        counterparty.CreatedAt = counterparty.CreatedAt == default ? момент : counterparty.CreatedAt;
        counterparty.UpdatedAt = момент;

        var sql = $"""
            INSERT INTO counterparties ({КолонкиВставки})
            VALUES (
                @Type, @Name, @ShortName, @Inn, @Kpp, @Ogrn, @Okfs, @Okpo, @Okved, @Bik, @Ks, @Rs, @Rkc, @BankName,
                @PassportSeries, @PassportNumber, @PassportIssuedBy, @PassportIssuedDate,
                @BirthDate, @BirthPlace, @Position, @DirectorFio, @Email, @Phone, @SpecialNotes,
                @LegalZip, @LegalStr1, @LegalStr2, @LegalStr3, @LegalStr4, @LegalStr5, @LegalStr6, @LegalRaw,
                @PostZip, @PostStr1, @PostStr2, @PostStr3, @PostStr4, @PostStr5, @PostStr6, @PostRaw,
                @FactZip, @FactStr1, @FactStr2, @FactStr3, @FactStr4, @FactStr5, @FactStr6, @FactRaw,
                @AddressMainType, @GroupId, @IsVip, @IsSender, @IsActive, @IsDeleted, @CreatedAt, @UpdatedAt, @Logo);
            SELECT last_insert_rowid();
            """;

        var идентификатор = await _сессия.ExecuteScalarAsync<long>(
            sql,
            Параметры(counterparty),
            cancellationToken).ConfigureAwait(false);

        counterparty.Id = идентификатор;
        return идентификатор;
    }

    /// <inheritdoc />
    public async Task<Counterparty?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var строка = await _сессия.QueryFirstOrDefaultAsync<CounterpartyRow>(
            "SELECT * FROM counterparties WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return строка?.ToEntity();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(Counterparty counterparty, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(counterparty);

        counterparty.UpdatedAt = DateTime.UtcNow;

        var sql = $"""
            UPDATE counterparties SET
                type = @Type, name = @Name, short_name = @ShortName, inn = @Inn, kpp = @Kpp, ogrn = @Ogrn,
                okfs = @Okfs, okpo = @Okpo, okved = @Okved, bik = @Bik, ks = @Ks, rs = @Rs, rkc = @Rkc,
                bank_name = @BankName, passport_series = @PassportSeries, passport_number = @PassportNumber,
                passport_issued_by = @PassportIssuedBy, passport_issued_date = @PassportIssuedDate,
                birth_date = @BirthDate, birth_place = @BirthPlace, position = @Position,
                director_fio = @DirectorFio, email = @Email, phone = @Phone, special_notes = @SpecialNotes,
                legal_zip = @LegalZip, legal_str1 = @LegalStr1, legal_str2 = @LegalStr2, legal_str3 = @LegalStr3,
                legal_str4 = @LegalStr4, legal_str5 = @LegalStr5, legal_str6 = @LegalStr6, legal_raw = @LegalRaw,
                post_zip = @PostZip, post_str1 = @PostStr1, post_str2 = @PostStr2, post_str3 = @PostStr3,
                post_str4 = @PostStr4, post_str5 = @PostStr5, post_str6 = @PostStr6, post_raw = @PostRaw,
                fact_zip = @FactZip, fact_str1 = @FactStr1, fact_str2 = @FactStr2, fact_str3 = @FactStr3,
                fact_str4 = @FactStr4, fact_str5 = @FactStr5, fact_str6 = @FactStr6, fact_raw = @FactRaw,
                address_main_type = @AddressMainType, group_id = @GroupId, is_vip = @IsVip,
                is_sender = @IsSender, is_active = @IsActive, is_deleted = @IsDeleted, updated_at = @UpdatedAt,
                logo = @Logo
            WHERE id = @Id;
            """;

        var параметры = Параметры(counterparty);
        параметры.Add("Id", counterparty.Id);

        return await _сессия.ExecuteAsync(sql, параметры, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "UPDATE counterparties SET is_deleted = 1, updated_at = @UpdatedAt WHERE id = @Id AND is_deleted = 0;",
            new { Id = id, UpdatedAt = DateTime.UtcNow },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "UPDATE counterparties SET is_deleted = 0, updated_at = @UpdatedAt WHERE id = @Id AND is_deleted = 1;",
            new { Id = id, UpdatedAt = DateTime.UtcNow },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var затронуто = await _сессия.ExecuteAsync(
            "DELETE FROM counterparties WHERE id = @Id;",
            new { Id = id },
            cancellationToken).ConfigureAwait(false);

        return затронуто > 0;
    }

    /// <inheritdoc />
    public async Task<Page<Counterparty>> GetPagedAsync(
        PageRequest request,
        CounterpartyFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var нормализованный = request.Normalized();
        var условия = new List<string>();
        var параметры = new DynamicParameters();

        // Мягко удалённые записи исключаются из рабочих выборок по умолчанию
        // (ТЗ §3.1.19: «Правило мягкого удаления»).
        if (filter is null || !filter.IncludeDeleted)
        {
            условия.Add("is_deleted = 0");
        }

        if (filter is not null)
        {
            if (!string.IsNullOrWhiteSpace(filter.NameFragment))
            {
                условия.Add("name LIKE @NameFragment");
                параметры.Add("NameFragment", $"%{filter.NameFragment}%");
            }

            if (!string.IsNullOrWhiteSpace(filter.Inn))
            {
                условия.Add("inn = @Inn");
                параметры.Add("Inn", filter.Inn);
            }

            if (filter.GroupId is not null)
            {
                условия.Add("group_id = @GroupId");
                параметры.Add("GroupId", filter.GroupId.Value);
            }

            if (filter.IsVip is not null)
            {
                условия.Add("is_vip = @IsVip");
                параметры.Add("IsVip", filter.IsVip.Value);
            }

            if (filter.IsSender is not null)
            {
                условия.Add("is_sender = @IsSender");
                параметры.Add("IsSender", filter.IsSender.Value);
            }

            if (filter.IsActive is not null)
            {
                условия.Add("is_active = @IsActive");
                параметры.Add("IsActive", filter.IsActive.Value);
            }
        }

        var где = условия.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", условия);

        var всего = await _сессия.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM counterparties{где};",
            параметры,
            cancellationToken).ConfigureAwait(false);

        if (всего == 0)
        {
            return Page<Counterparty>.Empty(нормализованный);
        }

        параметры.Add("Limit", нормализованный.Limit);
        параметры.Add("Offset", нормализованный.Offset);

        var строки = await _сессия.QueryAsync<CounterpartyRow>(
            $"SELECT * FROM counterparties{где} ORDER BY id LIMIT @Limit OFFSET @Offset;",
            параметры,
            cancellationToken).ConfigureAwait(false);

        var элементы = строки.Select(строка => строка.ToEntity()).ToList();

        return new Page<Counterparty>(элементы, нормализованный.Offset, нормализованный.Limit, всего);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Counterparty>> GetByGroupAsync(
        long groupId,
        CancellationToken cancellationToken = default)
    {
        var строки = await _сессия.QueryAsync<CounterpartyRow>(
            "SELECT * FROM counterparties WHERE group_id = @GroupId AND is_deleted = 0 ORDER BY name, id;",
            new { GroupId = groupId },
            cancellationToken).ConfigureAwait(false);

        return строки.Select(строка => строка.ToEntity()).ToList();
    }

    /// <summary>
    /// Формирует параметры команды записи контрагента: адресные составляющие
    /// передаются отдельными колонками.
    /// </summary>
    /// <param name="counterparty">Сохраняемый контрагент.</param>
    /// <returns>Набор параметров команды.</returns>
    private static DynamicParameters Параметры(Counterparty counterparty)
    {
        var параметры = new DynamicParameters();
        параметры.Add("Type", (int)counterparty.Type);
        параметры.Add("Name", counterparty.Name);
        параметры.Add("ShortName", counterparty.ShortName);
        параметры.Add("Inn", counterparty.Inn);
        параметры.Add("Kpp", counterparty.Kpp);
        параметры.Add("Ogrn", counterparty.Ogrn);
        параметры.Add("Okfs", counterparty.Okfs);
        параметры.Add("Okpo", counterparty.Okpo);
        параметры.Add("Okved", counterparty.Okved);
        параметры.Add("Bik", counterparty.Bik);
        параметры.Add("Ks", counterparty.Ks);
        параметры.Add("Rs", counterparty.Rs);
        параметры.Add("Rkc", counterparty.Rkc);
        параметры.Add("BankName", counterparty.BankName);
        параметры.Add("PassportSeries", counterparty.PassportSeries);
        параметры.Add("PassportNumber", counterparty.PassportNumber);
        параметры.Add("PassportIssuedBy", counterparty.PassportIssuedBy);
        параметры.Add("PassportIssuedDate", counterparty.PassportIssuedDate);
        параметры.Add("BirthDate", counterparty.BirthDate);
        параметры.Add("BirthPlace", counterparty.BirthPlace);
        параметры.Add("Position", counterparty.Position);
        параметры.Add("DirectorFio", counterparty.DirectorFio);
        параметры.Add("Email", counterparty.Email);
        параметры.Add("Phone", counterparty.Phone);
        параметры.Add("SpecialNotes", counterparty.SpecialNotes);
        параметры.Add("AddressMainType", (int)counterparty.AddressMainType);
        параметры.Add("GroupId", counterparty.GroupId);
        параметры.Add("IsVip", counterparty.IsVip);
        параметры.Add("IsSender", counterparty.IsSender);
        параметры.Add("IsActive", counterparty.IsActive);
        параметры.Add("IsDeleted", counterparty.IsDeleted);
        параметры.Add("CreatedAt", counterparty.CreatedAt);
        параметры.Add("UpdatedAt", counterparty.UpdatedAt);
        параметры.Add("Logo", counterparty.Logo);

        ДобавитьАдрес(параметры, "Legal", counterparty.LegalAddress);
        ДобавитьАдрес(параметры, "Post", counterparty.PostalAddress);
        ДобавитьАдрес(параметры, "Fact", counterparty.ActualAddress);

        return параметры;
    }

    /// <summary>
    /// Добавляет параметры адресных колонок: индекс, шесть строк и исходную строку.
    /// </summary>
    /// <param name="параметры">Набор параметров команды.</param>
    /// <param name="префикс">Префикс имён колонок адреса.</param>
    /// <param name="адрес">Адрес контрагента.</param>
    private static void ДобавитьАдрес(DynamicParameters параметры, string префикс, Address? адрес)
    {
        параметры.Add($"{префикс}Zip", адрес?.Zip);
        параметры.Add($"{префикс}Str1", адрес?.Line1);
        параметры.Add($"{префикс}Str2", адрес?.Line2);
        параметры.Add($"{префикс}Str3", адрес?.Line3);
        параметры.Add($"{префикс}Str4", адрес?.Line4);
        параметры.Add($"{префикс}Str5", адрес?.Line5);
        параметры.Add($"{префикс}Str6", адрес?.Line6);
        параметры.Add($"{префикс}Raw", адрес?.Raw);
    }
}
