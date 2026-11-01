using System.Security.Cryptography;
using System.Text;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Шифрование данных лицензии алгоритмом AES-256-GCM
/// (ТЗ §8.10, функция 124: «Локальные данные лицензии шифруются симметричным
/// алгоритмом с ключом, производным от MachineID (AES-256)»).
/// </summary>
/// <remarks>
/// <para>
/// Ключ выводится из идентификатора оборудования (MachineID) и не хранится
/// рядом с данными и не выводится в журнал: в защищённом блоке содержатся
/// только соль, nonce и метка аутентификации.
/// </para>
/// <para>
/// Формат защищённого блока (ТЗ §8.10, функция 124):
/// <list type="bullet">
/// <item><description>1 байт — версия формата;</description></item>
/// <item><description>16 байт — соль;</description></item>
/// <item><description>12 байт — nonce;</description></item>
/// <item><description>16 байт — метка аутентификации GCM;</description></item>
/// <item><description>шифротекст.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class LicenseCrypto
{
    /// <summary>Версия формата защищённого блока.</summary>
    public const byte ВерсияФормата = 1;

    /// <summary>Размер соли, байт.</summary>
    public const int РазмерСоли = 16;

    /// <summary>Размер nonce, байт.</summary>
    public const int РазмерNonce = 12;

    /// <summary>Размер метки аутентификации GCM, байт.</summary>
    public const int РазмерМетки = 16;

    /// <summary>Длина префикса защищённого блока (версия, соль, nonce, метка), байт.</summary>
    public const int РазмерПрефикса = 1 + РазмерСоли + РазмерNonce + РазмерМетки;

    private readonly byte[] _корневойКлюч;

    /// <summary>
    /// Создаёт шифровальщик данных лицензии для указанного оборудования.
    /// </summary>
    /// <param name="machineId">
    /// Идентификатор оборудования (MachineID); из него выводится ключ
    /// шифрования (ТЗ §8.10, функция 124).
    /// </param>
    /// <exception cref="ArgumentException">Идентификатор не задан.</exception>
    public LicenseCrypto(string machineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);

        // Корневой ключ выводится один раз из MachineID и хранится только
        // в памяти: рядом с данными лицензии он не сохраняется.
        _корневойКлюч = SHA256.HashData(Encoding.UTF8.GetBytes(machineId));
    }

    /// <summary>
    /// Шифрует данные лицензии.
    /// </summary>
    /// <param name="plain">Открытые данные лицензии.</param>
    /// <returns>Защищённый блок в формате, описанном в документации типа.</returns>
    /// <exception cref="ArgumentNullException">Данные не заданы.</exception>
    public byte[] Protect(byte[] plain)
    {
        ArgumentNullException.ThrowIfNull(plain);

        var соль = RandomNumberGenerator.GetBytes(РазмерСоли);
        var nonce = RandomNumberGenerator.GetBytes(РазмерNonce);
        var ключ = ВывестиКлюч(соль, nonce);

        try
        {
            var шифротекст = new byte[plain.Length];
            var метка = new byte[РазмерМетки];

            using (var aes = new AesGcm(ключ, РазмерМетки))
            {
                aes.Encrypt(nonce, plain, шифротекст, метка);
            }

            var результат = new byte[РазмерПрефикса + шифротекст.Length];

            результат[0] = ВерсияФормата;
            Buffer.BlockCopy(соль, 0, результат, 1, РазмерСоли);
            Buffer.BlockCopy(nonce, 0, результат, 1 + РазмерСоли, РазмерNonce);
            Buffer.BlockCopy(метка, 0, результат, 1 + РазмерСоли + РазмерNonce, РазмерМетки);
            Buffer.BlockCopy(шифротекст, 0, результат, РазмерПрефикса, шифротекст.Length);

            return результат;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ключ);
        }
    }

    /// <summary>
    /// Расшифровывает данные лицензии.
    /// </summary>
    /// <param name="cipher">Защищённый блок.</param>
    /// <returns>Открытые данные лицензии.</returns>
    /// <exception cref="ArgumentNullException">Блок не задан.</exception>
    /// <exception cref="CryptographicException">
    /// Блок повреждён, получен из другой версии формата или зашифрован ключом
    /// другого оборудования. Вызывающий код переводит такую ошибку в состояние
    /// «данные лицензии отсутствуют, состояние trial» (ТЗ §8.10, функция 124).
    /// </exception>
    public byte[] Unprotect(byte[] cipher)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        if (cipher.Length < РазмерПрефикса)
        {
            throw new CryptographicException(
                "Данные лицензии повреждены: неверная длина защищённого блока.");
        }

        if (cipher[0] != ВерсияФормата)
        {
            throw new CryptographicException(
                "Данные лицензии повреждены: неизвестная версия формата защищённого блока.");
        }

        var соль = new byte[РазмерСоли];
        var nonce = new byte[РазмерNonce];
        var метка = new byte[РазмерМетки];

        Buffer.BlockCopy(cipher, 1, соль, 0, РазмерСоли);
        Buffer.BlockCopy(cipher, 1 + РазмерСоли, nonce, 0, РазмерNonce);
        Buffer.BlockCopy(cipher, 1 + РазмерСоли + РазмерNonce, метка, 0, РазмерМетки);

        var шифротекст = new byte[cipher.Length - РазмерПрефикса];
        Buffer.BlockCopy(cipher, РазмерПрефикса, шифротекст, 0, шифротекст.Length);

        var ключ = ВывестиКлюч(соль, nonce);

        try
        {
            var открытые = new byte[шифротекст.Length];

            using (var aes = new AesGcm(ключ, РазмерМетки))
            {
                aes.Decrypt(nonce, шифротекст, метка, открытые);
            }

            return открытые;
        }
        catch (CryptographicException исключение)
        {
            throw new CryptographicException(
                "Не удалось расшифровать данные лицензии: блок повреждён либо зашифрован ключом другого оборудования.",
                исключение);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ключ);
        }
    }

    /// <summary>
    /// Выводит ключ шифрования для конкретного сообщения из корневого ключа
    /// и соли с nonce: один корневой ключ не используется напрямую.
    /// </summary>
    /// <param name="соль">Соль защищённого блока.</param>
    /// <param name="nonce">Nonce защищённого блока.</param>
    /// <returns>Ключ AES-256 (32 байта).</returns>
    private byte[] ВывестиКлюч(byte[] соль, byte[] nonce)
    {
        var данные = new byte[соль.Length + nonce.Length];

        Buffer.BlockCopy(соль, 0, данные, 0, соль.Length);
        Buffer.BlockCopy(nonce, 0, данные, соль.Length, nonce.Length);

        using var hmac = new HMACSHA256(_корневойКлюч);

        return hmac.ComputeHash(данные);
    }
}
