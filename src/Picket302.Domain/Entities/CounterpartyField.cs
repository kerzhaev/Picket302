namespace Picket302.Domain.Entities;

/// <summary>
/// Пользовательское поле контрагента (ТЗ §3.1.9, таблица <c>counterparty_fields</c>).
/// </summary>
/// <remarks>
/// Имя ключа уникально в пределах контрагента
/// (<c>UNIQUE (counterparty_id, field_name)</c>). Пользовательское поле
/// автоматически становится доступным в дизайнере бланков как тег
/// <c>[Custom_&lt;имя&gt;]</c>.
/// </remarks>
public sealed class CounterpartyField
{
    /// <summary>Идентификатор поля (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Контрагент, которому принадлежит поле.</summary>
    public long CounterpartyId { get; set; }

    /// <summary>Имя ключа; уникально в пределах контрагента.</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Значение поля.</summary>
    public string? FieldValue { get; set; }

    /// <summary>Тип значения поля.</summary>
    public CounterpartyFieldType FieldType { get; set; } = CounterpartyFieldType.Text;

    /// <summary>Момент последнего изменения (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Префикс тега пользовательского поля в шаблонах.</summary>
    public const string CustomTagPrefix = "Custom_";

    /// <summary>
    /// Формирует тег шаблона для пользовательского поля (ТЗ §3.1.9).
    /// </summary>
    /// <param name="fieldName">Имя пользовательского поля.</param>
    /// <returns>Тег вида <c>[Custom_ИмяПоля]</c>.</returns>
    public static string BuildCustomTag(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return $"[{CustomTagPrefix}{fieldName}]";
    }
}
