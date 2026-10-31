namespace Picket302.Domain.Entities;

/// <summary>
/// Связь письма с номером первичного документа (ТЗ §3.1.15, таблица <c>mail_documents</c>).
/// </summary>
/// <remarks>
/// Номера связанных документов выводятся в реестре «через запятую».
/// </remarks>
public sealed class MailDocument
{
    /// <summary>Идентификатор связи (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Письмо, к которому относится документ.</summary>
    public long MailId { get; set; }

    /// <summary>Номер связанного документа.</summary>
    public string DocNumber { get; set; } = string.Empty;
}
