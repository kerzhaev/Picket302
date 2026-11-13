using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>Текущее состояние документа в цепочке движения.</summary>
/// <param name="Действие">Последнее движение; <see langword="null"/> — движений нет.</param>
/// <param name="Местонахождение">Человекочитаемое местонахождение.</param>
/// <param name="Архивен">Документ передан в архив (цепочка завершена).</param>
public sealed record IncomingLocation(
    IncomingMovement? Действие,
    string Местонахождение,
    bool Архивен);

/// <summary>
/// Трекер движения входящего документа (ТЗ §8.2, функция 30): приём →
/// передача(и) на исполнение → архив. История — только дополнение; переходы
/// валидируются (первое движение — приём; после архива движений нет).
/// </summary>
public sealed class IncomingMovementTracker
{
    private readonly IIncomingMovementRepository _репозиторий;
    private readonly IIncomingMailRepository _входящие;

    /// <summary>Создаёт трекер движения.</summary>
    /// <param name="репозиторий">Репозиторий движений (append-only).</param>
    /// <param name="входящие">Репозиторий журнала входящей корреспонденции.</param>
    public IncomingMovementTracker(
        IIncomingMovementRepository репозиторий,
        IIncomingMailRepository входящие)
    {
        ArgumentNullException.ThrowIfNull(репозиторий);
        ArgumentNullException.ThrowIfNull(входящие);

        _репозиторий = репозиторий;
        _входящие = входящие;
    }

    /// <summary>
    /// Регистрирует ПРИЁМ документа подразделением. Первое движение в цепочке;
    /// для документа с уже начатой историей запрещено.
    /// </summary>
    /// <param name="incomingId">Документ журнала входящей корреспонденции.</param>
    /// <param name="department">Подразделение-приёмник.</param>
    /// <param name="executor">Исполнитель (необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданное движение.</returns>
    /// <exception cref="InvalidOperationException">
    /// Документ не найден или приём уже зарегистрирован.
    /// </exception>
    public async Task<IncomingMovement> ПриёмAsync(
        long incomingId,
        string department,
        string? executor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(department);
        await ПроверитьСуществуетAsync(incomingId, cancellationToken).ConfigureAwait(false);

        var история = await _репозиторий
            .GetByIncomingAsync(incomingId, cancellationToken)
            .ConfigureAwait(false);

        if (история.Count > 0)
        {
            throw new InvalidOperationException(
                "Приём уже зарегистрирован: первое движение документа не повторяется.");
        }

        return await ДобавитьAsync(
            incomingId,
            DocumentMovementAction.Reception,
            department,
            executor,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Передаёт документ подразделению на исполнение. Требует начатой истории
    /// и неархивного документа.
    /// </summary>
    /// <param name="incomingId">Документ журнала.</param>
    /// <param name="department">Подразделение-получатель.</param>
    /// <param name="executor">Исполнитель (необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданное движение.</returns>
    /// <exception cref="InvalidOperationException">
    /// Документ не найден, приём не зарегистрирован или документ в архиве.
    /// </exception>
    public async Task<IncomingMovement> ПередатьAsync(
        long incomingId,
        string department,
        string? executor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(department);

        var история = await ИсторияВалиднуюAsync(incomingId, cancellationToken).ConfigureAwait(false);

        if (история.Count == 0)
        {
            throw new InvalidOperationException(
                "Документ ещё не принят: сначала зарегистрируйте приём.");
        }

        return await ДобавитьAsync(
            incomingId,
            DocumentMovementAction.Execution,
            department,
            executor,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Передаёт документ в архив — завершает цепочку; дальнейшие движения
    /// запрещены.
    /// </summary>
    /// <param name="incomingId">Документ журнала.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Созданное движение.</returns>
    /// <exception cref="InvalidOperationException">
    /// Документ не найден, не принят или уже в архиве.
    /// </exception>
    public async Task<IncomingMovement> АрхивироватьAsync(
        long incomingId,
        CancellationToken cancellationToken = default)
    {
        await ИсторияВалиднуюAsync(incomingId, cancellationToken).ConfigureAwait(false);

        return await ДобавитьAsync(
            incomingId,
            DocumentMovementAction.Archived,
            "Архив",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Текущее местонахождение документа (критерий приёмки: «текущее
    /// местонахождение видно»).
    /// </summary>
    /// <param name="incomingId">Документ журнала.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Последнее движение и человекочитаемое местонахождение.</returns>
    public async Task<IncomingLocation> ТекущееAsync(
        long incomingId,
        CancellationToken cancellationToken = default)
    {
        var история = await _репозиторий
            .GetByIncomingAsync(incomingId, cancellationToken)
            .ConfigureAwait(false);

        if (история.Count == 0)
        {
            return new IncomingLocation(null, "Не принят", false);
        }

        var последнее = история[^1];

        return последнее.Action switch
        {
            DocumentMovementAction.Reception => new IncomingLocation(
                последнее, $"{последнее.Department} (принят)", false),
            DocumentMovementAction.Execution => new IncomingLocation(
                последнее, $"{последнее.Department} (на исполнении)", false),
            _ => new IncomingLocation(последнее, "Архив", true),
        };
    }

    /// <summary>История движений документа (хронологическая, append-only).</summary>
    public Task<IReadOnlyList<IncomingMovement>> ИсторияAsync(
        long incomingId,
        CancellationToken cancellationToken = default)
        => _репозиторий.GetByIncomingAsync(incomingId, cancellationToken);

    /// <summary>Проверяет существование записи журнала.</summary>
    private async Task ПроверитьСуществуетAsync(
        long incomingId,
        CancellationToken cancellationToken)
    {
        var запись = await _входящие
            .GetByIdAsync(incomingId, cancellationToken)
            .ConfigureAwait(false);

        if (запись is null)
        {
            throw new InvalidOperationException($"Документ {incomingId} не найден в журнале входящей корреспонденции.");
        }
    }

    /// <summary>
    /// История с проверками существования документа и незавершённости цепочки
    /// (движения после архива запрещены).
    /// </summary>
    private async Task<IReadOnlyList<IncomingMovement>> ИсторияВалиднуюAsync(
        long incomingId,
        CancellationToken cancellationToken)
    {
        await ПроверитьСуществуетAsync(incomingId, cancellationToken).ConfigureAwait(false);

        var история = await _репозиторий
            .GetByIncomingAsync(incomingId, cancellationToken)
            .ConfigureAwait(false);

        if (история.Count > 0
            && история[^1].Action == DocumentMovementAction.Archived)
        {
            throw new InvalidOperationException(
                "Документ передан в архив: дальнейшие движения запрещены.");
        }

        return история;
    }

    /// <summary>Дописывает движение в историю (append-only).</summary>
    private async Task<IncomingMovement> ДобавитьAsync(
        long incomingId,
        DocumentMovementAction действие,
        string department,
        string? executor,
        CancellationToken cancellationToken)
    {
        var движение = new IncomingMovement
        {
            IncomingId = incomingId,
            MovedAt = DateTime.UtcNow,
            Action = действие,
            Department = department.Trim(),
            Executor = string.IsNullOrWhiteSpace(executor) ? null : executor.Trim(),
        };

        await _репозиторий.AddAsync(движение, cancellationToken).ConfigureAwait(false);

        return движение;
    }
}
