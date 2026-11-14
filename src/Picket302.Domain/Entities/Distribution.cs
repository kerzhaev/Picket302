namespace Picket302.Domain.Entities;

/// <summary>
/// Рассылка (ТЗ §3.1.10, таблица <c>distributions</c>).
/// </summary>
public sealed class Distribution
{
    /// <summary>Идентификатор рассылки (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Наименование рассылки.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Отправитель рассылки.</summary>
    public long? SenderId { get; set; }

    /// <summary>Вид рассылки: один ко многим либо многие к одному.</summary>
    public DistributionKind Kind { get; set; } = DistributionKind.OneToMany;

    /// <summary>Пояснение к рассылке.</summary>
    public string? Note { get; set; }

    /// <summary>Состояние рассылки.</summary>
    public DistributionState State { get; set; } = DistributionState.Draft;

    /// <summary>
    /// Вычисляемый счётчик «Всего необходимо конвертов» — сумма <c>copy_count</c>
    /// входящих писем.
    /// </summary>
    public int? EnvelopesRequired { get; set; }

    /// <summary>Момент создания рассылки (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Момент отправки рассылки (UTC).</summary>
    public DateTime? DispatchedAt { get; set; }

    /// <summary>
    /// Маркер версии записи: значение, предъявляемое при изменении, чтобы
    /// служба отклонила правку устаревшей копии.
    /// </summary>
    /// <remarks>
    /// <para>
    /// В совместном режиме версию ведёт служба в отдельной таблице
    /// <c>shared_row_versions</c>; в DTO записи она передаётся клиенту и
    /// обратно. В одиночном режиме версия не ведётся и остаётся равной нулю.
    /// </para>
    /// <para>
    /// Само поле в таблицах базы отсутствует: существующие репозитории и
    /// одиночный режим остаются неизменными.
    /// </para>
    /// </remarks>
    public long Version { get; set; }

}
