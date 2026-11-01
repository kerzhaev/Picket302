using System.Globalization;
using System.Text.Json;

using Picket302.Domain.Backup;
using Picket302.Infrastructure.Data;

namespace Picket302.Infrastructure.Backup;

/// <summary>
/// Предпросмотр содержимого архива базы данных
/// (ТЗ §10.5: «предпросмотр (дата, версия схемы, объем)»; §8.11, функция 129).
/// </summary>
/// <param name="FileName">Имя файла архива.</param>
/// <param name="FullPath">Полный путь к архиву.</param>
/// <param name="CreatedAtUtc">Момент создания архива (UTC).</param>
/// <param name="SchemaVersion">Версия схемы базы данных в архиве.</param>
/// <param name="SizeInBytes">Размер архива, байт.</param>
/// <param name="CreatedAtLocal">Момент создания архива в локальном времени.</param>
/// <param name="IsPasswordProtected">Признак защиты содержимого паролем.</param>
public sealed record ArchivePreview(
    string FileName,
    string FullPath,
    DateTime CreatedAtUtc,
    string? SchemaVersion,
    long SizeInBytes,
    DateTime? CreatedAtLocal,
    bool IsPasswordProtected);

/// <summary>
/// Результат восстановления базы данных из архива
/// (ТЗ §8.1, функция 104; §8.11, функция 129; §10.5).
/// </summary>
/// <param name="IsSuccess">Признак успешного исхода.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
/// <param name="SchemaVersion">Версия схемы, восстановленная из архива.</param>
/// <param name="SafetyCopyPath">Путь к страховочной копии прежнего состояния базы.</param>
/// <param name="RestartRequired">Признак необходимости перезапуска программы.</param>
public sealed record RestoreOutcome(
    bool IsSuccess,
    string? Error,
    string? SchemaVersion,
    string? SafetyCopyPath,
    bool RestartRequired)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsSuccess;
}

/// <summary>
/// Служба восстановления базы данных из архива
/// (ТЗ §8.1, функция 104; §8.11, функция 129; §10.5).
/// </summary>
/// <remarks>
/// <para>
/// Восстановление выполняется штатной службой архивирования: она проверяет
/// целостность архива и пароль, распаковывает образ во временную папку и
/// устанавливает его на место атомарно — прежняя база переименовывается в
/// страховочную копию с суффиксом <c>.backup</c> (TЗ §8.1, функция 104:
/// «выполняет восстановление атомарно (rename-старой → установка новой)…
/// Перед восстановлением автоматически создается страховочная копия текущего
/// состояния»).
/// </para>
/// <para>
/// Дополнительно служба проверяет совместимость версии схемы архива с версией
/// схемы, известной программе: более новая схема приводит к отказу до
/// изменения текущей базы.
/// </para>
/// </remarks>
public sealed class RestoreService
{
    /// <summary>
    /// Сообщение об отказе при более новой версии схемы архива
    /// (ТЗ §8.1, функция 104: «версия схемы новее программы — отказ»).
    /// </summary>
    public const string СообщениеНовееСхемы = "Версия схемы архива новее версии программы";

    private readonly string _путьБазы;
    private readonly IBackupService _службаАрхива;
    private readonly IReadOnlyList<string> _известныеВерсииСхемы;

    /// <summary>
    /// Создаёт службу восстановления.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <param name="службаАрхива">Служба архивирования и восстановления.</param>
    /// <param name="известныеВерсииСхемы">
    /// Версии схемы, известные программе. При значении <see langword="null"/>
    /// берутся версии встроенных миграций; явно переданный пустой перечень
    /// означает, что программе не известна ни одна версия схемы: архив с любой
    /// версией схемы считается более новым.
    /// </param>
    public RestoreService(
        string путьБазы,
        IBackupService службаАрхива,
        IReadOnlyList<string>? известныеВерсииСхемы = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьБазы);
        ArgumentNullException.ThrowIfNull(службаАрхива);

