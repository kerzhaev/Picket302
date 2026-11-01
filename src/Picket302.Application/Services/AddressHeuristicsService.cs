using System.Text.RegularExpressions;
using Picket302.Application.Import;
using Picket302.Domain.Contracts;

namespace Picket302.Application.Services;

/// <summary>
/// Результат автоматического распознавания адреса при импорте (ТЗ §8.12,
/// функция 148: «Автоматическое распознавание адресов при импорте»).
/// </summary>
/// <param name="Zip">Почтовый индекс, выделенный из строки адреса.</param>
/// <param name="Region">Регион (область, край, республика).</param>
/// <param name="City">Населённый пункт вместе с типом («г. Краснодар»).</param>
/// <param name="Street">Улица без номера дома.</param>
/// <param name="House">Номер дома (без префикса).</param>
/// <param name="Flat">Номер квартиры или офиса (без префикса).</param>
/// <param name="StreetHouseFlat">Улица, дом и квартира одной строкой.</param>
/// <param name="FullName">Распознанные ФИО.</param>
/// <param name="Lines">Шесть адресных строк доменной модели.</param>
/// <param name="NeedsVerification">
/// Признак того, что адрес разобран не полностью и требует проверки оператором.
/// </param>
public sealed record AddressRecognitionResult(
    string Zip,
    string Region,
    string City,
    string Street,
    string House,
    string Flat,
    string StreetHouseFlat,
    string FullName,
    IReadOnlyList<string> Lines,
    bool NeedsVerification)
{
    /// <summary>Пустой результат распознавания.</summary>
    public static AddressRecognitionResult Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty },
        false);
}

/// <summary>
/// Служба автоматического распознавания адресов при импорте (ТЗ §8.12,
/// функция 148).
/// </summary>
/// <remarks>
/// <para>
/// Служба выделяет почтовый index средствами <see cref="AddressParserService"/>,
/// а остальные части адреса — по словарям топонимов и сокращений
/// (<see cref="ToponymDictionary"/>), которые поставляются файлами и
/// расширяются пользователем.
/// </para>
/// <para>
/// Не распознанный фрагмент целиком помещается в первую свободную адресную
/// строку, а результат помечается признаком
/// <see cref="AddressRecognitionResult.NeedsVerification"/>.
/// </para>
/// </remarks>
public sealed partial class AddressHeuristicsService
{
    private static readonly Lazy<ToponymDictionary> СловариПоУмолчанию = new(() => ToponymDictionary.Load());

    private readonly ToponymDictionary _словарь;
    private readonly AddressParserService _разбор;

    /// <summary>
    /// Создаёт службу со встроенным словарём топонимов и словарями
    /// пользовательского каталога.
    /// </summary>
    public AddressHeuristicsService()
        : this(СловариПоУмолчанию.Value, new AddressParserService())
    {
    }

    /// <summary>
    /// Создаёт службу с указанным словарём топонимов.
    /// </summary>
    /// <param name="dictionary">Словарь топонимов и сокращений.</param>
    public AddressHeuristicsService(ToponymDictionary dictionary)
        : this(dictionary, new AddressParserService())
    {
    }

