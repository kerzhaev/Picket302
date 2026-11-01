using System.Data;
using System.Globalization;
using Dapper;

namespace Picket302.Infrastructure.Data;

/// <summary>
/// Обработчик типа <see cref="DateTime"/> для SQLite: значения хранятся в виде
/// текста в формате <c>yyyy-MM-dd HH:mm:ss.fffffff</c> (UTC).
/// </summary>
internal sealed class SqliteDateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    /// <summary>Формат хранения даты и времени в SQLite.</summary>
    public const string Формат = "yyyy-MM-dd HH:mm:ss.fffffff";

    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.Value = DateTime.SpecifyKind(value, DateTimeKind.Utc)
            .ToString(Формат, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override DateTime Parse(object? value)
    {
        if (value is DateTime момент)
        {
            return DateTime.SpecifyKind(момент, DateTimeKind.Utc);
        }

        if (value is null or DBNull)
        {
            return default;
        }

        var текст = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        return DateTime.Parse(
            текст,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}

/// <summary>
/// Обработчик типа <see cref="DateTime"/>? для SQLite.
/// </summary>
internal sealed class SqliteNullableDateTimeHandler : SqlMapper.TypeHandler<DateTime?>
{
    private readonly SqliteDateTimeHandler _вложенный = new();

    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, DateTime? value)
    {
        if (value is null)
        {
            parameter.Value = DBNull.Value;
            return;
        }

        _вложенный.SetValue(parameter, value.Value);
    }

    /// <inheritdoc />
    public override DateTime? Parse(object? value)
        => value is null or DBNull ? null : _вложенный.Parse(value);
}

/// <summary>
/// Обработчик типа <see cref="decimal"/> для SQLite: колонки с аффинностью
/// NUMERIC хранят целые значения как INTEGER, а дробные — как REAL, поэтому
/// тип значения в одной и той же колонке меняется от строки к строке.
/// </summary>
internal sealed class SqliteDecimalHandler : SqlMapper.TypeHandler<decimal>
{
    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, decimal value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value;
    }

    /// <inheritdoc />
    public override decimal Parse(object? value) => value switch
    {
        null or DBNull => 0m,
        decimal число => число,
        double число => Convert.ToDecimal(число, CultureInfo.InvariantCulture),
        float число => Convert.ToDecimal(число, CultureInfo.InvariantCulture),
        long число => число,
        int число => число,
        short число => число,
        byte число => число,
        string текст => decimal.Parse(текст, NumberStyles.Any, CultureInfo.InvariantCulture),
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Обработчик типа <see cref="decimal"/>? для SQLite.
/// </summary>
internal sealed class SqliteNullableDecimalHandler : SqlMapper.TypeHandler<decimal?>
{
    private readonly SqliteDecimalHandler _вложенный = new();

    /// <inheritdoc />
    public override void SetValue(IDbDataParameter parameter, decimal? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    /// <inheritdoc />
    public override decimal? Parse(object? value)
        => value is null or DBNull ? null : _вложенный.Parse(value);
}

/// <summary>
/// Общая настройка слоя доступа к данным (Dapper).
/// </summary>
/// <remarks>
/// Настройки применяются перед первым обращением к Dapper: сопоставление
/// колонок <c>snake_case</c> со свойствами сущностей и обработчики типов даты
/// и денежных величин для SQLite. Настройка идемпотентна.
/// </remarks>
internal static class DapperConfiguration
{
    private static readonly object Защита = new();
    private static bool _настроено;

    /// <summary>
    /// Применяет настройки Dapper: сопоставление имён колонок с подчёркиваниями,
    /// обработчики типов даты и денежных величин.
    /// </summary>
    public static void Применить()
    {
        lock (Защита)
        {
            if (_настроено)
            {
                return;
            }

            _настроено = true;
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            SqlMapper.AddTypeHandler<DateTime>(new SqliteDateTimeHandler());
            SqlMapper.AddTypeHandler<DateTime?>(new SqliteNullableDateTimeHandler());
            SqlMapper.AddTypeHandler<decimal>(new SqliteDecimalHandler());
            SqlMapper.AddTypeHandler<decimal?>(new SqliteNullableDecimalHandler());
        }
    }
}
