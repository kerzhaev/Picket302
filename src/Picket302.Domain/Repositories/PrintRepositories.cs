using Picket302.Domain.Common;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Repositories;

/// <summary>
/// Репозиторий шаблонов конвертов и бланков (ТЗ §3.1.4, таблица <c>print_templates</c>).
/// </summary>
public interface IPrintTemplateRepository
{
    /// <summary>Добавляет шаблон и возвращает его идентификатор.</summary>
    /// <param name="template">Сохраняемый шаблон.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(PrintTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Читает шаблон по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Шаблон либо <see langword="null"/>, если запись не найдена.</returns>
    Task<PrintTemplate?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает перечень шаблонов.</summary>
    /// <param name="kind">Вид шаблона; <see langword="null"/> — все виды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список шаблонов.</returns>
    Task<IReadOnlyList<PrintTemplate>> GetAllAsync(
        string? kind = null,
        CancellationToken cancellationToken = default);

    /// <summary>Обновляет шаблон.</summary>
    /// <param name="template">Сохраняемый шаблон.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(PrintTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Удаляет шаблон.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий координатной сетки полей бланка (ТЗ §3.1.5, таблица <c>blank_fields</c>).
/// </summary>
public interface IBlankFieldRepository
{
    /// <summary>Добавляет поле бланка и возвращает его идентификатор.</summary>
    /// <param name="field">Сохраняемое поле.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(BlankField field, CancellationToken cancellationToken = default);

    /// <summary>Возвращает поля шаблона.</summary>
    /// <param name="templateId">Идентификатор шаблона.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список полей шаблона.</returns>
    Task<IReadOnlyList<BlankField>> GetByTemplateAsync(
        long templateId,
        CancellationToken cancellationToken = default);

    /// <summary>Обновляет поле бланка.</summary>
    /// <param name="field">Сохраняемое поле.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(BlankField field, CancellationToken cancellationToken = default);

    /// <summary>Удаляет поле бланка.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий неизменяемого реестра сессий печати (ТЗ §3.1.6, таблица <c>print_jobs</c>).
/// </summary>
/// <remarks>
/// Репозиторий не предоставляет операций изменения и удаления: записи журнала
/// неизменяемы (ТЗ §3.1.6).
/// </remarks>
public interface IPrintJobRepository
{
    /// <summary>Добавляет запись реестра сессий печати.</summary>
    /// <param name="printJob">Сохраняемая запись.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(PrintJob printJob, CancellationToken cancellationToken = default);

    /// <summary>Читает запись реестра по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись либо <see langword="null"/>, если не найдена.</returns>
    Task<PrintJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает постраничную выборку реестра.</summary>
    /// <param name="request">Параметры постраничной выборки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница записей реестра.</returns>
    Task<Page<PrintJob>> GetPagedAsync(
        PageRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий журнала напечатанных конвертов (ТЗ §3.1.7, таблица <c>print_history</c>).
/// </summary>
public interface IPrintHistoryRepository
{
    /// <summary>Добавляет запись журнала и возвращает её идентификатор.</summary>
    /// <param name="history">Сохраняемая запись.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(PrintHistory history, CancellationToken cancellationToken = default);

    /// <summary>Читает запись журнала по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись либо <see langword="null"/>, если не найдена.</returns>
    Task<PrintHistory?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает записи журнала по отправлению.</summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список записей журнала.</returns>
    Task<IReadOnlyList<PrintHistory>> GetByMailAsync(
        long mailId,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает постраничную выборку журнала.</summary>
    /// <param name="request">Параметры постраничной выборки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница записей журнала.</returns>
    Task<Page<PrintHistory>> GetPagedAsync(
        PageRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий кэша сгенерированных кодов (ТЗ §3.1.8, таблица <c>qr_cache</c>).
/// </summary>
public interface IQrCacheRepository
{
    /// <summary>Добавляет запись кэша и возвращает её идентификатор.</summary>
    /// <param name="cache">Сохраняемая запись.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(QrCache cache, CancellationToken cancellationToken = default);

    /// <summary>Читает запись кэша по хэшу полезной нагрузки.</summary>
    /// <param name="payloadHash">SHA-256 хэш строки данных.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Запись кэша либо <see langword="null"/>, если не найдена.</returns>
    Task<QrCache?> GetByPayloadHashAsync(
        string payloadHash,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет запись кэша.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий профилей принтеров (ТЗ §3.1.16, таблица <c>printer_profiles</c>).
/// </summary>
public interface IPrinterProfileRepository
{
    /// <summary>Добавляет профиль принтера и возвращает его идентификатор.</summary>
    /// <param name="profile">Сохраняемый профиль.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(PrinterProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Читает профиль по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Профиль либо <see langword="null"/>, если запись не найдена.</returns>
    Task<PrinterProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Читает профиль по паре «принтер + формат» (ТЗ §3.1.16).</summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="envelopeFormat">Формат конверта.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Профиль либо <see langword="null"/>, если запись не найдена.</returns>
    Task<PrinterProfile?> GetByPrinterAndFormatAsync(
        string printerName,
        string envelopeFormat,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает профили указанного принтера.</summary>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список профилей.</returns>
    Task<IReadOnlyList<PrinterProfile>> GetByPrinterAsync(
        string printerName,
        CancellationToken cancellationToken = default);

    /// <summary>Обновляет профиль принтера.</summary>
    /// <param name="profile">Сохраняемый профиль.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(PrinterProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Удаляет профиль принтера.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
