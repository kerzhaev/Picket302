using System.Globalization;
using System.Text;
using Picket302.Domain.Entities;

namespace Picket302.Printing.Audit;

/// <summary>
/// Запись печатной сессии для журнала аудита (ТЗ §6.5; §8.6, функция 88).
/// </summary>
public sealed record PrintJobRecord
{
    /// <summary>Имя принтера.</summary>
    public string PrinterName { get; init; } = string.Empty;

    /// <summary>Ширина носителя, мм.</summary>
    public decimal PaperWidthMm { get; init; }

    /// <summary>Высота носителя, мм.</summary>
    public decimal PaperHeightMm { get; init; }

    /// <summary>Имя формата носителя в терминах драйвера.</summary>
    public string PaperSizeName { get; init; } = string.Empty;

    /// <summary>Лоток подачи.</summary>
    public string PaperSource { get; init; } = string.Empty;

    /// <summary>Режим двусторонней печати в терминах драйвера.</summary>
    public string Duplex { get; init; } = "Simplex";

    /// <summary>Имя файла бланка.</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Ориентация макета.</summary>
    public TemplateOrientation Orientation { get; init; } = TemplateOrientation.Landscape;

    /// <summary>Гарнитура шрифта бланка.</summary>
    public string FontName { get; init; } = string.Empty;

    /// <summary>Кегль шрифта бланка, пунктов.</summary>
    public decimal FontSizePt { get; init; }

    /// <summary>Гарнитура шрифта кодового штампа.</summary>
    public string IndexFontName { get; init; } = "ZIPcode";

    /// <summary>Толщина линии элементов цифр кодового штампа, мм.</summary>
    public decimal IndexLineWidthMm { get; init; }

    /// <summary>Калибровочное смещение по горизонтали, мм.</summary>
    public decimal OffsetXMm { get; init; }

    /// <summary>Калибровочное смещение по вертикали, мм.</summary>
    public decimal OffsetYMm { get; init; }

    /// <summary>Число объектов (отправлений) в задании.</summary>
    public int ObjectCount { get; init; }

    /// <summary>Число страниц в задании.</summary>
    public int PageCount { get; init; }

    /// <summary>Число копий.</summary>
    public int CopyCount { get; init; }

    /// <summary>Итог сессии печати.</summary>
    public string Status { get; init; } = PrintJobStatus.Success;

    /// <summary>Длительность сессии печати.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Ошибка записи аудита печати: печать без журнала запрещена (ТЗ §8.6,
/// функция 88: «Сбой записи аудита отменяет печатную транзакцию»).
/// </summary>
public sealed class PrintAuditException : InvalidOperationException
{
    /// <summary>
    /// Создаёт исключение аудита.
    /// </summary>
    /// <param name="message">Текст ошибки.</param>
    /// <param name="innerException">Причина сбоя.</param>
    public PrintAuditException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Журналирование параметров печати (ТЗ §6.5; §8.6, функция 88:
/// <c>Printing.Audit.PrintAuditWriter.WriteSessionRecord</c>).
/// </summary>
/// <remarks>
/// Каждая сессия печати — успешная и неуспешная — записывается в текстовый
/// канал аудита <c>print_audit.log</c> строго формата §6.5. Сбой записи аудита
/// отменяет печатную транзакцию: печать без журнала недопустима.
/// </remarks>
public sealed class PrintAuditWriter
{
    /// <summary>Имя файла канала аудита печати.</summary>
    public const string FileName = "print_audit.log";

    /// <summary>Разделитель строк записи.</summary>
    private const string РазделительСтрок = "\n";

    /// <summary>Строка-разделитель записи.</summary>
    private const string Разделитель = "-------------";

    /// <summary>
    /// Кодировка файла аудита: UTF-8 без маркера порядка байтов, чтобы файл
    /// оставался пригодным для построчного разбора сторонними средствами.
    /// </summary>
    private static readonly Encoding Кодировка = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string _путьФайла;

    /// <summary>
    /// Создаёт журналист аудита печати.
    /// </summary>
    /// <param name="logDirectory">Каталог хранения файла аудита.</param>
    public PrintAuditWriter(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        _путьФайла = Path.Combine(logDirectory, FileName);
    }

    /// <summary>
    /// Формирует текст записи аудита строго по формату §6.5.
    /// </summary>
    /// <param name="record">Параметры печатной сессии.</param>
    /// <returns>Текст записи.</returns>
    public static string FormatRecord(PrintJobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var строки = new[]
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Разделитель} Вывод на принтер: {record.PrinterName}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Разделитель} Параметры принтера: paper={record.PaperWidthMm:0}x{record.PaperHeightMm:0}mm, " +
                $"source={record.PaperSource}, duplex={record.Duplex}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Шаблон: {record.TemplateName}, форма {record.PaperWidthMm:0}x{record.PaperHeightMm:0} мм, " +
                $"ориентация {record.Orientation}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Имя шрифта: {record.FontName}, Размер шрифта: {record.FontSizePt:0.0}pt, " +
                $"Шрифт индекса: {record.IndexFontName} ({record.IndexLineWidthMm:0.00}mm)"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Смещение: X={record.OffsetXMm:0.00}мм Y={record.OffsetYMm:0.00}мм"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Объектов: {record.ObjectCount}, страниц: {record.PageCount}, копий: {record.CopyCount}"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Статус: {StatusText(record.Status)}, Затрачено времени: {record.Duration.TotalSeconds:0.0}с"),
            Разделитель,
        };

