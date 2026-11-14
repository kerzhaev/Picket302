using System.Security.Cryptography;
using System.Text;
using Picket302.Shared.Protocol;

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
    /// <summary>Число знаков в части кода сопряжения, несущей отпечаток сервера.</summary>
    public const int ДлинаФрагмента = 8;

    /// <summary>Число знаков в одноразовой части кода сопряжения.</summary>
    public const int ДлинаСекрета = 16;

    /// <summary>Число знаков в одной группе кода сопряжения (для чтения вслух).</summary>
    public const int ЗнаковВГруппе = 4;

    /// <summary>Срок действия кода сопряжения.</summary>
    public static readonly TimeSpan СрокКодаСопряжения = TimeSpan.FromMinutes(10);

    /// <summary>Предельное число попыток сопряжения по одному коду.</summary>
    public const int МаксимумПопытокСопряжения = 5;

    /// <summary>Алфавит кода сопряжения: без похожих знаков (0/O, 1/I).</summary>
    private const string Алфавит = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

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
    /// Вычисляет отпечаток открытого ключа сертификата сервера (SPKI,
    /// SHA-256): клиент закрепляет это значение и сверяет его при каждом
    /// подключении.
    /// </summary>
    /// <param name="сертификат">Сертификат сервера.</param>
    /// <returns>Шестнадцатеричный SHA-256 открытого ключа.</returns>
    public static string ВычислитьОтпечатокСервера(
        System.Security.Cryptography.X509Certificates.X509Certificate2 сертификат)
        => SharedFingerprint.ВычислитьОтпечаток(сертификат);

    /// <summary>
    /// Вычисляет фрагмент отпечатка сервера: короткую часть, которая входит
    /// в код сопряжения и позволяет клиенту опознать сервер до отправки
    /// одноразового секрета.
    /// </summary>
    /// <param name="отпечаток">Полный отпечаток открытого ключа.</param>
    /// <returns>Фрагмент отпечатка в алфавите кода сопряжения.</returns>
    public static string ПолучитьФрагментОтпечатка(string отпечаток)
        => SharedFingerprint.ПолучитьФрагмент(отпечаток);

    /// <summary>
    /// Создаёт код сопряжения: фрагмент отпечатка сервера и одноразовый
    /// секрет в одной строке.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Код одноразовый и ограничен по времени: его секретная часть хранится
    /// на сервере только в виде отпечатка и действует ровно для одного
    /// подключения.
    /// </para>
    /// <para>
    /// Фрагмент отпечатка связывает код с ключом сервера: клиент сверяет его
    /// с сертификатом, который предъявил сервер, и принимает соединение только
    /// при совпадении. Поэтому секретная часть кода не передаётся тому, кто
    /// не является настоящим сервером, и проверка сертификата не отключается
    /// ни на одном шаге.
    /// </para>
    /// </remarks>
    /// <param name="фрагмент">Фрагмент отпечатка сервера.</param>
    /// <returns>Код сопряжения в виде групп через дефис.</returns>
    public static string СоздатьКодСопряжения(string фрагмент)
        => SharedFingerprint.СоздатьКод(фрагмент, СоздатьСлучайныйКод(ДлинаСекрета));

    /// <summary>
    /// Разделяет код сопряжения на фрагмент отпечатка и одноразовый секрет.
    /// </summary>
    /// <param name="код">Код сопряжения.</param>
    /// <returns>Фрагмент и секрет.</returns>
    public static (string Фрагмент, string Секрет) РазделитьКодСопряжения(string код)
        => SharedFingerprint.РазделитьКод(код);

    /// <summary>
    /// Вычисляет отпечаток одноразовой части кода сопряжения.
    /// </summary>
    /// <param name="секрет">Секретная часть кода.</param>
    /// <returns>Шестнадцатеричный SHA-256 секрета.</returns>
    public static string ВычислитьОтпечатокСекретаКода(string секрет)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(секрет ?? string.Empty)))
            .ToLowerInvariant();

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
    /// Приводит отпечаток к короткому виду «A1:B2:C3:…» для показа
    /// оператору.
    /// </summary>
    /// <param name="отпечаток">Полный отпечаток.</param>
    /// <returns>Короткая запись отпечатка.</returns>
    public static string СократитьОтпечаток(string отпечаток)
        => SharedFingerprint.СократитьОтпечаток(отпечаток);

    /// <summary>
    /// Создаёт случайный код заданной длины.
    /// </summary>
    /// <param name="длина">Число знаков.</param>
    /// <returns>Случайный код.</returns>
    private static string СоздатьСлучайныйКод(int длина)
    {
        var байты = RandomNumberGenerator.GetBytes(длина);
        var символы = new char[длина];

        for (var i = 0; i < длина; i++)
        {
            символы[i] = Алфавит[байты[i] % Алфавит.Length];
        }

        return new string(символы);
    }

}
