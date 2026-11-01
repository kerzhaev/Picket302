using System.Text;
using Picket302.Domain.Contracts;

namespace Picket302.Application.Import;

/// <summary>
/// Чтение текста с разделителями в строки и ячейки (ТЗ §8.5, функция 71:
/// «разделители запятая/точка с запятой/табуляция»; ТЗ §8.12, функция 73:
/// «символ разделителя, символ экранирования кавычек, строки начала данных»).
/// </summary>
/// <remarks>
/// <para>
/// Разбор учитывает обрамление значений символом кавычки; внутри кавычек
/// удвоенная кавычка (<c>""</c>) обозначает один символ кавычки. Значение
/// в кавычках может содержать разделитель и перевод строки.
/// </para>
/// <para>
/// Число столбцов сверяется с числом столбцов строки заголовков (или первой
/// строки данных, если заголовка нет). Несовпадение помечает строку как
/// ошибочную, а текст ошибки содержит номер строки (ТЗ §8.12, функция 73:
/// «Несовпадение числа колонок в строке помечает строку как ошибочную»).
/// </para>
/// </remarks>
public sealed class DelimitedTextReader
{
    /// <summary>Символ перевода строки в содержимом файла.</summary>
    private const char ПереводСтроки = '\n';

    /// <summary>
    /// Читает поток с текстом с разделителями согласно профилю импорта.
    /// </summary>
    /// <param name="поток">Поток с содержимым файла.</param>
    /// <param name="профиль">Профиль сопоставления: разделитель, кодировка, начало данных.</param>
    /// <returns>Разобранная таблица строк.</returns>
    public DelimitedTextTable Read(Stream поток, CsvProfile профиль)
    {
        ArgumentNullException.ThrowIfNull(поток);
        ArgumentNullException.ThrowIfNull(профиль);

        var кодировка = TextEncodingDetector.ПоИмени(профиль.EncodingName);

        if (кодировка is null)
        {
            using var буфер = new MemoryStream();
            поток.CopyTo(буфер);

            var байты = буфер.ToArray();
            var определение = TextEncodingDetector.Detect(байты);

            return Read(определение.Encoding.GetString(байты), профиль);
        }

        using var читатель = new StreamReader(поток, кодировка, detectEncodingFromByteOrderMarks: false);

        return Read(читатель.ReadToEnd(), профиль);
    }

    /// <summary>
    /// Читает текст с разделителями согласно профилю импорта.
    /// </summary>
    /// <param name="текст">Текст с разделителями.</param>
    /// <param name="профиль">Профиль сопоставления.</param>
    /// <returns>Разобранная таблица строк.</returns>
    public DelimitedTextTable Read(string текст, CsvProfile профиль)
    {
        ArgumentNullException.ThrowIfNull(текст);
        ArgumentNullException.ThrowIfNull(профиль);

        var разделитель = профиль.Delimiter == '\0' ? ',' : профиль.Delimiter;
        var кавычка = профиль.QuoteChar == '\0' ? '"' : профиль.QuoteChar;

        // Маркер BOM UTF-8 не является частью значения первой ячейки.
        if (текст.Length > 0 && текст[0] == '\uFEFF')
        {
            текст = текст[1..];
        }

        var физическиеСтроки = РазделитьНаСтроки(текст, разделитель, кавычка);

        var началоДанных = Math.Max(1, профиль.FirstDataRow);
        var заголовки = new List<string>();
        var строки = new List<DelimitedTextRow>();

        var ожидаемоеЧислоКолонок = -1;
        var началоРазбора = началоДанных;

        if (профиль.HasHeaderRow)
        {
            // Строка заголовков — первая строка файла: «Начать импорт с» задаёт
            // первую строку данных, а не заголовка, поэтому имена столбцов
            // остаются правильными при любом сдвиге.
            var строкаЗаголовка = НайтиСтроку(физическиеСтроки, 1);

            if (строкаЗаголовка is not null)
            {
                заголовки.AddRange(строкаЗаголовка.Ячейки);
                ожидаемоеЧислоКолонок = строкаЗаголовка.Ячейки.Count;

                if (началоРазбора <= строкаЗаголовка.Номер)
                {
                    началоРазбора = строкаЗаголовка.Номер + 1;
                }
            }
        }

        foreach (var строка in физическиеСтроки)
        {
            if (строка.Номер < началоРазбора)
            {
                continue;
            }

            if (ожидаемоеЧислоКолонок < 0)
            {
                ожидаемоеЧислоКолонок = строка.Ячейки.Count;
            }

            var ошибочная = ожидаемоеЧислоКолонок >= 0 && строка.Ячейки.Count != ожидаемоеЧислоКолонок;
            var ожидание = заголовки.Count > 0
                ? $"с числом столбцов заголовка ({ожидаемоеЧислоКолонок})"
                : $"с ожидаемым числом столбцов ({ожидаемоеЧислоКолонок})";
            var текстОшибки = ошибочная
                ? $"Строка {строка.Номер}: число столбцов ({строка.Ячейки.Count}) не совпадает {ожидание}."
                : null;

            строки.Add(new DelimitedTextRow(строка.Номер, строка.Ячейки, ошибочная, текстОшибки));
        }

        return new DelimitedTextTable(заголовки, строки);
    }

