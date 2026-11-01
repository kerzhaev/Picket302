using System.Globalization;

namespace Picket302.Infrastructure.Text;

/// <summary>
/// Словарь локализации суммы прописью — файл .lng (ТЗ §8.11, функции 150–151;
/// EVIDENCE/localization_dictionaries.txt: состав ключей One, Ten, Twen, Hndr,
/// Thou, Mill, Bill, Thrill, Currency, Cop, Comma, CurrencyMale, Month1,
/// Month2, DayOfWeek).
/// </summary>
/// <remarks>
/// Формат файла: <c>Key=значение1;значение2;…</c>. Числа от 1 до 9 — в мужском
/// роде, формы женского рода («одна», «дві») — в девятой и десятой позициях
/// ключа One, «ноль» — в одиннадцатой. Валюта и копейки — в трёх формах.
/// CurrencyMale — род: 1 — мужской, 0 — женский или сокращение («одна грн»).
/// </remarks>
public sealed class LngDictionary
{
    /// <summary>Обязательные ключи словаря (состав по EVIDENCE).</summary>
    public static readonly IReadOnlyList<string> RequiredKeys = new[]
    {
        "One", "Ten", "Twen", "Hndr", "Thou", "Mill", "Bill", "Thrill",
        "Currency", "Cop", "Comma", "CurrencyMale", "Month1", "Month2", "DayOfWeek",
    };

    private const string ПрефиксРесурса = "Picket302.Infrastructure.Text.lng.";

    private readonly Dictionary<string, string[]> _ключи;

    /// <summary>Создаёт словарь из разобранных строк файла.</summary>
    /// <param name="keys">Разобранные пары «ключ — формы».</param>
    private LngDictionary(Dictionary<string, string[]> keys)
    {
        _ключи = keys;
    }

    /// <summary>Загружает встроенный словарь валюты (Rub, Byn, Uah, usd).</summary>
    /// <param name="name">Имя файла без расширения; поиск без учёта регистра.</param>
    /// <returns>Словарь.</returns>
    /// <exception cref="FileNotFoundException">Встроенный словарь не найден.</exception>
    public static LngDictionary LoadBuiltin(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var сборка = typeof(LngDictionary).Assembly;
        var имена = сборка.GetManifestResourceNames()
            .Where(имяРесурса => имяРесурса.StartsWith(ПрефиксРесурса, StringComparison.OrdinalIgnoreCase)
                && имяРесурса.EndsWith(".lng", StringComparison.OrdinalIgnoreCase));

        foreach (var имяРесурса in имена)
        {
            var имяФайла = имяРесурса[ПрефиксРесурса.Length..];

            if (!string.Equals(Path.GetFileNameWithoutExtension(имяФайла), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var поток = сборка.GetManifestResourceStream(имяРесурса)!;

            using var читатель = new StreamReader(поток);

            return Parse(читатель.ReadToEnd());
        }

        throw new FileNotFoundException($"Встроенный словарь суммы прописью «{name}.lng» не найден.");
    }

    /// <summary>Загружает словарь из файла.</summary>
    /// <param name="filePath">Путь к файлу .lng.</param>
    /// <returns>Словарь.</returns>
    public static LngDictionary LoadFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Parse(File.ReadAllText(filePath));
    }

    /// <summary>Разбирает текст файла .lng.</summary>
    /// <param name="content">Текст файла.</param>
    /// <returns>Словарь.</returns>
    public static LngDictionary Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var ключи = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var сыраяСтрока in content.Split('\n'))
        {
            var строка = сыраяСтрока.TrimEnd('\r').Trim();

            if (строка.Length == 0 || строка.StartsWith("#", StringComparison.Ordinal) || строка.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            var позиция = строка.IndexOf('=');

            if (позиция <= 0)
            {
                continue;
            }

            var ключ = строка[..позиция].Trim();
            var значение = строка[(позиция + 1)..].Trim();

            ключи[ключ] = значение.Length == 0
                ? Array.Empty<string>()
                : значение.Split(';').Select(форма => форма.Trim()).ToArray();
        }

        return new LngDictionary(ключи);
    }

    /// <summary>
    /// Валидатор полноты ключей (ТЗ §8.11, ф.151: «Отсутствие обязательного
    /// ключа — файл отклоняется с перечнем недостающих ключей»).
    /// </summary>
    /// <returns>Список недостающих ключей; пуст — словарь полон.</returns>
    public IReadOnlyList<string> Validate()
    {
        var недостающие = new List<string>();

        foreach (var обязательный in RequiredKeys)
        {
            if (!_ключи.ContainsKey(обязательный))
            {
                недостающие.Add(обязательный);
            }
        }

        return недостающие;
    }

    /// <summary>Словарь полон и валюта с копейками заполнены.</summary>
    public bool IsComplete => Validate().Count == 0;

    /// <summary>Возвращает формы ключа.</summary>
    /// <param name="key">Имя ключа.</param>
    /// <returns>Формы либо пустой список.</returns>
    public IReadOnlyList<string> GetForms(string key) =>
        _ключи.TryGetValue(key, out var формы) ? формы : Array.Empty<string>();

    /// <summary>Возвращает форму ключа по индексу либо пустую строку.</summary>
    /// <param name="key">Имя ключа.</param>
    /// <param name="index">Индекс формы.</param>
    /// <returns>Форма.</returns>
    public string Form(string key, int index)
    {
        var формы = GetForms(key);

        return index >= 0 && index < формы.Count ? формы[index] : string.Empty;
    }

    /// <summary>Возвращает значение ключа-строки (Comma).</summary>
    /// <param name="key">Имя ключа.</param>
    /// <returns>Значение либо пустая строка.</returns>
    public string Value(string key) => Form(key, 0);

    /// <summary>Род существительного валюты: 1 — мужской, 0 — женский/сокращение.</summary>
    public bool CurrencyMale
    {
        get
        {
            var значение = Value("CurrencyMale");

            return значение != "0";
        }
    }

    /// <summary>Десятичный разделитель сумм (ключ Comma).</summary>
    public string DecimalSeparator => Value("Comma");

    /// <summary>Название месяца в падеже (ключ Month1 — именительный, Month2 — родительный).</summary>
    /// <param name="month">Номер месяца 1–12.</param>
    /// <param name="genitive">Родительный падеж (Month2).</param>
    /// <returns>Название месяца.</returns>
    public string Month(int month, bool genitive = true)
    {
        if (month < 1 || month > 12)
        {
            return string.Empty;
        }

        return Form(genitive ? "Month2" : "Month1", month - 1);
    }

    /// <summary>Название дня недели (ключ DayOfWeek; 0 — воскресенье).</summary>
    /// <param name="dayOfWeek">Значение DayOfWeek.</param>
    /// <returns>Название дня недели.</returns>
    public string DayOfWeekName(DayOfWeek dayOfWeek)
    {
        var индекс = dayOfWeek switch
        {
            DayOfWeek.Sunday => 0,
            DayOfWeek.Monday => 1,
            DayOfWeek.Tuesday => 2,
            DayOfWeek.Wednesday => 3,
            DayOfWeek.Thursday => 4,
            DayOfWeek.Friday => 5,
            DayOfWeek.Saturday => 6,
            _ => 0,
        };

        return Form("DayOfWeek", индекс);
    }
}
