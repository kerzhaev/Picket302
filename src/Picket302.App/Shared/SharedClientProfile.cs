using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Infrastructure.Integration;
using Picket302.Shared.Protocol;

namespace Picket302.App.Shared;

/// <summary>
/// Реквизиты подключения клиента к общей базе: адрес службы, устройство и
/// закреплённый отпечаток сервера.
/// </summary>
/// <remarks>
/// <para>
/// Реквизиты хранятся в профиле рабочего места: адрес службы и отпечаток
/// сервера — открытыми, секрет устройства — в защищённом хранилище Windows
/// (DPAPI) и отдельно от реквизитов.
/// </para>
/// <para>
/// Закреплённый отпечаток — обязательная часть: клиент сверяет его с
/// сертификатом, который предъявил сервер, при каждом подключении.
/// </para>
/// </remarks>
public sealed class SharedClientProfile
{
    /// <summary>
    /// Имя файла реквизитов подключения. Совпадает с именем, которое ищет
    /// COM-сервер автоматизации: правило сопряжения должно быть одно.
    /// </summary>
    public const string SettingsFileName = SharedWorkstationMode.SettingsFileName;

    /// <summary>Имя секрета устройства в защищённом хранилище.</summary>
    public const string DeviceSecretName = "shared-device-secret";

    private readonly string _путьРеквизитов;
    private readonly DpapiSecretStore _секреты;

    /// <summary>
    /// Создаёт профиль подключения к общей базе.
    /// </summary>
    /// <param name="каталогПрофиля">Каталог профиля приложения.</param>
    public SharedClientProfile(string каталогПрофиля)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогПрофиля);

        _путьРеквизитов = Path.Combine(каталогПрофиля, SettingsFileName);
        _секреты = new DpapiSecretStore(Path.Combine(каталогПрофиля, DpapiSecretStore.SecretsFolderName));
    }

    /// <summary>Путь к файлу реквизитов.</summary>
    public string ПутьРеквизитов => _путьРеквизитов;

    /// <summary>
    /// Читает реквизиты подключения; при отсутствии файла возвращает пустые.
    /// </summary>
    /// <returns>Реквизиты подключения.</returns>
    public SharedClientSettings Прочитать()
    {
        if (!File.Exists(_путьРеквизитов))
        {
            return new SharedClientSettings();
        }

        try
        {
            var текст = File.ReadAllText(_путьРеквизитов);

            return JsonSerializer.Deserialize<SharedClientSettings>(текст, ПараметрыJson)
                ?? new SharedClientSettings();
        }
        catch (JsonException)
        {
            return new SharedClientSettings();
        }
        catch (IOException)
        {
            return new SharedClientSettings();
        }
    }

    /// <summary>
    /// Сохраняет реквизиты подключения.
    /// </summary>
    /// <param name="реквизиты">Реквизиты подключения.</param>
    public void Сохранить(SharedClientSettings реквизиты)
    {
        ArgumentNullException.ThrowIfNull(реквизиты);

        var каталог = Path.GetDirectoryName(_путьРеквизитов);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        File.WriteAllText(
            _путьРеквизитов,
            JsonSerializer.Serialize(реквизиты, ПараметрыJson),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Сохраняет секрет устройства в защищённое хранилище Windows.
    /// </summary>
    /// <param name="секрет">Секрет устройства.</param>
    public void СохранитьСекрет(string секрет) => _секреты.Save(DeviceSecretName, секрет);

    /// <summary>
    /// Читает секрет устройства.
    /// </summary>
    /// <returns>Секрет устройства либо <see langword="null"/>.</returns>
    public string? ПрочитатьСекрет()
    {
        try
        {
            return _секреты.Exists(DeviceSecretName)
                ? _секреты.Load(DeviceSecretName)
                : null;
        }
        catch (CryptographicException)
        {
            // Секрет не расшифровывается текущим пользователем: устройство
            // необходимо сопрягнуть заново.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Удаляет секрет устройства и реквизиты подключения.
    /// </summary>
    public void Удалить()
    {
        try
        {
            if (_секреты.Exists(DeviceSecretName))
            {
                _секреты.Delete(DeviceSecretName);
            }
        }
        catch (IOException)
        {
            // Очистка не удалась: подключение всё равно невозможно без
            // секрета устройства.
        }

        try
        {
            if (File.Exists(_путьРеквизитов))
            {
                File.Delete(_путьРеквизитов);
            }
        }
        catch (IOException)
        {
            // Реквизиты остаются: без секрета устройства подключение
            // невозможно.
        }
    }

    private static JsonSerializerOptions ПараметрыJson => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>
/// Реквизиты подключения клиента к общей базе.
/// </summary>
/// <param name="Host">Адрес сервера (IP-адрес или DNS-имя).</param>
/// <param name="Port">Порт службы.</param>
/// <param name="DeviceId">Идентификатор сопряжённого устройства.</param>
/// <param name="ServerId">Идентификатор сервера.</param>
/// <param name="ServerFingerprint">
/// Закреплённый отпечаток открытого ключа сервера.
/// </param>
public sealed record SharedClientSettings(
    string Host = "",
    int Port = SharedProtocol.DefaultPort,
    string DeviceId = "",
    string ServerId = "",
    string ServerFingerprint = "")
{
    /// <summary>
    /// Признак того, что реквизиты заданы и пригодны для подключения.
    /// </summary>
    public bool Готовы =>
        !string.IsNullOrWhiteSpace(Host)
        && Port is > 0 and <= 65535
        && !string.IsNullOrWhiteSpace(DeviceId)
        && !string.IsNullOrWhiteSpace(ServerFingerprint);
}
