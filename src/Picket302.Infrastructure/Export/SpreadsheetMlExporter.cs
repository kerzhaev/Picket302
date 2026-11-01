using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Picket302.Infrastructure.Export;

/// <summary>
/// Тип столбца экспортируемой таблицы (ТЗ §8.5, функция 69: «с автоматическим
/// форматированием типов данных (числа, даты, текст)»).
/// </summary>
public enum ExportColumnType
{
    /// <summary>Тип определяется по значению ячейки.</summary>
    Auto,

    /// <summary>Текст: значение попадает в таблицу общих строк.</summary>
    Text,

    /// <summary>Число: значение записывается числовой ячейкой.</summary>
    Number,

    /// <summary>Дата: значение записывается числовой ячейкой с форматом даты.</summary>
    Date,
}

/// <summary>
/// Столбец экспортируемой таблицы (ТЗ §8.5, функция 69).
/// </summary>
public sealed class ExportColumn
{
    /// <summary>Создаёт столбец экспортируемой таблицы.</summary>
    /// <param name="name">Заголовок столбца.</param>
    /// <param name="type">Тип столбца; по умолчанию тип определяется по значениям.</param>
    public ExportColumn(string name, ExportColumnType type = ExportColumnType.Auto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Type = type;
    }

    /// <summary>Заголовок столбца.</summary>
    public string Name { get; }

    /// <summary>Тип столбца.</summary>
    public ExportColumnType Type { get; }
}

/// <summary>
/// Таблица, подготовленная к экспорту в Excel (ТЗ §8.5, функции 61 и 69).
/// </summary>
/// <remarks>
/// Модель не зависит от UI: её используют как адаптер представления грида
/// (<see cref="SpreadsheetMlExporter.ExportGridToExcel"/>), так и модульные
/// тесты, не создающие элементов управления WinForms.
/// </remarks>
public sealed class ExportTable
{
    /// <summary>Создаёт таблицу для экспорта.</summary>
    /// <param name="columns">Столбцы таблицы.</param>
    /// <param name="rows">Строки таблицы.</param>
    public ExportTable(IEnumerable<ExportColumn> columns, IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        Columns = columns.ToArray();
        Rows = rows.Select(row => (IReadOnlyList<object?>)row.ToArray()).ToArray();
    }

    /// <summary>Столбцы таблицы.</summary>
    public IReadOnlyList<ExportColumn> Columns { get; }

