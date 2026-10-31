using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Управление записями адресной книги
/// (ТЗ §8.1, функция 19: «Мягкое удаление комбинацией Ctrl+Alt+F8»).
/// </summary>
public sealed class CounterpartyManager
{
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт менеджер записей адресной книги.</summary>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public CounterpartyManager(
        ICounterpartyRepository контрагенты,
        IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _контрагенты = контрагенты;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Помечает выделенные записи как удалённые без показа промежуточных
    /// диалогов (ТЗ §8.1, функция 19).
    /// </summary>
    /// <param name="ids">Идентификаторы удаляемых записей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число помеченных записей либо текст нарушения.</returns>
    public async Task<Result<int>> FastDeleteMarkAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return Result<int>.Failure("Не выбрано ни одной записи.");
        }

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var помечено = 0;

            foreach (var id in ids)
            {
                if (await _контрагенты.SoftDeleteAsync(id, cancellationToken).ConfigureAwait(false))
                {
                    помечено++;
                }
            }

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result<int>.Success(помечено);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Возвращает мягко удалённые записи в рабочее состояние
    /// (ТЗ §8.1, функция 19: «с возможностью восстановления из меню «Сервис»»).
    /// </summary>
    /// <param name="ids">Идентификаторы восстанавливаемых записей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число восстановленных записей либо текст нарушения.</returns>
    public async Task<Result<int>> RestoreAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return Result<int>.Failure("Не выбрано ни одной записи.");
        }

        var восстановлено = 0;

        foreach (var id in ids)
        {
            if (await _контрагенты.RestoreAsync(id, cancellationToken).ConfigureAwait(false))
            {
                восстановлено++;
            }
        }

        return Result<int>.Success(восстановлено);
    }
}