    /// <summary>
    /// Создаёт службу с указанным словарём и разборщиком адресов.
    /// </summary>
    /// <param name="dictionary">Словарь топонимов и сокращений.</param>
    /// <param name="parser">Служба разбора неструктурированной строки адреса.</param>
    public AddressHeuristicsService(ToponymDictionary dictionary, AddressParserService parser)
    {
        _словарь = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _разбор = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// Распознаёт адрес и возвращает его в виде адресных строк доменной модели.
    /// </summary>
    /// <param name="rawAddress">Исходная строка адреса.</param>
    /// <returns>
    /// Разобранный адрес: индекс, строки 1..6 и признак необходимости проверки.
    /// </returns>
    public ParsedAddress Recognize(string rawAddress)
    {
        var результат = RecognizeDetailed(rawAddress);

        return new ParsedAddress(результат.Zip, результат.Lines, результат.NeedsVerification);
    }

    /// <summary>
    /// Распознаёт адрес и возвращает подробный результат: регион, населённый
    /// пункт, улицу, дом, квартиру, индекс и ФИО.
    /// </summary>
    /// <param name="rawAddress">Исходная строка адреса.</param>
    /// <returns>Подробный результат распознавания адреса.</returns>
    public AddressRecognitionResult RecognizeDetailed(string rawAddress)
    {
        var исходный = rawAddress ?? string.Empty;

        if (string.IsNullOrWhiteSpace(исходный))
        {
            return AddressRecognitionResult.Empty with { NeedsVerification = true };
        }

        var разобранный = _разбор.ParseRawString(исходный, extractZip: true);
        var индекс = разобранный.Zip;

        var город = string.Empty;
        var регион = string.Empty;
        var улица = string.Empty;
        var дом = string.Empty;
        var домТекст = string.Empty;
        var квартира = string.Empty;
        var квартираТекст = string.Empty;
        var фио = string.Empty;
        var частиУлицы = new List<string>();
        var неизвестные = new List<string>();

        foreach (var фрагмент in РазделитьНаФрагменты(исходный, индекс))
        {
            var текст = фрагмент.Trim();

            if (текст.Length == 0 || текст == индекс)
            {
                continue;
            }

            if (ПохожеНаФИО(текст))
            {
                фио = текст;
                continue;
            }

            if (ЭтоРегион(текст))
            {
                регион = текст;
                continue;
            }

            if (РазобратьУлицу(текст, out var текстУлицы, out var номерДома, out var текстДома, out var номерКвартиры, out var текстКвартиры))
            {
                if (!string.IsNullOrWhiteSpace(текстУлицы))
                {
                    улица = string.IsNullOrWhiteSpace(улица)
                        ? текстУлицы
                        : $"{улица}, {текстУлицы}";
                }

                if (!string.IsNullOrWhiteSpace(номерДома))
                {
                    дом = номерДома;
                    домТекст = текстДома;
                }

                if (!string.IsNullOrWhiteSpace(номерКвартиры))
                {
                    квартира = номерКвартиры;
                    квартираТекст = текстКвартиры;
                }

                частиУлицы.Add(текст);
                continue;
            }

            if (ЭтоПоселение(текст))
            {
                город = текст;
                continue;
            }

            if (_словарь.IsCity(текст))
            {
                город = текст;
                continue;
            }

            неизвестные.Add(текст);
        }

        var строки = new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty };

        строки[0] = фио;
        строки[1] = улица;
        строки[2] = домТекст;
        строки[3] = квартираТекст;
        строки[4] = город;
        строки[5] = регион;

        foreach (var неизвестный in неизвестные)
        {
            var позиция = Array.FindIndex(строки, string.IsNullOrEmpty);

            if (позиция < 0)
            {
                break;
            }

            строки[позиция] = неизвестный;
        }

        var нужнаПроверка = индекс.Length == 0 || неизвестные.Count > 0;

        return new AddressRecognitionResult(
            индекс,
            регион,
            город,
            улица,
            дом,
            квартира,
            string.Join(", ", частиУлицы),
            фио,
            строки,
            нужнаПроверка);
    }

    /// <summary>
    /// Разбивает исходную строку адреса на фрагменты по запятым, отбрасывая
    /// фрагмент с почтовым индексом.
    /// </summary>
    /// <param name="исходный">Исходная строка адреса.</param>
    /// <param name="индекс">Выделенный почтовый индекс.</param>
    /// <returns>Список фрагментов адреса.</returns>
    private static IEnumerable<string> РазделитьНаФрагменты(string исходный, string индекс)
    {
        foreach (var фрагмент in исходный.Split(','))
        {
            var текст = фрагмент.Trim();

            if (текст.Length == 0)
            {
                continue;
            }

            if (индекс.Length > 0 && текст == индекс)
            {
                continue;
            }

            yield return текст;
        }
    }

    /// <summary>
    /// Определяет, является ли фрагмент регионом.
    /// </summary>
    /// <param name="текст">Фрагмент адреса.</param>
    /// <returns><see langword="true"/>, если фрагмент распознан как регион.</returns>
    private bool ЭтоРегион(string текст)
    {
        if (_словарь.IsRegion(текст))
        {
            return true;
        }

        var слова = текст.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (слова.Length == 0)
        {
            return false;
        }

        var последнее = ToponymDictionary.Normalize(слова[^1]);

        return ПоследнееСловоРегион(последнее);
    }

    /// <summary>
    /// Проверяет, является ли последнее слово фрагмента признаком региона.
    /// </summary>
    /// <param name="слово">Нормализованное слово.</param>
    /// <returns><see langword="true"/>, если слово — признак региона.</returns>
    private static bool ПоследнееСловоРегион(string слово)
        => слово is "обл" or "область" or "край" or "респ" or "республика" or "ао" or "округ";

    /// <summary>
    /// Определяет, является ли фрагмент населённым пунктом с типом
    /// («г. Краснодар», «пгт. Азбука»).
    /// </summary>
    /// <param name="текст">Фрагмент адреса.</param>
    /// <returns><see langword="true"/>, если фрагмент распознан как населённый пункт.</returns>
    private bool ЭтоПоселение(string текст)
    {
        var слова = текст.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (слова.Length < 2)
        {
            return false;
        }

        var тип = ToponymDictionary.Normalize(слова[0]);

        if (!_словарь.IsSettlementType(тип))
        {
            return false;
        }

        var название = string.Join(' ', слова.Skip(1)).Trim();

        return название.Length > 0 && !char.IsDigit(название[0]);
    }

