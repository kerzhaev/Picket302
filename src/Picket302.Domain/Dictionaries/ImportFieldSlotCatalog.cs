using Picket302.Domain.Contracts;

namespace Picket302.Domain.Dictionaries;

/// <summary>
/// Каталог слотов поля мастера импорта (ТЗ §8.12, функция 159, шаг 3).
/// </summary>
/// <remarks>
/// Слот — это поле, в которое мастер импорта сопоставляет столбец файла.
/// Список слотов зафиксирован ТЗ; подписи используются в интерфейсе мастера.
/// </remarks>
public static class ImportFieldSlotCatalog
{
    private static readonly IReadOnlyList<(ImportFieldSlot Слот, string Заголовок)> Описание =
    [
        (ImportFieldSlot.FullName, "ФИО"),
        (ImportFieldSlot.OrganizationName, "Название организации"),
        (ImportFieldSlot.PostalAddress, "Почтовый адрес"),
        (ImportFieldSlot.LegalAddress, "Юридический адрес"),
        (ImportFieldSlot.ActualAddress, "Фактический адрес"),
        (ImportFieldSlot.Other, "Прочее"),
        (ImportFieldSlot.Name, "Название"),
        (ImportFieldSlot.ShortName, "Сокращенное"),
    ];

    /// <summary>Все слоты в порядке, зафиксированном ТЗ.</summary>
    public static IReadOnlyList<ImportFieldSlot> All =>
        Описание.Select(описание => описание.Слот).ToList();

    /// <summary>
    /// Возвращает русскую подпись слота для интерфейса мастера импорта.
    /// </summary>
    /// <param name="слот">Слот поля.</param>
    /// <returns>Подпись поля.</returns>
    public static string Title(ImportFieldSlot слот)
    {
        foreach (var описание in Описание)
        {
            if (описание.Слот == слот)
            {
                return описание.Заголовок;
            }
        }

        return слот.ToString();
    }

    /// <summary>
    /// Проверяет, что поле является слотом мастера импорта.
    /// </summary>
    /// <param name="слот">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если слот зафиксирован ТЗ.</returns>
    public static bool IsKnown(ImportFieldSlot слот) => All.Contains(слот);
}
