using Picket302.Domain.Entities;

namespace Picket302.Domain.Contracts;

/// <summary>
/// Условия отбора рассылок (ТЗ §8.12, функция 156: «Сохранённые и отправленные
/// рассылки»).
/// </summary>
public sealed record DistributionFilter(
    /// <summary>
    /// Дата «Созданы с» — рассылки, созданные не ранее указанной даты.
    /// </summary>
    DateTime? CreatedFrom = null,
    /// <summary>Состояние рассылки; <see langword="null"/> — любое.</summary>
    DistributionState? State = null,
    /// <summary>Отправитель рассылки; <see langword="null"/> — любой.</summary>
    long? SenderId = null);

/// <summary>
/// Представление рассылки для списка и журнала (ТЗ §8.12, функция 156).
/// </summary>
/// <param name="Id">Идентификатор рассылки.</param>
/// <param name="Name">Наименование рассылки.</param>
/// <param name="Kind">Вид рассылки.</param>
/// <param name="State">Состояние рассылки.</param>
/// <param name="SenderId">Отправитель рассылки.</param>
/// <param name="Note">Пояснение к рассылке.</param>
/// <param name="EnvelopesRequired">Счётчик «Всего необходимо конвертов».</param>
/// <param name="CreatedAt">Момент создания рассылки (UTC).</param>
/// <param name="DispatchedAt">Момент отправки рассылки (UTC).</param>
/// <param name="RecipientCount">Число писем рассылки.</param>
public sealed record DistributionView(
    long Id,
    string Name,
    DistributionKind Kind,
    DistributionState State,
    long? SenderId,
    string? Note,
    int? EnvelopesRequired,
    DateTime CreatedAt,
    DateTime? DispatchedAt,
    int RecipientCount);
