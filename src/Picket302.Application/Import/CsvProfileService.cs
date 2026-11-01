using System.Text;
using Picket302.Domain.Contracts;
using Picket302.Domain.Dictionaries;

namespace Picket302.Application.Import;

/// <summary>
/// Определение параметров разбора CSV и подготовка профиля сопоставления
/// (ТЗ §8.12, функция 73: «Интерактивное определение кодировки (ANSI/UTF-8),
/// символа разделителя, символа экранирования кавычек, строки начала данных»).
/// </summary>
/// <remarks>
/// Служба не обращается к файловой системе: на вход подаётся текст предпросмотра
/// либо байты файла. Разделители, поддерживаемые ТЗ: запятая, точка с
/// запятой, табуляция.
/// </remarks>
public sealed class CsvProfileService
{
    /// <summary>Разделители столбцов, поддерживаемые программой.</summary>
    public static readonly IReadOnlyList<char> ПоддерживаемыеРазделители = [',', ';', '\t'];

    /// <summary>Символ обрамления значений по умолчанию.</summary>
    public const char СимволКавычкиПоУмолчанию = '"';

    /// <summary>Число строк предпросмотра, участвующих в определении разделителя.</summary>
    public const int ЧислоСтрокПредпросмотра = 20;

    /// <summary>
    /// Определяет разделитель столбцов по тексту предпросмотра.
    /// </summary>
    /// <param name="текстПредпросмотра">Текст предпросмотра файла.</param>
    /// <returns>Разделитель столбцов.</returns>
    public char DetectDelimiter(string текстПредпросмотра)
    {
        ArgumentNullException.ThrowIfNull(текстПредпросмотра);

        var строки = РазделитьНаСтроки(текстПредпросмотра);

        if (строки.Count == 0)
        {
            return ',';
        }

        var лучший = ПоддерживаемыеРазделители[0];
        var лучшийРезультат = -1;

        foreach (var разделитель in ПоддерживаемыеРазделители)
        {
            var результат = ПосчитатьСтолбцы(строки, разделитель);

            if (результат > лучшийРезультат)
            {
                лучшийРезультат = результат;
                лучший = разделитель;
            }
        }

        return лучший;
    }

    /// <summary>
    /// Определяет кодировку файла по его байтам.
    /// </summary>
    /// <param name="байты">Байты файла.</param>
    /// <returns>Результат определения кодировки.</returns>
    public EncodingDetectionResult DetectEncoding(byte[] байты)
    {
        ArgumentNullException.ThrowIfNull(байты);

        return TextEncodingDetector.Detect(байты);
    }

    /// <summary>
    /// Определяет заголовки столбцов по тексту предпросмотра.
    /// </summary>
    /// <param name="текстПредпросмотра">Текст предпросмотра файла.</param>
    /// <param name="разделитель">Разделитель столбцов.</param>
    /// <returns>Заголовки столбцов; пуст, если первая строка не содержит заголовков.</returns>
    public IReadOnlyList<string> DetectHeaders(string текстПредпросмотра, char разделитель)
    {
        ArgumentNullException.ThrowIfNull(текстПредпросмотра);

        var строки = РазделитьНаСтроки(текстПредпросмотра);

        if (строки.Count == 0)
        {
            return Array.Empty<string>();
        }

        return РазделитьСтроку(строки[0], разделитель);
    }

    /// <summary>
    /// Предлагает профиль сопоставления по тексту предпросмотра.
    /// </summary>
    /// <param name="текстПредпросмотра">Текст предпросмотра файла.</param>
    /// <param name="имяПрофиля">Имя профиля; при пустом значении используется имя по умолчанию.</param>
    /// <returns>Профиль сопоставления с определёнными разделителем и сопоставлением столбцов.</returns>
    public CsvProfile Propose(string текстПредпросмотра, string имяПрофиля = "")
    {
        ArgumentNullException.ThrowIfNull(текстПредпросмотра);

        var разделитель = DetectDelimiter(текстПредпросмотра);
        var заголовки = DetectHeaders(текстПредпросмотра, разделитель);

        return СобратьПрофиль(имяПрофиля, разделитель, TextEncodingDetector.Utf8Name, заголовки);
    }

