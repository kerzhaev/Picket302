namespace Picket302.Domain.Entities;

/// <summary>
/// Ориентация макета (ТЗ §3.1.4, колонка <c>print_templates.orientation</c>).
/// </summary>
public enum TemplateOrientation
{
    /// <summary>Книжная.</summary>
    Portrait = 0,

    /// <summary>Альбомная.</summary>
    Landscape = 1,
}

/// <summary>
/// Выравнивание текста в поле бланка (ТЗ §3.1.5, колонка <c>blank_fields.align</c>).
/// </summary>
public enum FieldAlignment
{
    /// <summary>По левому краю.</summary>
    Left = 0,

    /// <summary>По центру.</summary>
    Center = 1,

    /// <summary>По правому краю.</summary>
    Right = 2,

    /// <summary>По ширине.</summary>
    Justify = 3,
}

/// <summary>
/// Тип поля бланка (ТЗ §3.1.5, колонка <c>blank_fields.kind</c>):
/// <c>text</c>, <c>post_index</c>, <c>qr</c>, <c>picture</c>, <c>barcode</c>.
/// </summary>
public static class BlankFieldKind
{
    /// <summary>Текстовое поле.</summary>
    public const string Text = "text";

    /// <summary>Поле почтового индекса (кодовый штамп).</summary>
    public const string PostIndex = "post_index";

    /// <summary>Двумерный код.</summary>
    public const string Qr = "qr";

    /// <summary>Растровое изображение.</summary>
    public const string Picture = "picture";

    /// <summary>Линейный штрих-код.</summary>
    public const string Barcode = "barcode";

    /// <summary>Все допустимые значения поля.</summary>
    public static IReadOnlyList<string> All { get; } =
        new[] { Text, PostIndex, Qr, Picture, Barcode };
}

/// <summary>
/// Вид шаблона (ТЗ §3.1.4, колонка <c>print_templates.kind</c>): <c>envelope</c>,
/// <c>blank</c>, <c>report</c>, <c>label</c>, <c>notification</c>.
/// </summary>
public static class PrintTemplateKind
{
    /// <summary>Конверт.</summary>
    public const string Envelope = "envelope";

    /// <summary>Бланк.</summary>
    public const string Blank = "blank";

    /// <summary>Отчёт.</summary>
    public const string Report = "report";

    /// <summary>Этикетка.</summary>
    public const string Label = "label";

    /// <summary>Уведомление.</summary>
    public const string Notification = "notification";

    /// <summary>Все допустимые значения поля.</summary>
    public static IReadOnlyList<string> All { get; } =
        new[] { Envelope, Blank, Report, Label, Notification };
}

/// <summary>
/// Режим печати (ТЗ §3.1.6, колонка <c>print_jobs.print_mode</c>): <c>selected</c>,
/// <c>batch_preview</c>, <c>batch_direct</c>, <c>test</c>.
/// </summary>
/// <remarks>
/// Значение <c>test</c> требуется ТЗ §8.1, функция 143 «Пробная печать
/// шаблона»: «журнал print_jobs (print_mode = test)». Перечисление §3.1.6
/// дополняется этим значением — расхождение зафиксировано в
/// docs/OPEN_QUESTIONS_LOG.md (допущение S2.20).
/// </remarks>
public static class PrintModes
{
    /// <summary>Печать выбранных отправлений.</summary>
    public const string Selected = "selected";

    /// <summary>Предпросмотр партии.</summary>
    public const string BatchPreview = "batch_preview";

    /// <summary>Прямая печать партии.</summary>
    public const string BatchDirect = "batch_direct";

    /// <summary>Пробная печать одного экземпляра (ТЗ §8.1, функция 143).</summary>
    public const string Test = "test";

    /// <summary>Все допустимые значения поля.</summary>
    public static IReadOnlyList<string> All { get; } =
        new[] { Selected, BatchPreview, BatchDirect, Test };
}

/// <summary>
/// Итог печатной сессии (ТЗ §3.1.6, колонка <c>print_jobs.status</c>): <c>SUCCESS</c>,
/// <c>ABORTED</c>, <c>FAILED</c>, <c>BLOCKED_SIZE</c>.
/// </summary>
public static class PrintJobStatus
{
    /// <summary>Печать выполнена.</summary>
    public const string Success = "SUCCESS";

    /// <summary>Печать прервана пользователем.</summary>
    public const string Aborted = "ABORTED";

    /// <summary>Печать завершилась ошибкой.</summary>
    public const string Failed = "FAILED";

    /// <summary>Размер носителя не соответствует возможностям принтера.</summary>
    public const string BlockedSize = "BLOCKED_SIZE";

    /// <summary>Все допустимые значения поля.</summary>
    public static IReadOnlyList<string> All { get; } =
        new[] { Success, Aborted, Failed, BlockedSize };
}

/// <summary>
/// Подача носителя в принтере (ТЗ §3.1.16, колонка <c>printer_profiles.feed_alignment</c>).
/// </summary>
public enum FeedAlignment
{
    /// <summary>По центру.</summary>
    Center = 0,

    /// <summary>По левому краю.</summary>
    Left = 1,

    /// <summary>По правому краю.</summary>
    Right = 2,
}
