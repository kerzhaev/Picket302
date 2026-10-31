using Picket302.Domain.Common;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Repositories;

/// <summary>
/// Условия отбора почтовых отправлений (ТЗ §11.3: «интеграционные: жизненный цикл статусов»).
/// </summary>
/// <param name="Status">Статус отправления.</param>
/// <param name="CounterpartyId">Контрагент-получатель.</param>
/// <param name="SenderId">Контрагент-отправитель.</param>
/// <param name="DistributionId">Рассылка.</param>
/// <param name="PostRegisterId">Почтовый реестр.</param>
/// <param name="DateFrom">Начало диапазона даты создания (включительно).</param>
/// <param name="DateTo">Конец диапазона даты создания (включительно).</param>
public sealed record MailFilter(
    MailStatus? Status = null,
    long? CounterpartyId = null,
    long? SenderId = null,
    long? DistributionId = null,
    long? PostRegisterId = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null);

/// <summary>
/// Репозиторий почтовых отправлений (ТЗ §3.1.3, таблица <c>mails</c>).
/// </summary>
public interface IMailRepository
{
    /// <summary>Добавляет отправление и возвращает его идентификатор.</summary>
    /// <param name="mail">Сохраняемое отправление.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(Mail mail, CancellationToken cancellationToken = default);

    /// <summary>Читает отправление по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отправление либо <see langword="null"/>, если запись не найдена.</returns>
    Task<Mail?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Обновляет отправление.</summary>
    /// <param name="mail">Сохраняемое отправление.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(Mail mail, CancellationToken cancellationToken = default);

    /// <summary>Удаляет отправление.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает постраничную выборку отправлений.</summary>
    /// <param name="request">Параметры постраничной выборки.</param>
    /// <param name="filter">Условия отбора.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница отправлений.</returns>
    Task<Page<Mail>> GetPagedAsync(
        PageRequest request,
        MailFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает отправления контрагента-получателя.</summary>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список отправлений.</returns>
    Task<IReadOnlyList<Mail>> GetByCounterpartyAsync(
        long counterpartyId,
        CancellationToken cancellationToken = default);

    /// <summary>Возвращает отправления рассылки.</summary>
    /// <param name="distributionId">Идентификатор рассылки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список отправлений.</returns>
    Task<IReadOnlyList<Mail>> GetByDistributionAsync(
        long distributionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий рассылок (ТЗ §3.1.10, таблица <c>distributions</c>).
/// </summary>
public interface IDistributionRepository
{
    /// <summary>Добавляет рассылку и возвращает её идентификатор.</summary>
    /// <param name="distribution">Сохраняемая рассылка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(Distribution distribution, CancellationToken cancellationToken = default);

    /// <summary>Читает рассылку по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Рассылка либо <see langword="null"/>, если запись не найдена.</returns>
    Task<Distribution?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает перечень рассылок.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список рассылок.</returns>
    Task<IReadOnlyList<Distribution>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Обновляет рассылку.</summary>
    /// <param name="distribution">Сохраняемая рассылка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(Distribution distribution, CancellationToken cancellationToken = default);

    /// <summary>Удаляет рассылку.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Пересчитывает счётчик «Всего необходимо конвертов» рассылки как сумму
    /// <c>copy_count</c> входящих писем (ТЗ §3.1.10).
    /// </summary>
    /// <param name="distributionId">Идентификатор рассылки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Вычисленное число конвертов.</returns>
    Task<int> RecalculateEnvelopesRequiredAsync(
        long distributionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий папок писем (ТЗ §3.1.11, таблица <c>folders</c>).
/// </summary>
public interface IFolderRepository
{
    /// <summary>Добавляет папку и возвращает её идентификатор.</summary>
    /// <param name="folder">Сохраняемая папка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(Folder folder, CancellationToken cancellationToken = default);

    /// <summary>Читает папку по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Папка либо <see langword="null"/>, если запись не найдена.</returns>
    Task<Folder?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает перечень всех папок.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список папок.</returns>
    Task<IReadOnlyList<Folder>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Обновляет папку.</summary>
    /// <param name="folder">Сохраняемая папка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(Folder folder, CancellationToken cancellationToken = default);

    /// <summary>Удаляет папку.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает счётчик вложенности папки — число отправлений, отнесённых
    /// к ней через <c>mails.folder_id</c> (ТЗ §8.1, функция 22: «Перетаскивание
    /// письма в папку изменяет ее счетчик вложенности»).
    /// </summary>
    /// <param name="id">Идентификатор папки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число писем в папке.</returns>
    Task<int> CountMailsAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий почтовых реестров (ТЗ §3.1.13, таблица <c>post_registers</c>).
/// </summary>
public interface IPostRegisterRepository
{
    /// <summary>Добавляет реестр и возвращает его идентификатор.</summary>
    /// <param name="register">Сохраняемый реестр.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(PostRegister register, CancellationToken cancellationToken = default);

    /// <summary>Читает реестр по идентификатору.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Реестр либо <see langword="null"/>, если запись не найдена.</returns>
    Task<PostRegister?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает перечень реестров.</summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список реестров.</returns>
    Task<IReadOnlyList<PostRegister>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Обновляет реестр.</summary>
    /// <param name="register">Сохраняемый реестр.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых строк.</returns>
    Task<int> UpdateAsync(PostRegister register, CancellationToken cancellationToken = default);

    /// <summary>Удаляет реестр.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий движений входящих документов (ТЗ §3.1.14, таблица <c>document_movements</c>).
/// </summary>
public interface IDocumentMovementRepository
{
    /// <summary>Добавляет движение документа.</summary>
    /// <param name="movement">Сохраняемое движение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(DocumentMovement movement, CancellationToken cancellationToken = default);

    /// <summary>Возвращает движения по отправлению.</summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список движений.</returns>
    Task<IReadOnlyList<DocumentMovement>> GetByMailAsync(
        long mailId,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет движение документа.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Репозиторий связей писем с номерами первичных документов (ТЗ §3.1.15, таблица <c>mail_documents</c>).
/// </summary>
public interface IMailDocumentRepository
{
    /// <summary>Добавляет связь письма с документом.</summary>
    /// <param name="mailDocument">Сохраняемая связь.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор сохранённой записи.</returns>
    Task<long> AddAsync(MailDocument mailDocument, CancellationToken cancellationToken = default);

    /// <summary>Возвращает связи письма с документами.</summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список связей.</returns>
    Task<IReadOnlyList<MailDocument>> GetByMailAsync(
        long mailId,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет связь письма с документом.</summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если запись удалена.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
