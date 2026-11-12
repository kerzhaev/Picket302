using System.Reflection;

namespace Picket302.Infrastructure.Templates;

/// <summary>Запись о поставляемом XSLT-стиле документа (ТЗ §8.4, функция 67).</summary>
/// <param name="Id">Идентификатор стиля (имя ресурса без расширения).</param>
/// <param name="Name">Наименование документа по-русски.</param>
public sealed record DocumentStyleRecord(string Id, string Name);

/// <summary>
/// Библиотека поставляемых XSLT-стилей документов (ТЗ §8.4, функция 67:
/// «12 XML + 16 XSLT»). XML-сторона — определения запросов слоя данных
/// (<c>Templates/Queries/*.xml</c>, см. <c>BuiltinReportQueryCatalog</c>);
/// XSLT-сторона — 16 стилей в <c>Templates/Documents/*.xslt</c>, каждый
/// преобразует модель <c>&lt;parameters&gt;</c> в WordprocessingML-документ
/// для конвейера функции 68 штатным <see cref="System.Xml.Xsl.XslCompiledTransform"/>
/// — без MS Office и без внешних сущностей.
/// </summary>
public static class DocumentStyleLibrary
{
    /// <summary>Префикс имён встроенных ресурсов стилей.</summary>
    public const string ResourcePrefix = "Picket302.Infrastructure.Templates.Documents.";

    private static readonly DocumentStyleRecord[] Стили =
    [
        new("notify-arrival", "Уведомление о поступлении отправления"),
        new("claim-nondelivery", "Претензия о недоставке отправления"),
        new("act-reconciliation", "Акт сверки взаиморасчётов"),
        new("cover-letter", "Сопроводительное письмо"),
        new("enclosure-list", "Опись вложения"),
        new("power-of-attorney-corr", "Доверенность на получение корреспонденции"),
        new("refusal-notice", "Уведомление об отказе от получения"),
        new("documents-request", "Запрос документов"),
        new("gratitude-letter", "Благодарственное письмо"),
        new("payment-reminder", "Напоминание об оплате"),
        new("requisites-change", "Уведомление о смене реквизитов"),
        new("work-completion-act", "Акт выполненных работ"),
        new("receipt-confirmation", "Письмо-подтверждение получения"),
        new("documents-receipt-ack", "Расписка в получении документов"),
        new("package-cover-sheet", "Сопроводительный лист к пакету"),
        new("storage-expiry-notice", "Уведомление об истечении срока хранения"),
    ];

    /// <summary>Перечень поставляемых стилей (16 записей).</summary>
    public static IReadOnlyList<DocumentStyleRecord> Styles => Стили;

    /// <summary>
    /// Загружает текст стиля из встроенного ресурса.
    /// </summary>
    /// <param name="styleId">Идентификатор стиля.</param>
    /// <returns>Текст XSLT.</returns>
    /// <exception cref="FileNotFoundException">Стиль не найден.</exception>
    public static string LoadFromResource(string styleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleId);

        var имя = ResourcePrefix + styleId + ".xslt";
        var сборка = Assembly.GetExecutingAssembly();

        using var поток = сборка.GetManifestResourceStream(имя)
            ?? throw new FileNotFoundException($"Стиль «{styleId}» отсутствует в ресурсах сборки.", имя);

        using var читатель = new StreamReader(поток);

        return читатель.ReadToEnd();
    }
}
