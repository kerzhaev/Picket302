using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;

namespace Picket302.Application.Queries;

/// <summary>
/// Построение условий отбора и сортировки контрагентов
/// (ТЗ §8.1, функция 9: «Фильтры и сортировка по любой колонке»).
/// </summary>
/// <remarks>
/// Формирует динамическое условие отбора по выбранному столбцу. Встроенный
/// фильтр «Избранное» и пользовательский фильтр по группам — два раздельных
/// механизма, объединять их в один нельзя (ТЗ §7.7).
/// </remarks>
public static class CounterpartyFilterQuery
{
    /// <summary>
    /// Применяет условия отбора к исходной выборке.
    /// </summary>
    /// <param name="baseQuery">Исходная выборка контрагентов.</param>
    /// <param name="criteria">Условия отбора.</param>
    /// <returns>Отфильтрованная выборка.</returns>
    public static IQueryable<Counterparty> ApplyFilter(
        IQueryable<Counterparty> baseQuery,
        FilterCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.OnlyFavorites)
        {
            baseQuery = baseQuery.Where(контрагент => контрагент.IsVip);
        }

        if (criteria.GroupId is not null)
        {
            var groupId = criteria.GroupId.Value;
            baseQuery = baseQuery.Where(контрагент => контрагент.GroupId == groupId);
        }

        foreach (var условие in criteria.Conditions)
        {
            baseQuery = ПрименитьУсловие(baseQuery, условие);
        }

        return baseQuery;
    }

    /// <summary>
    /// Применяет сортировку по выбранной колонке.
    /// </summary>
    /// <param name="query">Исходная выборка.</param>
    /// <param name="criteria">Условия отбора с указанием колонки сортировки.</param>
    /// <returns>Отсортированная выборка.</returns>
    public static IQueryable<Counterparty> ApplySort(
        IQueryable<Counterparty> query,
        FilterCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(criteria);

        if (string.IsNullOrWhiteSpace(criteria.SortColumn))
        {
            return query;
        }

        var поУбыванию = criteria.SortDescending;

        return criteria.SortColumn.ToLowerInvariant() switch
        {
            "name" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.Name)
                : query.OrderBy(контрагент => контрагент.Name),
            "shortname" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.ShortName)
                : query.OrderBy(контрагент => контрагент.ShortName),
            "inn" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.Inn)
                : query.OrderBy(контрагент => контрагент.Inn),
            "kpp" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.Kpp)
                : query.OrderBy(контрагент => контрагент.Kpp),
            "phone" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.Phone)
                : query.OrderBy(контрагент => контрагент.Phone),
            "email" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.Email)
                : query.OrderBy(контрагент => контрагент.Email),
            "post_zip" or "zip" or "main_zip" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.PostalAddress != null
                    ? контрагент.PostalAddress.Zip
                    : string.Empty)
                : query.OrderBy(контрагент => контрагент.PostalAddress != null
                    ? контрагент.PostalAddress.Zip
                    : string.Empty),
            "post_str5" or "city" or "main_city" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.PostalAddress != null
                    && контрагент.PostalAddress.Lines.Count > 4
                    ? контрагент.PostalAddress.Lines[4]
                    : string.Empty)
                : query.OrderBy(контрагент => контрагент.PostalAddress != null
                    && контрагент.PostalAddress.Lines.Count > 4
                    ? контрагент.PostalAddress.Lines[4]
                    : string.Empty),
            "post_str1" or "main_street" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.PostalAddress != null
                    && контрагент.PostalAddress.Lines.Count > 0
                    ? контрагент.PostalAddress.Lines[0]
                    : string.Empty)
                : query.OrderBy(контрагент => контрагент.PostalAddress != null
                    && контрагент.PostalAddress.Lines.Count > 0
                    ? контрагент.PostalAddress.Lines[0]
                    : string.Empty),
            "group_id" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.GroupId)
                : query.OrderBy(контрагент => контрагент.GroupId),
            "is_vip" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.IsVip)
                : query.OrderBy(контрагент => контрагент.IsVip),
            "created_at" => поУбыванию
                ? query.OrderByDescending(контрагент => контрагент.CreatedAt)
                : query.OrderBy(контрагент => контрагент.CreatedAt),
            _ => query,
        };
    }

    /// <summary>
    /// Применяет одно условие отбора к выборке.
    /// </summary>
    /// <param name="query">Исходная выборка.</param>
    /// <param name="условие">Условие отбора.</param>
    /// <returns>Отфильтрованная выборка.</returns>
    private static IQueryable<Counterparty> ПрименитьУсловие(
        IQueryable<Counterparty> query,
        FilterCriteria.Condition условие)
    {
        var значение = условие.Value ?? string.Empty;

        if (значение.Length == 0)
        {
            return query;
        }

        return условие.Field.ToLowerInvariant() switch
        {
            "name" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.Name == значение),
                FilterOperator.NotContains => query.Where(к => !к.Name.Contains(значение)),
                _ => query.Where(к => к.Name.Contains(значение)),
            },
            "shortname" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.ShortName == значение),
                FilterOperator.NotContains => query.Where(к => к.ShortName != null && !к.ShortName.Contains(значение)),
                _ => query.Where(к => к.ShortName != null && к.ShortName.Contains(значение)),
            },
            "inn" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.Inn == значение),
                FilterOperator.NotContains => query.Where(к => к.Inn != null && !к.Inn.Contains(значение)),
                _ => query.Where(к => к.Inn != null && к.Inn.Contains(значение)),
            },
            "kpp" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.Kpp == значение),
                _ => query.Where(к => к.Kpp != null && к.Kpp.Contains(значение)),
            },
            "phone" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.Phone == значение),
                FilterOperator.NotContains => query.Where(к => к.Phone != null && !к.Phone.Contains(значение)),
                _ => query.Where(к => к.Phone != null && к.Phone.Contains(значение)),
            },
            "email" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.Email == значение),
                FilterOperator.NotContains => query.Where(к => к.Email != null && !к.Email.Contains(значение)),
                _ => query.Where(к => к.Email != null && к.Email.Contains(значение)),
            },
            "post_zip" or "zip" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.PostalAddress != null && к.PostalAddress.Zip == значение),
                FilterOperator.NotContains => query.Where(к => к.PostalAddress != null && !к.PostalAddress.Zip.Contains(значение)),
                _ => query.Where(к => к.PostalAddress != null && к.PostalAddress.Zip.Contains(значение)),
            },
            "post_str5" or "city" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.PostalAddress != null && к.PostalAddress.Lines[4] == значение),
                FilterOperator.NotContains => query.Where(к => к.PostalAddress != null && !к.PostalAddress.Lines[4].Contains(значение)),
                _ => query.Where(к => к.PostalAddress != null && к.PostalAddress.Lines[4].Contains(значение)),
            },
            "post_str1" => условие.Operator switch
            {
                FilterOperator.Equals => query.Where(к => к.PostalAddress != null && к.PostalAddress.Lines[0] == значение),
                _ => query.Where(к => к.PostalAddress != null && к.PostalAddress.Lines[0].Contains(значение)),
            },
            _ => query,
        };
    }
}
