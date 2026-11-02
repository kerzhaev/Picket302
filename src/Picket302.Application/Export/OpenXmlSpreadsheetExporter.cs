using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Picket302.Application.Export;

/// <summary>
/// Выгрузка таблицы в книгу Excel (XLSX) без Microsoft Office
/// (ТЗ §8.5, функция 71; ТЗ §8.5, функция 70 — «Полная независимость
/// экспорта от Microsoft Office»).
/// </summary>
/// <remarks>
/// <para>
/// XLSX — это ZIP-архив контейнера Open Packaging Conventions: карта типов
/// содержимого <c>[Content_Types].xml</c>, связи <c>_rels/.rels</c> и части
/// книги <c>xl/workbook.xml</c>, <c>xl/_rels/workbook.xml.rels</c>,
/// <c>xl/worksheets/sheet1.xml</c>, <c>xl/sharedStrings.xml</c>,
/// <c>xl/styles.xml</c>.
/// </para>
/// <para>
/// Текстовые значения выносятся в общую таблицу строк, числа записываются
/// числами, даты — серийными номерами с форматом ячейки. Сборки
/// Microsoft.Office.Interop.* и OLE/COM не используются.
/// </para>
/// </remarks>
public sealed class OpenXmlSpreadsheetExporter : ISpreadsheetExporter
{
    /// <summary>Имя карты типов содержимого пакета.</summary>
    public const string ContentTypesPartName = "[Content_Types].xml";

    /// <summary>Имя части со связями пакета.</summary>
    public const string PackageRelationshipsPartName = "_rels/.rels";

    /// <summary>Имя части книги.</summary>
    public const string WorkbookPartName = "xl/workbook.xml";

    /// <summary>Имя части со связями книги.</summary>
    public const string WorkbookRelationshipsPartName = "xl/_rels/workbook.xml.rels";

    /// <summary>Имя части листа книги.</summary>
    public const string WorksheetPartName = "xl/worksheets/sheet1.xml";

    /// <summary>Имя части общей таблицы строк.</summary>
    public const string SharedStringsPartName = "xl/sharedStrings.xml";

    /// <summary>Имя части стилей книги.</summary>
    public const string StylesPartName = "xl/styles.xml";

    /// <summary>Пространство имён частей книги.</summary>
    private const string SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Пространство имён связей пакета.</summary>
    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Пространство имён карты типов содержимого.</summary>
    private const string ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>Пространство имён связей документа.</summary>
    private const string OfficeRelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Номер пользовательского формата даты в таблице стилей.</summary>
    private const int DateFormatId = 164;

    /// <summary>Индекс стиля даты в списке форматов ячеек.</summary>
    private const int DateStyleIndex = 1;

    /// <inheritdoc />
    public void Write(SpreadsheetTable table, Stream output)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(output);

        var строки = new List<SpreadsheetCell[]>();

        foreach (var строка in table.Rows)
        {
            ArgumentNullException.ThrowIfNull(строка);
            строки.Add([.. строка]);
        }

        var общиеСтроки = new List<string>();
        var индексыОбщихСтрок = new Dictionary<string, int>(StringComparer.Ordinal);

        int ИндексОбщейСтроки(string значение)
        {
            if (индексыОбщихСтрок.TryGetValue(значение, out var индекс))
            {
                return индекс;
            }

            индекс = общиеСтроки.Count;
            общиеСтроки.Add(значение);
            индексыОбщихСтрок[значение] = индекс;
            return индекс;
        }

        // Заголовки столбцов и текстовые значения ячеек попадают в общую таблицу строк.
        var индексыЗаголовков = new int[table.Headers.Count];

        for (var i = 0; i < table.Headers.Count; i++)
        {
            индексыЗаголовков[i] = ИндексОбщейСтроки(table.Headers[i]);
        }

        var подготовленныеСтроки = new List<(int[] Строки, IReadOnlyList<SpreadsheetCell> Значения)>();

