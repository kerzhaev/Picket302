using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Picket302.Domain.Backup;

namespace Picket302.Infrastructure.Backup;

/// <summary>
/// Точка расписания архивирования (ТЗ §8.11, функция 128; §10.4).
/// </summary>
/// <param name="Время">Момент запуска архивирования в сутках.</param>
public sealed record BackupSchedulePoint(TimeSpan Время);

/// <summary>
/// Расписание автоматического архивирования: до трёх точек в сутках
/// (ТЗ §8.11, функция 128; §10.4: «Расписание: до трех точек в сутки»).
/// </summary>
/// <param name="Точки">Моменты запуска архивирования.</param>
public sealed record BackupSchedule(IReadOnlyList<TimeSpan> Точки)
{
    /// <summary>
    /// Максимальное число точек расписания (ТЗ §8.11, функция 128).
    /// </summary>
    public const int МаксимумТочек = 3;
}

/// <summary>
/// Служба автоматического архивирования по расписанию
/// (ТЗ §8.1, функция 103; §8.11, функция 128; §10.4).
/// </summary>
/// <remarks>
/// <para>
/// Расписание — до трёх точек в сутки (по умолчанию 09:00, 13:05, 16:05).
/// В назначенные моменты создаётся архив базы данных, после чего применяется
/// политика хранения: «за каждый день» либо «только последняя копия».
/// </para>
/// <para>
/// Целевая папка — любая папка Windows, включая сетевую и подключённую
/// облачную: отдельной облачной интеграции нет, используется путь как есть.
/// </para>
/// <para>
/// Если целевая папка недоступна, попытка считается неудачной и назначается
/// повтор через 10 минут (ТЗ §8.1, функция 103: «Недоступность целевой папки —
/// повтор через 10 минут»).
/// </para>
/// </remarks>
public sealed class ScheduledBackupService
{
    /// <summary>
    /// Точки расписания по умолчанию: 09:00, 13:05, 16:05
    /// (ТЗ §8.1, функция 103; §8.11, функция 128; §10.4).
    /// </summary>
    public static readonly TimeSpan[] ТочкиПоУмолчанию =
    [
        new(9, 0, 0),
        new(13, 5, 0),
        new(16, 5, 0),
    ];

    /// <summary>
    /// Интервал повтора неудачной попытки архивирования (ТЗ §8.1, функция 103).
    /// </summary>
    public static readonly TimeSpan ИнтервалПовтора = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Допустимое расхождение момента запуска с точкой расписания: архив
    /// создаётся, если точка попала в окно одной минуты.
    /// </summary>
    public static readonly TimeSpan ДопускРасписания = TimeSpan.FromMinutes(1);

    /// <summary>Сообщение о запуске вне точек расписания.</summary>
    public const string СообщениеВнеРасписания = "Время запуска не совпадает с расписанием";

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _путьБазы;
    private readonly IBackupService _службаАрхива;
    private readonly BackupPolicyService _политика = new();
    private readonly string _путьНастроек;

