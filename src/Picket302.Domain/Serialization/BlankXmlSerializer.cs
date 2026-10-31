using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Picket302.Domain.Serialization;

/// <summary>
/// Ошибка структуры XML бланка с указанием позиции (ТЗ §8.3, функция 39:
/// «Несоответствие XSD вызывает отказ загрузки с указанием строки ошибки»).
/// </summary>
public sealed class BlankXmlException : Exception
{
    /// <summary>Создаёт ошибку разбора XML бланка.</summary>
    /// <param name="message">Текст ошибки.</param>
    /// <param name="lineNumber">Номер строки (1 — первая строка; 0 — позиция неизвестна).</param>
    /// <param name="linePosition">Позиция в строке.</param>
    /// <param name="inner">Вложенное исключение.</param>
    public BlankXmlException(string message, int lineNumber = 0, int linePosition = 0, Exception? inner = null)
        : base(message, inner)
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>Номер строки с ошибкой.</summary>
    public int LineNumber { get; }

    /// <summary>Позиция в строке с ошибкой.</summary>
    public int LinePosition { get; }
}

/// <summary>
/// Сериализатор векторного координатного дерева бланка (ТЗ §8.3, функция 39:
/// <c>Domain.Serialization.BlankXmlSerializer</c>).
/// </summary>
/// <remarks>
/// Загрузка строго валидируется против встроенной XSD-схемы
/// <c>Domain/Serialization/BlankSchema.xsd</c>; несоответствие вызывает
/// <see cref="BlankXmlException"/> с номером строки.
/// </remarks>
public sealed class BlankXmlSerializer
{
    /// <summary>Имя встроенного ресурса со схемой.</summary>
    public const string SchemaResourceName = "Picket302.Domain.Serialization.BlankSchema.xsd";

    private readonly XmlSchemaSet _схемы;

    /// <summary>
    /// Создаёт сериализатор и загружает встроенную XSD-схему.
    /// </summary>
    public BlankXmlSerializer()
    {
        _схемы = new XmlSchemaSet();
        _схемы.Add(null, XmlReader.Create(ОткрытьСхему()));
    }

    /// <summary>
    /// Сериализует бланк в XML-строку (ТЗ §8.3, функция 39).
    /// </summary>
    /// <param name="blank">Бланк.</param>
    /// <returns>XML-документ blank.xml.</returns>
    public string SerializeBlank(CustomBlank blank)
    {
        ArgumentNullException.ThrowIfNull(blank);

        var документ = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            ЗаписатьБланк(blank));

