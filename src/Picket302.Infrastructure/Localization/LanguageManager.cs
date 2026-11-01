using System.Text;
using System.Xml.Linq;
using Picket302.Infrastructure.Text;

namespace Picket302.Infrastructure.Localization;

/// <summary>
/// Менеджер локализации: набор языковых файлов и переключение языка без
/// перезапуска программы (ТЗ §8.11, функция 112:
/// <c>public void LoadLanguage(string code); public void SwitchLanguage(string code)</c>;
/// §8.11, функция 151: <c>public ILanguagePack LoadPack(string file)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Локализация — расширяемый набор файлов словарей, а не захардкоженный
/// набор: интерфейсные строки лежат в файлах <c>lang/&lt;код&gt;.xml</c>,
/// словари суммы прописью — в файлах <c>lng/&lt;валюта&gt;.lng</c>.
/// </para>
/// <para>
/// Переключение языка применяется без перезапуска программы: менеджер
/// загружает новый набор и сообщает подписчикам событием
/// <see cref="LanguageChanged"/>, по которому открытые формы перерисовываются.
/// </para>
/// <para>
/// Отсутствующий ключ отображается своим именем и регистрируется в журнале;
/// отсутствующий файл языка приводит к откату на русский с предупреждением.
/// </para>
/// </remarks>
public sealed class LanguageManager
{
    /// <summary>Код русского языка — основной язык поставки.</summary>
    public const string DefaultLanguageCode = "ru";

    /// <summary>Имя каталога файлов интерфейсных строк.</summary>
    public const string LanguageFolderName = "lang";

    /// <summary>Имя каталога файлов словарей суммы прописью.</summary>
    public const string CurrencyFolderName = "lng";

    /// <summary>Расширение файла интерфейсных строк.</summary>
    public const string LanguageFileExtension = ".xml";

    /// <summary>Расширение файла словаря суммы прописью.</summary>
    public const string CurrencyFileExtension = ".lng";

    /// <summary>Имя файла-шаблона нового языка.</summary>
    public const string TemplateLanguageCode = "template";

    private readonly Action<string>? _журнал;
    private ILanguagePack _текущий;

    /// <summary>
    /// Создаёт менеджер локализации для каталогов рядом с программой.
    /// </summary>
    /// <param name="корневаяПапка">
    /// Каталог, в котором размещаются подкаталоги <c>lang</c> и <c>lng</c>;
    /// при <see langword="null"/> берётся каталог исполняемого файла.
    /// </param>
    /// <param name="журнал">Приёмник записей в журнал о проблемах локализации.</param>
    public LanguageManager(string? корневаяПапка = null, Action<string>? журнал = null)
    {
        var корень = корневаяПапка ?? AppContext.BaseDirectory;

        LanguageFolder = Path.Combine(корень, LanguageFolderName);
        CurrencyFolder = Path.Combine(корень, CurrencyFolderName);
        _журнал = журнал;

        _текущий = ЗагрузитьНабор(DefaultLanguageCode) ?? ПустойНабор();
    }

    /// <summary>Каталог файлов интерфейсных строк.</summary>
    public string LanguageFolder { get; }

    /// <summary>Каталог файлов словарей суммы прописью.</summary>
    public string CurrencyFolder { get; }

    /// <summary>Код текущего языка.</summary>
    public string CurrentCode => _текущий.Code;

    /// <summary>Текущий набор локализации.</summary>
    public ILanguagePack Current => _текущий;

    /// <summary>
    /// Событие смены языка: подписчики перерисовывают открытые формы
    /// (ТЗ §8.11, функция 112: переключение без перезапуска).
    /// </summary>
    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    /// <summary>
    /// Загружает язык без смены текущего (ТЗ §8.11, функция 112:
    /// <c>public void LoadLanguage(string code)</c>).
    /// </summary>
    /// <param name="code">Код языка.</param>
    /// <returns>Загруженный набор либо <see langword="null"/>, если язык недоступен.</returns>
    public ILanguagePack? LoadLanguage(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var путь = Path.Combine(LanguageFolder, code + LanguageFileExtension);

        if (!File.Exists(путь))
        {
            ЗаписатьВЖурнал($"Файл языка «{code}» не найден: {путь}. Используется русский язык.");
            return null;
        }

        try
        {
            return ПрочитатьЯзык(путь, code);
        }
        catch (System.Xml.XmlException ошибка)
        {
            ЗаписатьВЖурнал($"Файл языка «{code}» повреждён: {ошибка.Message}. Используется русский язык.");
            return null;
        }
        catch (IOException ошибка)
        {
            ЗаписатьВЖурнал($"Не удалось прочитать файл языка «{code}»: {ошибка.Message}.");
            return null;
        }
    }

