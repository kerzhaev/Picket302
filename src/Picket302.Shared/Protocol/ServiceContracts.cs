namespace Picket302.Shared.Protocol;

/// <summary>
/// Запрос сопряжения клиентского устройства.
/// </summary>
/// <param name="Code">Одноразовый код сопряжения, показанный администратором.</param>
/// <param name="Nonce">Одноразовое значение вызова.</param>
/// <param name="DeviceName">Имя устройства для показа администратору.</param>
public sealed record PairRequest(
    string Code,
    string Nonce,
    string DeviceName);

/// <summary>
/// Ответ службы на запрос сопряжения.
/// </summary>
/// <param name="DeviceId">Идентификатор устройства.</param>
/// <param name="Secret">
/// Секрет устройства. Клиент сохраняет его в защищённом хранилище Windows;
/// на сервере остаётся только отпечаток.
/// </param>
/// <param name="ServerId">Идентификатор сервера.</param>
/// <param name="ServerFingerprint">
/// Отпечаток открытого ключа сервера: клиент закрепляет это значение и
/// сверяет его при каждом подключении.
/// </param>
public sealed record PairResponse(
    string DeviceId,
    string Secret,
    string ServerId,
    string ServerFingerprint);

/// <summary>
/// Сведения о сервере.
/// </summary>
/// <param name="ServerId">Идентификатор сервера.</param>
/// <param name="ProtocolVersion">Версия протокола.</param>
/// <param name="ServerFingerprint">Отпечаток открытого ключа сервера.</param>
/// <param name="DeviceCount">Число сопряжённых устройств.</param>
public sealed record ServerInfoResponse(
    string ServerId,
    int ProtocolVersion,
    string ServerFingerprint,
    int DeviceCount);

/// <summary>
/// Состояние службы.
/// </summary>
/// <param name="Status">Состояние («готова»).</param>
/// <param name="ProtocolVersion">Версия протокола.</param>
/// <param name="ServerId">Идентификатор сервера.</param>
/// <param name="TimeUtc">Текущее время службы (UTC).</param>
public sealed record HealthResponse(
    string Status,
    int ProtocolVersion,
    string ServerId,
    DateTime TimeUtc);

/// <summary>
/// Одноразовый код сопряжения, выданный локальной консолью администратора.
/// </summary>
/// <param name="Code">Код сопряжения.</param>
/// <param name="Nonce">Одноразовое значение вызова.</param>
/// <param name="ExpiresAt">Момент истечения кода (UTC).</param>
/// <param name="ServerFingerprint">Отпечаток открытого ключа сервера.</param>
public sealed record AdminPairingCodeResponse(
    string Code,
    string Nonce,
    DateTime ExpiresAt,
    string ServerFingerprint);

/// <summary>
/// Сведения о сопряжённом устройстве для показа администратору.
/// </summary>
/// <param name="DeviceId">Идентификатор устройства.</param>
/// <param name="Name">Имя устройства.</param>
/// <param name="CreatedAt">Момент сопряжения (UTC).</param>
/// <param name="LastSeenAt">Момент последнего обращения (UTC).</param>
/// <param name="RevokedAt">Момент отзыва доступа (UTC).</param>
/// <param name="RevokedReason">Причина отзыва.</param>
public sealed record DeviceInfoResponse(
    string DeviceId,
    string Name,
    DateTime CreatedAt,
    DateTime? LastSeenAt,
    DateTime? RevokedAt,
    string? RevokedReason);

/// <summary>
/// Ответ на служебный запрос отзыва доступа устройства.
/// </summary>
/// <param name="DeviceId">Идентификатор устройства.</param>
/// <param name="Revoked">Признак того, что доступ отозван.</param>
public sealed record RevokeDeviceResponse(
    string DeviceId,
    bool Revoked);
