namespace Picket302.Printing.Engine;

/// <summary>
/// Фильтрация номеров страниц задания по чётности (ТЗ §8.6, функция 87:
/// <c>Printing.Engine.PageParityFilter.FilterPageNumbers</c>).
/// </summary>
/// <remarks>
/// Фильтр применяется как к многостраничным бланкам (уведомления), так и к
/// выборке писем при двухпроходной печати. Нумерация страниц начинается с 1:
/// при общем числе страниц 1 отбор «только нечётные» возвращает эту
/// страницу, а отбор «только чётные» — пустую последовательность.
/// </remarks>
public sealed class PageParityFilter
{
    /// <summary>
    /// Возвращает номера страниц, отобранные по чётности.
    /// </summary>
    /// <param name="totalPages">Общее число страниц задания.</param>
    /// <param name="parity">Правило отбора.</param>
    /// <returns>Номера страниц по возрастанию.</returns>
    public IEnumerable<int> FilterPageNumbers(int totalPages, PageParity parity)
    {
        if (totalPages <= 0)
        {
            return Array.Empty<int>();
        }

        var номера = new List<int>(totalPages);

        for (var страница = 1; страница <= totalPages; страница++)
        {
            var чётная = страница % 2 == 0;

            var подходит = parity switch
            {
                PageParity.All => true,
                PageParity.OddOnly => !чётная,
                PageParity.EvenOnly => чётная,
                _ => true,
            };

            if (подходит)
            {
                номера.Add(страница);
            }
        }

        return номера;
    }
}
