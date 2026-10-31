using System.Security.Cryptography;
using System.Text;

namespace Picket302.Domain.Entities;

/// <summary>
/// Кэш сгенерированных векторных и растровых кодов (ТЗ §3.1.8, таблица <c>qr_cache</c>).
/// </summary>
/// <remarks>
/// Ключом записи служит SHA-256 хэш строки данных (<c>payload_hash</c>, уникальный).
/// В кэш попадает несжатый растр 1-bpp/8-bpp в исходном DPI без антиалиасинга.
/// </remarks>
public sealed class QrCache
{
    /// <summary>Длина hex-представления SHA-256 (ТЗ §3.1.8: <c>CHAR(64)</c>).</summary>
    public const int PayloadHashLength = 64;

    /// <summary>Идентификатор записи кэша (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>SHA-256 хэш строки данных; уникальный в пределах кэша.</summary>
    public string PayloadHash { get; set; } = string.Empty;

    /// <summary>Растровое изображение кода.</summary>
    public byte[] ImageBlob { get; set; } = Array.Empty<byte>();

    /// <summary>Момент создания записи кэша (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Вычисляет SHA-256 хэш строки данных в виде hex-строки длиной 64 знака.
    /// </summary>
    /// <param name="payload">Строка данных, закодированная в код.</param>
    /// <returns>Hex-представление SHA-256 хэша.</returns>
    public static string ComputePayloadHash(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var байты = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(байты).ToLowerInvariant();
    }

    /// <summary>
    /// Создаёт запись кэша: вычисляет ключ хэша по строке данных.
    /// </summary>
    /// <param name="payload">Строка данных, закодированная в код.</param>
    /// <param name="imageBlob">Растровое изображение кода.</param>
    /// <param name="createdAt">Момент создания записи (UTC).</param>
    /// <returns>Готовая запись кэша.</returns>
    public static QrCache Create(string payload, byte[] imageBlob, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(imageBlob);

        return new QrCache
        {
            PayloadHash = ComputePayloadHash(payload),
            ImageBlob = imageBlob,
            CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
        };
    }
}
