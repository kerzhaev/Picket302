using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>
/// Реестр входящей корреспонденции в структуре «1С:Документооборот»
/// (ТЗ §8.4, функция 55: <c>Printing.Reports.OneCIncomingReport.BuildOneCCompatibleRegistry</c>).
/// </summary>
public sealed class OneCIncomingReport : ReportBase
{
    /// <summary>Идентификатор запроса слоя данных.</summary>
    public const string QueryId = "qrReport_OneCIncoming";

    /// <summary>Создаёт отчёт «Спецификация 1С».</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    public OneCIncomingReport(IReportDataProvider dataProvider)
        : base(dataProvider)
    {
    }

    /// <summary>
    /// Формирует проекцию журнала входящих в структуру колонок конфигурации
    /// «1С:Документооборот» (ТЗ §8.4, функция 55).
    /// </summary>
    /// <param name="date">Дата регистрации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма со структурой столбцов импорта 1С.</returns>
    public async Task<ReportDocument> BuildOneCCompatibleRegistry(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var значения = new QueryParameterValues();
        значения.Set("Date", date);

        var строки = await ВыполнитьAsync(QueryId, значения, cancellationToken).ConfigureAwait(false);

        var блок = new ReportBlock();
        блок.HeaderLines.Add("Реестр входящей корреспонденции — спецификация 1С");

        // Структура столбцов совпадает с требованиями импорта 1С
        // (ТЗ §8.4, ф.55: «Структура столбцов отчета совпадает с требованиями
        // импорта 1С»).
        блок.Columns.Add(new ReportColumn("РегНомер", 20m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("ДатаРегистрации", 26m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Корреспондент", 40m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("ИННКорреспондента", 22m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Тема", 50m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Содержание", 40m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("СостояниеДокумента", 24m, ReportColumnAlign.Center));

        foreach (var строка in строки)
        {
            блок.Rows.Add(new ReportRow(
                Строка(строка, "reg_number"),
                Строка(строка, "reg_date"),
                Строка(строка, "correspondent"),
                Строка(строка, "correspondent_inn"),
                Строка(строка, "subject"),
                Строка(строка, "summary"),
                Строка(строка, "doc_state")));
        }

        блок.TotalValues["Документов"] = строки.Count;
        блок.Totals.Add($"ИТОГО документов: {строки.Count}");

        return new ReportDocument
        {
            Title = "Реестр входящих (спецификация 1С)",
            QueryId = QueryId,
            Blocks = { блок },
        };
    }
}
