using System.Text;

using Picket302.Domain.Entities;

namespace Picket302.Domain.Contracts;

/// <summary>
/// Формат универсального обмена адресной книгой (ТЗ §8.12, функция 71).
/// </summary>
public enum ExchangeFormat
{
    /// <summary>Текстовый файл с разделителями.</summary>
    Txt = 0,

    /// <summary>Текст с разделителями (comma/semicolon/tab).</summary>
    Csv = 1,

    /// <summary>HTML-документ.</summary>
    Html = 2,

    /// <summary>Формат Rich Text Format.</summary>
    Rtf = 3,

    /// <summary>Книга Excel в формате XLSX (OpenXML).</summary>
    Xls = 4,

    /// <summary>Книга Excel в формате XLSX (OpenXML), синоним <see cref="ExchangeFormat.Xls"/>.</summary>
    Xlsx = 5,
}

/// <summary>
/// Действие, выполняемое после завершения выгрузки (ТЗ §8.12, функция 160).
/// </summary>
public enum ExportPostAction
{
    /// <summary>Ничего не выполнять.</summary>
    None = 0,

    /// <summary>Открыть выгруженный файл в приложении по умолчанию.</summary>
    OpenFile = 1,

    /// <summary>Открыть папку с выгруженным файлом.</summary>
    OpenFolder = 2,
}

/// <summary>
/// Формат представления дат в выгрузке (ТЗ §8.12, функция 160).
/// </summary>
public enum ExportDateFormat
{
    /// <summary>ISO 8601: <c>yyyy-MM-dd</c>.</summary>
    Iso = 0,

    /// <summary>Точки: <c>dd.MM.yyyy</c>.</summary>
    DotSeparated = 1,

    /// <summary>Слэши: <c>dd/MM/yyyy</c>.</summary>
    SlashSeparated = 2,

    /// <summary>Только время: <c>HH:mm:ss</c>.</summary>
    TimeOnly = 3,

    /// <summary>Пользовательский формат из свойства <see cref="ExportRequest.CustomDateFormat"/>.</summary>
    Custom = 4,
}

/// <summary>
/// Запрос выгрузки данных адресной книги (ТЗ §8.12, функция 160).
/// </summary>
public sealed class ExportRequest
{
    /// <summary>Формат выгрузки.</summary>
    public ExchangeFormat Format { get; set; } = ExchangeFormat.Csv;

    /// <summary>Путь итогового файла.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Формат представления дат.</summary>
    public ExportDateFormat DateFormat { get; set; } = ExportDateFormat.Iso;

    /// <summary>Пользовательский формат даты (используется при <see cref="ExportDateFormat.Custom"/>).</summary>
    public string? CustomDateFormat { get; set; }

    /// <summary>Действие после выгрузки.</summary>
    public ExportPostAction PostAction { get; set; } = ExportPostAction.None;

    /// <summary>Выгружаемые контрагенты.</summary>
    public IReadOnlyList<Counterparty> Items { get; set; } = Array.Empty<Counterparty>();

    /// <summary>Разделитель столбцов (только для CSV/TXT).</summary>
    public char Delimiter { get; set; } = ';';

    /// <summary>Кодировка выгрузки (только для CSV/TXT).</summary>
    public Encoding? Encoding { get; set; }

    /// <summary>Признак включения строки заголовков (только для CSV/TXT/XLSX).</summary>
    public bool IncludeHeader { get; set; } = true;

    /// <summary>
    /// Возвращает шаблон формата даты по настройке запроса.
    /// </summary>
    /// <returns>Шаблон .NET для <c>DateTime.ToString</c>.</returns>
    public string ResolveDateFormat() => DateFormat switch
    {
        ExportDateFormat.Iso => "yyyy-MM-dd",
        ExportDateFormat.DotSeparated => "dd.MM.yyyy",
        ExportDateFormat.SlashSeparated => "dd/MM/yyyy",
        ExportDateFormat.TimeOnly => "HH:mm:ss",
        ExportDateFormat.Custom => string.IsNullOrWhiteSpace(CustomDateFormat) ? "yyyy-MM-dd" : CustomDateFormat,
        _ => "yyyy-MM-dd",
    };
}

/// <summary>
/// Результат выгрузки (ТЗ §8.12, функция 160).
/// </summary>
/// <param name="FilePath">Путь выгруженного файла.</param>
/// <param name="RecordCount">Число выгруженных записей.</param>
/// <param name="Format">Формат выгрузки.</param>
public sealed record ExportResult(string FilePath, int RecordCount, ExchangeFormat Format);
