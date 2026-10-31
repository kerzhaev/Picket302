using System.Globalization;

namespace Picket302.Infrastructure.Text;

/// <summary>
/// Сумма прописью по словарю .lng (ТЗ §8.11, функция 150:
/// <c>Infrastructure.Text.AmountInWords.ToWords</c>).
/// </summary>
/// <remarks>
/// <para>Числительные по падежам (ключи One/Ten/Twen/Hndr/Thou/Mill/Bill/Thrill),
/// валюта и копейки — в трёх формах (Currency, Cop), род существительного
/// CurrencyMale (1 — мужской; 0 — женский или сокращение: «одна грн»),
/// десятичный разделитель Comma, месяцы и дни недели в падежах
/// (Month1/Month2, DayOfWeek).</para>
/// <para>Склонение — матрица форм по последним цифрам числа и типу
/// существительного. Неполный словарь (отсутствующий ключ) — сумма цифрами
/// с предупреждением.</para>
/// </remarks>
public sealed class AmountInWords
{
    /// <summary>Предупреждение о неполном словаре (ТЗ §8.11, ф.150: «сумма цифрами + предупреждение»).</summary>
    public const string IncompleteDictionaryWarning =
        "Словарь суммы прописью неполон: отсутствуют ключи {0}. Сумма выведена цифрами.";

    private static readonly string[] Разряды = { "Thou", "Mill", "Bill", "Thrill" };

    /// <summary>Предупреждения последнего преобразования (журнал, ТЗ §8.11).</summary>
    public IReadOnlyList<string> Warnings => _предупреждения;

    private readonly List<string> _предупреждения = new();

    /// <summary>
    /// Формирует сумму прописью (ТЗ §8.11, функция 150).
    /// </summary>
    /// <param name="amount">Сумма; копейки — до двух знаков.</param>
    /// <param name="lngFile">Имя файла словаря («Rub», «Byn», «Uah», «usd»).</param>
    /// <returns>Сумма прописью либо сумма цифрами при неполном словаре.</returns>
    public string ToWords(decimal amount, string lngFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lngFile);

        _предупреждения.Clear();

        var словарь = LngDictionary.LoadBuiltin(lngFile);
        var недостающие = словарь.Validate();

        if (недостающие.Count > 0 || !ЕстьВалютаИКопейки(словарь))
        {
            var ключи = недостающие.Count > 0
                ? string.Join(", ", недостающие)
                : "Currency, Cop";

            _предупреждения.Add(string.Format(CultureInfo.InvariantCulture, IncompleteDictionaryWarning, ключи));

            return Цифрами(amount, словарь.DecimalSeparator);
        }

