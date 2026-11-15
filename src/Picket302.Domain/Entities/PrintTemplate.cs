namespace Picket302.Domain.Entities;

/// <summary>
/// Шаблон конверта или бланка (ТЗ §3.1.4, таблица <c>print_templates</c>).
/// </summary>
/// <remarks>
/// Шаблон хранит только метаданные: геометрия полей лежит в <c>blank_fields</c>,
/// а сам макет — в файле пакета <c>.lbk</c>, путь к которому указан в
/// <see cref="FilePath"/> (ТЗ §2.1 «Файловая независимость макетов»).
/// </remarks>
public sealed class PrintTemplate
{
    /// <summary>Идентификатор шаблона (первичный ключ).</summary>
    public long Id { get; set; }

    /// <summary>Наименование шаблона.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Вид шаблона: <c>envelope</c>, <c>blank</c>, <c>report</c>, <c>label</c>, <c>notification</c>.</summary>
    public string Kind { get; set; } = PrintTemplateKind.Envelope;

    /// <summary>Формат: C6, DL, C5, C4, B4, CUSTOM.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Ширина макета, мм.</summary>
    public decimal WidthMm { get; set; }

    /// <summary>Высота макета, мм.</summary>
    public decimal HeightMm { get; set; }

    /// <summary>Ориентация макета.</summary>
    public TemplateOrientation Orientation { get; set; } = TemplateOrientation.Portrait;

    /// <summary>Относительный путь к архиву макета <c>.lbk</c>.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Признак встроенного макета: физическое удаление запрещено.</summary>
    public bool IsBuiltin { get; set; }

    /// <summary>Страна норматива разметки полей.</summary>
    public string Country { get; set; } = "RUS";

    /// <summary>
    /// Идентификатор управляемого пакета шаблона (.lbk), в котором лежит
    /// содержимое макета; пусто — шаблон не связан с файловым пакетом.
    /// </summary>
    /// <remarks>
    /// В совместном режиме путь к файлу клиенту не передаётся: содержимое
    /// макета рабочее место получает по идентификатору пакета и версии.
    /// </remarks>
    public string? PackageId { get; set; }

    /// <summary>
    /// Версия пакета шаблона, с которой связана строка; пусто — привязки нет.
    /// </summary>
    public long? PackageVersion { get; set; }

    /// <summary>
    /// SHA-256 содержимого версии пакета в виде шестнадцатеричной строки.
    /// </summary>
    public string? PackageHash { get; set; }

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
