using Microsoft.Data.Sqlite;
using Picket302.Domain.Queries;
using Picket302.Printing.Reports;

namespace Picket302.Infrastructure.Queries;

/// <summary>
/// Исполнитель запросов слоя данных на SQLite текущей поставки (ТЗ §4.1;
/// §10.1 — поставка на SQLite). Значения параметров передаются в СУБД
/// исключительно через привязанные параметры (StrictParametric, ТЗ §4.1).
/// </summary>
public sealed class SqliteReportDataProvider : IReportDataProvider
{
    /// <summary>Диалект текущей поставки.</summary>
    public const string CurrentDialect = QueryDialects.Sqlite;

    private readonly SqliteConnection _соединение;
    private readonly QueryValidator _валидатор = new();

    /// <summary>Создаёт поставщик данных поверх соединения SQLite.</summary>
    /// <param name="connection">Открытое соединение с базой данных.</param>
    public SqliteReportDataProvider(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _соединение = connection;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IReportRow>> RunQueryAsync(
        string queryId,
        QueryParameterValues values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);

        return RunDefinitionAsync(BuiltinReportQueryCatalog.Get(queryId), values, cancellationToken);
    }

    /// <summary>
    /// Исполняет произвольное определение запроса (ТЗ §8.4, функция 56:
    /// пользовательские отчёты из <c>Templates/Reports/User</c>). Определение
    /// и значения валидируются штатным <see cref="QueryValidator"/>; исполняется
    /// только команда диалекта SQLite текущей поставки.
    /// </summary>
    /// <param name="definition">Определение запроса (встроенное или пользовательское).</param>
    /// <param name="values">Значения параметров; передаются в СУБД только привязкой.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Строки результата с точными псевдонимами колонок.</returns>
    public async Task<IReadOnlyList<IReportRow>> RunDefinitionAsync(
        ReportQueryDefinition definition,
        QueryParameterValues values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(values);

        definition.ApplyDefaults(values, DateTime.UtcNow);
        _валидатор.ValidateDefinition(definition);
        _валидатор.ValidateValues(definition, values, CurrentDialect);

        var команда = definition.GetCommand(CurrentDialect);

        // Слой запросов — только чтение (D-S27-03): пользовательский query.xml
        // исполняется на живом соединении сессии, поэтому операторы изменения
        // данных (INSERT/UPDATE/DELETE/DROP/…) и составные команды запрещены
        // независимо от валидности синтаксиса.
        ТребоватьТолькоЧтение(definition, команда.Sql);

        var (текст, параметры) = Подготовить(команда.Sql, values);

        using var sqliteКоманда = _соединение.CreateCommand();
        sqliteКоманда.CommandText = текст;

        foreach (var (имя, значение) in параметры)
        {
            sqliteКоманда.Parameters.AddWithValue(имя, Привести(значение));
        }
        var строки = new List<ReportRowData>();

        using var читатель = await sqliteКоманда.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await читатель.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var значения = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            for (var колонка = 0; колонка < читатель.FieldCount; колонка++)
            {
                значения[читатель.GetName(колонка)] = читатель.IsDBNull(колонка) ? null : читатель.GetValue(колонка);
            }

            строки.Add(new ReportRowData(значения));
        }