    /// <summary>
    /// Предлагает профиль сопоставления по байтам файла: кодировка определяется
    /// автоматически, разделитель — по содержимому.
    /// </summary>
    /// <param name="байты">Байты файла.</param>
    /// <param name="имяПрофиля">Имя профиля; при пустом значении используется имя по умолчанию.</param>
    /// <returns>Профиль сопоставления с определёнными кодировкой и разделителем.</returns>
    public CsvProfile Propose(byte[] байты, string имяПрофиля = "")
    {
        ArgumentNullException.ThrowIfNull(байты);

        var определение = DetectEncoding(байты);
        var текст = определение.Encoding.GetString(байты);

        var разделитель = DetectDelimiter(текст);
        var заголовки = DetectHeaders(текст, разделитель);

        return СобратьПрофиль(имяПрофиля, разделитель, определение.EncodingName, заголовки);
    }

    /// <summary>
    /// Предлагает сопоставление столбцов файла слотам полей по заголовкам.
    /// </summary>
    /// <param name="заголовки">Заголовки столбцов файла.</param>
    /// <returns>Сопоставление столбцов со слотами полей.</returns>
    public IReadOnlyList<ImportColumnBinding> ProposeBindings(IReadOnlyList<string> заголовки)
    {
        ArgumentNullException.ThrowIfNull(заголовки);

        var сопоставление = new List<ImportColumnBinding>();

        for (var индекс = 0; индекс < заголовки.Count; индекс++)
        {
            var заголовок = заголовки[индекс];

            if (string.IsNullOrWhiteSpace(заголовок))
            {
                continue;
            }

            var слот = НайтиСлот(заголовок);

            if (слот is not null)
            {
                сопоставление.Add(new ImportColumnBinding(индекс, слот.Value.Слот, слот.Value.Название));
            }
        }

        return сопоставление;
    }

    /// <summary>
    /// Названия столбцов файла, сопоставляемые со слотами полей: словарь ТЗ
    /// дополнен распространёнными сокращениями выгрузок из 1С.
    /// </summary>
    private static readonly (string Название, ImportFieldSlot Слот)[] ИзвестныеНазвания =
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
    /// Ищет слот поля по его названию из словаря ТЗ.
    /// </summary>
    /// <param name="заголовок">Заголовок столбца файла.</param>
    /// <returns>Слот поля и его название либо <see langword="null"/>, если столбец не распознан.</returns>
    private static (ImportFieldSlot Слот, string Название)? НайтиСлот(string заголовок)
    {
        var текст = заголовок.Trim();

        foreach (var (название, слот) in ИзвестныеНазвания)
        {
            if (string.Equals(текст, название, StringComparison.OrdinalIgnoreCase))
            {
                return (слот, ImportFieldSlotCatalog.Title(слот));
            }
        }

        foreach (var слот in ImportFieldSlotCatalog.All)
        {
            if (string.Equals(текст, ImportFieldSlotCatalog.Title(слот), StringComparison.OrdinalIgnoreCase))
            {
                return (слот, ImportFieldSlotCatalog.Title(слот));
            }
        }

        return null;
    }

    /// <summary>
    /// Собирает профиль сопоставления.
    /// </summary>
    /// <param name="имяПрофиля">Имя профиля.</param>
    /// <param name="разделитель">Разделитель столбцов.</param>
    /// <param name="имяКодировки">Имя кодировки.</param>
    /// <param name="заголовки">Заголовки столбцов файла.</param>
    /// <returns>Профиль сопоставления.</returns>
    private static CsvProfile СобратьПрофиль(
        string имяПрофиля,
        char разделитель,
        string имяКодировки,
        IReadOnlyList<string> заголовки)
    {
        var сервис = new CsvProfileService();

        return new CsvProfile
        {
            Name = string.IsNullOrWhiteSpace(имяПрофиля) ? "Профиль импорта CSV" : имяПрофиля.Trim(),
            EncodingName = имяКодировки,
            Delimiter = разделитель,
            QuoteChar = СимволКавычкиПоУмолчанию,
            HasHeaderRow = заголовки.Count > 0,
            FirstDataRow = 1,
            Bindings = [.. сервис.ProposeBindings(заголовки)],
        };
    }