        return string.Join(РазделительСтрок, строки);
    }

    /// <summary>
    /// Возвращает русское наименование итога сессии печати.
    /// </summary>
    /// <param name="status">Итог сессии печати.</param>
    /// <returns>Текст статуса для журнала аудита.</returns>
    public static string StatusText(string status) => status switch
    {
        PrintJobStatus.Success => "УСПЕШНО",
        PrintJobStatus.Aborted => "ПРЕРВАНО",
        PrintJobStatus.Failed => "ОШИБКА",
        PrintJobStatus.BlockedSize => "ЗАБЛОКИРОВАНО",
        _ => status.ToUpperInvariant(),
    };

    /// <summary>
    /// Записывает запись печатной сессии в канал аудита.
    /// </summary>
    /// <param name="record">Параметры печатной сессии.</param>
    /// <exception cref="PrintAuditException">
    /// Запись аудита не выполнена: печать отменяется.
    /// </exception>
    public void WriteSessionRecord(PrintJobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var текст = FormatRecord(record);

        try
        {
            var каталог = Path.GetDirectoryName(_путьФайла);

            if (!string.IsNullOrEmpty(каталог))
            {
                Directory.CreateDirectory(каталог);
            }

            File.AppendAllText(_путьФайла, текст + РазделительСтрок, Кодировка);
        }
        catch (Exception исключение)
        {
            throw new PrintAuditException(
                "Не удалось записать запись аудита печати: печать отменена.",
                исключение);
        }
    }

    /// <summary>
    /// Проверяет готовность канала аудита к записи до передачи задания в спулер.
    /// </summary>
    /// <exception cref="PrintAuditException">
    /// Канал аудита недоступен: печать отменяется (ТЗ §2.2).
    /// </exception>
    /// <remarks>
    /// Проверка выполняется до передачи данных в подсистему печати, чтобы
    /// недоступность канала аудита отменяла печать, а не фиксировалась
    /// постфактум.
    /// </remarks>
    public void УбедитьсяВДоступностиКанала()
    {
        try
        {
            var каталог = Path.GetDirectoryName(_путьФайла);

            if (!string.IsNullOrEmpty(каталог))
            {
                Directory.CreateDirectory(каталог);
            }

            using var поток = new FileStream(
                _путьФайла,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);

            поток.Flush();
        }
        catch (Exception исключение)
        {
            throw new PrintAuditException(
                "Канал аудита печати недоступен: печать отменена.",
                исключение);
        }
    }
}