        return Пропись(amount, словарь);
    }

    /// <summary>
    /// Формирует сумму прописью по загруженному словарю.
    /// </summary>
    /// <param name="amount">Сумма.</param>
    /// <param name="dictionary">Словарь.</param>
    /// <returns>Сумма прописью либо цифрами.</returns>
    public string ToWords(decimal amount, LngDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        _предупреждения.Clear();

        var недостающие = dictionary.Validate();

        if (недостающие.Count > 0 || !ЕстьВалютаИКопейки(dictionary))
        {
            var ключи = недостающие.Count > 0
                ? string.Join(", ", недостающие)
                : "Currency, Cop";

            _предупреждения.Add(string.Format(CultureInfo.InvariantCulture, IncompleteDictionaryWarning, ключи));

            return Цифрами(amount, dictionary.DecimalSeparator);
        }

        return Пропись(amount, dictionary);
    }

    /// <summary>
    /// Форматирует дату «день месяца» по словарю: «1 января 2024 г.»
    /// (ключ Month2 — родительный падеж; EVIDENCE: Month2=Января…).
    /// </summary>
    /// <param name="date">Дата.</param>
    /// <param name="lngFile">Имя файла словаря.</param>
    /// <returns>Строка даты.</returns>
    public string FormatDate(DateTime date, string lngFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lngFile);

        var словарь = LngDictionary.LoadBuiltin(lngFile);

        return FormatDate(date, словарь);
    }

    /// <summary>
    /// Форматирует дату «день месяца» по загруженному словарю.
    /// </summary>
    /// <param name="date">Дата.</param>
    /// <param name="dictionary">Словарь.</param>
    /// <returns>Строка «1 января 2024 г.» (название месяца — в нижнем регистре).</returns>
    public string FormatDate(DateTime date, LngDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        var месяц = dictionary.Month(date.Month, genitive: true);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2} г.",
            date.Day,
            месяц.Length == 0 ? месяц : char.ToLowerInvariant(месяц[0]) + месяц[1..],
            date.Year);
    }

    private static bool ЕстьВалютаИКопейки(LngDictionary словарь) =>
        словарь.GetForms("Currency").Count >= 3 && словарь.GetForms("Cop").Count >= 3;

    private string Пропись(decimal сумма, LngDictionary словарь)
    {
        var округленная = Math.Round(сумма, 2, MidpointRounding.ToEven);
        var рубли = (long)Math.Truncate(округленная);
        var копейки = (int)Math.Round((округленная - Math.Truncate(округленная)) * 100m);

        рубли = Math.Abs(рубли);
        копейки = Math.Abs(копейки);

        if (рубли > 999_999_999_999_999L)
        {
            _предупреждения.Add("Сумма превышает диапазон разрядов словаря (триллионы). Сумма выведена цифрами.");

            return Цифрами(сумма, словарь.DecimalSeparator);
        }

        var части = new List<string>();

        // Разряды: триллионы, миллиарды, миллионы, тысячи, единицы.
        var группы = РазбитьНаГруппы(рубли);

        for (var группа = группы.Count - 1; группа >= 0; группа--)
        {
            var значение = группы[группа];

            if (значение == 0)
            {
                continue;
            }

            // Тысячи — женский род («одна тысяча», «две тысячи»); миллионы
            // и выше — мужской; единицы согласуются с валютой (CurrencyMale).
            var мужскойРод = группа == 0
                ? словарь.CurrencyMale
                : группа != 1;

            части.Add(ЧислоСловами(значение, мужскойРод, словарь));

            if (группа > 0)
            {
                части.Add(ФормаСуществительного(Разряды[группа - 1], значение, словарь));
            }
        }

        if (рубли == 0 && части.Count == 0)
        {
            // «нуль рублей»: числительное ноль для единиц.
            части.Add(словарь.Form("One", 11));
        }

        части.Add(ФормаСуществительного("Currency", рубли, словарь));

        // Копейки выводятся цифрами в печатных формах: «00 копеек», «01 коп.».
        части.Add(копейки.ToString("00", CultureInfo.InvariantCulture));
        части.Add(ФормаСуществительного("Cop", копейки, словарь));

        var прописью = string.Join(" ", части.Where(часть => часть.Length > 0));

        return ПрописнаяПервая(прописью);
    }

    private static bool МужскойРодЧисла(bool существительноеМужское, long число)
    {
        // Числительные 1 и 2 согласуются с существительным; остальные — мужской род.
        if (!существительноеМужское)
        {
            var последние = число % 10;

            if (последние == 1 || последние == 2)
            {
                return false;
            }
        }

        return true;
    }

    private static string ЧислоСловами(long число, bool мужскойРодСуществительного, LngDictionary словарь)
    {
        var части = new List<string>();

        var сотни = число / 100;
        var десятки = (число % 100) / 10;
        var единицы = число % 10;
        var подростки = число % 100;

        if (сотни > 0)
        {
            части.Add(словарь.Form("Hndr", (int)сотни - 1));
        }

        if (подростки >= 10 && подростки <= 19)
        {
            части.Add(словарь.Form("Ten", (int)подростки - 10));
        }
        else
        {
            if (десятки > 0)
            {
                части.Add(словарь.Form("Twen", (int)десятки - 2));
            }

            if (единицы > 0)
            {
                части.Add(словарь.Form("One", МужскойРодЧисла(мужскойРодСуществительного, число) ? (int)единицы - 1 : (int)единицы + 8));
            }
        }

        return string.Join(" ", части);
    }

    /// <summary>
    /// Форма существительного по последним цифрам числа
    /// (1 — единственная; 2–4 — множественная I; прочие — множественная II).
    /// </summary>
    private static string ФормаСуществительного(string ключ, long число, LngDictionary словарь)
    {
        var подростки = число % 100;
        var последние = число % 10;

        var индекс = подростки >= 11 && подростки <= 14
            ? 2
            : последние switch
            {
                1 => 0,
                2 or 3 or 4 => 1,
                _ => 2,
            };

        return словарь.Form(ключ, индекс);
    }

    private static string Цифрами(decimal сумма, string разделитель)
    {
        var символ = string.IsNullOrEmpty(разделитель) ? "," : разделитель;

        return сумма.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', символ[0]);
    }

    private static string ПрописнаяПервая(string текст)
    {
        if (текст.Length == 0)
        {
            return текст;
        }

        return char.ToUpperInvariant(текст[0]) + текст[1..];
    }

    private static List<long> РазбитьНаГруппы(long число)
    {
        var группы = new List<long>();

        var остаток = число;

        do
        {
            группы.Add(остаток % 1000);
            остаток /= 1000;
        }
        while (остаток > 0);

        return группы;
    }
}
