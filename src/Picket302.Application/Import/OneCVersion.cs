namespace Picket302.Application.Import;

/// <summary>
/// Версия выгрузки 1С:Предприятие (ТЗ §8.5, функция 72: «Импорт контрагентов
/// из выгрузок 1С:Предприятие 7.7 и 8.x»).
/// </summary>
public enum OneCVersion
{
    /// <summary>
    /// 1С:Предприятие 7.7 — текстовый формат выгрузки справочника «Контрагенты».
    /// </summary>
    V77 = 0,

    /// <summary>
    /// 1С:Предприятие 8.x — XML- и текстовый форматы выгрузки справочника
    /// «Контрагенты».
    /// </summary>
    V8x = 1,
}

/// <summary>
/// Подписи форматов выгрузки 1С для описания источника записи контрагента
/// (ТЗ §8.5, функция 72; ТЗ §9.7: «Импорт &lt;формат&gt; от &lt;дата&gt;
/// из &lt;файл&gt;»).
/// </summary>
public static class OneCVersionExtensions
{
    /// <summary>Подпись формата выгрузки 1С 7.7.</summary>
    public const string FormatV77 = "1С 7.7";

    /// <summary>Подпись формата выгрузки 1С 8.x.</summary>
    public const string FormatV8x = "1С 8.x";

    /// <summary>
    /// Возвращает подпись формата выгрузки для поля «Источник» записи контрагента.
    /// </summary>
    /// <param name="version">Версия выгрузки 1С.</param>
    /// <returns>Подпись формата: «1С 7.7» либо «1С 8.x».</returns>
    public static string GetSourceFormatName(this OneCVersion version) => version switch
    {
        OneCVersion.V77 => FormatV77,
        _ => FormatV8x,
    };
}