        return документ.Declaration + Environment.NewLine + документ.Root;
    }

    /// <summary>
    /// Разбирает XML бланка со строгой валидацией против XSD (ТЗ §8.3, функция 39).
    /// </summary>
    /// <param name="xml">Текст blank.xml.</param>
    /// <returns>Модель бланка.</returns>
    /// <exception cref="BlankXmlException">
    /// XML не соответствует встроенной XSD-схеме либо не является корректным XML.
    /// </exception>
    public CustomBlank Deserialize(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        XDocument документ;

        try
        {
            документ = XDocument.Parse(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException ошибка)
        {
            throw new BlankXmlException(
                "Ошибка разбора XML бланка: " + ошибка.Message,
                ошибка.LineNumber,
                ошибка.LinePosition,
                ошибка);
        }

        var ошибки = new List<(string Сообщение, int Строка, int Столбец)>();
        документ.Validate(
            _схемы,
            (отправитель, аргументы) =>
            {
                if (аргументы.Severity == XmlSeverityType.Error)
                {
                    var строка = 0;
                    var столбец = 0;

                    if (аргументы.Exception is XmlSchemaValidationException ошибкаСхемы)
                    {
                        строка = ошибкаСхемы.LineNumber;
                        столбец = ошибкаСхемы.LinePosition;
                    }

                    ошибки.Add((аргументы.Message, строка, столбец));
                }
            });

        if (ошибки.Count > 0)
        {
            var (сообщение, строка, столбец) = ошибки[0];

            if (строка == 0)
            {
                (строка, столбец) = ПозицияОшибки(документ);
            }

            throw new BlankXmlException(
                "Формат бланка не соответствует XSD-схеме: " + сообщение,
                строка,
                столбец);
        }

        var корень = документ.Root
            ?? throw new BlankXmlException("Формат бланка пуст.", 1, 1);

        return ПрочитатьБланк(корень);
    }

    private static Stream ОткрытьСхему()
    {
        var сборка = typeof(BlankXmlSerializer).Assembly;
        var поток = сборка.GetManifestResourceStream(SchemaResourceName);

        return поток
            ?? throw new InvalidOperationException(
                $"Встроенный ресурс XSD-схемы бланка «{SchemaResourceName}» не найден.");
    }

    private static (int Строка, int Столбец) ПозицияОшибки(XDocument документ)
    {
        // Валидатор XSD не всегда сообщает позицию; берём сведения о строках
        // корневого элемента как опорную точку отчёта.
        if (документ.Root is null)
        {
            return (1, 1);
        }

        var строки = (IXmlLineInfo)документ.Root;

        return строки.HasLineInfo() ? (строки.LineNumber, строки.LinePosition) : (1, 1);
    }

    private static XElement ЗаписатьБланк(CustomBlank бланк)
    {
        var корень = new XElement(
            "CustomBlank",
            new XAttribute("version", бланк.Version),
            new XElement("Name", бланк.Name),
            new XElement("Country", бланк.Country),
            new XElement("StandardLayout", бланк.StandardLayout ? "true" : "false"),
            new XElement(
                "Geometry",
                new XElement("Width", new XAttribute("mm", Ф(бланк.Geometry.WidthMm))),
                new XElement("Height", new XAttribute("mm", Ф(бланк.Geometry.HeightMm))),
                new XElement(
                    "Orientation",
                    бланк.Geometry.Orientation == BlankOrientation.Landscape ? "Landscape" : "Portrait"),
                new XElement("Bleed", new XAttribute("mm", Ф(бланк.Geometry.BleedMm))),
                new XElement("CropMarks", бланк.Geometry.CropMarks ? "true" : "false")),
            new XElement(
                "Pages",
                бланк.Pages.Select(ЗаписатьСтраницу)));

        return корень;
    }

    private static XElement ЗаписатьСтраницу(BlankPage страница)
    {
        var элемент = new XElement("Page", new XAttribute("number", страница.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (!string.IsNullOrWhiteSpace(страница.BackgroundFile))
        {
            элемент.Add(
                new XElement(
                    "Background",
                    new XAttribute("file", страница.BackgroundFile),
                    new XAttribute("opacity", Ф(страница.BackgroundOpacity))));
        }

        if (страница.Fields.Count > 0)
        {
            элемент.Add(new XElement("Fields", страница.Fields.Select(ЗаписатьПоле)));
        }

        return элемент;
    }

    private static XElement ЗаписатьПоле(BlankFieldSpec поле)
    {
        var элемент = new XElement(
            "Field",
            new XAttribute("name", поле.Name),
            new XAttribute("kind", поле.Kind));

        if (поле.Rotation != 0)
        {
            элемент.Add(new XAttribute("rotation", поле.Rotation.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (поле.Frame != BlankFieldFrame.None)
        {
            элемент.Add(new XAttribute("frame", поле.Frame.ToString()));
        }

        if (!поле.Visible)
        {
            элемент.Add(new XAttribute("visible", "false"));
        }

        if (!поле.Printable)
        {
            элемент.Add(new XAttribute("printable", "false"));
        }

        if (поле.MaxChars != 255)
        {
            элемент.Add(new XAttribute("maxChars", поле.MaxChars.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        элемент.Add(
            new XElement(
                "Bounds",
                new XAttribute("x", Ф(поле.Bounds.XMm)),
                new XAttribute("y", Ф(поле.Bounds.YMm)),
                new XAttribute("width", Ф(поле.Bounds.WidthMm)),
                new XAttribute("height", Ф(поле.Bounds.HeightMm))));

        if (поле.Kind == BlankFieldKinds.PostIndex && поле.PostIndex is { } индекс)
        {
            элемент.Add(
                new XElement(
                    "PostIndex",
                    new XAttribute("mmDigitWidth", Ф(индекс.DigitWidthMm)),
                    new XAttribute("mmDigitHeight", Ф(индекс.DigitHeightMm)),
                    new XAttribute("mmLineWidth", Ф(индекс.LineWidthMm)),
                    new XAttribute("mmSpacing", Ф(индекс.SpacingMm)),
                    new XAttribute("digitCount", индекс.DigitCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new XAttribute("showGrid", индекс.ShowGrid ? "true" : "false")));
        }

        if (поле.Kind != BlankFieldKinds.PostIndex && поле.Kind != BlankFieldKinds.Zone)
        {
            var шрифт = new XElement(
                "Font",
                new XAttribute("name", поле.FontName),
                new XAttribute("sizePt", Ф(поле.FontSizePt)));

            if (поле.MinFontSizePt != 6.5m)
            {
                шрифт.SetAttributeValue("minSizePt", Ф(поле.MinFontSizePt));
            }

            if (поле.MaxFontSizePt != 11.0m)
            {
                шрифт.SetAttributeValue("maxSizePt", Ф(поле.MaxFontSizePt));
            }

            if (поле.Bold)
            {
                шрифт.SetAttributeValue("bold", "true");
            }

            if (поле.Italic)
            {
                шрифт.SetAttributeValue("italic", "true");
            }

            элемент.Add(шрифт);
        }

        if (поле.AlignHorizontal != "Left" || поле.AlignVertical != "Center")
        {
            элемент.Add(
                new XElement(
                    "Align",
                    new XAttribute("horizontal", поле.AlignHorizontal),
                    new XAttribute("vertical", поле.AlignVertical)));
        }

        if (!поле.WordWrap || поле.MaxLines != 1)
        {
            элемент.Add(
                new XElement(
                    "Wrap",
                    new XAttribute("wordWrap", поле.WordWrap ? "true" : "false"),
                    new XAttribute("maxLines", поле.MaxLines.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        if (поле.Antialiasing || !поле.Center || поле.Transparent)
        {
            элемент.Add(
                new XElement(
                    "Rendering",
                    new XAttribute("antialiasing", поле.Antialiasing ? "true" : "false"),
                    new XAttribute("center", поле.Center ? "true" : "false"),
                    new XAttribute("transparent", поле.Transparent ? "true" : "false")));
        }

        if (!string.IsNullOrWhiteSpace(поле.OnBeforePrint))
        {
            элемент.Add(
                new XElement(
                    "Script",
                    new XAttribute("event", "onBeforePrint"),
                    new XCData(поле.OnBeforePrint)));
        }

        return элемент;
    }

    private static CustomBlank ПрочитатьБланк(XElement корень)
    {
        var бланк = new CustomBlank
        {
            Version = (string?)корень.Attribute("version") ?? "2.0",
            Name = Строка(корень.Element("Name")),
            Country = Строка(корень.Element("Country")),
            StandardLayout = Буль(корень.Element("StandardLayout")),
        };

        var геометрия = корень.Element("Geometry")
            ?? throw new BlankXmlException("Формат бланка не содержит элемент Geometry.");

        бланк.Geometry = new BlankGeometry
        {
            WidthMm = Десятичное(геометрия.Element("Width"), "mm"),
            HeightMm = Десятичное(геометрия.Element("Height"), "mm"),
            Orientation = string.Equals(геометрия.Element("Orientation")?.Value, "Landscape", StringComparison.Ordinal)
                ? BlankOrientation.Landscape
                : BlankOrientation.Portrait,
            BleedMm = Десятичное(геометрия.Element("Bleed"), "mm"),
            CropMarks = Буль(геометрия.Element("CropMarks")),
        };

        foreach (var страница in корень.Element("Pages")?.Elements("Page") ?? Enumerable.Empty<XElement>())
        {
            бланк.Pages.Add(ПрочитатьСтраницу(страница));
        }

        return бланк;
    }

    private static BlankPage ПрочитатьСтраницу(XElement элемент)
    {
        var страница = new BlankPage
        {
            Number = int.Parse(
                (string?)элемент.Attribute("number") ?? "1",
                System.Globalization.CultureInfo.InvariantCulture),
        };

        var фон = элемент.Element("Background");

        if (фон is not null)
        {
            страница.BackgroundFile = (string?)фон.Attribute("file");
            страница.BackgroundOpacity = фон.Attribute("opacity") is null
                ? 1.0m
                : decimal.Parse(фон.Attribute("opacity")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var поле in элемент.Element("Fields")?.Elements("Field") ?? Enumerable.Empty<XElement>())
        {
            страница.Fields.Add(ПрочитатьПоле(поле));
        }

        return страница;
    }

    private static BlankFieldSpec ПрочитатьПоле(XElement элемент)
    {
        var габариты = элемент.Element("Bounds")
            ?? throw new BlankXmlException($"Поле «{(string?)элемент.Attribute("name")}" + "» не содержит элемент Bounds.");

        var поле = new BlankFieldSpec
        {
            Name = (string?)элемент.Attribute("name") ?? string.Empty,
            Kind = (string?)элемент.Attribute("kind") ?? BlankFieldKinds.Text,
            Bounds = new BlankBounds
            {
                XMm = decimal.Parse((string?)габариты.Attribute("x") ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                YMm = decimal.Parse((string?)габариты.Attribute("y") ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                WidthMm = decimal.Parse((string?)габариты.Attribute("width") ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                HeightMm = decimal.Parse((string?)габариты.Attribute("height") ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            },
            Rotation = int.Parse((string?)элемент.Attribute("rotation") ?? "0", System.Globalization.CultureInfo.InvariantCulture),
            Frame = РазобратьРамку((string?)элемент.Attribute("frame")),
            Visible = РазобратьФлаг((string?)элемент.Attribute("visible"), true),
            Printable = РазобратьФлаг((string?)элемент.Attribute("printable"), true),
            MaxChars = int.Parse((string?)элемент.Attribute("maxChars") ?? "255", System.Globalization.CultureInfo.InvariantCulture),
        };

        if (поле.Kind == BlankFieldKinds.PostIndex)
        {
            var штамп = элемент.Element("PostIndex")
                ?? throw new BlankXmlException($"Поле кодового штампа «{поле.Name}" + "» не содержит элемент PostIndex.");

            поле.PostIndex = new BlankPostIndexSpec
            {
                DigitWidthMm = decimal.Parse((string?)штамп.Attribute("mmDigitWidth") ?? "3.7", System.Globalization.CultureInfo.InvariantCulture),
                DigitHeightMm = decimal.Parse((string?)штамп.Attribute("mmDigitHeight") ?? "5.5", System.Globalization.CultureInfo.InvariantCulture),
                LineWidthMm = decimal.Parse((string?)штамп.Attribute("mmLineWidth") ?? "0.45", System.Globalization.CultureInfo.InvariantCulture),
                SpacingMm = decimal.Parse((string?)штамп.Attribute("mmSpacing") ?? "5.4", System.Globalization.CultureInfo.InvariantCulture),
                DigitCount = int.Parse((string?)штамп.Attribute("digitCount") ?? "6", System.Globalization.CultureInfo.InvariantCulture),
                ShowGrid = РазобратьФлаг((string?)штамп.Attribute("showGrid"), true),
            };
        }
        else
        {
            var шрифт = элемент.Element("Font");

            if (шрифт is not null)
            {
                поле.FontName = (string?)шрифт.Attribute("name") ?? поле.FontName;
                поле.FontSizePt = РазобратьДесятичное((string?)шрифт.Attribute("sizePt"), поле.FontSizePt);
                поле.MinFontSizePt = РазобратьДесятичное((string?)шрифт.Attribute("minSizePt"), поле.MinFontSizePt);
                поле.MaxFontSizePt = РазобратьДесятичное((string?)шрифт.Attribute("maxSizePt"), поле.MaxFontSizePt);
                поле.Bold = РазобратьФлаг((string?)шрифт.Attribute("bold"), false);
                поле.Italic = РазобратьФлаг((string?)шрифт.Attribute("italic"), false);
            }
        }

        var выравнивание = элемент.Element("Align");

        if (выравнивание is not null)
        {
            поле.AlignHorizontal = (string?)выравнивание.Attribute("horizontal") ?? поле.AlignHorizontal;
            поле.AlignVertical = (string?)выравнивание.Attribute("vertical") ?? поле.AlignVertical;
        }

        var перенос = элемент.Element("Wrap");

        if (перенос is not null)
        {
            поле.WordWrap = РазобратьФлаг((string?)перенос.Attribute("wordWrap"), поле.WordWrap);
            поле.MaxLines = int.Parse((string?)перенос.Attribute("maxLines") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        }

        var отрисовка = элемент.Element("Rendering");

        if (отрисовка is not null)
        {
            поле.Antialiasing = РазобратьФлаг((string?)отрисовка.Attribute("antialiasing"), поле.Antialiasing);
            поле.Center = РазобратьФлаг((string?)отрисовка.Attribute("center"), поле.Center);
            поле.Transparent = РазобратьФлаг((string?)отрисовка.Attribute("transparent"), поле.Transparent);
        }

        var скрипт = элемент
            .Elements("Script")
            .FirstOrDefault(с => string.Equals((string?)с.Attribute("event"), "onBeforePrint", StringComparison.Ordinal));

        поле.OnBeforePrint = скрипт?.Value;

        return поле;
    }

    private static BlankFieldFrame РазобратьРамку(string? значение) => значение switch
    {
        "Box" => BlankFieldFrame.Box,
        "Underline" => BlankFieldFrame.Underline,
        _ => BlankFieldFrame.None,
    };

    private static bool РазобратьФлаг(string? значение, bool поУмолчанию) => значение switch
    {
        "true" => true,
        "false" => false,
        "1" => true,
        "0" => false,
        _ => поУмолчанию,
    };

    private static decimal РазобратьДесятичное(string? значение, decimal поУмолчанию) =>
        значение is null
            ? поУмолчанию
            : decimal.Parse(значение, System.Globalization.CultureInfo.InvariantCulture);

    private static decimal Десятичное(XElement? элемент, string атрибут)
    {
        if (элемент is null)
        {
            throw new BlankXmlException($"Элемент «{элемент?.Name.LocalName}" + "» отсутствует.");
        }

        var значение = (string?)элемент.Attribute(атрибут)
            ?? throw new BlankXmlException($"Элемент «{элемент.Name.LocalName}" + $"» не содержит атрибут «{атрибут}».");

        return decimal.Parse(значение, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Строка(XElement? элемент)
    {
        if (элемент is null)
        {
            throw new BlankXmlException("Обязательный элемент бланка отсутствует.");
        }

        return элемент.Value;
    }

    private static bool Буль(XElement? элемент) => элемент is not null
        && string.Equals(элемент.Value, "true", StringComparison.OrdinalIgnoreCase);

    private static string Ф(decimal значение) =>
        значение.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);
}
