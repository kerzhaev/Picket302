using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Services;

/// <summary>
/// Массовое каскадное обновление адресов писем
/// (ТЗ §8.1, функция 15: «Массовое каскадное обновление адресов писем»).
/// </summary>
/// <remarks>
/// Обновляются только отправления в статусе «Черновик»: напечатанные письма
/// хранят снимок адреса на момент печати и не изменяются
/// (ТЗ §8.1, функция 15: «После смены адреса контрагента адреса в напечатанных
/// письмах остаются прежними, а в черновиках — обновляются»).
/// </remarks>
public sealed class CascadeAddressUpdater
{
    private readonly IMailRepository _письма;

    /// <summary>Создаёт исполнитель каскадного обновления адресов.</summary>
    /// <param name="письма">Репозиторий почтовых отправлений.</param>
    public CascadeAddressUpdater(IMailRepository письма)
    {
        ArgumentNullException.ThrowIfNull(письма);
        _письма = письма;
    }

    /// <summary>
    /// Заменяет адрес получателя во всех ещё не напечатанных письмах контрагента.
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента-получателя.</param>
    /// <param name="newAddress">Новый адрес получателя.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число обновлённых писем.</returns>
    public Task<int> UpdateUnprintedLettersAddressAsync(
        long counterpartyId,
        Address newAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newAddress);

        return _письма.UpdateDraftRecipientAddressAsync(counterpartyId, newAddress, cancellationToken);
    }
}