        foreach (var строка in строки)
        {
            var ссылки = new int[строка.Length];

            for (var i = 0; i < строка.Length; i++)
            {
                var ячейка = строка[i];

                ссылки[i] = ячейка.IsText ? ИндексОбщейСтроки((string)ячейка.Value!) : -1;
            }

            подготовленныеСтроки.Add((ссылки, строка));
        }

        using var архив = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        ЗаписатьЧасть(архив, ContentTypesPartName, writer => ЗаписатьТипыСодержимого(writer));
        ЗаписатьЧасть(архив, PackageRelationshipsPartName, writer => ЗаписатьСвязиПакета(writer));
        ЗаписатьЧасть(архив, WorkbookPartName, writer => ЗаписатьКнигу(writer, table.SheetName));
        ЗаписатьЧасть(архив, WorkbookRelationshipsPartName, writer => ЗаписатьСвязиКниги(writer));
        ЗаписатьЧасть(архив, WorksheetPartName, writer => ЗаписатьЛист(writer, table, индексыЗаголовков, подготовленныеСтроки));
        ЗаписатьЧасть(архив, SharedStringsPartName, writer => ЗаписатьОбщиеСтроки(writer, общиеСтроки));
        ЗаписатьЧасть(архив, StylesPartName, writer => ЗаписатьСтили(writer, table.DateFormat));
    }

    /// <summary>Записывает XML-часть пакета.</summary>
    /// <param name="архив">Архив пакета.</param>
    /// <param name="имя">Имя части внутри пакета.</param>
    /// <param name="запись">Действие, записывающее содержимое части.</param>
    private static void ЗаписатьЧасть(ZipArchive архив, string имя, Action<XmlWriter> запись)
    {
        var записьАрхива = архив.CreateEntry(имя, CompressionLevel.Optimal);

        using var поток = записьАрхива.Open();
        using var писатель = XmlWriter.Create(поток, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = false,
        });

        запись(писатель);
    }

    /// <summary>Записывает карту типов содержимого пакета.</summary>
    /// <param name="писатель">Писатель XML.</param>
    private static void ЗаписатьТипыСодержимого(XmlWriter писатель)
    {
        писатель.WriteStartDocument(true);
        писатель.WriteStartElement("Types", ContentTypesNamespace);

        писатель.WriteStartElement("Default", ContentTypesNamespace);
        писатель.WriteAttributeString("Extension", "rels");
        писатель.WriteAttributeString(
            "ContentType",
            "application/vnd.openxmlformats-package.relationships+xml");
        писатель.WriteEndElement();

        писатель.WriteStartElement("Default", ContentTypesNamespace);
        писатель.WriteAttributeString("Extension", "xml");
        писатель.WriteAttributeString("ContentType", "application/xml");
        писатель.WriteEndElement();

        ЗаписатьПереопределение(
            писатель,
            "/xl/workbook.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        ЗаписатьПереопределение(
            писатель,
            "/xl/worksheets/sheet1.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        ЗаписатьПереопределение(
            писатель,
            "/xl/sharedStrings.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml");
        ЗаписатьПереопределение(
            писатель,
            "/xl/styles.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");

        писатель.WriteEndElement();
        писатель.WriteEndDocument();
    }

    /// <summary>Записывает переопределение типа содержимого части пакета.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="часть">Имя части.</param>
    /// <param name="тип">Тип содержимого части.</param>
    private static void ЗаписатьПереопределение(XmlWriter писатель, string часть, string тип)
    {
        писатель.WriteStartElement("Override", ContentTypesNamespace);
        писатель.WriteAttributeString("PartName", часть);
        писатель.WriteAttributeString("ContentType", тип);
        писатель.WriteEndElement();
    }

    /// <summary>Записывает связи пакета.</summary>
    /// <param name="писатель">Писатель XML.</param>
    private static void ЗаписатьСвязиПакета(XmlWriter писатель)
    {
        писатель.WriteStartDocument(true);
        писатель.WriteStartElement("Relationships", RelationshipsNamespace);

        писатель.WriteStartElement("Relationship", RelationshipsNamespace);
        писатель.WriteAttributeString("Id", "rId1");
        писатель.WriteAttributeString(
            "Type",
            $"{OfficeRelationshipsNamespace}/officeDocument");
        писатель.WriteAttributeString("Target", "xl/workbook.xml");
        писатель.WriteEndElement();

        писатель.WriteEndElement();
        писатель.WriteEndDocument();
    }

    /// <summary>Записывает часть книги со списком листов.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="имяЛиста">Имя листа книги.</param>
    private static void ЗаписатьКнигу(XmlWriter писатель, string имяЛиста)
    {
        писатель.WriteStartDocument(true);
        писатель.WriteStartElement("workbook", SpreadsheetNamespace);
        писатель.WriteAttributeString("xmlns", "r", null, OfficeRelationshipsNamespace);
        писатель.WriteStartElement("sheets", SpreadsheetNamespace);

        писатель.WriteStartElement("sheet", SpreadsheetNamespace);
        писатель.WriteAttributeString("name", имяЛиста);
        писатель.WriteAttributeString("sheetId", "1");
        писатель.WriteAttributeString("id", "rId1", OfficeRelationshipsNamespace);
        писатель.WriteEndElement();

        писатель.WriteEndElement();
        писатель.WriteEndElement();
        писатель.WriteEndDocument();
    }

    /// <summary>Записывает связи книги: лист, общая таблица строк и стили.</summary>
    /// <param name="писатель">Писатель XML.</param>
    private static void ЗаписатьСвязиКниги(XmlWriter писатель)
    {
        писатель.WriteStartDocument(true);
        писатель.WriteStartElement("Relationships", RelationshipsNamespace);

        ЗаписатьСвязь(писатель, "rId1", $"{OfficeRelationshipsNamespace}/worksheet", "worksheets/sheet1.xml");
        ЗаписатьСвязь(писатель, "rId2", $"{OfficeRelationshipsNamespace}/sharedStrings", "sharedStrings.xml");
        ЗаписатьСвязь(писатель, "rId3", $"{OfficeRelationshipsNamespace}/styles", "styles.xml");

        писатель.WriteEndElement();
        писатель.WriteEndDocument();
    }

    /// <summary>Записывает одну связь части пакета.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="идентификатор">Идентификатор связи.</param>
    /// <param name="тип">Тип связи.</param>
    /// <param name="цель">Цель связи.</param>
    private static void ЗаписатьСвязь(XmlWriter писатель, string идентификатор, string тип, string цель)
    {
        писатель.WriteStartElement("Relationship", RelationshipsNamespace);
        писатель.WriteAttributeString("Id", идентификатор);
        писатель.WriteAttributeString("Type", тип);
        писатель.WriteAttributeString("Target", цель);
        писатель.WriteEndElement();
    }

    /// <summary>Записывает лист книги с данными таблицы.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="таблица">Выгружаемая таблица.</param>
    /// <param name="индексыЗаголовков">Индексы заголовков в общей таблице строк.</param>
    /// <param name="строки">Подготовленные строки данных.</param>
    private static void ЗаписатьЛист(
        XmlWriter писатель,
        SpreadsheetTable таблица,
        int[] индексыЗаголовков,
        IReadOnlyList<(int[] Строки, IReadOnlyList<SpreadsheetCell> Значения)> строки)
    {
        var числоСтолбцов = Math.Max(таблица.ColumnCount, строки.Count == 0 ? 0 : строки.Max(с => с.Значения.Count));

        // Строка заголовков не пишется при IncludeHeader=false (F-D10),
        // строки данных сдвигаются на одну позицию вверх.
        var писатьЗаголовок = таблица.IncludeHeader;
        var числоСтрок = строки.Count + (писатьЗаголовок ? 1 : 0);

        писатель.WriteStartDocument(true);
        писатель.WriteStartElement("worksheet", SpreadsheetNamespace);

        писатель.WriteStartElement("dimension", SpreadsheetNamespace);
        писатель.WriteAttributeString(
            "ref",
            $"A1:{БукваСтолбца(Math.Max(числоСтолбцов, 1))}{числоСтрок}");
        писатель.WriteEndElement();

        писатель.WriteStartElement("sheetData", SpreadsheetNamespace);

        if (писатьЗаголовок)
        {
            писатель.WriteStartElement("row", SpreadsheetNamespace);
            писатель.WriteAttributeString("r", "1");

            for (var i = 0; i < индексыЗаголовков.Length; i++)
            {
                ЗаписатьОбщуюЯчейку(писатель, $"{БукваСтолбца(i + 1)}1", индексыЗаголовков[i]);
            }

            писатель.WriteEndElement();
        }

        for (var номер = 0; номер < строки.Count; номер++)
        {
            var строка = строки[номер];

            писатель.WriteStartElement("row", SpreadsheetNamespace);
            писатель.WriteAttributeString("r", (номер + (писатьЗаголовок ? 2 : 1)).ToString(CultureInfo.InvariantCulture));

            for (var i = 0; i < строка.Значения.Count; i++)
            {
                var ссылка = $"{БукваСтолбца(i + 1)}{номер + 2}";
                var ячейка = строка.Значения[i];

                if (ячейка.IsEmpty)
                {
                    continue;
                }

                if (ячейка.IsText)
                {
                    ЗаписатьОбщуюЯчейку(писатель, ссылка, строка.Строки[i]);
                }
                else if (ячейка.IsNumber)
                {
                    ЗаписатьЧисловуюЯчейку(писатель, ссылка, (decimal)ячейка.Value!);
                }
                else if (ячейка.IsDate)
                {
                    ЗаписатьДату(писатель, ссылка, (DateTime)ячейка.Value!);
                }
            }

            писатель.WriteEndElement();
        }

        писатель.WriteEndElement();
        писатель.WriteEndElement();
        писатель.WriteEndDocument();
    }

    /// <summary>Записывает ячейку со ссылкой на общую таблицу строк.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="ссылка">Адрес ячейки.</param>
    /// <param name="индекс">Индекс строки в общей таблице строк.</param>
    private static void ЗаписатьОбщуюЯчейку(XmlWriter писатель, string ссылка, int индекс)
    {
        писатель.WriteStartElement("c", SpreadsheetNamespace);
        писатель.WriteAttributeString("r", ссылка);
        писатель.WriteAttributeString("t", "s");
        писатель.WriteElementString("v", SpreadsheetNamespace, индекс.ToString(CultureInfo.InvariantCulture));
        писатель.WriteEndElement();
    }

    /// <summary>Записывает числовую ячейку.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="ссылка">Адрес ячейки.</param>
    /// <param name="значение">Числовое значение.</param>
    private static void ЗаписатьЧисловуюЯчейку(XmlWriter писатель, string ссылка, decimal значение)
    {
        писатель.WriteStartElement("c", SpreadsheetNamespace);
        писатель.WriteAttributeString("r", ссылка);
        писатель.WriteElementString("v", SpreadsheetNamespace, значение.ToString(CultureInfo.InvariantCulture));
        писатель.WriteEndElement();
    }

    /// <summary>Записывает ячейку с датой в виде серийного номера.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="ссылка">Адрес ячейки.</param>
    /// <param name="значение">Значение даты.</param>
    private static void ЗаписатьДату(XmlWriter писатель, string ссылка, DateTime значение)
    {
        var серийныйНомер = значение.ToOADate();

        писатель.WriteStartElement("c", SpreadsheetNamespace);
        писатель.WriteAttributeString("r", ссылка);
        писатель.WriteAttributeString("s", DateStyleIndex.ToString(CultureInfo.InvariantCulture));
        писатель.WriteElementString(
            "v",
            SpreadsheetNamespace,
            серийныйНомер.ToString(CultureInfo.InvariantCulture));
        писатель.WriteEndElement();
    }

    /// <summary>Записывает общую таблицу строк книги.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="строки">Строки общей таблицы.</param>
    private static void ЗаписатьОбщиеСтроки(XmlWriter писатель, IReadOnlyList<string> строки)
    {
        писатель.WriteStartDocument(true);
        писатель.WriteStartElement("sst", SpreadsheetNamespace);
        писатель.WriteAttributeString("count", строки.Count.ToString(CultureInfo.InvariantCulture));
        писатель.WriteAttributeString("uniqueCount", строки.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var строка in строки)
        {
            писатель.WriteStartElement("si", SpreadsheetNamespace);
            писатель.WriteStartElement("t", SpreadsheetNamespace);
            писатель.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
            писатель.WriteString(строка);
            писатель.WriteEndElement();
            писатель.WriteEndElement();
        }

        писатель.WriteEndElement();
        писатель.WriteEndDocument();
    }

    /// <summary>Записывает стили книги: обычный формат и формат даты.</summary>
    /// <param name="писатель">Писатель XML.</param>
    /// <param name="форматДаты">Код формата даты для книги Excel.</param>
    private static void ЗаписатьСтили(XmlWriter писатель, string форматДаты)
    {
        var кодФормата = ПривестиКодФормата(форматДаты);

        писатель.WriteStartDocument(true);
        писатель.WriteStartElement("styleSheet", SpreadsheetNamespace);

        писатель.WriteStartElement("numFmts", SpreadsheetNamespace);
        писатель.WriteAttributeString("count", "1");
        писатель.WriteStartElement("numFmt", SpreadsheetNamespace);
        писатель.WriteAttributeString("numFmtId", DateFormatId.ToString(CultureInfo.InvariantCulture));
        писатель.WriteAttributeString("formatCode", кодФормата.Replace("\"", "\"\"", StringComparison.Ordinal));
        писатель.WriteEndElement();
        писатель.WriteEndElement();

        писатель.WriteStartElement("fonts", SpreadsheetNamespace);
        писатель.WriteAttributeString("count", "1");
        писатель.WriteStartElement("font", SpreadsheetNamespace);
        писатель.WriteElementString("sz", SpreadsheetNamespace, "11");
        писатель.WriteElementString("name", SpreadsheetNamespace, "Calibri");
        писатель.WriteElementString("family", SpreadsheetNamespace, "2");
        писатель.WriteEndElement();
        писатель.WriteEndElement();

        писатель.WriteStartElement("fills", SpreadsheetNamespace);
        писатель.WriteAttributeString("count", "2");
        писатель.WriteStartElement("fill", SpreadsheetNamespace);
        писатель.WriteStartElement("patternFill", SpreadsheetNamespace);
        писатель.WriteAttributeString("patternType", "none");
        писатель.WriteEndElement();
        писатель.WriteEndElement();
        писатель.WriteStartElement("fill", SpreadsheetNamespace);
        писатель.WriteStartElement("patternFill", SpreadsheetNamespace);
        писатель.WriteAttributeString("patternType", "gray125");
        писатель.WriteEndElement();
        писатель.WriteEndElement();
        писатель.WriteEndElement();

        писатель.WriteStartElement("borders", SpreadsheetNamespace);
        писатель.WriteAttributeString("count", "1");
        писатель.WriteStartElement("border", SpreadsheetNamespace);
        писатель.WriteStartElement("left", SpreadsheetNamespace);
        писатель.WriteEndElement();
        писатель.WriteStartElement("right", SpreadsheetNamespace);
        писатель.WriteEndElement();
        писатель.WriteStartElement("top", SpreadsheetNamespace);
        писатель.WriteEndElement();
        писатель.WriteStartElement("bottom", SpreadsheetNamespace);
        писатель.WriteEndElement();
        писатель.WriteStartElement("diagonal", SpreadsheetNamespace);
        писатель.WriteEndElement();
        писатель.WriteEndElement();
        писатель.WriteEndElement();

        писатель.WriteStartElement("cellStyleXfs", SpreadsheetNamespace);
        писатель.WriteAttributeString("count", "1");
        писатель.WriteStartElement("xf", SpreadsheetNamespace);
        писатель.WriteAttributeString("numFmtId", "0");
        писатель.WriteAttributeString("fontId", "0");
        писатель.WriteAttributeString("fillId", "0");
        писатель.WriteAttributeString("borderId", "0");
        писатель.WriteEndElement();
        писатель.WriteEndElement();

        писатель.WriteStartElement("cellXfs", SpreadsheetNamespace);
        писатель.WriteAttributeString("count", "2");
        писатель.WriteStartElement("xf", SpreadsheetNamespace);
        писатель.WriteAttributeString("numFmtId", "0");
        писатель.WriteAttributeString("fontId", "0");
        писатель.WriteAttributeString("fillId", "0");
        писатель.WriteAttributeString("borderId", "0");
        писатель.WriteAttributeString("xfId", "0");
        писатель.WriteEndElement();
        писатель.WriteStartElement("xf", SpreadsheetNamespace);
        писатель.WriteAttributeString("numFmtId", DateFormatId.ToString(CultureInfo.InvariantCulture));
        писатель.WriteAttributeString("fontId", "0");
        писатель.WriteAttributeString("fillId", "0");
        писатель.WriteAttributeString("borderId", "0");
        писатель.WriteAttributeString("xfId", "0");
        писатель.WriteAttributeString("applyNumberFormat", "1");
        писатель.WriteEndElement();
        писатель.WriteEndElement();

        писатель.WriteStartElement("cellStyles", SpreadsheetNamespace);
        писатель.WriteAttributeString("count", "1");
        писатель.WriteStartElement("cellStyle", SpreadsheetNamespace);
        писатель.WriteAttributeString("name", "Обычный");
        писатель.WriteAttributeString("xfId", "0");
        писатель.WriteAttributeString("builtinId", "0");
        писатель.WriteEndElement();
        писатель.WriteEndElement();

        писатель.WriteEndElement();
        писатель.WriteEndDocument();
    }

    /// <summary>
    /// Приводит формат даты к коду формата книги Excel: убираются символы,
    /// недопустимые в коде формата.
    /// </summary>
    /// <param name="форматДаты">Формат даты .NET.</param>
    /// <returns>Код формата книги Excel.</returns>
    private static string ПривестиКодФормата(string форматДаты)
    {
        var символы = форматДаты
            .Where(символ => !ЗапрещённыеСимволыФормата.Contains(символ))
            .ToArray();

        var код = new string(символы).Trim();

        return код.Length == 0 ? SpreadsheetTable.DefaultDateFormat : код;
    }

    /// <summary>Символы, недопустимые в коде формата книги Excel.</summary>
    private static readonly char[] ЗапрещённыеСимволыФормата = ['*', '?', '[', ']', '\\', ';'];

    /// <summary>
    /// Возвращает буквенное обозначение столбца по его номеру (нумерация с единицы).
    /// </summary>
    /// <param name="номер">Номер столбца.</param>
    /// <returns>Обозначение столбца: A, B, …, Z, AA и далее.</returns>
    private static string БукваСтолбца(int номер)
    {
        var буквы = string.Empty;
        var значение = номер;

        while (значение > 0)
        {
            var остаток = (значение - 1) % 26;
            буквы = (char)('A' + остаток) + буквы;
            значение = (значение - 1) / 26;
        }

        return буквы;
    }
}
