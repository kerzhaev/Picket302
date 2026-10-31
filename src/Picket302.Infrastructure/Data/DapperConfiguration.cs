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
/// Общая настройка слоя доступа к данным (Dapper).
/// </summary>
/// <remarks>
/// Настройки применяются перед первым обращением к Dapper: сопоставление
/// колонок <c>snake_case</c> со свойствами сущностей и обработчики типов даты
/// для SQLite. Настройка идемпотентна.
/// </remarks>
internal static class DapperConfiguration
{
    private static readonly object Защита = new();
    private static bool _настроено;

    /// <summary>
    /// Применяет настройки Dapper: сопоставление имён колонок с подчёркиваниями
    /// и обработчики типов даты.
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
        }
    }
}
