using System.Reflection;
using System.Text.Json;
using Picket302.Domain.Entities;

namespace Picket302.Domain.Postal;

/// <summary>
/// Справочник форматов конвертов (ТЗ §8.5, функция 149: «Список форматов строится
/// из таблицы форматов стандарта (включая варианты "/О" с окном), а не задается
/// константами вручную»; §6.3, ГОСТ Р 51506-99).
/// </summary>
/// <remarks>
/// Справочник загружается из встроенного ресурса <c>Postal/EnvelopeFormats.json</c>:
/// размеры, тип закрывающего клапана (Б/В), территория хождения (Вн/Мн) и варианты
/// «/О» с окном хранятся данными, а не константами кода.
/// </remarks>
public sealed class EnvelopeFormatCatalog
{
    /// <summary>Имя встроенного ресурса со справочником форматов.</summary>
    private const string ResourceName = "Picket302.Domain.Postal.EnvelopeFormats.json";

    /// <summary>Код страны по умолчанию (внутренние отправления Российской Федерации).</summary>
    public const string DefaultCountryCode = "RUS";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly IReadOnlyList<EnvelopeGeometry> _formats;

    private EnvelopeFormatCatalog(IReadOnlyList<EnvelopeGeometry> formats)
    {
        _formats = formats;
    }

    /// <summary>Справочник, загруженный из встроенного ресурса.</summary>
    public static EnvelopeFormatCatalog Default { get; } = LoadDefault();

    /// <summary>
    /// Возвращает геометрию формата для страны; при наличии вариантов «Вн» и «Мн»
    /// выбирается внутренний.
    /// </summary>
    /// <param name="formatCode">Обозначение формата («C6», «C5/О»).</param>
    /// <param name="countryCode">Код страны отправления.</param>
    /// <returns>Геометрия формата.</returns>
    /// <exception cref="KeyNotFoundException">Формат не найден для указанной страны.</exception>
    public EnvelopeGeometry Get(string formatCode, string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        var нормализованныйКод = NormalizeCode(formatCode);
        var нормализованнаяСтрана = countryCode.Trim().ToUpperInvariant();

        foreach (var территория in new[] { EnvelopeTerritory.Domestic, EnvelopeTerritory.International })
        {
            var найдено = Найти(нормализованныйКод, нормализованнаяСтрана, территория);
            if (найдено is not null)
            {
                return найдено;
            }
        }

        throw new KeyNotFoundException(
            $"Формат конверта «{formatCode}» не найден для страны «{countryCode}».");
    }

    /// <summary>
    /// Возвращает геометрию формата для страны и территории хождения.
    /// </summary>
    /// <param name="formatCode">Обозначение формата («C6», «C5/О»).</param>
    /// <param name="countryCode">Код страны отправления.</param>
    /// <param name="territory">Территория хождения («Вн» либо «Мн»).</param>
    /// <returns>Геометрия формата.</returns>
    /// <exception cref="KeyNotFoundException">Формат не найден для указанных страны и территории.</exception>
    public EnvelopeGeometry Get(
        string formatCode,
        string countryCode,
        EnvelopeTerritory territory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        var нормализованныйКод = NormalizeCode(formatCode);
        var нормализованнаяСтрана = countryCode.Trim().ToUpperInvariant();
        var найдено = Найти(нормализованныйКод, нормализованнаяСтрана, territory);

        if (найдено is null)
        {
            throw new KeyNotFoundException(
                $"Формат конверта «{formatCode}» не найден для страны «{countryCode}» " +
                $"и территории «{territory}».");
        }

        return найдено;
    }

    /// <summary>
    /// Возвращает геометрию формата по перечислению (ТЗ §8.4, функция 42).
    /// </summary>
    /// <param name="format">Формат конверта.</param>
    /// <param name="orientation">
    /// Ориентация макета. Геометрия формата от ориентации не зависит: ориентация
    /// определяет, какая сторона становится шириной макета, — см.
    /// <see cref="EnvelopeGeometry.GetWidthMm"/>.
    /// </param>
    /// <returns>Геометрия формата.</returns>
    public EnvelopeGeometry Get(EnvelopeFormat format, TemplateOrientation orientation)
    {
        _ = orientation;

        return Get(format.ToString(), DefaultCountryCode, EnvelopeTerritory.Domestic);
    }

