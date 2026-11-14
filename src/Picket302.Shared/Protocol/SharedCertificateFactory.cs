using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Picket302.Shared.Protocol;

/// <summary>
/// Создание самоподписанного сертификата сервера.
/// </summary>
/// <remarks>
/// <para>
/// Сертификат создаётся на серверном компьютере при установке службы и
/// кладётся в хранилище сертификатов компьютера. Ключ не предназначен для
/// выдачи третьим лицам: он нужен, чтобы клиент мог опознать сервер по
/// отпечатку открытого ключа, показанному администратором.
/// </para>
/// <para>
/// Клиент проверяет не цепочку доверия, а сам ключ: сертификат выдан сам
/// собой, и доверия к центру сертификации здесь нет.
/// </para>
/// </remarks>
public static class SharedCertificateFactory
{
    /// <summary>Срок действия сертификата сервера, лет.</summary>
    public const int СрокЛет = 10;

    /// <summary>
    /// Создаёт самоподписанный сертификат сервера.
    /// </summary>
    /// <param name="имяСубъекта">Имя субъекта сертификата.</param>
    /// <param name="dnsИмена">Дополнительные DNS-имена в сертификате.</param>
    /// <param name="ipАдреса">IP-адреса в сертификате.</param>
    /// <returns>Готовый сертификат с закрытым ключом.</returns>
    public static X509Certificate2 Создать(
        string имяСубъекта,
        IEnumerable<string>? dnsИмена = null,
        IEnumerable<string>? ipАдреса = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяСубъекта);

        using var ключ = RSA.Create(3072);

        var запрос = new CertificateRequest(
            $"CN={имяСубъекта}",
            ключ,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        запрос.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        var альтернативы = new SubjectAlternativeNameBuilder();
        var добавлено = 0;

        foreach (var dns in dnsИмена ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(dns))
            {
                альтернативы.AddDnsName(dns.Trim());
                добавлено++;
            }
        }

        foreach (var адрес in ipАдреса ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(адрес)
                && IPAddress.TryParse(адрес.Trim(), out var разобранный))
            {
                альтернативы.AddIpAddress(разобранный);
                добавлено++;
            }
        }

        if (добавлено > 0)
        {
            запрос.CertificateExtensions.Add(альтернативы.Build());
        }

        var начало = DateTimeOffset.UtcNow.AddDays(-1);
        var конец = начало.AddYears(СрокЛет);

        var сертификат = запрос.CreateSelfSigned(начало, конец);

        // Сертификат переносится в хранилище компьютера установщиком, поэтому
        // он должен переносить закрытый ключ отдельно от создавшего его
        // объекта: экспорт в PFX и повторный импорт дают сертификату
        // собственный ключ.
        var пакет = сертификат.Export(X509ContentType.Pfx);

        return new X509Certificate2(пакет, (string?)null, X509KeyStorageFlags.Exportable);
    }
}
