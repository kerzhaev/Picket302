using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Результат проверки целостности собственных сборок программы
/// (ТЗ §8.10, функция 125: «Дополнительно реализуется только проверка
/// целостности собственных сборок (хеш при старте)»).
/// </summary>
/// <param name="IsIntact">
/// <see langword="true"/> — нарушений не обнаружено.
/// </param>
/// <param name="Нарушения">Перечень обнаруженных нарушений.</param>
/// <param name="Сообщение">Пояснение для оператора.</param>
public sealed record IntegrityCheckResult(
    bool IsIntact,
    IReadOnlyList<string> Нарушения,
    string? Сообщение)
{
    /// <summary>
    /// Создаёт результат без нарушений.
    /// </summary>
    /// <param name="сообщение">Пояснение для оператора.</param>
    /// <returns>Результат проверки.</returns>
    public static IntegrityCheckResult БезНарушений(string? сообщение = null)
        => new(true, [], сообщение);

    /// <summary>
    /// Создаёт результат с нарушениями.
    /// </summary>
    /// <param name="нарушения">Перечень нарушений.</param>
    /// <param name="сообщение">Пояснение для оператора.</param>
    /// <returns>Результат проверки.</returns>
    public static IntegrityCheckResult СНарушениями(IReadOnlyList<string> нарушения, string сообщение)
        => new(false, нарушения, сообщение);
}

/// <summary>
/// Проверка целостности собственных сборок программы при запуске
/// (ТЗ §8.10, функция 125).
/// </summary>
/// <remarks>
/// <para>
/// Контрольные суммы SHA-256 сверяются с файлом манифеста, который создаётся
/// при первом запуске рядом с программой. Нарушение целостности не блокирует
/// работу: выдаётся предупреждение с предложением переустановить программу
/// (ТЗ §8.10, функция 125: «Нарушение целостности сборки — предупреждение с
/// предложением переустановки»).
/// </para>
/// <para>
/// Проверка не является средством самозащиты процесса: она не обращается к
/// портам ввода-вывода и не перехватывает отладчик (ТЗ §8.10, функция 125;
/// §10.7 — явный запрет агрессивной самозащиты процесса).
/// </para>
/// </remarks>
public sealed class AssemblyIntegrityChecker
{
    /// <summary>Имя файла манифеста целостности.</summary>
    public const string ИмяФайлаМанифеста = "integrity-manifest.json";

    private readonly string _путьМанифеста;

    /// <summary>
    /// Создаёт проверяльщик целостности сборок.
    /// </summary>
    /// <param name="путьМанифеста">
    /// Путь к файлу манифеста; по умолчанию — файл рядом с исполняемым файлом
    /// программы.
    /// </param>
    public AssemblyIntegrityChecker(string? путьМанифеста = null)
    {
        _путьМанифеста = путьМанифеста
            ?? Path.Combine(AppContext.BaseDirectory, ИмяФайлаМанифеста);
    }

    /// <summary>
    /// Путь к файлу манифеста целостности.
    /// </summary>
    public string ПутьМанифеста => _путьМанифеста;

    /// <summary>
    /// Проверяет целостность указанных сборок.
    /// </summary>
    /// <param name="assemblyPaths">Пути к проверяемым файлам сборок.</param>
    /// <returns>
    /// Результат проверки: при нарушении — перечень расхождений и
    /// предупреждение с предложением переустановить программу.
    /// </returns>
    /// <exception cref="ArgumentNullException">Перечень путей не задан.</exception>
    public IntegrityCheckResult Check(IEnumerable<string> assemblyPaths)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);

        var пути = assemblyPaths
            .Where(путь => !string.IsNullOrWhiteSpace(путь))
            .Select(путь => Path.GetFullPath(путь))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!System.IO.File.Exists(_путьМанифеста))
        {
            var значения = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var путь in пути)
            {
                if (System.IO.File.Exists(путь))
                {
                    значения[Path.GetFileName(путь)] = ВычислитьСумму(путь);
                }
            }

            ЗаписатьМанифест(значения);

            return IntegrityCheckResult.БезНарушений(
                "Файл манифеста целостности создан: контрольные суммы сборок зафиксированы.");
        }

        Dictionary<string, string>? манифест;

        try
        {
            манифест = JsonSerializer.Deserialize<Dictionary<string, string>>(
                System.IO.File.ReadAllText(_путьМанифеста, Encoding.UTF8));
        }
        catch (Exception исключение) when (исключение is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return IntegrityCheckResult.СНарушениями(
                ["Файл манифеста целостности недоступен: " + исключение.Message],
                "Не удалось проверить целостность сборок. Рекомендуется переустановить программу.");
        }

        if (манифест is null)
        {
            return IntegrityCheckResult.СНарушениями(
                ["Файл манифеста целостности повреждён."],
                "Не удалось проверить целостность сборок. Рекомендуется переустановить программу.");
        }

        var нарушения = new List<string>();

        foreach (var путь in пути)
        {
            var имя = Path.GetFileName(путь);

            if (!манифест.TryGetValue(имя, out var ожидаемая))
            {
                continue;
            }

            if (!System.IO.File.Exists(путь))
            {
                нарушения.Add(имя + ": файл сборки не найден.");
                continue;
            }

            var фактическая = ВычислитьСумму(путь);

            if (!string.Equals(ожидаемая, фактическая, StringComparison.OrdinalIgnoreCase))
            {
                нарушения.Add(
                    имя + ": ожидаемая сумма " + ожидаемая + ", фактическая " + фактическая + ".");
            }
        }

        if (нарушения.Count == 0)
        {
            return IntegrityCheckResult.БезНарушений("Целостность сборок не нарушена.");
        }

        return IntegrityCheckResult.СНарушениями(
            нарушения,
            "Обнаружено нарушение целостности сборки программы. Рекомендуется переустановить программу.");
    }

    /// <summary>
    /// Вычисляет контрольную сумму файла (SHA-256, шестнадцатеричная строка).
    /// </summary>
    /// <param name="путь">Путь к файлу.</param>
    /// <returns>Контрольная сумма файла.</returns>
    private static string ВычислитьСумму(string путь)
    {
        using var поток = File.OpenRead(путь);

        return Convert.ToHexString(SHA256.HashData(поток)).ToLowerInvariant();
    }

    /// <summary>
    /// Записывает файл манифеста целостности.
    /// </summary>
    /// <param name="значения">Контрольные суммы сборок.</param>
    private void ЗаписатьМанифест(Dictionary<string, string> значения)
    {
        var каталог = Path.GetDirectoryName(_путьМанифеста);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        System.IO.File.WriteAllText(
            _путьМанифеста,
            JsonSerializer.Serialize(значения, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
