using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>
/// Печатные формы статистики рассылок и состава групп (ТЗ §8.4, функции 63:
/// <c>Printing.Reports.StatsAndGroupReports</c>).
/// </summary>
/// <remarks>
/// Формирует сводный отчет по адресам заданной группы с итоговым числом
/// контрагентов, а также статистику почтовых рассылок по категориям.
/// Пустая группа формирует отчет с уведомлением «Нет данных».
/// </remarks>
public sealed class StatsAndGroupReports : ReportBase
{
    /// <summary>Идентификатор запроса состава группы (ТЗ §8.4, ф.63).</summary>
    public const string GroupQueryId = "qrReport_GroupEnvelopes";

    /// <summary>Идентификатор запроса статистики рассылок.</summary>
    public const string MailingStatsQueryId = "qrReport_MailingStats";

    /// <summary>Уведомление пустой выборки.</summary>
    public const string NoDataNotice = "Нет данных";

    /// <summary>Создаёт отчёт статистики и групп.</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    public StatsAndGroupReports(IReportDataProvider dataProvider)
        : base(dataProvider)
    {
    }

    /// <summary>
    /// Формирует сводный отчет по адресам заданной группы (ТЗ §8.4, функция 63).
    /// </summary>
    /// <param name="groupId">Идентификатор группы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма; пустая группа даёт строку «Нет данных».</returns>
    public async Task<ReportDocument> GenerateGroupReport(
        long groupId,
        CancellationToken cancellationToken = default)
    {
        var значения = new QueryParameterValues();
        значения.Set("GroupId", groupId);

        var строки = await ВыполнитьAsync(GroupQueryId, значения, cancellationToken).ConfigureAwait(false);

        var блок = new ReportBlock();
        блок.HeaderLines.Add("Состав группы адресатов");

        блок.Columns.Add(new ReportColumn("№", 10m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Группа", 30m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Индекс", 18m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Адрес", 80m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Примечание", 30m, ReportColumnAlign.Left));

        foreach (var строка in строки)
        {
            var адрес = string.Join(
                ", ",
                new[] { "str_to1", "str_to2", "str_to3", "str_to4", "str_to5", "str_to6" }
                    .Select(колонка => Строка(строка, колонка))
                    .Where(часть => часть.Length > 0));

            блок.Rows.Add(new ReportRow(
                (блок.Rows.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Строка(строка, "group_name"),
                Строка(строка, "ind_to"),
                адрес,
                Строка(строка, "prim")));
        }

        // Пустая группа формирует отчет с уведомлением «Нет данных».
        блок.TotalValues["Контрагентов"] = строки.Count;
        блок.Totals.Add(строки.Count == 0
            ? NoDataNotice
            : $"ИТОГО контрагентов в группе: {строки.Count}");

        return new ReportDocument
        {
            Title = "Печать состава группы",
            QueryId = GroupQueryId,
            Blocks = { блок },
        };
    }

    /// <summary>
    /// Формирует статистику почтовых рассылок по категориям
    /// (запрос qrReport_MailingStats, ТЗ §4.2).
    /// </summary>
    /// <param name="from">Начало периода.</param>
    /// <param name="to">Конец периода.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма статистики.</returns>
    public async Task<ReportDocument> GenerateMailingStats(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var значения = new QueryParameterValues();
        значения.Set("DateFrom", from);
        значения.Set("DateTo", to);

        var строки = await ВыполнитьAsync(MailingStatsQueryId, значения, cancellationToken).ConfigureAwait(false);

        var блок = new ReportBlock();
        блок.HeaderLines.Add("Статистика почтовых рассылок по категориям");
        блок.HeaderLines.Add($"Период: {from:dd.MM.yyyy} — {to:dd.MM.yyyy}");

        блок.Columns.Add(new ReportColumn("Категория", 40m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Дата", 26m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Конвертов", 20m, ReportColumnAlign.Right));
        блок.Columns.Add(new ReportColumn("Плата, руб.", 26m, ReportColumnAlign.Right));
        блок.Columns.Add(new ReportColumn("Ценность, руб.", 30m, ReportColumnAlign.Right));

        var итого = 0m;
        var итогПлата = 0m;
        var итогЦенность = 0m;

        foreach (var строка in строки)
        {
            var конвертов = Число(строка, "count_konv");
            var плата = Число(строка, "total_fee");
            var ценность = Число(строка, "total_val");

            итого += конвертов;
            итогПлата += плата;
            итогЦенность += ценность;

            блок.Rows.Add(new ReportRow(
                Строка(строка, "gr_name"),
                Строка(строка, "date_stat"),
                конвертов.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                Деньги(плата),
                Деньги(ценность)));
        }

        блок.TotalValues["Конвертов"] = итого;
        блок.TotalValues["Плата"] = итогПлата;
        блок.TotalValues["Ценность"] = итогЦенность;
        блок.Totals.Add(строки.Count == 0
            ? NoDataNotice
            : $"ИТОГО конвертов: {итого:0}, плата {Деньги(итогПлата)} руб., ценность {Деньги(итогЦенность)} руб.");

        return new ReportDocument
        {
            Title = "Статистика рассылок",
            QueryId = MailingStatsQueryId,
            Blocks = { блок },
        };
    }
}