    /// <summary>
    /// Определяет число столбцов файла по образцу строк предпросмотра.
    /// </summary>
    /// <param name="строки">Строки предпросмотра.</param>
    /// <param name="разделитель">Разделитель столбцов.</param>
    /// <returns>Наиболее частое число столбцов в строках предпросмотра.</returns>
    private static int ПосчитатьСтолбцы(IReadOnlyList<string> строки, char разделитель)
    {
        var количества = new Dictionary<int, int>();

        foreach (var строка in строки)
        {
            var число = РазделитьСтроку(строка, разделитель).Count;

            количества[число] = количества.TryGetValue(число, out var значение) ? значение + 1 : 1;
        }

        var лучшее = 1;
        var лучшееКоличество = 0;

        foreach (var (число, количество) in количества)
        {
            if (количество > лучшееКоличество || (количество == лучшееКоличество && число > лучшее))
            {
                лучшее = число;
                лучшееКоличество = количество;
            }
        }

        return лучшее;
    }

    /// <summary>
    /// Делит текст предпросмотра на строки, не учитывая переводы строк внутри кавычек.
    /// </summary>
    /// <param name="текст">Текст предпросмотра.</param>
    /// <returns>Строки предпросмотра.</returns>
    private static List<string> РазделитьНаСтроки(string текст)
    {
        var строки = new List<string>();
        var текущая = new StringBuilder();
        var вКавычках = false;

        for (var i = 0; i < текст.Length; i++)
        {
            var символ = текст[i];

            if (символ == СимволКавычкиПоУмолчанию)
            {
                if (вКавычках && i + 1 < текст.Length && текст[i + 1] == СимволКавычкиПоУмолчанию)
                {
                    текущая.Append(СимволКавычкиПоУмолчанию);
                    i++;
                    continue;
                }

                вКавычках = !вКавычках;
                текущая.Append(символ);
                continue;
            }

            if (!вКавычках && (символ == '\r' || символ == '\n'))
            {
                if (символ == '\r' && i + 1 < текст.Length && текст[i + 1] == '\n')
                {
                    i++;
                }

                строки.Add(текущая.ToString());
                текущая.Clear();
                continue;
            }

            текущая.Append(символ);
        }

        if (текущая.Length > 0)
        {
            строки.Add(текущая.ToString());
        }

        return строки.Count > ЧислоСтрокПредпросмотра
            ? строки.GetRange(0, ЧислоСтрокПредпросмотра)
            : строки;
    }

    /// <summary>
    /// Делит строку на ячейки по разделителю, не учитывая разделители внутри кавычек.
    /// </summary>
    /// <param name="строка">Строка файла.</param>
    /// <param name="разделитель">Разделитель столбцов.</param>
    /// <returns>Значения ячеек строки.</returns>
    private static List<string> РазделитьСтроку(string строка, char разделитель)
    {
        var ячейки = new List<string>();
        var значение = new StringBuilder();
        var вКавычках = false;

        for (var i = 0; i < строка.Length; i++)
        {
            var символ = строка[i];

            if (символ == СимволКавычкиПоУмолчанию)
            {
                if (вКавычках && i + 1 < строка.Length && строка[i + 1] == СимволКавычкиПоУмолчанию)
                {
                    значение.Append(СимволКавычкиПоУмолчанию);
                    i++;
                    continue;
                }

                вКавычках = !вКавычках;
                continue;
            }

            if (!вКавычках && символ == разделитель)
            {
                ячейки.Add(значение.ToString());
                значение.Clear();
                continue;
            }

            значение.Append(символ);
        }

        ячейки.Add(значение.ToString());

        return ячейки;
    }
}
