namespace Picket302.Domain.Entities;

/// <summary>
/// Реестр сессий печати (ТЗ §3.1.6, таблица <c>print_jobs</c>).
/// </summary>
/// <remarks>
/// Запись журнала неизменяема: после создания ни одно поле не меняется.
/// Неизменность обеспечивается тремя средствами — закрытыми свойствами
/// сущности, отсутствием операций изменения и удаления в репозитории и
/// запрещающими триггерами <c>UPDATE</c>/<c>DELETE</c> в схеме БД
/// (ТЗ §3.1.6: «Неизменяемый реестр сессий печати»).
/// </remarks>
public sealed class PrintJob
{
    /// <summary>
    /// Создаёт запись реестра сессий печати.
    /// </summary>
    /// <param name="id">Идентификатор записи.</param>
    /// <param name="createdAt">Момент создания записи (UTC).</param>
    /// <param name="templateId">Шаблон, по которому печаталась партия.</param>
    /// <param name="printerName">Имя принтера.</param>
    /// <param name="paperSizeName">Имя формата носителя в терминах драйвера.</param>
    /// <param name="paperWidthMm">Ширина носителя, мм.</param>
    /// <param name="paperHeightMm">Высота носителя, мм.</param>
    /// <param name="fontInfo">Описание используемых шрифтов.</param>
    /// <param name="copies">Число напечатанных экземпляров.</param>
    /// <param name="printMode">Режим печати.</param>
    /// <param name="status">Итог сессии печати.</param>
    /// <param name="errorText">Текст ошибки, если сессия завершилась неудачей.</param>
    /// <param name="devmodeSnapshot">Бинарный снимок параметров печати (DEVMODE).</param>
    public PrintJob(
        long id,
        DateTime createdAt,
        long? templateId,
        string printerName,
        string paperSizeName,
        decimal paperWidthMm,
        decimal paperHeightMm,
        string? fontInfo,
        int copies,
        string printMode,
        string status,
        string? errorText,
        byte[]? devmodeSnapshot)
    {
        Id = id;
        CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
        TemplateId = templateId;
        PrinterName = printerName;
        PaperSizeName = paperSizeName;
        PaperWidthMm = paperWidthMm;
        PaperHeightMm = paperHeightMm;
        FontInfo = fontInfo;
        Copies = copies;
        PrintMode = printMode;
        Status = status;
        ErrorText = errorText;
        DevmodeSnapshot = devmodeSnapshot;
    }

    /// <summary>Идентификатор сессии печати (первичный ключ).</summary>
    public long Id { get; }

    /// <summary>Момент создания записи (UTC).</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Шаблон, по которому печаталась партия.</summary>
    public long? TemplateId { get; }

    /// <summary>Имя принтера.</summary>
    public string PrinterName { get; }

    /// <summary>Имя формата носителя в терминах драйвера.</summary>
    public string PaperSizeName { get; }

    /// <summary>Ширина носителя, мм.</summary>
    public decimal PaperWidthMm { get; }

    /// <summary>Высота носителя, мм.</summary>
    public decimal PaperHeightMm { get; }

    /// <summary>Описание используемых шрифтов.</summary>
    public string? FontInfo { get; }

    /// <summary>Число напечатанных экземпляров.</summary>
    public int Copies { get; }

    /// <summary>Режим печати: <c>selected</c>, <c>batch_preview</c>, <c>batch_direct</c>, <c>test</c>.</summary>
    public string PrintMode { get; }

    /// <summary>Итог сессии: <c>SUCCESS</c>, <c>ABORTED</c>, <c>FAILED</c>, <c>BLOCKED_SIZE</c>.</summary>
    public string Status { get; }

    /// <summary>Текст ошибки, если сессия завершилась неудачей.</summary>
    public string? ErrorText { get; }

    /// <summary>Бинарный снимок параметров печати (DEVMODE).</summary>
    public byte[]? DevmodeSnapshot { get; }
}
