namespace Picket302.Printing.Elements;

/// <summary>
/// Отрисовка машиночитаемого почтового индекса — кодового штампа
/// (ТЗ §6.3; §8.6, функция 90: <c>Printing.Elements.PostIndexRenderer</c>).
/// </summary>
/// <remarks>
/// <para>Параметры штампа заданы ГОСТ Р 51506-99: ширина знака 3,7 мм (±0,1 мм),
/// высота знака 5,5 мм (±0,1 мм), толщина линии элементов цифр от 0,20 мм до
/// 0,50 мм (по умолчанию 0,45 мм), шаг между знаками 5,4 мм, шесть знаков.</para>
/// <para>Шрифт ZIPcode поставляется с программой. При его отсутствии печать
/// блокируется: скрытый фолбэк на Arial/Times запрещён (ТЗ §6.3).</para>
/// <para>Если индекс не помещается в поле, допускается пропорциональное
/// уменьшение штампа (санкция стандарта).</para>
/// </remarks>
public sealed class PostIndexRenderer
{
    /// <summary>Текст предупреждения о толщине линии вне ГОСТ.</summary>
    public const string LineWidthWarning =
        "Толщина линии вне ГОСТ (0.2-0.5мм): задано {0:0.##} мм.";

    /// <summary>Текст предупреждения о габаритах знака вне допуска ГОСТ.</summary>
    public const string DigitSizeWarning =
        "Габариты знака кодового штампа вне допуска ГОСТ ({0:0.##}±{1:0.##} мм): " +
        "ширина {2:0.##} мм, высота {3:0.##} мм.";

    /// <summary>Текст блокирующего предупреждения об отсутствии шрифта ZIPcode.</summary>
    public const string MissingFontWarning =
        "Шрифт ZIPcode не найден в системе. Требуется переустановить системные " +
        "шрифты из комплекта поставки. Подмена на Arial/Times не допускается.";

    /// <summary>Текст предупреждения о неверном имени шрифта кодового штампа.</summary>
    public const string FontNameWarning =
        "Для кодового штампа должен применяться шрифт ZIPcode (задано «{0}»).";

    /// <summary>Текст блокирующего предупреждения о неверном имени шрифта.</summary>
    public const string FontNameBlockReason =
        "Для кодового штампа должен применяться шрифт ZIPcode.";

    private readonly IZipcodeFontGuard _шрифт;

    /// <summary>
    /// Создаёт отрисовщик кодового штампа.
    /// </summary>
    /// <param name="fontGuard">Контроль наличия шрифта ZIPcode.</param>
    public PostIndexRenderer(IZipcodeFontGuard fontGuard)
    {
        ArgumentNullException.ThrowIfNull(fontGuard);

        _шрифт = fontGuard;
    }

    /// <summary>
    /// Проверяет нормативность параметров кодового штампа.
    /// </summary>
    /// <param name="spec">Параметры штампа.</param>
    /// <returns>Результат проверки.</returns>
    public PostIndexValidationResult Validate(PostIndexSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var предупреждения = new List<string>();

        if (spec.LineWidthMm > PostIndexSpec.MaxLineWidthMm)
        {
            предупреждения.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, LineWidthWarning, spec.LineWidthMm));

