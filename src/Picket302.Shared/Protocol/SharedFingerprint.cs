using System.Security.Cryptography;
using System.Text;

namespace Picket302.Shared.Protocol;

/// <summary>
/// Отпечаток открытого ключа сервера и код сопряжения.
/// </summary>
/// <remarks>
/// <para>
/// Расчёты находятся в контрактном слое, чтобы клиент и служба вычисляли
/// отпечаток и фрагмент кода одинаково: иначе код, показанный
/// администратором, не совпал бы с тем, что проверяет клиент.
/// </para>
/// <para>
/// Код сопряжения состоит из двух частей: фрагмента отпечатка сервера и
/// одноразового секрета. Клиент сверяет фрагмент с сертификатом, который
/// предъявил сервер, и отправляет секрет только при совпадении. Поэтому
/// проверка сертификата не отключается ни на одном шаге, а секретная часть
/// кода не попадает к постороннему.
/// </para>
/// </remarks>
public static class SharedFingerprint
{
    /// <summary>Число знаков в части кода сопряжения, несущей отпечаток сервера.</summary>
    public const int ДлинаФрагмента = 8;

    /// <summary>Число знаков в одноразовой части кода сопряжения.</summary>
    public const int ДлинаСекрета = 16;

    /// <summary>Число знаков в одной группе кода сопряжения (для чтения вслух).</summary>
    public const int ЗнаковВГруппе = 4;

    /// <summary>Алфавит кода сопряжения: без похожих знаков (0/O, 1/I).</summary>
    private const string Алфавит = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Вычисляет отпечаток открытого ключа сертификата (SPKI, SHA-256).
    /// </summary>
    /// <param name="сертификат">Сертификат сервера.</param>
    /// <returns>Шестнадцатеричный SHA-256 открытого ключа.</returns>
    public static string ВычислитьОтпечаток(
        System.Security.Cryptography.X509Certificates.X509Certificate2 сертификат)
    {
        ArgumentNullException.ThrowIfNull(сертификат);

        return Convert.ToHexString(SHA256.HashData(сертификат.PublicKey.EncodedKeyValue.RawData))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Вычисляет фрагмент отпечатка сервера: короткую часть, входящую в код
    /// сопряжения.
    /// </summary>
    /// <param name="отпечаток">Полный отпечаток открытого ключа.</param>
    /// <returns>Фрагмент отпечатка в алфавите кода сопряжения.</returns>
    public static string ПолучитьФрагмент(string отпечаток)
    {
        if (string.IsNullOrWhiteSpace(отпечаток))
        {
            return string.Empty;
        }

        var байты = Convert.FromHexString(отпечаток);
        var код = КодироватьBase32(SHA256.HashData(байты));

        return код[..ДлинаФрагмента];
    }

    /// <summary>
    /// Создаёт код сопряжения: фрагмент отпечатка сервера и одноразовый
    /// секрет.
    /// </summary>
    /// <param name="фрагмент">Фрагмент отпечатка сервера.</param>
    /// <param name="секрет">Одноразовый секрет.</param>
    /// <returns>Код сопряжения в виде групп через дефис.</returns>
    public static string СоздатьКод(string фрагмент, string секрет)
    {
        var код = (фрагмент + секрет).ToUpperInvariant();

        return РазделитьНаГруппы(код);
    }

    /// <summary>
    /// Разделяет код сопряжения на фрагмент отпечатка и одноразовый секрет.
    /// </summary>
    /// <param name="код">Код сопряжения.</param>
    /// <returns>Фрагмент и секрет.</returns>
    public static (string Фрагмент, string Секрет) РазделитьКод(string код)
    {
        var очищенный = (код ?? string.Empty)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        if (очищенный.Length <= ДлинаФрагмента)
        {
            return (string.Empty, string.Empty);
        }

        return (
            очищенный[..ДлинаФрагмента],
            очищенный[ДлинаФрагмента..]);
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

    /// <summary>
    /// Разделяет строку на группы по четыре знака через дефис.
    /// </summary>
    /// <param name="значение">Строка.</param>
    /// <returns>Строка с группами.</returns>
    private static string РазделитьНаГруппы(string значение)
    {
        var группы = new List<string>();

        for (var i = 0; i < значение.Length; i += ЗнаковВГруппе)
        {
            группы.Add(значение.Substring(i, Math.Min(ЗнаковВГруппе, значение.Length - i)));
        }

        return string.Join("-", группы);
    }

    /// <summary>
    /// Кодирует байты в алфавит Base32 без выравнивания.
    /// </summary>
    /// <param name="байты">Кодируемые байты.</param>
    /// <returns>Строка Base32.</returns>
    private static string КодироватьBase32(byte[] байты)
    {
        var результат = new StringBuilder((байты.Length * 8 / 5) + 1);
        var буфер = 0;
        var битов = 0;

        foreach (var байт in байты)
        {
            буфер = (буфер << 8) | байт;
            битов += 8;

            while (битов >= 5)
            {
                битов -= 5;
                результат.Append(Алфавит[(буфер >> битов) & 31]);
            }
        }

        if (битов > 0)
        {
            результат.Append(Алфавит[(буфер << (5 - битов)) & 31]);
        }

        return результат.ToString();
    }
}
