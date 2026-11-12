using System.Drawing;
using Picket302.Domain.Entities;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;

namespace Picket302.Printing.Rendering;

/// <summary>
/// Единый генератор изображения бланка для печати и предпросмотра (ТЗ §6.1;
/// §8.6, функция 80: «тем же конвейером рендеринга, что и физическая печать
/// (единый генератор метафайла)»).
/// </summary>
public sealed class EnvelopeRenderer
{
    private readonly ITextMeasurer _измеритель;

    /// <summary>
    /// Создаёт генератор изображения бланка.
    /// </summary>
    /// <param name="measurer">Измеритель текста для автоподбора кегля.</param>
    /// <summary>Резолвер клипарта: id → PNG-байты (S26, функция 158).</summary>
    private readonly Func<string, byte[]?>? _резолверКлипарта;

    public EnvelopeRenderer(ITextMeasurer measurer, Func<string, byte[]?>? клипартРезолвер = null)
    {
        ArgumentNullException.ThrowIfNull(measurer);

        _измеритель = measurer;
        _резолверКлипарта = клипартРезолвер;
    }

    /// <summary>
    /// Отрисовывает содержимое бланка для одного отправления.
    /// </summary>
    /// <param name="context">Графический контекст.</param>
    /// <param name="job">Контекст печатной сессии.</param>
    /// <param name="item">Отправление.</param>
    /// <param name="postIndex">Отрисовщик кодового штампа.</param>
    /// <param name="qrCode">Отрисовщик QR-кода.</param>
    /// <param name="logo">Отрисовщик логотипа.</param>
    /// <returns>Предупреждения сессии, возникшие при подборе кегля и выводе элементов.</returns>
    public IReadOnlyList<string> Render(
        IGraphicsContext context,
        PrintJobContext job,
        PrintableItem item,
        PostIndexRenderer? postIndex = null,
        QrCodeRenderer? qrCode = null,
        LogoRenderer? logo = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(item);

        var предупреждения = new List<string>();
        var подбор = new TextFitter(_измеритель);
        var логотипБайты = item.LogoBytes ?? job.LogoBytes;
        var границыЛоготипа = НайтиГраницыЛоготипа(job, логотипБайты);

        foreach (var поле in job.Fields)
        {
            // Невидимые поля не выводятся на печать так же, как непечатаемые
            // (F-A7: прежде проверялась только IsPrintable).
            if (!поле.IsPrintable || !поле.IsVisible)
            {
                continue;
            }

            // Многостраничный бланк: выводятся поля текущей страницы.
            if (поле.Page != job.CurrentPage)
            {
                continue;
            }

            var значение = item.Values.TryGetValue(поле.Name, out var найденное)
                ? найденное
                : string.Empty;

            switch (поле.Kind)
            {
                case BlankFieldKind.PostIndex:
                    if (postIndex is null)
                    {
                        // Почтовый индекс нельзя выводить обычным шрифтом:
                        // адресат не сможет прочитать его машинно (ТЗ §7,
                        // критерий 2 «Заказ»).
                        предупреждения.Add(
                            "Почтовый индекс не выведен: недоступен отрисовщик кодового штампа");

                        break;
                    }

                    postIndex.RenderMachineReadableIndex(context, значение, new PostIndexSpec
                    {
                        LineWidthMm = job.IndexLineWidthMm,
                        FontName = job.IndexFontName,
                        DigitCount = job.IndexDigitCount,
                        InternationalIndex = job.InternationalIndex,
                        AvailableWidthMm = поле.LengthMm,
                        OriginXMm = (float)(поле.XMm + job.OffsetXMm),
                        OriginYMm = (float)(поле.YMm + job.OffsetYMm),
                    });

                    break;

                case BlankFieldKind.Qr:
                    if (qrCode is null)
                    {
                        предупреждения.Add(
                            "QR-код не выведен: недоступен отрисовщик QR-кода");

                        break;
                    }

                    qrCode.RenderQr(
                        context,
                        значение,
                        new RectangleF(
                            (float)(поле.XMm + job.OffsetXMm),
                            (float)(поле.YMm + job.OffsetYMm),
                            (float)поле.LengthMm,
                            (float)поле.HeightMm),
                        границыЛоготипа,
                        new QrRenderOptions { Dpi = job.PrintDpi });

                    break;

                case BlankFieldKind.Picture:
                    // S26 (функция 158): picture-поле с ClipartId рисует
                    // клипарт из библиотеки; без ClipartId — прежний логотип.
                    if (поле.ClipartId is not null)
                    {
                        var клипартБайты = _резолверКлипарта?.Invoke(поле.ClipartId);
                        if (клипартБайты is { Length: > 0 })
                        {
                            using var клипартИзображение = Image.FromStream(
                                new MemoryStream(клипартБайты));

                            context.DrawImageMm(
                                клипартИзображение,
                                (float)(поле.XMm + job.OffsetXMm),
                                (float)(поле.YMm + job.OffsetYMm),
                                (float)поле.LengthMm,
                                (float)поле.HeightMm);
                        }
                        else
                        {
                            предупреждения.Add(
                                $"Клипарт «{поле.ClipartId}» не найден в библиотеке");
                        }

                        break;
                    }

                    if (!job.WithLogo)
                    {
                        break;
                    }

                    if (logo is null || логотипБайты is null)
                    {
                        предупреждения.Add(
                            "Логотип не выведен: файл логотипа недоступен");

                        break;
                    }

                    var предупреждениеЛоготипа = logo.DrawLogo(
                        context,
                        логотипБайты,
                        new RectangleF(
                            (float)(поле.XMm + job.OffsetXMm),
                            (float)(поле.YMm + job.OffsetYMm),
                            (float)поле.LengthMm,
                            (float)поле.HeightMm));

                    if (предупреждениеЛоготипа is not null)
                    {
                        предупреждения.Add(предупреждениеЛоготипа);
                    }

                    break;

                default:
                    var результат = подбор.FitField(поле, значение);

                    if (результат.Warning is not null)
                    {
                        предупреждения.Add(результат.Warning);
                    }

                    if (результат.Overflow && job.IsPreview)
                    {
                        // Маркер переполнения поля: выводится только в
                        // предпросмотре (ТЗ §8.6, функция 91).
                        context.FillRectangleMm(
                            (float)(поле.XMm + job.OffsetXMm),
                            (float)(поле.YMm + job.OffsetYMm),
                            (float)поле.LengthMm,
                            (float)поле.HeightMm);
                    }

                    var верх = (float)(поле.YMm + job.OffsetYMm);
                    var лево = (float)(поле.XMm + job.OffsetXMm);
                    var высотаСтроки = (float)Math.Max(
                        1.0,
                        _измеритель.MeasureLineHeight(поле.FontName, результат.FontSizePt));

                    var номерСтроки = 0;

                    foreach (var строка in результат.Lines)
                    {
                        context.DrawStringMm(
                            строка,
                            поле.FontName,
                            (float)поле.LengthMm,
                            высотаСтроки,
                            лево,
                            верх + (номерСтроки * высотаСтроки));

                        номерСтроки++;
                    }

                    break;
            }
        }

        return предупреждения;
    }

