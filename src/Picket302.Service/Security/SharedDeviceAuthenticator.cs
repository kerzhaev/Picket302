using Picket302.Service.Data;

namespace Picket302.Service.Security;

/// <summary>
/// Опознание устройства-клиента по предъявленному секрету.
/// </summary>
/// <remarks>
/// <para>
/// Клиент предъявляет секрет устройства в заголовке
/// <c>Authorization: Bearer &lt;секрет&gt;</c>. Служба сверяет его отпечаток
/// с записью сопряжённого устройства: открытое значение секрета на сервере не
/// хранится.
/// </para>
/// <para>
/// Отозванное устройство не проходит опознание ни при каком секрете.
/// </para>
/// </remarks>
public sealed class SharedDeviceAuthenticator
{
    /// <summary>Ключ сведений об устройстве в контексте запроса.</summary>
    public const string КлючУстройства = "Picket302.Device";

    private readonly ServerDatabase _база;
    private readonly Action<string> _журнал;

    /// <summary>
    /// Создаёт опознаватель устройств.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    /// <param name="журнал">
    /// Приёмник записей журнала. В журнал попадают только факты (предъявлен
    /// секрет или нет, найдено устройство или нет) — сами секреты и коды не
    /// записываются.
    /// </param>
    public SharedDeviceAuthenticator(ServerDatabase база, Action<string>? журнал = null)
    {
        ArgumentNullException.ThrowIfNull(база);

        _база = база;
        _журнал = журнал ?? (_ => { });
    }

    /// <summary>
    /// Опознаёт устройство по заголовку авторизации.
    /// </summary>
    /// <param name="заголовок">Значение заголовка <c>Authorization</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Сведения об устройстве либо <see langword="null"/>, если секрет не
    /// предъявлен, не найден или устройство отозвано.
    /// </returns>
    public async Task<SharedDevice?> ОпознатьAsync(
        string? заголовок,
        CancellationToken cancellationToken = default)
    {
        var секрет = ИзвлечьСекрет(заголовок);

        if (секрет is null)
        {
            _журнал("Опознание устройства: секрет не предъявлен.");

            return null;
        }

        var отпечаток = SharedSecrets.ВычислитьОтпечатокСекрета(секрет);

        var сессия = _база.СоздатьСессию();
        await using var освобождение = сессия.ConfigureAwait(false);
        var устройства = new SharedDeviceStore(сессия);

        var устройство = await устройства.НайтиПоСекретуAsync(отпечаток, cancellationToken)
            .ConfigureAwait(false);

        if (устройство is null)
        {
            _журнал("Опознание устройства: устройство с предъявленным секретом не найдено.");

            return null;
        }

        if (устройство.IsRevoked)
        {
            _журнал($"Опознание устройства: доступ устройства {устройство.DeviceId} отозван.");

            return null;
        }

        await устройства.ОтметитьОбращениеAsync(устройство.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        _журнал($"Опознание устройства: устройство {устройство.DeviceId} опознано.");

        return устройство;
    }

    /// <summary>
    /// Извлекает секрет из заголовка авторизации.
    /// </summary>
    /// <param name="заголовок">Значение заголовка <c>Authorization</c>.</param>
    /// <returns>Секрет либо <see langword="null"/>.</returns>
    public static string? ИзвлечьСекрет(string? заголовок)
    {
        if (string.IsNullOrWhiteSpace(заголовок))
        {
            return null;
        }

        const string префикс = "Bearer ";

        if (!заголовок.StartsWith(префикс, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var секрет = заголовок[префикс.Length..].Trim();

        return секрет.Length == 0 ? null : секрет;
    }
}