    /// <summary>
    /// Переключает язык интерфейса без перезапуска программы (ТЗ §8.11,
    /// функция 112: <c>public void SwitchLanguage(string code)</c>).
    /// </summary>
    /// <param name="code">Код языка.</param>
    /// <returns>Применённый набор локализации.</returns>
    public ILanguagePack SwitchLanguage(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var набор = LoadLanguage(code) ?? ЗагрузитьНабор(DefaultLanguageCode) ?? ПустойНабор();

        _текущий = набор;

        LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(набор.Code, набор));

        return набор;
    }

    /// <summary>
    /// Загружает набор локализации из файла (ТЗ §8.11, функция 151:
    /// <c>public ILanguagePack LoadPack(string file)</c>).
    /// </summary>
    /// <param name="file">Путь к файлу словаря: .xml либо .lng.</param>
    /// <returns>Загруженный набор.</returns>
    /// <exception cref="FileNotFoundException">Файл словаря не найден.</exception>
    /// <exception cref="InvalidDataException">
    /// Словарь суммы прописью неполон: перечень недостающих ключей приведён
    /// в тексте ошибки.
    /// </exception>
    public ILanguagePack LoadPack(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        var путь = Path.GetFullPath(file);

        if (!File.Exists(путь))
        {
            throw new FileNotFoundException($"Файл словаря локализации не найден: {путь}.", путь);
        }

        var расширение = Path.GetExtension(путь);

        if (string.Equals(расширение, CurrencyFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            var словарь = LngDictionary.LoadFile(путь);
            var недостающие = словарь.Validate();

            if (недостающие.Count > 0)
            {
                throw new InvalidDataException(
                    $"Словарь «{Path.GetFileName(путь)}» отклонён: отсутствуют обязательные ключи: "
                    + string.Join(", ", недостающие) + ".");
            }

            var код = Path.GetFileNameWithoutExtension(путь);

            return new CurrencyLanguagePack(код, код, путь, словарь);
        }

        if (string.Equals(расширение, LanguageFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return ПрочитатьЯзык(путь, Path.GetFileNameWithoutExtension(путь));
        }

        throw new InvalidDataException(
            $"Файл словаря «{Path.GetFileName(путь)}» имеет неподдерживаемое расширение. "
            + $"Ожидаются файлы {LanguageFileExtension} и {CurrencyFileExtension}.");
    }

    /// <summary>
    /// Возвращает строку по ключу текущего языка. Отсутствующий ключ
    /// отображается своим именем и регистрируется в журнале (ТЗ §8.11,
    /// функция 112).
    /// </summary>
    /// <param name="key">Ключ строки.</param>
    /// <returns>Значение строки либо имя ключа.</returns>
    public string GetString(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_текущий.TryGetString(key, out var значение))
        {
            return значение;
        }

        ЗаписатьВЖурнал($"Отсутствует ключ локализации «{key}» в языке «{_текущий.Code}».");

        return key;
    }

    /// <summary>
    /// Возвращает перечень доступных языков интерфейса.
    /// </summary>
    /// <returns>Перечень языков.</returns>
    public IReadOnlyList<LanguageInfo> AvailableLanguages()
    {
        if (!Directory.Exists(LanguageFolder))
        {
            return Array.Empty<LanguageInfo>();
        }

        var языки = new List<LanguageInfo>();

        foreach (var путь in Directory.EnumerateFiles(LanguageFolder, "*" + LanguageFileExtension))
        {
            var код = Path.GetFileNameWithoutExtension(путь);

            if (string.Equals(код, TemplateLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var имя = ПрочитатьИмяЯзыка(путь) ?? код;

            языки.Add(new LanguageInfo(код, имя, путь));
        }

        return языки
            .OrderBy(язык => язык.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Возвращает перечень доступных валют — наборов локализации суммы
    /// прописью.
    /// </summary>
    /// <returns>Перечень валют.</returns>
    public IReadOnlyList<CurrencyInfo> AvailableCurrencies()
    {
        if (!Directory.Exists(CurrencyFolder))
        {
            return Array.Empty<CurrencyInfo>();
        }

        var валюты = new List<CurrencyInfo>();

        foreach (var путь in Directory.EnumerateFiles(CurrencyFolder, "*" + CurrencyFileExtension))
        {
            var код = Path.GetFileNameWithoutExtension(путь);

            try
            {
                var набор = (CurrencyLanguagePack)LoadPack(путь);

                валюты.Add(new CurrencyInfo(код, код, путь, набор.IsComplete, набор.MissingKeys));
            }
            catch (InvalidDataException ошибка)
            {
                валюты.Add(new CurrencyInfo(
                    код,
                    код,
                    путь,
                    false,
                    ИзвлечьНедостающиеКлючи(ошибка.Message)));
            }
            catch (FileNotFoundException)
            {
                валюты.Add(new CurrencyInfo(код, код, путь, false, Array.Empty<string>()));
            }
        }

        return валюты
            .OrderBy(валюта => валюта.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Создаёт новый язык копированием файла-шаблона (ТЗ §8.11, функция 112:
    /// «Новый язык добавляется копированием шаблона словаря»).
    /// </summary>
    /// <param name="code">Код нового языка.</param>
    /// <param name="name">Отображаемое имя нового языка.</param>
    /// <returns>Путь к созданному файлу словаря.</returns>
    public string CreateLanguageFromTemplate(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Directory.CreateDirectory(LanguageFolder);

        var путьШаблона = Path.Combine(LanguageFolder, TemplateLanguageCode + LanguageFileExtension);
        var путьНового = Path.Combine(LanguageFolder, code + LanguageFileExtension);

        if (!File.Exists(путьШаблона))
        {
            var содержимое = new XElement(
                "language",
                new XAttribute("code", code),
                new XAttribute("name", name),
                new XElement("string", new XAttribute("name", "Menu.File"), "File"))
                .ToString();

            File.WriteAllText(путьНового, содержимое, new UTF8Encoding(false));

            return путьНового;
        }

        var документ = XDocument.Load(путьШаблона, LoadOptions.PreserveWhitespace);
        var корень = документ.Root ?? new XElement("language");

        корень.SetAttributeValue("code", code);
        корень.SetAttributeValue("name", name);

        документ.Save(путьНового);

        return путьНового;
    }

    /// <summary>
    /// Читает набор языка из файла словаря интерфейсных строк.
    /// </summary>
    private static XmlLanguagePack ПрочитатьЯзык(string путь, string кодПоУмолчанию)
    {
        var документ = XDocument.Load(путь);
        var корень = документ.Root ?? throw new System.Xml.XmlException(
            $"Файл языка «{Path.GetFileName(путь)}» не содержит корневого элемента <language>.");

        var код = корень.Attribute("code")?.Value ?? кодПоУмолчанию;
        var имя = корень.Attribute("name")?.Value ?? код;

        var строки = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var элемент in корень.Elements("string"))
        {
            var ключ = элемент.Attribute("name")?.Value;

            if (string.IsNullOrWhiteSpace(ключ))
            {
                continue;
            }

            строки[ключ] = элемент.Value;
        }

        return new XmlLanguagePack(код, имя, путь, строки);
    }

    /// <summary>
    /// Читает отображаемое имя языка из файла словаря.
    /// </summary>
    private string? ПрочитатьИмяЯзыка(string путь)
    {
        try
        {
            var документ = XDocument.Load(путь);

            return документ.Root?.Attribute("name")?.Value;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Извлекает перечень недостающих ключей из текста ошибки загрузки.
    /// </summary>
    private static IReadOnlyList<string> ИзвлечьНедостающиеКлючи(string сообщение)
    {
        var маркер = "обязательные ключи:";
        var позиция = сообщение.IndexOf(маркер, StringComparison.OrdinalIgnoreCase);

        if (позиция < 0)
        {
            return Array.Empty<string>();
        }

        var хвост = сообщение[(позиция + маркер.Length)..].TrimEnd('.', ' ');

        return хвост.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Загружает набор языка по коду без предупреждений.
    /// </summary>
    private ILanguagePack? ЗагрузитьНабор(string код)
    {
        var путь = Path.Combine(LanguageFolder, код + LanguageFileExtension);

        if (!File.Exists(путь))
        {
            return null;
        }

        try
        {
            return ПрочитатьЯзык(путь, код);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Формирует пустой набор локализации: используется, когда файлы языков
    /// недоступны.
    /// </summary>
    private static ILanguagePack ПустойНабор()
        => new XmlLanguagePack(
            DefaultLanguageCode,
            "Русский",
            string.Empty,
            new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Записывает сообщение о проблеме локализации в журнал.
    /// </summary>
    private void ЗаписатьВЖурнал(string сообщение) => _журнал?.Invoke(сообщение);
}
