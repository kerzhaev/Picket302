using Picket302.Domain;
using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.Rules;

namespace Picket302.Application.Services;

/// <summary>
/// Управление пользовательскими полями контрагента
/// (ТЗ §8.1, функция 7: «Пользовательские поля контрагента»).
/// </summary>
/// <remarks>
/// Пользовательское поле — динамическая пара «ключ-значение», хранящаяся в
/// таблице <c>counterparty_fields</c>. Поле публикуется в печатных макетах под
/// тегом вида <c>[Custom_КодКлиента]</c>.
/// </remarks>
public sealed class CustomFieldManager
{
    private readonly ICounterpartyFieldRepository _поля;
    private readonly IUnitOfWork _единицаРаботы;

    /// <summary>Создаёт менеджер пользовательских полей.</summary>
    /// <param name="поля">Репозиторий пользовательских полей.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    public CustomFieldManager(
        ICounterpartyFieldRepository поля,
        IUnitOfWork единицаРаботы)
    {
        ArgumentNullException.ThrowIfNull(поля);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _поля = поля;
        _единицаРаботы = единицаРаботы;
    }

    /// <summary>
    /// Сохраняет значение пользовательского поля контрагента. Поле с таким же
    /// именем обновляется — дубликаты не создаются
    /// (ТЗ §8.1, функция 7: «Дублирование имени ключа для одного контрагента —
    /// обновление существующего»).
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="fieldName">Имя поля.</param>
    /// <param name="value">Значение поля.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сохранённое поле либо текст нарушения.</returns>
    public async Task<Result<CounterpartyField>> SetCustomValueAsync(
        long counterpartyId,
        string fieldName,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (counterpartyId <= 0)
        {
            return Result<CounterpartyField>.Failure("Контрагент не указан.");
        }

        if (!CustomFieldTag.TryFormat(fieldName, out _))
        {
            return Result<CounterpartyField>.Failure(
                $"Имя пользовательского поля «{fieldName}» недопустимо.");
        }

        var имя = fieldName.Trim();
        var существующие = await _поля.GetByCounterpartyAsync(counterpartyId, cancellationToken)
            .ConfigureAwait(false);

        var поле = существующие.FirstOrDefault(сущ =>
            string.Equals(сущ.FieldName, имя, StringComparison.OrdinalIgnoreCase));

        if (поле is null)
        {
            поле = new CounterpartyField
            {
                CounterpartyId = counterpartyId,
                FieldName = имя,
                FieldValue = value ?? string.Empty,
                FieldType = CounterpartyFieldType.Text,
            };

            await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _поля.AddAsync(поле, cancellationToken).ConfigureAwait(false);
                await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }

            return Result<CounterpartyField>.Success(поле);
        }

        поле.FieldValue = value ?? string.Empty;

        await _единицаРаботы.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _поля.UpdateAsync(поле, cancellationToken).ConfigureAwait(false);
            await _единицаРаботы.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await _единицаРаботы.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        return Result<CounterpartyField>.Success(поле);
    }

    /// <summary>
    /// Возвращает пользовательские поля контрагента.
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список полей контрагента.</returns>
    public Task<IReadOnlyList<CounterpartyField>> GetCustomFieldsAsync(
        long counterpartyId,
        CancellationToken cancellationToken = default)
        => _поля.GetByCounterpartyAsync(counterpartyId, cancellationToken);

    /// <summary>
    /// Возвращает значения пользовательских полей контрагента в виде словаря
    /// «имя поля — значение».
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Словарь значений.</returns>
    public async Task<IReadOnlyDictionary<string, string>> GetCustomValuesAsync(
        long counterpartyId,
        CancellationToken cancellationToken = default)
    {
        var поля = await _поля.GetByCounterpartyAsync(counterpartyId, cancellationToken)
            .ConfigureAwait(false);

        return поля.ToDictionary(
            поле => поле.FieldName,
            поле => поле.FieldValue ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Удаляет пользовательское поле контрагента.
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="fieldName">Имя поля.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если поле удалено.</returns>
    public async Task<bool> RemoveCustomValueAsync(
        long counterpartyId,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        var поля = await _поля.GetByCounterpartyAsync(counterpartyId, cancellationToken)
            .ConfigureAwait(false);

        var поле = поля.FirstOrDefault(сущ =>
            string.Equals(сущ.FieldName, fieldName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (поле is null)
        {
            return false;
        }

        return await _поля.DeleteAsync(поле.Id, cancellationToken).ConfigureAwait(false);
    }
}
