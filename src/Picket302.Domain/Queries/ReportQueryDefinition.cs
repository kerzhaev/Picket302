namespace Picket302.Domain.Queries;

/// <summary>Диалекты SQL команды запроса (ТЗ §4.1, атрибут Command/@dialect).</summary>
public static class QueryDialects
{
    /// <summary>Диалект PostgreSQL 16.</summary>
    public const string PostgreSql = "PostgreSql";

    /// <summary>Диалект SQLite 3 (поставка текущего этапа).</summary>
    public const string Sqlite = "Sqlite";
}

/// <summary>Типы правил валидации запроса (ТЗ §4.1, ValidationRules/Rule/@type).</summary>
public static class QueryRuleTypes
{
    /// <summary>Контроль диапазона дат.</summary>
    public const string DateRange = "DateRange";

    /// <summary>Защита от SQL-инъекций.</summary>
    public const string SqlInjectionGuard = "SqlInjectionGuard";
}

/// <summary>Режимы защиты от SQL-инъекций (ТЗ §4.1, Rule/@mode).</summary>
public static class SqlGuardModes
{
    /// <summary>
    /// Строгий параметрический режим: значения параметров передаются в СУБД
    /// исключительно через привязанные параметры; подстановка значений в текст
    /// команды запрещена.
    /// </summary>
    public const string StrictParametric = "StrictParametric";
}

/// <summary>
/// Параметр запроса (ТЗ §4.1, элемент Parameter).
/// </summary>
public sealed class QueryParameter
{
    /// <summary>Имя параметра; в тексте команды обозначается <c>:Имя</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Тип значения: DateTime, Int64, Int32, String, Decimal.</summary>
    public string Type { get; set; } = "String";

    /// <summary>Обязательность заполнения.</summary>
    public bool Required { get; set; }

    /// <summary>
    /// Выражение значения по умолчанию: константа либо «Today», «Today-30d».
    /// </summary>
    public string? Default { get; set; }
}

/// <summary>
/// Правило валидации запроса (ТЗ §4.1, элемент Rule).
/// </summary>
public sealed class QueryRule
{
    /// <summary>Тип правила: DateRange, SqlInjectionGuard.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Имя параметра начала диапазона (для DateRange).</summary>
    public string? Start { get; set; }

    /// <summary>Имя параметра конца диапазона (для DateRange).</summary>
    public string? End { get; set; }

    /// <summary>Максимальная длина диапазона в днях (для DateRange).</summary>
    public int? MaxDays { get; set; }

    /// <summary>Режим защиты (для SqlInjectionGuard).</summary>
    public string? Mode { get; set; }
}

/// <summary>
/// Команда SQL в конкретном диалекте (ТЗ §4.1, элемент Command).
/// </summary>
public sealed class QueryCommand
{
    /// <summary>Диалект: PostgreSql, Sqlite.</summary>
    public string Dialect { get; set; } = QueryDialects.Sqlite;

    /// <summary>Текст команды; значения параметров обозначены <c>:Имя</c>.</summary>
    public string Sql { get; set; } = string.Empty;
}

/// <summary>
/// Значения параметров запроса (ТЗ §4.1: слой запросов «хранит SQL-проекцию
/// в файле бланка/отчёта, а не в коде», значения подставляются только
/// параметрами).
/// </summary>
public sealed class QueryParameterValues
{
    private readonly Dictionary<string, object?> _значения = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Задаёт значение параметра.</summary>
    /// <param name="name">Имя параметра.</param>
    /// <param name="value">Значение.</param>
    public void Set(string name, object? value) => _значения[name] = value;

    /// <summary>Читает значение параметра.</summary>
    /// <param name="name">Имя параметра.</param>
    /// <returns>Значение либо <see langword="null"/>.</returns>
    public object? Get(string name) => _значения.TryGetValue(name, out var значение) ? значение : null;

    /// <summary>Имена заданных параметров.</summary>
    public IReadOnlyCollection<string> Names => _значения.Keys;

    /// <summary>Проверяет, задан ли параметр.</summary>
    /// <param name="name">Имя параметра.</param>
    /// <returns><see langword="true"/>, если параметр задан.</returns>
    public bool Contains(string name) => _значения.ContainsKey(name);
}

