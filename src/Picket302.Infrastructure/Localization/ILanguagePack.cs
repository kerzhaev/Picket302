using Picket302.Infrastructure.Text;

namespace Picket302.Infrastructure.Localization;

/// <summary>
/// Набор локализации: словарь интерфейсных строк либо словарь суммы
/// прописью (ТЗ §8.11, функция 151:
/// <c>public ILanguagePack LoadPack(string file)</c>).
/// </summary>
public interface ILanguagePack
{
    /// <summary>Код языка или валюты.</summary>
    string Code { get; }

    /// <summary>Отображаемое имя набора.</summary>
    string Name { get; }

    /// <summary>Путь к файлу набора.</summary>
    string FilePath { get; }

    /// <summary>Недостающие обязательные ключи; пустой — набор полон.</summary>
    IReadOnlyList<string> MissingKeys { get; }

    /// <summary>Признак полноты набора.</summary>
    bool IsComplete { get; }

    /// <summary>
    /// Возвращает строку по ключу. Отсутствующий ключ возвращается своим
    /// именем (ТЗ §8.11, функция 112).
    /// </summary>
    /// <param name="key">Ключ строки.</param>
    /// <returns>Значение строки либо имя ключа.</returns>
    string GetString(string key);

    /// <summary>
    /// Пытается получить строку по ключу.
    /// </summary>
    /// <param name="key">Ключ строки.</param>
    /// <param name="value">Полученное значение.</param>
    /// <returns><see langword="true"/>, если ключ найден.</returns>
    bool TryGetString(string key, out string value);
}

/// <summary>
/// Набор локализации, загруженный из файла XML словаря интерфейсных строк
/// (ТЗ §8.11, функция 112: файлы <c>lang/&lt;код&gt;.xml</c>).
/// </summary>
/// <remarks>
/// Формат файла:
/// <code>
/// &lt;language code="ru" name="Русский"&gt;
///   &lt;string name="Menu.File"&gt;Файл&lt;/string&gt;
/// &lt;/language&gt;
/// </code>
/// </remarks>
public sealed class XmlLanguagePack : ILanguagePack
{
    private readonly Dictionary<string, string> _строки;

    /// <summary>
    /// Создаёт набор локализации из разобранных строк.
    /// </summary>
    /// <param name="code">Код языка.</param>
    /// <param name="name">Отображаемое имя языка.</param>
    /// <param name="filePath">Путь к файлу словаря.</param>
    /// <param name="строки">Разобранные строки словаря.</param>
    public XmlLanguagePack(
        string code,
        string name,
        string filePath,
        IReadOnlyDictionary<string, string> строки)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(строки);

        Code = code;
        Name = name;
        FilePath = filePath;
        _строки = new Dictionary<string, string>(строки, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public string Code { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string FilePath { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> MissingKeys => Array.Empty<string>();

    /// <inheritdoc />
    public bool IsComplete => true;

    /// <inheritdoc />
    public string GetString(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _строки.TryGetValue(key, out var значение) ? значение : key;
    }

    /// <inheritdoc />
    public bool TryGetString(string key, out string value)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _строки.TryGetValue(key, out value!);
    }
}

/// <summary>
/// Набор локализации, загруженный из файла .lng словаря суммы прописью
/// (ТЗ §8.11, функции 150 и 151: файлы <c>lng/&lt;валюта&gt;.lng</c>).
/// </summary>
/// <remarks>
/// Обязательные ключи словаря проверяются при загрузке: неполный файл
/// отклоняется с перечнем недостающих ключей.
/// </remarks>
public sealed class CurrencyLanguagePack : ILanguagePack
{
    private readonly LngDictionary _словарь;

    /// <summary>
    /// Создаёт набор локализации из словаря суммы прописью.
    /// </summary>
    /// <param name="code">Код валюты.</param>
    /// <param name="name">Отображаемое имя валюты.</param>
    /// <param name="filePath">Путь к файлу словаря.</param>
    /// <param name="словарь">Загруженный словарь.</param>
    public CurrencyLanguagePack(string code, string name, string filePath, LngDictionary словарь)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(словарь);

        Code = code;
        Name = name;
        FilePath = filePath;
        _словарь = словарь;
    }

    /// <inheritdoc />
    public string Code { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string FilePath { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> MissingKeys => _словарь.Validate();

    /// <inheritdoc />
    public bool IsComplete => _словарь.IsComplete;

    /// <inheritdoc />
    public string GetString(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var формы = _словарь.GetForms(key);

        return формы.Count == 0 ? key : string.Join(";", формы);
    }

    /// <inheritdoc />
    public bool TryGetString(string key, out string value)
    {
        ArgumentNullException.ThrowIfNull(key);

        var формы = _словарь.GetForms(key);

        if (формы.Count == 0)
        {
            value = key;
            return false;
        }

        value = string.Join(";", формы);

        return true;
    }
}

/// <summary>
/// Сведения о доступном языке интерфейса (ТЗ §8.11, функция 112: «Страница
/// «Язык» настроек; выпадающий список языков»).
/// </summary>
/// <param name="Code">Код языка.</param>
/// <param name="Name">Отображаемое имя языка.</param>
/// <param name="FilePath">Путь к файлу словаря.</param>
public sealed record LanguageInfo(string Code, string Name, string FilePath);

/// <summary>
/// Сведения о доступной валюте (наборе локализации суммы прописью).
/// </summary>
/// <param name="Code">Код валюты.</param>
/// <param name="Name">Отображаемое имя валюты.</param>
/// <param name="FilePath">Путь к файлу словаря.</param>
/// <param name="IsComplete">Признак полноты словаря.</param>
/// <param name="MissingKeys">Недостающие обязательные ключи.</param>
public sealed record CurrencyInfo(
    string Code,
    string Name,
    string FilePath,
    bool IsComplete,
    IReadOnlyList<string> MissingKeys);

/// <summary>
/// Аргументы события смены языка интерфейса.
/// </summary>
/// <param name="Code">Код нового языка.</param>
/// <param name="Pack">Загруженный набор локализации.</param>
public sealed record LanguageChangedEventArgs(string Code, ILanguagePack Pack);
