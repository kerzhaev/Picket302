namespace Picket302.Domain.Entities;

/// <summary>
/// Профиль принтера и калибровочные смещения (ТЗ §3.1.16, таблица <c>printer_profiles</c>).
/// </summary>
/// <remarks>
/// Калибровка хранится отдельно для каждой пары «принтер + формат»:
/// <c>UNIQUE (printer_name, envelope_format)</c>. Смещения задаются с шагом 0,1 мм.
/// </remarks>
public sealed class PrinterProfile
{
    /// <summary>Идентификатор профиля (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Имя принтера.</summary>
    public string PrinterName { get; set; } = string.Empty;

    /// <summary>Формат конверта, для которого настроена калибровка.</summary>
    public string EnvelopeFormat { get; set; } = string.Empty;

    /// <summary>Сдвиг по горизонтали, мм.</summary>
    public decimal OffsetXMm { get; set; }

    /// <summary>Сдвиг по вертикали, мм.</summary>
    public decimal OffsetYMm { get; set; }

    /// <summary>Подача носителя: по центру, по левому краю, по правому краю.</summary>
    public FeedAlignment FeedAlignment { get; set; } = FeedAlignment.Center;

    /// <summary>Признак закрепления формата A4.</summary>
    public bool FixA4 { get; set; }

    /// <summary>Признак поддержки двусторонней печати.</summary>
    public bool DuplexCapable { get; set; }

    /// <summary>Признак профиля по умолчанию.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Снимок DEVMODE для воспроизведения настроек.</summary>
    public byte[]? DevmodeTemplate { get; set; }
}