    /// <summary>Строки таблицы.</summary>
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }

    /// <summary>Строит модель экспорта по таблице данных ADO.NET.</summary>
    /// <param name="table">Таблица данных.</param>
    /// <returns>Модель экспорта.</returns>
    public static ExportTable FromDataTable(DataTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var columns = table.Columns
            .Cast<DataColumn>()
            .Select(column => new ExportColumn(column.ColumnName, MapColumnType(column.DataType)))
            .ToArray();

        var rows = table.Rows
            .Cast<DataRow>()
            .Select(row => (IReadOnlyList<object?>)row.ItemArray)
            .ToArray();

        return new ExportTable(columns, rows);
    }

    /// <summary>Строит модель экспорта по представлению грида.</summary>
    /// <remarks>
    /// Тип столбца определяется по его значениям: столбец, все значения
    /// которого числовые, экспортируется числами, даже если грид хранит их
    /// текстом (ТЗ §8.5, функция 69: числовые колонки импортируются в Excel
    /// как числа, а не как текст).
    /// </remarks>
    /// <param name="grid">Представление грида.</param>
    /// <returns>Модель экспорта.</returns>
    public static ExportTable FromDataGridView(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var columnCount = grid.Columns.Count;

        var columns = new List<ExportColumn>();
        var rows = new List<IReadOnlyList<object?>>();

        foreach (DataGridViewRow row in grid.Rows)
        {
            // Строка-шаблон добавления не содержит данных.
            if (row.IsNewRow)
            {
                continue;
            }

            var values = new object?[columnCount];

            for (var index = 0; index < columnCount; index++)
            {
                values[index] = row.Cells[index].Value;
            }

            rows.Add(values);
        }

        for (var index = 0; index < columnCount; index++)
        {
            var column = grid.Columns[index];

            var name = string.IsNullOrWhiteSpace(column.HeaderText)
                ? column.Name
                : column.HeaderText;

            var values = rows.Select(row => row[index]).ToArray();

            columns.Add(new ExportColumn(name, DetectColumnType(values)));
        }

        return new ExportTable(columns, rows);
    }

    /// <summary>Определяет тип столбца по значениям его ячеек.</summary>
    /// <param name="values">Значения ячеек столбца.</param>
    /// <returns>Тип столбца экспорта.</returns>
    private static ExportColumnType DetectColumnType(IReadOnlyList<object?> values)
    {
        var hasValues = false;
        var allNumbers = true;
        var allDates = true;

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            hasValues = true;

            var isNumber = value is not bool && SpreadsheetMlExporter.TryFormatNumber(value, out _);
            var isDate = value is DateTime or DateTimeOffset;

            if (!isNumber)
            {
                allNumbers = false;
            }

            if (!isDate)
            {
                allDates = false;
            }

            if (!allNumbers && !allDates)
            {
                break;
            }
        }

        if (!hasValues)
        {
            return ExportColumnType.Auto;
        }

        if (allNumbers)
        {
            return ExportColumnType.Number;
        }

        if (allDates)
        {
            return ExportColumnType.Date;
        }

        // Смешанный столбец: тип определяется по значению каждой ячейки.
        return ExportColumnType.Auto;
    }

    /// <summary>Отображает тип данных столбца в тип экспорта.</summary>
    /// <param name="dataType">Тип данных столбца.</param>
    /// <returns>Тип столбца экспорта.</returns>
    private static ExportColumnType MapColumnType(Type dataType)
    {
        if (dataType == typeof(DateTime) || dataType == typeof(DateTimeOffset))
        {
            return ExportColumnType.Date;
        }

        if (dataType == typeof(byte)
            || dataType == typeof(sbyte)
            || dataType == typeof(short)
            || dataType == typeof(ushort)
            || dataType == typeof(int)
            || dataType == typeof(uint)
            || dataType == typeof(long)
            || dataType == typeof(ulong)
            || dataType == typeof(float)
            || dataType == typeof(double)
            || dataType == typeof(decimal))
        {
            return ExportColumnType.Number;
        }

        return ExportColumnType.Text;
    }
}

/// <summary>
/// Экспорт в Microsoft Excel (XLSX) без установленного MS Office
/// (ТЗ §8.5, функция 69 — «Экспорт в Microsoft Excel без установленного MS Office»).
/// </summary>
/// <remarks>
/// <para>
/// XLSX — это пакет Open Packaging Conventions: ZIP-архив с картой типов
/// содержимого <c>[Content_Types].xml</c>, связями <c>_rels/.rels</c>,
/// книгой <c>xl/workbook.xml</c>, листом <c>xl/worksheets/sheet1.xml</c>,
/// таблицей общих строк <c>xl/sharedStrings.xml</c> и стилями
/// <c>xl/styles.xml</c>. Пакет собирается штатным
/// <see cref="NativeOpenXmlPackager"/> (ТЗ §8.5, функция 70) на базе
/// <c>System.IO.Compression</c>: ни OLE, ни COM, ни Interop-библиотеки
/// Microsoft Office не используются.
/// </para>
/// <para>
/// Числовые значения записываются числовыми ячейками (тип «n» — атрибут
/// <c>t</c> не выставляется), текстовые — таблицей общих строк. Поэтому
/// числовые столбцы открываются в Excel как числа, а не как текст.
/// </para>
/// </remarks>
public sealed class SpreadsheetMlExporter
{
    /// <summary>Индекс стиля ячейки: полужирный заголовок.</summary>
    private const int HeaderStyleIndex = 1;

    /// <summary>Индекс стиля ячейки: дата и время.</summary>
    private const int DateStyleIndex = 2;

    /// <summary>
    /// Культуры разбора числового текста: инвариантная («1234.56») и русская
    /// («1234,56», «1 234,56»).
    /// </summary>
    private static readonly CultureInfo[] ЧисловыеКультуры =
    [
        CultureInfo.InvariantCulture,
        CultureInfo.GetCultureInfo("ru-RU"),
    ];

