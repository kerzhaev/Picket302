namespace Picket302.Infrastructure.Logging;

/// <summary>
/// Независимые каналы журналирования (ТЗ §10.3; §8.11, функция 111:
/// <c>public ILogger GetChannel(LogChannel channel) // App | Print | Audit | Email</c>).
/// </summary>
public enum LogChannel
{
    /// <summary>Ошибки приложения: исключения с трассировкой, уровень Warning+.</summary>
    App = 0,

    /// <summary>Печатные сессии в формате §6.5, включая отклонённые.</summary>
    Print = 1,

    /// <summary>Изменения справочников и входы в программу.</summary>
    Audit = 2,

    /// <summary>Отправки писем с кодами ответа SMTP.</summary>
    Email = 3,
}

/// <summary>
/// Сведения о печатной сессии для журнала печати (ТЗ §6.5: «Журналирование
/// параметров печати»).
/// </summary>
/// <remarks>
/// Формат записи журнала печати задан ТЗ §6.5 дословно и воспроизводится
/// методом <see cref="Форматировать"/>.
/// </remarks>
/// <param name="PrinterName">Имя принтера.</param>
/// <param name="Paper">Размер бумаги, мм.</param>
/// <param name="Source">Лоток подачи бумаги.</param>
/// <param name="Duplex">Режим двусторонней печати.</param>
/// <param name="TemplateName">Имя шаблона бланка.</param>
/// <param name="TemplateSize">Формат бланка, мм.</param>
/// <param name="Orientation">Ориентация бланка.</param>
/// <param name="FontName">Имя шрифта бланка.</param>
/// <param name="FontSize">Размер шрифта, пунты.</param>
/// <param name="IndexFontName">Имя шрифта индекса.</param>
/// <param name="IndexFontHeight">Высота шрифта индекса, мм.</param>
/// <param name="OffsetX">Смещение по горизонтали, мм.</param>
/// <param name="OffsetY">Смещение по вертикали, мм.</param>
/// <param name="ObjectCount">Число объектов.</param>
/// <param name="PageCount">Число страниц.</param>
/// <param name="CopyCount">Число копий.</param>
/// <param name="Status">Статус печати.</param>
/// <param name="ElapsedSeconds">Затраченное время, с.</param>
public sealed record PrintSessionLogRecord(
    string PrinterName,
    string Paper,
    string Source,
    string Duplex,
    string TemplateName,
    string TemplateSize,
    string Orientation,
    string FontName,
    double FontSize,
    string IndexFontName,
    double IndexFontHeight,
    double OffsetX,
    double OffsetY,
    int ObjectCount,
    int PageCount,
    int CopyCount,
    string Status,
    double ElapsedSeconds)
{
    /// <summary>Значение статуса успешной печати (ТЗ §6.5).</summary>
    public const string УспешныйСтатус = "УСПЕШНО";

    /// <summary>
    /// Формирует запись журнала печати в формате ТЗ §6.5.
    /// </summary>
    /// <returns>Многострочный текст записи.</returns>
    public string Форматировать() => string.Join(
        Environment.NewLine,
        "------------- Вывод на принтер: " + PrinterName,
        $"------------- Параметры принтера: paper={Paper}, source={Source}, duplex={Duplex}",
        $"Шаблон: {TemplateName}, форма {TemplateSize} мм, ориентация {Orientation}",
        $"Имя шрифта: {FontName}, Размер шрифта: {FontSize.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}pt, Шрифт индекса: {IndexFontName} ({IndexFontHeight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}mm)",
        $"Смещение: X={OffsetX.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}мм Y={OffsetY.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}мм",
        $"Объектов: {ObjectCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, страниц: {PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, копий: {CopyCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        $"Статус: {Status}, Затрачено времени: {ElapsedSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}с",
        "-------------");
}
