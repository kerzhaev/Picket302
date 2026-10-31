using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Services;

/// <summary>
/// Создание исходящего почтового отправления
/// (ТЗ §8.1, функция 20: «Создание исходящего почтового отправления»).
/// </summary>
/// <remarks>
/// При создании отправления фиксируется снимок адресов на момент создания:
/// последующие изменения карточек контрагентов не изменяют уже созданные
/// отправления (ТЗ §3.1.19: «Правило снимков»).
/// </remarks>
public sealed class MailFactory
{
    private readonly ICounterpartyRepository _контрагенты;

    /// <summary>Создаёт фабрику почтовых отправлений.</summary>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    public MailFactory(ICounterpartyRepository контрагенты)
    {
        ArgumentNullException.ThrowIfNull(контрагенты);
        _контрагенты = контрагенты;
    }

    /// <summary>
    /// Создаёт исходящее отправление: получатель, отправитель, формат конверта.
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента-получателя.</param>
    /// <param name="senderId">Идентификатор контрагента-отправителя.</param>
    /// <param name="envelopeType">Формат конверта (C6, DL, C5, C4).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданное отправление в статусе «Черновик» (создано).</returns>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если отправитель или получатель не задан либо у получателя
    /// отсутствует основной адрес.
    /// </exception>
    public async Task<Mail> CreateOutcomingAsync(
        long counterpartyId,
        long senderId,
        string envelopeType,
        CancellationToken cancellationToken = default)
    {
        if (counterpartyId <= 0)
        {
            throw new InvalidOperationException("Получатель не указан — создание письма невозможно.");
        }

        if (senderId <= 0)
        {
            throw new InvalidOperationException("Отправитель не указан — создание письма невозможно.");
        }

        var получатель = await _контрагенты.GetByIdAsync(counterpartyId, cancellationToken)
            .ConfigureAwait(false);

        if (получатель is null)
        {
            throw new InvalidOperationException(
                $"Контрагент-получатель с идентификатором {counterpartyId} не найден.");
        }

        var адрес = получатель.GetAddress(получатель.AddressMainType);

        if (адрес is null)
        {
            throw new InvalidOperationException(
                $"У получателя «{получатель.Name}» не задан основной адрес.");
        }

        var отправление = new Mail
        {
            CounterpartyId = counterpartyId,
            SenderId = senderId,
            EnvelopeType = envelopeType,
            CopyCount = Mail.MinimumCopyCount,
            Status = MailStatus.Draft,
            DateCreated = DateTime.UtcNow,
            RecipientAddress = адрес,
            IsVip = получатель.IsVip,
        };

        return отправление;
    }

    /// <summary>
    /// Создаёт исходящее отправление с заданным адресом получателя — снимок
    /// адреса фиксируется на момент создания.
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента-получателя.</param>
    /// <param name="senderId">Идентификатор контрагента-отправителя.</param>
    /// <param name="envelopeType">Формат конверта.</param>
    /// <param name="recipientAddress">Адрес получателя.</param>
    /// <param name="isVip">Признак приоритетного адресата.</param>
    /// <param name="copyCount">Число экземпляров.</param>
    /// <returns>Созданное отправление.</returns>
    public static Mail CreateOutcoming(
        long counterpartyId,
        long senderId,
        string envelopeType,
        Address recipientAddress,
        bool isVip = false,
        int copyCount = Mail.MinimumCopyCount)
    {
        ArgumentNullException.ThrowIfNull(recipientAddress);

        if (counterpartyId <= 0)
        {
            throw new InvalidOperationException("Получатель не указан — создание письма невозможно.");
        }

        if (senderId <= 0)
        {
            throw new InvalidOperationException("Отправитель не указан — создание письма невозможно.");
        }

        return new Mail
        {
            CounterpartyId = counterpartyId,
            SenderId = senderId,
            EnvelopeType = envelopeType,
            CopyCount = copyCount < Mail.MinimumCopyCount ? Mail.MinimumCopyCount : copyCount,
            Status = MailStatus.Draft,
            DateCreated = DateTime.UtcNow,
            RecipientAddress = recipientAddress,
            IsVip = isVip,
        };
    }
}
