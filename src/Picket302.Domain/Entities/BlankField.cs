namespace Picket302.Domain.Entities;

/// <summary>
/// Координатная сетка полей бланка (ТЗ §3.1.5, таблица <c>blank_fields</c>).
/// </summary>
public sealed class BlankField
{
    /// <summary>Идентификатор поля (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Шаблон, которому принадлежит поле.</summary>
    public long TemplateId { get; set; }

    /// <summary>Наименование поля.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Тип поля: <c>text</c>, <c>post_index</c>, <c>qr</c>, <c>picture</c>, <c>barcode</c>.</summary>
    public string Kind { get; set; } = BlankFieldKind.Text;

    /// <summary>Номер страницы.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Положение по горизонтали, мм.</summary>
    public decimal XMm { get; set; }

    /// <summary>Положение по вертикали, мм.</summary>
    public decimal YMm { get; set; }

    /// <summary>Длина поля, мм.</summary>
    public decimal LengthMm { get; set; }

    /// <summary>Высота поля, мм.</summary>
    public decimal HeightMm { get; set; }

    /// <summary>
    /// Поворот текста в поле, градусов: 0/90/180/270.
    /// </summary>
    /// <remarks>
    /// На конвертах адрес печатается вертикальной колонкой (снизу вверх) —
    /// так напечатаны рамки на бланках Почты России.
    /// </remarks>
    public int Rotation { get; set; }

    /// <summary>Гарнитура шрифта.</summary>
    public string FontName { get; set; } = "Tahoma";

    /// <summary>Кегль шрифта, пунктов.</summary>
    public decimal FontHeightPt { get; set; } = 10.0m;

    /// <summary>Минимальный кегль при автоподборе, пунктов.</summary>
    public decimal FontHeightMinPt { get; set; } = 6.0m;

    /// <summary>Максимальный кегль при автоподборе, пунктов.</summary>
    public decimal FontHeightMaxPt { get; set; } = 14.0m;

    /// <summary>Признак полужирного начертания.</summary>
    public bool IsBold { get; set; }

    /// <summary>Признак курсивного начертания.</summary>
    public bool IsItalic { get; set; }

    /// <summary>Цвет текста в формате RGB.</summary>
    public int ColorRgb { get; set; }

    /// <summary>Выравнивание текста.</summary>
    public FieldAlignment Align { get; set; } = FieldAlignment.Left;

    /// <summary>Максимальное число знаков в поле.</summary>
    public int MaxChars { get; set; } = 255;

    /// <summary>Признак видимости поля в редакторе.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Признак печати поля.</summary>
    public bool IsPrintable { get; set; } = true;

    /// <summary>
    /// Роль зоны: <c>TopSender</c>, <c>BottomRecipient</c>, <c>PostIndexZone</c>,
    /// <c>InfoZone</c>.
    /// </summary>
    public string? ZoneRole { get; set; }

    /// <summary>
    /// Идентификатор клипарта; только для вида <c>picture</c> (S26,
    /// функция 158). NULL/пусто — поле ведёт себя как прежний логотип.
    /// </summary>
    public string? ClipartId { get; set; }

    /// <summary>
    /// Маркер версии записи: значение, предъявляемое при изменении, чтобы
    /// служба отклонила правку устаревшей копии.
    /// </summary>
    /// <remarks>
    /// <para>
    /// В совместном режиме версию ведёт служба в отдельной таблице
    /// <c>shared_row_versions</c>; в DTO записи она передаётся клиенту и
    /// обратно. В одиночном режиме версия не ведётся и остаётся равной нулю.
    /// </para>
    /// <para>
    /// Само поле в таблицах базы отсутствует: существующие репозитории и
    /// одиночный режим остаются неизменными.
    /// </para>
    /// </remarks>
    public long Version { get; set; }

}
