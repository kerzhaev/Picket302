using System.IO.Compression;
using System.Security.Cryptography;
using Dapper;
using Picket302.Infrastructure.Data;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Data;

/// <summary>
/// Хранилище управляемых пакетов шаблонов (.lbk) серверной службы.
/// </summary>
/// <remarks>
/// <para>
/// Пакет хранится на сервере вместе с номером версии и SHA-256 каждой
/// версии: клиент запрашивает конкретную версию для предпросмотра и печати,
/// а кэш на рабочем месте сравнивает отпечаток и не отдаёт устаревшее
/// содержимое после смены версии.
/// </para>
/// <para>
/// Файловая раскладка: <c>{каталог}\{packageId}\v{версия}.lbk</c> — содержимое
/// версии, рядом <c>v{версия}.sha256</c> — отпечаток содержимого. Запись
/// выполняется во временный файл в том же каталоге с последующим переименованием,
/// поэтому сбой не оставляет наполовину записанный пакет.
/// </para>
/// <para>
/// Присланный архив проверяется до записи: это ZIP с обязательным
/// <c>blank.xml</c>, число записей и распакованный объём ограничены, пути
/// записей не выходят за корень пакета. Содержимое файла на сервере клиент
/// не изменяет: путь к файлу клиенту не передаётся.
/// </para>
/// </remarks>
public sealed class SharedTemplatePackageStore
{
    private readonly string _каталог;
    private readonly ISqliteSession _сессия;

