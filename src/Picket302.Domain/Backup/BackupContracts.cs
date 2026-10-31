using System.Globalization;

namespace Picket302.Domain.Backup;

/// <summary>
/// Параметры создания архива базы данных (ТЗ §8.1, функция 103; §10.4).
/// </summary>
/// <param name="TargetFolder">Папка назначения архива (любая папка Windows, включая сетевую).</param>
/// <param name="Password">
/// Необязательный пароль: при заданном значении содержимое базы шифруется
/// алгоритмом AES-256-GCM, ключ выводится из пароля функцией PBKDF2.
/// </param>
public sealed record BackupOptions(string TargetFolder, string? Password = null)
{
    /// <summary>Префикс имени файла архива (ТЗ §7.8, §10.4).</summary>
    public const string ArchiveNamePrefix = "Base_";

    /// <summary>Расширение файла архива.</summary>
    public const string ArchiveExtension = ".zip";

    /// <summary>
    /// Формирует имя файла архива вида <c>Base_ГГММДДЧЧММ.zip</c>
    /// (ТЗ §7.8: «Имя архива: Base_ГГММДДЧЧММ.zip (дата + время создания)»).
    /// </summary>
    /// <param name="moment">Момент создания архива (локальное время).</param>
    /// <returns>Имя файла архива.</returns>
    /// <remarks>
    /// Формат метки времени задаётся инвариантной культурой: в локалях с
    /// негригорианским календарём культура по умолчанию дала бы нечто, отличное
    /// от требуемого «ГГММДДЧЧММ».
    /// </remarks>
    public static string BuildArchiveFileName(DateTime moment)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{ArchiveNamePrefix}{moment:yyMMddHHmm}{ArchiveExtension}");
}

/// <summary>
/// Результат создания архива базы данных.
/// </summary>
/// <param name="IsSuccess">Признак успешного исхода.</param>
/// <param name="ArchivePath">Путь к созданному архиву; <see langword="null"/> при ошибке.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
public sealed record BackupResult(bool IsSuccess, string? ArchivePath, string? Error)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Создаёт успешный результат.</summary>
    /// <param name="archivePath">Путь к созданному архиву.</param>
    /// <returns>Успешный результат.</returns>
    public static BackupResult Success(string archivePath) => new(true, archivePath, null);

    /// <summary>Создаёт неуспешный результат.</summary>
    /// <param name="error">Текст ошибки.</param>
    /// <returns>Неуспешный результат.</returns>
    public static BackupResult Failure(string error) => new(false, null, error);
}

/// <summary>
/// Результат восстановления базы данных из архива.
/// </summary>
/// <param name="IsSuccess">Признак успешного исхода.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
/// <param name="SchemaVersion">Версия схемы, восстановленная из архива.</param>
public sealed record RestoreResult(bool IsSuccess, string? Error, string? SchemaVersion)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Создаёт успешный результат.</summary>
    /// <param name="schemaVersion">Версия схемы в восстановленной базе.</param>
    /// <returns>Успешный результат.</returns>
    public static RestoreResult Success(string? schemaVersion) => new(true, null, schemaVersion);

    /// <summary>Создаёт неуспешный результат.</summary>
    /// <param name="error">Текст ошибки.</param>
    /// <returns>Неуспешный результат.</returns>
    public static RestoreResult Failure(string error) => new(false, error, null);
}

/// <summary>
/// Сведения об архиве базы данных в папке хранения.
/// </summary>
/// <param name="FileName">Имя файла архива.</param>
/// <param name="FullPath">Полный путь к архиву.</param>
/// <param name="CreatedAt">Момент создания архива (UTC).</param>
/// <param name="SizeInBytes">Размер архива, байт.</param>
/// <param name="SchemaVersion">Версия схемы, зафиксированная в манифесте архива.</param>
public sealed record BackupArchiveInfo(
    string FileName,
    string FullPath,
    DateTime CreatedAt,
    long SizeInBytes,
    string? SchemaVersion);
