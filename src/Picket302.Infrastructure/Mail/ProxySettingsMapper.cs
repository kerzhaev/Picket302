using System.Globalization;
using System.Net;
using Picket302.Domain.Email;
using Picket302.Infrastructure.Integration;

namespace Picket302.Infrastructure.Mail;

/// <summary>
/// Построение параметров прокси-сервера для SMTP-клиента (ТЗ §8.7, функция 95:
/// «настройка прокси для исходящей почты»).
/// </summary>
/// <remarks>
/// <para>
/// Туннелирование выполняется самим SMTP-клиентом (команда HTTP CONNECT), а
/// <see cref="WebProxy"/> используется как описание параметров прокси: адрес,
/// порт и учётные данные авторизации.
/// </para>
/// <para>
/// Пароль прокси хранится в защищённом виде (DPAPI) и расшифровывается только
/// на время установления соединения; в открытом виде он не сохраняется.
/// </para>
/// </remarks>
public sealed class ProxySettingsMapper
{
    /// <summary>
    /// Сообщение при недоступности прокси-сервера.
    /// </summary>
    public const string UnavailableMessage = "Проверьте параметры прокси или отключите прокси";

    private readonly DpapiSecretStore _секреты;

    /// <summary>
    /// Создаёт построитель параметров прокси.
    /// </summary>
    /// <param name="секреты">
    /// Хранилище секретов DPAPI; по умолчанию — стандартная папка профиля
    /// пользователя.
    /// </param>
    public ProxySettingsMapper(DpapiSecretStore? секреты = null)
    {
        _секреты = секреты ?? new DpapiSecretStore();
    }

    /// <summary>
    /// Формирует параметры прокси по настройкам.
    /// </summary>
    /// <param name="settings">Настройки прокси.</param>
    /// <returns>
    /// Описание прокси; <see langword="null"/>, если прокси выключен или его
    /// параметры не заданы.
    /// </returns>
    public WebProxy? BuildProxy(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Server))
        {
            return null;
        }

        var порт = settings.Port is > 0 and <= 65535 ? settings.Port : 8080;

        if (!Uri.TryCreate(
                string.Create(CultureInfo.InvariantCulture, $"http://{settings.Server}:{порт}"),
                UriKind.Absolute,
                out var адрес))
        {
            return null;
        }

        var прокси = new WebProxy(адрес)
        {
            BypassProxyOnLocal = false,
        };

        if (!string.IsNullOrWhiteSpace(settings.Login))
        {
            var пароль = ЗащищённыйПароль.Расшифровать(_секреты, settings.ProtectedPassword);

            прокси.Credentials = new NetworkCredential(settings.Login, пароль ?? string.Empty);
        }

        return прокси;
    }

    /// <summary>
    /// Проверяет настройки прокси: при включённом прокси должны быть заданы
    /// адрес и корректный порт.
    /// </summary>
    /// <param name="settings">Настройки прокси.</param>
    /// <returns>Текст ошибки; пустая строка — настройки корректны.</returns>
    public string ValidateProxySettings(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(settings.Server))
        {
            return "Не указан адрес прокси-сервера.";
        }

        if (settings.Port is < 1 or > 65535)
        {
            return "Порт прокси-сервера должен быть в диапазоне 1..65535.";
        }

        if (!string.IsNullOrWhiteSpace(settings.ProtectedPassword)
            && string.IsNullOrWhiteSpace(settings.Login))
        {
            return "Задан пароль прокси, но не указан логин.";
        }

        return string.Empty;
    }
}
