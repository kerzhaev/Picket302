using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;

namespace Picket302.Application.Services;

/// <summary>
/// Служба ведения адресной книги (ТЗ §8.1, функции 1–19; §3.1.1).
/// </summary>
/// <remarks>
/// Служба проверяет реквизиты карточки перед сохранением (ИНН/УНП, КПП, БИК) и
/// обеспечивает мягкое удаление: удалённая запись исключается из рабочих
/// выборок (ТЗ §3.1.19).
/// </remarks>
public sealed class CounterpartyService
{
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт службу адресной книги.</summary>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public CounterpartyService(ICounterpartyRepository контрагенты, IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _контрагенты = контрагенты;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Сохраняет карточку контрагента: проверяет реквизиты и создаёт либо
    /// обновляет запись.
    /// </summary>
    /// <param name="counterparty">Сохраняемая карточка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённая карточка либо текст нарушения.</returns>
    public async Task<Result<Counterparty>> SaveAsync(
        Counterparty counterparty,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(counterparty);

        var проверка = Проверить(counterparty);

        if (проверка.IsFailure)
        {
            return Result<Counterparty>.Failure(проверка.Error!);
        }

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (counterparty.Id == 0)
            {
                await _контрагенты.AddAsync(counterparty, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _контрагенты.UpdateAsync(counterparty, cancellationToken).ConfigureAwait(false);
            }

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<Counterparty>.Success(counterparty);
    }

    /// <summary>
    /// Выполняет мягкое удаление карточки: запись исключается из рабочих
    /// выборок, но остаётся в базе (ТЗ §3.1.19).
    /// </summary>
    /// <param name="id">Идентификатор карточки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Удалённая карточка либо текст нарушения.</returns>
    public async Task<Result<Counterparty>> SoftDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var карточка = await _контрагенты.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (карточка is null)
        {
            return Result<Counterparty>.Failure($"Контрагент с идентификатором {id} не найден.");
        }

        var удалена = await _контрагенты.SoftDeleteAsync(id, cancellationToken).ConfigureAwait(false);

        if (!удалена)
        {
            return Result<Counterparty>.Failure("Не удалось пометить контрагента как удалённого.");
        }

        карточка.MarkDeleted();

        return Result<Counterparty>.Success(карточка);
    }

    /// <summary>
    /// Возвращает постраничную выборку контрагентов.
    /// </summary>
    /// <param name="request">Параметры постраничной выборки.</param>
    /// <param name="filter">Условия отбора.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Страница контрагентов.</returns>
    public Task<Page<Counterparty>> GetPagedAsync(
        PageRequest request,
        CounterpartyFilter? filter = null,
        CancellationToken cancellationToken = default)
        => _контрагенты.GetPagedAsync(request, filter, cancellationToken);

    /// <summary>
    /// Проверяет реквизиты карточки контрагента.
    /// </summary>
    /// <param name="counterparty">Проверяемая карточка.</param>
    /// <returns>Успешный результат либо текст нарушения.</returns>
    public static Result<Counterparty> Проверить(Counterparty counterparty)
    {
        ArgumentNullException.ThrowIfNull(counterparty);

        if (string.IsNullOrWhiteSpace(counterparty.Name))
        {
            return Result<Counterparty>.Failure("Наименование контрагента не задано.");
        }

        if (counterparty.LegalAddress is not null
            && !PostalIndexValidator.IsValid(counterparty.LegalAddress.Zip))
        {
            return Result<Counterparty>.Failure("Некорректный почтовый индекс юридического адреса.");
        }

        if (counterparty.PostalAddress is not null
            && !PostalIndexValidator.IsValid(counterparty.PostalAddress.Zip))
        {
            return Result<Counterparty>.Failure("Некорректный почтовый индекс почтового адреса.");
        }

        if (counterparty.ActualAddress is not null
            && !PostalIndexValidator.IsValid(counterparty.ActualAddress.Zip))
        {
            return Result<Counterparty>.Failure("Некорректный почтовый индекс фактического адреса.");
        }

        if (!string.IsNullOrWhiteSpace(counterparty.Inn))
        {
            var инн = InnValidator.Validate(counterparty.Inn, counterparty.Type);

            if (инн.IsFailure)
            {
                return Result<Counterparty>.Failure(инн.Error!);
            }
        }

        if (!string.IsNullOrWhiteSpace(counterparty.Kpp))
        {
            var кпп = BankRequisitesValidator.ValidateKpp(counterparty.Kpp);

            if (кпп.IsFailure)
            {
                return Result<Counterparty>.Failure(кпп.Error!);
            }
        }

        if (!string.IsNullOrWhiteSpace(counterparty.Bik))
        {
            var bik = BankRequisitesValidator.ValidateBik(counterparty.Bik);

            if (bik.IsFailure)
            {
                return Result<Counterparty>.Failure(bik.Error!);
            }
        }

        return Result<Counterparty>.Success(counterparty);
    }
}