    /// <summary>
    /// Наборы правил разбора числового текста: без разделителей групп
    /// (инвариантный «точка») и с разделителями групп (русский «запятая»).
    /// </summary>
    private static readonly NumberStyles[] ЧисловыеСтили =
    [
        NumberStyles.Float,
        NumberStyles.Number,
    ];

    /// <summary>Карта типов содержимого пакета XLSX.</summary>
    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    /// <summary>Связи пакета XLSX: корневая книга.</summary>
    private const string PackageRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    /// <summary>Связи книги: лист, таблица общих строк и стили.</summary>
    private const string WorkbookRelationshipsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    /// <summary>Книга XLSX со ссылкой на лист.</summary>
    private const string WorkbookXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets>
            <sheet name="Лист1" sheetId="1" r:id="rId1"/>
          </sheets>
        </workbook>
        """;

    /// <summary>Стили книги: обычный, полужирный заголовок и формат даты.</summary>
    private const string StylesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="1">
            <numFmt numFmtId="164" formatCode="yyyy\-mm\-dd\ hh:mm:ss"/>
          </numFmts>
          <fonts count="2">
            <font>
              <sz val="11"/>
              <name val="Calibri"/>
            </font>
            <font>
              <b/>
              <sz val="11"/>
              <name val="Calibri"/>
            </font>
          </fonts>
          <fills count="2">
            <fill>
              <patternFill patternType="none"/>
            </fill>
            <fill>
              <patternFill patternType="gray125"/>
            </fill>
          </fills>
          <borders count="1">
            <border>
              <left/>
              <right/>
              <top/>
              <bottom/>
              <diagonal/>
            </border>
          </borders>
          <cellStyleXfs count="1">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
          </cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
          </cellXfs>
          <cellStyles count="1">
            <cellStyle name="Normal" xfId="0" builtinId="0"/>
          </cellStyles>
        </styleSheet>
        """;

    /// <summary>Упаковщик пакетов OPC.</summary>
    private readonly NativeOpenXmlPackager _packager;

    /// <summary>Создаёт экспортёр XLSX.</summary>
    /// <param name="packager">Упаковщик пакетов OPC; по умолчанию новый.</param>
    public SpreadsheetMlExporter(NativeOpenXmlPackager? packager = null)
    {
        _packager = packager ?? new NativeOpenXmlPackager();
    }

