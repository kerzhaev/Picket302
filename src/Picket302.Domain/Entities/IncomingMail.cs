namespace Picket302.Domain.Entities;

/// <summary>Тип входящей корреспонденции (ТЗ §8.2, функция 29).</summary>
public enum IncomingMailType
{
    /// <summary>Письмо (конверт).</summary>
    Letter = 0,

    /// <summary>Бандероль/посылка.</summary>
    Parcel = 1,

    /// <summary>Курьерская доставка.</summary>
    Courier = 2,

    /// <summary>Прочее.</summary>
    Other = 3,
}

/// <summary>
/// Запись журнала входящей корреспонденции (ТЗ §8.2, функция 29):
/// регистрационный номер, дата, отправитель, содержание, тип и путь
/// к скану в профиле пользователя.
/// </summary>
public sealed class IncomingMail
{
    /// <summary>Идентификатор записи.</summary>
    public long Id { get; set; }

    /// <summary>Регистрационный номер (уникальный в журнале).</summary>
    public string RegNumber { get; set; } = string.Empty;

    /// <summary>Дата регистрации (UTC).</summary>
    public DateTime RegDate { get; set; }

    /// <summary>Отправитель (как указан на конверте/накладной).</summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>Краткое содержание / тема.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Тип входящей корреспонденции.</summary>
    public IncomingMailType DocType { get; set; } = IncomingMailType.Letter;

    /// <summary>
    /// Путь к скану в каталоге профиля пользователя
    /// (<c>scans/</c>); <see langword="null"/> — скан не прилагался.
    /// </summary>
    public string? ScanPath { get; set; }

    /// <summary>Момент создания записи (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