    /// <summary>
    /// Возвращает строки данных QR-кодов, подлежащих выводу на бланке.
    /// </summary>
    /// <param name="job">Контекст печатной сессии.</param>
    /// <returns>Перечень строк данных кодов.</returns>
    /// <remarks>
    /// Список используется для прогрева кэша <c>qr_cache</c> перед печатью:
    /// ранее сформированные коды берутся из кэша по SHA-256 ключу строки.
    /// </remarks>
    public IReadOnlyList<string> ПолучитьПолезныеНагрузкиQr(PrintJobContext job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var нагрузки = new List<string>();

        foreach (var поле in job.Fields)
        {
            if (поле.Kind != BlankFieldKind.Qr || !поле.IsPrintable)
            {
                continue;
            }

            foreach (var элемент in job.Items)
            {
                if (элемент.Values.TryGetValue(поле.Name, out var значение) &&
                    !string.IsNullOrWhiteSpace(значение))
                {
                    нагрузки.Add(значение);
                }
            }
        }

        return нагрузки;
    }

    /// <summary>
    /// Возвращает границы поля логотипа: код QR не должен пересекаться с логотипом
    /// (ТЗ §6.4; §8.6, функция 48).
    /// </summary>
    /// <param name="job">Контекст печатной сессии.</param>
    /// <param name="логотипБайты">Логотип, выводимый на конверте.</param>
    /// <returns>Границы логотипа либо <see langword="null"/>, если логотип не выводится.</returns>
    private static RectangleF? НайтиГраницыЛоготипа(PrintJobContext job, byte[]? логотипБайты)
    {
        if (!job.WithLogo || логотипБайты is null)
        {
            return null;
        }

        foreach (var поле in job.Fields)
        {
            if (поле.Kind != BlankFieldKind.Picture || !поле.IsPrintable)
            {
                continue;
            }

            return new RectangleF(
                (float)(поле.XMm + job.OffsetXMm),
                (float)(поле.YMm + job.OffsetYMm),
                (float)поле.LengthMm,
                (float)поле.HeightMm);
        }

        return null;
    }
}
