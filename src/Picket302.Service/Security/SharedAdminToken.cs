using System.Security.Cryptography;
using System.Text;

namespace Picket302.Service.Security;

/// <summary>
/// Административный токен службы: подтверждает права локального
/// администратора на служебные операции.
/// </summary>
/// <remarks>
/// <para>
/// Токен хранится в файле в защищённом каталоге данных службы и читается
/// только локальной консолью администратора. Служебные запросы принимаются
/// лишь с локального адреса и лишь с этим токеном: удалённым запросом код
/// сопряжения получить нельзя.
/// </para>
/// </remarks>
public sealed class SharedAdminToken
{
    /// <summary>Имя файла административного токена.</summary>
    public const string ИмяФайла = "admin.token";

    private readonly string _путь;

    /// <summary>
    /// Создаёт хранилище административного токена.
    /// </summary>
    /// <param name="каталогДанных">Каталог данных службы.</param>
    public SharedAdminToken(string каталогДанных)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогДанных);

        _путь = Path.Combine(каталогДанных, ИмяФайла);
    }

    /// <summary>Путь к файлу административного токена.</summary>
    public string Путь => _путь;

    /// <summary>
    /// Возвращает административный токен, создавая его при первом обращении.
    /// </summary>
    /// <returns>Токен в виде строки Base64.</returns>
    public string ПолучитьИлиСоздать()
    {
        if (File.Exists(_путь))
        {
            try
            {
                var содержимое = File.ReadAllText(_путь).Trim();

                if (содержимое.Length > 0)
                {
                    return содержимое;
                }
            }
            catch (IOException)
            {
                // Файл недоступен: создаётся новый токен.
            }
        }

        var токен = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Directory.CreateDirectory(Path.GetDirectoryName(_путь) ?? ".");

        File.WriteAllText(
            _путь,
            токен,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return токен;
    }

    /// <summary>
    /// Сверяет предъявленный токен с сохранённым.
    /// </summary>
    /// <param name="предъявленный">Предъявленный токен.</param>
    /// <returns><see langword="true"/>, если токен верный.</returns>
    public bool Проверить(string? предъявленный)
    {
        if (string.IsNullOrWhiteSpace(предъявленный) || !File.Exists(_путь))
        {
            return false;
        }

        try
        {
            var сохранённый = File.ReadAllText(_путь).Trim();

            return SharedSecrets.СравнитьПостоянно(сохранённый, предъявленный.Trim());
        }
        catch (IOException)
        {
            return false;
        }
    }
}
