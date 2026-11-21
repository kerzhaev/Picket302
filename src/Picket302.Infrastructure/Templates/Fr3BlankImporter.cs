using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Picket302.Domain.Serialization;

namespace Picket302.Infrastructure.Templates;

/// <summary>
/// Импорт макета исходной программы (FastReport, файлы .fr3) в нашу модель бланка.
/// </summary>
/// <remarks>
/// <para>
/// Макеты исходной программы — это готовая, проверенная раскладка настоящих
/// бланков: координаты зон адреса, кодового штампа и поворот текста взяты из
/// них, а не выдуманы. Импортёр переносит эту геометрию один в один.
/// </para>
/// <para>
/// Что переносится:
/// </para>
/// <list type="bullet">
/// <item>размер страницы (PaperWidth/PaperHeight, мм);</item>
/// <item>поля: имя — тег из выражения вида «[edt_str_to1]», координаты — с
/// пересчётом из единиц FastReport (1/96 дюйма) в миллиметры с учётом
/// смещения полосы;</item>
/// <item>поворот текста (Rotation) — на конвертах адрес печатается вертикальной
/// колонкой, снизу вверх;</item>
/// <item>гарнитура и кегль;</item>
/// <item>кодовый штамп (объект ZipCode) — как поле типа
/// <see cref="BlankFieldKinds.PostIndex"/>.</item>
/// </list>
/// <para>
/// Файл читается как XML; кодировка определяется по содержимому: макеты
/// исходной программы фактически записаны в Windows-1251, хотя объявляют
/// UTF-8.
/// </para>
/// </remarks>
public sealed class Fr3BlankImporter
{
    /// <summary>Миллиметров в одной единице FastReport (1/96 дюйма).</summary>
    private const decimal МмВЕдинице = 25.4m / 96m;

