using Picket302.Domain.Entities;

namespace Picket302.Domain.Contracts;

/// <summary>
/// План формирования рассылки (ТЗ §8.1, функция 21: «Виды рассылок
/// («1 ко многим» и «многие к 1»)»).
/// </summary>
/// <remarks>
/// План описывает один из двух видов рассылки: один отправитель — множеству
/// получателей либо множество отправителей — одному центральному получателю.
/// </remarks>
public sealed class DistributionPlan
{
    /// <summary>Наименование рассылки.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Вид рассылки.</summary>
    public DistributionKind Kind { get; set; } = DistributionKind.OneToMany;

    /// <summary>
    /// Отправитель рассылки. Для вида «один ко многим» — единственный
    /// отправитель; для вида «многие к одному» — центральный получатель.
    /// </summary>
    public long SenderId { get; set; }

    /// <summary>
    /// Получатели рассылки. Для вида «один ко многим» — адресаты письма;
    /// для вида «многие к одному» — отправители писем.
    /// </summary>
    public IReadOnlyList<long> CounterpartyIds { get; set; } = [];

    /// <summary>Пояснение к рассылке.</summary>
    public string? Note { get; set; }

    /// <summary>Формат конверта для писем рассылки.</summary>
    public string? EnvelopeType { get; set; }

    /// <summary>Число копий каждого письма рассылки.</summary>
    public int CopyCount { get; set; } = 1;
}
