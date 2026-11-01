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
    /// <summary>
    /// Ключ параметра в таблице <c>parameters</c>: порог подтверждения
    /// пакетной правки.
    /// </summary>
    public const string ПорогПодтвержденияKey = "BatchEditConfirmationThreshold";

    /// <summary>
    /// Порог подтверждения по умолчанию: правка более 10 записей требует
    /// явного подтверждения оператора (ТЗ §8.1, функция 18; §13 —
    /// «порог подтверждения группового редактирования: N = 10, конфигурируется»).
    /// </summary>
    public const int ПорогПодтвержденияПоУмолчанию = 10;

    private readonly ICounterpartyRepository _контрагенты;
    private readonly IUnitOfWork _единицаРаботы;
    private readonly IParameterRepository? _параметры;

    /// <summary>Создаёт службу пакетного редактирования.</summary>
    /// <param name="контрагенты">Репозиторий контрагентов.</param>
    /// <param name="единицаРаботы">Единица работы над базой данных.</param>
    /// <param name="параметры">
    /// Хранилище параметров профиля; при <see langword="null"/> порог
    /// подтверждения всегда равен <see cref="ПорогПодтвержденияПоУмолчанию"/>.
    /// </param>
    public BatchEditService(
        ICounterpartyRepository контрагенты,
        IUnitOfWork единицаРаботы,
        IParameterRepository? параметры = null)
    {
        ArgumentNullException.ThrowIfNull(контрагенты);
        ArgumentNullException.ThrowIfNull(единицаРаботы);

        _контрагенты = контрагенты;
        _единицаРаботы = единицаРаботы;
        _параметры = параметры;
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
    /// Возвращает порог, начиная с которого пакетная правка требует
    /// подтверждения оператора.
    /// </summary>
    /// <remarks>
    /// Значение хранится в профиле пользователя под ключом
    /// <see cref="ПорогПодтвержденияKey"/> и настраивается; при отсутствии
    /// или некорректном значении применяется
    /// <see cref="ПорогПодтвержденияПоУмолчанию"/>.
    /// </remarks>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число записей, начиная с которого требуется подтверждение.</returns>
    public async Task<int> GetConfirmationThresholdAsync(CancellationToken cancellationToken = default)
    {
        if (_параметры is null)
        {
            return ПорогПодтвержденияПоУмолчанию;
        }

        var значение = await _параметры
            .GetValueAsync(ПорогПодтвержденияKey, cancellationToken)
            .ConfigureAwait(false);

        return int.TryParse(
            значение,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var порог) && порог >= 0
            ? порог
            : ПорогПодтвержденияПоУмолчанию;
    }

    /// <summary>
    /// Определяет, требуется ли подтверждение пакетной правки указанного
    /// числа записей.
    /// </summary>
    /// <param name="числоЗаписей">Число записей, затронутых правкой.</param>
    /// <param name="порог">Порог подтверждения.</param>
    /// <returns>
    /// <see langword="true"/>, если затронуто строго больше порога записей.
    /// </returns>
    public static bool ТребуетсяПодтверждение(int числоЗаписей, int порог) => числоЗаписей > порог;

    /// <summary>
    /// Возвращает признак необходимости подтверждения пакетной правки по
    /// текущему порогу из профиля.
    /// </summary>
    /// <param name="числоЗаписей">Число записей, затронутых правкой.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если требуется подтверждение.</returns>
    public async Task<bool> ТребуетсяПодтверждениеAsync(
        int числоЗаписей,
        CancellationToken cancellationToken = default)
    {
        var порог = await GetConfirmationThresholdAsync(cancellationToken).ConfigureAwait(false);

        return ТребуетсяПодтверждение(числоЗаписей, порог);
    }

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