            return new PostIndexValidationResult
            {
                IsBlocked = true,
                Warnings = предупреждения,
                BlockReason = "Толщина линии кодового штампа превышает 0.5 мм.",
            };
        }

        if (spec.LineWidthMm < PostIndexSpec.MinLineWidthMm)
        {
            предупреждения.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, LineWidthWarning, spec.LineWidthMm));
        }

        // Допуск по габаритам знака (±0,1 мм) — предупреждение, печать не блокируется.
        var отклонениеШирины = Math.Abs(spec.DigitWidthMm - PostIndexSpec.DefaultDigitWidthMm);
        var отклонениеВысоты = Math.Abs(spec.DigitHeightMm - PostIndexSpec.DefaultDigitHeightMm);

        if (отклонениеШирины > PostIndexSpec.DigitWidthToleranceMm ||
            отклонениеВысоты > PostIndexSpec.DigitHeightToleranceMm)
        {
            предупреждения.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                DigitSizeWarning,
                PostIndexSpec.DefaultDigitWidthMm,
                PostIndexSpec.DigitWidthToleranceMm,
                spec.DigitWidthMm,
                spec.DigitHeightMm));
        }

        if (!string.Equals(spec.FontName, PostIndexSpec.DefaultFontName, StringComparison.OrdinalIgnoreCase))
        {
            предупреждения.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                FontNameWarning,
                spec.FontName));

            return new PostIndexValidationResult
            {
                IsBlocked = true,
                Warnings = предупреждения,
                BlockReason = FontNameBlockReason,
            };
        }

        if (!_шрифт.IsZipcodeFontAvailable)
        {
            предупреждения.Add(MissingFontWarning);

            return new PostIndexValidationResult
            {
                IsBlocked = true,
                Warnings = предупреждения,
                BlockReason = "Не найден шрифт ZIPcode.",
            };
        }

        return new PostIndexValidationResult
        {
            IsBlocked = false,
            Warnings = предупреждения,
        };
    }

    /// <summary>
    /// Проверяет наличие шрифта ZIPcode, при необходимости регистрируя его.
    /// </summary>
    /// <returns><see langword="true"/>, если шрифт доступен.</returns>
    public bool EnsureZipcodeFontInstalled() => _шрифт.EnsureZipcodeFontInstalled();

    /// <summary>
    /// Отрисовывает машиночитаемый индекс кодовым штампом.
    /// </summary>
    /// <param name="context">Графический контекст.</param>
    /// <param name="zip">Почтовый индекс.</param>
    /// <param name="spec">Параметры штампа.</param>
    /// <exception cref="PostIndexBlockedException">
    /// Параметры штампа нарушают нормативы либо отсутствует шрифт ZIPcode.
    /// </exception>
    public void RenderMachineReadableIndex(IGraphicsContext context, string zip, PostIndexSpec spec)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spec);

        var проверка = Validate(spec);

        if (проверка.IsBlocked)
        {
            throw new PostIndexBlockedException(проверка.BlockReason ?? "Печать кодового штампа заблокирована.");
        }

        var знаки = ОпределитьЗнаки(zip, spec);
        var масштаб = ВычислитьМасштаб(spec);

        // Семейство берётся у гарда: гарнитура «ZIPcode» может отсутствовать в
        // перечислении установленных шрифтов (файл комплекта поставки не
        // регистрируется в системе), и создание шрифта по имени молча дало бы
        // «Microsoft Sans Serif» — на конверте вместо индекса печаталась бы
        // бессмыслица.
        var семейство = _шрифт.PostIndexFontFamily;

        if (семейство is null)
        {
            throw new PostIndexBlockedException("Не найден шрифт ZIPcode.");
        }

        for (var индекс = 0; индекс < знаки.Count; индекс++)
        {
            context.DrawStringMm(
                знаки[индекс],
                семейство,
                (float)(spec.DigitWidthMm * масштаб),
                (float)(spec.DigitHeightMm * масштаб),
                spec.OriginXMm + (float)(индекс * spec.AdvanceMm * масштаб),
                spec.OriginYMm);
        }
    }

    /// <summary>
    /// Возвращает знаки кодового штампа: для международного отправления —
    /// фиксированный индекс «555», для внутреннего — индекс, дополненный нулями
    /// до нормативного числа знаков.
    /// </summary>
    /// <param name="zip">Почтовый индекс.</param>
    /// <param name="spec">Параметры штампа.</param>
    /// <returns>Список знаков штампа.</returns>
    public static IReadOnlyList<string> ОпределитьЗнаки(string zip, PostIndexSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!string.IsNullOrWhiteSpace(spec.InternationalIndex))
        {
            return spec.InternationalIndex.Trim().ToCharArray().Select(знак => знак.ToString()).ToList();
        }

        var цифры = new string((zip ?? string.Empty).Where(char.IsDigit).ToArray());

        if (цифры.Length > spec.DigitCount)
        {
            цифры = цифры[^spec.DigitCount..];
        }

        цифры = цифры.PadLeft(spec.DigitCount, '0');

        return цифры.Select(знак => знак.ToString()).ToList();
    }

    /// <summary>
    /// Вычисляет коэффициент пропорционального уменьшения штампа при нехватке
    /// места в поле индекса.
    /// </summary>
    /// <param name="spec">Параметры штампа.</param>
    /// <returns>Коэффициент масштаба; 1 — уменьшение не требуется.</returns>
    public static decimal ВычислитьМасштаб(PostIndexSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.AvailableWidthMm is not { } доступнаяШирина || доступнаяШирина <= 0)
        {
            return 1m;
        }

        // Ширина штампа: расстояние от начала первого знака до конца последнего —
        // (число знаков − 1) × шаг + ширина знака.
        var требуемаяШирина = ((spec.DigitCount - 1) * spec.AdvanceMm) + spec.DigitWidthMm;

        if (требуемаяШирина <= доступнаяШирина)
        {
            return 1m;
        }

        return доступнаяШирина / требуемаяШирина;
    }
}
