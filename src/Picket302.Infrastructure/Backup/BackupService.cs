using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Domain.Backup;
using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Backup;

/// <summary>
/// Манифест архива базы данных: сведения о содержимом, версии схемы и параметрах
/// шифрования (ТЗ §8.1, функции 103–104; §10.4, §10.5).
/// </summary>
internal sealed class BackupManifest
{
    /// <summary>Версия формата манифеста.</summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    /// <summary>Момент создания архива (UTC).</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Версия схемы базы данных на момент архивирования.</summary>
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    /// <summary>Имя файла базы данных внутри архива.</summary>
    [JsonPropertyName("databaseEntry")]
    public string DatabaseEntry { get; set; } = BackupService.DatabaseEntryName;

    /// <summary>Размер файла базы данных, байт.</summary>
    [JsonPropertyName("databaseSize")]
    public long DatabaseSize { get; set; }

    /// <summary>Признак шифрования содержимого.</summary>
    [JsonPropertyName("encrypted")]
    public bool Encrypted { get; set; }

    /// <summary>Соль вывода ключа (base64).</summary>
    [JsonPropertyName("salt")]
    public string? Salt { get; set; }

    /// <summary>Вектор инициализации (base64).</summary>
    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }

    /// <summary>Метка аутентификации (base64).</summary>
    [JsonPropertyName("tag")]
    public string? Tag { get; set; }
}

/// <summary>
/// Служба архивирования и восстановления базы данных SQLite
/// (ТЗ §8.1, функции 103–104; §10.4, §10.5).
/// </summary>
/// <remarks>
/// <para>
/// Архив — стандартный ZIP-контейнер (совместим со «Сжатыми папками» Windows),
/// содержащий манифест и полный образ файла базы данных. Образ снимается
/// командой <c>VACUUM INTO</c> — он гарантированно целостен и не содержит
/// незакрытых страниц.
/// </para>
/// <para>
/// Пароль архива необязателен. При заданном пароле содержимое базы шифруется
/// алгоритмом AES-256-GCM, ключ выводится из пароля функцией PBKDF2 (SHA-256).
/// Неверный пароль приводит к ошибке аутентификации и не изменяет текущую базу.
/// </para>
/// <para>
/// Восстановление атомарно: текущая база переименовывается в страховочную копию,
/// после чего на её место устанавливается восстановленная.
/// </para>
/// </remarks>
public sealed class BackupService : IBackupService
{
    /// <summary>Имя записи файла базы данных внутри архива.</summary>
    public const string DatabaseEntryName = "database/picket302.db";

    /// <summary>Имя записи манифеста внутри архива.</summary>
    public const string ManifestEntryName = "manifest.json";

    /// <summary>Расширение страховочной копии при восстановлении.</summary>
    public const string SafetyCopySuffix = ".backup";

    /// <summary>Число итераций вывода ключа PBKDF2.</summary>
    private const int ИтерацииPbkdf2 = 210_000;

    /// <summary>Размер соли вывода ключа, байт.</summary>
    private const int РазмерСоли = 16;

    /// <summary>Размер вектора инициализации AES-GCM, байт.</summary>
    private const int РазмерNonce = 12;

    /// <summary>Размер метки аутентификации AES-GCM, байт.</summary>
    private const int РазмерМетки = 16;

    /// <summary>Размер ключа шифрования, байт (AES-256).</summary>
    private const int РазмерКлюча = 32;

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _путьБазы;
    private readonly IReadOnlyList<string> _известныеВерсииСхемы;

    /// <summary>Создаёт службу архивирования.</summary>
    /// <param name="databaseFilePath">Путь к файлу базы данных SQLite.</param>
    /// <remarks>
    /// Версии схемы, известные программе, берутся из встроенных миграций:
    /// проверка «версия схемы архива новее программы» (ТЗ §8.1, функция 104)
    /// выполняется автоматически.
    /// </remarks>
    public BackupService(string databaseFilePath)
        : this(databaseFilePath, ИзвестныеВерсииСхемыПрограммы())
    {
    }

    /// <summary>Создаёт службу архивирования со списком известных версий схемы.</summary>
    /// <param name="databaseFilePath">Путь к файлу базы данных SQLite.</param>
    /// <param name="knownSchemaVersions">
    /// Версии схемы, известные программе: более новая версия в архиве приводит
    /// к отказу восстановления (ТЗ §8.1, функция 104).
    /// </param>
    public BackupService(string databaseFilePath, IReadOnlyList<string> knownSchemaVersions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseFilePath);