    /// <summary>
    /// Создаёт хранилище пакетов.
    /// </summary>
    /// <param name="каталог">Каталог пакетов шаблонов.</param>
    /// <param name="сессия">Сессия базы данных.</param>
    public SharedTemplatePackageStore(string каталог, ISqliteSession сессия)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталог);
        ArgumentNullException.ThrowIfNull(сессия);

        _каталог = каталог;
        _сессия = сессия;
    }

    /// <summary>
    /// Проверяет архив пакета на допустимость.
    /// </summary>
    /// <param name="содержимое">Содержимое пакета.</param>
    /// <exception cref="InvalidOperationException">
    /// Архив отклонён; текст поясняет причину и пригоден для показа
    /// оператору.
    /// </exception>
    public static void ПроверитьАрхив(byte[] содержимое)
    {
        ArgumentNullException.ThrowIfNull(содержимое);

        if (содержимое.Length == 0)
        {
            throw new InvalidOperationException("Пакет шаблона пуст.");
        }

        if (содержимое.Length > SharedProtocol.MaxTemplatePackageBytes)
        {
            throw new InvalidOperationException(
                $"Пакет шаблона больше допустимого ({SharedProtocol.MaxTemplatePackageBytes / (1024 * 1024)} МиБ).");
        }

        try
        {
            using var поток = new MemoryStream(содержимое);
            using var архив = new ZipArchive(поток, ZipArchiveMode.Read);

            var записи = архив.Entries;

            if (записи.Count > SharedProtocol.MaxTemplatePackageEntries)
            {
                throw new InvalidOperationException(
                    $"В пакете больше допустимого числа файлов ({SharedProtocol.MaxTemplatePackageEntries}).");
            }

            var распаковано = 0L;
            var естьБланк = false;

            foreach (var запись in записи)
            {
                ПроверитьИмяЗаписи(запись.FullName);

                распаковано += запись.Length;

                if (распаковано > SharedProtocol.MaxTemplatePackageExpandedBytes)
                {
                    throw new InvalidOperationException(
                        "Распакованное содержимое пакета больше допустимого "
                        + $"({SharedProtocol.MaxTemplatePackageExpandedBytes / (1024 * 1024)} МиБ).");
                }

                if (string.Equals(запись.FullName, "blank.xml", StringComparison.OrdinalIgnoreCase))
                {
                    естьБланк = true;
                }
            }

            if (!естьБланк)
            {
                throw new InvalidOperationException("В пакете нет файла blank.xml — описание бланка обязательно.");
            }
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("Пакет шаблона не является ZIP-архивом.");
        }
    }

    /// <summary>
    /// Проверяет, что путь записи не выходит за корень пакета.
    /// </summary>
    /// <param name="имя">Имя записи архива.</param>
    private static void ПроверитьИмяЗаписи(string имя)
    {
        if (string.IsNullOrWhiteSpace(имя))
        {
            throw new InvalidOperationException("В пакете есть запись без имени.");
        }

        var нормализованное = имя.Replace('\\', '/');

        if (нормализованное.StartsWith('/') || нормализованное.StartsWith("../", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Пакет содержит запись с недопустимым путём: {имя}");
        }

        if (нормализованное.Contains("/../", StringComparison.Ordinal)
            || нормализованное.EndsWith("/..", StringComparison.Ordinal)
            || нормализованное.EndsWith("/.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Пакет содержит запись с недопустимым путём: {имя}");
        }

        foreach (var сегмент in нормализованное.Split('/'))
        {
            if (сегмент.Length == 0)
            {
                continue;
            }

            if (сегмент.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                && сегмент.IndexOf(':') >= 0)
            {
                throw new InvalidOperationException(
                    $"Пакет содержит запись с недопустимым путём: {имя}");
            }
        }

        if (нормализованное.Length >= 2 && нормализованное[1] == ':')
        {
            throw new InvalidOperationException(
                $"Пакет содержит запись с недопустимым путём: {имя}");
        }
    }

    /// <summary>
    /// Сохраняет новую версию пакета.
    /// </summary>
    /// <param name="name">Наименование пакета.</param>
    /// <param name="содержимое">Содержимое пакета.</param>
    /// <param name="isBuiltin">Признак встроенного пакета поставки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Метаданные сохранённой версии.</returns>
    public async Task<TemplatePackageDto> СохранитьAsync(
        string name,
        byte[] содержимое,
        bool isBuiltin,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(содержимое);

        ПроверитьАрхив(содержимое);

        Directory.CreateDirectory(_каталог);

        var существующий = await НайтиПоИмениAsync(name, cancellationToken).ConfigureAwait(false);
        var идентификатор = существующий?.PackageId ?? Guid.NewGuid().ToString("D");
        var версия = (существующий?.Version ?? 0) + 1;

        var каталогПакета = Path.Combine(_каталог, идентификатор);
        Directory.CreateDirectory(каталогПакета);

        var отпечаток = Convert.ToHexString(SHA256.HashData(содержимое)).ToLowerInvariant();

        // Запись во временный файл и переименование: сбой не оставляет
        // наполовину записанную версию.
        var временный = Path.Combine(каталогПакета, $"v{версия}.tmp");
        var итоговый = Path.Combine(каталогПакета, $"v{версия}.lbk");
        var файлОтпечатка = Path.Combine(каталогПакета, $"v{версия}.sha256");

        await File.WriteAllBytesAsync(временный, содержимое, cancellationToken).ConfigureAwait(false);
        File.Move(временный, итоговый, overwrite: true);
        await File.WriteAllTextAsync(
            файлОтпечатка,
            отпечаток,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);

        await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO shared_template_packages
                    (package_id, name, version, content_hash, size_bytes, file_name, is_builtin, updated_at)
                VALUES (@Id, @Name, @Version, @Hash, @Size, @FileName, @Builtin, @Now)
                ON CONFLICT (package_id) DO UPDATE SET
                    version = @Version,
                    content_hash = @Hash,
                    size_bytes = @Size,
                    is_builtin = @Builtin,
                    updated_at = @Now;
                """,
                new
                {
                    Id = идентификатор,
                    Name = name,
                    Version = версия,
                    Hash = отпечаток,
                    Size = (long)содержимое.Length,
                    FileName = $"v{версия}.lbk",
                    Builtin = isBuiltin ? 1 : 0,
                    Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
                },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new TemplatePackageDto(
            идентификатор,
            name,
            версия,
            отпечаток,
            содержимое.Length,
            isBuiltin,
            DateTime.UtcNow);
    }

    /// <summary>
    /// Перечисляет пакеты.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Метаданные пакетов.</returns>
    public async Task<IReadOnlyList<TemplatePackageDto>> ПеречислитьAsync(
        CancellationToken cancellationToken = default)
    {
        var строки = await _сессия.Connection.QueryAsync<СтрокаПакета>(
            new CommandDefinition(
                """
                SELECT package_id AS PackageId,
                       name AS Name,
                       version AS Version,
                       content_hash AS ContentHash,
                       size_bytes AS SizeBytes,
                       is_builtin AS IsBuiltin,
                       updated_at AS UpdatedAt
                FROM shared_template_packages
                ORDER BY name;
                """,
                null,
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return строки
            .Select(строка => new TemplatePackageDto(
                строка.PackageId,
                строка.Name,
                строка.Version,
                строка.ContentHash,
                строка.SizeBytes,
                строка.IsBuiltin != 0,
                Разобрать(строка.UpdatedAt)))
            .ToArray();
    }

    /// <summary>
    /// Читает содержимое конкретной версии пакета.
    /// </summary>
    /// <param name="packageId">Идентификатор пакета.</param>
    /// <param name="version">Версия.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// Содержимое версии; <see langword="null"/> — версии с таким номером нет.
    /// </returns>
    public async Task<byte[]?> ПрочитатьВерсиюAsync(
        string packageId,
        long version,
        CancellationToken cancellationToken = default)
    {
        if (version <= 0)
        {
            throw new InvalidOperationException("Версия пакета должна быть положительной.");
        }

        // Идентификатор пакета — GUID из службы; составление пути из
        // произвольной строки клиента исключается проверкой формата.
        if (!Guid.TryParse(packageId, out _))
        {
            throw new InvalidOperationException("Идентификатор пакета недопустим.");
        }

        var файл = Path.Combine(_каталог, packageId, $"v{version}.lbk");

        if (!File.Exists(файл))
        {
            return null;
        }

        var содержимое = await File.ReadAllBytesAsync(файл, cancellationToken).ConfigureAwait(false);

        var отпечаток = Convert.ToHexString(SHA256.HashData(содержимое)).ToLowerInvariant();

        var файлОтпечатка = Path.Combine(_каталог, packageId, $"v{version}.sha256");

        if (File.Exists(файлОтпечатка))
        {
            var ожидаемый = (await File.ReadAllTextAsync(файлОтпечатка, cancellationToken)
                    .ConfigureAwait(false))
                .Trim();

            if (!string.Equals(отпечаток, ожидаемый, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Проверка целостности пакета не пройдена: содержимое на сервере повреждено.");
            }
        }

        return содержимое;
    }

    /// <summary>
    /// Удаляет пакет вместе с файлами всех версий.
    /// </summary>
    /// <param name="packageId">Идентификатор пакета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если пакет удалён.</returns>
    public async Task<bool> УдалитьAsync(string packageId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(packageId, out _))
        {
            throw new InvalidOperationException("Идентификатор пакета недопустим.");
        }

        var удалено = await _сессия.Connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM shared_template_packages WHERE package_id = @Id;",
                new { Id = packageId },
                _сессия.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (удалено == 0)
        {
            return false;
        }

        var каталогПакета = Path.Combine(_каталог, packageId);

        if (Directory.Exists(каталогПакета))
        {
            Directory.Delete(каталогПакета, recursive: true);
        }

        return true;
    }

    /// <summary>
    /// Ищет пакет по наименованию.
    /// </summary>
    /// <param name="name">Наименование пакета.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Метаданные пакета либо <see langword="null"/>.</returns>
    private async Task<TemplatePackageDto?> НайтиПоИмениAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var все = await ПеречислитьAsync(cancellationToken).ConfigureAwait(false);

        return все.FirstOrDefault(пакет =>
            string.Equals(пакет.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static DateTime Разобрать(string значение) =>
        DateTime.TryParse(
            значение,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var разобранное)
            ? разобранное
            : DateTime.MinValue;

    private sealed class СтрокаПакета
    {
        public string PackageId { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public long Version { get; init; }

        public string ContentHash { get; init; } = string.Empty;

        public long SizeBytes { get; init; }

        public int IsBuiltin { get; init; }

        public string UpdatedAt { get; init; } = string.Empty;
    }
}
