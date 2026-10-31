using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>
/// Расширенный реестр отправлений (ТЗ §8.4, функция 51:
/// <c>Printing.Reports.ExtendedRegistryReport.GenerateExtendedRegistry</c>).
/// </summary>
/// <remarks>
/// Управленческий реестр с дополнительными графами: номер телефона,
/// внутренний номер дела, примечание, статус доставки.
/// </remarks>
public sealed class ExtendedRegistryReport : ReportBase
{
    /// <summary>Идентификатор запроса слоя данных.</summary>
    public const string QueryId = "qrReport_Envelopes";

    /// <summary>Создаёт отчёт.</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    public ExtendedRegistryReport(IReportDataProvider dataProvider)
        : base(dataProvider)
    {
    }

    /// <summary>
    /// Формирует расширенный реестр (ТЗ §8.4, функция 51).
    /// </summary>
    /// <param name="filter">Условия отбора отправлений.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма.</returns>
    public async Task<ReportDocument> GenerateExtendedRegistry(
        ReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var значения = filter.ToParameters();
        var строки = await ВыполнитьAsync(QueryId, значения, cancellationToken).ConfigureAwait(false);

        var блок = new ReportBlock();
        блок.HeaderLines.Add("Расширенный реестр почтовых отправлений");

        блок.Columns.Add(new ReportColumn("№", 10m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Получатель", 50m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Адрес", 62m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Телефон", 24m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Номер дела", 20m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Примечание", 28m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Статус доставки", 22m, ReportColumnAlign.Center));

        var номер = 0;

        foreach (var строка in строки)
        {
            номер++;

            var адрес = string.Join(
                ", ",
                new[] { "str_to1", "str_to2", "str_to3", "str_to4", "str_to5", "str_to6" }
                    .Select(колонка => Строка(строка, колонка))
                    .Where(часть => часть.Length > 0));

            // Пользовательские примечания по каждому отправлению (ТЗ §8.4, ф.51).
            блок.Rows.Add(new ReportRow(
                номер.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Строка(строка, "str_to6"),
                адрес,
                Строка(строка, "str_to5"),
                Строка(строка, "mail_id"),
                Строка(строка, "prim"),
                Строка(строка, "vip") == "1" ? "Приоритетно" : "В обработке"));
        }

        return new ReportDocument
        {
            Title = "Расширенный реестр отправлений",
            QueryId = QueryId,
            Blocks = { блок },
        };
    }
}
