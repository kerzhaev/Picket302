using System.Reflection;
using System.Text;

namespace Picket302.Application.Import;

/// <summary>
/// Категория словаря топонимов (ТЗ §8.12, функция 148: «Словари топонимов и
/// сокращений (поставляются файлами, расширяемы)»).
/// </summary>
public enum ToponymCategory
{
    /// <summary>Регион: область, край, республика.</summary>
    Region = 0,

    /// <summary>Населённый пункт: город, посёлок, село.</summary>
    City = 1,

    /// <summary>Тип улицы: ул., пр., пер., бул. и их полные формы.</summary>
    Street = 2,

    /// <summary>Тип населённого пункта: г., пгт., с., пос., мкр.</summary>
    Settlement = 3,

    /// <summary>Дом, корпус, строение.</summary>
    House = 4,

    /// <summary>Квартира, офис, помещение.</summary>
    Flat = 5,
}

/// <summary>
/// Словари топонимов и сокращений, применяемые при автоматическом
/// распознавании адресов при импорте (ТЗ §8.12, функция 148).
/// </summary>
/// <remarks>
/// <para>
/// Словари поставляются файлами и расширяемы пользователем. Формат файла —
/// текстовый, UTF-8, по одному понятию в строке; синонимы одного понятия
/// перечисляются через «|». Строка, начинающаяся с «#», является комментарием;
/// секция «[категория]» задаёт категорию понятий для всех следующих строк
/// (категории: <c>region</c>, <c>city</c>, <c>street</c>, <c>settlement</c>,
/// <c>house</c>, <c>flat</c>).
/// </para>
/// <para>
/// Порядок загрузки: встроенные понятия → встроенные ресурсы сборки → файлы
/// рядом с программой → файлы пользовательского каталога
/// (<c>%APPDATA%\Picket302\Dictionaries</c>). Пользовательский каталог имеет
/// наивысший приоритет: его понятия дополняют и заменяют встроенные.
/// </para>
/// </remarks>
public sealed class ToponymDictionary
{
    /// <summary>Имя файла словаря регионов.</summary>
    public const string RegionsFileName = "regions.txt";

    /// <summary>Имя файла словаря населённых пунктов.</summary>
    public const string CitiesFileName = "cities.txt";

    /// <summary>Имя файла словаря типов улиц, домов, квартир и поселений.</summary>
    public const string StreetTypesFileName = "street-types.txt";

    /// <summary>Имя файла словаря типов населённых пунктов.</summary>
    public const string SettlementsFileName = "settlements.txt";

    /// <summary>Имя файла словаря обозначений дома.</summary>
    public const string HouseTypesFileName = "house-types.txt";

    /// <summary>Имя файла словаря обозначений квартиры и офиса.</summary>
    public const string FlatTypesFileName = "flat-types.txt";

    private readonly Dictionary<ToponymCategory, HashSet<string>> _terms;

    private ToponymDictionary(Dictionary<ToponymCategory, HashSet<string>> terms) => _terms = terms;