    /// <summary>
    /// Формирует файл XLSX по таблице данных (ТЗ §8.5, функция 69:
    /// <c>public void ExportToXlsx(DataTable table, string outputXlsxPath)</c>).
    /// </summary>
    /// <param name="table">Таблица данных.</param>
    /// <param name="outputXlsxPath">Путь к целевому файлу XLSX.</param>
    public void ExportToXlsx(DataTable table, string outputXlsxPath)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputXlsxPath);

        ExportToXlsx(ExportTable.FromDataTable(table), outputXlsxPath);
    }

    /// <summary>
    /// Формирует файл XLSX по модели таблицы без обращения к UI
    /// (ТЗ §8.5, функция 69).
    /// </summary>
    /// <param name="table">Модель экспортируемой таблицы.</param>
    /// <param name="outputXlsxPath">Путь к целевому файлу XLSX.</param>
    public void ExportToXlsx(ExportTable table, string outputXlsxPath)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputXlsxPath);

        var parts = BuildPackage(table);

        _packager.WritePackage(outputXlsxPath, parts);
    }

    /// <summary>
    /// Формирует файл XLSX по модели таблицы в поток (ТЗ §8.5, функция 69).
    /// </summary>
    /// <param name="table">Модель экспортируемой таблицы.</param>
    /// <param name="output">Целевой поток; остаётся открытым после записи.</param>
    public void ExportToXlsx(ExportTable table, Stream output)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(output);

        var parts = BuildPackage(table);

        _packager.WritePackageToStream(output, parts);
    }

    /// <summary>
    /// Прямой экспорт текущей таблицы в MS Excel без OLE и COM
    /// (ТЗ §8.5, функция 61: <c>public void ExportGridToExcel(DataGridView grid, string filePath)</c>).
    /// </summary>
    /// <remarks>
    /// Файл формируется потоком как пакет XLSX; открытие файла в
    /// ассоциированном приложении выполняет слой интерфейса вызовом
    /// <see cref="OpenInAssociatedApplication"/>. Приложение не требует
    /// установленного Microsoft Office: чтение грида используется только как
    /// источник данных.
    /// </remarks>
    /// <param name="grid">Представление грида.</param>
    /// <param name="filePath">Путь к целевому файлу XLSX.</param>
    /// <exception cref="IOException">Файл заблокирован другой программой.</exception>
    public void ExportGridToExcel(DataGridView grid, string filePath)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        ExportToXlsx(ExportTable.FromDataGridView(grid), filePath);
    }

    /// <summary>
    /// Открывает файл в ассоциированном приложении (ТЗ §8.5, функция 61:
    /// «открывает файл в ассоциированном приложении»).
    /// </summary>
    /// <remarks>
    /// Запуск выполняется оболочкой Windows через ассоциацию расширения файла:
    /// ни COM, ни Interop-библиотеки Microsoft Office не используются. На сервере
    /// без Desktop Experience метод не вызывается.
    /// </remarks>
    /// <param name="filePath">Путь к открываемому файлу.</param>
    public void OpenInAssociatedApplication(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Файл не найден: {filePath}", filePath);
        }

        using var process = Process.Start(new ProcessStartInfo(filePath)
        {
            UseShellExecute = true,
        });
    }

    /// <summary>Формирует части пакета XLSX.</summary>
    /// <param name="table">Модель экспортируемой таблицы.</param>
    /// <returns>Части пакета в порядке записи.</returns>
    private static IReadOnlyList<OpenXmlPackagePart> BuildPackage(ExportTable table)
    {
        var strings = new SharedStringsCollector();

        var sheetXml = BuildSheetXml(table, strings);

        var sharedStringsXml = BuildSharedStringsXml(strings);

        return new List<OpenXmlPackagePart>
        {
            new(NativeOpenXmlPackager.ContentTypesPartName, ContentTypesXml),
            new(NativeOpenXmlPackager.PackageRelationshipsPartName, PackageRelationshipsXml),
            new("xl/workbook.xml", WorkbookXml),
            new("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml),
            new("xl/worksheets/sheet1.xml", sheetXml),
            new("xl/sharedStrings.xml", sharedStringsXml),
            new("xl/styles.xml", StylesXml),
        };
    }

    /// <summary>Формирует часть листа книги.</summary>
    /// <param name="table">Модель экспортируемой таблицы.</param>
    /// <param name="strings">Сборщик таблицы общих строк.</param>
    /// <returns>XML части листа SpreadsheetML.</returns>
    private static string BuildSheetXml(ExportTable table, SharedStringsCollector strings)
    {
        var builder = new StringBuilder();

        var columnCount = table.Columns.Count;
        var rowCount = table.Rows.Count + 1;

        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        builder.Append(CultureInfo.InvariantCulture, $"<dimension ref=\"A1:{ColumnReference(columnCount, rowCount)}\"/>");
        builder.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
        builder.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

        if (columnCount > 0)
        {
            builder.Append("<cols>");

            for (var index = 0; index < columnCount; index++)
            {
                var width = Math.Clamp(table.Columns[index].Name.Length + 2, 10, 60);

                builder.Append(CultureInfo.InvariantCulture, $"<col min=\"{index + 1}\" max=\"{index + 1}\" width=\"{width}\" customWidth=\"1\"/>");
            }

            builder.Append("</cols>");
        }

        builder.Append("<sheetData>");

        // Строка заголовков столбцов.
        builder.Append("<row r=\"1\">");

        for (var index = 0; index < columnCount; index++)
        {
            var reference = ColumnReference(index + 1, 1);
            var value = strings.GetOrAdd(table.Columns[index].Name);

            builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{HeaderStyleIndex}\" t=\"s\"><v>{value}</v></c>");
        }

        builder.Append("</row>");

        // Строки данных.
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 2;

            builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowNumber}\">");

            var row = table.Rows[rowIndex];

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var value = columnIndex < row.Count ? row[columnIndex] : null;
                var reference = ColumnReference(columnIndex + 1, rowNumber);

                AppendCell(builder, strings, table.Columns[columnIndex], value, reference);
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData>");
        builder.Append("</worksheet>");

        return builder.ToString();
    }

    /// <summary>Добавляет ячейку листа.</summary>
    /// <param name="builder">Сборщик XML листа.</param>
    /// <param name="strings">Сборщик таблицы общих строк.</param>
    /// <param name="column">Столбец таблицы.</param>
    /// <param name="value">Значение ячейки.</param>
    /// <param name="reference">Адрес ячейки (например, «B2»).</param>
    private static void AppendCell(
        StringBuilder builder,
        SharedStringsCollector strings,
        ExportColumn column,
        object? value,
        string reference)
    {
        switch (ResolveCellKind(column.Type, value))
        {
            case ExportCellKind.Number:
                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\"><v>{FormatNumber(value!)}</v></c>");
                break;

            case ExportCellKind.Date:
                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" s=\"{DateStyleIndex}\"><v>{FormatDate(value!)}</v></c>");
                break;

            case ExportCellKind.Boolean:
                var flag = (bool)value! ? 1 : 0;

                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"b\"><v>{flag}</v></c>");
                break;

            default:
                var index = strings.GetOrAdd(FormatText(value));

                builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"s\"><v>{index}</v></c>");
                break;
        }
    }

    /// <summary>Определяет вид ячейки по типу столбца и значению.</summary>
    /// <param name="columnType">Тип столбца.</param>
    /// <param name="value">Значение ячейки.</param>
    /// <returns>Вид ячейки.</returns>
    private static ExportCellKind ResolveCellKind(ExportColumnType columnType, object? value)
    {
        if (value is null)
        {
            return ExportCellKind.Text;
        }

        switch (columnType)
        {
            case ExportColumnType.Number:
                return TryFormatNumber(value, out _) ? ExportCellKind.Number : ExportCellKind.Text;

            case ExportColumnType.Date:
                return TryFormatDate(value, out _) ? ExportCellKind.Date : ExportCellKind.Text;

            case ExportColumnType.Text:
                return ExportCellKind.Text;

            default:
                break;
        }

        if (value is bool)
        {
            return ExportCellKind.Boolean;
        }

        if (value is DateTime or DateTimeOffset)
        {
            return ExportCellKind.Date;
        }

        if (IsNumericType(value.GetType()))
        {
            return ExportCellKind.Number;
        }

        return ExportCellKind.Text;
    }

    /// <summary>Признак числового типа данных.</summary>
    /// <param name="type">Тип данных.</param>
    /// <returns><see langword="true"/>, если тип числовой.</returns>
    private static bool IsNumericType(Type type) =>
        type == typeof(byte)
        || type == typeof(sbyte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong)
        || type == typeof(float)
        || type == typeof(double)
        || type == typeof(decimal);

    /// <summary>Форматирует значение как число в инвариантном формате.</summary>
    /// <param name="value">Значение ячейки.</param>
    /// <returns>Текст числа с точкой в качестве разделителя дробной части.</returns>
    private static string FormatNumber(object value) =>
        TryFormatNumber(value, out var text)
            ? text
            : "0";

    /// <summary>Пытается представить значение числом.</summary>
    /// <param name="value">Значение ячейки.</param>
    /// <param name="text">Текст числа в инвариантном формате.</param>
    /// <returns><see langword="true"/>, если значение числовое.</returns>
    internal static bool TryFormatNumber(object value, out string text)
    {
        text = "0";

        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                return true;

            case float or double or decimal:
                text = Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .ToString("G15", CultureInfo.InvariantCulture);
                return true;

            case string textValue:
                return TryParseNumberText(textValue, out text);

            default:
                return false;
        }
    }

    /// <summary>
    /// Разбирает текстовое значение как число.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Разделитель дробной части определяется по двум культурам: инвариантной
    /// («1234.56») и русской («1234,56», «1 234,56»). Флаг AllowThousands в
    /// инвариантной культуре не применяется: при нём «1234,56» разбирается как
    /// 123456, то есть денежная сумма искажается без предупреждения.
    /// </para>
    /// <para>
    /// Числом считается только значение, цифровая запись которого совпадает с
    /// канонической формой разобранного числа: иначе «012345» (почтовый
    /// индекс) превратилось бы в 12345, то есть потерялись бы ведущие нули.
    /// </para>
    /// </remarks>
    /// <param name="textValue">Текстовое значение ячейки.</param>
    /// <param name="text">Текст числа в инвариантном формате.</param>
    /// <returns><see langword="true"/>, если значение числовое.</returns>
    private static bool TryParseNumberText(string textValue, out string text)
    {
        text = "0";

        var значение = textValue.Trim();

        if (значение.Length == 0)
        {
            return false;
        }

        foreach (var культура in ЧисловыеКультуры)
        {
            foreach (var стили in ЧисловыеСтили)
            {
                if (!decimal.TryParse(значение, стили, культура, out var число))
                {
                    continue;
                }

                if (!СовпадаетПоЦифрам(значение, число))
                {
                    continue;
                }

                text = число.ToString("G15", CultureInfo.InvariantCulture);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Сравнивает исходную запись значения с канонической записью числа
    /// без учёта разделителей групп и знака.
    /// </summary>
    /// <param name="исходное">Исходный текст значения.</param>
    /// <param name="число">Разобранное число.</param>
    /// <returns><see langword="true"/>, если записи совпадают по цифрам.</returns>
    private static bool СовпадаетПоЦифрам(string исходное, decimal число)
    {
        var эталон = число.ToString("0.############################", CultureInfo.InvariantCulture);

        var цифрыИсходного = ЦифроваяФорма(исходное);
        var цифрыЭталона = ЦифроваяФорма(эталон);

        return цифрыИсходного.Length > 0
            && цифрыИсходного == цифрыЭталона;
    }

    /// <summary>
    /// Приводит запись числа к каноническому виду: оставляет цифры и один
    /// разделитель дробной части, отбрасывает разделители групп и знак.
    /// </summary>
    /// <remarks>
    /// Ведущие нули целой части сохраняются — по ним «012345» отличается от
    /// 12345, и почтовый индекс не должен терять нули. Нули дробной части
    /// отбрасываются: «789,10» и 789,1 — одно и то же число.
    /// </remarks>
    /// <param name="значение">Проверяемая запись.</param>
    /// <returns>Нормализованная запись либо пустая строка, если запись не является числом.</returns>
    private static string ЦифроваяФорма(string значение)
    {
        var целая = new StringBuilder();
        var дробная = new StringBuilder();
        var разделительВстречался = false;
        var вДробнойЧасти = false;

        foreach (var символ in значение)
        {
            if (char.IsAsciiDigit(символ))
            {
                if (вДробнойЧасти)
                {
                    дробная.Append(символ);
                }
                else
                {
                    целая.Append(символ);
                }

                continue;
            }

            if (символ is ',' or '.')
            {
                if (разделительВстречался)
                {
                    return string.Empty;
                }

                разделительВстречался = true;
                вДробнойЧасти = true;
                continue;
            }

            if (char.IsWhiteSpace(символ) || символ is '+' or '-' or '_')
            {
                continue;
            }

            return string.Empty;
        }

        if (целая.Length == 0)
        {
            return string.Empty;
        }

        var хвост = дробная.ToString().TrimEnd('0');

        return хвост.Length == 0 ? целая.ToString() : целая + "." + хвост;
    }

    /// <summary>Форматирует значение как дату в серийном виде Excel.</summary>
    /// <param name="value">Значение ячейки.</param>
    /// <returns>Серийный номер даты.</returns>
    private static string FormatDate(object value) =>
        TryFormatDate(value, out var serial)
            ? serial
            : "0";

    /// <summary>Пытается представить значение датой.</summary>
    /// <param name="value">Значение ячейки.</param>
    /// <param name="serial">Серийный номер даты в формате Excel.</param>
    /// <returns><see langword="true"/>, если значение распознано как дата.</returns>
    internal static bool TryFormatDate(object value, out string serial)
    {
        serial = "0";

        switch (value)
        {
            case DateTime dateTime:
                serial = dateTime.ToOADate()
                    .ToString("R", CultureInfo.InvariantCulture);
                return true;

            case DateTimeOffset dateTimeOffset:
                serial = dateTimeOffset.DateTime.ToOADate()
                    .ToString("R", CultureInfo.InvariantCulture);
                return true;

            case string textValue:
                if (DateTime.TryParse(
                        textValue,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsed)
                    || DateTime.TryParse(
                        textValue,
                        CultureInfo.GetCultureInfo("ru-RU"),
                        DateTimeStyles.None,
                        out parsed))
                {
                    serial = parsed.ToOADate().ToString("R", CultureInfo.InvariantCulture);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>Форматирует значение как текст.</summary>
    /// <param name="value">Значение ячейки.</param>
    /// <returns>Текст ячейки.</returns>
    private static string FormatText(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Формирует часть таблицы общих строк.</summary>
    /// <param name="strings">Сборщик таблицы общих строк.</param>
    /// <returns>XML части таблицы общих строк SpreadsheetML.</returns>
    private static string BuildSharedStringsXml(SharedStringsCollector strings)
    {
        var builder = new StringBuilder();

        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
        builder.Append(CultureInfo.InvariantCulture, $"count=\"{strings.Count}\" uniqueCount=\"{strings.Count}\">");

        foreach (var value in strings.Values)
        {
            builder.Append("<si><t xml:space=\"preserve\">");
            builder.Append(EscapeXml(value));
            builder.Append("</t></si>");
        }

        builder.Append("</sst>");

        return builder.ToString();
    }

    /// <summary>Экранирует текст для XML.</summary>
    /// <param name="value">Исходный текст.</param>
    /// <returns>Экранированный текст.</returns>
    private static string EscapeXml(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var symbol in value)
        {
            switch (symbol)
            {
                case '&':
                    builder.Append("&amp;");
                    break;

                case '<':
                    builder.Append("&lt;");
                    break;

                case '>':
                    builder.Append("&gt;");
                    break;

                case '"':
                    builder.Append("&quot;");
                    break;

                case '\'':
                    builder.Append("&apos;");
                    break;

                default:
                    // Управляющие символы, запрещённые в XML, отбрасываются.
                    if (symbol < 0x20 && symbol is not ('\t' or '\n' or '\r'))
                    {
                        break;
                    }

                    builder.Append(symbol);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Формирует адрес ячейки (например, «B2»).</summary>
    /// <param name="column">Номер столбца, начиная с 1.</param>
    /// <param name="row">Номер строки, начиная с 1.</param>
    /// <returns>Адрес ячейки.</returns>
    private static string ColumnReference(int column, int row)
    {
        var name = string.Empty;

        var value = column;

        while (value > 0)
        {
            var remainder = (value - 1) % 26;

            name = (char)('A' + remainder) + name;

            value = (value - 1) / 26;
        }

        return name + row.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Вид ячейки листа Excel.</summary>
    private enum ExportCellKind
    {
        /// <summary>Текстовая ячейка (таблица общих строк).</summary>
        Text,

        /// <summary>Числовая ячейка.</summary>
        Number,

        /// <summary>Ячейка с датой (число в формате даты).</summary>
        Date,

        /// <summary>Логическая ячейка.</summary>
        Boolean,
    }

    /// <summary>Сборщик таблицы общих строк SpreadsheetML.</summary>
    private sealed class SharedStringsCollector
    {
        /// <summary>Индексы строк.</summary>
        private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

        /// <summary>Строки в порядке добавления.</summary>
        private readonly List<string> _values = new();

        /// <summary>Число строк в таблице общих строк.</summary>
        public int Count => _values.Count;

        /// <summary>Строки таблицы.</summary>
        public IReadOnlyList<string> Values => _values;

        /// <summary>Возвращает индекс строки, добавляя её при необходимости.</summary>
        /// <param name="text">Текст строки.</param>
        /// <returns>Индекс строки в таблице общих строк.</returns>
        public int GetOrAdd(string text)
        {
            if (_indexes.TryGetValue(text, out var index))
            {
                return index;
            }

            index = _values.Count;

            _values.Add(text);
            _indexes[text] = index;

            return index;
        }
    }
}
