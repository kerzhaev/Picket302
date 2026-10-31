namespace Picket302.Domain.Entities;

/// <summary>
/// Хранилище параметров «ключ — значение» (ТЗ §3.1.17, таблица <c>parameters</c>).
/// </summary>
/// <remarks>
/// Служит для счётчиков нумерации (<c>LastOutgoingNumber</c>,
/// <c>LastIncomingNumber</c>), глобального отправителя, шаблонов масок номеров
/// и иных настроек. Имя ключа уникально.
/// </remarks>
public sealed class Parameter
{
    /// <summary>Ключ счётчика исходящих регистрационных номеров.</summary>
    public const string LastOutgoingNumberKey = "LastOutgoingNumber";

    /// <summary>Ключ счётчика входящих регистрационных номеров.</summary>
    public const string LastIncomingNumberKey = "LastIncomingNumber";

    /// <summary>Идентификатор параметра (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Имя параметра; уникально в пределах хранилища.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Значение параметра.</summary>
    public string? Value { get; set; }

    /// <summary>Момент последнего изменения (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