    /// <summary>
    /// Разбирает текст на физические строки и ячейки, учитывая кавычки.
    /// </summary>
    /// <param name="текст">Исходный текст.</param>
    /// <param name="разделитель">Разделитель столбцов.</param>
    /// <param name="кавычка">Символ обрамления значений.</param>
    /// <returns>Строки файла с номерами и ячейками.</returns>
    private static List<ФизическаяСтрока> РазделитьНаСтроки(string текст, char разделитель, char кавычка)
    {
        var строки = new List<ФизическаяСтрока>();
        var ячейки = new List<string>();
        var значение = new StringBuilder();
        var вКавычках = false;
        var началоЯчейки = true;
        var номерСтроки = 1;

        void ЗавершитьЯчейку()
        {
            ячейки.Add(значение.ToString());
            значение.Clear();
            началоЯчейки = true;
        }

        void ЗавершитьСтроку()
        {
            ЗавершитьЯчейку();

            строки.Add(new ФизическаяСтрока(номерСтроки, ячейки.ToArray()));
            номерСтроки++;

            ячейки.Clear();
            началоЯчейки = true;
        }

        for (var i = 0; i < текст.Length; i++)
        {
            var символ = текст[i];

            if (вКавычках)
            {
                if (символ == кавычка)
                {
                    if (i + 1 < текст.Length && текст[i + 1] == кавычка)
                    {
                        значение.Append(кавычка);
                        i++;
                        continue;
                    }

                    вКавычках = false;
                    continue;
                }

                значение.Append(символ);
                continue;
            }

            if (символ == кавычка && началоЯчейки)
            {
                вКавычках = true;
                началоЯчейки = false;
                continue;
            }

            if (символ == разделитель)
            {
                ЗавершитьЯчейку();
                continue;
            }

            if (символ == ПереводСтроки)
            {
                ЗавершитьСтроку();
                continue;
            }

            if (символ == '\r')
            {
                // Возврат каретки перед переводом строки пропускается.
                if (i + 1 < текст.Length && текст[i + 1] == ПереводСтроки)
                {
                    continue;
                }

                ЗавершитьСтроку();
                continue;
            }

            значение.Append(символ);
            началоЯчейки = false;
        }

        if (вКавычках)
        {
            // Незакрытая кавычка: значение считается обычным текстом до конца файла.
            строки.Add(new ФизическаяСтрока(номерСтроки, [значение.ToString()]));
            return строки;
        }

        if (!началоЯчейки || ячейки.Count > 0)
        {
            ЗавершитьСтроку();
        }

        return строки;
    }

    /// <summary>
    /// Находит строку с заданным номером среди физических строк файла.
    /// </summary>
    /// <param name="строки">Физические строки файла.</param>
    /// <param name="номер">Номер строки (нумерация с единицы).</param>
    /// <returns>Найденная строка либо <see langword="null"/>.</returns>
    private static ФизическаяСтрока? НайтиСтроку(List<ФизическаяСтрока> строки, int номер)
        => строки.FirstOrDefault(строка => строка.Номер == номер);

    /// <summary>Физическая строка файла: номер и разобранные ячейки.</summary>
    /// <param name="Номер">Номер строки в файле (нумерация с единицы).</param>
    /// <param name="Ячейки">Значения ячеек строки.</param>
    private sealed record ФизическаяСтрока(int Номер, IReadOnlyList<string> Ячейки);
}

/// <summary>
/// Строка файла с разделителями (ТЗ §8.12, функция 73).
/// </summary>
/// <param name="RowNumber">Номер строки в файле (нумерация с единицы).</param>
/// <param name="Cells">Значения ячеек строки.</param>
/// <param name="HasError">Признак ошибочной строки.</param>
/// <param name="Error">Текст ошибки строки; содержит номер строки.</param>
public sealed record DelimitedTextRow(
    int RowNumber,
    IReadOnlyList<string> Cells,
    bool HasError = false,
    string? Error = null);

/// <summary>
/// Таблица строк файла с разделителями (ТЗ §8.12, функция 73).
/// </summary>
/// <param name="Headers">Заголовки столбцов; пуст, если в файле нет строки заголовков.</param>
/// <param name="Rows">Строки данных.</param>
public sealed class DelimitedTextTable
{
    /// <summary>Создаёт таблицу строк файла.</summary>
    /// <param name="headers">Заголовки столбцов.</param>
    /// <param name="rows">Строки данных.</param>
    public DelimitedTextTable(IReadOnlyList<string> headers, IReadOnlyList<DelimitedTextRow> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        Headers = headers;
        Rows = rows;
    }

    /// <summary>Заголовки столбцов.</summary>
    public IReadOnlyList<string> Headers { get; }

    /// <summary>Строки данных файла.</summary>
    public IReadOnlyList<DelimitedTextRow> Rows { get; }

    /// <summary>Ошибочные строки файла.</summary>
    public IReadOnlyList<DelimitedTextRow> ErrorRows => Rows.Where(строка => строка.HasError).ToList();

    /// <summary>Число ошибочных строк файла.</summary>
    public int ErrorCount => Rows.Count(строка => строка.HasError);

    /// <summary>Признак наличия ошибочных строк.</summary>
    public bool HasErrors => ErrorCount > 0;
}
