namespace Picket302.Domain.Queries;

/// <summary>
/// Нарушение правил валидации запроса (ТЗ §4.1, ValidationRules).
/// </summary>
/// <param name="Message">Текст нарушения.</param>
public sealed class QueryValidationException(string message) : Exception(message)
{
    /// <summary>Имя параметра, вызвавшего нарушение; <see langword="null"/> — нарушение общее.</summary>
    public string? ParameterName { get; init; }
}

/// <summary>
/// Валидатор определения запроса и значений параметров (ТЗ §4.1, §8.3: слой
/// запросов «StrictParametric» — значения передаются только привязкой
/// параметров, подстановка значений в текст команды запрещена).
/// </summary>
public sealed class QueryValidator
{
    /// <summary>
    /// Проверяет целостность определения запроса: команда каждого диалекта
    /// должна использовать только объявленные параметры, обязательные
    /// параметры объявлены.
    /// </summary>
    /// <param name="definition">Определение запроса.</param>
    /// <exception cref="QueryValidationException">Определение нарушает правила.</exception>
    public void ValidateDefinition(ReportQueryDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            throw new QueryValidationException("Определение запроса не содержит идентификатор.");
        }

        foreach (var команда in definition.Commands)
        {
            foreach (var плейсхолдер in ExtractPlaceholders(команда.Sql))
            {
                if (definition.FindParameter(плейсхолдер) is null)
                {
                    throw new QueryValidationException(
                        $"Команда диалекта «{команда.Dialect}» запроса «{definition.Id}» использует " +
                        $"необъявленный параметр «{плейсхолдер}».");
                }
            }
        }
    }

    /// <summary>
    /// Проверяет значения параметров против правил запроса: обязательность,
    /// диапазон дат (DateRange/maxDays), строгий параметрический режим
    /// (SqlInjectionGuard StrictParametric).
    /// </summary>
    /// <param name="definition">Определение запроса.</param>
    /// <param name="values">Значения параметров.</param>
    /// <param name="dialect">Диалект, для которого выполняется проверка команды.</param>
    /// <exception cref="QueryValidationException">Значения нарушают правила.</exception>
    public void ValidateValues(
        ReportQueryDefinition definition,
        QueryParameterValues values,
        string dialect = QueryDialects.Sqlite)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var параметр in definition.Parameters)
        {
            var значение = values.Get(параметр.Name);

            if (параметр.Required && значение is null)
            {
                throw new QueryValidationException(
                    $"Не задано обязательное значение параметра «{параметр.Name}».")
                {
                    ParameterName = параметр.Name,
                };
            }
        }

        foreach (var правило in definition.ValidationRules)
        {
            switch (правило.Type)
            {
                case QueryRuleTypes.DateRange:
                    ПроверитьДиапазонДат(правило, definition, values);
                    break;

                case QueryRuleTypes.SqlInjectionGuard:
                    ПроверитьЗащиту(правило, definition, values, dialect);
                    break;

                default:
                    throw new QueryValidationException(
                        $"Запрос «{definition.Id}» содержит неизвестное правило валидации «{правило.Type}».");
            }
        }
    }

    private static void ПроверитьДиапазонДат(
        QueryRule правило,
        ReportQueryDefinition определение,
        QueryParameterValues значения)
    {
        var начало = ЗначениеДаты(значения.Get(правило.Start ?? "DateFrom"));
        var конец = ЗначениеДаты(значения.Get(правило.End ?? "DateTo"));

        if (начало is null || конец is null)
        {
            return;
        }

        if (начало > конец)
        {
            throw new QueryValidationException(
                $"Диапазон дат запроса «{определение.Id}» задан неверно: начало позже конца.")
            {
                ParameterName = правило.Start,
            };
        }

        if (правило.MaxDays is { } максимум && (конец.Value - начало.Value).TotalDays > максимум)
        {
            throw new QueryValidationException(
                $"Диапазон дат запроса «{определение.Id}» превышает {максимум} дн.")
            {
                ParameterName = правило.End,
            };
        }
    }

    private static void ПроверитьЗащиту(
        QueryRule правило,
        ReportQueryDefinition определение,
        QueryParameterValues значения,
        string диалект)
    {
        var режим = правило.Mode ?? SqlGuardModes.StrictParametric;

        if (!string.Equals(режим, SqlGuardModes.StrictParametric, StringComparison.Ordinal))
        {
            throw new QueryValidationException(
                $"Запрос «{определение.Id}» использует неподдерживаемый режим защиты «{режим}».");
        }

        var команда = определение.GetCommand(диалект);

        foreach (var имя in значения.Names)
        {
            if (определение.FindParameter(имя) is null)
            {
                throw new QueryValidationException(
                    $"Значение задано для необъявленного параметра «{имя}» запроса «{определение.Id}».")
                {
                    ParameterName = имя,
                };
            }
        }

        // Строгий параметрический режим: в значениях строковых параметров
        // недопустимы служебные конструкции SQL — значения попадают в СУБД
        // только через привязанные параметры, но входной поток проверяется
        // до обращения к драйверу.
        foreach (var имя in значения.Names)
        {
            var параметр = определение.FindParameter(имя)!;

            if (!string.Equals(параметр.Type, "String", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (значения.Get(имя) is string текст && ПодозрительныйТекст(текст))
            {
                throw new QueryValidationException(
                    $"Значение параметра «{имя}» содержит недопустимые конструкции SQL " +
                    "(строгий параметрический режим).")
                {
                    ParameterName = имя,
                };
            }
        }

        foreach (var плейсхолдер in ExtractPlaceholders(команда.Sql))
        {
            if (определение.FindParameter(плейсхолдер) is null)
            {
                throw new QueryValidationException(
                    $"Команда запроса «{определение.Id}» использует необъявленный параметр «{плейсхолдер}».")
                {
                    ParameterName = плейсхолдер,
                };
            }
        }
    }

    /// <summary>
    /// Определяет, содержит ли строковое значение конструкции, запрещённые
    /// строгим параметрическим режимом.
    /// </summary>
    /// <param name="text">Значение строкового параметра.</param>
    /// <returns><see langword="true"/>, если значение недопустимо.</returns>
    public static bool ПодозрительныйТекст(string text)
    {
        var верхний = text.ToUpperInvariant();

        return верхний.Contains("--", StringComparison.Ordinal)
            || верхний.Contains("/*", StringComparison.Ordinal)
            || верхний.Contains(';')
            || верхний.Contains('\0');
    }

    /// <summary>
    /// Извлекает имена параметров <c>:Имя</c> из текста команды; части внутри
    /// одинарных кавычек (литералы SQL) игнорируются.
    /// </summary>
    /// <param name="sql">Текст команды.</param>
    /// <returns>Отсортированный список уникальных имён.</returns>
    public static IReadOnlyList<string> ExtractPlaceholders(string sql)
    {
        var имена = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var вЛитерале = false;

        for (var позиция = 0; позиция < sql.Length; позиция++)
        {
            var знак = sql[позиция];

            if (знак == '\'')
            {
                if (вЛитерале && позиция + 1 < sql.Length && sql[позиция + 1] == '\'')
                {
                    позиция++;
                    continue;
                }

                вЛитерале = !вЛитерале;
                continue;
            }

            if (вЛитерале)
            {
                continue;
            }

            if (знак == ':' && позиция + 1 < sql.Length && (char.IsLetter(sql[позиция + 1]) || sql[позиция + 1] == '_'))
            {
                var начало = позиция + 1;
                var конец = начало;

                while (конец < sql.Length && (char.IsLetterOrDigit(sql[конец]) || sql[конец] == '_'))
                {
                    конец++;
                }

                имена.Add(sql[начало..конец]);
                позиция = конец - 1;
            }
        }

        return имена.ToList();
    }

    private static DateTime? ЗначениеДаты(object? значение) => значение switch
    {
        null => null,
        DateTime момент => момент,
        string текст when DateTime.TryParse(
            текст,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var разобрано) => разобрано,
        _ => null,
    };
}
