using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>
/// Журнал регистрации входящей корреспонденции (ТЗ §8.4, функция 54:
/// <c>Printing.Reports.IncomingJournalReport.BuildIncomingJournal</c>).
/// </summary>
public sealed class IncomingJournalReport : ReportBase
{
    /// <summary>Идентификатор запроса слоя данных (ТЗ §8.4, ф.54).</summary>
    public const string QueryId = "qrReport_IncomingJournal";

    /// <summary>Создаёт отчёт журнала входящих.</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    public IncomingJournalReport(IReportDataProvider dataProvider)
        : base(dataProvider)
    {
    }

    /// <summary>
    /// Формирует сброшюрованный журнал учета входящих писем (ТЗ §8.4, функция 54).
    /// </summary>
    /// <param name="from">Начало периода.</param>
    /// <param name="to">Конец периода.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма журнала.</returns>
    public async Task<ReportDocument> BuildIncomingJournal(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var значения = new QueryParameterValues();
        значения.Set("DateFrom", from);
        значения.Set("DateTo", to);

        var строки = await ВыполнитьAsync(QueryId, значения, cancellationToken).ConfigureAwait(false);

        var блок = new ReportBlock();
        блок.HeaderLines.Add("ЖУРНАЛ РЕГИСТРАЦИИ ВХОДЯЩЕЙ КОРРЕСПОНДЕНЦИИ");
        блок.HeaderLines.Add(
            $"Период: {from:dd.MM.yyyy} — {to:dd.MM.yyyy}");

        блок.Columns.Add(new ReportColumn("Вх. №", 18m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Дата", 22m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Отправитель", 45m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("ИНН", 22m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Содержание (тема)", 55m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Отметка об исполнении", 28m, ReportColumnAlign.Left));

        foreach (var строка in строки)
        {
            блок.Rows.Add(new ReportRow(
                Строка(строка, "inc_num"),
                Строка(строка, "reg_date"),
                Строка(строка, "sender_name"),
                Строка(строка, "sender_inn"),
                Строка(строка, "subject"),
                Строка(строка, "execution_mark")));
        }

        блок.TotalValues["Документов"] = строки.Count;
        блок.Totals.Add($"ИТОГО документов за период: {строки.Count}");
        блок.FooterLines.Add("Журнал прошит, число листов: ______");

        return new ReportDocument
        {
            Title = "Журнал входящей корреспонденции",
            QueryId = QueryId,
            Blocks = { блок },
        };
    }
}
