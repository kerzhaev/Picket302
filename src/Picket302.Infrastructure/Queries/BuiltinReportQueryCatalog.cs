using Picket302.Domain.Queries;

namespace Picket302.Infrastructure.Queries;

/// <summary>
/// Каталог встроенных запросов слоя данных (ТЗ §4.1: «Каждый печатный отчет
/// или пакетный бланк содержит файл определения запроса данных query.xml»;
/// §4.2 — восемь запросов с точными именами колонок).
/// </summary>
/// <remarks>
/// Определения хранятся файлами (встроенные ресурсы
/// <c>Infrastructure/Templates/Queries/*.xml</c>), а не в коде. Для текущей
/// поставки обязательна команда диалекта Sqlite; PostgreSQL-вариант каждого
/// запроса хранится для совместимости.
/// </remarks>
public sealed class BuiltinReportQueryCatalog
{
    /// <summary>Префикс имени встроенного ресурса.</summary>
    public const string ResourcePrefix = "Picket302.Infrastructure.Templates.Queries.";

    /// <summary>Идентификаторы восьми встроенных запросов ТЗ §4.2.</summary>
    public static readonly IReadOnlyList<string> QueryIds = new[]
    {
        "qrReport_Envelopes",
        "qrQRcode_Fetch",
        "qrReport_Inventory107",
        "qrReport_IncomingJournal",
        "qrReport_MailingStats",
        "qrReport_GroupEnvelopes",
        "qrReport_PrintHistoryReport",
        "qrReport_MoneyTransfer112ep",
    };

    /// <summary>Идентификаторы служебных запросов печатных форм ТЗ §8.4.</summary>
    public static readonly IReadOnlyList<string> AuxiliaryQueryIds = new[]
    {
        "qrReport_F103",
        "qrReport_F103_ByIds",
        "qrReport_OneCIncoming",
        "qrReport_AuxForm",
    };

    private static readonly ReportQueryXmlSerializer Сериализатор = new();

    private static readonly Lazy<IReadOnlyDictionary<string, ReportQueryDefinition>> Кэш =
        new(ЗагрузитьВсе, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Возвращает определение запроса по идентификатору.</summary>
    /// <param name="queryId">Идентификатор («qrReport_Envelopes»).</param>
    /// <returns>Определение запроса.</returns>
    /// <exception cref="KeyNotFoundException">Запрос не найден.</exception>
    public static ReportQueryDefinition Get(string queryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);

        if (Кэш.Value.TryGetValue(queryId, out var определение))
        {
            return определение;
        }

        throw new KeyNotFoundException($"Встроенный запрос слоя данных «{queryId}» не найден.");
    }

    /// <summary>Возвращает все встроенные определения запросов.</summary>
    /// <returns>Список определений.</returns>
    public static IReadOnlyList<ReportQueryDefinition> GetAll() => Кэш.Value.Values.ToList();

    /// <summary>Разбирает query.xml из встроенного ресурса.</summary>
    /// <param name="queryId">Идентификатор.</param>
    /// <returns>Определение.</returns>
    public static ReportQueryDefinition LoadFromResource(string queryId)
    {
        var имяРесурса = ResourcePrefix + queryId + ".xml";
        var сборка = typeof(BuiltinReportQueryCatalog).Assembly;

        using var поток = сборка.GetManifestResourceStream(имяРесурса)
            ?? throw new InvalidOperationException($"Встроенный ресурс запроса «{имяРесурса}» не найден.");

        using var читатель = new StreamReader(поток);
        var текст = читатель.ReadToEnd();

        return Сериализатор.Deserialize(текст);
    }

    private static IReadOnlyDictionary<string, ReportQueryDefinition> ЗагрузитьВсе()
    {
        var словарь = new Dictionary<string, ReportQueryDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var идентификатор in QueryIds.Concat(AuxiliaryQueryIds))
        {
            var определение = LoadFromResource(идентификатор);
            словарь[определение.Id] = определение;
        }

        return словарь;
    }
}
