using System.Text.Json;

namespace Picket302.Infrastructure.Integration;

/// <summary>
/// Режим работы рабочего места: сопряжено ли оно с общей базой.
/// </summary>
/// <remarks>
/// <para>
/// Проверка живёт в слое инфраструктуры, потому что её результат нужен
/// двум независимым частям программы: оболочке (которая знает, что
/// работает в общем режиме) и COM-серверу автоматизации (отдельный процесс,
/// который иначе открыл бы устаревшую локальную базу вместо общей).
/// </para>
/// <para>
/// Одна реализация на всех: расхождение в правилах проверки означало бы,
/// что COM-сервер перестаёт отказывать в общем режиме и молча работает с
/// локальной копией данных.
/// </para>
/// </remarks>
public static class SharedWorkstationMode
{
    /// <summary>Имя файла реквизитов сопряжения в профиле рабочего места.</summary>
    public const string SettingsFileName = "shared.json";

    /// <summary>Имя каталога приложения в профиле пользователя.</summary>
    public const string ApplicationDirectoryName = "Picket302";

    /// <summary>
    /// Возвращает каталог профиля рабочего места по умолчанию.
    /// </summary>
    /// <returns>Каталог профиля.</returns>
    public static string КаталогПрофиляПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ApplicationDirectoryName);

    /// <summary>
    /// Определяет, сопряжено ли рабочее место с общей базой.
    /// </summary>
    /// <param name="каталогПрофиля">
    /// Каталог профиля; пусто — профиль пользователя по умолчанию.
    /// </param>
    /// <returns>
    /// <see langword="true"/>, если в профиле есть реквизиты сопряжения:
    /// идентификатор устройства и закреплённый отпечаток сервера.
    /// </returns>
    /// <remarks>
    /// Файл реквизитов читается как документ JSON, а имена свойств ищутся без
    /// учёта регистра: правило сопряжения не должно зависеть от того, каким
    /// стилем именования записан файл.
    /// </remarks>
    public static bool СопряженоСОбщейБазой(string? каталогПрофиля = null)
    {
        try
        {
            var путь = Path.Combine(
                string.IsNullOrWhiteSpace(каталогПрофиля) ? КаталогПрофиляПоУмолчанию() : каталогПрофиля,
                SettingsFileName);

            if (!File.Exists(путь))
            {
                return false;
            }

            var текст = File.ReadAllText(путь);

            using var документ = JsonDocument.Parse(текст);

            if (документ.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var устройство = НайтиСвойство(документ.RootElement, "DeviceId");
            var отпечаток = НайтиСвойство(документ.RootElement, "ServerFingerprint");

            return !string.IsNullOrWhiteSpace(устройство)
                && !string.IsNullOrWhiteSpace(отпечаток);
        }
        catch (JsonException)
        {
            // Файл реквизитов повреждён: сопряжение не подтверждено.
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Находит значение свойства элемента без учёта регистра имени.
    /// </summary>
    /// <param name="элемент">Элемент JSON.</param>
    /// <param name="имя">Имя свойства.</param>
    /// <returns>Значение свойства либо <see langword="null"/>.</returns>
    private static string? НайтиСвойство(JsonElement элемент, string имя)
    {
        foreach (var свойство in элемент.EnumerateObject())
        {
            if (!string.Equals(свойство.Name, имя, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return свойство.Value.ValueKind == JsonValueKind.String
                ? свойство.Value.GetString()
                : свойство.Value.ToString();
        }

        return null;
    }
}
