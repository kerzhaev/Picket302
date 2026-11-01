using Picket302.Domain.Backup;

namespace Picket302.Infrastructure.Backup;

/// <summary>
/// Режим хранения архивов базы данных (ТЗ §8.12, функция 161; §10.4).
/// </summary>
public enum BackupRetentionMode
{
    /// <summary>
    /// «Хранить данные за каждый день»: копии группируются по дням, копии старше
    /// заданного срока удаляются автоматически.
    /// </summary>
    DailyHistory = 0,

    /// <summary>
    /// «Хранить только последнюю сохранённую базу»: предыдущая копия удаляется
    /// только после успешного создания новой.
    /// </summary>
    LastOnly = 1,
}

/// <summary>
/// Настройка хранения архивов базы данных (ТЗ §8.12, функция 161; §10.4).
/// </summary>
/// <param name="Mode">Режим хранения.</param>
/// <param name="RetentionDays">
/// Срок хранения копий в днях — применяется в режиме «за каждый день».
/// </param>
/// <param name="TargetFolder">
/// Папка хранения архивов: любая папка Windows, включая сетевую и подключённую
/// облачную (отдельной облачной интеграции нет).
/// </param>
/// <param name="Password">Необязательный пароль шифрования архива.</param>
public sealed record BackupPolicy(
    BackupRetentionMode Mode,
    int RetentionDays,
    string TargetFolder,
    string? Password);

/// <summary>
/// Результат применения политики хранения архивов
/// (ТЗ §8.12, функция 161; §10.4).
/// </summary>
/// <param name="IsSuccess">Признак успешного исхода.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
/// <param name="DeletedFiles">Полные пути удалённых файлов архивов.</param>
public sealed record BackupPolicyResult(
    bool IsSuccess,
    string? Error,
    IReadOnlyList<string> DeletedFiles)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsSuccess;
}

/// <summary>
/// Служба очистки архивов базы данных по политике хранения
/// (ТЗ §8.12, функция 161: «Резервное копирование: два режима хранения и очистка»;
/// §10.4: «Хранение: два режима — «за каждый день» … и «только последняя копия»»).
/// </summary>
/// <remarks>
/// <para>
/// Режим «за каждый день» удаляет копии старше заданного срока; режим
/// «только последняя копия» удаляет все копии, кроме вновь созданной.
/// </para>
/// <para>
/// Безопасный отказ: если новый архив не создан, ни одна копия не удаляется —
/// сбой создания копии не приводит к потере предыдущей (ТЗ §8.12, функция 161:
/// «Ошибка создания новой копии в режиме LastOnly не удаляет предыдущую»).
/// </para>
/// </remarks>
public sealed class BackupPolicyService
{
    /// <summary>Название режима «за каждый день».</summary>
    public const string ОписаниеЗаКаждыйДень = "за каждый день";

    /// <summary>Название режима «только последняя копия».</summary>
    public const string ОписаниеТолькоПоследняя = "только последняя копия";

    /// <summary>
    /// Применяет политику хранения к перечню существующих архивов.
    /// </summary>
    /// <param name="policy">Политика хранения.</param>
    /// <param name="существующие">Сведения о существующих архивах.</param>
    /// <param name="новыйАрхив">
    /// Полный путь вновь созданного архива. Значение <see langword="null"/> или
    /// отсутствующий файл означает, что новый архив не создан: очистка не
    /// выполняется (безопасный отказ).
    /// </param>
    /// <returns>Результат применения политики.</returns>
    public BackupPolicyResult Apply(
        BackupPolicy policy,
        IReadOnlyList<BackupArchiveInfo> существующие,
        string? новыйАрхив)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(существующие);

        if (новыйАрхив is null)
        {
            return new BackupPolicyResult(
                false,
                "Новый архив не создан: очистка архивов не выполняется (безопасный отказ).",
                Array.Empty<string>());
        }

        var удалённые = new List<string>();
        var ошибки = new List<string>();

        switch (policy.Mode)
        {
            case BackupRetentionMode.DailyHistory:
                УдалитьПоСроку(policy, существующие, новыйАрхив, удалённые, ошибки);
                break;

            case BackupRetentionMode.LastOnly:
                // Предыдущая копия удаляется только после того, как новый архив
                // действительно создан: иначе сбой оставил бы папку без копий.
                if (!File.Exists(новыйАрхив))
                {
                    return new BackupPolicyResult(
                        false,
                        $"Новый архив не найден: {новыйАрхив}. Очистка архивов не выполняется (безопасный отказ).",
                        Array.Empty<string>());
                }

                УдалитьВсеКромеНового(существующие, новыйАрхив, удалённые, ошибки);
                break;

            default:
                return new BackupPolicyResult(
                    false,
                    $"Неизвестный режим хранения архивов: {policy.Mode}.",
                    Array.Empty<string>());
        }

