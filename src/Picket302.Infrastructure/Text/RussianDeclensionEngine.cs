using System.Text;
using Picket302.Domain.Common;

namespace Picket302.Infrastructure.Text;

/// <summary>
/// Нормализация и склонение русских ФИО и адресов
/// (ТЗ §8.1, функция 6: «Нормализация и склонение русских ФИО и адресов»).
/// </summary>
/// <remarks>
/// <para>
/// Датéльный падеж используется для адресного поля «Кому:», родительный — для
/// «От кого:» (ТЗ §8.1, функция 6).
/// </para>
/// <para>
/// Нераспознанное иностранное имя остаётся в исходном именительном падеже:
/// имя считается русским, если фамилия имеет характерное русское окончание.
/// </para>
/// </remarks>
public sealed class RussianDeclensionEngine
{
    /// <summary>
    /// Буквы, после которых женское окончание «-а» даёт форму «-и»
    /// (Анна → Анны, Ольга → Ольги).
    /// </summary>
    private static readonly char[] БуквыШипящие = ['г', 'к', 'х', 'ж', 'ч', 'ш', 'щ'];

    /// <summary>
    /// Окончания, при которых имя не склоняется: иностранные и нарицательные
    /// фамилии.
    /// </summary>
    private static readonly string[] НесклоняемыеОкончания =
    [
        "о", "е", "э", "и", "ы", "у", "ю", "ко", "ги", "си", "ио", "ых", "их",
    ];

    /// <summary>
    /// Характерные окончания русских фамилий: по ним имя признаётся русским.
    /// </summary>
    private static readonly string[] РусскиеОкончанияФамилий =
    [
        "ов", "ев", "ёв", "ин", "ын", "ский", "цкий", "ый", "ий", "ко", "ых", "их",
        "ова", "ева", "ёва", "ина", "ына", "а", "я", "ь",
    ];

