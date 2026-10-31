using Picket302.Domain.Entities;

namespace Picket302.Domain.Postal;

/// <summary>
/// Геометрия формата конверта (ТЗ §6.3; ГОСТ Р 51506-99, таблицы 1 и 2).
/// </summary>
/// <remarks>
/// Размеры приведены по стандарту в обозначении «высота H × длина L». Ширина и высота
/// макета зависят от ориентации и вычисляются методами
/// <see cref="GetWidthMm"/> и <see cref="GetHeightMm"/>.
/// </remarks>
public sealed class EnvelopeGeometry
{
    /// <summary>
    /// Создаёт геометрию формата.
    /// </summary>
    /// <param name="formatCode">Обозначение формата («C6», «C5/О»).</param>
    /// <param name="displayName">Отображаемое наименование формата.</param>
    /// <param name="heightMm">Высота H по ГОСТ Р 51506-99, мм.</param>
    /// <param name="lengthMm">Длина L по ГОСТ Р 51506-99, мм.</param>
    /// <param name="flapType">Тип конверта по месту расположения закрывающего клапана.</param>
    /// <param name="territory">Территория хождения конверта.</param>
    /// <param name="hasWindow">Признак изготовления с окном на месте зоны адреса адресата.</param>
    /// <param name="indexDigitCount">Число знаков кодового штампа.</param>
    /// <param name="internationalIndex">Индекс, вписываемый в трёхзначный штамп («555»).</param>
    /// <param name="addressOrderSmallToLarge">
    /// Признак вывода адреса от мелкого к крупному (порядок международных отправлений).
    /// </param>
    /// <param name="countries">Коды стран, для которых формат применяется.</param>
    public EnvelopeGeometry(
        string formatCode,
        string displayName,
        decimal heightMm,
        decimal lengthMm,
        EnvelopeFlapType flapType,
        EnvelopeTerritory territory,
        bool hasWindow,
        int indexDigitCount,
        string? internationalIndex,
        bool addressOrderSmallToLarge,
        IReadOnlyList<string> countries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatCode);
        ArgumentNullException.ThrowIfNull(countries);

        FormatCode = formatCode;
        DisplayName = displayName;
        HeightMm = heightMm;
        LengthMm = lengthMm;
        FlapType = flapType;
        Territory = territory;
        HasWindow = hasWindow;
        IndexDigitCount = indexDigitCount;
        InternationalIndex = internationalIndex;
        AddressOrderSmallToLarge = addressOrderSmallToLarge;
        Countries = countries;
    }

    /// <summary>Обозначение формата.</summary>
    public string FormatCode { get; }

    /// <summary>Отображаемое наименование формата.</summary>
    public string DisplayName { get; }

    /// <summary>Высота H по ГОСТ Р 51506-99, мм (короткая сторона).</summary>
    public decimal HeightMm { get; }

    /// <summary>Длина L по ГОСТ Р 51506-99, мм (длинная сторона).</summary>
    public decimal LengthMm { get; }

    /// <summary>Тип конверта по месту расположения закрывающего клапана.</summary>
    public EnvelopeFlapType FlapType { get; }

    /// <summary>Территория хождения конверта.</summary>
    public EnvelopeTerritory Territory { get; }

    /// <summary>Признак изготовления с окном на месте зоны адреса адресата.</summary>
    public bool HasWindow { get; }

    /// <summary>Число знаков кодового штампа: 6 для «Вн», 3 для «Мн».</summary>
    public int IndexDigitCount { get; }

    /// <summary>Индекс, вписываемый в трёхзначный штамп международного отправления.</summary>
    public string? InternationalIndex { get; }

    /// <summary>
    /// Признак вывода адреса от мелкого к крупному — порядок оформления
    /// международных отправлений.
    /// </summary>
    public bool AddressOrderSmallToLarge { get; }

    /// <summary>Коды стран, для которых формат применяется.</summary>
    public IReadOnlyList<string> Countries { get; }

    /// <summary>
    /// Ширина макета при заданной ориентации: длинная сторона в альбомной,
    /// короткая — в книжной.
    /// </summary>
    /// <param name="orientation">Ориентация макета.</param>
    /// <returns>Ширина макета, мм.</returns>
    public decimal GetWidthMm(TemplateOrientation orientation) =>
        orientation == TemplateOrientation.Landscape ? LengthMm : HeightMm;

    /// <summary>
    /// Высота макета при заданной ориентации: короткая сторона в альбомной,
    /// длинная — в книжной.
    /// </summary>
    /// <param name="orientation">Ориентация макета.</param>
    /// <returns>Высота макета, мм.</returns>
    public decimal GetHeightMm(TemplateOrientation orientation) =>
        orientation == TemplateOrientation.Landscape ? HeightMm : LengthMm;
}
