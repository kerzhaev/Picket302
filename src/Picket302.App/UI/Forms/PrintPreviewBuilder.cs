using System.IO;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Printing;
using Picket302.Printing.Devices;
using Picket302.Printing.Engine;
using Picket302.Printing.Elements;
using Picket302.Printing.Metrics;
using Picket302.Printing.Preview;

namespace Picket302.App.UI.Forms;

/// <summary>
/// Формирует контекст печатной сессии для интерактивного предпросмотра
/// (ТЗ §8.6, функция 80: <c>Printing.Preview.PrintPreviewEngine</c>).
/// </summary>
/// <remarks>
/// Предпросмотр строится тем же конвейером, что и физическая печать: бланк,
/// поля, данные отправлений и метрики принтера. Контекст предпросмотра
/// повторяет обработку режимов «Зафиксировать размер A4» и «Размеченный
/// типографский конверт», поэтому изображение предпросмотра совпадает с
/// печатью.
/// </remarks>
public sealed class PrintPreviewBuilder
{
    private readonly IPrintTemplateRepository _шаблоны;
    private readonly IBlankFieldRepository _поля;
    private readonly IPrintDataSource _данные;
    private readonly PrinterManager _принтеры;
    private readonly PrintPreviewEngine _движок;

    /// <summary>
    /// Создаёт сборщик предпросмотра.
    /// </summary>
    /// <param name="шаблоны">Хранилище шаблонов.</param>
    /// <param name="поля">Хранилище полей бланка.</param>
    /// <param name="данные">Источник данных отправлений.</param>
    /// <param name="принтеры">Менеджер принтеров.</param>
    /// <param name="движок">Движок предпросмотра печати.</param>
    public PrintPreviewBuilder(
        IPrintTemplateRepository шаблоны,
        IBlankFieldRepository поля,
        IPrintDataSource данные,
        PrinterManager принтеры,
        PrintPreviewEngine движок)
    {
        ArgumentNullException.ThrowIfNull(шаблоны);
        ArgumentNullException.ThrowIfNull(поля);
        ArgumentNullException.ThrowIfNull(данные);
        ArgumentNullException.ThrowIfNull(принтеры);
        ArgumentNullException.ThrowIfNull(движок);

        _шаблоны = шаблоны;
        _поля = поля;
        _данные = данные;
        _принтеры = принтеры;
        _движок = движок;
    }

    /// <summary>
    /// Формирует документ предпросмотра для печатной операции.
    /// </summary>
    /// <param name="запрос">Параметры печати.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Документ предпросмотра.</returns>
    public async Task<PreviewDocument> ПостроитьAsync(
        PrintRequest запрос,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(запрос);

        if (запрос.TemplateId is null)
        {
            throw new InvalidOperationException("Шаблон не выбран.");
        }

        var шаблон = await _шаблоны
            .GetByIdAsync(запрос.TemplateId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (шаблон is null)
        {
            throw new InvalidOperationException("Шаблон не выбран.");
        }

        var поля = await _поля
            .GetByTemplateAsync(шаблон.Id, cancellationToken)
            .ConfigureAwait(false);

        var элементы = await _данные
            .GetItemsAsync(запрос.MailIds, cancellationToken)
            .ConfigureAwait(false);

        var имяШаблона = string.IsNullOrWhiteSpace(шаблон.FilePath)
            ? шаблон.Name
            : Path.GetFileName(шаблон.FilePath);

        var метрики = new BlankMetrics
        {
            TemplateName = имяШаблона,
            WidthMm = шаблон.WidthMm,
            HeightMm = шаблон.HeightMm,
            Orientation = шаблон.Orientation,
            PaperSizeName = шаблон.Format,
            PageCount = поля.Count > 0 ? поля.Max(поле => поле.Page) : 1,
        };

        var контекст = new PrintJobContext
        {
            TemplateId = шаблон.Id,
            TemplateName = имяШаблона,
            Blank = метрики,
            Fields = поля,
            Items = элементы,
            Printer = ПолучитьСведенияОПринтере(запрос, шаблон),
            Orientation = шаблон.Orientation,
            PaperSizeName = шаблон.Format,
            PaperWidthMm = шаблон.WidthMm,
            PaperHeightMm = шаблон.HeightMm,
            DmPaperSize = PrinterDeviceInfo.PaperSizeCodeByFormat(шаблон.Format),
            FontName = "Tahoma",
            FontSizePt = 10m,
            IndexFontName = PostIndexSpec.DefaultFontName,
            IndexLineWidthMm = PostIndexSpec.DefaultLineWidthMm,
            OffsetXMm = запрос.OffsetXMm,
            OffsetYMm = запрос.OffsetYMm,
            Duplex = запрос.Duplex,
            Parity = запрос.Parity,
            PrintMode = запрос.PrintMode,
            Copies = запрос.Copies,
            FixA4 = запрос.FixA4,
            Preprinted = запрос.Preprinted,
            WithLogo = запрос.WithLogo,
            TotalPages = Math.Max(метрики.PageCount, 1),
        };

        if (запрос.FixA4)
        {
            контекст = new FixA4Mode().ApplyFixA4(контекст);
        }

        if (запрос.Preprinted)
        {
            контекст = new PreprintedMode().StripBackground(контекст);
        }

        return _движок.RenderPreview(контекст);
    }

    /// <summary>
    /// Получает реальные сведения о принтере из драйвера (DEVMODE и
    /// <c>DeviceCapabilitiesW</c>) — ТЗ §6.2.
    /// </summary>
    /// <param name="запрос">Параметры печати.</param>
    /// <param name="шаблон">Макет бланка.</param>
    /// <returns>Сведения о принтере.</returns>
    private PrinterDeviceInfo ПолучитьСведенияОПринтере(PrintRequest запрос, PrintTemplate шаблон)
    {
        var кодФормата = PrinterDeviceInfo.PaperSizeCodeByFormat(шаблон.Format);

        if (!string.IsNullOrWhiteSpace(запрос.PrinterName))
        {
            try
            {
                return _принтеры.GetDeviceInfo(запрос.PrinterName, кодФормата);
            }
#pragma warning disable CA1031 // Сбой опроса драйвера не должен прерывать предпросмотр.
            catch (Exception)
#pragma warning restore CA1031
            {
                // Драйвер недоступен: размеры печатной области принимаются равными
                // размеру бланка.
            }
        }

        return new PrinterDeviceInfo
        {
            PrinterName = запрос.PrinterName,
            PaperSizeName = шаблон.Format,
            PaperWidthMm = шаблон.WidthMm,
            PaperHeightMm = шаблон.HeightMm,
            PrintableWidthMm = шаблон.WidthMm,
            PrintableHeightMm = шаблон.HeightMm,
            DmPaperSize = кодФормата,
        };
    }
}
