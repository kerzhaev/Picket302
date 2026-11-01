namespace Picket302.App.UI.Components;

/// <summary>
/// Выбор отправителя по первым буквам наименования
/// (ТЗ §8.1, функция 16: «Текущий отправитель» как глобальная сущность;
/// выбор отправителя по первым буквам).
/// </summary>
/// <remarks>
/// Оператор набирает начало наименования отправителя, и справочник отправителей
/// прокручивается до первой записи, наименование которой начинается с набранных
/// букв. Сравнение регистронезависимое; при отсутствии совпадений текущий
/// отправитель не меняется.
/// </remarks>
public static class SenderQuickSelect
{
    /// <summary>
    /// Находит отправителя по началу наименования.
    /// </summary>
    /// <param name="отправители">Список отправителей: идентификатор и наименование.</param>
    /// <param name="префикс">Набранные первые буквы наименования.</param>
    /// <returns>
    /// Идентификатор найденного отправителя либо <see langword="null"/>, если
    /// совпадений нет или префикс пуст.
    /// </returns>
    public static long? FindByPrefix(
        IReadOnlyList<(long Id, string Name)> отправители,
        string? префикс)
    {
        ArgumentNullException.ThrowIfNull(отправители);

        if (string.IsNullOrWhiteSpace(префикс))
        {
            return null;
        }

        var начало = префикс.Trim();

        foreach (var (идентификатор, наименование) in отправители)
        {
            if (наименование.StartsWith(начало, StringComparison.OrdinalIgnoreCase))
            {
                return идентификатор;
            }
        }

        return null;
    }
}
