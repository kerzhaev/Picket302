using Picket302.Domain.Common;

namespace Picket302.Domain.ValueObjects;

/// <summary>
/// Адрес в структуре «почтовый индекс + 6 адресных строк».
/// </summary>
/// <remarks>
/// ТЗ §3.1.1 (таблица <c>counterparties</c>) и §8.1, функция 4: индекс хранится отдельно
/// (<c>VARCHAR(6)</c>), адресные строки — в отдельных колонках <c>str1..str6</c>
/// (<c>VARCHAR(255)</c> каждая). Адрес никогда не хранится одной склеенной строкой;
/// поле <c>*_raw</c> служит только для round-trip импорта.
/// </remarks>
public sealed class Address
{
    /// <summary>Количество адресных строк (<c>str1..str6</c>).</summary>
    public const int LineCount = 6;

    /// <summary>Количество знаков в почтовом индексе (<c>VARCHAR(6)</c>).</summary>
    public const int ZipLength = 6;

    /// <summary>Максимальное количество знаков в одной адресной строке (<c>VARCHAR(255)</c>).</summary>
    public const int MaxLineLength = 255;

    /// <summary>Разделитель строк в текстовом снимке адреса.</summary>
    public const char SnapshotSeparator = '\n';

    private readonly string[] _lines;

    private Address(string zip, string[] lines, string? raw)
    {
        Zip = zip;
        _lines = lines;
        Raw = raw;
    }

    /// <summary>Почтовый индекс: ровно 6 цифровых символов.</summary>
    public string Zip { get; }

    /// <summary>Адресные строки в порядке <c>str1..str6</c>; ровно 6 элементов.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Адресная строка 1 — организация или ФИО.</summary>
    public string Line1 => _lines[0];

    /// <summary>Адресная строка 2 — подразделение или улица.</summary>
    public string Line2 => _lines[1];

    /// <summary>Адресная строка 3 — дом, корпус, строение.</summary>
    public string Line3 => _lines[2];

    /// <summary>Адресная строка 4 — квартира, офис.</summary>
    public string Line4 => _lines[3];

    /// <summary>Адресная строка 5 — населённый пункт.</summary>
    public string Line5 => _lines[4];

    /// <summary>Адресная строка 6 — область, край, регион.</summary>
    public string Line6 => _lines[5];

    /// <summary>
    /// Исходная строка адреса, как она была в файле импорта. Служит только для
    /// round-trip импорта и не является носителем структурированных данных
    /// (ТЗ §3.1.19: «поле <c>*_raw</c> служит только для round-trip импорта»).
    /// </summary>
    public string? Raw { get; }

    /// <summary>
    /// Признак наличия структурированных данных: индекс задан и хотя бы одна
    /// адресная строка непуста.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(Zip) && _lines.All(string.IsNullOrEmpty);

    /// <summary>
    /// Создаёт адрес по почтовому индексу и шести адресным строкам.
    /// </summary>
    /// <param name="zip">Почтовый индекс: ровно 6 цифровых символов.</param>
    /// <param name="lines">
    /// Ровно 6 адресных строк. Строка длиной свыше 255 знаков обрезается,
    /// в результат добавляется предупреждение.
    /// </param>
    /// <returns>
    /// Успешный результат с адресом либо текст нарушения правила адреса.
    /// </returns>
    public static Result<Address> Create(string zip, string[] lines)
        => Create(zip, lines, null);

    /// <summary>
    /// Создаёт адрес по почтовому индексу, шести адресным строкам и исходной
    /// строке импорта.
    /// </summary>
    /// <param name="zip">Почтовый индекс: ровно 6 цифровых символов.</param>
    /// <param name="lines">
    /// Ровно 6 адресных строк. Строка длиной свыше 255 знаков обрезается,
    /// в результат добавляется предупреждение.
    /// </param>
    /// <param name="raw">
    /// Исходная строка адреса из файла импорта. Сохраняется без изменений и
    /// используется только для round-trip импорта.
    /// </param>
    /// <returns>
    /// Успешный результат с адресом либо текст нарушения правила адреса.
    /// </returns>
    public static Result<Address> Create(string zip, string[] lines, string? raw)
    {
        if (zip is null)
        {
            return Result<Address>.Failure("Почтовый индекс не задан.");
        }

        if (zip.Length != ZipLength || !zip.All(char.IsAsciiDigit))
        {
            return Result<Address>.Failure(
                $"Почтовый индекс должен состоять ровно из {ZipLength} цифровых символов.");
        }

        if (lines is null)
        {
            return Result<Address>.Failure("Список адресных строк не задан.");
        }

        if (lines.Length != LineCount)
        {
            return Result<Address>.Failure(
                $"Список адресных строк должен содержать ровно {LineCount} элементов, передано {lines.Length}.");
        }

        var warnings = new List<string>();
        var prepared = new string[LineCount];

        for (var i = 0; i < LineCount; i++)
        {
            var line = lines[i] ?? string.Empty;

            if (line.Length > MaxLineLength)
            {
                line = line[..MaxLineLength];
                warnings.Add($"Адресная строка {i + 1} длиннее {MaxLineLength} знаков — значение обрезано.");
            }

            prepared[i] = line;
        }

        return Result<Address>.Success(new Address(zip, prepared, raw), warnings.ToArray());
    }

    /// <summary>
    /// Создаёт адрес из строки в формате «индекс + 6 строк», разделённых
    /// переводом строки. Используется при разборе значения <c>*_raw</c>.
    /// </summary>
    /// <param name="raw">
    /// Исходная строка: первая строка — индекс, далее — до шести адресных строк.
    /// </param>
    /// <returns>
    /// Успешный результат с адресом либо текст нарушения правила адреса.
    /// </returns>
    public static Result<Address> Parse(string raw)
    {
        if (raw is null)
        {
            return Result<Address>.Failure("Исходная строка адреса не задана.");
        }

        var parts = raw.Split(SnapshotSeparator, StringSplitOptions.None);
        var zip = parts.Length > 0 ? parts[0].Trim() : string.Empty;
        var lines = new string[LineCount];

        for (var i = 0; i < LineCount; i++)
        {
            lines[i] = parts.Length > i + 1 ? parts[i + 1] : string.Empty;
        }

        return Create(zip, lines, raw);
    }

    /// <summary>
    /// Формирует текстовый снимок адреса для журнала напечатанных конвертов
    /// (ТЗ §3.1.7, §3.1.19: «<c>print_history</c> хранит текстовые снимки адресов
    /// на момент печати»).
    /// </summary>
    /// <returns>
    /// Снимок: индекс и шесть адресных строк, разделённые переводом строки.
    /// Пустые строки сохраняются, чтобы структура снимка не менялась.
    /// </returns>
    public string ToSnapshot()
    {
        var parts = new string[LineCount + 1];
        parts[0] = Zip;

        for (var i = 0; i < LineCount; i++)
        {
            parts[i + 1] = _lines[i];
        }

        return string.Join(SnapshotSeparator, parts);
    }

    /// <summary>
    /// Возвращает копию адреса с заменёнными адресными строками, сохраняя
    /// индекс и исходную строку импорта.
    /// </summary>
    /// <param name="lines">Новые шесть адресных строк.</param>
    /// <returns>Успешный результат с новым адресом либо текст нарушения правила адреса.</returns>
    public Result<Address> WithLines(string[] lines)
        => Create(Zip, lines, Raw);
}
