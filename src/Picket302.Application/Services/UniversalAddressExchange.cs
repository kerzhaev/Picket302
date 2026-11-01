using System.Globalization;
using System.Text;
using Picket302.Application.Export;
using Picket302.Application.Import;
using Picket302.Domain.Contracts;
using Picket302.Domain.Dictionaries;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Services;

/// <summary>
/// Универсальный обмен адресными списками: TXT, CSV, HTML, RTF, XLS и XLSX
/// (ТЗ §8.5, функция 71: «Импорт и экспорт адресных списков в форматах
/// TXT/CSV/HTML/RTF/XLS/XLSX»).
/// </summary>
/// <remarks>
/// <para>
/// Служба переводит список контрагентов в нейтральную таблицу и выводит её в
/// выбранном формате. Для текстовых форматов поддерживаются разделители
/// «запятая», «точка с запятой» и «табуляция», кодировки UTF-8 и CP1251.
/// </para>
/// <para>
/// Импорт читает файл согласно профилю сопоставления (ТЗ §8.12, функция 73) и
/// восстанавливает поля адресов: почтовый индекс и шесть адресных строк
/// каждого из трёх срезов адресации.
/// </para>
/// </remarks>
public sealed class UniversalAddressExchange
{
    /// <summary>Заголовки столбцов выгрузки.</summary>
    private static readonly string[] Заголовки =
    [
        "№",
        "Тип",
        "Название",
        "Сокращенное",
        "ИНН",
        "КПП",
        "ОГРН",
        "Телефон",
        "E-mail",
        "Примечание",
        "Почтовый адрес",
        "Юридический адрес",
        "Фактический адрес",
        "Дата создания",
    ];

    /// <summary>Название типа контрагента для юридического лица.</summary>
    private const string ТипЮридическогоЛица = "Юридическое лицо";

    /// <summary>Название типа контрагента для физического лица.</summary>
    private const string ТипФизическогоЛица = "Физическое лицо";

    /// <summary>Ширина столбца таблицы RTF в твипах.</summary>
    private const int ШиринаСтолбцаRtf = 2400;

    private readonly ISpreadsheetExporter _экспортТаблиц;

    /// <summary>
    /// Создаёт службу с собственным писателем книги Excel без Office.
    /// </summary>
    public UniversalAddressExchange()
        : this(new OpenXmlSpreadsheetExporter())
    {
    }

    /// <summary>
    /// Создаёт службу с указанным писателем книги Excel.
    /// </summary>
    /// <param name="экспортТаблиц">Писатель книги Excel (XLSX).</param>
    public UniversalAddressExchange(ISpreadsheetExporter экспортТаблиц)
    {
        ArgumentNullException.ThrowIfNull(экспортТаблиц);

        _экспортТаблиц = экспортТаблиц;
    }

    /// <summary>
    /// Выгружает список контрагентов в выбранный формат (ТЗ §8.5, функция 71).
    /// </summary>
    /// <param name="data">Выгружаемые контрагенты.</param>
    /// <param name="format">Формат выгрузки.</param>
    /// <param name="output">Целевой поток; остаётся открытым после записи.</param>
    public void ExportData(IEnumerable<Counterparty> data, ExchangeFormat format, Stream output)
        => ExportData(data, format, output, null);

