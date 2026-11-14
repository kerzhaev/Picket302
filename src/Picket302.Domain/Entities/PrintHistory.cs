namespace Picket302.Domain.Entities;

/// <summary>
/// Журнал напечатанных конвертов (ТЗ §3.1.7, таблица <c>print_history</c>).
/// </summary>
/// <remarks>
/// Запись хранит текстовые снимки адресов получателя и отправителя на момент
/// печати. Последующие изменения карточек контрагентов не изменяют исторические
/// записи (ТЗ §3.1.19: «Правило снимков»).
/// </remarks>
public sealed class PrintHistory
{
    /// <summary>Идентификатор записи журнала (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Момент печати (UTC).</summary>
    public DateTime PrintedAt { get; set; }

    /// <summary>Отправление, для которого напечатан конверт.</summary>
    public long? MailId { get; set; }

    /// <summary>Снимок адреса получателя на момент печати.</summary>
    public string RecipientAddressSnapshot { get; set; } = string.Empty;

    /// <summary>Снимок адреса отправителя на момент печати.</summary>
    public string SenderAddressSnapshot { get; set; } = string.Empty;

    /// <summary>Число напечатанных экземпляров.</summary>
    public int CopyCount { get; set; }

    /// <summary>Признак приоритетного адресата на момент печати.</summary>
    public bool IsVip { get; set; }

    /// <summary>Технологическое примечание.</summary>
    public string? Note { get; set; }

    /// <summary>Описание шаблона, которым напечатан конверт.</summary>
    public string TemplateDescription { get; set; } = string.Empty;

    /// <summary>Сессия печати, в рамках которой напечатан конверт.</summary>
    public long? PrintJobId { get; set; }

    /// <summary>
    /// Исход печатной операции: «подготовлено», «передано в локальный спулер»,
    /// «ошибка» либо «неизвестный исход после обрыва».
    /// </summary>
    /// <remarks>
    /// Значение различает состояния, которые нельзя смешивать: подтверждение
    /// спулера не равно физически напечатанному конверту. В одиночном режиме
    /// поле остаётся пустым — там исход печати определяется самой программой.
    /// </remarks>
    public string? Outcome { get; set; }

    /// <summary>
    /// Идентификатор устройства-клиента, выполнившего печать; в одиночном
    /// режиме пусто.
    /// </summary>
    public string? DeviceId { get; set; }
}
