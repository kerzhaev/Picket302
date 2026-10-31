namespace Picket302.Domain.Dictionaries;

/// <summary>
/// Описание одного поля единой схемы «БД ↔ Шаблоны ↔ Импорт» (ТЗ §3.2).
/// </summary>
/// <param name="ColumnName">Имя колонки БД.</param>
/// <param name="Description">Описание сущности.</param>
/// <param name="TemplateTag">Тег привязки в шаблоне.</param>
/// <param name="ImportAliases">
/// Псевдонимы маппера импорта; пустой список — поле не импортируется.
/// </param>
/// <param name="Computed">
/// Признак вычисляемого поля: значение не хранится в БД, а формируется
/// вычислением (например, машинночитаемый штрих-код индекса).
/// </param>
public sealed record FieldDescriptor(
    string ColumnName,
    string Description,
    string TemplateTag,
    IReadOnlyList<string> ImportAliases,
    bool Computed = false)
{
    /// <summary>Признак поля, доступного мапперу импорта.</summary>
    public bool IsImportable => ImportAliases.Count > 0;
}