        _путьБазы = Path.GetFullPath(databaseFilePath);
        _известныеВерсииСхемы = knownSchemaVersions ?? Array.Empty<string>();
    }

    /// <summary>Путь к файлу базы данных.</summary>
    public string DatabasePath => _путьБазы;

    /// <inheritdoc />
    public BackupResult BackupNow(BackupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.TargetFolder))
        {
            return BackupResult.Failure("Не указана папка назначения архива.");
        }

        if (!Directory.Exists(options.TargetFolder))
        {
            return BackupResult.Failure(
                $"Папка назначения архива недоступна: {options.TargetFolder}.");
        }

        var временнаяПапка = Path.Combine(Path.GetTempPath(), "Picket302Backup_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(временнаяПапка);

            var образ = Path.Combine(временнаяПапка, "picket302.db");
            var ошибкаСнятия = СнятьОбраз(образ);

            if (ошибкаСнятия is not null)
            {
                return BackupResult.Failure(ошибкаСнятия);
            }

            var версияСхемы = ПрочитатьВерсиюСхемы(образ);
            var имяФайла = ПодобратьИмяАрхива(options.TargetFolder, DateTime.Now);
            var путьАрхива = Path.Combine(options.TargetFolder, имяФайла);
            var временныйАрхив = Path.Combine(временнаяПапка, имяФайла);

            var ошибкаСборки = СобратьАрхив(образ, временныйАрхив, версияСхемы, options.Password);

            if (ошибкаСборки is not null)
            {
                return BackupResult.Failure(ошибкаСборки);
            }

            File.Move(временныйАрхив, путьАрхива, overwrite: false);

            return BackupResult.Success(путьАрхива);
        }
        catch (IOException ошибка)
        {
            return BackupResult.Failure($"Не удалось создать архив базы данных: {ошибка.Message}");
        }
        catch (UnauthorizedAccessException ошибка)
        {
            return BackupResult.Failure($"Нет доступа к папке назначения архива: {ошибка.Message}");
        }
        finally
        {
            ПопытатьсяУдалитьКаталог(временнаяПапка);
        }
    }

    /// <inheritdoc />
    public RestoreResult RestoreFromArchive(string archivePath, string password)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return RestoreResult.Failure("Не указан путь к архиву.");
        }

        if (!File.Exists(archivePath))
        {
            return RestoreResult.Failure($"Файл архива не найден: {archivePath}.");
        }

        var временнаяПапка = Path.Combine(Path.GetTempPath(), "Picket302Restore_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(временнаяПапка);

            var манифест = ПрочитатьМанифест(archivePath);

            if (манифест is null)
            {
                return RestoreResult.Failure("Архив повреждён: манифест не найден.");
            }

            var восстановленная = Path.Combine(временнаяПапка, "picket302.db");
            var ошибка = ИзвлечьБазу(archivePath, манифест, восстановленная, password);

            if (ошибка is not null)
            {
                return RestoreResult.Failure(ошибка);
            }

            var версияСхемы = ПрочитатьВерсиюСхемы(восстановленная);
            var ошибкаВерсии = ПроверитьВерсиюСхемы(версияСхемы);

            if (ошибкаВерсии is not null)
            {
                return RestoreResult.Failure(ошибкаВерсии);
            }

            var ошибкаЗамены = ЗаменитьБазу(восстановленная);

            if (ошибкаЗамены is not null)
            {
                return RestoreResult.Failure(ошибкаЗамены);
            }

            return RestoreResult.Success(версияСхемы);
        }
        catch (InvalidDataException ошибка)
        {
            return RestoreResult.Failure($"Архив повреждён: {ошибка.Message}");
        }
        catch (IOException ошибка)
        {
            return RestoreResult.Failure($"Не удалось восстановить базу данных: {ошибка.Message}");
        }
        catch (UnauthorizedAccessException ошибка)
        {
            return RestoreResult.Failure($"Нет доступа к файлу базы данных: {ошибка.Message}");
        }
        finally
        {
            ПопытатьсяУдалитьКаталог(временнаяПапка);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<BackupArchiveInfo> ListArchives(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Array.Empty<BackupArchiveInfo>();
        }

        var архивы = new List<BackupArchiveInfo>();

        foreach (var путь in Directory.EnumerateFiles(folder, BackupOptions.ArchiveNamePrefix + "*" + BackupOptions.ArchiveExtension))
        {
            var информация = new FileInfo(путь);
            var манифест = ПрочитатьМанифест(путь);

            архивы.Add(new BackupArchiveInfo(
                информация.Name,
                информация.FullName,
                информация.LastWriteTimeUtc,
                информация.Length,
                манифест?.SchemaVersion));
        }

        return архивы
            .OrderByDescending(архив => архив.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Снимает согласованный образ файла базы данных командой <c>VACUUM INTO</c>.
    /// </summary>
    /// <param name="путьОбраза">Путь создаваемого файла образа.</param>
    /// <returns>Текст ошибки либо <see langword="null"/> при успехе.</returns>
    private string? СнятьОбраз(string путьОбраза)
    {
        if (!File.Exists(_путьБазы))
        {
            return $"Файл базы данных не найден: {_путьБазы}.";
        }

        using var соединение = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = _путьБазы,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            }.ToString());

        соединение.Open();

        var каталог = Path.GetDirectoryName(путьОбраза)
            ?? throw new InvalidOperationException("Не удалось определить каталог образа базы данных.");
        Directory.CreateDirectory(каталог);

        using var команда = соединение.CreateCommand();
        команда.CommandText = "VACUUM INTO $path;";
        команда.Parameters.AddWithValue("$path", путьОбраза);
        команда.ExecuteNonQuery();

        return null;
    }

    /// <summary>
    /// Читает версию схемы базы данных из файла.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <returns>Номер последней применённой версии либо <see langword="null"/>.</returns>
    private static string? ПрочитатьВерсиюСхемы(string путьБазы)
    {
        using var соединение = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = путьБазы,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            }.ToString());

        соединение.Open();

        using var команда = соединение.CreateCommand();
        команда.CommandText = """
            SELECT version FROM schema_version ORDER BY id DESC LIMIT 1;
            """;

        return команда.ExecuteScalar() as string;
    }

    /// <summary>
    /// Собирает ZIP-архив с манифестом и образом базы данных.
    /// </summary>
    /// <param name="образ">Путь к файлу образа базы данных.</param>
    /// <param name="путьАрхива">Путь создаваемого архива.</param>
    /// <param name="версияСхемы">Версия схемы базы данных.</param>
    /// <param name="пароль">Необязательный пароль шифрования.</param>
    /// <returns>Текст ошибки либо <see langword="null"/> при успехе.</returns>
    private static string? СобратьАрхив(
        string образ,
        string путьАрхива,
        string? версияСхемы,
        string? пароль)
    {
        var манифест = new BackupManifest
        {
            CreatedAt = DateTime.UtcNow,
            SchemaVersion = версияСхемы,
            DatabaseSize = new FileInfo(образ).Length,
        };

        var содержимое = File.ReadAllBytes(образ);

        if (!string.IsNullOrEmpty(пароль))
        {
            var соль = RandomNumberGenerator.GetBytes(РазмерСоли);
            var nonce = RandomNumberGenerator.GetBytes(РазмерNonce);
            var ключ = ВывестиКлюч(пароль, соль);
            var шифротекст = new byte[содержимое.Length];
            var метка = new byte[РазмерМетки];

            using (var aes = new AesGcm(ключ, РазмерМетки))
            {
                aes.Encrypt(nonce, содержимое, шифротекст, метка);
            }

            содержимое = шифротекст;
            манифест.Encrypted = true;
            манифест.Salt = Convert.ToBase64String(соль);
            манифест.Nonce = Convert.ToBase64String(nonce);
            манифест.Tag = Convert.ToBase64String(метка);
        }

        try
        {
            using (var архив = System.IO.Compression.ZipFile.Open(
                путьАрхива,
                System.IO.Compression.ZipArchiveMode.Create))
            {
                var записьМанифеста = архив.CreateEntry(ManifestEntryName, System.IO.Compression.CompressionLevel.Optimal);
                using (var поток = записьМанифеста.Open())
                {
                    JsonSerializer.Serialize(поток, манифест, ПараметрыJson);
                }

                var записьБазы = архив.CreateEntry(BackupService.DatabaseEntryName, System.IO.Compression.CompressionLevel.Optimal);
                using var потокБазы = записьБазы.Open();
                потокБазы.Write(содержимое, 0, содержимое.Length);
            }
        }
        catch (CryptographicException ошибка)
        {
            return $"Не удалось зашифровать архив базы данных: {ошибка.Message}";
        }

        return null;
    }

    /// <summary>
    /// Читает манифест архива.
    /// </summary>
    /// <param name="путьАрхива">Путь к архиву.</param>
    /// <returns>Манифест либо <see langword="null"/>, если он отсутствует или не читается.</returns>
    private static BackupManifest? ПрочитатьМанифест(string путьАрхива)
    {
        try
        {
            using var архив = System.IO.Compression.ZipFile.OpenRead(путьАрхива);
            var запись = архив.GetEntry(ManifestEntryName);

            if (запись is null)
            {
                return null;
            }

            using var поток = запись.Open();
            return JsonSerializer.Deserialize<BackupManifest>(поток, ПараметрыJson);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Извлекает образ базы данных из архива, при необходимости расшифровывая его.
    /// </summary>
    /// <param name="путьАрхива">Путь к архиву.</param>
    /// <param name="манифест">Манифест архива.</param>
    /// <param name="путьБазы">Путь создаваемого файла базы данных.</param>
    /// <param name="пароль">Пароль архива.</param>
    /// <returns>Текст ошибки либо <see langword="null"/> при успехе.</returns>
    private static string? ИзвлечьБазу(
        string путьАрхива,
        BackupManifest манифест,
        string путьБазы,
        string? пароль)
    {
        byte[] содержимое;

        try
        {
            using var архив = System.IO.Compression.ZipFile.OpenRead(путьАрхива);
            var запись = архив.GetEntry(манифест.DatabaseEntry);

            if (запись is null)
            {
                return "Архив повреждён: файл базы данных не найден.";
            }

            using var поток = запись.Open();
            using var буфер = new MemoryStream();
            поток.CopyTo(буфер);
            содержимое = буфер.ToArray();
        }
        catch (InvalidDataException ошибка)
        {
            return $"Архив повреждён: {ошибка.Message}";
        }

        if (!манифест.Encrypted)
        {
            File.WriteAllBytes(путьБазы, содержимое);
            return null;
        }

        if (string.IsNullOrEmpty(пароль))
        {
            return "Архив защищён паролем, но пароль не задан.";
        }

        try
        {
            var соль = Convert.FromBase64String(манифест.Salt ?? string.Empty);
            var nonce = Convert.FromBase64String(манифест.Nonce ?? string.Empty);
            var метка = Convert.FromBase64String(манифест.Tag ?? string.Empty);
            var ключ = ВывестиКлюч(пароль, соль);
            var расшифровано = new byte[содержимое.Length];

            using var aes = new AesGcm(ключ, РазмерМетки);

            // Порядок аргументов AesGcm.Decrypt: nonce, ciphertext, tag, plaintext.
            aes.Decrypt(nonce, содержимое, метка, расшифровано);

            File.WriteAllBytes(путьБазы, расшифровано);
        }
        catch (CryptographicException)
        {
            return "Неверный пароль архива: содержимое не прошло проверку подлинности.";
        }
        catch (FormatException)
        {
            return "Архив повреждён: параметры шифрования некорректны.";
        }

        return null;
    }

    /// <summary>
    /// Проверяет, что версия схемы архива не новее известной программе.
    /// </summary>
    /// <param name="версияСхемы">Версия схемы из архива.</param>
    /// <returns>Текст ошибки либо <see langword="null"/> при успехе.</returns>
    /// <remarks>
    /// Сравнение числовое: архив со схемой старее известной программе версии
    /// допустим — недостающие миграции применяются после восстановления.
    /// Отказ возникает только когда схема архива новее программы (ТЗ §8.1,
    /// функция 104: «отказ с указанием требуемой версии»).
    /// </remarks>
    private string? ПроверитьВерсиюСхемы(string? версияСхемы)
    {
        if (версияСхемы is null || _известныеВерсииСхемы.Count == 0)
        {
            return null;
        }

        var требуемая = МаксимальнаяИзвестнаяВерсия();

        if (!int.TryParse(версияСхемы, NumberStyles.None, CultureInfo.InvariantCulture, out var номерАрхива))
        {
            return $"Версия схемы архива ({версияСхемы}) некорректна. "
                + $"Требуемая версия: {требуемая}.";
        }

        if (номерАрхива <= требуемая)
        {
            return null;
        }

        return $"Версия схемы архива ({версияСхемы}) новее установленной программы. "
            + $"Требуемая версия: {требуемая}.";
    }

    /// <summary>
    /// Возвращает наибольший номер версии схемы, известной программе.
    /// </summary>
    /// <returns>Номер максимальной известной версии схемы.</returns>
    private int МаксимальнаяИзвестнаяВерсия()
        => _известныеВерсииСхемы
            .Select(версия => int.TryParse(
                версия,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var номер)
                    ? номер
                    : 0)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// Возвращает версии схемы, известные программе: они определяются
    /// встроенными миграциями (ТЗ §3.1.19).
    /// </summary>
    /// <returns>Перечень номеров версий схемы.</returns>
    private static IReadOnlyList<string> ИзвестныеВерсииСхемыПрограммы()
        => new MigrationRunner().Migrations.Select(миграция => миграция.Version).ToArray();

    /// <summary>
    /// Заменяет текущую базу восстановленной: текущая база сохраняется как
    /// страховочная копия, затем устанавливается восстановленная.
    /// </summary>
    /// <param name="восстановленная">Путь к восстановленному файлу базы данных.</param>
    /// <returns>Текст ошибки либо <see langword="null"/> при успехе.</returns>
    private string? ЗаменитьБазу(string восстановленная)
    {
        var каталог = Path.GetDirectoryName(_путьБазы);

        if (каталог is null)
        {
            return "Не удалось определить каталог размещения базы данных.";
        }

        Directory.CreateDirectory(каталог);

        // Пул соединений Microsoft.Data.Sqlite удерживает дескрипторы файлов
        // базы: перед заменой файла пул освобождается, иначе Windows не даёт
        // переименовать используемый файл.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var страховочная = _путьБазы + SafetyCopySuffix;

        if (File.Exists(_путьБазы))
        {
            File.Move(_путьБазы, страховочная, overwrite: true);
        }

        try
        {
            File.Move(восстановленная, _путьБазы, overwrite: true);
        }
        catch (IOException ошибка)
        {
            if (File.Exists(страховочная) && !File.Exists(_путьБазы))
            {
                File.Move(страховочная, _путьБазы, overwrite: true);
            }

            return $"Не удалось установить восстановленную базу данных: {ошибка.Message}";
        }

        // Журнал упреждающей записи и индекс общей памяти прежней базы не должны
        // применяться к восстановленной: они удаляются вместе с заменой файла.
        УдалитьСопутствующиеФайлы(_путьБазы);

        return null;
    }

    /// <summary>
    /// Удаляет сопутствующие файлы базы данных SQLite (журнал WAL и индекс
    /// общей памяти), не препятствуя выполнению операции при ошибке.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    private static void УдалитьСопутствующиеФайлы(string путьБазы)
    {
        foreach (var суффикс in new[] { "-wal", "-shm" })
        {
            try
            {
                var путь = путьБазы + суффикс;

                if (File.Exists(путь))
                {
                    File.Delete(путь);
                }
            }
            catch (IOException)
            {
                // Сопутствующий файл не удалён — на результат операции не влияет.
            }
            catch (UnauthorizedAccessException)
            {
                // Сопутствующий файл не удалён — на результат операции не влияет.
            }
        }
    }

    /// <summary>
    /// Подбирает имя файла архива, не затирая ранее созданный архив.
    /// </summary>
    /// <param name="папка">Папка назначения.</param>
    /// <param name="момент">Момент создания архива.</param>
    /// <returns>Имя файла архива.</returns>
    private static string ПодобратьИмяАрхива(string папка, DateTime момент)
    {
        var имя = BackupOptions.BuildArchiveFileName(момент);
        var путь = Path.Combine(папка, имя);

        if (!File.Exists(путь))
        {
            return имя;
        }

        for (var номер = 2; номер < 1000; номер++)
        {
            var имяСНомером = $"{имя[..^BackupOptions.ArchiveExtension.Length]}_{номер}{BackupOptions.ArchiveExtension}";
            var путьСНомером = Path.Combine(папка, имяСНомером);

            if (!File.Exists(путьСНомером))
            {
                return имяСНомером;
            }
        }

        return имя;
    }

    /// <summary>
    /// Выводит ключ шифрования из пароля.
    /// </summary>
    /// <param name="пароль">Пароль.</param>
    /// <param name="соль">Соль вывода ключа.</param>
    /// <returns>Ключ длиной 32 байта.</returns>
    private static byte[] ВывестиКлюч(string пароль, byte[] соль)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(пароль),
            соль,
            ИтерацииPbkdf2,
            HashAlgorithmName.SHA256,
            РазмерКлюча);

    /// <summary>
    /// Удаляет временный каталог, не прерывая выполнение операции при ошибке.
    /// </summary>
    /// <param name="путь">Путь к каталогу.</param>
    private static void ПопытатьсяУдалитьКаталог(string путь)
    {
        try
        {
            if (Directory.Exists(путь))
            {
                Directory.Delete(путь, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временный каталог не удалён — на результат операции не влияет.
        }
        catch (UnauthorizedAccessException)
        {
            // Временный каталог не удалён — на результат операции не влияет.
        }
    }
}
