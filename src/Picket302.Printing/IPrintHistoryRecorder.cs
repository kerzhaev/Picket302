namespace Picket302.Printing;

/// <summary>
/// Регистратор истории печати конвертов (ТЗ §3.1.7, §8.4, функция 64:
/// «При каждом физическом выводе конверта на принтер фиксируется неизменяемый
/// снимок адресных строк получателя и отправителя, имя бланка и дату»).
/// </summary>
/// <remarks>
/// Интерфейс объявлен в слое печати, чтобы конвейер печати не зависел от слоя
/// приложения: реализация переносит снимок адресов в таблицу
/// <c>print_history</c> через доменный сервис.
/// </remarks>
public interface IPrintHistoryRecorder
{
    /// <summary>
    /// Фиксирует снимок адресов каждого напечатанного конверта.
    /// </summary>
    /// <param name="mailIds">Идентификаторы напечатанных отправлений.</param>
    /// <param name="printJobId">Идентификатор сессии печати.</param>
    /// <param name="templateDescription">Имя бланка (описание шаблона печати).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача, завершающаяся после записи снимков.</returns>
    Task RecordAsync(
        IReadOnlyList<long> mailIds,
        long? printJobId,
        string templateDescription,
        CancellationToken cancellationToken = default);
}