        _путьБазы = Path.GetFullPath(путьБазы);
        _службаАрхива = службаАрхива;
        _известныеВерсииСхемы = известныеВерсииСхемы ?? ИзвестныеВерсииСхемыПрограммы();
    }

    /// <summary>Путь к файлу базы данных.</summary>
    public string ПутьБазы => _путьБазы;

    /// <summary>
    /// Восстановление всегда завершается предложением перезапуска программы:
    /// открытые соединения к прежней базе должны быть закрыты (ТЗ §10.5).
    /// </summary>
    public bool RestartRequired => true;

    /// <summary>
    /// Возвращает сведения о содержимом архива для предпросмотра
    /// (ТЗ §10.5: «предпросмотр (дата, версия схемы, объем)»).
    /// </summary>
    /// <param name="archivePath">Путь к архиву.</param>
    /// <returns>
    /// Сведения об архиве либо <see langword="null"/>, если архив отсутствует
    /// или не читается.
    /// </returns>
    public ArchivePreview? Preview(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return null;
        }

        var манифест = ПрочитатьМанифест(archivePath);

        if (манифест is null)
        {
            return null;
        }

        var информация = new FileInfo(archivePath);

        return new ArchivePreview(
            информация.Name,
            информация.FullName,
            манифест.CreatedAt,
            манифест.SchemaVersion,
            информация.Length,
            ВЛокальноеВремя(манифест.CreatedAt),
            манифест.Encrypted);
    }

    /// <summary>
    /// Восстанавливает базу данных из архива (ТЗ §8.1, функция 104;
    /// §8.11, функция 129; §10.5).
    /// </summary>
    /// <param name="archivePath">Путь к архиву.</param>
    /// <param name="password">Пароль архива; пустое значение — архив без пароля.</param>
    /// <returns>Результат восстановления.</returns>
    public RestoreOutcome Restore(string archivePath, string? password)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return Неудача("Не указан путь к архиву.");
        }

        if (!File.Exists(archivePath))
        {
            return Неудача($"Файл архива не найден: {archivePath}.");
        }

        var манифест = ПрочитатьМанифест(archivePath);

        if (манифест is null)
        {
            return Неудача("Архив повреждён: манифест не найден.");
        }

        var ошибкаСхемы = ПроверитьВерсиюСхемы(манифест.SchemaVersion);

        if (ошибкаСхемы is not null)
        {
            return Неудача(ошибкаСхемы);
        }

        var результат = _службаАрхива.RestoreFromArchive(archivePath, password ?? string.Empty);

        if (результат.IsFailure)
        {
            return Неудача(результат.Error ?? "Не удалось восстановить базу данных.");
        }

        if (!File.Exists(_путьБазы) || new FileInfo(_путьБазы).Length == 0)
        {
            return Неудача("Восстановленная база данных не создана: файл базы отсутствует или пуст.");
        }

        var страховочная = _путьБазы + BackupService.SafetyCopySuffix;

        return new RestoreOutcome(
            true,
            null,
            результат.SchemaVersion,
            File.Exists(страховочная) ? страховочная : null,
            RestartRequired);
    }

    /// <summary>
    /// Формирует неуспешный результат восстановления.
    /// </summary>
    /// <param name="ошибка">Текст ошибки.</param>
    /// <returns>Результат восстановления.</returns>
    private static RestoreOutcome Неудача(string ошибка)
        => new(false, ошибка, null, null, false);

    /// <summary>
    /// Проверяет, что версия схемы архива не новее известной программе
    /// (ТЗ §8.1, функция 104: «версия схемы новее программы — отказ с
    /// указанием требуемой версии»).
    /// </summary>
    /// <param name="версияСхемы">Версия схемы в архиве.</param>
    /// <returns>Текст ошибки либо <see langword="null"/> при успехе.</returns>
    /// <remarks>
    /// Сравнение числовое: архив со схемой старее известной программе версии
    /// допустим — недостающие миграции применяются после восстановления.
    /// </remarks>
    private string? ПроверитьВерсиюСхемы(string? версияСхемы)
    {
        if (версияСхемы is null)
        {
            return null;
        }

        if (!int.TryParse(версияСхемы, NumberStyles.None, CultureInfo.InvariantCulture, out var номерАрхива))
        {
            return null;
        }

        var требуемая = МаксимальнаяИзвестнаяВерсия();

        if (номерАрхива <= требуемая)
        {
            return null;
        }

        return СообщениеНовееСхемы;
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
    /// Приводит момент времени к локальному времени.
    /// </summary>
    /// <param name="момент">Момент времени.</param>
    /// <returns>Момент времени в локальном времени.</returns>
    private static DateTime? ВЛокальноеВремя(DateTime момент)
    {
        if (момент == default)
        {
            return null;
        }

        return момент.Kind switch
        {
            DateTimeKind.Utc => момент.ToLocalTime(),
            DateTimeKind.Local => момент,
            _ => DateTime.SpecifyKind(момент, DateTimeKind.Utc).ToLocalTime(),
        };
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
            var запись = архив.GetEntry(BackupService.ManifestEntryName);

            if (запись is null)
            {
                return null;
            }

            using var поток = запись.Open();

            return JsonSerializer.Deserialize<BackupManifest>(поток);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
