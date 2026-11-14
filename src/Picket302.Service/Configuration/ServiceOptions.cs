using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Configuration;

/// <summary>
/// Настройки серверной службы совместного режима.
/// </summary>
/// <remarks>
/// <para>
/// Настройки хранятся в файле <c>service.json</c> в защищённом каталоге под
/// <c>ProgramData</c>. Файл читает только служба и локальная консоль
/// администратора; права на каталог ограничены учётной записью службы и
/// администраторами.
/// </para>
/// <para>
/// Секреты в этом файле не хранятся: отпечаток сертификата — идентификатор
/// открытого ключа, а не ключ.
/// </para>
/// </remarks>
public sealed class ServiceOptions
{
    /// <summary>Имя файла настроек службы.</summary>
    public const string SettingsFileName = "service.json";

    /// <summary>Порт службы по умолчанию.</summary>
    public const int DefaultPort = SharedProtocol.DefaultPort;

    /// <summary>
    /// Корневой каталог данных службы по умолчанию —
    /// <c>%ProgramData%\Picket302\Service</c>.
    /// </summary>
    public static string КаталогДанныхПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Picket302",
        "Service");

    /// <summary>Порт TCP, на котором служба принимает подключения.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Каталог данных службы (база, шаблоны, состояние).</summary>
    public string DataDirectory { get; set; } = КаталогДанныхПоУмолчанию();

    /// <summary>Отпечаток сертификата сервера; пусто — сертификат не задан.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Путь к файлу базы данных.</summary>
    public string DatabasePath => Path.Combine(DataDirectory, "picket302.db");

    /// <summary>Каталог управляемых пакетов шаблонов.</summary>
    public string TemplatesDirectory => Path.Combine(DataDirectory, "templates", "packages");

    /// <summary>Каталог журналов службы.</summary>
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Каталог резервных копий общей базы.</summary>
    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    /// <summary>Путь к файлу настроек службы.</summary>
    public string SettingsPath => Path.Combine(DataDirectory, SettingsFileName);

    /// <summary>
    /// Читает настройки из файла; при отсутствии или повреждении файла
    /// возвращает настройки по умолчанию.
    /// </summary>
    /// <param name="путьФайла">Путь к файлу настроек.</param>
    /// <returns>Настройки службы.</returns>
    public static ServiceOptions Прочитать(string путьФайла)
    {
        if (!File.Exists(путьФайла))
        {
            return new ServiceOptions();
        }

        try
        {
            var текст = File.ReadAllText(путьФайла);
            var настройки = JsonSerializer.Deserialize<ServiceOptions>(текст, ПараметрыJson);

            return настройки is null ? new ServiceOptions() : настройки;
        }
        catch (JsonException)
        {
            return new ServiceOptions();
        }
        catch (IOException)
        {
            return new ServiceOptions();
        }
    }

    /// <summary>
    /// Сохраняет настройки в файл, создавая каталог при необходимости.
    /// </summary>
    public void Сохранить()
    {
        Directory.CreateDirectory(DataDirectory);

        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(this, ПараметрыJson),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static JsonSerializerOptions ПараметрыJson => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
