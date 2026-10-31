using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>
/// Стандартная опись вложений ф.107 (ТЗ §8.4, функция 53:
/// <c>Printing.Reports.InventoryF107Report.GenerateF107</c>).
/// </summary>
/// <remarks>
/// Генерирует 2 экземпляра бланка ф.107 на одном листе — «Экземпляр отправителя»
/// и «Экземпляр почты» — со списком предметов, количеством и суммой ценности.
/// Итоговые суммы каждого экземпляра обязаны сходиться с суммой позиций;
/// опись без позиций вложения не формируется.
/// </remarks>
public sealed class InventoryF107Report : ReportBase
{
    /// <summary>Идентификатор запроса слоя данных описи (ТЗ §8.4, ф.53).</summary>
    public const string QueryId = "qrReport_Inventory107";

    /// <summary>Сообщение об отсутствии позиций вложения.</summary>
    public const string NoItemsError = "Опись без позиций вложения не формируется.";

    /// <summary>Подпись первого экземпляра (ТЗ §8.4, ф.53).</summary>
    public const string SenderCopyCaption = "Экземпляр отправителя";

    /// <summary>Подпись второго экземпляра.</summary>
    public const string PostCopyCaption = "Экземпляр почты";

    /// <summary>Создаёт отчёт описи ф.107.</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    public InventoryF107Report(IReportDataProvider dataProvider)
        : base(dataProvider)
    {
    }

    /// <summary>
    /// Формирует опись ф.107 двумя экземплярами на одном листе.
    /// </summary>
    /// <param name="inventoryId">Идентификатор описи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма с двумя блоками.</returns>
    /// <exception cref="ReportValidationException">Опись не содержит позиций.</exception>
    public async Task<ReportDocument> GenerateF107(
        long inventoryId,
        CancellationToken cancellationToken = default)
    {
        var значения = new QueryParameterValues();
        значения.Set("InventoryId", inventoryId);

        var строки = await ВыполнитьAsync(QueryId, значения, cancellationToken).ConfigureAwait(false);
        ТребоватьНеПусто(строки, NoItemsError);

        var наименование = Строка(строки[0], "inv_name");
        var кому = Строка(строки[0], "komu");
        var куда = Строка(строки[0], "kuda");
        var число = (int)Число(строки[0], "count_konv");

        var сумма = строки.Sum(строка => Число(строка, "total_item_cost"));

        var блок = СоставитьБлок(SenderCopyCaption, наименование, кому, куда, число, строки, сумма);
        var блокПочты = СоставитьБлок(PostCopyCaption, наименование, кому, куда, число, строки, сумма);

        // Оба экземпляра печатаются на одном листе А4 (ТЗ §8.4, ф.53).
        return new ReportDocument
        {
            Title = "Опись вложений ф.107",
            QueryId = QueryId,
            PageWidthMm = 210m,
            PageHeightMm = 297m,
            Blocks = { блок, блокПочты },
        };
    }

    private static ReportBlock СоставитьБлок(
        string подпись,
        string наименование,
        string кому,
        string куда,
        int число,
        IReadOnlyList<IReportRow> строки,
        decimal сумма)
    {
        var блок = new ReportBlock { Caption = подпись };

        блок.HeaderLines.Add("ОПИСЬ вложения в почтовое отправление");
        блок.HeaderLines.Add("Наименование: " + наименование);
        блок.HeaderLines.Add("Кому: " + кому);
        блок.HeaderLines.Add("Куда: " + куда);

        блок.Columns.Add(new ReportColumn("№", 10m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Наименование предметов", 70m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Кол-во", 20m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Стоимость, руб.", 35m, ReportColumnAlign.Right));
        блок.Columns.Add(new ReportColumn("Сумма, руб.", 35m, ReportColumnAlign.Right));

        var итого = 0m;

        for (var номер = 0; номер < строки.Count; номер++)
        {
            var строка = строки[номер];
            var количество = Число(строка, "kol");
            var стоимость = Число(строка, "cost");
            var суммаПозиции = Число(строка, "total_item_cost");
            итого += суммаПозиции;

            блок.Rows.Add(new ReportRow(
                (номер + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Строка(строка, "det_name"),
                количество.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                Деньги(стоимость),
                Деньги(суммаПозиции)));
        }

        блок.TotalValues["Итого"] = итого;
        блок.TotalValues["Объявленная ценность"] = сумма;
        блок.Totals.Add(
            $"Итого предметов: {строки.Count}, мест: {число}, сумма ценности: {Деньги(итого)} руб.");

        return блок;
    }
}
