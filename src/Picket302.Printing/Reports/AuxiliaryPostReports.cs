using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>
/// Дополнительные ведомственные формы Почты — формы 5 и 6 (ТЗ §8.4, функция 57:
/// <c>Printing.Reports.AuxiliaryPostReports.GenerateAuxForm</c>).
/// </summary>
public sealed class AuxiliaryPostReports : ReportBase
{
    /// <summary>Идентификатор запроса слоя данных ведомости.</summary>
    public const string QueryId = "qrReport_AuxForm";

    /// <summary>Номер поддерживаемой формы 5 (ведомость сдачи партионной почты).</summary>
    public const int FormNumber5 = 5;

    /// <summary>Номер поддерживаемой формы 6 (сопроводительная ведомость).</summary>
    public const int FormNumber6 = 6;

    /// <summary>Создаёт отчёт дополнительных форм.</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    public AuxiliaryPostReports(IReportDataProvider dataProvider)
        : base(dataProvider)
    {
    }

    /// <summary>
    /// Формирует специализированный ведомственный бланк сдачи партионной почты
    /// (ТЗ §8.4, функция 57).
    /// </summary>
    /// <param name="formNumber">Номер ведомственной формы: 5 или 6.</param>
    /// <param name="mailIds">Идентификаторы отправлений партии.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма ведомости.</returns>
    /// <exception cref="ReportValidationException">Номер формы не поддерживается.</exception>
    public async Task<ReportDocument> GenerateAuxForm(
        int formNumber,
        long[] mailIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailIds);

        if (formNumber is not (FormNumber5 or FormNumber6))
        {
            throw new ReportValidationException(
                $"Дополнительная форма №{formNumber} не поддерживается: доступны формы 5 и 6.");
        }

        var значения = new QueryParameterValues();
        значения.Set("SelectedMailIds", mailIds.ToList());

        var строки = await ВыполнитьAsync(QueryId, значения, cancellationToken).ConfigureAwait(false);

        var блок = new ReportBlock();
        блок.HeaderLines.Add($"ФОРМА {formNumber}");
        блок.HeaderLines.Add(formNumber == FormNumber5
            ? "Ведомость сдачи партионной почты"
            : "Сопроводительная ведомость партионной почты");

        блок.Columns.Add(new ReportColumn("№", 10m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Исх. №", 22m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("ШПИ", 30m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Индекс места назначения", 30m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Вес, г", 14m, ReportColumnAlign.Right));
        блок.Columns.Add(new ReportColumn("Плата, руб.", 22m, ReportColumnAlign.Right));
        блок.Columns.Add(new ReportColumn("Ценность, руб.", 22m, ReportColumnAlign.Right));

        var итогПлата = 0m;
        var итогЦенность = 0m;
        var итогВес = 0m;

        for (var номер = 0; номер < строки.Count; номер++)
        {
            var строка = строки[номер];
            var вес = Число(строка, "item_weight");
            var плата = Число(строка, "item_fee");
            var ценность = Число(строка, "item_value");

            итогВес += вес;
            итогПлата += плата;
            итогЦенность += ценность;

            блок.Rows.Add(new ReportRow(
                (номер + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Строка(строка, "out_num"),
                Строка(строка, "barcode"),
                Строка(строка, "recipient_zip"),
                вес.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                Деньги(плата),
                Деньги(ценность)));
        }

        блок.TotalValues["Вес"] = итогВес;
        блок.TotalValues["Плата"] = итогПлата;
        блок.TotalValues["Ценность"] = итогЦенность;
        блок.Totals.Add(
            $"ИТОГО: мест {строки.Count}, вес {итогВес:0.###} г, " +
            $"плата {Деньги(итогПлата)} руб., ценность {Деньги(итогЦенность)} руб.");

        return new ReportDocument
        {
            Title = $"Дополнительная форма {formNumber}",
            QueryId = QueryId,
            Blocks = { блок },
        };
    }
}
