using System.Text.Json;
using System.Text.Json.Serialization;

namespace Picket302.Shared.Protocol;

/// <summary>
/// Единые настройки сериализации протокола совместного режима.
/// </summary>
/// <remarks>
/// <para>
/// Настройки одинаковы на клиенте и на сервере: клиент читает ответы службы
/// теми же правилами, которыми служба читает запросы.
/// </para>
/// <para>
/// Перечисления сериализуются именами, а не числами: при разборе журнала
/// сообщение остаётся читаемым, а новое значение перечисления не ломает
/// разбор старых записей, пока имя не менялось.
/// </para>
/// </remarks>
public static class SharedJson
{
    /// <summary>
    /// Настройки сериализации протокола.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Создать();

    /// <summary>
    /// Создаёт настройки сериализации (используется в тестах, которым нужна
    /// отдельная копия настроек).
    /// </summary>
    /// <returns>Новый набор настроек.</returns>
    public static JsonSerializerOptions Создать()
    {
        var настройки = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };

        настройки.Converters.Add(new JsonStringEnumConverter());

        return настройки;
    }
}