    /// <summary>
    /// Разбирает фрагмент как часть адреса: улицу, дом и квартиру.
    /// </summary>
    /// <param name="текст">Фрагмент адреса.</param>
    /// <param name="улица">Улица без номера дома и квартиры.</param>
    /// <param name="дом">Номер дома.</param>
    /// <param name="домТекст">Дом вместе с префиксом («д. 35»).</param>
    /// <param name="квартира">Номер квартиры либо офиса.</param>
    /// <param name="квартираТекст">Квартира вместе с префиксом («оф. 4»).</param>
    /// <returns><see langword="true"/>, если фрагмент относится к адресу.</returns>
    private bool РазобратьУлицу(
        string текст,
        out string улица,
        out string дом,
        out string домТекст,
        out string квартира,
        out string квартираТекст)
    {
        улица = string.Empty;
        дом = string.Empty;
        домТекст = string.Empty;
        квартира = string.Empty;
        квартираТекст = string.Empty;

        var остаток = текст;

        var совпадениеДома = HouseRegex().Match(остаток);

        if (совпадениеДома.Success)
        {
            дом = совпадениеДома.Groups["номер"].Value;
            домТекст = совпадениеДома.Value.Trim();
            остаток = УдалитьФрагмент(остаток, совпадениеДома);
        }

        var совпадениеКвартиры = FlatRegex().Match(остаток);

        if (совпадениеКвартиры.Success)
        {
            квартира = совпадениеКвартиры.Groups["номер"].Value;
            квартираТекст = совпадениеКвартиры.Value.Trim();
            остаток = УдалитьФрагмент(остаток, совпадениеКвартиры);
        }

        var содержитТипУлицы = текст
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(ToponymDictionary.Normalize)
            .Any(_словарь.IsStreetType);

        if (!содержитТипУлицы && дом.Length == 0 && квартира.Length == 0)
        {
            return false;
        }

        улица = НормализоватьПробелы(остаток).Trim(' ', ',', '-');

        return true;
    }

    /// <summary>
    /// Удаляет найденный фрагмент из текста, сохраняя остальные части.
    /// </summary>
    /// <param name="текст">Исходный текст.</param>
    /// <param name="совпадение">Найденное совпадение.</param>
    /// <returns>Текст без найденного фрагмента.</returns>
    private static string УдалитьФрагмент(string текст, Match совпадение)
    {
        if (!совпадение.Success)
        {
            return текст;
        }

        var начало = совпадение.Index;
        var длина = совпадение.Length;

        return текст[..начало] + " " + текст[(начало + длина)..];
    }

    /// <summary>
    /// Схлопывает повторяющиеся пробелы и убирает пробелы у знаков препинания.
    /// </summary>
    /// <param name="текст">Исходный текст.</param>
    /// <returns>Нормализованный текст.</returns>
    private static string НормализоватьПробелы(string текст)
    {
        var части = текст.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', части);
    }

    /// <summary>
    /// Определяет, является ли фрагмент ФИО: три слова, последнее из которых
    /// выглядит как отчество.
    /// </summary>
    /// <param name="текст">Фрагмент адреса.</param>
    /// <returns><see langword="true"/>, если фрагмент распознан как ФИО.</returns>
    private static bool ПохожеНаФИО(string текст)
    {
        var слова = текст.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (слова.Length != 3)
        {
            return false;
        }

        if (!слова.All(НачинаетсяСЗаглавной))
        {
            return false;
        }

        if (слова[1].Length == 1 && слова[2].Length == 1)
        {
            return true;
        }

        return ПохожеНаОтчество(слова[2]);
    }

    /// <summary>
    /// Проверяет, начинается ли слово с заглавной буквы.
    /// </summary>
    /// <param name="слово">Проверяемое слово.</param>
    /// <returns><see langword="true"/>, если слово начинается с заглавной буквы.</returns>
    private static bool НачинаетсяСЗаглавной(string слово)
        => слово.Length > 0 && char.IsUpper(слово[0]);

    /// <summary>
    /// Проверяет, похоже ли слово на отчество.
    /// </summary>
    /// <param name="слово">Проверяемое слово.</param>
    /// <returns><see langword="true"/>, если слово похоже на отчество.</returns>
    private static bool ПохожеНаОтчество(string слово)
    {
        var нормализованное = ToponymDictionary.Normalize(слово);

        if (нормализованное.Length < 3)
        {
            return false;
        }

        foreach (var окончание in ОкончанияОтчеств)
        {
            if (нормализованное.EndsWith(окончание, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Окончания отчеств, используемые при распознавании ФИО.</summary>
    private static readonly string[] ОкончанияОтчеств =
    [
        "ович",
        "евич",
        "ьич",
        "ич",
        "овна",
        "евна",
        "ична",
        "инична",
        "иична",
        "оглы",
        "кызы",
        "уулу",
    ];

    /// <summary>
    /// Регулярное выражение номера дома: «д. 35», «дом 35», «домовладение 35».
    /// </summary>
    /// <returns>Регулярное выражение.</returns>
    [GeneratedRegex(@"(?<!\w)(?:д|дом|домовладение)\.?\s*(?<номер>\d+[а-яА-Я]?(?:/\d+)?)(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HouseRegex();

    /// <summary>
    /// Регулярное выражение номера квартиры или офиса: «кв. 12», «офис 4».
    /// </summary>
    /// <returns>Регулярное выражение.</returns>
    [GeneratedRegex(@"(?<!\w)(?:кв|квартира|оф|офис|пом|помещение|аб)\.?\s*(?<номер>\d+[а-яА-Я]?)(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlatRegex();
}