    /// <summary>Возвращает все форматы, применимые для страны.</summary>
    /// <param name="countryCode">Код страны отправления.</param>
    /// <returns>Список геометрий форматов.</returns>
    public IReadOnlyList<EnvelopeGeometry> GetAll(string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        var нормализованнаяСтрана = countryCode.Trim().ToUpperInvariant();

        return _formats
            .Where(формат => формат.Countries.Contains(нормализованнаяСтрана, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Возвращает все форматы, применимые для страны и территории хождения.</summary>
    /// <param name="countryCode">Код страны отправления.</param>
    /// <param name="territory">Территория хождения.</param>
    /// <returns>Список геометрий форматов.</returns>
    public IReadOnlyList<EnvelopeGeometry> GetAll(string countryCode, EnvelopeTerritory territory)
    {
        return GetAll(countryCode)
            .Where(формат => формат.Territory == territory)
            .ToList();
    }

    /// <summary>Возвращает обозначения форматов, применимых для страны.</summary>
    /// <param name="countryCode">Код страны отправления.</param>
    /// <returns>Список обозначений форматов без повторов.</returns>
    public IReadOnlyList<string> GetFormatCodes(string countryCode)
    {
        var коды = new List<string>();
        var учтённые = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var формат in GetAll(countryCode))
        {
            if (учтённые.Add(формат.FormatCode))
            {
                коды.Add(формат.FormatCode);
            }
        }

        return коды;
    }

    /// <summary>
    /// Пытается получить геометрию формата для страны и территории хождения.
    /// </summary>
    /// <param name="formatCode">Обозначение формата.</param>
    /// <param name="countryCode">Код страны отправления.</param>
    /// <param name="territory">Территория хождения.</param>
    /// <param name="geometry">Полученная геометрия либо <see langword="null"/>.</param>
    /// <returns><see langword="true"/>, если формат найден.</returns>
    public bool TryGet(
        string formatCode,
        string countryCode,
        EnvelopeTerritory territory,
        out EnvelopeGeometry? geometry)
    {
        geometry = null;

        if (string.IsNullOrWhiteSpace(formatCode) || string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        geometry = Найти(
            NormalizeCode(formatCode),
            countryCode.Trim().ToUpperInvariant(),
            territory);

        return geometry is not null;
    }

    /// <summary>
    /// Загружает справочник из встроенного ресурса.
    /// </summary>
    /// <returns>Справочник форматов.</returns>
    public static EnvelopeFormatCatalog LoadDefault()
    {
        var сборка = typeof(EnvelopeFormatCatalog).Assembly;

        using var поток = сборка.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Встроенный ресурс со справочником форматов «{ResourceName}» не найден.");

        return Load(поток);
    }

    /// <summary>
    /// Загружает справочник из потока с JSON-документом.
    /// </summary>
    /// <param name="jsonStream">Поток с содержимым справочника.</param>
    /// <returns>Справочник форматов.</returns>
    public static EnvelopeFormatCatalog Load(Stream jsonStream)
    {
        ArgumentNullException.ThrowIfNull(jsonStream);

        var документ = JsonSerializer.Deserialize<КаталогДокумент>(jsonStream, JsonOptions)
            ?? throw new InvalidOperationException("Справочник форматов конвертов пуст.");

        var форматы = new List<EnvelopeGeometry>(документ.Formats.Count);

        foreach (var запись in документ.Formats)
        {
            форматы.Add(СоздатьГеометрию(запись));
        }

        if (форматы.Count == 0)
        {
            throw new InvalidOperationException("Справочник форматов конвертов не содержит ни одной записи.");
        }

        return new EnvelopeFormatCatalog(форматы);
    }

    /// <summary>
    /// Приводит обозначение формата к сравнимому виду: без пробелов, в верхнем регистре,
    /// с кириллической буквой «О» в обозначении варианта с окном.
    /// </summary>
    /// <param name="formatCode">Обозначение формата.</param>
    /// <returns>Нормализованное обозначение.</returns>
    public static string NormalizeCode(string formatCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatCode);

        return formatCode
            .Trim()
            .Replace('O', 'О')
            .ToUpperInvariant();
    }

    private EnvelopeGeometry? Найти(string код, string страна, EnvelopeTerritory территория)
    {
        foreach (var формат in _formats)
        {
            if (формат.Territory != территория)
            {
                continue;
            }

            if (!string.Equals(формат.FormatCode, код, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (формат.Countries.Contains(страна, StringComparer.OrdinalIgnoreCase))
            {
                return формат;
            }
        }

        return null;
    }

    private static EnvelopeGeometry СоздатьГеометрию(КаталогЗапись запись)
    {
        ArgumentNullException.ThrowIfNull(запись);

        if (string.IsNullOrWhiteSpace(запись.Code))
        {
            throw new InvalidOperationException("Запись справочника форматов не содержит обозначения.");
        }

        if (запись.HeightMm <= 0 || запись.LengthMm <= 0)
        {
            throw new InvalidOperationException(
                $"Запись справочника «{запись.Code}» содержит недопустимые размеры формата.");
        }

        return new EnvelopeGeometry(
            запись.Code.Trim(),
            запись.DisplayName ?? запись.Code.Trim(),
            запись.HeightMm,
            запись.LengthMm,
            РазобратьКлапан(запись.FlapType),
            РазобратьТерриторию(запись.Territory),
            запись.HasWindow,
            запись.IndexDigitCount,
            запись.InternationalIndex,
            запись.AddressOrderSmallToLarge,
            запись.Countries ?? new List<string>());
    }

    private static EnvelopeFlapType РазобратьКлапан(string? значение)
    {
        var нормализовано = (значение ?? string.Empty)
            .Trim()
            .Replace('B', 'В')
            .Replace(" ", string.Empty)
            .ToUpperInvariant();

        return нормализовано switch
        {
            "Б" => EnvelopeFlapType.Side,
            "В" => EnvelopeFlapType.Top,
            "В/Б" => EnvelopeFlapType.SideOrTop,
            _ => throw new InvalidOperationException(
                $"Неизвестный тип закрывающего клапана «{значение}»."),
        };
    }

    private static EnvelopeTerritory РазобратьТерриторию(string? значение)
    {
        var нормализовано = (значение ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .ToUpperInvariant();

        return нормализовано switch
        {
            "ВН" => EnvelopeTerritory.Domestic,
            "МН" => EnvelopeTerritory.International,
            _ => throw new InvalidOperationException(
                $"Неизвестная территория хождения «{значение}»."),
        };
    }

    private sealed class КаталогДокумент
    {
        public List<КаталогЗапись> Formats { get; set; } = new();
    }

    private sealed class КаталогЗапись
    {
        public string? Code { get; set; }

        public string? DisplayName { get; set; }

        public decimal HeightMm { get; set; }

        public decimal LengthMm { get; set; }

        public string? FlapType { get; set; }

        public string? Territory { get; set; }

        public bool HasWindow { get; set; }

        public List<string>? Countries { get; set; }

        public int IndexDigitCount { get; set; }

        public string? InternationalIndex { get; set; }

        public bool AddressOrderSmallToLarge { get; set; }
    }
}
