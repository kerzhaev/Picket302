using System.Security.Cryptography;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Пара ключей подписи лицензии: открытый ключ проверяет подпись ответа
/// активации, закрытый ключ находится только у поставщика
/// (ТЗ §8.10, функция 122: «получает подписанный ответ (подпись проверяется
/// локально)»).
/// </summary>
/// <param name="ОткрытыйКлюч">
/// Открытый ключ в сыром формате: X и Y по 32 байта (64 байта).
/// </param>
/// <param name="ЗакрытыйКлюч">
/// Закрытый ключ в сыром формате: D, X и Y по 32 байта (96 байт).
/// </param>
public sealed record LicenseKeyPair(byte[] ОткрытыйКлюч, byte[] ЗакрытыйКлюч);

/// <summary>
/// Проверка и формирование подписи ответа активации
/// (ТЗ §8.10, функция 122; §10.7 «Лицензирование»).
/// </summary>
/// <remarks>
/// <para>
/// Применяется ECDSA с кривой P-256 (NIST P-256). Формат подписи фиксирован:
/// IEEE P1363 (конкатенация R и S по 32 байта без вычета S из N), длина 64
/// байта. Хеш — SHA-256.
/// </para>
/// <para>
/// Защита ограничена подписью ключа и ответа активации: никаких проверок
/// окружения и низкоуровневых приёмов самозащиты процесс не выполняет
/// (ТЗ §8.10, функция 125; §10.7 — явный запрет агрессивной самозащиты).
/// </para>
/// </remarks>
public sealed class LicenseSignature
{
    /// <summary>Текст ошибки при неверной подписи ответа.</summary>
    public const string ТекстОшибкиПовреждения = "Ответ повреждён: подпись не прошла проверку";

    /// <summary>Длина открытого ключа в сыром формате, байт.</summary>
    public const int РазмерОткрытогоКлюча = 64;

    /// <summary>Длина закрытого ключа в сыром формате, байт.</summary>
    public const int РазмерЗакрытогоКлюча = 96;

    /// <summary>Длина подписи в формате IEEE P1363, байт.</summary>
    public const int РазмерПодписи = 64;

    /// <summary>
    /// Текст последней ошибки проверки подписи; <see langword="null"/> —
    /// последняя проверка завершилась успешно.
    /// </summary>
    public string? ПоследняяОшибка { get; private set; }

    /// <summary>
    /// Проверяет подпись ответа активации.
    /// </summary>
    /// <param name="payload">Подписанные данные ответа.</param>
    /// <param name="signature">Подпись в формате IEEE P1363 (64 байта).</param>
    /// <param name="publicKey">Открытый ключ поставщика (64 байта).</param>
    /// <returns>
    /// <see langword="true"/> — подпись верна; <see langword="false"/> — подпись
    /// не прошла проверку (текст ошибки — в <see cref="ПоследняяОшибка"/>).
    /// </returns>
    public bool Verify(byte[] payload, byte[] signature, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (publicKey.Length != РазмерОткрытогоКлюча)
        {
            ПоследняяОшибка = "Ответ повреждён: открытый ключ поставщика имеет неверную длину.";
            return false;
        }

        if (signature.Length != РазмерПодписи)
        {
            ПоследняяОшибка = ТекстОшибкиПовреждения;
            return false;
        }

        try
        {
            var параметры = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = publicKey.AsSpan(0, 32).ToArray(),
                    Y = publicKey.AsSpan(32, 32).ToArray(),
                },
            };

            using var ecdsa = ECDsa.Create(параметры);

            var верна = ecdsa.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

            ПоследняяОшибка = верна ? null : ТекстОшибкиПовреждения;

            return верна;
        }
        catch (CryptographicException исключение)
        {
            ПоследняяОшибка = $"{ТекстОшибкиПовреждения} ({исключение.Message})";
            return false;
        }
    }

    /// <summary>
    /// Подписывает данные ответа активации.
    /// </summary>
    /// <param name="payload">Подписываемые данные.</param>
    /// <param name="privateKey">Закрытый ключ в сыром формате (96 байт).</param>
    /// <returns>Подпись в формате IEEE P1363 (64 байта).</returns>
    public byte[] Sign(byte[] payload, byte[] privateKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(privateKey);

        if (privateKey.Length != РазмерЗакрытогоКлюча)
        {
            throw new CryptographicException(
                "Закрытый ключ имеет неверную длину: ожидается 96 байт (D, X, Y).");
        }

        var параметры = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = privateKey.AsSpan(32, 32).ToArray(),
                Y = privateKey.AsSpan(64, 32).ToArray(),
            },
            D = privateKey.AsSpan(0, 32).ToArray(),
        };

        using var ecdsa = ECDsa.Create(параметры);

        return ecdsa.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// Создаёт пару ключей ECDSA P-256 для подписи ответа активации.
    /// </summary>
    /// <remarks>
    /// Метод предназначен для сервера активации и для проверочных сценариев:
    /// в рабочей поставке закрытый ключ остаётся у поставщика.
    /// </remarks>
    /// <returns>Пара ключей.</returns>
    public static LicenseKeyPair GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var параметры = ecdsa.ExportParameters(includePrivateParameters: true);

        var открытый = new byte[РазмерОткрытогоКлюча];
        var закрытый = new byte[РазмерЗакрытогоКлюча];

        Buffer.BlockCopy(параметры.Q.X!, 0, открытый, 0, 32);
        Buffer.BlockCopy(параметры.Q.Y!, 0, открытый, 32, 32);
        Buffer.BlockCopy(параметры.D!, 0, закрытый, 0, 32);
        Buffer.BlockCopy(параметры.Q.X!, 0, закрытый, 32, 32);
        Buffer.BlockCopy(параметры.Q.Y!, 0, закрытый, 64, 32);

        return new LicenseKeyPair(открытый, закрытый);
    }
}
