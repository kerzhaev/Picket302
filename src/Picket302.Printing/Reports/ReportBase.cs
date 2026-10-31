using Picket302.Domain.Queries;
using System.Globalization;

namespace Picket302.Printing.Reports;

/// <summary>
/// Общие помощники формирования печатных форм: чтение значений строк запроса
/// слоя данных, форматирование денежных величин, суммы прописью подключаются
/// на уровне бланков переводов (ТЗ §8.4).
/// </summary>
public abstract class ReportBase
{
    /// <summary>Поставщик данных запросов слоя данных.</summary>
    protected readonly IReportDataProvider Данные;

    /// <summary>
    /// Создаёт отчёт поверх поставщика данных.
    /// </summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    protected ReportBase(IReportDataProvider dataProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProvider);

        Данные = dataProvider;
    }

    /// <summary>Исполняет запрос слоя данных.</summary>
    /// <param name="queryId">Идентификатор запроса.</param>
    /// <param name="values">Значения параметров.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Строки результата.</returns>
    protected Task<IReadOnlyList<IReportRow>> ВыполнитьAsync(
        string queryId,
        QueryParameterValues values,
        CancellationToken cancellationToken = default) =>
        Данные.RunQueryAsync(queryId, values, cancellationToken);

    /// <summary>Читает строку колонки.</summary>
    /// <param name="row">Строка.</param>
    /// <param name="column">Колонка.</param>
    /// <returns>Строковое представление либо пустая строка.</returns>
    protected static string Строка(IReportRow row, string column) => row.GetValue(column) switch
    {
        null => string.Empty,
        string текст => текст,
        DateTime момент => момент.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        decimal число => число.ToString("0.##", CultureInfo.InvariantCulture),
        long число => число.ToString(CultureInfo.InvariantCulture),
        int число => число.ToString(CultureInfo.InvariantCulture),
        var прочее => прочее.ToString() ?? string.Empty,
    };

    /// <summary>Читает денежную величину колонки.</summary>
    /// <param name="row">Строка.</param>
    /// <param name="column">Колонка.</param>
    /// <returns>Значение, 0 если пусто.</returns>
    protected static decimal Число(IReportRow row, string column) => row.GetValue(column) switch
    {
        null => 0m,
        decimal число => число,
        double число => (decimal)число,
        float число => (decimal)число,
        long число => число,
        int число => число,
        string текст when decimal.TryParse(текст, NumberStyles.Any, CultureInfo.InvariantCulture, out var разобрано) => разобрано,
        _ => 0m,
    };

    /// <summary>Форматирует денежную величину: два знака после разделителя.</summary>
    /// <param name="value">Значение.</param>
    /// <returns>Строка «0,00».</returns>
    protected static string Деньги(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Проверяет, что выборка не пуста.</summary>
    /// <param name="rows">Строки.</param>
    /// <param name="message">Текст ошибки.</param>
    protected static void ТребоватьНеПусто(IReadOnlyList<IReportRow> rows, string message)
    {
        if (rows.Count == 0)
        {
            throw new ReportValidationException(message);
        }
    }
}
