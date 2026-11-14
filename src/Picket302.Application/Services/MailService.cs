using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Services;

/// <summary>
/// Служба ведения почтовых отправлений (ТЗ §8.1, функции 20–35; §3.1.3).
/// </summary>
/// <remarks>
/// Служба контролирует жизненный цикл отправления и формирует снимки адресов
/// для журнала напечатанных конвертов: последующие изменения карточек
/// контрагентов не изменяют исторические записи (ТЗ §3.1.19: «Правило снимков»).
/// Создание отправления дополнительно ограничивается воротами лицензии
/// (ТЗ §8.10, функция 119).
/// </remarks>
public sealed class MailService
{
    private readonly IMailRepository _письма;
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IPrintHistoryRepository _журнал;
    private readonly IUnitOfWork _единицаРаботы;
    private readonly ILicenseGate? _ворота;

    /// <summary>Создаёт службу почтовых отправлений.</summary>
    /// <param name="письма">Репозиторий отправлений.</param>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="журнал">Репозиторий журнала напечатанных конвертов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    /// <param name="ворота">Ворота лицензии; при отсутствии проверка не выполняется.</param>
    public MailService(
        IMailRepository письма,
        ICounterpartyRepository контрагенты,
        IPrintHistoryRepository журнал,
        IUnitOfWork единицаРаботы,
        ILicenseGate? ворота = null)
    {
        ArgumentNullException.ThrowIfNull(письма);
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(журнал);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _письма = письма;
        _контрагенты = контрагенты;
        _журнал = журнал;
        _единицаРаботы = единицаРаботы;
        _ворота = ворота;
    }

    /// <summary>
    /// Создаёт отправление, проверяя число экземпляров и адрес получателя.
    /// </summary>
    /// <param name="mail">Сохраняемое отправление.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданное отправление либо текст нарушения.</returns>
    public async Task<Result<Mail>> CreateAsync(Mail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);

        var разрешение = _ворота?.Проверить(LicenseOperation.СозданиеЗаписей);

        if (разрешение is { Разрешено: false })
        {
            return Result<Mail>.Failure(разрешение.Сообщение);
        }

        if (mail.CopyCount < Mail.MinimumCopyCount)
        {
            return Result<Mail>.Failure(
                $"Число экземпляров должно быть не меньше {Mail.MinimumCopyCount}.");
        }

        if (mail.RecipientAddress is null)
        {
            return Result<Mail>.Failure("Адрес получателя не задан.");
        }

        if (!PostalIndexValidator.IsValid(mail.RecipientAddress.Zip))
        {
            return Result<Mail>.Failure("Некорректный почтовый индекс получателя.");
        }

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _письма.AddAsync(mail, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Mail>.Success(mail);
    }

    /// <summary>
    /// Переводит отправление в новый статус, проставляя временную метку
    /// (ТЗ §8.1, функция 24).
    /// </summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="newStatus">Целевой статус.</param>
    /// <param name="moment">Момент перехода (UTC).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отправление либо текст нарушения.</returns>
    public async Task<Result<Mail>> TransitionStatusAsync(
        long mailId,
        MailStatus newStatus,
        DateTime? moment = null,
        CancellationToken cancellationToken = default)
    {
        var отправление = await _письма.GetByIdAsync(mailId, cancellationToken).ConfigureAwait(false);

        if (отправление is null)
        {
            return Result<Mail>.Failure($"Отправление с идентификатором {mailId} не найдено.");
        }

        if (!отправление.CanTransitionTo(newStatus))
        {
            return Result<Mail>.Failure(
                $"Недопустимый переход статуса отправления: «{отправление.Status}» → «{newStatus}».");
        }

        отправление.TransitionStatus(newStatus, moment ?? DateTime.UtcNow);

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _письма.UpdateAsync(отправление, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Mail>.Success(отправление);
    }

    /// <summary>
    /// Перемещает отправление в папку пользовательской классификации
    /// (ТЗ §8.1, функция 22: «Таблица folders, колонка mails.folder_id»).
    /// </summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="folderId">Идентификатор папки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отправление либо текст нарушения.</returns>
    public async Task<Result<Mail>> MoveToFolderAsync(
        long mailId,
        long folderId,
        CancellationToken cancellationToken = default)
    {
        var отправление = await _письма.GetByIdAsync(mailId, cancellationToken).ConfigureAwait(false);

        if (отправление is null)
        {
            return Result<Mail>.Failure($"Отправление с идентификатором {mailId} не найдено.");
        }

        отправление.MoveToFolder(folderId);

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _письма.UpdateAsync(отправление, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Mail>.Success(отправление);
    }

    /// <summary>
    /// Регистрирует печать отправления в журнале напечатанных конвертов: в
    /// запись журнала переносятся снимки адресов получателя и отправителя на
    /// момент печати (ТЗ §3.1.7, §3.1.19).
    /// </summary>
    /// <param name="mailId">Идентификатор отправления.</param>
    /// <param name="printJobId">Идентификатор сессии печати.</param>
    /// <param name="templateDescription">Описание шаблона печати.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="outcome">
    /// Исход печатной операции («подготовлено», «передано в локальный спулер»,
    /// «ошибка», «неизвестный исход после обрыва»). Пусто — исход
    /// определяется самой программой (одиночный режим).
    /// </param>
    /// <param name="deviceId">
    /// Идентификатор устройства-клиента, выполнившего печать; пусто в
    /// одиночном режиме.
    /// </param>
    /// <returns>Запись журнала либо текст нарушения.</returns>
    public async Task<Result<PrintHistory>> RegisterPrintAsync(
        long mailId,
        long? printJobId,
        string templateDescription,
        CancellationToken cancellationToken = default,
        string? outcome = null,
        string? deviceId = null)
    {
        if (string.IsNullOrWhiteSpace(templateDescription))
        {
            return Result<PrintHistory>.Failure("Описание шаблона печати не задано.");
        }

        var отправление = await _письма.GetByIdAsync(mailId, cancellationToken).ConfigureAwait(false);

        if (отправление is null)
        {
            return Result<PrintHistory>.Failure($"Отправление с идентификатором {mailId} не найдено.");
        }

        var отправитель = await _контрагенты.GetByIdAsync(отправление.SenderId, cancellationToken).ConfigureAwait(false);
        var адресОтправителя = отправитель?.GetAddress(отправитель.AddressMainType);

        var запись = new PrintHistory
        {
            PrintedAt = DateTime.UtcNow,
            MailId = mailId,
            RecipientAddressSnapshot = отправление.RecipientAddress?.ToSnapshot() ?? string.Empty,
            SenderAddressSnapshot = адресОтправителя?.ToSnapshot() ?? string.Empty,
            CopyCount = отправление.CopyCount,
            IsVip = отправление.IsVip,
            Note = отправление.Note,
            TemplateDescription = templateDescription,
            PrintJobId = printJobId,
            Outcome = outcome,
            DeviceId = deviceId,
        };

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _журнал.AddAsync(запись, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<PrintHistory>.Success(запись);
    }
}
