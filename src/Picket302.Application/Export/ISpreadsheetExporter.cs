using System.Globalization;

namespace Picket302.Application.Export;

/// <summary>
/// Абстракция выгрузки таблицы в книгу Excel (ТЗ §8.5, функция 71;
/// ТЗ §8.12, функция 160).
/// </summary>
/// <remarks>
/// Слой приложения не ссылается на слой инфраструктуры (ТЗ §2.1), поэтому
/// конкретный писатель книги объявляется интерфейсом и внедряется в
/// <see cref="Picket302.Application.Services.UniversalAddressExchange"/>.
/// В приложении доступна собственная реализация без Office —
/// <see cref="OpenXmlSpreadsheetExporter"/>.
/// </remarks>
public interface ISpreadsheetExporter
{
    /// <summary>Записывает таблицу в поток в формате книги Excel (XLSX).</summary>
    /// <param name="table">Выгружаемая таблица.</param>
    /// <param name="output">Целевой поток; остаётся открытым после записи.</param>
    void Write(SpreadsheetTable table, Stream output);
}

/// <summary>
/// Таблица выгрузки: заголовки столбцов и строки значений (ТЗ §8.5, функция 71).
/// </summary>
/// <remarks>
/// Таблица не зависит от формата: один и тот же объект выводится в CSV, HTML,
/// RTF и XLSX. Значение ячейки — текст, число или дата.
/// </remarks>
public sealed class SpreadsheetTable
{
    /// <summary>Предельная длина имени листа книги Excel.</summary>
    public const int MaxSheetNameLength = 31;

    /// <summary>Формат представления дат по умолчанию.</summary>
    public const string DefaultDateFormat = "yyyy-MM-dd";

    /// <summary>Создаёт таблицу выгрузки.</summary>
    /// <param name="sheetName">Имя листа книги Excel.</param>
    /// <param name="headers">Заголовки столбцов.</param>
    /// <param name="rows">Строки таблицы.</param>
    /// <param name="dateFormat">
    /// Формат представления дат в таблице; при пустом значении применяется
    /// формат <see cref="DefaultDateFormat"/>.
    /// </param>
    /// <param name="includeHeader">
    /// Писать ли строку заголовков (F-D10): прежде XLSX-писатель игнорировал
    /// признак и всегда выводил заголовки.
    /// </param>
    public SpreadsheetTable(
        string sheetName,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<SpreadsheetCell>> rows,
        string? dateFormat = null,
        bool includeHeader = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        SheetName = НормализоватьИмяЛиста(sheetName);
        Headers = headers;
        Rows = rows;
        DateFormat = string.IsNullOrWhiteSpace(dateFormat) ? DefaultDateFormat : dateFormat;
        IncludeHeader = includeHeader;
    }

    /// <summary>Имя листа книги Excel.</summary>
    public string SheetName { get; }

    /// <summary>Заголовки столбцов.</summary>
    public IReadOnlyList<string> Headers { get; }

    /// <summary>Признак вывода строки заголовков (F-D10).</summary>
    public bool IncludeHeader { get; }

    /// <summary>
    /// Возвращает копию таблицы с переопределённым признаком вывода заголовков
    /// (F-D10).
    /// </summary>
    /// <param name="includeHeader">Новый признак.</param>
    /// <returns>Копия таблицы.</returns>
    public SpreadsheetTable СПризнакомЗаголовка(bool includeHeader) => new(
        SheetName,
        Headers,
        Rows,
        DateFormat,
        includeHeader);

    /// <summary>Строки таблицы.</summary>
    public IReadOnlyList<IReadOnlyList<SpreadsheetCell>> Rows { get; }

    /// <summary>Формат представления дат в таблице.</summary>
    public string DateFormat { get; }

    /// <summary>Число столбцов таблицы.</summary>
    public int ColumnCount => Headers.Count;

    /// <summary>Число строк данных таблицы.</summary>
    public int RowCount => Rows.Count;

    /// <summary>
    /// Создаёт таблицу из строк обычных значений: строки, числа и даты
    /// преобразуются в ячейки соответствующего типа.
    /// </summary>
    /// <param name="sheetName">Имя листа книги Excel.</param>
    /// <param name="headers">Заголовки столбцов.</param>
    /// <param name="rows">Строки таблицы.</param>
    /// <returns>Таблица выгрузки.</returns>
    public static SpreadsheetTable Create(
        string sheetName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var ячейки = new List<IReadOnlyList<SpreadsheetCell>>();

        foreach (var строка in rows)
        {
            ArgumentNullException.ThrowIfNull(строка);

            var значения = new List<SpreadsheetCell>(строка.Count);

            foreach (var значение in строка)
            {
                значения.Add(SpreadsheetCell.FromValue(значение));
            }

            ячейки.Add(значения);
        }

        return new SpreadsheetTable(sheetName, headers, ячейки);
    }

