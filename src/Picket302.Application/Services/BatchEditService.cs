using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Пакетное редактирование одного поля у выделенных записей
/// (ТЗ §8.1, функция 18: «Пакетное редактирование одного поля у выделенных
/// записей»).
/// </summary>
/// <remarks>
/// Операция выполняется в рамках одной транзакции: при ошибке блокировки строк
/// изменение откатывается целиком.
/// </remarks>
public sealed class BatchEditService
{
    private readonly ICounterpartyRepository _контрагенты;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт службу пакетного редактирования.</summary>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public BatchEditService(
        ICounterpartyRepository контрагенты,
        IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _контрагенты = контрагенты;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Имена свойств контрагента, допускающих пакетное изменение
    /// (ТЗ §8.1, функция 18).
    /// </summary>
    public static IReadOnlyList<string> EditableProperties { get; } =
    [
        "Name",
        "ShortName",
        "Inn",
        "Kpp",
        "Phone",
        "Email",
        "GroupId",
        "IsVip",
        "IsActive",
    ];

    /// <summary>
    /// Выполняет массовое изменение указанного поля у выделенных записей.
    /// </summary>
    /// <param name="ids">Идентификаторы изменяемых записей.</param>
    /// <param name="propertyName">Имя изменяемого свойства.</param>
    /// <param name="newValue">Новое значение свойства.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых записей либо текст нарушения.</returns>
    public async Task<Result<int>> BatchUpdateFieldAsync(
        IReadOnlyList<long> ids,
        string propertyName,
        object? newValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return Result<int>.Failure("Не выбрано ни одной записи.");
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return Result<int>.Failure("Не указано изменяемое поле.");
        }

        if (!EditableProperties.Contains(propertyName, StringComparer.Ordinal))
        {
            return Result<int>.Failure(
                $"Поле «{propertyName}» не допускает пакетного изменения.");
        }

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var изменено = await _контрагенты
                .UpdateFieldAsync(ids, propertyName, newValue, cancellationToken)
                .ConfigureAwait(false);

            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);

            return Result<int>.Success(изменено);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