    /// <summary>
    /// Топонимы, склонение которых задано явно: именительный, родительный,
    /// дательный, винительный, творительный и предложный.
    /// </summary>
    private static readonly Dictionary<string, string[]> ИзвестныеТопонимы = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Москва"] = ["Москва", "Москвы", "Москве", "Москву", "Москвой", "Москве"],
        ["Россия"] = ["Россия", "России", "России", "Россию", "Россией", "России"],
        ["Российская Федерация"] =
        [
            "Российская Федерация", "Российской Федерации", "Российской Федерации",
            "Российскую Федерацию", "Российской Федерацией", "Российской Федерации",
        ],
        ["Украина"] = ["Украина", "Украины", "Украине", "Украину", "Украиной", "Украине"],
        ["Беларусь"] = ["Беларусь", "Беларуси", "Беларуси", "Беларусь", "Беларусью", "Беларуси"],
        ["Казахстан"] = ["Казахстан", "Казахстана", "Казахстану", "Казахстан", "Казахстаном", "Казахстане"],
        ["Санкт-Петербург"] =
        [
            "Санкт-Петербург", "Санкт-Петербурга", "Санкт-Петербургу",
            "Санкт-Петербург", "Санкт-Петербургом", "Санкт-Петербурге",
        ],
    };

    /// <summary>
    /// Сокращения, после которых следующее слово — топоним и склоняется.
    /// </summary>
    /// <remarks>
    /// Перечислены только обозначения населённых пунктов и территорий: названия
    /// улиц склонением не затрагиваются.
    /// </remarks>
    private static readonly string[] ПрефиксыТопонимов =
    [
        "г", "город", "с", "пос", "поселок", "посёлок", "село", "дер", "деревня",
        "обл", "область", "край", "рн", "район", "м", "место", "ст", "станица",
    ];

    /// <summary>
    /// Знаки препинания, отбрасываемые при склонении топонима.
    /// </summary>
    private static readonly char[] ЗнакиПрепинания = [',', ';', ':', '.'];

    /// <summary>
    /// Склоняет полное имя (ФИО) в указанный падеж.
    /// </summary>
    /// <param name="fio">Исходное имя: фамилия, имя и отчество.</param>
    /// <param name="gCase">Требуемый падеж.</param>
    /// <returns>Склонённое имя; при нераспознанном имени — исходная строка.</returns>
    public string Declinename(string fio, GrammaticalCase gCase)
    {
        if (string.IsNullOrWhiteSpace(fio))
        {
            return fio ?? string.Empty;
        }

        if (gCase == GrammaticalCase.Nominative)
        {
            return fio;
        }

        var части = fio.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (части.Length == 0)
        {
            return fio;
        }

        var роли = ОпределитьРоли(части);

        if (!ЯвляетсяРусским(части, роли))
        {
            return fio;
        }

        var род = ОпределитьРод(части, роли);
        var результат = new StringBuilder(fio.Length + 16);

        for (var i = 0; i < части.Length; i++)
        {
            if (i > 0)
            {
                результат.Append(' ');
            }

            результат.Append(СклонениеСлова(части[i], род, роли[i], gCase));
        }

        return результат.ToString();
    }

    /// <summary>
    /// Склоняет адресную строку: склоняются топонимы (населённые пункты,
    /// области), остальные элементы адреса сохраняются без изменений.
    /// </summary>
    /// <param name="address">Исходный адрес.</param>
    /// <param name="gCase">Требуемый падеж.</param>
    /// <returns>Склонённый адрес.</returns>
    public string DeclineAddress(string address, GrammaticalCase gCase)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return address ?? string.Empty;
        }

        if (gCase == GrammaticalCase.Nominative)
        {
            return address;
        }

        var слова = address.Split(' ', StringSplitOptions.None);
        var результат = new StringBuilder(address.Length + 16);
        var ждёмТопоним = false;

        for (var i = 0; i < слова.Length; i++)
        {
            var слово = слова[i];

            if (i > 0)
            {
                результат.Append(' ');
            }

            var основа = БезЗнаков(слово);

            if (ждёмТопоним)
            {
                результат.Append(СклонениеТопонима(слово, gCase));
                ждёмТопоним = false;
                continue;
            }

            if (ИзвестныеТопонимы.ContainsKey(основа))
            {
                результат.Append(СклонениеТопонима(слово, gCase));
                continue;
            }

            результат.Append(слово);

            if (ПрефиксыТопонимов.Contains(основа, StringComparer.OrdinalIgnoreCase))
            {
                ждёмТопоним = true;
            }
        }

        return результат.ToString();
    }

    /// <summary>
    /// Определяет, является ли имя русским.
    /// </summary>
    /// <param name="части">Части имени.</param>
    /// <param name="роли">Роли частей имени.</param>
    /// <returns><see langword="true"/>, если имя русское.</returns>
    private static bool ЯвляетсяРусским(string[] части, string[] роли)
    {
        if (части.Any(часть => часть.Any(символ => char.IsLetter(символ) && !ЯвляетсяКириллицей(символ))))
        {
            return false;
        }

        if (части.Length == 1)
        {
            return true;
        }

        var фамилия = части[0];

        return РусскиеОкончанияФамилий.Any(окончание =>
            фамилия.Length >= окончание.Length
            && фамилия.EndsWith(окончание, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Определяет роль каждой части имени: фамилия, имя или отчество.
    /// </summary>
    private static string[] ОпределитьРоли(string[] части)
    {
        var роли = new string[части.Length];
        var последнее = части[^1];

        if (ЯвляетсяОтчеством(последнее) && части.Length >= 3)
        {
            роли[0] = "фамилия";
            роли[1] = "имя";
            роли[2] = "отчество";

            for (var i = 3; i < части.Length; i++)
            {
                роли[i] = "имя";
            }

            return роли;
        }

        if (части.Length >= 2)
        {
            роли[0] = "фамилия";
            роли[1] = "имя";

            for (var i = 2; i < части.Length; i++)
            {
                роли[i] = "имя";
            }

            return роли;
        }

        // Одно слово: фамилия, если это не женское имя.
        роли[0] = части[0].EndsWith('а') || части[0].EndsWith('я') ? "имя" : "фамилия";
        return роли;
    }

    /// <summary>
    /// Определяет род по отчеству либо по форме имени.
    /// </summary>
    private static string ОпределитьРод(string[] части, string[] роли)
    {
        for (var i = части.Length - 1; i >= 0; i--)
        {
            if (роли[i] != "отчество")
            {
                continue;
            }

            var отчество = части[i];

            if (отчество.EndsWith("вич", StringComparison.OrdinalIgnoreCase))
            {
                return "мужской";
            }

            if (отчество.EndsWith("вна", StringComparison.OrdinalIgnoreCase)
                || отчество.EndsWith("ична", StringComparison.OrdinalIgnoreCase)
                || отчество.EndsWith("чна", StringComparison.OrdinalIgnoreCase))
            {
                return "женский";
            }
        }

        for (var i = части.Length - 1; i >= 0; i--)
        {
            if (роли[i] != "имя")
            {
                continue;
            }

            var имя = части[i];

            if (имя.EndsWith('а') || имя.EndsWith('я'))
            {
                return "женский";
            }
        }

        return "мужской";
    }

    /// <summary>
    /// Определяет, является ли слово отчеством.
    /// </summary>
    private static bool ЯвляетсяОтчеством(string слово)
        => слово.EndsWith("вич", StringComparison.OrdinalIgnoreCase)
            || слово.EndsWith("вна", StringComparison.OrdinalIgnoreCase)
            || слово.EndsWith("ична", StringComparison.OrdinalIgnoreCase)
            || слово.EndsWith("чна", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Склоняет одно слово по правилам соответствующей роли.
    /// </summary>
    private static string СклонениеСлова(string слово, string род, string роль, GrammaticalCase падеж)
    {
        if (!СодержитКириллицу(слово) || Несклоняемое(слово))
        {
            return слово;
        }

        if (роль == "отчество")
        {
            if (род == "женский")
            {
                // Женское отчество склоняется заменой окончания: Петровна → Петровне.
                var основа = слово.EndsWith("ична", StringComparison.OrdinalIgnoreCase)
                    ? слово[..^4]
                    : слово.EndsWith("на", StringComparison.OrdinalIgnoreCase)
                        ? слово[..^2]
                        : слово;

                return основа + ЖенскиеОтчества[падеж];
            }

            // Мужское отчество склоняется присоединением окончания к слову целиком:
            // Иванович → Ивановичу.
            return слово + МужскиеОтчества[падеж];
        }

        var последняяБуква = слово[^1];

        if (последняяБуква == 'а' || последняяБуква == 'я')
        {
            var женскаяФамилия = роль == "фамилия"
                && последняяБуква == 'а'
                && (слово.EndsWith("ова", StringComparison.OrdinalIgnoreCase)
                    || слово.EndsWith("ева", StringComparison.OrdinalIgnoreCase)
                    || слово.EndsWith("ёва", StringComparison.OrdinalIgnoreCase)
                    || слово.EndsWith("ина", StringComparison.OrdinalIgnoreCase)
                    || слово.EndsWith("ына", StringComparison.OrdinalIgnoreCase));

            var основа = слово[..^1];

            if (женскаяФамилия)
            {
                return основа + (ПоследняяБукваШипящая(основа)
                    ? ЖенскиеФамилии_аШипящие[падеж]
                    : ЖенскиеФамилии_а[падеж]);
            }

            if (последняяБуква == 'я')
            {
                return основа + ЖенскиеНа_я[падеж];
            }

            return основа + (ПоследняяБукваШипящая(основа)
                ? ЖенскиеНа_аШипящие[падеж]
                : ЖенскиеНа_а[падеж]);
        }

        if (слово.EndsWith("ия", StringComparison.OrdinalIgnoreCase))
        {
            return слово[..^2] + ЖенскиеНа_ия[падеж];
        }

        if (роль == "фамилия"
            && (слово.EndsWith("ый", StringComparison.OrdinalIgnoreCase)
                || слово.EndsWith("ий", StringComparison.OrdinalIgnoreCase)))
        {
            return слово[..^2] + МужскиеНа_ый[падеж];
        }

        if (последняяБуква == 'ь')
        {
            return слово[..^1] + МужскиеНа_ь[падеж];
        }

        if (роль == "имя" && последняяБуква == 'й')
        {
            // Андрей → Андрею, Сергей → Сергею.
            return слово + МужскиеНа_й[падеж];
        }

        if (слово.EndsWith("ин", StringComparison.OrdinalIgnoreCase)
            || слово.EndsWith("ын", StringComparison.OrdinalIgnoreCase)
            || слово.EndsWith("ев", StringComparison.OrdinalIgnoreCase)
            || слово.EndsWith("ов", StringComparison.OrdinalIgnoreCase)
            || слово.EndsWith("ёв", StringComparison.OrdinalIgnoreCase))
        {
            // Иванов → Иванову, Иванова, Ивановым, Иванове.
            return слово + МужскиеНа_ин[падеж];
        }

        return слово + Мужские[падеж];
    }

    /// <summary>
    /// Склоняет топоним: населённый пункт, область, край.
    /// </summary>
    private static string СклонениеТопонима(string слово, GrammaticalCase падеж)
    {
        var знаки = new string(слово.Reverse()
            .TakeWhile(символ => ЗнакиПрепинания.Contains(символ))
            .Reverse()
            .ToArray());

        var основа = знаки.Length > 0 ? слово[..^знаки.Length] : слово;

        if (!СодержитКириллицу(основа)
            || Несклоняемое(основа)
            || !основа.Any(char.IsLetter))
        {
            return слово;
        }

        return СклонениеОсновыТопонима(основа, падеж) + знаки;
    }

    /// <summary>
    /// Склоняет основу топонима.
    /// </summary>
    private static string СклонениеОсновыТопонима(string основа, GrammaticalCase падеж)
    {
        if (ИзвестныеТопонимы.TryGetValue(основа, out var формы))
        {
            return формы[ИндексПадежа(падеж)];
        }

        var слово = основа;

        if (слово.EndsWith('а'))
        {
            return слово[..^1] + ТопонимыНа_а[падеж];
        }

        if (слово.EndsWith('я'))
        {
            return слово[..^1] + ТопонимыНа_я[падеж];
        }

        if (слово.EndsWith('ь'))
        {
            return слово[..^1] + ТопонимыНа_ь[падеж];
        }

        if (слово.EndsWith('о') || слово.EndsWith('е'))
        {
            return слово[..^1] + Мужские[падеж];
        }

        return слово + МужскиеТопонимы[падеж];
    }

    /// <summary>
    /// Возвращает слово без завершающих знаков препинания.
    /// </summary>
    private static string БезЗнаков(string слово)
    {
        var знаки = слово.Reverse()
            .TakeWhile(символ => ЗнакиПрепинания.Contains(символ))
            .Reverse()
            .ToArray();

        return знаки.Length > 0 ? слово[..^знаки.Length] : слово;
    }

    /// <summary>
    /// Возвращает индекс падежа в массиве форм (именительный — 0).
    /// </summary>
    private static int ИндексПадежа(GrammaticalCase падеж) => падеж switch
    {
        GrammaticalCase.Nominative => 0,
        GrammaticalCase.Genitive => 1,
        GrammaticalCase.Dative => 2,
        GrammaticalCase.Accusative => 3,
        GrammaticalCase.Instrumental => 4,
        GrammaticalCase.Prepositional => 5,
        _ => 0,
    };

    /// <summary>
    /// Определяет, содержит ли слово кириллические буквы.
    /// </summary>
    private static bool СодержитКириллицу(string слово)
        => слово.Any(символ => символ is >= 'А' and <= 'я' || символ == 'Ё' || символ == 'ё');

    /// <summary>
    /// Определяет, что последняя буква основы — шипящая.
    /// </summary>
    /// <param name="основа">Основа слова.</param>
    /// <returns><see langword="true"/>, если последняя буква шипящая.</returns>
    private static bool ПоследняяБукваШипящая(string основа)
        => основа.Length > 0 && БуквыШипящие.Contains(основа[^1]);

    /// <summary>
    /// Определяет, что буква не относится к кириллице.
    /// </summary>
    /// <param name="символ">Проверяемый символ.</param>
    /// <returns><see langword="true"/>, если символ не кириллический.</returns>
    private static bool ЯвляетсяКириллицей(char символ)
        => символ is >= 'А' and <= 'я' || символ == 'Ё' || символ == 'ё';

    /// <summary>
    /// Определяет, что слово не склоняется.
    /// </summary>
    private static bool Несклоняемое(string слово)
        => НесклоняемыеОкончания.Any(окончание =>
            слово.Length > окончание.Length
            && слово.EndsWith(окончание, StringComparison.OrdinalIgnoreCase));

    /// <summary>Формы мужского имени на согласную.</summary>
    private static readonly Dictionary<GrammaticalCase, string> Мужские = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "а",
        [GrammaticalCase.Dative] = "у",
        [GrammaticalCase.Accusative] = "а",
        [GrammaticalCase.Instrumental] = "ом",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы фамилии на «-ин», «-ов», «-ев».</summary>
    private static readonly Dictionary<GrammaticalCase, string> МужскиеНа_ин = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "а",
        [GrammaticalCase.Dative] = "у",
        [GrammaticalCase.Accusative] = "а",
        [GrammaticalCase.Instrumental] = "ым",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы фамилии на «-ый», «-ий».</summary>
    private static readonly Dictionary<GrammaticalCase, string> МужскиеНа_ый = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "ого",
        [GrammaticalCase.Dative] = "ому",
        [GrammaticalCase.Accusative] = "ого",
        [GrammaticalCase.Instrumental] = "ым",
        [GrammaticalCase.Prepositional] = "ом",
    };

    /// <summary>Формы имени на мягкий знак.</summary>
    private static readonly Dictionary<GrammaticalCase, string> МужскиеНа_ь = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "я",
        [GrammaticalCase.Dative] = "ю",
        [GrammaticalCase.Accusative] = "я",
        [GrammaticalCase.Instrumental] = "ем",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы имени на «-й» (Андрей → Андрею).</summary>
    private static readonly Dictionary<GrammaticalCase, string> МужскиеНа_й = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "я",
        [GrammaticalCase.Dative] = "ю",
        [GrammaticalCase.Accusative] = "я",
        [GrammaticalCase.Instrumental] = "ем",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы женского имени на «-а».</summary>
    private static readonly Dictionary<GrammaticalCase, string> ЖенскиеНа_а = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "ы",
        [GrammaticalCase.Dative] = "е",
        [GrammaticalCase.Accusative] = "у",
        [GrammaticalCase.Instrumental] = "ой",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы женского имени на «-а» после шипящей.</summary>
    private static readonly Dictionary<GrammaticalCase, string> ЖенскиеНа_аШипящие = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "и",
        [GrammaticalCase.Dative] = "е",
        [GrammaticalCase.Accusative] = "у",
        [GrammaticalCase.Instrumental] = "ей",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы женского имени на «-я».</summary>
    private static readonly Dictionary<GrammaticalCase, string> ЖенскиеНа_я = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "и",
        [GrammaticalCase.Dative] = "е",
        [GrammaticalCase.Accusative] = "ю",
        [GrammaticalCase.Instrumental] = "ей",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы женского имени на «-ия».</summary>
    private static readonly Dictionary<GrammaticalCase, string> ЖенскиеНа_ия = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "и",
        [GrammaticalCase.Dative] = "и",
        [GrammaticalCase.Accusative] = "ю",
        [GrammaticalCase.Instrumental] = "ей",
        [GrammaticalCase.Prepositional] = "и",
    };

    /// <summary>Формы женской фамилии на «-ова», «-ина».</summary>
    private static readonly Dictionary<GrammaticalCase, string> ЖенскиеФамилии_а = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "ой",
        [GrammaticalCase.Dative] = "ой",
        [GrammaticalCase.Accusative] = "ую",
        [GrammaticalCase.Instrumental] = "ой",
        [GrammaticalCase.Prepositional] = "ой",
    };

    /// <summary>Формы женской фамилии на «-ова», «-ина» после шипящей.</summary>
    private static readonly Dictionary<GrammaticalCase, string> ЖенскиеФамилии_аШипящие = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "ей",
        [GrammaticalCase.Dative] = "ей",
        [GrammaticalCase.Accusative] = "юю",
        [GrammaticalCase.Instrumental] = "ей",
        [GrammaticalCase.Prepositional] = "ей",
    };

    /// <summary>Формы мужского отчества на «-вич».</summary>
    private static readonly Dictionary<GrammaticalCase, string> МужскиеОтчества = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "а",
        [GrammaticalCase.Dative] = "у",
        [GrammaticalCase.Accusative] = "а",
        [GrammaticalCase.Instrumental] = "ем",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы женского отчества (основа + окончание: Петров + не).</summary>
    private static readonly Dictionary<GrammaticalCase, string> ЖенскиеОтчества = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "ны",
        [GrammaticalCase.Dative] = "не",
        [GrammaticalCase.Accusative] = "ну",
        [GrammaticalCase.Instrumental] = "ной",
        [GrammaticalCase.Prepositional] = "не",
    };

    /// <summary>Формы топонима на «-а».</summary>
    private static readonly Dictionary<GrammaticalCase, string> ТопонимыНа_а = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "ы",
        [GrammaticalCase.Dative] = "е",
        [GrammaticalCase.Accusative] = "у",
        [GrammaticalCase.Instrumental] = "ой",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы топонима на «-я».</summary>
    private static readonly Dictionary<GrammaticalCase, string> ТопонимыНа_я = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "и",
        [GrammaticalCase.Dative] = "е",
        [GrammaticalCase.Accusative] = "ю",
        [GrammaticalCase.Instrumental] = "ей",
        [GrammaticalCase.Prepositional] = "е",
    };

    /// <summary>Формы топонима на мягкий знак.</summary>
    private static readonly Dictionary<GrammaticalCase, string> ТопонимыНа_ь = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "и",
        [GrammaticalCase.Dative] = "и",
        [GrammaticalCase.Accusative] = "ь",
        [GrammaticalCase.Instrumental] = "ью",
        [GrammaticalCase.Prepositional] = "и",
    };

    /// <summary>Формы топонима на согласную.</summary>
    private static readonly Dictionary<GrammaticalCase, string> МужскиеТопонимы = new()
    {
        [GrammaticalCase.Nominative] = string.Empty,
        [GrammaticalCase.Genitive] = "а",
        [GrammaticalCase.Dative] = "у",
        [GrammaticalCase.Accusative] = "а",
        [GrammaticalCase.Instrumental] = "ом",
        [GrammaticalCase.Prepositional] = "е",
    };
}
