using System.Globalization;

using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>
/// Форма реестра почтовых отправлений ф.103 (ТЗ §8.4, функция 50:
/// <c>Printing.Reports.Form103Report.GenerateF103</c>).
/// </summary>
/// <remarks>
/// <para>Формирует регламентный отчет Почты России: титульная часть с реквизитами
/// отправителя, табличная часть начислений (исходящий номер, ШПИ/баркод,
/// получатель, вес, ценность, плата), расчет платы за пересылку, блок подписей
/// почтовых работников.</para>
/// <para>Письма от разных отправителей в один реестр включить нельзя —
/// выборка валидируется по отправителю (ошибка валидации выборки).
/// Итоговая строка платы должна сходиться с суммой строк табличной части.</para>
/// </remarks>
public sealed class Form103Report : ReportBase
{
    /// <summary>Идентификатор запроса слоя данных реестра ф.103.</summary>
    public const string QueryId = "qrReport_F103_ByIds";

    /// <summary>Сообщение об ошибке смешения отправителей (ТЗ §8.4, функция 50).</summary>
    public const string MixedSendersError =
        "Письма от разных отправителей в один реестр включить нельзя (ошибка валидации выборки).";

    /// <summary>Сообщение о пустой выборке.</summary>
    public const string EmptySelectionError =
        "В реестр ф.103 не выбрано ни одного отправления.";

    /// <summary>Создаёт отчёт ф.103.</summary>
    /// <param name="dataProvider">Поставщик данных запросов слоя данных.</param>
    public Form103Report(IReportDataProvider dataProvider)
        : base(dataProvider)
    {
    }

    /// <summary>
    /// Формирует печатную форму реестра ф.103 (ТЗ §8.4, функция 50).
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="senderId">Идентификатор отправителя.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Печатная форма реестра.</returns>
    /// <exception cref="ReportValidationException">
    /// Выборка пуста либо содержит письма разных отправителей.
    /// </exception>
    public async Task<ReportDocument> GenerateF103(
        long[] mailIds,
        long senderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailIds);

        if (mailIds.Length == 0)
        {
            throw new ReportValidationException(EmptySelectionError);
        }

        var значения = new QueryParameterValues();
        значения.Set("SelectedMailIds", mailIds.ToList());

        var строки = await ВыполнитьAsync(QueryId, значения, cancellationToken).ConfigureAwait(false);
        ТребоватьНеПусто(строки, EmptySelectionError);

        var смешение = строки
            .Where(строка => Число(строка, "sender_id") != senderId)
            .ToList();

        if (смешение.Count > 0)
        {
            throw new ReportValidationException(MixedSendersError);
        }

        var блок = new ReportBlock();
        блок.HeaderLines.Add("ФОРМА Ф.103");
        блок.HeaderLines.Add("Реестр почтовых отправлений");
        блок.HeaderLines.Add(
            "Отправитель: " + Строка(строки[0], "sender_name"));

        блок.Columns.Add(new ReportColumn("№", 10m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Исх. №", 22m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Дата отправки", 22m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Получатель", 55m, ReportColumnAlign.Left));
        блок.Columns.Add(new ReportColumn("Индекс", 18m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("ШПИ (баркод)", 30m, ReportColumnAlign.Center));
        блок.Columns.Add(new ReportColumn("Вес, г", 14m, ReportColumnAlign.Right));
        блок.Columns.Add(new ReportColumn("Ценность, руб.", 22m, ReportColumnAlign.Right));
        блок.Columns.Add(new ReportColumn("Плата, руб.", 22m, ReportColumnAlign.Right));

        var итогПлата = 0m;
        var итогЦенность = 0m;
        var итогВес = 0m;

        for (var номер = 0; номер < строки.Count; номер++)
        {
            var строка = строки[номер];
            var плата = Число(строка, "item_fee");
            var ценность = Число(строка, "item_value");
            var вес = Число(строка, "item_weight");

            итогПлата += плата;
            итогЦенность += ценность;
            итогВес += вес;

            блок.Rows.Add(new ReportRow(
                (номер + 1).ToString(CultureInfo.InvariantCulture),
                Строка(строка, "out_num"),
                Строка(строка, "dispatch_date"),
                Строка(строка, "recipient_name"),
                Строка(строка, "recipient_zip"),
                Строка(строка, "barcode"),
                вес.ToString("0.###", CultureInfo.InvariantCulture),
                Деньги(ценность),
                Деньги(плата)));
        }

        // Итоги должны сходиться с суммой строк табличной части (ТЗ §8.4, ф.50).
        блок.TotalValues["Отправлений"] = строки.Count;
        блок.TotalValues["Вес"] = итогВес;
        блок.TotalValues["Ценность"] = итогЦенность;
        блок.TotalValues["Плата"] = итогПлата;
        блок.Totals.Add(
            $"ИТОГО: отправлений {строки.Count}, вес {итогВес:0.###} г, " +
            $"ценность {Деньги(итогЦенность)} руб., плата {Деньги(итогПлата)} руб.");
        блок.FooterLines.Add("Оператор: _______________  Подпись: _______________");
        блок.FooterLines.Add("Приемщик: _______________  Подпись: _______________");

        return new ReportDocument
        {
            Title = "Реестр почтовых отправлений ф.103",
            QueryId = QueryId,
            Blocks = { блок },
        };
    }

    /// <summary>
    /// Проверяет сходимость итогов: сумма платы строк равна значению итога.
    /// </summary>
    /// <param name="document">Печатная форма.</param>
    /// <returns><see langword="true"/>, если итоги сходятся.</returns>
    public static bool TotalsMatch(ReportDocument document)
    {
        var блок = document.Main;
        var сумма = блок.Rows
            .Select(строка => строка.Cells.Count > 8 && decimal.TryParse(строка.Cells[8], NumberStyles.Any, CultureInfo.InvariantCulture, out var плата) ? плата : 0m)
            .Sum();

        return блок.TotalValues.TryGetValue("Плата", out var итог) && сумма == итог;
    }
}
