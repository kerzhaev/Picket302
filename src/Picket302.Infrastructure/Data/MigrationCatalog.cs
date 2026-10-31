using System.Reflection;

namespace Picket302.Infrastructure.Data;

/// <summary>
/// Описание миграции схемы (ТЗ §3.1.18, §3.1.19).
/// </summary>
/// <param name="Version">Номер версии миграции.</param>
/// <param name="Name">Краткое имя миграции из имени файла ресурса.</param>
/// <param name="Description">Описание миграции из заголовка SQL-файла.</param>
/// <param name="Sql">Текст SQL-скрипта миграции.</param>
/// <param name="ResourceName">Имя встроенного ресурса сборки.</param>
/// <param name="RequiresForeignKeysOff">
/// Требуется ли на время выполнения миграции отключить проверку внешних ключей.
/// </param>
public sealed record Migration(
    string Version,
    string Name,
    string Description,
    string Sql,
    string ResourceName,
    bool RequiresForeignKeysOff);

/// <summary>
/// Результат применения миграции.
/// </summary>
/// <param name="Version">Номер версии миграции.</param>
/// <param name="Description">Описание миграции.</param>
/// <param name="AppliedAt">Момент применения (UTC).</param>
public sealed record AppliedMigration(string Version, string Description, DateTime AppliedAt);

/// <summary>
/// Источник встроенных SQL-ресурсов миграций схемы.
/// </summary>
/// <remarks>
/// Миграции поставляются как встроенные ресурсы сборки Infrastructure в формате
/// <c>V&lt;версия&gt;__&lt;описание&gt;.sql</c> (ТЗ §3.1.19).
/// </remarks>
public sealed class MigrationCatalog
{
    /// <summary>Префикс имени встроенного ресурса миграций SQLite.</summary>
    public const string ResourcePrefix = "Picket302.Infrastructure.Data.Migrations.Sqlite.";

    /// <summary>Суффикс имени файла миграции.</summary>
    public const string ResourceSuffix = ".sql";

    /// <summary>Разделитель версии и описания в имени файла миграции.</summary>
    public const string NameSeparator = "__";

    /// <summary>
    /// Директива в заголовке миграции, требующая отключить проверку внешних
    /// ключей на время выполнения скрипта.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Нужна миграциям, пересобирающим таблицу, на которую ссылаются другие
    /// таблицы: <c>PRAGMA foreign_keys</c> не действует внутри транзакции,
    /// поэтому переключение выполняет исполнитель миграций до её открытия и
    /// возвращает проверку обратно после завершения.
    /// </para>
    /// </remarks>
    public const string ForeignKeysOffDirective = "-- Требуется: foreign_keys=OFF";

    /// <summary>
    /// Загружает миграции из встроенных ресурсов сборки.
    /// </summary>
    /// <param name="assembly">Сборка с ресурсами миграций.</param>
    /// <returns>Миграции, упорядоченные по возрастанию версии.</returns>
    public static IReadOnlyList<Migration> Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var миграции = new List<Migration>();

        foreach (var имя in assembly.GetManifestResourceNames())
        {
            if (!имя.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !имя.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var имяФайла = имя[ResourcePrefix.Length..];
            var (версия, краткоеИмя) = РазобратьИмяФайла(имяФайла);

            using var поток = assembly.GetManifestResourceStream(имя)
                ?? throw new InvalidOperationException($"Ресурс миграции не найден: {имя}.");
            using var читатель = new StreamReader(поток);
            var скрипт = читатель.ReadToEnd();

            миграции.Add(new Migration(
                версия,
                краткоеИмя,
                ПрочитатьОписание(скрипт),
                скрипт,
                имя,
                ТребуетсяОтключениеВнешнихКлючей(скрипт)));
        }

        return миграции
            .OrderBy(миграция => int.Parse(миграция.Version, System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(миграция => миграция.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Определяет, требует ли миграция отключения проверки внешних ключей.
    /// </summary>
    /// <param name="скрипт">Текст SQL-скрипта миграции.</param>
    /// <returns><see langword="true"/>, если в заголовке есть директива.</returns>
    private static bool ТребуетсяОтключениеВнешнихКлючей(string скрипт)
        => скрипт.Contains(ForeignKeysOffDirective, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Разбирает имя файла миграции вида <c>V001__описание.sql</c>.
    /// </summary>
    /// <param name="имяФайла">Имя файла ресурса.</param>
    /// <returns>Номер версии и краткое имя миграции.</returns>
    private static (string Версия, string Имя) РазобратьИмяФайла(string имяФайла)
    {
        var безРасширения = имяФайла[..^ResourceSuffix.Length];
        var разделитель = безРасширения.IndexOf(NameSeparator, StringComparison.Ordinal);

        if (разделитель <= 0 || разделитель + NameSeparator.Length >= безРасширения.Length)
        {
            throw new InvalidOperationException(
                $"Имя ресурса миграции не соответствует формату V<версия>__<описание>.sql: {имяФайла}.");
        }

        var префикс = безРасширения[..разделитель];

        if (!префикс.StartsWith('V')
            || !int.TryParse(префикс.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException(
                $"Имя ресурса миграции не содержит номера версии: {имяФайла}.");
        }

        return (префикс[1..], безРасширения[(разделитель + NameSeparator.Length)..]);
    }

    /// <summary>
    /// Извлекает описание миграции из заголовка SQL-скрипта.
    /// </summary>
    /// <param name="скрипт">Текст SQL-скрипта.</param>
    /// <returns>Описание миграции; имя файла, если описание не задано.</returns>
    private static string ПрочитатьОписание(string скрипт)
    {
        foreach (var строка in скрипт.Split('\n'))
        {
            var обрезанная = строка.Trim();

            if (!обрезанная.StartsWith("--", StringComparison.Ordinal))
            {
                if (обрезанная.Length == 0)
                {
                    continue;
                }

                break;
            }

            const string Префикс = "-- Описание:";

            if (обрезанная.StartsWith(Префикс, StringComparison.Ordinal))
            {
                return обрезанная[Префикс.Length..].Trim();
            }
        }

        return string.Empty;
    }
}
