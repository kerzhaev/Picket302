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
    public async Task<IReadOnlyList<IReportRow>> RunQueryAsync(
        string queryId,
        QueryParameterValues values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(values);

        var определение = BuiltinReportQueryCatalog.Get(queryId);
        определение.ApplyDefaults(values, DateTime.UtcNow);
        _валидатор.ValidateDefinition(определение);
        _валидатор.ValidateValues(определение, values, CurrentDialect);

        var команда = определение.GetCommand(CurrentDialect);
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
