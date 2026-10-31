namespace Picket302.Domain.Entities;

/// <summary>
/// Движение входящего документа (ТЗ §3.1.14, таблица <c>document_movements</c>).
/// </summary>
public sealed class DocumentMovement
{
    /// <summary>Идентификатор движения (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Отправление, к которому относится документ.</summary>
    public long MailId { get; set; }

    /// <summary>Момент движения (UTC).</summary>
    public DateTime MovedAt { get; set; }

    /// <summary>Подразделение-получатель.</summary>
    public string TargetDepartment { get; set; } = string.Empty;

    /// <summary>Исполнитель.</summary>
    public string? Executor { get; set; }

    /// <summary>Действие: приём, передача на исполнение, в архив.</summary>
    public DocumentMovementAction? Action { get; set; }
}
