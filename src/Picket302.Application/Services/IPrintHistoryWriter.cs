using Picket302.Domain.Common;
using Picket302.Domain.Entities;

namespace Picket302.Application.Services;

/// <summary>
/// Запись печатного события в журнал печати.
/// </summary>
/// <remarks>
/// <para>
/// Абстракция нужна, чтобы журнал печати в одиночном режиме и в совместном
/// заполнялись одинаково: в одиночном режиме запись идёт в локальную базу, в
/// совместном — через серверную службу.
/// </para>
/// <para>
/// Исход печати и устройство, выполнившее печать, передаются отдельно: в
/// общем журнале различаются «подготовлено», «передано в локальный спулер»,
/// «ошибка» и «неизвестный исход после обрыва».
/// </para>
/// </remarks>
public interface IPrintHistoryWriter
{
    /// <summary>
    /// Записывает печатное событие в журнал.
    /// </summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="printJobId">Идентификатор сессии печати.</param>
    /// <param name="templateDescription">Описание использованного шаблона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="outcome">
    /// Исход печати; пусто — определяется программой.
    /// </param>
    /// <param name="deviceId">Устройство, выполнившее печать; пусто в одиночном режиме.</param>
    /// <param name="operationId">
    /// Идентификатор операции записи; пусто в одиночном режиме. По нему повтор
    /// записи после обрыва связи опознаётся как повтор той же операции.
    /// </param>
    /// <returns>Запись журнала либо текст нарушения.</returns>
    Task<Result<PrintHistory>> RegisterPrintAsync(
        long mailId,
        long? printJobId,
        string templateDescription,
        CancellationToken cancellationToken = default,
        PrintOutcome? outcome = null,
        string? deviceId = null,
        string? operationId = null);
}
