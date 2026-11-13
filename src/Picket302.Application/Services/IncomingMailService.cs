using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Служба входящей корреспонденции (ТЗ §8.2, функция 29): регистрация
/// с копированием скана в профиль пользователя, поиск, просмотр, удаление.
/// </summary>
/// <remarks>
/// <para>
/// Путь скана живёт в профиле (<c>scans/</c>): файл копируется ДО записи в
/// базу, и при сбое записи файл НЕ удаляется — путь к скану не теряется
/// (критерий приёмки №29: «путь скана в профиле, не теряется при ошибке»).
/// Повторная регистрация с тем же сканом копирует файл заново под новым
/// именем (GUID), поэтому ничего не перезаписывается.
/// </para>
/// </remarks>
public sealed class IncomingMailService
{
    /// <summary>Подкаталог сканов в профиле пользователя.</summary>
    public const string СканыПодкаталог = "scans";

    private readonly IIncomingMailRepository _репозиторий;
    private readonly string _кореньПрофиля;

    /// <summary>Создаёт службу входящей корреспонденции.</summary>
    /// <param name="репозиторий">Репозиторий журнала.</param>
    /// <param name="кореньПрофиля">
    /// Каталог данных профиля пользователя (сканы хранятся в нём).
    /// </param>
    public IncomingMailService(IIncomingMailRepository репозиторий, string кореньПрофиля)
    {
        ArgumentNullException.ThrowIfNull(репозиторий);
        ArgumentException.ThrowIfNullOrWhiteSpace(кореньПрофиля);

        _репозиторий = репозиторий;
        _кореньПрофиля = кореньПрофиля;
    }

    /// <summary>Каталог сканов профиля.</summary>
    public string КаталогСканов => Path.Combine(_кореньПрофиля, СканыПодкаталог);

    /// <summary>
    /// Регистрирует входящее отправление: копирует скан в профиль и записывает
    /// журнал. При ошибке записи в базу скан остаётся в профиле — путь не
    /// теряется, регистрацию можно повторить.
    /// </summary>
    /// <param name="regNumber">Регистрационный номер (уникальный).</param>
    /// <param name="regDate">Дата регистрации (UTC).</param>
    /// <param name="sender">Отправитель.</param>
    /// <param name="subject">Краткое содержание.</param>
    /// <param name="docType">Тип корреспонденции.</param>
    /// <param name="scanSourcePath">
    /// Путь к исходному файлу скана; <see langword="null"/> — без скана.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Зарегистрированная запись.</returns>
    /// <exception cref="InvalidOperationException">
    /// Регистрационный номер уже занят (скан при этом остаётся в профиле).
    /// </exception>
    public async Task<IncomingMail> ЗарегистрироватьAsync(
        string regNumber,
        DateTime regDate,
        string sender,
        string subject,
        IncomingMailType docType,
        string? scanSourcePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regNumber);

        // Ск snapshot для журнала: путь в профиле фиксируется даже при
        // последующем сбое записи.
        var сканВПрофиле = КопироватьСканВПрофиль(scanSourcePath);

        var запись = new IncomingMail
        {
            RegNumber = regNumber.Trim(),
            RegDate = regDate,
            Sender = sender ?? string.Empty,
            Subject = subject ?? string.Empty,
            DocType = docType,
            ScanPath = сканВПрофиле,
            CreatedAt = DateTime.UtcNow,
        };

        await _репозиторий.AddAsync(запись, cancellationToken).ConfigureAwait(false);

        return запись;
    }

    /// <summary>Поиск по номеру/отправителю/содержанию.</summary>
    public Task<IReadOnlyList<IncomingMail>> НайтиAsync(
        string? запрос,
        CancellationToken cancellationToken = default)
        => _репозиторий.SearchAsync(запрос, cancellationToken);

    /// <summary>Просмотр записи по идентификатору.</summary>
    public Task<IncomingMail?> ПрочитатьAsync(
        long id,
        CancellationToken cancellationToken = default)
        => _репозиторий.GetByIdAsync(id, cancellationToken);

    /// <summary>Удаляет запись журнала; файл скана в профиле сохраняется.</summary>
    public async Task<bool> УдалитьAsync(
        long id,
        CancellationToken cancellationToken = default)
        => await _репозиторий.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Копирует файл скана в каталог профиля, возвращает путь копии
    /// (или <see langword="null"/> без скана). Существование источника
    /// проверяется, ошибки копирования пробрасываются — журнал не записывается
    /// с битым сканом.
    /// </summary>
    private string? КопироватьСканВПрофиль(string? источник)
    {
        if (string.IsNullOrWhiteSpace(источник))
        {
            return null;
        }

        if (!File.Exists(источник))
        {
            throw new FileNotFoundException("Файл скана не найден.", источник);
        }

        Directory.CreateDirectory(КаталогСканов);

        var имя = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{Path.GetExtension(источник)}";
        var цель = Path.Combine(КаталогСканов, имя);

        File.Copy(источник, цель, overwrite: false);

        return цель;
    }
}
