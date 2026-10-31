using Picket302.Domain.Common;

namespace Picket302.Infrastructure.Persistence;

/// <summary>
/// Виртуальный постраничный просмотр
/// (ТЗ §8.1, функция 11: «Виртуальный постраничный просмотр (Pagination)»).
/// </summary>
/// <remarks>
/// <para>
/// В память загружается только текущая отображаемая страница (50–200 записей):
/// данные поставляются постраничным загрузчиком, а не вычитываются целиком.
/// </para>
/// <para>
/// Нумерация страниц начинается с нуля — как в смещении <c>OFFSET</c>.
/// </para>
/// </remarks>
/// <typeparam name="T">Тип элемента страницы.</typeparam>
public sealed class VirtualDataBuffer<T>
{
    /// <summary>Размер страницы по умолчанию.</summary>
    public const int DefaultPageSize = 100;

    private readonly Func<int, int, CancellationToken, Task<Page<T>>> _загрузчик;

    /// <summary>
    /// Создаёт виртуальный буфер поверх постраничного загрузчика данных.
    /// </summary>
    /// <param name="загрузчик">
    /// Загрузчик страницы: принимает смещение и размер страницы, возвращает
    /// страницу данных.
    /// </param>
    public VirtualDataBuffer(Func<int, int, CancellationToken, Task<Page<T>>> загрузчик)
    {
        ArgumentNullException.ThrowIfNull(загрузчик);
        _загрузчик = загрузчик;
    }

    /// <summary>
    /// Возвращает страницу данных по индексу и размеру страницы.
    /// </summary>
    /// <param name="pageIndex">Индекс страницы (нумерация с нуля).</param>
    /// <param name="pageSize">Размер страницы; 0 — значение по умолчанию.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница данных.</returns>
    public async Task<Page<T>> GetPage(
        int pageIndex,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var индекс = Math.Max(pageIndex, 0);
        var размер = pageSize <= 0 ? DefaultPageSize : pageSize;
        var смещение = индекс * размер;

        return await _загрузчик(смещение, размер, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Возвращает общее число записей, доступных в буфере.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Общее число записей.</returns>
    public async Task<long> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        var страница = await _загрузчик(0, 1, cancellationToken).ConfigureAwait(false);
        return страница.TotalCount;
    }

    /// <summary>
    /// Формирует текст панели пагинации «Записи 1-100 из 150000»
    /// (ТЗ §8.1, функция 11).
    /// </summary>
    /// <param name="pageIndex">Индекс текущей страницы (нумерация с нуля).</param>
    /// <param name="pageSize">Размер страницы.</param>
    /// <param name="totalCount">Общее число записей.</param>
    /// <returns>Текст панели пагинации.</returns>
    public static string FormatPaginationText(int pageIndex, int pageSize, long totalCount)
    {
        var размер = pageSize <= 0 ? DefaultPageSize : pageSize;

        if (totalCount <= 0)
        {
            return "Записи 0-0 из 0";
        }

        var начало = (long)Math.Max(pageIndex, 0) * размер + 1;
        var конец = Math.Min(начало + размер - 1, totalCount);

        return $"Записи {начало}-{конец} из {totalCount}";
    }
}
