using Dapper;
using Picket302.Domain.Licensing;
using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Счётчик объёма данных программы по базе данных: число контрагентов и
/// созданных документов (ТЗ §8.10, функция 119: «лимит по объёму (число
/// документов/контрагентов)»).
/// </summary>
/// <remarks>
/// Счётчик обращается к базе при каждой проверке лимитов, поэтому объём
/// всегда соответствует фактическому наполнению: счётчик не заполнялся
/// главной формой, и лимит объёма не срабатывал.
/// </remarks>
public static class DatabaseUsageCounter
{
    /// <summary>
    /// Создаёт функцию счётчика объёма по указанной сессии базы данных.
    /// </summary>
    /// <param name="сессия">Сессия доступа к базе данных.</param>
    /// <returns>Функция, возвращающая текущий снимок объёма.</returns>
    /// <exception cref="ArgumentNullException">Сессия не задана.</exception>
    public static Func<СнимокИспользования> Создать(ISqliteSession сессия)
    {
        ArgumentNullException.ThrowIfNull(сессия);

        return () => new СнимокИспользования(
            Посчитать(сессия, "SELECT COUNT(*) FROM counterparties WHERE is_deleted = 0"),
            Посчитать(сессия, "SELECT COUNT(*) FROM mails"));
    }

    /// <summary>
    /// Считает строки запроса; при недоступной базе возвращает ноль, чтобы
    /// проверка лимитов не блокировала работу программы из-за сбоя счётчика.
    /// </summary>
    /// <param name="сессия">Сессия доступа к базе данных.</param>
    /// <param name="запрос">Запрос подсчёта строк.</param>
    /// <returns>Число строк либо ноль.</returns>
    private static int Посчитать(ISqliteSession сессия, string запрос)
    {
        try
        {
            return сессия.Connection.Query<int>(запрос).FirstOrDefault();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return 0;
        }
    }
}