    /// <summary>
    /// Выгружает список контрагентов в выбранный формат с заданными параметрами
    /// (ТЗ §8.5, функция 71; ТЗ §8.12, функция 160: «выбор формата файла …
    /// и настроек даты»).
    /// </summary>
    /// <param name="data">Выгружаемые контрагенты.</param>
    /// <param name="format">Формат выгрузки.</param>
    /// <param name="output">Целевой поток; остаётся открытым после записи.</param>
    /// <param name="options">
    /// Параметры выгрузки: разделитель, кодировка, строка заголовков и формат даты.
    /// </param>
    public void ExportData(
        IEnumerable<Counterparty> data,
        ExchangeFormat format,
        Stream output,
        AddressExchangeOptions? options)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);

        var параметры = options ?? new AddressExchangeOptions();
        var таблица = СобратьТаблицу(data, параметры);

        switch (format)
        {
            case ExchangeFormat.Txt:
            case ExchangeFormat.Csv:
                ЗаписатьТекст(таблица, output, параметры);
                break;

            case ExchangeFormat.Html:
                ЗаписатьHtml(таблица, output, параметры);
                break;

            case ExchangeFormat.Rtf:
                ЗаписатьRtf(таблица, output, параметры);
                break;

            case ExchangeFormat.Xls:
            case ExchangeFormat.Xlsx:
                _экспортТаблиц.Write(таблица, output);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "Неизвестный формат выгрузки адресного списка.");
        }
    }

    /// <summary>
    /// Импортирует адресный список из файла с разделителями согласно профилю
    /// сопоставления (ТЗ §8.5, функция 71; ТЗ §8.12, функция 73).
    /// </summary>
    /// <param name="input">Поток с содержимым файла.</param>
    /// <param name="profile">Профиль сопоставления столбцов.</param>
    /// <returns>Черновики контрагентов, подготовленные к проверке оператором.</returns>
    /// <exception cref="AddressExchangeImportException">
    /// Структура файла повреждена: число столбцов строки не совпадает с
    /// ожидаемым. Текст ошибки содержит номер строки.
    /// </exception>
    public IReadOnlyList<CounterpartyDraft> ImportData(Stream input, CsvProfile profile)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);

        var таблица = new DelimitedTextReader().Read(input, profile);

        return СобратьЧерновики(таблица, profile);
    }

    /// <summary>
    /// Импортирует адресный список из текста с разделителями согласно профилю
    /// сопоставления (ТЗ §8.5, функция 71).
    /// </summary>
    /// <param name="text">Текст файла с разделителями.</param>
    /// <param name="profile">Профиль сопоставления столбцов.</param>
    /// <returns>Черновики контрагентов, подготовленные к проверке оператором.</returns>
    /// <exception cref="AddressExchangeImportException">
    /// Структура файла повреждена: число столбцов строки не совпадает с
    /// ожидаемым. Текст ошибки содержит номер строки.
    /// </exception>
    public IReadOnlyList<CounterpartyDraft> ImportData(string text, CsvProfile profile)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(profile);

        var таблица = new DelimitedTextReader().Read(text, profile);

        return СобратьЧерновики(таблица, profile);
    }

    /// <summary>
    /// Импортирует адресный список из документа RTF согласно профилю
    /// сопоставления (ТЗ §8.5, функция 71: «Импорт и экспорт адресных списков
    /// в форматах TXT/CSV/HTML/RTF/XLS/XLSX»).
    /// </summary>
    /// <param name="input">Поток с содержимым документа RTF.</param>
    /// <param name="profile">Профиль сопоставления столбцов.</param>
    /// <returns>Черновики контрагентов, подготовленные к проверке оператором.</returns>
    /// <exception cref="AddressExchangeImportException">
    /// Структура файла повреждена: число столбцов строки не совпадает с
    /// ожидаемым. Текст ошибки содержит номер строки.
    /// </exception>
    public IReadOnlyList<CounterpartyDraft> ImportRtf(Stream input, CsvProfile profile)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);

        var таблица = new RtfTableReader().Read(
            input,
            profile.HasHeaderRow,
            Math.Max(1, profile.FirstDataRow));

        return СобратьЧерновики(таблица, profile);
    }

    /// <summary>
    /// Импортирует адресный список из текста документа RTF согласно профилю
    /// сопоставления (ТЗ §8.5, функция 71).
    /// </summary>
    /// <param name="rtf">Текст документа RTF.</param>
    /// <param name="profile">Профиль сопоставления столбцов.</param>
    /// <returns>Черновики контрагентов, подготовленные к проверке оператором.</returns>
    /// <exception cref="AddressExchangeImportException">
    /// Структура файла повреждена: число столбцов строки не совпадает с
    /// ожидаемым. Текст ошибки содержит номер строки.
    /// </exception>
    public IReadOnlyList<CounterpartyDraft> ImportRtf(string rtf, CsvProfile profile)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        ArgumentNullException.ThrowIfNull(profile);

        var таблица = new RtfTableReader().Read(
            rtf,
            profile.HasHeaderRow,
            Math.Max(1, profile.FirstDataRow));

        return СобратьЧерновики(таблица, profile);
    }

    /// <summary>
    /// Собирает таблицу выгрузки по списку контрагентов.
    /// </summary>
    /// <param name="data">Выгружаемые контрагенты.</param>
    /// <param name="параметры">Параметры выгрузки.</param>
    /// <returns>Нейтральная таблица выгрузки.</returns>
    private static SpreadsheetTable СобратьТаблицу(
        IEnumerable<Counterparty> data,
        AddressExchangeOptions параметры)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(параметры);

        var строки = new List<IReadOnlyList<SpreadsheetCell>>();
        var номер = 0;

        foreach (var контрагент in data)
        {
            if (контрагент is null)
            {
                continue;
            }

            номер++;

            строки.Add(
            [
                SpreadsheetCell.Number(номер),
                SpreadsheetCell.Text(НазваниеТипа(контрагент.Type)),
                SpreadsheetCell.Text(контрагент.Name),
                SpreadsheetCell.Text(контрагент.ShortName),
                SpreadsheetCell.Text(контрагент.Inn),
                SpreadsheetCell.Text(контрагент.Kpp),
                SpreadsheetCell.Text(контрагент.Ogrn),
                SpreadsheetCell.Text(контрагент.Phone),
                SpreadsheetCell.Text(контрагент.Email),
                SpreadsheetCell.Text(контрагент.SpecialNotes),
                SpreadsheetCell.Text(СнимокАдреса(контрагент.PostalAddress)),
                SpreadsheetCell.Text(СнимокАдреса(контрагент.LegalAddress)),
                SpreadsheetCell.Text(СнимокАдреса(контрагент.ActualAddress)),
                SpreadsheetCell.Date(контрагент.CreatedAt),
            ]);
        }

        return new SpreadsheetTable(параметры.SheetName, Заголовки, строки, параметры.DateFormat);
    }

    /// <summary>
    /// Возвращает название типа контрагента для выгрузки.
    /// </summary>
    /// <param name="тип">Тип контрагента.</param>
    /// <returns>Название типа контрагента.</returns>
    private static string НазваниеТипа(CounterpartyType тип) => тип switch
    {
        CounterpartyType.NaturalPerson => ТипФизическогоЛица,
        _ => ТипЮридическогоЛица,
    };

    /// <summary>
    /// Возвращает текстовый снимок адреса для выгрузки: почтовый индекс и
    /// шесть адресных строк, разделённые переводом строки.
    /// </summary>
    /// <param name="адрес">Адрес контрагента; может отсутствовать.</param>
    /// <returns>Снимок адреса либо пустая строка.</returns>
    private static string СнимокАдреса(Address? адрес) => адрес?.ToSnapshot() ?? string.Empty;

    /// <summary>
    /// Выводит таблицу в текстовый формат с разделителями (ТXT, CSV).
    /// </summary>
    /// <param name="таблица">Таблица выгрузки.</param>
    /// <param name="output">Целевой поток.</param>
    /// <param name="параметры">Параметры выгрузки.</param>
    private static void ЗаписатьТекст(
        SpreadsheetTable таблица,
        Stream output,
        AddressExchangeOptions параметры)
    {
        var кодировка = параметры.Encoding ?? TextEncodingDetector.Utf8WithBom;

        using var писатель = new StreamWriter(output, кодировка, bufferSize: 4096, leaveOpen: true);

        if (параметры.IncludeHeader)
        {
            var строка = new StringBuilder();

            for (var i = 0; i < таблица.Headers.Count; i++)
            {
                if (i > 0)
                {
                    строка.Append(параметры.Delimiter);
                }

                строка.Append(ОбернутьЗначение(таблица.Headers[i], параметры));
            }

            писатель.Write(строка.ToString());
            писатель.Write("\r\n");
        }

        foreach (var строка in таблица.Rows)
        {
            var текст = new StringBuilder();

            for (var i = 0; i < строка.Count; i++)
            {
                if (i > 0)
                {
                    текст.Append(параметры.Delimiter);
                }

                текст.Append(ОбернутьЗначение(строка[i].AsText(параметры.DateFormat), параметры));
            }

            писатель.Write(текст.ToString());
            писатель.Write("\r\n");
        }
    }

    /// <summary>
    /// Оборачивает значение в кавычки, если оно содержит разделитель, кавычку
    /// или перевод строки.
    /// </summary>
    /// <param name="значение">Значение ячейки.</param>
    /// <param name="параметры">Параметры выгрузки.</param>
    /// <returns>Значение, готовое к записи в текстовый файл.</returns>
    private static string ОбернутьЗначение(string значение, AddressExchangeOptions параметры)
    {
        var требуетсяКавычка = значение.Contains(параметры.Delimiter)
            || значение.Contains(параметры.QuoteChar)
            || значение.Contains('\n')
            || значение.Contains('\r');

        if (!требуетсяКавычка)
        {
            return значение;
        }

        var удвоеннаяКавычка = new string(параметры.QuoteChar, 2);

        return string.Concat(
            параметры.QuoteChar,
            значение.Replace(параметры.QuoteChar.ToString(), удвоеннаяКавычка, StringComparison.Ordinal),
            параметры.QuoteChar);
    }

    /// <summary>
    /// Выводит таблицу в HTML-документ (ТЗ §8.5, функция 71).
    /// </summary>
    /// <param name="таблица">Таблица выгрузки.</param>
    /// <param name="output">Целевой поток.</param>
    /// <param name="параметры">Параметры выгрузки.</param>
    private static void ЗаписатьHtml(
        SpreadsheetTable таблица,
        Stream output,
        AddressExchangeOptions параметры)
    {
        using var писатель = new StreamWriter(
            output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true);

        писатель.Write("<!DOCTYPE html>\n");
        писатель.Write("<html lang=\"ru\">\n");
        писатель.Write("<head>\n");
        писатель.Write("<meta charset=\"utf-8\">\n");
        писатель.Write("<title>Адресная книга</title>\n");
        писатель.Write(
            "<style>table{border-collapse:collapse}th,td{border:1px solid #808080;"
            + "padding:4px 6px;text-align:left;vertical-align:top}</style>\n");
        писатель.Write("</head>\n");
        писатель.Write("<body>\n");
        писатель.Write("<h1>Адресная книга</h1>\n");
        писатель.Write("<table>\n");

        if (параметры.IncludeHeader)
        {
            писатель.Write("<thead>\n<tr>");

            foreach (var заголовок in таблица.Headers)
            {
                писатель.Write("<th>");
                писатель.Write(ЭкранироватьHtml(заголовок));
                писатель.Write("</th>");
            }

            писатель.Write("</tr>\n</thead>\n");
        }

        писатель.Write("<tbody>\n");

        foreach (var строка in таблица.Rows)
        {
            писатель.Write("<tr>");

            foreach (var ячейка in строка)
            {
                писатель.Write("<td>");
                писатель.Write(ЭкранироватьHtml(ячейка.AsText(параметры.DateFormat)));
                писатель.Write("</td>");
            }

            писатель.Write("</tr>\n");
        }

        писатель.Write("</tbody>\n");
        писатель.Write("</table>\n");
        писатель.Write("</body>\n");
        писатель.Write("</html>\n");
    }

    /// <summary>
    /// Экранирует текст для включения в HTML-документ.
    /// </summary>
    /// <param name="значение">Исходный текст.</param>
    /// <returns>Экранированный текст.</returns>
    private static string ЭкранироватьHtml(string значение) => значение
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// Выводит таблицу в документ Rich Text Format (ТЗ §8.5, функция 71).
    /// </summary>
    /// <param name="таблица">Таблица выгрузки.</param>
    /// <param name="output">Целевой поток.</param>
    /// <param name="параметры">Параметры выгрузки.</param>
    private static void ЗаписатьRtf(
        SpreadsheetTable таблица,
        Stream output,
        AddressExchangeOptions параметры)
    {
        var документ = new StringBuilder();

        документ.Append("{\\rtf1\\ansi\\ansicpg1251\\deff0\n");
        документ.Append("{\\fonttbl{\\f0\\fnil\\fcharset204 Calibri;}}\n");
        документ.Append("{\\colortbl;\\red0\\green0\\blue0;}\n");

        var числоСтолбцов = Math.Max(таблица.ColumnCount, 1);

        if (параметры.IncludeHeader)
        {
            ЗаписатьСтрокуRtf(документ, таблица.Headers, числоСтолбцов, жирный: true);
        }

        foreach (var строка in таблица.Rows)
        {
            var значения = new List<string>(строка.Count);

            foreach (var ячейка in строка)
            {
                значения.Add(ячейка.AsText(параметры.DateFormat));
            }

            ЗаписатьСтрокуRtf(документ, значения, числоСтолбцов, жирный: false);
        }

        документ.Append("}\n");

        var байты = Encoding.ASCII.GetBytes(документ.ToString());

        output.Write(байты, 0, байты.Length);
    }

    /// <summary>
    /// Записывает строку таблицы в документ RTF.
    /// </summary>
    /// <param name="документ">Текст документа.</param>
    /// <param name="значения">Значения ячеек строки.</param>
    /// <param name="числоСтолбцов">Число столбцов таблицы.</param>
    /// <param name="жирный">Признак выделения строки заголовков.</param>
    private static void ЗаписатьСтрокуRtf(
        StringBuilder документ,
        IReadOnlyList<string> значения,
        int числоСтолбцов,
        bool жирный)
    {
        документ.Append("\\trowd\\trgaph80\\trleft0");

        for (var i = 0; i < числоСтолбцов; i++)
        {
            документ.Append(CultureInfo.InvariantCulture, $@"\cellx{(i + 1) * ШиринаСтолбцаRtf}");
        }

        документ.Append('\n');
        документ.Append(жирный ? "{\\intbl\\b " : "{\\intbl ");

        for (var i = 0; i < числоСтолбцов; i++)
        {
            if (i > 0)
            {
                документ.Append("\\cell ");
            }

            var значение = i < значения.Count ? значения[i] : string.Empty;

            документ.Append(ЭкранироватьRtf(значение));
        }

        документ.Append(жирный ? "\\b0}" : "}");
        документ.Append("\\row\n");
    }

    /// <summary>
    /// Экранирует текст для включения в документ RTF.
    /// </summary>
    /// <param name="значение">Исходный текст.</param>
    /// <returns>Экранированный текст.</returns>
    private static string ЭкранироватьRtf(string значение)
    {
        var результат = new StringBuilder(значение.Length);

        foreach (var символ in значение)
        {
            switch (символ)
            {
                case '\\':
                    результат.Append("\\\\");
                    break;

                case '{':
                    результат.Append("\\{");
                    break;

                case '}':
                    результат.Append("\\}");
                    break;

                case '\t':
                    результат.Append("\\tab ");
                    break;

                case '\r':
                    break;

                case '\n':
                    результат.Append("\\line ");
                    break;

                default:
                    if (символ < 32)
                    {
                        break;
                    }

                    if (символ < 128)
                    {
                        результат.Append(символ);
                        break;
                    }

                    var код = символ < 32768 ? символ : символ - 65536;
                    результат.Append(CultureInfo.InvariantCulture, $"\\u{код.ToString(CultureInfo.InvariantCulture)}");
                    результат.Append('?');
                    break;
            }
        }

        return результат.ToString();
    }

    /// <summary>
    /// Преобразует разобранную таблицу в черновики контрагентов.
    /// </summary>
    /// <param name="таблица">Разобранная таблица файла.</param>
    /// <param name="профиль">Профиль сопоставления столбцов.</param>
    /// <returns>Черновики контрагентов.</returns>
    /// <exception cref="AddressExchangeImportException">
    /// Строка файла повреждена: число столбцов не совпадает с ожидаемым.
    /// </exception>
    private static IReadOnlyList<CounterpartyDraft> СобратьЧерновики(
        DelimitedTextTable таблица,
        CsvProfile профиль)
    {
        foreach (var ошибочная in таблица.ErrorRows)
        {
            throw new AddressExchangeImportException(
                ошибочная.RowNumber,
                ошибочная.Error ?? $"Строка {ошибочная.RowNumber}: повреждённая структура файла.");
        }

        var черновики = new List<CounterpartyDraft>();

        foreach (var строка in таблица.Rows)
        {
            if (строка.HasError)
            {
                continue;
            }

            черновики.Add(СобратьЧерновик(строка, таблица.Headers, профиль));
        }

        return черновики;
    }

    /// <summary>
    /// Создаёт черновик контрагента по строке файла.
    /// </summary>
    /// <param name="строка">Строка файла.</param>
    /// <param name="заголовки">Заголовки столбцов файла.</param>
    /// <param name="профиль">Профиль сопоставления столбцов.</param>
    /// <returns>Черновик контрагента.</returns>
    private static CounterpartyDraft СобратьЧерновик(
        DelimitedTextRow строка,
        IReadOnlyList<string> заголовки,
        CsvProfile профиль)
    {
        var черновик = new CounterpartyDraft();

        for (var индекс = 0; индекс < строка.Cells.Count; индекс++)
        {
            var значение = строка.Cells[индекс];

            var слот = НайтиСлот(индекс, заголовки, профиль);

            if (слот is not null)
            {
                ПрименитьСлот(черновик, слот.Value, значение);
                continue;
            }

            var поле = НайтиПоле(индекс, заголовки);

            if (поле is not null)
            {
                ПрименитьПоле(черновик, поле.Value, значение);
            }
        }

        черновик.NeedsVerification = ТребуетсяПроверка(черновик);

        return черновик;
    }

    /// <summary>
    /// Определяет слот поля для столбца: сначала сопоставление профиля,
    /// затем — название столбца из словаря полей ТЗ.
    /// </summary>
    /// <param name="индекс">Номер столбца (нумерация с нуля).</param>
    /// <param name="заголовки">Заголовки столбцов файла.</param>
    /// <param name="профиль">Профиль сопоставления столбцов.</param>
    /// <returns>Слот поля либо <see langword="null"/>, если столбец не сопоставлен.</returns>
    private static ImportFieldSlot? НайтиСлот(
        int индекс,
        IReadOnlyList<string> заголовки,
        CsvProfile профиль)
    {
        foreach (var привязка in профиль.Bindings)
        {
            if (привязка.ColumnIndex == индекс && привязка.Slot is not null)
            {
                return привязка.Slot;
            }
        }

        if (индекс >= заголовки.Count)
        {
            return null;
        }

        var заголовок = заголовки[индекс].Trim();

        foreach (var (название, слот) in ИзвестныеСлоты)
        {
            if (string.Equals(заголовок, название, StringComparison.OrdinalIgnoreCase))
            {
                return слот;
            }
        }

        foreach (var слот in ImportFieldSlotCatalog.All)
        {
            if (string.Equals(заголовок, ImportFieldSlotCatalog.Title(слот), StringComparison.OrdinalIgnoreCase))
            {
                return слот;
            }
        }

        return null;
    }

    /// <summary>
    /// Названия столбцов файла, сопоставляемые со слотами полей (ТЗ §8.12,
    /// функция 73). Список дополняет словарь полей ТЗ сокращениями, которые
    /// встречаются в выгрузках из 1С и в собственной выгрузке программы.
    /// </summary>
    private static readonly (string Название, ImportFieldSlot Слот)[] ИзвестныеСлоты =
    [
        ("Название", ImportFieldSlot.Name),
        ("Наименование", ImportFieldSlot.Name),
        ("Сокращенное", ImportFieldSlot.ShortName),
        ("Сокращённое", ImportFieldSlot.ShortName),
        ("Сокращенное наименование", ImportFieldSlot.ShortName),
        ("Сокращённое наименование", ImportFieldSlot.ShortName),
        ("ФИО", ImportFieldSlot.FullName),
        ("Название организации", ImportFieldSlot.OrganizationName),
        ("Почтовый адрес", ImportFieldSlot.PostalAddress),
        ("Юридический адрес", ImportFieldSlot.LegalAddress),
        ("Фактический адрес", ImportFieldSlot.ActualAddress),
        ("Прочее", ImportFieldSlot.Other),
    ];

    /// <summary>
    /// Определяет поле карточки для столбца по его названию. Используется для
    /// реквизитов, не входящих в словарь слотов мастера импорта.
    /// </summary>
    /// <param name="индекс">Номер столбца (нумерация с нуля).</param>
    /// <param name="заголовки">Заголовки столбцов файла.</param>
    /// <returns>Поле карточки либо <see langword="null"/>, если столбец не распознан.</returns>
    private static ИзвестноеПоле? НайтиПоле(int индекс, IReadOnlyList<string> заголовки)
    {
        if (индекс >= заголовки.Count)
        {
            return null;
        }

        var заголовок = заголовки[индекс].Trim();

        foreach (var (название, поле) in ИзвестныеПоля)
        {
            if (string.Equals(заголовок, название, StringComparison.OrdinalIgnoreCase))
            {
                return поле;
            }
        }

        return null;
    }

    /// <summary>Поля карточки контрагента, распознаваемые по названию столбца.</summary>
    private static readonly (string Название, ИзвестноеПоле Поле)[] ИзвестныеПоля =
    [
        ("Тип", ИзвестноеПоле.Тип),
        ("ИНН", ИзвестноеПоле.ИНН),
        ("КПП", ИзвестноеПоле.КПП),
        ("ОГРН", ИзвестноеПоле.ОГРН),
        ("Телефон", ИзвестноеПоле.Телефон),
        ("E-mail", ИзвестноеПоле.ЭлПочта),
        ("Примечание", ИзвестноеПоле.Примечание),
    ];

    /// <summary>
    /// Переносит значение столбца в слот поля черновика.
    /// </summary>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <param name="слот">Слот поля.</param>
    /// <param name="значение">Значение столбца.</param>
    private static void ПрименитьСлот(CounterpartyDraft черновик, ImportFieldSlot слот, string значение)
    {
        switch (слот)
        {
            case ImportFieldSlot.Name:
                черновик.Name = значение;
                break;

            case ImportFieldSlot.ShortName:
                черновик.ShortName = значение;
                break;

            case ImportFieldSlot.FullName:
            case ImportFieldSlot.OrganizationName:
                if (string.IsNullOrEmpty(черновик.Name))
                {
                    черновик.Name = значение;
                }

                break;

            case ImportFieldSlot.PostalAddress:
            {
                var адрес = РазобратьАдрес(значение);
                черновик.PostalZip = адрес.Zip;
                черновик.PostalLines = адрес.Lines;
                break;
            }

            case ImportFieldSlot.LegalAddress:
            {
                var адрес = РазобратьАдрес(значение);
                черновик.LegalZip = адрес.Zip;
                черновик.LegalLines = адрес.Lines;
                break;
            }

            case ImportFieldSlot.ActualAddress:
            {
                var адрес = РазобратьАдрес(значение);
                черновик.ActualZip = адрес.Zip;
                черновик.ActualLines = адрес.Lines;
                break;
            }

            case ImportFieldSlot.Other:
                if (string.IsNullOrEmpty(черновик.Note))
                {
                    черновик.Note = значение;
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Переносит значение столбца в поле карточки черновика.
    /// </summary>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <param name="поле">Поле карточки.</param>
    /// <param name="значение">Значение столбца.</param>
    private static void ПрименитьПоле(CounterpartyDraft черновик, ИзвестноеПоле поле, string значение)
    {
        switch (поле)
        {
            case ИзвестноеПоле.Тип:
                черновик.Type = РазобратьТип(значение);
                break;

            case ИзвестноеПоле.ИНН:
                черновик.Inn = значение;
                break;

            case ИзвестноеПоле.КПП:
                черновик.Kpp = значение;
                break;

            case ИзвестноеПоле.ОГРН:
                черновик.Ogrn = значение;
                break;

            case ИзвестноеПоле.Телефон:
                черновик.Phone = значение;
                break;

            case ИзвестноеПоле.ЭлПочта:
                черновик.Email = значение;
                break;

            case ИзвестноеПоле.Примечание:
                черновик.Note = значение;
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Разбирает значение столбца с адресом: почтовый индекс и шесть адресных
    /// строк. Если индекс не распознан, значение сохраняется целиком в первой
    /// строке адреса, а запись помечается как требующая проверки.
    /// </summary>
    /// <param name="значение">Значение столбца.</param>
    /// <returns>Почтовый индекс и адресные строки.</returns>
    private static (string Zip, string[] Lines) РазобратьАдрес(string значение)
    {
        var разбор = Address.Parse(значение);

        if (разбор.IsSuccess)
        {
            var адрес = разбор.Value;
            return (адрес.Zip, [.. адрес.Lines]);
        }

        var строки = new string[Address.LineCount];
        строки[0] = значение;

        return (string.Empty, строки);
    }

    /// <summary>
    /// Разбирает название типа контрагента.
    /// </summary>
    /// <param name="значение">Значение столбца «Тип».</param>
    /// <returns>Тип контрагента.</returns>
    private static CounterpartyType РазобратьТип(string значение)
    {
        var текст = значение.Trim();

        if (string.Equals(текст, ТипФизическогоЛица, StringComparison.OrdinalIgnoreCase)
            || string.Equals(текст, nameof(CounterpartyType.NaturalPerson), StringComparison.OrdinalIgnoreCase)
            || string.Equals(текст, "ФЛ", StringComparison.OrdinalIgnoreCase))
        {
            return CounterpartyType.NaturalPerson;
        }

        return CounterpartyType.LegalEntity;
    }

    /// <summary>
    /// Определяет, требует ли запись проверки оператором: адрес заполнен, но
    /// почтовый индекс не распознан (ТЗ §8.1, функция 5).
    /// </summary>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <returns>Признак необходимости проверки.</returns>
    private static bool ТребуетсяПроверка(CounterpartyDraft черновик)
        => ТребуетсяПроверкаАдреса(черновик.PostalZip, черновик.PostalLines)
            || ТребуетсяПроверкаАдреса(черновик.LegalZip, черновик.LegalLines)
            || ТребуетсяПроверкаАдреса(черновик.ActualZip, черновик.ActualLines);

    /// <summary>
    /// Определяет, требует ли адрес проверки оператором.
    /// </summary>
    /// <param name="индекс">Почтовый индекс.</param>
    /// <param name="строки">Адресные строки.</param>
    /// <returns>Признак необходимости проверки.</returns>
    private static bool ТребуетсяПроверкаАдреса(string индекс, IReadOnlyList<string> строки)
        => string.IsNullOrEmpty(индекс) && строки.Any(строка => !string.IsNullOrWhiteSpace(строка));

    /// <summary>Поля карточки контрагента, распознаваемые по названию столбца.</summary>
    private enum ИзвестноеПоле
    {
        /// <summary>Тип контрагента.</summary>
        Тип,

        /// <summary>ИНН.</summary>
        ИНН,

        /// <summary>КПП.</summary>
        КПП,

        /// <summary>ОГРН.</summary>
        ОГРН,

        /// <summary>Телефон.</summary>
        Телефон,

        /// <summary>Адрес электронной почты.</summary>
        ЭлПочта,

        /// <summary>Примечание.</summary>
        Примечание,
    }
}

/// <summary>
/// Параметры выгрузки адресного списка (ТЗ §8.5, функция 71;
/// ТЗ §8.12, функция 160).
/// </summary>
public sealed class AddressExchangeOptions
{
    /// <summary>Разделитель столбцов текстовых форматов.</summary>
    public char Delimiter { get; set; } = ';';

    /// <summary>Символ обрамления значений текстовых форматов.</summary>
    public char QuoteChar { get; set; } = '"';

    /// <summary>Кодировка текстовых форматов; по умолчанию UTF-8 с маркером BOM.</summary>
    public Encoding? Encoding { get; set; }

    /// <summary>Признак включения строки заголовков.</summary>
    public bool IncludeHeader { get; set; } = true;

    /// <summary>Формат представления дат в выгрузке.</summary>
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    /// <summary>Имя листа книги Excel.</summary>
    public string SheetName { get; set; } = "Адреса";
}

/// <summary>
/// Ошибка импорта адресного списка: повреждённая структура файла
/// (ТЗ §8.5, функция 71: «Повреждённая структура файла вызывает ошибку
/// импорта строки с логированием номера строки»).
/// </summary>
public sealed class AddressExchangeImportException : Exception
{
    /// <summary>Создаёт ошибку импорта строки.</summary>
    /// <param name="rowNumber">Номер ошибочной строки файла (нумерация с единицы).</param>
    /// <param name="message">Описание ошибки; содержит номер строки.</param>
    public AddressExchangeImportException(int rowNumber, string message)
        : base($"Ошибка импорта: {message}")
    {
        RowNumber = rowNumber;
    }

    /// <summary>Номер ошибочной строки файла (нумерация с единицы).</summary>
    public int RowNumber { get; }
}
