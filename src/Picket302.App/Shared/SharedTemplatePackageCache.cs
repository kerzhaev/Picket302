using System.Security.Cryptography;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;

namespace Picket302.App.Shared;

/// <summary>
/// Локальный кэш пакетов шаблонов рабочего места.
/// </summary>
/// <remarks>
/// <para>
/// Пакет .lbk хранится на сервере с версией и отпечатком SHA-256; рабочее
/// место держит копию той версии, с которой работает предпросмотр и печать.
/// Ключ записи кэша — идентификатор пакета, номер версии и отпечаток
/// содержимого: после смены версии на сервере кэш не отдаёт старый шаблон —
/// содержимое загружается заново и проверяется по отпечатку.
/// </para>
/// <para>
/// Файловая раскладка: <c>{каталог}\{packageId}\v{версия}-{отпечаток}.lbk</c>.
/// Запись во временный файл и переименование: сбой не оставляет
/// наполовину загруженный пакет.
/// </para>
/// </remarks>
public sealed class SharedTemplatePackageCache
{
    private readonly string _каталог;

    /// <summary>
    /// Создаёт кэш пакетов шаблонов.
    /// </summary>
    /// <param name="каталогПрофиля">Каталог профиля приложения.</param>
    public SharedTemplatePackageCache(string каталогПрофиля)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогПрофиля);

        _каталог = Path.Combine(каталогПрофиля, "Templates", "Cache");
    }

    /// <summary>Каталог кэша.</summary>
    public string Каталог => _каталог;

    /// <summary>
    /// Возвращает локальный путь к содержимому версии пакета, при
    /// необходимости загружая его с сервера.
    /// </summary>
    /// <param name="packageId">Идентификатор пакета.</param>
    /// <param name="version">Версия пакета.</param>
    /// <param name="contentHash">
    /// Ожидаемый отпечаток SHA-256 содержимого.
    /// </param>
    /// <param name="загрузить">
    /// Загрузка содержимого версии с сервера. Передаётся делегатом, чтобы
    /// кэш оставался проверяемым без сетевого транспорта.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Локальный путь к содержимому версии.</returns>
    public async Task<string> ПутьКВерсииAsync(
        string packageId,
        long version,
        string contentHash,
        Func<CancellationToken, Task<byte[]>> загрузить,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentNullException.ThrowIfNull(загрузить);

        var ожидаемый = contentHash.Trim().ToLowerInvariant();

        var каталогПакета = Path.Combine(_каталог, packageId);
        Directory.CreateDirectory(каталогПакета);

        var итоговый = Path.Combine(каталогПакета, $"v{version}-{ожидаемый}.lbk");

        if (File.Exists(итоговый)
            && ОтпечатокФайла(итоговый) == ожидаемый)
        {
            return итоговый;
        }

        var содержимое = await загрузить(cancellationToken).ConfigureAwait(false);

        var фактический = Convert.ToHexString(SHA256.HashData(содержимое)).ToLowerInvariant();

        if (!string.Equals(фактический, ожидаемый, StringComparison.Ordinal))
        {
            throw new SharedModeException(
                SharedErrorCodes.ArchiveRejected,
                "Проверка целостности пакета шаблона не пройдена: содержимое на сервере "
                + "не совпадает с объявленным отпечатком. Пакет не сохранён.");
        }

        var временный = Path.Combine(каталогПакета, $"v{version}.tmp");

        await File.WriteAllBytesAsync(временный, содержимое, cancellationToken).ConfigureAwait(false);
        File.Move(временный, итоговый, overwrite: true);

        return итоговый;
    }

    /// <summary>
    /// Загружает пакет шаблона на сервер.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    /// <param name="name">Наименование пакета.</param>
    /// <param name="путьКФайлу">Локальный путь к .lbk-файлу.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Метаданные сохранённой версии.</returns>
    public async Task<TemplatePackageDto> ЗагрузитьAsync(
        SharedTransport транспорт,
        string name,
        string путьКФайлу,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(путьКФайлу);

        var содержимое = await File.ReadAllBytesAsync(путьКФайлу, cancellationToken)
            .ConfigureAwait(false);

        return await транспорт
            .ВыгрузитьПакетШаблонаAsync(name, содержимое, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Вычисляет отпечаток содержимого файла.
    /// </summary>
    /// <param name="путь">Путь к файлу.</param>
    /// <returns>Шестнадцатеричный SHA-256.</returns>
    private static string ОтпечатокФайла(string путь)
    {
        try
        {
            using var поток = File.OpenRead(путь);

            return Convert.ToHexString(SHA256.HashData(поток)).ToLowerInvariant();
        }
        catch (IOException)
        {
            // Файл недоступен: кэш считается неполным, пакет загружается заново.
            return string.Empty;
        }
    }
}