    /// <summary>
    /// Создаёт службу архивирования по расписанию.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <param name="службаАрхива">Служба создания и перечня архивов.</param>
    /// <param name="политика">Политика хранения архивов.</param>
    /// <param name="путьНастроек">Путь к файлу настроек расписания (JSON).</param>
    public ScheduledBackupService(
        string путьБазы,
        IBackupService службаАрхива,
        BackupPolicy политика,
        string путьНастроек)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьБазы);
        ArgumentNullException.ThrowIfNull(службаАрхива);
        ArgumentNullException.ThrowIfNull(политика);
        ArgumentException.ThrowIfNullOrWhiteSpace(путьНастроек);

        _путьБазы = Path.GetFullPath(путьБазы);
        _службаАрхива = службаАрхива;
        Политика = политика;
        _путьНастроек = путьНастроек;

        var настройки = ПрочитатьНастройки();

        Schedule = ПрочитатьРасписание(настройки);
        NextRetryAt = настройки?.СледующийПовтор;
    }

    /// <summary>
    /// Создаёт службу архивирования по расписанию со штатной службой архивов.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <param name="политика">Политика хранения архивов.</param>
    /// <param name="путьНастроек">Путь к файлу настроек расписания (JSON).</param>
    public ScheduledBackupService(
        string путьБазы,
        BackupPolicy политика,
        string путьНастроек)
        : this(путьБазы, new BackupService(путьБазы), политика, путьНастроек)
    {
    }

    /// <summary>Путь к файлу базы данных.</summary>
    public string ПутьБазы => _путьБазы;

    /// <summary>Политика хранения архивов.</summary>
    public BackupPolicy Политика { get; private set; }

    /// <summary>Путь к файлу настроек расписания.</summary>
    public string ПутьНастроек => _путьНастроек;

    /// <summary>
    /// Текущее расписание архивирования (до трёх точек в сутки).
    /// </summary>
    public BackupSchedule Schedule { get; private set; }

    /// <summary>
    /// Точки текущего расписания в виде перечня.
    /// </summary>
    public IReadOnlyList<BackupSchedulePoint> ТочкиРасписания
        => Schedule.Точки.Select(точка => new BackupSchedulePoint(точка)).ToList();

    /// <summary>
    /// Момент следующего повтора неудачной попытки архивирования;
    /// <see langword="null"/> — повтор не назначен.
    /// </summary>
    public DateTime? NextRetryAt { get; private set; }

    /// <summary>
    /// Файлы архивов, удалённые при последнем применении политики хранения.
    /// </summary>
    public IReadOnlyList<string> ПоследниеУдалённыеАрхивы { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Настраивает расписание архивирования и сохраняет его в файл настроек.
    /// </summary>
    /// <param name="schedule">Новое расписание.</param>
    /// <exception cref="ArgumentException">
    /// Расписание пусто или содержит больше трёх точек.
    /// </exception>
    public void ConfigureSchedule(BackupSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var точки = schedule.Точки ?? Array.Empty<TimeSpan>();

        if (точки.Count == 0)
        {
            throw new ArgumentException("Расписание должно содержать хотя бы одну точку.", nameof(schedule));
        }

        if (точки.Count > BackupSchedule.МаксимумТочек)
        {
            throw new ArgumentException(
                $"Расписание не может содержать больше {BackupSchedule.МаксимумТочек} точек.",
                nameof(schedule));
        }

        Schedule = schedule with { Точки = точки.ToList() };

        ЗаписатьНастройки();
    }

    /// <summary>
    /// Настраивает политику хранения архивов.
    /// </summary>
    /// <param name="политика">Новая политика хранения.</param>
    public void ConfigurePolicy(BackupPolicy политика)
    {
        ArgumentNullException.ThrowIfNull(политика);

        Политика = политика;
    }

    /// <summary>
    /// Создаёт архив, если заданный момент попадает в окно одной из точек
    /// расписания (ТЗ §8.1, функция 103).
    /// </summary>
    /// <param name="now">Момент запуска.</param>
    /// <returns>
    /// Результат создания архива либо результат с сообщением
    /// «Время запуска не совпадает с расписанием».
    /// </returns>
    public BackupResult RunScheduledBackup(DateTime now)
    {
        if (!ПопадаетВРасписание(now))
        {
            return BackupResult.Failure(СообщениеВнеРасписания);
        }

        return СоздатьАрхив(now);
    }

    /// <summary>
    /// Создаёт архив немедленно, вне расписания (используется мастером
    /// архивирования, ТЗ §10.4).
    /// </summary>
    /// <returns>Результат создания архива.</returns>
    public BackupResult RunNow() => СоздатьАрхив(DateTime.Now);

    /// <summary>
    /// Определяет, пора ли повторить неудачную попытку архивирования
    /// (ТЗ §8.1, функция 103: «повтор через 10 минут»).
    /// </summary>
    /// <param name="now">Текущий момент.</param>
    /// <param name="lastAttemptUtc">Момент последней неудачной попытки (UTC).</param>
    /// <returns>Признак необходимости повтора.</returns>
    public bool ShouldRetry(DateTime now, DateTime lastAttemptUtc)
        => КUtc(now) - КUtc(lastAttemptUtc) >= ИнтервалПовтора;

    /// <summary>
    /// Назначает повтор неудачной попытки архивирования через 10 минут и
    /// сохраняет состояние повтора в файле настроек.
    /// </summary>
    /// <param name="now">Момент неудачной попытки.</param>
    public void ScheduleRetry(DateTime now)
    {
        NextRetryAt = now.Add(ИнтервалПовтора);

        ЗаписатьНастройки();
    }

    /// <summary>
    /// Отменяет назначенный повтор неудачной попытки.
    /// </summary>
    public void CancelRetry()
    {
        if (NextRetryAt is null)
        {
            return;
        }

        NextRetryAt = null;

        ЗаписатьНастройки();
    }

    /// <summary>
    /// Определяет, попадает ли момент в окно одной из точек расписания.
    /// </summary>
    /// <param name="now">Проверяемый момент.</param>
    /// <returns>Признак попадания в расписание.</returns>
    private bool ПопадаетВРасписание(DateTime now)
    {
        var время = now.TimeOfDay;

        foreach (var точка in Schedule.Точки)
        {
            var разница = время - точка;

            if (разница < TimeSpan.Zero)
            {
                разница = -разница;
            }

            if (разница <= ДопускРасписания)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Создаёт архив базы данных и применяет политику хранения.
    /// </summary>
    /// <param name="now">Момент запуска.</param>
    /// <returns>Результат создания архива.</returns>
    private BackupResult СоздатьАрхив(DateTime now)
    {
        var папка = Политика.TargetFolder;

        if (string.IsNullOrWhiteSpace(папка))
        {
            return BackupResult.Failure("Не указана папка назначения архива.");
        }

        var результат = _службаАрхива.BackupNow(new BackupOptions(папка, Политика.Password));

        if (результат.IsFailure)
        {
            if (ПапкаНедоступна(папка, результат.Error))
            {
                ScheduleRetry(now);
            }

            return результат;
        }

        CancelRetry();

        ПрименитьПолитику(папка, результат.ArchivePath);

        return результат;
    }

    /// <summary>
    /// Применяет политику хранения к архивам целевой папки.
    /// </summary>
    /// <param name="папка">Папка хранения архивов.</param>
    /// <param name="новыйАрхив">Путь вновь созданного архива.</param>
    private void ПрименитьПолитику(string папка, string? новыйАрхив)
    {
        var архивы = _службаАрхива.ListArchives(папка);
        var итог = _политика.Apply(Политика, архивы, новыйАрхив);

        ПоследниеУдалённыеАрхивы = итог.DeletedFiles;
    }

    /// <summary>
    /// Определяет, что папка назначения архива недоступна.
    /// </summary>
    /// <param name="папка">Папка назначения архива.</param>
    /// <param name="ошибка">Текст ошибки создания архива.</param>
    /// <returns>Признак недоступности папки.</returns>
    private static bool ПапкаНедоступна(string папка, string? ошибка)
    {
        if (!Directory.Exists(папка))
        {
            return true;
        }

        return ошибка is not null
            && ошибка.Contains("недоступна", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Читает расписание из файла настроек; при отсутствии файла или повреждённом
    /// содержимом применяются точки по умолчанию.
    /// </summary>
    /// <param name="настройки">Содержимое файла настроек.</param>
    /// <returns>Расписание архивирования.</returns>
    private static BackupSchedule ПрочитатьРасписание(Настройки? настройки)
    {
        if (настройки is null || настройки.Точки.Count == 0)
        {
            return new BackupSchedule(ТочкиПоУмолчанию.ToList());
        }

        var точки = настройки.Точки
            .Select(РазобратьВремя)
            .Where(точка => точка is not null)
            .Select(точка => точка!.Value)
            .Distinct()
            .Take(BackupSchedule.МаксимумТочек)
            .ToList();

        return точки.Count == 0
            ? new BackupSchedule(ТочкиПоУмолчанию.ToList())
            : new BackupSchedule(точки);
    }

    /// <summary>
    /// Читает содержимое файла настроек расписания.
    /// </summary>
    /// <returns>Содержимое настроек либо <see langword="null"/>.</returns>
    private Настройки? ПрочитатьНастройки()
    {
        try
        {
            if (!File.Exists(_путьНастроек))
            {
                return null;
            }

            var текст = File.ReadAllText(_путьНастроек);

            if (string.IsNullOrWhiteSpace(текст))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Настройки>(текст, ПараметрыJson);
        }
        catch (JsonException)
        {
            // Повреждённый файл настроек заменяется настройками по умолчанию.
            return null;
        }
        catch (IOException)
        {
            // Файл настроек недоступен — применяются настройки по умолчанию.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Файл настроек недоступен — применяются настройки по умолчанию.
            return null;
        }
    }

    /// <summary>
    /// Сохраняет расписание и состояние повтора в файл настроек.
    /// </summary>
    private void ЗаписатьНастройки()
    {
        try
        {
            var каталог = Path.GetDirectoryName(_путьНастроек);

            if (!string.IsNullOrEmpty(каталог))
            {
                Directory.CreateDirectory(каталог);
            }

            var настройки = new Настройки
            {
                Точки = Schedule.Точки.Select(ОписатьВремя).ToList(),
                СледующийПовтор = NextRetryAt,
            };

            File.WriteAllText(_путьНастроек, JsonSerializer.Serialize(настройки, ПараметрыJson));
        }
        catch (IOException)
        {
            // Файл настроек недоступен — расписание остаётся действующим в памяти.
        }
        catch (UnauthorizedAccessException)
        {
            // Файл настроек недоступен — расписание остаётся действующим в памяти.
        }
    }

    /// <summary>
    /// Форматирует момент времени для файла настроек.
    /// </summary>
    /// <param name="время">Момент времени.</param>
    /// <returns>Текст момента в формате «ЧЧ:ММ:СС».</returns>
    private static string ОписатьВремя(TimeSpan время)
        => время.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Разбирает момент времени из файла настроек.
    /// </summary>
    /// <param name="текст">Текст момента времени.</param>
    /// <returns>Момент времени либо <see langword="null"/>.</returns>
    private static TimeSpan? РазобратьВремя(string? текст)
    {
        if (string.IsNullOrWhiteSpace(текст))
        {
            return null;
        }

        return TimeSpan.TryParseExact(
            текст.Trim(),
            @"hh\:mm\:ss",
            CultureInfo.InvariantCulture,
            out var время)
                ? время
                : null;
    }

    /// <summary>
    /// Приводит момент времени к UTC.
    /// </summary>
    /// <param name="значение">Момент времени.</param>
    /// <returns>Момент времени в UTC.</returns>
    private static DateTime КUtc(DateTime значение) => значение.Kind switch
    {
        DateTimeKind.Utc => значение,
        DateTimeKind.Local => значение.ToUniversalTime(),
        _ => DateTime.SpecifyKind(значение, DateTimeKind.Utc),
    };

    /// <summary>
    /// Содержимое файла настроек расписания.
    /// </summary>
    private sealed class Настройки
    {
        /// <summary>Точки расписания в формате «ЧЧ:ММ:СС».</summary>
        public List<string> Точки { get; set; } = [];

        /// <summary>Момент следующего повтора неудачной попытки.</summary>
        public DateTime? СледующийПовтор { get; set; }
    }
}