    /// <summary>
    /// Импортирует макет из файла .fr3.
    /// </summary>
    /// <param name="путь">Путь к файлу .fr3.</param>
    /// <returns>Импортированный бланк.</returns>
    /// <exception cref="FileNotFoundException">Файла нет.</exception>
    /// <exception cref="InvalidDataException">Файл не является макетом FastReport.</exception>
    public static CustomBlank Import(string путь)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путь);

        if (!File.Exists(путь))
        {
            throw new FileNotFoundException("Файл макета не найден.", путь);
        }

        var корень = Разобрать(File.ReadAllBytes(путь));

        var страница = корень.Descendants()
            .FirstOrDefault(элемент => элемент.Name.LocalName == "TfrxReportPage");

        if (страница is null)
        {
            throw new InvalidDataException("Файл не содержит страницы отчёта FastReport.");
        }

        var ширина = Число(страница, "PaperWidth", 210m);
        var высота = Число(страница, "PaperHeight", 297m);

        var бланк = new CustomBlank
        {
            Name = ИмяОтчёта(корень, путь),
            StandardLayout = true,
            Geometry = new BlankGeometry
            {
                WidthMm = ширина,
                HeightMm = высота,
                Orientation = ширина >= высота
                    ? BlankOrientation.Landscape
                    : BlankOrientation.Portrait,
            },
        };

        var страницаБланка = new BlankPage { Number = 1 };
        бланк.Pages.Add(страницаБланка);

        // Полосы в FastReport называются по-разному (TfrxPageHeader,
        // TfrxReportTitle, TfrxDataBand…), поэтому полоса ищется по
        // родителю объекта, а не по имени.
        foreach (var объект in страница.Descendants().Where(элемент =>
            элемент.Name.LocalName is "TfrxMemoView" or "TfrxTextView" or "TfrxZipCodeView"))
        {
            var поле = ПолеОбъекта(объект, СмещениеПолосы(объект));

            if (поле is not null)
            {
                страницаБланка.Fields.Add(поле);
            }
        }

        if (страницаБланка.Fields.Count == 0)
        {
            throw new InvalidDataException("В макете не найдено ни одного поля с привязкой к данным.");
        }

        return бланк;
    }

    /// <summary>
    /// Преобразует объект макета в поле бланка.
    /// </summary>
    private static BlankFieldSpec? ПолеОбъекта(XElement объект, (decimal X, decimal Y) смещение)
    {
        var (смещениеX, смещениеY) = смещение;
        var вид = объект.Name.LocalName;

        var left = Число(объект, "Left", 0m) + смещениеX;
        var top = Число(объект, "Top", 0m) + смещениеY;
        var width = Число(объект, "Width", 0m);
        var height = Число(объект, "Height", 0m);
        var поворот = (int)Число(объект, "Rotation", 0m);

        var гарнитура = объект.Attribute("Font.Name")?.Value ?? "Tahoma";
        var кегль = Число(объект, "Font.Size", 9m);

        if (вид == "TfrxZipCodeView")
        {
            // Кодовый штамп: машиночитаемый индекс получателя.
            return new BlankFieldSpec
            {
                Name = BlankFieldKinds.PostIndex == "post_index" ? "Индекс для машинной обработки" : "Индекс",
                Kind = BlankFieldKinds.PostIndex,
                Bounds = new BlankBounds
                {
                    XMm = left * МмВЕдинице,
                    YMm = top * МмВЕдинице,
                    WidthMm = width * МмВЕдинице,
                    HeightMm = height * МмВЕдинице,
                },
                FontName = "ZIPcode",
                FontSizePt = 8m,
                MinFontSizePt = 6m,
                MaxFontSizePt = 10m,
                Printable = true,
                PostIndex = new BlankPostIndexSpec(),
            };
        }

        if (вид != "TfrxMemoView" && вид != "TfrxTextView")
        {
            return null;
        }

        var текст = объект.Attribute("Text")?.Value ?? string.Empty;
        var тег = ТегВыражения(текст);

        if (тег is null)
        {
            return null;
        }

        return new BlankFieldSpec
        {
            Name = тег,
            Kind = BlankFieldKinds.Text,
            Bounds = new BlankBounds
            {
                XMm = left * МмВЕдинице,
                YMm = top * МмВЕдинице,
                WidthMm = width * МмВЕдинице,
                HeightMm = height * МмВЕдинице,
            },
            FontName = гарнитура,
            FontSizePt = кегль,
            MinFontSizePt = Math.Max(4m, кегль - 3m),
            MaxFontSizePt = кегль + 2m,
            Rotation = поворот,
            WordWrap = true,
            MaxLines = 6,
            Printable = true,
        };
    }

    /// <summary>
    /// Возвращает смещение полосы, в которой лежит объект.
    /// </summary>
    private static (decimal X, decimal Y) СмещениеПолосы(XElement объект)
    {
        for (var родитель = объект.Parent; родитель is not null; родитель = родитель.Parent)
        {
            var имя = родитель.Name.LocalName;

            if (имя.StartsWith("Tfrx", StringComparison.Ordinal)
                && имя != "TfrxReportPage"
                && имя != "TfrxReport")
            {
                return (Число(родитель, "Left", 0m), Число(родитель, "Top", 0m));
            }
        }

        return (0m, 0m);
    }

    /// <summary>
    /// Возвращает тег привязки из выражения вида «[edt_str_to1]».
    /// </summary>
    private static string? ТегВыражения(string текст)
    {
        var начало = текст.IndexOf('[');
        var конец = текст.IndexOf(']');

        if (начало < 0 || конец <= начало)
        {
            return null;
        }

        var тег = текст[начало..(конец + 1)].Trim();

        return тег.Length > 2 ? тег : null;
    }

    private static string ИмяОтчёта(XElement корень, string путь)
    {
        var имя = корень.Attribute("ReportOptions.Name")?.Value;

        if (!string.IsNullOrWhiteSpace(имя))
        {
            return имя;
        }

        return Path.GetFileNameWithoutExtension(путь);
    }

    private static decimal Число(XElement элемент, string имя, decimal поУмолчанию)
    {
        var значение = элемент.Attribute(имя)?.Value;

        if (string.IsNullOrWhiteSpace(значение))
        {
            return поУмолчанию;
        }

        return decimal.TryParse(значение, NumberStyles.Float, CultureInfo.InvariantCulture, out var число)
            ? число
            : поУмолчанию;
    }

    /// <summary>
    /// Разбирает файл макета: сначала UTF-8, при негодном содержимом —
    /// Windows-1251 (так записаны макеты исходной программы).
    /// </summary>
    /// <summary>
    /// Кириллица Windows-1251 для байтов 0xC0–0xFF.
    /// </summary>
    /// <remarks>
    /// Кодовая страница 1251 в .NET не встроена, а макеты исходной программы
    /// записаны именно в ней. Таблица избавляет от зависимости от пакета
    /// System.Text.Encoding.CodePages.
    /// </remarks>
    private const string Кириллица1251 =
        "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ" +
        "абвгдежзийклмнопрстуфхцчшщъыьэюя";

    /// <summary>
    /// Декодирует байты в строку: сначала строгий UTF-8, при негодных байтах —
    /// Windows-1251 (макеты исходной программы записаны в ней, хотя
    /// объявляют UTF-8).
    /// </summary>
    /// <param name="байты">Содержимое файла.</param>
    /// <returns>Текст файла.</returns>
    private static string Декодировать(byte[] байты)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(байты);
        }
        catch (DecoderFallbackException)
        {
            var символы = new char[байты.Length];

            for (var индекс = 0; индекс < байты.Length; индекс++)
            {
                var байт = байты[индекс];
                символы[индекс] = байт >= 0xC0 && байт <= 0xFF
                    ? Кириллица1251[байт - 0xC0]
                    : (char)байт;
            }

            return new string(символы);
        }
    }

    private static XElement Разобрать(byte[] байты)
    {
        var текст = Декодировать(байты);

        try
        {
            return XElement.Parse(текст);
        }
        catch (System.Xml.XmlException ошибка)
        {
            throw new InvalidDataException(
                "Файл не является макетом FastReport: " + ошибка.Message,
                ошибка);
        }
    }
}