    /// <summary>
    /// Каталог пользовательских словарей по умолчанию:
    /// <c>%APPDATA%\Picket302\Dictionaries</c>.
    /// </summary>
    public static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Picket302",
        "Dictionaries");

    /// <summary>
    /// Загружает словари: встроенные понятия, встроенные ресурсы сборки, файлы
    /// рядом с программой и файлы пользовательского каталога.
    /// </summary>
    /// <param name="userDirectory">
    /// Каталог пользовательских словарей; при пустом значении используется
    /// <see cref="UserDirectory"/>.
    /// </param>
    /// <returns>Загруженный словарь.</returns>
    public static ToponymDictionary Load(string? userDirectory = null)
    {
        var термины = СоздатьВстроенныйПонятия();

        ДобавитьСодержимое(термины, ПрочитатьВстроенныеРесурсы(RegionsFileName), ToponymCategory.Region);
        ДобавитьСодержимое(термины, ПрочитатьВстроенныеРесурсы(CitiesFileName), ToponymCategory.City);
        ДобавитьСодержимое(термины, ПрочитатьВстроенныеРесурсы(StreetTypesFileName), ToponymCategory.Street);

        foreach (var каталог in КаталогиПрограммы())
        {
            ДобавитьФайлы(термины, каталог);
        }

        ДобавитьФайлы(термины, string.IsNullOrWhiteSpace(userDirectory) ? UserDirectory : userDirectory);

        return new ToponymDictionary(термины);
    }

    /// <summary>
    /// Возвращает понятия заданной категории в нормализованном виде.
    /// </summary>
    /// <param name="category">Категория понятия.</param>
    /// <returns>Множество понятий категории.</returns>
    public IReadOnlyCollection<string> GetTerms(ToponymCategory category)
        => _terms.TryGetValue(category, out var понятия)
            ? понятия
            : (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>
    /// Проверяет, относится ли значение к понятиям заданной категории.
    /// </summary>
    /// <param name="category">Категория понятия.</param>
    /// <param name="value">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если значение найдено в словаре.</returns>
    public bool Contains(ToponymCategory category, string? value)
    {
        var нормализованное = Normalize(value);

        return нормализованное.Length > 0
            && _terms.TryGetValue(category, out var понятия)
            && понятия.Contains(нормализованное);
    }

    /// <summary>Проверяет, является ли значение регионом.</summary>
    /// <param name="value">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если значение найдено в словаре регионов.</returns>
    public bool IsRegion(string? value) => Contains(ToponymCategory.Region, value);

    /// <summary>Проверяет, является ли значение населённым пунктом.</summary>
    /// <param name="value">Проверяемое значение.</param>
    /// <returns><see langword="true"/>, если значение найдено в словаре городов.</returns>
    public bool IsCity(string? value) => Contains(ToponymCategory.City, value);

    /// <summary>Проверяет, является ли слово обозначением типа улицы.</summary>
    /// <param name="value">Проверяемое слово.</param>
    /// <returns><see langword="true"/>, если слово найдено в словаре типов улиц.</returns>
    public bool IsStreetType(string? value) => Contains(ToponymCategory.Street, value);

    /// <summary>Проверяет, является ли слово обозначением населённого пункта.</summary>
    /// <param name="value">Проверяемое слово.</param>
    /// <returns><see langword="true"/>, если слово найдено в словаре поселений.</returns>
    public bool IsSettlementType(string? value) => Contains(ToponymCategory.Settlement, value);

    /// <summary>Проверяет, является ли слово обозначением дома.</summary>
    /// <param name="value">Проверяемое слово.</param>
    /// <returns><see langword="true"/>, если слово найдено в словаре домов.</returns>
    public bool IsHouseType(string? value) => Contains(ToponymCategory.House, value);

    /// <summary>Проверяет, является ли слово обозначением квартиры либо офиса.</summary>
    /// <param name="value">Проверяемое слово.</param>
    /// <returns><see langword="true"/>, если слово найдено в словаре квартир.</returns>
    public bool IsFlatType(string? value) => Contains(ToponymCategory.Flat, value);

    /// <summary>
    /// Нормализует понятие словаря: обрезка краёв, нижний регистр, удаление
    /// точек и запятых, схлопывание пробелов.
    /// </summary>
    /// <param name="value">Исходное значение.</param>
    /// <returns>Нормализованное значение.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var пробел = false;

        foreach (var символ in value.Trim())
        {
            if (символ is '.' or ',' or '"' or '«' or '»' or '„')
            {
                continue;
            }

            if (char.IsWhiteSpace(символ))
            {
                пробел = true;
                continue;
            }

            if (пробел && builder.Length > 0)
            {
                builder.Append(' ');
            }

            пробел = false;
            builder.Append(char.ToLowerInvariant(символ));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Каталоги, в которых ищутся файлы словарей, установленные вместе с программой.
    /// </summary>
    /// <returns>Список каталогов в порядке возрастания приоритета.</returns>
    private static IEnumerable<string> КаталогиПрограммы()
    {
        var базовый = AppContext.BaseDirectory;

        yield return Path.Combine(базовый, "Dictionaries");
        yield return Path.Combine(базовый, "Resources", "Toponyms");
    }

    /// <summary>
    /// Добавляет понятия из файлов каталога. Отсутствующие и нечитаемые файлы
    /// пропускаются: словари не являются обязательными.
    /// </summary>
    /// <param name="термины">Накопитель понятий.</param>
    /// <param name="каталог">Каталог с файлами словарей.</param>
    private static void ДобавитьФайлы(Dictionary<ToponymCategory, HashSet<string>> термины, string каталог)
    {
        if (string.IsNullOrWhiteSpace(каталог) || !Directory.Exists(каталог))
        {
            return;
        }

        ДобавитьСодержимое(термины, ПрочитатьФайл(каталог, RegionsFileName), ToponymCategory.Region);
        ДобавитьСодержимое(термины, ПрочитатьФайл(каталог, CitiesFileName), ToponymCategory.City);
        ДобавитьСодержимое(термины, ПрочитатьФайл(каталог, StreetTypesFileName), ToponymCategory.Street);
        ДобавитьСодержимое(термины, ПрочитатьФайл(каталог, SettlementsFileName), ToponymCategory.Settlement);
        ДобавитьСодержимое(термины, ПрочитатьФайл(каталог, HouseTypesFileName), ToponymCategory.House);
        ДобавитьСодержимое(термины, ПрочитатьФайл(каталог, FlatTypesFileName), ToponymCategory.Flat);
    }

    /// <summary>
    /// Читает файл словаря из каталога.
    /// </summary>
    /// <param name="каталог">Каталог с файлами словарей.</param>
    /// <param name="имяФайла">Имя файла словаря.</param>
    /// <returns>Содержимое файла либо пустая строка.</returns>
    private static string ПрочитатьФайл(string каталог, string имяФайла)
    {
        try
        {
            var путь = Path.Combine(каталог, имяФайла);

            return File.Exists(путь) ? File.ReadAllText(путь, Encoding.UTF8) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Читает словарь из встроенного ресурса сборки, если он туда включён.
    /// </summary>
    /// <param name="имяФайла">Имя файла словаря.</param>
    /// <returns>Содержимое ресурса либо пустая строка.</returns>
    private static string ПрочитатьВстроенныеРесурсы(string имяФайла)
    {
        try
        {
            var сборка = typeof(ToponymDictionary).Assembly;

            foreach (var имя in сборка.GetManifestResourceNames())
            {
                if (!имя.EndsWith(имяФайла, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var поток = сборка.GetManifestResourceStream(имя);

                if (поток is null)
                {
                    continue;
                }

                using var читатель = new StreamReader(поток, Encoding.UTF8);
                return читатель.ReadToEnd();
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// Разбирает содержимое файла словаря и добавляет понятия в накопитель.
    /// </summary>
    /// <param name="термины">Накопитель понятий.</param>
    /// <param name="содержимое">Содержимое файла словаря.</param>
    /// <param name="категорияПоУмолчанию">Категория для строк без секции.</param>
    private static void ДобавитьСодержимое(
        Dictionary<ToponymCategory, HashSet<string>> термины,
        string содержимое,
        ToponymCategory категорияПоУмолчанию)
    {
        if (string.IsNullOrWhiteSpace(содержимое))
        {
            return;
        }

        var категория = категорияПоУмолчанию;

        foreach (var строка in содержимое.Split('\n'))
        {
            var строкаБезПеревода = строка.TrimEnd('\r').Trim();

            if (строкаБезПеревода.Length == 0 || строкаБезПеревода.StartsWith('#'))
            {
                continue;
            }

            if (строкаБезПеревода.StartsWith('[') && строкаБезПеревода.EndsWith(']'))
            {
                категория = РазобратьКатегория(строкаБезПеревода[1..^1], категорияПоУмолчанию);
                continue;
            }

            foreach (var часть in строкаБезПеревода.Split('|'))
            {
                var понятие = Normalize(часть);

                if (понятие.Length == 0)
                {
                    continue;
                }

                if (!термины.TryGetValue(категория, out var множество))
                {
                    множество = new HashSet<string>(StringComparer.Ordinal);
                    термины[категория] = множество;
                }

                множество.Add(понятие);
            }
        }
    }

    /// <summary>
    /// Разбирает имя секции файла словаря.
    /// </summary>
    /// <param name="имя">Имя секции.</param>
    /// <param name="категорияПоУмолчанию">Категория при неизвестном имени секции.</param>
    /// <returns>Категория секции.</returns>
    private static ToponymCategory РазобратьКатегория(string имя, ToponymCategory категорияПоУмолчанию)
        => имя.Trim().ToLowerInvariant() switch
        {
            "region" or "регион" => ToponymCategory.Region,
            "city" or "город" or "населенный пункт" => ToponymCategory.City,
            "street" or "улица" => ToponymCategory.Street,
            "settlement" or "поселение" => ToponymCategory.Settlement,
            "house" or "дом" => ToponymCategory.House,
            "flat" or "квартира" => ToponymCategory.Flat,
            _ => категорияПоУмолчанию,
        };

    /// <summary>
    /// Создаёт словарь из встроенных понятий.
    /// </summary>
    /// <returns>Готовый словарь.</returns>
    private static ToponymDictionary СоздатьВстроенный()
    {
        var термины = СоздатьВстроенныйПонятия();
        return new ToponymDictionary(термины);
    }

    /// <summary>
    /// Формирует набор встроенных понятий: типовые топонимы и сокращения.
    /// </summary>
    /// <returns>Набор понятий по категориям.</returns>
    private static Dictionary<ToponymCategory, HashSet<string>> СоздатьВстроенныйПонятия()
    {
        var термины = new Dictionary<ToponymCategory, HashSet<string>>
        {
            [ToponymCategory.Region] = new(StringComparer.Ordinal),
            [ToponymCategory.City] = new(StringComparer.Ordinal),
            [ToponymCategory.Street] = new(StringComparer.Ordinal),
            [ToponymCategory.Settlement] = new(StringComparer.Ordinal),
            [ToponymCategory.House] = new(StringComparer.Ordinal),
            [ToponymCategory.Flat] = new(StringComparer.Ordinal),
        };

        foreach (var понятие in ВстроенныеРегионы)
        {
            термины[ToponymCategory.Region].Add(НормализоватьПонятие(понятие));
        }

        foreach (var понятие in ВстроенныеГорода)
        {
            термины[ToponymCategory.City].Add(НормализоватьПонятие(понятие));
        }

        foreach (var понятие in ВстроенныеТипыУлиц)
        {
            термины[ToponymCategory.Street].Add(НормализоватьПонятие(понятие));
        }

        foreach (var понятие in ВстроенныеПоселения)
        {
            термины[ToponymCategory.Settlement].Add(НормализоватьПонятие(понятие));
        }

        foreach (var понятие in ВстроенныеДома)
        {
            термины[ToponymCategory.House].Add(НормализоватьПонятие(понятие));
        }

        foreach (var понятие in ВстроенныеКвартиры)
        {
            термины[ToponymCategory.Flat].Add(НормализоватьПонятие(понятие));
        }

        return термины;
    }

    /// <summary>
    /// Нормализует встроенное понятие тем же правилом, что и понятия из файлов.
    /// </summary>
    /// <param name="понятие">Исходное понятие.</param>
    /// <returns>Нормализованное понятие.</returns>
    private static string НормализоватьПонятие(string понятие) => Normalize(понятие);

    /// <summary>Встроенные регионы: области, края и республики.</summary>
    private static readonly string[] ВстроенныеРегионы =
    [
        "Алтайский край",
        "Амурская область",
        "Архангельская область",
        "Астраханская область",
        "Белгородская область",
        "Брянская область",
        "Владимирская область",
        "Волгоградская область",
        "Вологодская область",
        "Воронежская область",
        "Забайкальский край",
        "Ивановская область",
        "Иркутская область",
        "Калининградская область",
        "Калужская область",
        "Камчатский край",
        "Кемеровская область",
        "Кировская область",
        "Костромская область",
        "Краснодарский край",
        "Красноярский край",
        "Курганская область",
        "Ленинградская область",
        "Липецкая область",
        "Магаданская область",
        "Московская область",
        "Мурманская область",
        "Нижегородская область",
        "Новгородская область",
        "Новосибирская область",
        "Омская область",
        "Оренбургская область",
        "Орловская область",
        "Пензенская область",
        "Пермский край",
        "Приморский край",
        "Псковская область",
        "Республика Адыгея",
        "Республика Башкортостан",
        "Республика Бурятия",
        "Республика Дагестан",
        "Республика Карелия",
        "Республика Коми",
        "Республика Крым",
        "Республика Марий Эл",
        "Республика Мордовия",
        "Республика Саха (Якутия)",
        "Республика Татарстан",
        "Республика Удмуртия",
        "Республика Хакасия",
        "Республика Чечня",
        "Республика Чувашия",
        "Ростовская область",
        "Рязанская область",
        "Самарская область",
        "Саратовская область",
        "Сахалинская область",
        "Свердловская область",
        "Смоленская область",
        "Ставропольский край",
        "Тамбовская область",
        "Тверская область",
        "Томская область",
        "Тульская область",
        "Тюменская область",
        "Ульяновская область",
        "Хабаровский край",
        "Челябинская область",
        "Ярославская область",
    ];

    /// <summary>Встроенные населённые пункты.</summary>
    private static readonly string[] ВстроенныеГорода =
    [
        "Архангельск",
        "Астрахань",
        "Барнаул",
        "Белгород",
        "Брянск",
        "Владивосток",
        "Владимир",
        "Волгоград",
        "Вологда",
        "Воронеж",
        "Екатеринбург",
        "Иваново",
        "Ижевск",
        "Иркутск",
        "Казань",
        "Калининград",
        "Калуга",
        "Кемерово",
        "Киров",
        "Кострома",
        "Краснодар",
        "Красноярск",
        "Курск",
        "Липецк",
        "Магнитогорск",
        "Махачкала",
        "Москва",
        "Мурманск",
        "Набережные Челны",
        "Нижний Новгород",
        "Новокузнецк",
        "Новороссийск",
        "Новосибирск",
        "Омск",
        "Орёл",
        "Оренбург",
        "Пенза",
        "Пермь",
        "Псков",
        "Ростов-на-Дону",
        "Рязань",
        "Самара",
        "Санкт-Петербург",
        "Саратов",
        "Смоленск",
        "Сочи",
        "Ставрополь",
        "Тамбов",
        "Тверь",
        "Тольятти",
        "Томск",
        "Тула",
        "Тюмень",
        "Улан-Удэ",
        "Ульяновск",
        "Уфа",
        "Хабаровск",
        "Чебоксары",
        "Челябинск",
        "Ярославль",
    ];

    /// <summary>Встроенные обозначения типов улиц.</summary>
    private static readonly string[] ВстроенныеТипыУлиц =
    [
        "ул",
        "улица",
        "пр",
        "проспект",
        "пр-кт",
        "пер",
        "переулок",
        "б",
        "бул",
        "бульвар",
        "ш",
        "шоссе",
        "наб",
        "набережная",
        "пл",
        "площадь",
        "проезд",
        "туп",
        "тупик",
        "линия",
        "тракт",
        "шос",
        "набережн",
    ];

    /// <summary>Встроенные обозначения населённых пунктов.</summary>
    private static readonly string[] ВстроенныеПоселения =
    [
        "г",
        "город",
        "пгт",
        "пгт",
        "посёлок городского типа",
        "с",
        "село",
        "пос",
        "посёлок",
        "д",
        "деревня",
        "ст",
        "станция",
        "сл",
        "слобода",
        "мкр",
        "микрорайон",
        "мк",
        "х",
        "хутор",
        "п",
        "проселок",
    ];

    /// <summary>Встроенные обозначения дома.</summary>
    private static readonly string[] ВстроенныеДома =
    [
        "д",
        "дом",
        "домовладение",
        "корп",
        "корпус",
        "к",
        "корпус",
        "стр",
        "строение",
        "вл",
        "владение",
    ];

    /// <summary>Встроенные обозначения квартиры и офиса.</summary>
    private static readonly string[] ВстроенныеКвартиры =
    [
        "кв",
        "квартира",
        "оф",
        "офис",
        "пом",
        "помещение",
        "аб",
        "абонентский ящик",
    ];

    /// <summary>
    /// Встроенный словарь: используется, когда файлы словарей недоступны.
    /// </summary>
    /// <remarks>
    /// Свойство объявлено после встроенных понятий: инициализаторы статических
    /// полей выполняются в порядке объявления.
    /// </remarks>
    public static ToponymDictionary Default { get; } = СоздатьВстроенный();
}
