namespace Picket302.Domain.Entities;

/// <summary>
/// Движение входящего документа (ТЗ §8.2, функция 30: приём / передача
/// на исполнение / в архив). История — только дополнение: записи не
/// редактируются и не удаляются.
/// </summary>
public sealed class IncomingMovement
{
    /// <summary>Идентификатор движения.</summary>
    public long Id { get; set; }

    /// <summary>Входящий документ (журнал функции 29).</summary>
    public long IncomingId { get; set; }

    /// <summary>Момент движения (UTC).</summary>
    public DateTime MovedAt { get; set; }

    /// <summary>Действие: приём, передача, в архив.</summary>
    public DocumentMovementAction Action { get; set; } = DocumentMovementAction.Reception;

    /// <summary>Подразделение-получатель (для передачи) либо место приёма.</summary>
    public string Department { get; set; } = string.Empty;

    /// <summary>Исполнитель (необязательно).</summary>
    public string? Executor { get; set; }
}
