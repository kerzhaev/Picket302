namespace Picket302.Domain.Backup;

/// <summary>
/// Служба архивирования и восстановления базы данных (ТЗ §8.1, функции 103–104;
/// §10.4, §10.5).
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Создаёт полный архив базы данных (ТЗ §8.1, функция 103).
    /// </summary>
    /// <param name="options">Параметры создания архива.</param>
    /// <returns>Результат создания архива.</returns>
    BackupResult BackupNow(BackupOptions options);

    /// <summary>
    /// Восстанавливает базу данных из архива (ТЗ §8.1, функция 104).
    /// </summary>
    /// <param name="archivePath">Путь к архиву.</param>
    /// <param name="password">Пароль архива; пустая строка — архив без пароля.</param>
    /// <returns>Результат восстановления.</returns>
    RestoreResult RestoreFromArchive(string archivePath, string password);

    /// <summary>
    /// Перечисляет архивы базы данных в указанной папке.
    /// </summary>
    /// <param name="folder">Папка хранения архивов.</param>
    /// <returns>Сведения об архивах, упорядоченные по времени создания.</returns>
    IReadOnlyList<BackupArchiveInfo> ListArchives(string folder);
}
