using Picket302.Domain.Queries;

namespace Picket302.Printing.Reports;

/// <summary>Выравнивание колонки таблицы отчёта.</summary>
public enum ReportColumnAlign
{
    /// <summary>По левому краю.</summary>
    Left = 0,

    /// <summary>По центру.</summary>
    Center = 1,

    /// <summary>По правому краю.</summary>
    Right = 2,
}

/// <summary>Колонка табличной части отчёта.</summary>
/// <param name="Name">Заголовок колонки.</param>
/// <param name="WidthMm">Ширина колонки, мм.</param>
/// <param name="Align">Выравнивание значений.</param>
public sealed record ReportColumn(string Name, decimal WidthMm, ReportColumnAlign Align = ReportColumnAlign.Left);

/// <summary>Строка табличной части отчёта.</summary>
public sealed class ReportRow
{
    /// <summary>Создаёт строку со значениями колонок.</summary>
    /// <param name="cells">Значения по колонкам.</param>
    public ReportRow(params string?[] cells)
    {
        Cells = cells.Select(значение => значение ?? string.Empty).ToArray();
    }

    /// <summary>Значения колонок.</summary>
    public IReadOnlyList<string> Cells { get; }

    /// <summary>Число колонок.</summary>
    public int Count => Cells.Count;
}

/// <summary>
/// Блок печатной формы: заголовок, шапка, табличная часть, итоги, подписи.
/// Бланк ф.107 печатается двумя блоками на одном листе.
/// </summary>
public sealed class ReportBlock
{
    /// <summary>Подпись блока (например, «Экземпляр отправителя»); <see langword="null"/> — без подписи.</summary>
    public string? Caption { get; init; }

    /// <summary>Строки шапки формы (реквизиты отправителя, наименование формы).</summary>
    public List<string> HeaderLines { get; } = new();

    /// <summary>Колонки табличной части.</summary>
    public List<ReportColumn> Columns { get; } = new();

    /// <summary>Строки табличной части.</summary>
    public List<ReportRow> Rows { get; } = new();

    /// <summary>Строки итогов («Итого», суммы платы и ценности).</summary>
    public List<string> Totals { get; } = new();

    /// <summary>
    /// Числовые значения итогов по наименованиям («Плата», «Ценность», «Вес»,
    /// «Отправлений»); используются для контроля сходимости итогов.
    /// </summary>
    public Dictionary<string, decimal> TotalValues { get; } = new();

    /// <summary>Строки нижнего колонтитула (блок подписей почтовых работников).</summary>
    public List<string> FooterLines { get; } = new();
}

/// <summary>
/// Печатная форма реестра/описи/журнала (ТЗ §8.4, функции 50–64).
/// </summary>
public sealed class ReportDocument
{
    /// <summary>Наименование печатной формы.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Идентификатор запроса слоя данных, породившего форму (ТЗ §4).</summary>
    public string QueryId { get; init; } = string.Empty;

    /// <summary>Ширина листа, мм.</summary>
    public decimal PageWidthMm { get; init; } = 210m;

    /// <summary>Высота листа, мм.</summary>
    public decimal PageHeightMm { get; init; } = 297m;

    /// <summary>Блоки формы; блоки печатаются на одном листе (ф.107 — два экземпляра).</summary>
    public List<ReportBlock> Blocks { get; } = new();

    /// <summary>Основной (первый) блок формы.</summary>
    public ReportBlock Main => Blocks[0]
        ?? throw new InvalidOperationException("Печатная форма не содержит ни одного блока.");
}

/// <summary>
/// Строка данных отчёта: значения по именам колонок запроса слоя данных
/// (точные псевдонимы колонок из ТЗ §4.2).
/// </summary>
public interface IReportRow
{
    /// <summary>Имена колонок строки.</summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Читает значение колонки.</summary>
    /// <param name="column">Псевдоним колонки запроса.</param>
    /// <returns>Значение либо <see langword="null"/>, если колонки нет.</returns>
    object? GetValue(string column);
}

/// <summary>
/// Поставщик данных печатных форм: исполняет запросы слоя данных
/// (ТЗ §4: «Слой запросов — механизм хранения SQL-проекции в файле бланка
/// или отчёта, а не в коде»).
/// </summary>
public interface IReportDataProvider
{
    /// <summary>
    /// Исполняет встроенный запрос слоя данных по идентификатору.
    /// </summary>
    /// <param name="queryId">Идентификатор запроса (например, «qrReport_Envelopes»).</param>
    /// <param name="values">Значения параметров; передаются в СУБД только привязкой.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Строки результата с точными псевдонимами колонок.</returns>
    Task<IReadOnlyList<IReportRow>> RunQueryAsync(
        string queryId,
        QueryParameterValues values,
        CancellationToken cancellationToken = default);
}

/// <summary>Фильтр расширенного реестра (ТЗ §8.4, функция 51: ReportFilter).</summary>
/// <param name="DateFrom">Начало диапазона дат создания.</param>
/// <param name="DateTo">Конец диапазона дат создания.</param>
/// <param name="SenderId">Отправитель.</param>
/// <param name="MailCategory">Категория отправления; <see langword="null"/> — любая.</param>
/// <param name="MailIds">Явный список отправлений; <see langword="null"/> — не ограничено.</param>
public sealed record ReportFilter(
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    long? SenderId = null,
    int? MailCategory = null,
    IReadOnlyList<long>? MailIds = null)
{
    /// <summary>Параметры слоя данных для фильтра (DateFrom/DateTo/SenderId/MailCategory).</summary>
    public QueryParameterValues ToParameters()
    {
        var значения = new QueryParameterValues();

        if (DateFrom is { } начало)
        {
            значения.Set("DateFrom", начало);
        }

        if (DateTo is { } конец)
        {
            значения.Set("DateTo", конец);
        }

        if (SenderId is { } отправитель)
        {
            значения.Set("SenderId", отправитель);
        }

        if (MailCategory is { } категория)
        {
            значения.Set("MailCategory", категория);
        }

        if (MailIds is { } список)
        {
            значения.Set("SelectedMailIds", список);
        }

        return значения;
    }
}

/// <summary>
/// Нарушение правил формирования реестра (ТЗ §8.4, функция 50: «Письма от
/// разных отправителей в один реестр включить нельзя»).
/// </summary>
/// <param name="message">Текст ошибки валидации выборки.</param>
public sealed class ReportValidationException(string message) : Exception(message);
