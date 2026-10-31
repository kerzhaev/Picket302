using Picket302.Domain.Entities;
using Picket302.Domain.Postal;

namespace Picket302.Printing.Templates;

/// <summary>Режим разметки нового конверта (ТЗ §8.11, функция 145).</summary>
public enum StandardLayoutMode
{
    /// <summary>«Стандартная разметка» — зоны адресов по пресету формата и страны.</summary>
    Standard = 0,

    /// <summary>«Без стандартной разметки» — поля размещаются вручную.</summary>
    None = 1,
}

/// <summary>Габариты зоны стандартной разметки, мм.</summary>
/// <param name="XMm">Положение по горизонтали, мм.</param>
/// <param name="YMm">Положение по вертикали, мм.</param>
/// <param name="WidthMm">Ширина зоны, мм.</param>
/// <param name="HeightMm">Высота зоны, мм.</param>
public sealed record LayoutZone(decimal XMm, decimal YMm, decimal WidthMm, decimal HeightMm);

/// <summary>Результат пресета стандартной разметки.</summary>
public sealed class StandardLayoutPreset
{
    /// <summary>Код формата из справочника («C6», «DL/О»).</summary>
    public string FormatCode { get; init; } = string.Empty;

    /// <summary>Код страны норматива: RUS, UKR.</summary>
    public string Country { get; init; } = EnvelopeFormatCatalog.DefaultCountryCode;

    /// <summary>Ширина конверта, мм.</summary>
    public decimal WidthMm { get; init; }

    /// <summary>Высота конверта, мм.</summary>
    public decimal HeightMm { get; init; }

    /// <summary>Зона адреса отправителя (верхний левый угол).</summary>
    public LayoutZone? SenderZone { get; init; }

    /// <summary>Зона адреса получателя (нижний правый угол).</summary>
    public LayoutZone? RecipientZone { get; init; }

    /// <summary>Зона кодового штампа индекса (ГОСТ Р 51506-99, нижний правый угол).</summary>
    public LayoutZone? PostIndexZone { get; init; }
}

/// <summary>
/// Пресеты «стандартная разметка» по связке «формат + страна» (ТЗ §8.11,
/// функция 145: <c>Printing.Templates.StandardLayoutPresets</c>; §8.5, функция 149:
/// список форматов строится из таблицы форматов стандарта, включая варианты
/// «/О» с окном, а не задается константами вручную).
/// </summary>
/// <remarks>
/// <para>Пресет определяет нормативные зоны конверта по ГОСТ Р 51506-99:
/// адрес отправителя — в верхнем левом углу, адрес получателя — в нижнем правом,
/// под ним — кодовый штамп шестизначного индекса. Для украинской серии
/// разметка совпадает по зонам, но список форматов и число знаков индекса
/// определяются справочником страны.</para>
/// <para>Режим «без стандартной разметки» возвращает пустой пресет: поля
/// размещаются пользователем вручную.</para>
/// </remarks>
public sealed class StandardLayoutPresets
{
    /// <summary>Справочник форматов.</summary>
    private readonly EnvelopeFormatCatalog _каталог;

    /// <summary>Создаёт генератор пресетов на основе справочника форматов.</summary>
    /// <param name="catalog">Справочник форматов конвертов.</param>
    public StandardLayoutPresets(EnvelopeFormatCatalog? catalog = null)
    {
        _каталог = catalog ?? EnvelopeFormatCatalog.Default;
    }

    /// <summary>
    /// Возвращает список форматов из справочника для страны, включая варианты
    /// «/О» (с окном).
    /// </summary>
    /// <param name="countryCode">Код страны: RUS, UKR.</param>
    /// <returns>Список обозначений форматов.</returns>
    public IReadOnlyList<string> GetAvailableFormats(string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        return _каталог.GetFormatCodes(countryCode);
    }

    /// <summary>
    /// Возвращает пресет стандартной разметки для формата и страны.
    /// </summary>
    /// <param name="formatCode">Обозначение формата из справочника («C6», «DL/О»).</param>
    /// <param name="countryCode">Код страны.</param>
    /// <param name="mode">Режим разметки; «без стандартной разметки» даёт пустые зоны.</param>
    /// <returns>Пресет разметки.</returns>
    /// <exception cref="KeyNotFoundException">Формат не найден в справочнике для страны.</exception>
    public StandardLayoutPreset GetPreset(
        string formatCode,
        string countryCode,
        StandardLayoutMode mode = StandardLayoutMode.Standard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        var геометрия = _каталог.Get(formatCode, countryCode, EnvelopeTerritory.Domestic);

        if (mode == StandardLayoutMode.None)
        {
            // Режим «без стандартной разметки»: поля вручную (ТЗ §8.11, функция 145).
            return new StandardLayoutPreset
            {
                FormatCode = геометрия.FormatCode,
                Country = countryCode.Trim().ToUpperInvariant(),
                WidthMm = геометрия.GetWidthMm(TemplateOrientation.Landscape),
                HeightMm = геометрия.GetHeightMm(TemplateOrientation.Landscape),
            };
        }

        // Конверт печатается в альбомной ориентации: ширина — длинная сторона.
        var ширина = геометрия.GetWidthMm(TemplateOrientation.Landscape);
        var высота = геометрия.GetHeightMm(TemplateOrientation.Landscape);

        return new StandardLayoutPreset
        {
            FormatCode = геометрия.FormatCode,
            Country = countryCode.Trim().ToUpperInvariant(),
            WidthMm = ширина,
            HeightMm = высота,
            SenderZone = new LayoutZone(12m, 8m, ширина * 0.45m, высота * 0.30m),
            RecipientZone = new LayoutZone(
                ширина - (ширина * 0.52m) - 12m,
                высота - (высота * 0.42m) - 18m,
                ширина * 0.52m,
                высота * 0.42m),
            PostIndexZone = new LayoutZone(
                ширина - 12m - (геометрия.IndexDigitCount - 1) * 5.4m - 3.7m,
                высота - 14m,
                (геометрия.IndexDigitCount - 1) * 5.4m + 3.7m,
                5.5m),
        };
    }

    /// <summary>
    /// Проверяет, является ли обозначение формата вариантом «/О» (с окном).
    /// </summary>
    /// <param name="formatCode">Обозначение формата.</param>
    /// <returns><see langword="true"/>, если формат с окном.</returns>
    public static bool IsWindowVariant(string formatCode) =>
        EnvelopeFormatCatalog.NormalizeCode(formatCode).EndsWith("/О", StringComparison.Ordinal);
}