        return строки;
    }

    /// <summary>
    /// Слой запросов — только чтение: текст команды обязан быть единственным
    /// оператором SELECT или WITH (рекурсивные CTE для перечней). Операторы
    /// изменения схемы/данных запрещены независимо от валидности синтаксиса,
    /// поскольку запросы исполняются на живом соединении сессии
    /// (D-S27-03: пользовательский query.xml → предпросмотр данных;
    /// S27R2-01: обход «WITH …; DELETE …» — сканер проверяет ВСЕ ключевые
    /// слова верхнего уровня вне строковых литералов и комментариев, поэтому
    /// DML после определений CTE тоже отклоняется).
    /// </summary>
    /// <param name="definition">Определение запроса (для текста нарушения).</param>
    /// <param name="sql">Текст команды диалекта SQLite.</param>
    /// <exception cref="QueryValidationException">Команда не только для чтения.</exception>
    private static void ТребоватьТолькоЧтение(ReportQueryDefinition definition, string sql)
    {
        var текст = sql.Trim();

        // Хвостовая точка с запятой допустима (встроенные запросы записаны с
        // ней); внутренние точки с запятой означают составную команду.
        текст = текст.TrimEnd(';').TrimEnd();

        if (текст.Contains(';', StringComparison.Ordinal))
        {
            throw new QueryValidationException(
                $"Запрос «{definition.Id}» содержит несколько операторов — слой данных исполняет только одиночную команду чтения.");
        }

        var слова = ТокеныВерхнегоУровня(текст);

        if (слова.Count == 0
            || ( !string.Equals(слова[0].Текст, "SELECT", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(слова[0].Текст, "WITH", StringComparison.OrdinalIgnoreCase)))
        {
            throw new QueryValidationException(
                $"Запрос «{definition.Id}» не является командой чтения (ожидается SELECT или WITH) — слой данных исполняет только выборки.");
        }

        foreach (var (токен, функция) in слова)
        {
            // REPLACE — исключение: это и оператор замены, и имя функции
            // REPLACE(строка, …); как функция в выборках допустим.
            if (ЗапрещённыеСлова.Contains(токен)
                && (токен != "REPLACE" || !функция))
            {
                throw new QueryValidationException(
                    $"Запрос «{definition.Id}» содержит оператор изменения данных «{токен}» — слой данных исполняет только выборки.");
            }
        }
    }

    /// <summary>Операторы изменения схемы/данных, запрещённые в выборках.</summary>
    private static readonly HashSet<string> ЗапрещённыеСлова = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "REPLACE",
        "ATTACH", "DETACH", "PRAGMA", "VACUUM", "REINDEX", "GRANT", "REVOKE",
        "TRUNCATE",
    };

    /// <summary>Слово верхнего уровня с признаком функционального употребления
    /// (следом — открывающая скобка после пробелов).</summary>
    /// <param name="Текст">Слово.</param>
    /// <param name="Функция">За словом следует «(».</param>
    private readonly record struct ТокенВерхнегоУровня(string Текст, bool Функция);

    /// <summary>
    /// Разбирает текст команды на слова верхнего уровня: вне строковых
    /// литералов (одинарные кавычки, '' — escape), комментариев (-- до конца
    /// строки, /* … */) и скобок произвольной глубины.
    /// </summary>
    /// <param name="текст">Текст команды.</param>
    /// <returns>Токены в порядке появления.</returns>
    private static List<ТокенВерхнегоУровня> ТокеныВерхнегоУровня(string текст)
    {
        var слова = new List<ТокенВерхнегоУровня>();
        var текущее = new System.Text.StringBuilder();
        var глубина = 0;
        var позиция = 0;

        void ЗавершитьСлово()
        {
            if (текущее.Length > 0 && глубина == 0)
            {
                // Функциональное употребление: после слова (допускаются
                // пробелы) идёт «(».
                var взгляд = позиция;

                while (взгляд < текст.Length && char.IsWhiteSpace(текст[взгляд]))
                {
                    взгляд++;
                }

                слова.Add(new ТокенВерхнегоУровня(
                    текущее.ToString(),
                    взгляд < текст.Length && текст[взгляд] == '('));
            }

            текущее.Clear();
        }

        while (позиция < текст.Length)
        {
            var знак = текст[позиция];

            if (знак == '\'')
            {
                // Строковый литерал: '' — escape внутри литерала.
                ЗавершитьСлово();
                позиция++;

                while (позиция < текст.Length)
                {
                    if (текст[позиция] == '\'')
                    {
                        if (позиция + 1 < текст.Length && текст[позиция + 1] == '\'')
                        {
                            позиция += 2;
                            continue;
                        }

                        break;
                    }

                    позиция++;
                }

                позиция++;
                continue;
            }

            if (знак == '-' && позиция + 1 < текст.Length && текст[позиция + 1] == '-')
            {
                // Строчный комментарий — до конца строки.
                ЗавершитьСлово();

                while (позиция < текст.Length && текст[позиция] != '\n')
                {
                    позиция++;
                }

                continue;
            }

            if (знак == '/' && позиция + 1 < текст.Length && текст[позиция + 1] == '*')
            {
                // Блочный комментарий.
                ЗавершитьСлово();
                позиция += 2;

                while (позиция + 1 < текст.Length
                    && !(текст[позиция] == '*' && текст[позиция + 1] == '/'))
                {
                    позиция++;
                }

                позиция += 2;
                continue;
            }

            if (знак == '(')
            {
                ЗавершитьСлово();
                глубина++;
                позиция++;
                continue;
            }

            if (знак == ')')
            {
                ЗавершитьСлово();
                глубина = Math.Max(0, глубина - 1);
                позиция++;
                continue;
            }

            if (char.IsLetterOrDigit(знак) || знак == '_')
            {
                текущее.Append(знак);
                позиция++;
                continue;
            }

            ЗавершитьСлово();
            позиция++;
        }

        ЗавершитьСлово();

        return слова;
    }

    /// <summary>
    /// Готовит текст команды: параметр-список <c>:Имя</c> разворачивается в
    /// перечень <c>(@Имя0, @Имя1, …)</c> для конструкции IN; скалярные
    /// параметры связываются под именем <c>:Имя</c> (префикс текста команды).
    /// </summary>
    /// <param name="sql">Исходный текст команды.</param>
    /// <param name="values">Значения параметров.</param>
    /// <returns>Текст команды и связка «имя — значение».</returns>
    public static (string Sql, IReadOnlyList<(string Name, object? Value)> Parameters) Подготовить(
        string sql,
        QueryParameterValues values)
    {
        var параметры = new List<(string, object?)>();
        var текст = sql;

        foreach (var имя in values.Names)
        {
            var значение = values.Get(имя);

            if (значение is IReadOnlyList<long> список)
            {
                if (список.Count == 0)
                {
                    // Пустой список: гарантированно пустая выборка.
                    текст = ЗаменитьПлейсхолдер(текст, имя, "NULL");
                    continue;
                }

                var имена = new List<string>(список.Count);

                for (var i = 0; i < список.Count; i++)
                {
                    var параметр = "@" + имя + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    имена.Add(параметр);
                    параметры.Add((параметр, список[i]));
                }

                // В текстах запросов параметр используется в составе IN (:Имя) —
                // разворачиваем в перечень без дополнительных скобок.
                текст = ЗаменитьПлейсхолдер(текст, имя, string.Join(", ", имена));
                continue;
            }

            параметры.Add((":" + имя, значение));
        }

        return (текст, параметры);
    }

    private static string ЗаменитьПлейсхолдер(string sql, string имя, string замена)
    {
        // Замена :Имя и @Имя, не входящих в состав более длинного идентификатора.
        foreach (var префикс in new[] { ":", "@" })
        {
            var целевой = префикс + имя;
            var позиция = sql.IndexOf(целевой, StringComparison.Ordinal);

            if (позиция >= 0
                && (позиция + целевой.Length == sql.Length
                    || !char.IsLetterOrDigit(sql[позиция + целевой.Length])
                    && sql[позиция + целевой.Length] != '_'))
            {
                return sql.Remove(позиция, целевой.Length).Insert(позиция, замена);
            }
        }

        return sql;
    }

    private static object Привести(object? значение) => значение switch
    {
        null => DBNull.Value,
        DateTime момент => момент.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        decimal число => число,
        long число => число,
        int число => число,
        bool флаг => флаг ? 1L : 0L,
        IEnumerable<long> => throw new InvalidOperationException("Списки идентификаторов разворачиваются в тексте команды."),
        _ => значение,
    };

    /// <summary>Строка результата запроса слоя данных.</summary>
    private sealed class ReportRowData : IReportRow
    {
        private readonly Dictionary<string, object?> _значения;

        public ReportRowData(Dictionary<string, object?> значения)
        {
            _значения = значения;
        }

        public IReadOnlyList<string> ColumnNames => _значения.Keys.ToList();

        public object? GetValue(string column) =>
            _значения.TryGetValue(column, out var значение) ? значение : null;
    }
}