    /// <summary>
    /// Приводит имя листа к допустимому для книги Excel: убирает
    /// запрещённые символы и ограничивает длину.
    /// </summary>
    /// <param name="sheetName">Исходное имя листа.</param>
    /// <returns>Допустимое имя листа.</returns>
    private static string НормализоватьИмяЛиста(string sheetName)
    {
        var символы = sheetName
            .Select(символ => ЗапрещённыеСимволыЛиста.Contains(символ) ? '_' : символ)
            .ToArray();

        var имя = new string(символы).Trim();

        if (имя.Length == 0)
        {
            имя = "Лист1";
        }

        return имя.Length > MaxSheetNameLength ? имя[..MaxSheetNameLength] : имя;
    }

    /// <summary>Символы, запрещённые в имени листа книги Excel.</summary>
    private static readonly char[] ЗапрещённыеСимволыЛиста = ['\\', '/', ':', '*', '?', '[', ']'];
}

/// <summary>
/// Ячейка таблицы выгрузки (ТЗ §8.5, функция 71).
/// </summary>
/// <remarks>
/// Допустимые значения: текст, число (<see cref="decimal"/>) и дата
/// (<see cref="DateTime"/>). Пустая ячейка не содержит значения.
/// </remarks>
public sealed class SpreadsheetCell
{
    private SpreadsheetCell(object? value)
    {
        Value = value;
    }

    /// <summary>Значение ячейки; <see langword="null"/> — ячейка пустая.</summary>
    public object? Value { get; }

    /// <summary>Признак пустой ячейки.</summary>
    public bool IsEmpty => Value is null;

    /// <summary>Признак текстовой ячейки.</summary>
    public bool IsText => Value is string;

    /// <summary>Признак числовой ячейки.</summary>
    public bool IsNumber => Value is decimal;

    /// <summary>Признак ячейки с датой.</summary>
    public bool IsDate => Value is DateTime;

    /// <summary>Пустая ячейка.</summary>
    public static SpreadsheetCell Empty { get; } = new(null);

    /// <summary>Создаёт текстовую ячейку.</summary>
    /// <param name="value">Текст значения.</param>
    /// <returns>Ячейка с текстом.</returns>
    public static SpreadsheetCell Text(string? value) => new(value ?? string.Empty);

    /// <summary>Создаёт числовую ячейку.</summary>
    /// <param name="value">Числовое значение.</param>
    /// <returns>Ячейка с числом.</returns>
    public static SpreadsheetCell Number(decimal value) => new(value);

    /// <summary>Создаёт ячейку с датой.</summary>
    /// <param name="value">Значение даты.</param>
    /// <returns>Ячейка с датой.</returns>
    public static SpreadsheetCell Date(DateTime value) => new(value);

    /// <summary>Создаёт ячейку из обычного значения.</summary>
    /// <param name="value">Значение ячейки.</param>
    /// <returns>Ячейка соответствующего типа.</returns>
    /// <exception cref="ArgumentException">Тип значения не поддерживается.</exception>
    public static SpreadsheetCell FromValue(object? value) => value switch
    {
        null => Empty,
        string текст => Text(текст),
        decimal число => Number(число),
        int целое => Number(целое),
        long длинное => Number(длинное),
        double вещественное => Number((decimal)вещественное),
        DateTime дата => Date(дата),
        _ => throw new ArgumentException(
            $"Тип значения «{value.GetType().Name}» не поддерживается таблицей выгрузки.",
            nameof(value)),
    };

    /// <summary>
    /// Возвращает текстовое представление значения ячейки.
    /// </summary>
    /// <param name="dateFormat">
    /// Формат даты для текстовых форматов выгрузки; при <see langword="null"/>
    /// применяется ISO-формат <c>yyyy-MM-dd</c>.
    /// </param>
    /// <returns>Текст значения ячейки.</returns>
    public string AsText(string? dateFormat = null) => Value switch
    {
        null => string.Empty,
        string текст => текст,
        decimal число => число.ToString(CultureInfo.InvariantCulture),
        DateTime дата => дата.ToString(dateFormat ?? "yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Value.ToString() ?? string.Empty,
    };
}
