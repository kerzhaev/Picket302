using Dapper;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
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
        birth_date, birth_place, position, director_fio, email, phone, special_notes, source,
        legal_zip, legal_str1, legal_str2, legal_str3, legal_str4, legal_str5, legal_str6, legal_raw,
        post_zip, post_str1, post_str2, post_str3, post_str4, post_str5, post_str6, post_raw,
        fact_zip, fact_str1, fact_str2, fact_str3, fact_str4, fact_str5, fact_str6, fact_raw,
        address_main_type, group_id, is_vip, is_sender, is_active, is_deleted, created_at, updated_at, logo
        """;

    private readonly ISqliteSession _сессия;

    /// <summary>
    /// Белый список выражений сортировки: ключ колонки адресной книги — выражение
    /// SQL (ТЗ §8.1, функция 9: «сортировка по любой колонке»).
    /// </summary>
    /// <remarks>
    /// Ключ выбирается пользователем (клик по заголовку столбца), поэтому в
    /// запрос подставляется только выражение из этого перечня: произвольный
    /// текст в SQL не попадает. Ключи «main_*» — вычисляемые проекции основного
    /// адреса записи (тип адреса задан в <c>address_main_type</c>).
    /// </remarks>
    private static readonly Dictionary<string, string> ВыраженияСортировки =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "name",
            ["shortname"] = "short_name",
            ["inn"] = "inn",
            ["kpp"] = "kpp",
            ["phone"] = "phone",
            ["email"] = "email",
            ["type"] = "type",
            ["vip"] = "is_vip",
            ["is_vip"] = "is_vip",
            ["group"] = "COALESCE((SELECT g.name FROM groups g WHERE g.id = counterparties.group_id), '')",
            ["group_name"] = "COALESCE((SELECT g.name FROM groups g WHERE g.id = counterparties.group_id), '')",
            ["group_id"] = "group_id",
            ["zip"] = "post_zip",
            ["post_zip"] = "post_zip",
            ["city"] = "post_str5",
            ["post_str5"] = "post_str5",
            ["street"] = "post_str2",
            ["post_str2"] = "post_str2",
            ["post_str1"] = "post_str1",
            ["main_zip"] =
                "CASE address_main_type WHEN 0 THEN legal_zip WHEN 2 THEN fact_zip ELSE post_zip END",
            ["main_city"] =
                "CASE address_main_type WHEN 0 THEN legal_str5 WHEN 2 THEN fact_str5 ELSE post_str5 END",
            ["main_street"] =
                "CASE address_main_type WHEN 0 THEN legal_str2 WHEN 2 THEN fact_str2 ELSE post_str2 END",
            ["copies"] = "1 + 0",
            ["created_at"] = "created_at",
            ["updated_at"] = "updated_at",
        };

    /// <summary>
    /// Белый список колонок отбора слоя запроса: имя поля условия — колонка
    /// таблицы контрагентов. Неизвестное поле условием не является.
    /// </summary>
    private static readonly Dictionary<string, string> КолонкиОтбора =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "name",
            ["shortname"] = "short_name",
            ["inn"] = "inn",
            ["kpp"] = "kpp",
            ["phone"] = "phone",
            ["email"] = "email",
            ["group_id"] = "group_id",
            ["zip"] = "post_zip",
            ["post_zip"] = "post_zip",
            ["post_str1"] = "post_str1",
            ["post_str2"] = "post_str2",
            ["street"] = "post_str2",
            ["post_str5"] = "post_str5",
            ["city"] = "post_str5",
            ["main_zip"] =
                "CASE address_main_type WHEN 0 THEN legal_zip WHEN 2 THEN fact_zip ELSE post_zip END",
            ["main_city"] =
                "CASE address_main_type WHEN 0 THEN legal_str5 WHEN 2 THEN fact_str5 ELSE post_str5 END",
            ["main_street"] =
                "CASE address_main_type WHEN 0 THEN legal_str2 WHEN 2 THEN fact_str2 ELSE post_str2 END",
        };

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
                @BirthDate, @BirthPlace, @Position, @DirectorFio, @Email, @Phone, @SpecialNotes, @Source,
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
                source = @Source,
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

            // Условия слоя запроса (ТЗ §8.1, функция 9) дополняют структурные
            // условия отбора: оба набора объединяются по «И».
            ДобавитьУсловияСлояЗапроса(условия, параметры, filter.Criteria);
        }

        var где = условия.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", условия);

        // Порядок сортировки вычисляется до обращения к базе: неизвестная
        // колонка отвергается, запрос не выполняется.
        var порядок = ПорядокСортировки(filter);

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
            $"SELECT * FROM counterparties{где} {порядок} LIMIT @Limit OFFSET @Offset;",
            параметры,
            cancellationToken).ConfigureAwait(false);

        var элементы = строки.Select(строка => строка.ToEntity()).ToList();

        return new Page<Counterparty>(элементы, нормализованный.Offset, нормализованный.Limit, всего);
    }

    /// <inheritdoc />
    public async Task<Counterparty?> FindByMergeKeyAsync(
        string? inn,
        string name,
        string? zip,
        CancellationToken cancellationToken = default)
    {
        // Ключ слияния импорта (ТЗ §8.12, функция 74): при заданном ИНН
        // совпадение ищется по ИНН, иначе — по наименованию и индексу.
        if (!string.IsNullOrWhiteSpace(inn))
        {
            var поИнн = await _сессия.QueryFirstOrDefaultAsync<CounterpartyRow>(
                """
                SELECT * FROM counterparties
                WHERE is_deleted = 0 AND inn IS NOT NULL AND inn <> '' AND inn = @Inn
                ORDER BY id LIMIT 1;
                """,
                new { Inn = inn.Trim() },
                cancellationToken).ConfigureAwait(false);

            if (поИнн is not null)
            {
                return поИнн.ToEntity();
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var условие = "is_deleted = 0 AND name = @Name";

        if (!string.IsNullOrWhiteSpace(zip))
        {
            условие += " AND post_zip = @Zip";
        }

        var параметры = new DynamicParameters();
        параметры.Add("Name", name.Trim());
        параметры.Add("Zip", string.IsNullOrWhiteSpace(zip) ? null : zip.Trim());

        var поНаименованию = await _сессия.QueryFirstOrDefaultAsync<CounterpartyRow>(
            $"SELECT * FROM counterparties WHERE {условие} ORDER BY id LIMIT 1;",
            параметры,
            cancellationToken).ConfigureAwait(false);

        return поНаименованию?.ToEntity();
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<Counterparty>> GetSendersAsync(
        CancellationToken cancellationToken = default)
    {
        var строки = await _сессия.QueryAsync<CounterpartyRow>(
            """
            SELECT * FROM counterparties
            WHERE is_sender = 1 AND is_deleted = 0
            ORDER BY name, id;
            """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return строки.Select(строка => строка.ToEntity()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetDistinctFieldValuesAsync(
        string field,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var колонка = СоответствующаяКолонка(field);

        var значения = await _сессия.QueryAsync<string>(
            $"""
             SELECT DISTINCT {колонка} FROM counterparties
             WHERE is_deleted = 0 AND {колонка} IS NOT NULL AND TRIM({колонка}) <> ''
             ORDER BY {колонка} COLLATE NOCASE;
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return значения.ToList();
    }

    /// <inheritdoc />
    public async Task<int> UpdateFieldAsync(
        IReadOnlyList<long> ids,
        string propertyName,
        object? newValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return 0;
        }

        var колонка = СоответствующаяКолонка(propertyName);

        var параметры = new DynamicParameters();
        параметры.Add("Ids", ids);
        параметры.Add("Value", newValue);
        параметры.Add("UpdatedAt", DateTime.UtcNow);

        return await _сессия.ExecuteAsync(
            $"""
             UPDATE counterparties SET {колонка} = @Value, updated_at = @UpdatedAt
             WHERE id IN @Ids;
             """,
            параметры,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Добавляет к набору условий отбора условия слоя запроса
    /// (ТЗ §8.1, функция 9: «Фильтры и сортировка по любой колонке»).
    /// </summary>
    /// <param name="условия">Набор условий отбора.</param>
    /// <param name="параметры">Набор параметров команды.</param>
    /// <param name="критерии">Критерии слоя запроса; могут отсутствовать.</param>
    private static void ДобавитьУсловияСлояЗапроса(
        List<string> условия,
        DynamicParameters параметры,
        FilterCriteria? критерии)
    {
        if (критерии is null)
        {
            return;
        }

        // Встроенный фильтр «Избранное».
        if (критерии.OnlyFavorites)
        {
            условия.Add("is_vip = 1");
        }

        // Пользовательский фильтр по группе (категории).
        if (критерии.GroupId is not null)
        {
            условия.Add("group_id = @CriteriaGroupId");
            параметры.Add("CriteriaGroupId", критерии.GroupId.Value);
        }

        var номер = 0;
        foreach (var условие in критерии.Conditions)
        {
            ДобавитьУсловие(условия, параметры, условие, номер);
            номер++;
        }
    }

    /// <summary>
    /// Добавляет одно условие отбора слоя запроса. Поле условия сопоставляется
    /// с колонкой по белому списку; неизвестное поле условием не является.
    /// </summary>
    /// <param name="условия">Набор условий отбора.</param>
    /// <param name="параметры">Набор параметров команды.</param>
    /// <param name="условие">Условие отбора.</param>
    /// <param name="номер">Порядковый номер условия — для имени параметра.</param>
    private static void ДобавитьУсловие(
        List<string> условия,
        DynamicParameters параметры,
        FilterCriteria.Condition условие,
        int номер)
    {
        var значение = условие.Value;

        if (string.IsNullOrEmpty(значение))
        {
            return;
        }

        if (!КолонкиОтбора.TryGetValue(условие.Field.Trim(), out var колонка))
        {
            return;
        }

        var имяПараметра = $"Cond{номер}";

        switch (условие.Operator)
        {
            case FilterOperator.Equals:
                условия.Add($"{колонка} = @{имяПараметра}");
                параметры.Add(имяПараметра, значение);
                break;

            case FilterOperator.NotContains:
                условия.Add($"({колонка} IS NULL OR {колонка} NOT LIKE @{имяПараметра})");
                параметры.Add(имяПараметра, $"%{значение}%");
                break;

            case FilterOperator.GreaterThan:
                условия.Add($"{колонка} > @{имяПараметра}");
                параметры.Add(имяПараметра, значение);
                break;

            case FilterOperator.LessThan:
                условия.Add($"{колонка} < @{имяПараметра}");
                параметры.Add(имяПараметра, значение);
                break;

            default:
                условия.Add($"{колонка} LIKE @{имяПараметра}");
                параметры.Add(имяПараметра, $"%{значение}%");
                break;
        }
    }

    /// <summary>
    /// Формирует предложение ORDER BY по колонке адресной книги.
    /// </summary>
    /// <param name="filter">Условия отбора и сортировки.</param>
    /// <returns>Предложение ORDER BY.</returns>
    /// <exception cref="ArgumentException">
    /// Выбрасывается, если колонка сортировки не входит в белый список.
    /// </exception>
    private static string ПорядокСортировки(CounterpartyFilter? filter)
    {
        var критерии = filter?.Criteria;
        var колонка = критерии?.SortColumn ?? filter?.SortColumn;

        if (string.IsNullOrWhiteSpace(колонка))
        {
            return "ORDER BY id ASC";
        }

        var поУбыванию = критерии?.SortColumn is not null
            ? критерии.SortDescending
            : filter!.SortDescending;

        if (!ВыраженияСортировки.TryGetValue(колонка.Trim(), out var выражение))
        {
            throw new ArgumentException(
                $"Колонка сортировки «{колонка}» не поддерживается.",
                nameof(CounterpartyFilter.SortColumn));
        }

        return $"ORDER BY {выражение} {(поУбыванию ? "DESC" : "ASC")}, id ASC";
    }

    /// <summary>
    /// Сопоставляет имя поля справочника колонке таблицы контрагентов.
    /// </summary>
    /// <param name="field">Имя поля.</param>
    /// <returns>Имя колонки таблицы <c>counterparties</c>.</returns>
    /// <exception cref="ArgumentException">
    /// Выбрасывается, если поле не входит в перечень допустимых.
    /// </exception>
    private static string СоответствующаяКолонка(string field)
    {
        var соответствие = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "name",
            ["shortname"] = "short_name",
            ["inn"] = "inn",
            ["kpp"] = "kpp",
            ["phone"] = "phone",
            ["email"] = "email",
            ["groupid"] = "group_id",
            ["isvip"] = "is_vip",
            ["issender"] = "is_sender",
            ["isactive"] = "is_active",
            ["post_zip"] = "post_zip",
            ["zip"] = "post_zip",
            ["post_str1"] = "post_str1",
            ["post_str5"] = "post_str5",
            ["city"] = "post_str5",
        };

        if (!соответствие.TryGetValue(field.Trim(), out var колонка))
        {
            throw new ArgumentException(
                $"Поле «{field}» не поддерживается для выборки или пакетного изменения.",
                nameof(field));
        }

        return колонка;
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
        параметры.Add("Source", counterparty.Source);
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
