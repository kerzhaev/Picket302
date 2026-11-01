using Picket302.Domain.Serialization;

namespace Picket302.Printing.Templates;

/// <summary>
/// Итог применения пресета «стандартная разметка» к макету бланка
/// (ТЗ §8.11, функция 145).
/// </summary>
/// <param name="Применено">Признак успешного применения пресета.</param>
/// <param name="ПеремещеноПолей">Число полей, размещённых по нормативным зонам.</param>
/// <param name="Предупреждение">Пояснение для оператора; при отсутствии замечаний — <see langword="null"/>.</param>
public sealed record StandardLayoutResult(bool Применено, int ПеремещеноПолей, string? Предупреждение);

/// <summary>
/// Применяет пресет «стандартная разметка» к макету бланка и переводит макет
/// в режим «без стандартной разметки» (ТЗ §8.11, функция 145:
/// «Пресет "стандартная разметка" для формата + режим "без стандартной
/// разметки" (поля вручную)»).
/// </summary>
/// <remarks>
/// <para>
/// Адресные поля бланка размещаются по нормативным зонам пресета: адрес
/// отправителя — в верхней левой зоне, адрес получателя — в нижней правой,
/// кодовый штамп индекса — под адресом получателя (ГОСТ Р 51506-99).
/// </para>
/// <para>
/// В режиме «без стандартной разметки» положение полей не меняется: оператор
/// размещает их вручную, а признак стандартной разметки снимается.
/// </para>
/// </remarks>
public sealed class StandardLayoutApplier
{
    /// <summary>Имя поля адреса получателя.</summary>
    public const string ПолеКому = "строки «Кому»";

    /// <summary>Имя поля адреса получателя (улица, дом).</summary>
    public const string ПолеКуда = "строки «Куда»";

    /// <summary>Имя поля адреса отправителя.</summary>
    public const string ПолеОтКого = "строки «От кого»";

    /// <summary>Имя поля адреса отправителя (улица, дом).</summary>
    public const string ПолеОтКуда = "строки «Откуда»";

    /// <summary>Высота адресного поля, мм (совпадает с раскладкой встроенных форм).</summary>
    private const decimal ВысотаАдресногоПоля = 18m;

    /// <summary>Зазор между полями по вертикали, мм.</summary>
    private const decimal Зазор = 1m;

    private readonly StandardLayoutPresets _пресеты;

    /// <summary>Создаёт исполнитель пресета разметки.</summary>
    /// <param name="пресеты">Генератор пресетов; при отсутствии — штатный.</param>
    public StandardLayoutApplier(StandardLayoutPresets? пресеты = null)
    {
        _пресеты = пресеты ?? new StandardLayoutPresets();
    }

    /// <summary>
    /// Применяет режим разметки к макету бланка.
    /// </summary>
    /// <param name="бланк">Макет бланка.</param>
    /// <param name="формат">Обозначение формата конверта («C6», «DL/О»).</param>
    /// <param name="страна">Код страны норматива: RUS, UKR.</param>
    /// <param name="режим">Режим разметки.</param>
    /// <returns>Итог применения пресета.</returns>
    /// <exception cref="ArgumentNullException">Макет не задан.</exception>
    public StandardLayoutResult Применить(
        CustomBlank бланк,
        string формат,
        string страна,
        StandardLayoutMode режим = StandardLayoutMode.Standard)
    {
        ArgumentNullException.ThrowIfNull(бланк);

        if (режим == StandardLayoutMode.None)
        {
            // Режим «без стандартной разметки»: поля размещаются вручную,
            // текущее положение сохраняется (ТЗ §8.11, функция 145).
            бланк.StandardLayout = false;

            return new StandardLayoutResult(true, 0, null);
        }

        var пресет = _пресеты.GetPreset(формат, страна, режим);

        бланк.StandardLayout = true;
        бланк.Country = пресет.Country;

        var перемещено = 0;

        перемещено += Разместить(
            бланк,
            пресет.SenderZone,
            [ПолеОтКого, ПолеОтКуда]);

        перемещено += Разместить(
            бланк,
            пресет.RecipientZone,
            [ПолеКому, ПолеКуда]);

        перемещено += РазместитьШтамп(бланк, пресет.PostIndexZone);

        var предупреждение = перемещено == 0
            ? "На макете нет полей адреса и кодового штампа: пресет разметки применён только к геометрии бланка."
            : null;

        return new StandardLayoutResult(true, перемещено, предупреждение);
    }

    /// <summary>
    /// Размещает адресные поля внутри нормативной зоны, сверху вниз.
    /// </summary>
    /// <param name="бланк">Макет бланка.</param>
    /// <param name="зона">Нормативная зона.</param>
    /// <param name="имена">Имена полей адреса в порядке размещения.</param>
    /// <returns>Число размещённых полей.</returns>
    private static int Разместить(CustomBlank бланк, LayoutZone? зона, IReadOnlyList<string> имена)
    {
        if (зона is null)
        {
            return 0;
        }

        var размещено = 0;
        var верх = зона.YMm;

        foreach (var имя in имена)
        {
            var поле = НайтиПоле(бланк, имя);

            if (поле is null)
            {
                continue;
            }

            поле.Bounds.XMm = зона.XMm;
            поле.Bounds.YMm = верх;
            поле.Bounds.WidthMm = зона.WidthMm;
            поле.Bounds.HeightMm = ВысотаАдресногоПоля;

            верх += ВысотаАдресногоПоля + Зазор;
            размещено++;
        }

        return размещено;
    }

    /// <summary>
    /// Размещает поле кодового штампа индекса в нормативной зоне.
    /// </summary>
    /// <param name="бланк">Макет бланка.</param>
    /// <param name="зона">Нормативная зона кодового штампа.</param>
    /// <returns>Число размещённых полей.</returns>
    private static int РазместитьШтамп(CustomBlank бланк, LayoutZone? зона)
    {
        if (зона is null)
        {
            return 0;
        }

        var штамп = бланк.Pages
            .SelectMany(страница => страница.Fields)
            .FirstOrDefault(поле => поле.Kind == BlankFieldKinds.PostIndex);

        if (штамп is null)
        {
            return 0;
        }

        штамп.Bounds.XMm = зона.XMm;
        штамп.Bounds.YMm = зона.YMm;
        штамп.Bounds.WidthMm = зона.WidthMm;
        штамп.Bounds.HeightMm = зона.HeightMm;

        return 1;
    }

    /// <summary>
    /// Ищет поле макета по имени.
    /// </summary>
    /// <param name="бланк">Макет бланка.</param>
    /// <param name="имя">Имя поля.</param>
    /// <returns>Поле макета либо <see langword="null"/>.</returns>
    private static BlankFieldSpec? НайтиПоле(CustomBlank бланк, string имя)
        => бланк.Pages
            .SelectMany(страница => страница.Fields)
            .FirstOrDefault(поле => string.Equals(поле.Name, имя, StringComparison.OrdinalIgnoreCase));
}
