using System.Security.Cryptography;
using System.Text;

namespace Picket302.Service.Security;

/// <summary>
/// Криптографические помощники совместного режима.
/// </summary>
/// <remarks>
/// <para>
/// Секреты и коды сопряжения в открытом виде не хранятся: на сервере лежит
/// только проверяемый отпечаток, а сравнение выполняется постоянным временем,
/// чтобы по задержке нельзя было подбирать значение.
/// </para>
/// </remarks>
public static class SharedSecrets
{
    /// <summary>Длина кода сопряжения в знаках (без разделителей).</summary>
    public const int ДлинаКода = 12;

    /// <summary>Срок действия кода сопряжения.</summary>
    public static readonly TimeSpan СрокКодаСопряжения = TimeSpan.FromMinutes(10);

    /// <summary>Предельное число попыток сопряжения по одному коду.</summary>
    public const int МаксимумПопытокСопряжения = 5;

    /// <summary>
    /// Создаёт случайный секрет устройства.
    /// </summary>
    /// <remarks>
    /// Секрет записывается в заголовке авторизации, поэтому кодируется
    /// Base64Url без выравнивания: символы <c>+</c>, <c>/</c> и <c>=</c> в
    /// заголовке HTTP недопустимы.
    /// </remarks>
    /// <returns>Секрет в виде строки Base64Url (32 байта).</returns>
    public static string СоздатьСекретУстройства()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>
    /// Вычисляет проверяемый отпечаток секрета устройства: на сервере
    /// хранится только он.
    /// </summary>
    /// <param name="секрет">Секрет устройства.</param>
    /// <returns>Шестнадцатеричный SHA-256 секрета.</returns>
    public static string ВычислитьОтпечатокСекрета(string секрет)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(секрет ?? string.Empty)))
            .ToLowerInvariant();

    /// <summary>
    /// Создаёт код сопряжения: последовательность знаков, читаемая вслух.
    /// </summary>
    /// <returns>Код сопряжения.</returns>
    public static string СоздатьКодСопряжения()
    {
        // Алфавит без похожих знаков (0/O, 1/I): код диктует оператор.
        const string алфавит = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        var байты = RandomNumberGenerator.GetBytes(ДлинаКода);
        var символы = new char[ДлинаКода];

        for (var i = 0; i < ДлинаКода; i++)
        {
            символы[i] = алфавит[байты[i] % алфавит.Length];
        }

        return new string(символы);
    }

    /// <summary>
    /// Вычисляет отпечаток кода сопряжения в привязке к одноразовому
    ///challenge-значению: код без этого значения недействителен, даже если
    /// кто-то его перехватил.
    /// </summary>
    /// <param name="код">Код сопряжения.</param>
    /// <param name="nonce">Одноразовое значение вызова.</param>
    /// <returns>Шестнадцатеричный SHA-256.</returns>
    public static string ВычислитьОтпечатокКода(string код, string nonce)
    {
        var материал = Encoding.UTF8.GetBytes(
            (код ?? string.Empty).Trim().ToUpperInvariant() + ":" + (nonce ?? string.Empty));

        return Convert.ToHexString(SHA256.HashData(материал)).ToLowerInvariant();
    }

    /// <summary>
    /// Сравнивает две строки за постоянное время.
    /// </summary>
    /// <param name="левое">Первое значение.</param>
    /// <param name="правое">Второе значение.</param>
    /// <returns><see langword="true"/>, если значения равны.</returns>
    public static bool СравнитьПостоянно(string? левое, string? право)
    {
        var a = Encoding.UTF8.GetBytes(левое ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(право ?? string.Empty);

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Вычисляет отпечаток открытого ключа сертификата сервера (SPKI,
    /// SHA-256): клиент закрепляет это значение и сверяет его при каждом
    /// подключении.
    /// </summary>
    /// <param name="сертификат">Сертификат сервера.</param>
    /// <returns>Шестнадцатеричный SHA-256 открытого ключа.</returns>
    public static string ВычислитьОтпечатокСервера(System.Security.Cryptography.X509Certificates.X509Certificate2 сертификат)
    {
        ArgumentNullException.ThrowIfNull(сертификат);

        return Convert.ToHexString(SHA256.HashData(сертификат.PublicKey.EncodedKeyValue.RawData))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Приводит отпечаток к короткому виду «A1:B2:C3:…» для показа
    /// оператору.
    /// </summary>
    /// <param name="отпечаток">Полный отпечаток.</param>
    /// <returns>Короткая запись отпечатка.</returns>
    public static string СократитьОтпечаток(string отпечаток)
    {
        if (string.IsNullOrWhiteSpace(отпечаток))
        {
            return string.Empty;
        }

        var разделители = new List<string>();

        for (var i = 0; i + 1 < отпечаток.Length; i += 2)
        {
            разделители.Add(отпечаток.Substring(i, 2));
        }

        return string.Join(":", разделители);
    }
}
