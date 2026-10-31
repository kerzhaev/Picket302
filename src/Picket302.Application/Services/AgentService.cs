using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Services;

/// <summary>
/// Отправители (агенты) — записи адресной книги с флагом отправителя
/// (ТЗ §8.12, функция 157: «Отправители (агенты)»).
/// </summary>
/// <remarks>
/// Отдельная таблица отправителей не ведётся: отправителем является запись
/// адресной книги с установленным флагом <c>is_sender</c>
/// (ТЗ §8.11, функция 139).
/// </remarks>
public sealed class AgentService
{
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт службу отправителей.</summary>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public AgentService(
        ICounterpartyRepository контрагенты,
        IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _контрагенты = контрагенты;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Создаёт агента — отправителя из записи адресной книги
    /// (ТЗ §8.12, функция 157).
    /// </summary>
    /// <param name="dto">Данные агента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданный агент либо текст нарушения.</returns>
    public async Task<Result<Counterparty>> CreateAgentAsync(
        AgentDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return Result<Counterparty>.Failure("Наименование отправителя не задано.");
        }

        var проверка = CounterpartyService.Проверить(new Counterparty
        {
            Name = dto.Name,
            Type = dto.Type,
            Inn = dto.Inn,
            Kpp = dto.Kpp,
            Bik = dto.Bik,
        });

        if (проверка.IsFailure)
        {
            return Result<Counterparty>.Failure(проверка.Error!);
        }

        var агент = new Counterparty
        {
            Type = dto.Type,
            Name = dto.Name.Trim(),
            Inn = dto.Inn,
            Kpp = dto.Kpp,
            Bik = dto.Bik,
            Rs = dto.Account,
            BankName = dto.BankName,
            IsSender = dto.IsSender,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        if (!string.IsNullOrWhiteSpace(dto.Zip))
        {
            var строки = new string[Address.LineCount];

            for (var i = 0; i < Address.LineCount; i++)
            {
                строки[i] = dto.AddressLines.ElementAtOrDefault(i) ?? string.Empty;
            }

            var адрес = Address.Create(dto.Zip.Trim(), строки);

            if (адрес.IsFailure)
            {
                return Result<Counterparty>.Failure(адрес.Error!);
            }

            агент.PostalAddress = адрес.Value;
            агент.AddressMainType = AddressMainType.Postal;
        }

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _контрагенты.AddAsync(агент, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Counterparty>.Success(агент);
    }

    /// <summary>
    /// Возвращает справочник отправителей — записи с установленным флагом
    /// <c>is_sender</c> (ТЗ §8.11, функция 139).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список отправителей.</returns>
    public Task<IReadOnlyList<Counterparty>> ListAgentsAsync(CancellationToken cancellationToken = default)
        => _контрагенты.GetSendersAsync(cancellationToken);
}
