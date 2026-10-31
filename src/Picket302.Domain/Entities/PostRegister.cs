namespace Picket302.Domain.Entities;

/// <summary>
/// Почтовый реестр для трекера отправлений (ТЗ §3.1.13, таблица <c>post_registers</c>).
/// </summary>
/// <remarks>
/// Пароль трекера хранится исключительно в защищённом виде (DPAPI) в поле
/// <see cref="TrackPasswordProtected"/> и никогда открытым текстом.
/// </remarks>
public sealed class PostRegister
{
    /// <summary>Идентификатор реестра (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Номер реестра.</summary>
    public string RegisterNumber { get; set; } = string.Empty;

    /// <summary>Номер партии Почты России.</summary>
    public string? BatchNumber { get; set; }

    /// <summary>Дата отправки.</summary>
    public DateTime? SendingDate { get; set; }

    /// <summary>Пункт отправки.</summary>
    public string? ShippingPoint { get; set; }

    /// <summary>Логин трекера.</summary>
    public string? TrackLogin { get; set; }

    /// <summary>Защищённый пароль трекера (DPAPI).</summary>
    public byte[]? TrackPasswordProtected { get; set; }

    /// <summary>Токен трекера.</summary>
    public string? TrackToken { get; set; }

    /// <summary>
    /// Список идентификаторов вошедших отправлений в виде JSON-массива
    /// (в PostgreSQL — <c>JSONB</c>).
    /// </summary>
    public string? MailIds { get; set; }

    /// <summary>Момент создания реестра (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
