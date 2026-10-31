using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>
/// Стандартный сокращенный реестр передачи (ТЗ §8.4, функция 52:
/// <c>Printing.Reports.SimpleRegistryReport.GenerateSimpleRegistry</c>).
/// </summary>
/// <remarks>
/// Облегченная версия реестра для курьерских служб: №, Получатель, Адрес,
/// Подпись в получении. Выводится на одной странице при партии до 35 писем.
/// </remarks>
public sealed class SimpleRegistryReport : ReportBase
{
    /// <summary>Идентификатор запроса слоя данных.</summary>
    public const string QueryId = "qrReport_Envelopes";

    /// <summary>Число строк, помещающихся на одну страницу (ТЗ §8.4, ф.52).</summary>
    public const int RowsPerPage = 35;

    /// <summary>Создаёт отчёт.</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    public SimpleRegistryReport(IReportDataProvider dataProvider)
        : base(dataProvider)
    {
    }

    /// <summary>
    /// Формирует простой реестр передачи (ТЗ §8.4, функция 52).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма.</returns>
    public async Task<ReportDocument> GenerateSimpleRegistry(
        long[] mailIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailIds);

        var значения = new QueryParameterValues();
        значения.Set("SelectedMailIds", mailIds.ToList());

        var строки = await ВыполнитьAsync(QueryId, значения, cancellationToken).ConfigureAwait(false);

        var блок = new ReportBlock();
        блок.HeaderLines.Add("Простой реестр передачи");

        блок.Columns.Add(new ReportColumn("№", 10m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Получатель", 60m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Адрес", 80m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Подпись в получении", 40m, ReportColumnAlign.Left));

        for (var номер = 0; номер < строки.Count; номер++)
        {
            var строка = строки[номер];
            var адрес = string.Join(
                ", ",
                new[] { "ind_to", "str_to1", "str_to2", "str_to3", "str_to4", "str_to5", "str_to6" }
                    .Select(колонка => Строка(строка, колонка))
                    .Where(часть => часть.Length > 0));

            блок.Rows.Add(new ReportRow(
                (номер + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Строка(строка, "str_to6"),
                адрес,
                string.Empty));
        }

        return new ReportDocument
        {
            Title = "Простой реестр передачи",
            QueryId = QueryId,
            // Документ выводится в 1 страницу при партии до 35 писем.
            PageHeightMm = RowsPerPage * 6m + 60m,
            Blocks = { блок },
        };
    }
}