/// <summary>
/// Определение запроса слоя данных (ТЗ §4.1, корень ReportQueryDefinition).
/// </summary>
public sealed class ReportQueryDefinition
{
    /// <summary>Идентификатор запроса (например, «qrReport_Envelopes»).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Версия определения.</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>Человекочитаемое наименование.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Параметры запроса.</summary>
    public List<QueryParameter> Parameters { get; } = new();

    /// <summary>Правила валидации.</summary>
    public List<QueryRule> ValidationRules { get; } = new();

    /// <summary>Команды по диалектам.</summary>
    public List<QueryCommand> Commands { get; } = new();

    /// <summary>
    /// Возвращает команду для диалекта; для текущей поставки обязательна
    /// команда диалекта Sqlite, PostgreSQL-вариант хранится для совместимости.
    /// </summary>
    /// <param name="dialect">Диалект.</param>
    /// <returns>Команда.</returns>
    /// <exception cref="KeyNotFoundException">Команда диалекта не определена.</exception>
    public QueryCommand GetCommand(string dialect)
    {
        foreach (var команда in Commands)
        {
            if (string.Equals(команда.Dialect, dialect, StringComparison.OrdinalIgnoreCase))
            {
                return команда;
            }
        }

        throw new KeyNotFoundException(
            $"Запрос «{Id}» не содержит команды диалекта «{dialect}».");
    }

    /// <summary>Возвращает параметр по имени.</summary>
    /// <param name="name">Имя параметра.</param>
    /// <returns>Параметр либо <see langword="null"/>.</returns>
    public QueryParameter? FindParameter(string name)
    {
        foreach (var параметр in Parameters)
        {
            if (string.Equals(параметр.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return параметр;
            }
        }

        return null;
    }

    /// <summary>
    /// Заполняет значения параметров по умолчанию: выражения «Today»,
    /// «Today-Nd» и литералы.
    /// </summary>
    /// <param name="values">Приёмник значений.</param>
    /// <param name="now">Текущий момент.</param>
    public void ApplyDefaults(QueryParameterValues values, DateTime now)
    {
        foreach (var параметр in Parameters)
        {
            if (values.Contains(параметр.Name))
            {
                continue;
            }

            var значение = EvaluateDefault(параметр.Default, параметр.Type, now);

            if (значение is not null)
            {
                values.Set(параметр.Name, значение);
            }
        }
    }

    /// <summary>
    /// Вычисляет значение по умолчанию параметра.
    /// </summary>
    /// <param name="expression">Выражение: «Today», «Today-30d» либо литерал.</param>
    /// <param name="type">Тип параметра.</param>
    /// <param name="now">Текущий момент.</param>
    /// <returns>Значение либо <see langword="null"/>, если значения нет.</returns>
    public static object? EvaluateDefault(string? expression, string type, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var текст = expression.Trim();

        if (string.Equals(текст, "Today", StringComparison.OrdinalIgnoreCase))
        {
            return ПривестиТип(now.Date, type);
        }

        if (текст.StartsWith("Today-", StringComparison.OrdinalIgnoreCase))
        {
            var суффикс = текст["Today-".Length..].TrimEnd('d', 'D');

            if (int.TryParse(суффикс, System.Globalization.CultureInfo.InvariantCulture, out var дни))
            {
                return ПривестиТип(now.Date.AddDays(-дни), type);
            }
        }

        return литерал(текст, type);

        static object? литерал(string значение, string тип) => тип switch
        {
            "DateTime" => DateTime.Parse(значение, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
            "Int64" => long.Parse(значение, System.Globalization.CultureInfo.InvariantCulture),
            "Int32" => int.Parse(значение, System.Globalization.CultureInfo.InvariantCulture),
            "Decimal" => decimal.Parse(значение, System.Globalization.CultureInfo.InvariantCulture),
            _ => значение,
        };
    }

    private static object ПривестиТип(DateTime момент, string type) => type switch
    {
        "DateTime" => момент,
        "Int64" => момент.Ticks,
        "Int32" => (int)(момент.Ticks % int.MaxValue),
        _ => момент.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
    };
}