        return ошибки.Count == 0
            ? new BackupPolicyResult(true, null, удалённые)
            : new BackupPolicyResult(false, string.Join(" ", ошибки), удалённые);
    }

    /// <summary>
    /// Разбирает название режима хранения: русское название («за каждый день»,
    /// «только последняя копия») либо имя элемента перечисления.
    /// </summary>
    /// <param name="значение">Название режима.</param>
    /// <returns>Режим хранения.</returns>
    /// <exception cref="ArgumentException">Название режима не распознано.</exception>
    public static BackupRetentionMode ParseMode(string значение)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(значение);

        var нормализованное = значение.Trim();

        if (string.Equals(нормализованное, ОписаниеЗаКаждыйДень, StringComparison.OrdinalIgnoreCase)
            || string.Equals(нормализованное, nameof(BackupRetentionMode.DailyHistory), StringComparison.OrdinalIgnoreCase))
        {
            return BackupRetentionMode.DailyHistory;
        }

        if (string.Equals(нормализованное, ОписаниеТолькоПоследняя, StringComparison.OrdinalIgnoreCase)
            || string.Equals(нормализованное, nameof(BackupRetentionMode.LastOnly), StringComparison.OrdinalIgnoreCase))
        {
            return BackupRetentionMode.LastOnly;
        }

        throw new ArgumentException(
            $"Неизвестный режим хранения архивов: «{значение}». Ожидается «{ОписаниеЗаКаждыйДень}» "
            + $"или «{ОписаниеТолькоПоследняя}».",
            nameof(значение));
    }

    /// <summary>
    /// Возвращает русское название режима хранения для интерфейса оператора.
    /// </summary>
    /// <param name="mode">Режим хранения.</param>
    /// <returns>Название режима.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Режим не поддерживается.</exception>
    public static string DescribeMode(BackupRetentionMode mode) => mode switch
    {
        BackupRetentionMode.DailyHistory => ОписаниеЗаКаждыйДень,
        BackupRetentionMode.LastOnly => ОписаниеТолькоПоследняя,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Неизвестный режим хранения архивов."),
    };

    /// <summary>
    /// Удаляет копии старше срока хранения (режим «за каждый день»).
    /// </summary>
    /// <param name="policy">Политика хранения.</param>
    /// <param name="существующие">Сведения о существующих архивах.</param>
    /// <param name="новыйАрхив">Путь вновь созданного архива — он не удаляется.</param>
    /// <param name="удалённые">Список удаляемых файлов.</param>
    /// <param name="ошибки">Список текстов ошибок удаления.</param>
    private static void УдалитьПоСроку(
        BackupPolicy policy,
        IReadOnlyList<BackupArchiveInfo> существующие,
        string новыйАрхив,
        ICollection<string> удалённые,
        ICollection<string> ошибки)
    {
        // Срок, не заданный или заданный неположительным числом, очистку по сроку
        // не запускает: копии не удаляются, чтобы сбой настройки не привёл к
        // потере архивов.
        if (policy.RetentionDays <= 0)
        {
            return;
        }

        var граница = DateTime.UtcNow.AddDays(-policy.RetentionDays);

        foreach (var архив in существующие)
        {
            if (Совпадает(архив.FullPath, новыйАрхив))
            {
                continue;
            }

            if (архив.CreatedAt >= граница)
            {
                continue;
            }

            if (УдалитьФайл(архив.FullPath, удалённые, ошибки))
            {
                continue;
            }
        }
    }

    /// <summary>
    /// Удаляет все копии, кроме вновь созданной (режим «только последняя копия»).
    /// </summary>
    /// <param name="существующие">Сведения о существующих архивах.</param>
    /// <param name="новыйАрхив">Путь вновь созданного архива — он не удаляется.</param>
    /// <param name="удалённые">Список удаляемых файлов.</param>
    /// <param name="ошибки">Список текстов ошибок удаления.</param>
    private static void УдалитьВсеКромеНового(
        IReadOnlyList<BackupArchiveInfo> существующие,
        string новыйАрхив,
        ICollection<string> удалённые,
        ICollection<string> ошибки)
    {
        foreach (var архив in существующие)
        {
            if (Совпадает(архив.FullPath, новыйАрхив))
            {
                continue;
            }

            УдалитьФайл(архив.FullPath, удалённые, ошибки);
        }
    }

    /// <summary>
    /// Удаляет файл архива, не прерывая обработку остальных архивов при ошибке.
    /// </summary>
    /// <param name="путь">Путь к файлу архива.</param>
    /// <param name="удалённые">Список удалённых файлов.</param>
    /// <param name="ошибки">Список текстов ошибок удаления.</param>
    /// <returns>Признак успешного удаления.</returns>
    private static bool УдалитьФайл(string путь, ICollection<string> удалённые, ICollection<string> ошибки)
    {
        try
        {
            if (!File.Exists(путь))
            {
                return true;
            }

            File.Delete(путь);
            удалённые.Add(путь);

            return true;
        }
        catch (IOException ошибка)
        {
            ошибки.Add($"Не удалось удалить архив «{путь}»: {ошибка.Message}");

            return false;
        }
        catch (UnauthorizedAccessException ошибка)
        {
            ошибки.Add($"Нет доступа к архиву «{путь}»: {ошибка.Message}");

            return false;
        }
    }

    /// <summary>
    /// Сравнивает пути файлов без учёта регистра символов (файловая система Windows).
    /// </summary>
    /// <param name="первый">Первый путь.</param>
    /// <param name="второй">Второй путь.</param>
    /// <returns>Признак совпадения путей.</returns>
    private static bool Совпадает(string первый, string второй)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(первый),
                Path.GetFullPath(второй),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(первый, второй, StringComparison.OrdinalIgnoreCase);
        }
        catch (NotSupportedException)
        {
            return string.Equals(первый, второй, StringComparison.OrdinalIgnoreCase);
        }
        catch (PathTooLongException)
        {
            return string.Equals(первый, второй, StringComparison.OrdinalIgnoreCase);
        }
    }
}
