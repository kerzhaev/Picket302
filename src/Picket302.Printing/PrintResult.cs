using Picket302.Domain.Entities;
using Picket302.Printing.Elements;

namespace Picket302.Printing;

/// <summary>
/// Фаза печатной сессии (ТЗ §7.2: «Идет подготовка к печати…», «Идет печать.»).
/// </summary>
public enum PrintProgressPhase
{
    /// <summary>Подготовка задания: препроцессинг, валидация, формирование страниц.</summary>
    Preparing = 0,

    /// <summary>Передача задания в спулер.</summary>
    Printing = 1,

    /// <summary>
    /// Ручной переворот стопки при двухпроходной печати без поддержки
    /// двусторонней печати (ТЗ §8.6, функция 86).
    /// </summary>
    DuplexFlip = 2,
}

/// <summary>
/// Состояние прогресса печатной сессии (ТЗ §7.2).
/// </summary>
/// <param name="Phase">Текущая фаза.</param>
/// <param name="Completed">Число выполненных объектов.</param>
/// <param name="Total">Общее число объектов.</param>
/// <param name="Message">Текст состояния для оператора.</param>
public sealed record PrintProgress(
    PrintProgressPhase Phase,
    int Completed,
    int Total,
    string Message)
{
    /// <summary>Текст фазы подготовки.</summary>
    public const string PreparingMessage = "Идет подготовка к печати...";

    /// <summary>Текст фазы печати.</summary>
    public const string PrintingMessage = "Идет печать.";

    /// <summary>
    /// Текст фазы переворота стопки при двухпроходной печати.
    /// </summary>
    public const string DuplexFlipMessage =
        "Переверните стопку и заправьте её обратно в лоток для печати оборотной стороны.";
}

/// <summary>
/// Итог печатной сессии (ТЗ §8.6, функция 79).
/// </summary>
public sealed record PrintResult
{
    /// <summary>Признак успешного завершения.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Итог сессии: <c>SUCCESS</c>, <c>ABORTED</c>, <c>FAILED</c>, <c>BLOCKED_SIZE</c>.</summary>
    public string Status { get; init; } = PrintJobStatus.Success;

    /// <summary>Текст ошибки, если сессия завершилась неудачей.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Число напечатанных экземпляров.</summary>
    public int Copies { get; init; }

    /// <summary>Предупреждения сессии печати.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Идентификатор записи реестра сессий печати.</summary>
    public long? PrintJobId { get; init; }

    /// <summary>
    /// Форматы носителя, поддерживаемые выбранным принтером (ТЗ §8.6,
    /// функция 85: перечень форматов принтера в блокирующем предупреждении).
    /// </summary>
    public IReadOnlyList<string> AvailablePaperSizes { get; init; } = Array.Empty<string>();

    /// <summary>Имя принтера, использованного в сессии печати.</summary>
    public string PrinterName { get; init; } = string.Empty;
}

/// <summary>
/// Спулер печати: передача подготовленного задания в системный диспетчер
/// печати (ТЗ §6.1, §8.6, функция 79).
/// </summary>
public interface IPrintSpooler
{
    /// <summary>
    /// Печатает задание.
    /// </summary>
    /// <param name="context">Контекст печатной сессии.</param>
    /// <param name="render">Отрисовка страницы в графическом контексте.</param>
    /// <param name="cancellationToken">Токен отмены задания.</param>
    Task PrintAsync(
        PrintJobContext context,
        Action<IGraphicsContext> render,
        CancellationToken cancellationToken);
}

/// <summary>
/// Источник данных для печати: отправления и значения полей бланка
/// (ТЗ §6.1: «[Данные Mail] + [Бланк .lbk]»).
/// </summary>
public interface IPrintDataSource
{
    /// <summary>
    /// Возвращает значения полей бланка для указанных отправлений.
    /// </summary>
    /// <param name="mailIds">Идентификаторы отправлений.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список отправлений с подставленными значениями полей.</returns>
    Task<IReadOnlyList<PrintableItem>> GetItemsAsync(
        IReadOnlyList<long> mailIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Параметры печатной операции (ТЗ §8.6, функция 79).
/// </summary>
public sealed record PrintRequest
{
    /// <summary>Идентификаторы отправлений к печати.</summary>
    public IReadOnlyList<long> MailIds { get; init; } = Array.Empty<long>();

    /// <summary>Идентификатор выбранного шаблона.</summary>
    public long? TemplateId { get; init; }

    /// <summary>Имя принтера.</summary>
    public string PrinterName { get; init; } = string.Empty;

    /// <summary>Режим печати: <c>selected</c>, <c>batch_preview</c>, <c>batch_direct</c>, <c>test</c>.</summary>
    public string PrintMode { get; init; } = PrintModes.Selected;

    /// <summary>Режим двусторонней печати.</summary>
    public Engine.DuplexMode Duplex { get; init; } = Engine.DuplexMode.Simplex;

    /// <summary>Фильтр чётных и нечётных страниц.</summary>
    public Engine.PageParity Parity { get; init; } = Engine.PageParity.All;

    /// <summary>Число копий.</summary>
    public int Copies { get; init; } = 1;

    /// <summary>Калибровочное смещение по горизонтали, мм.</summary>
    public decimal OffsetXMm { get; init; }

    /// <summary>Калибровочное смещение по вертикали, мм.</summary>
    public decimal OffsetYMm { get; init; }

    /// <summary>Признак режима «Зафиксировать размер A4».</summary>
    public bool FixA4 { get; init; }

    /// <summary>Признак режима «Размеченный типографский конверт».</summary>
    public bool Preprinted { get; init; }

    /// <summary>Признак печати с логотипом.</summary>
    public bool WithLogo { get; init; }

    /// <summary>Признак пробной печати: записи в <c>mails</c> не создаются.</summary>
    public bool IsTest { get; init; }

    /// <summary>Признак показа предпросмотра перед печатью.</summary>
    public bool ShowPreview { get; init; }
}
