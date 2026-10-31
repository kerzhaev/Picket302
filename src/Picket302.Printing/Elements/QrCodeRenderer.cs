using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Printing.Codes;

namespace Picket302.Printing.Elements;

/// <summary>
/// Параметры вывода QR-кода (ТЗ §6.4).
/// </summary>
public sealed record QrRenderOptions
{
    /// <summary>Разрешение печати, DPI (300 или 600).</summary>
    public int Dpi { get; init; } = 300;

    /// <summary>Уровень коррекции ошибок.</summary>
    public QrErrorCorrectionLevel ErrorCorrection { get; init; } = QrErrorCorrectionLevel.Medium;

    /// <summary>Ширина тихой зоны вокруг кода, модулей.</summary>
    public int QuietZoneModules { get; init; } = 4;
}

/// <summary>
/// Ошибка вывода QR-кода (ТЗ §8.4, функция 48: превышение лимита символов либо
/// пересечение с логотипом).
/// </summary>
public sealed class QrRenderException : InvalidOperationException
{
    /// <summary>
    /// Создаёт исключение вывода QR-кода.
    /// </summary>
    /// <param name="message">Текст ошибки.</param>
    public QrRenderException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Вывод динамического QR-кода на конверт (ТЗ §6.4; §8.4, функция 48:
/// <c>Printing.Elements.QrCodeRenderer.RenderQr</c>).
/// </summary>
/// <remarks>
/// <para>Код формируется по ГОСТ Р 56042-2014 и выводится монохромным растром
/// 1 бит на пиксель без субпиксельного сглаживания. Модуль кода выравнивается
/// по целочисленной сетке пикселей целевого разрешения печати (300/600 DPI),
/// чтобы исключить «замыливание» штрихов.</para>
/// <para>Сформированный растр кэшируется в таблице <c>qr_cache</c> по SHA-256
/// ключу строки данных.</para>
/// </remarks>
public sealed class QrCodeRenderer
{
    private readonly IQrCacheRepository? _репозиторий;
    private readonly ConcurrentDictionary<string, byte[]> _кэшПамяти = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Payload, byte[] Blob)> _неСохранённые =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Создаёт отрисовщик QR-кодов.
    /// </summary>
    /// <param name="repository">Хранилище кэша QR-кодов; при отсутствии кэш ведётся только в памяти.</param>
    public QrCodeRenderer(IQrCacheRepository? repository = null)
    {
        _репозиторий = repository;
    }

    /// <summary>
    /// Загружает в оперативный кэш ранее сформированные коды из таблицы
    /// <c>qr_cache</c> по SHA-256 ключу строки данных.
    /// </summary>
    /// <param name="payloads">Строки данных, выводимые на конвертах.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число записей, найденных в хранилище кэша.</returns>
    public async Task<int> ПрогретьКэшAsync(
        IEnumerable<string> payloads,
        CancellationToken cancellationToken = default)
    {
        if (_репозиторий is null)
        {
            return 0;
        }

        var найдено = 0;

        foreach (var payload in payloads.Distinct(StringComparer.Ordinal))
        {
            var хэш = ВычислитьХэш(payload);

            if (_кэшПамяти.ContainsKey(хэш))
            {
                continue;
            }

            var запись = await _репозиторий
                .GetByPayloadHashAsync(хэш, cancellationToken)
                .ConfigureAwait(false);

            if (запись is not null && запись.ImageBlob.Length > 0)
            {
                _кэшПамяти[хэш] = запись.ImageBlob;
                найдено++;
            }
        }

        return найдено;
    }

    /// <summary>
    /// Сохраняет сформированные в текущей сессии коды в таблицу <c>qr_cache</c>.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число сохранённых записей.</returns>
    public async Task<int> СохранитьКэшAsync(CancellationToken cancellationToken = default)
    {
        if (_репозиторий is null)
        {
            return 0;
        }

        var сохранено = 0;

        foreach (var пара in _неСохранённые.ToArray())
        {
            var хэш = пара.Key;

            var запись = await _репозиторий
                .GetByPayloadHashAsync(хэш, cancellationToken)
                .ConfigureAwait(false);

            if (запись is not null)
            {
                _неСохранённые.TryRemove(хэш, out _);
                continue;
            }

            var новая = QrCache.Create(пара.Value.Payload, пара.Value.Blob, DateTime.UtcNow);

            await _репозиторий.AddAsync(новая, cancellationToken).ConfigureAwait(false);

            _неСохранённые.TryRemove(хэш, out _);
            сохранено++;
        }

        return сохранено;
    }

    /// <summary>
    /// Вычисляет ключ кэша для строки данных.
    /// </summary>
    /// <param name="payload">Строка данных.</param>
    /// <returns>Hex-представление SHA-256 хэша.</returns>
    public static string ВычислитьХэш(string payload) => QrCache.ComputePayloadHash(payload);

    /// <summary>
    /// Отрисовывает QR-код, используя оперативный кэш растра; при промахе
    /// код формируется заново и помещается в кэш для последующего сохранения
    /// в таблице <c>qr_cache</c>.
    /// </summary>
    /// <param name="context">Графический контекст.</param>
    /// <param name="payload">Кодируемая строка.</param>
    /// <param name="bounds">Границы размещения кода.</param>
    /// <param name="logoBounds">Границы логотипа: код не должен пересекаться с логотипом.</param>
    /// <param name="options">Параметры вывода.</param>
    public void RenderQr(
        IGraphicsContext context,
        string payload,
        RectangleF bounds,
        RectangleF? logoBounds = null,
        QrRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var параметры = options ?? new QrRenderOptions();
        var хэш = ВычислитьХэш(payload);

        // Пересечение с логотипом проверяется до вывода: код не должен
        // накладываться на логотип (ТЗ §8.6, функция 48).
        ПроверитьПересечениеСЛоготипом(bounds, logoBounds);

        if (_кэшПамяти.TryGetValue(хэш, out var блоб))
        {
            using var изКэша = QrBitmapCodec.Decode(блоб);

            Вывести(context, изКэша, bounds, параметры);

            return;
        }

        var код = QrEncoder.Encode(payload, параметры.ErrorCorrection);
        using var растр = ПостроитьРастр(код, bounds, параметры);

        Вывести(context, растр, bounds, параметры);

        var данные = QrBitmapCodec.Encode(растр, параметры.Dpi);

        _кэшПамяти[хэш] = данные;
        _неСохранённые[хэш] = (payload, данные);
    }

    private static void Вывести(
        IGraphicsContext context,
        Bitmap растр,
        RectangleF bounds,
        QrRenderOptions параметры) =>
        context.DrawImageMm(
            растр,
            bounds.X,
            bounds.Y,
            растр.Width / (float)параметры.Dpi * 25.4f,
            растр.Height / (float)параметры.Dpi * 25.4f);

    /// <summary>
    /// Отрисовывает QR-код с обращением к хранилищу кэша: готовый растр берётся
    /// из кэша по SHA-256 ключу строки, при отсутствии — формируется и сохраняется.
    /// </summary>
    /// <param name="context">Графический контекст.</param>
    /// <param name="payload">Кодируемая строка.</param>
    /// <param name="bounds">Границы размещения кода.</param>
    /// <param name="logoBounds">Границы логотипа.</param>
    /// <param name="options">Параметры вывода.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Признак использования записи кэша.</returns>
    public async Task<bool> RenderQrAsync(
        IGraphicsContext context,
        string payload,
        RectangleF bounds,
        RectangleF? logoBounds = null,
        QrRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var параметры = options ?? new QrRenderOptions();
        var хэш = ВычислитьХэш(payload);

        if (_репозиторий is not null)
        {
            var запись = await _репозиторий
                .GetByPayloadHashAsync(хэш, cancellationToken)
                .ConfigureAwait(false);

            if (запись is not null && запись.ImageBlob.Length > 0)
            {
                using var растр = QrBitmapCodec.Decode(запись.ImageBlob);
                ПроверитьПересечениеСЛоготипом(bounds, logoBounds);

                context.DrawImageMm(
                    растр,
                    bounds.X,
                    bounds.Y,
                    растр.Width / (float)параметры.Dpi * 25.4f,
                    растр.Height / (float)параметры.Dpi * 25.4f);

                return true;
            }
        }

        var код = QrEncoder.Encode(payload, параметры.ErrorCorrection);
        using var новыйРастр = ПостроитьРастр(код, bounds, параметры);

        ПроверитьПересечениеСЛоготипом(bounds, logoBounds);

        context.DrawImageMm(
            новыйРастр,
            bounds.X,
            bounds.Y,
            новыйРастр.Width / (float)параметры.Dpi * 25.4f,
            новыйРастр.Height / (float)параметры.Dpi * 25.4f);

        if (_репозиторий is not null)
        {
            var блоб = QrBitmapCodec.Encode(новыйРастр, параметры.Dpi);
            var запись = QrCache.Create(payload, блоб, DateTime.UtcNow);

            await _репозиторий.AddAsync(запись, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Строит монохромный растр QR-кода по целочисленной сетке модулей.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <param name="bounds">Границы размещения кода, мм.</param>
    /// <param name="options">Параметры вывода.</param>
    /// <returns>Растр 1 бит на пиксель.</returns>
    public static Bitmap ПостроитьРастр(QrCode code, RectangleF bounds, QrRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(options);

        var модулей = code.Size + (2 * options.QuietZoneModules);
        var сторонаМм = Math.Min(bounds.Width, bounds.Height);

        if (сторонаМм <= 0)
        {
            throw new QrRenderException("Границы размещения QR-кода не заданы.");
        }

        var целевыхПикселей = (int)Math.Round((сторонаМм / 25.4) * options.Dpi, MidpointRounding.AwayFromZero);

        if (целевыхПикселей < модулей)
        {
            throw new QrRenderException(
                $"QR-код не помещается в заданные границы: требуется не менее {модулей} пикселей " +
                $"по стороне при {options.Dpi} DPI.");
        }

        var пикселейНаМодуль = целевыхПикселей / модулей;
        var сторонаПикселей = пикселейНаМодуль * модулей;

        var растр = QrBitmapCodec.CreateMonochromeBitmap(сторонаПикселей, сторонаПикселей, options.Dpi);

        // Растр 1 бит на пиксель заполняется напрямую: GDI+ не допускает
        // создание графического контекста для индексированных форматов.
        var область = new Rectangle(0, 0, сторонаПикселей, сторонаПикселей);
        var данные = растр.LockBits(область, ImageLockMode.WriteOnly, PixelFormat.Format1bppIndexed);

        try
        {
            var буфер = new byte[данные.Stride * сторонаПикселей];
            Array.Clear(буфер);

            for (var y = 0; y < code.Size; y++)
            {
                for (var x = 0; x < code.Size; x++)
                {
                    if (!code.GetModule(x, y))
                    {
                        continue;
                    }

                    var левый = (x + options.QuietZoneModules) * пикселейНаМодуль;
                    var верхний = (y + options.QuietZoneModules) * пикселейНаМодуль;

                    for (var py = верхний; py < верхний + пикселейНаМодуль; py++)
                    {
                        for (var px = левый; px < левый + пикселейНаМодуль; px++)
                        {
                            буфер[(py * данные.Stride) + (px / 8)] |=
                                (byte)(QrBitmapCodec.ИндексЧёрного << (7 - (px % 8)));
                        }
                    }
                }
            }

            Marshal.Copy(буфер, 0, данные.Scan0, буфер.Length);
        }
        finally
        {
            растр.UnlockBits(данные);
        }

        return растр;
    }

    private static void ПроверитьПересечениеСЛоготипом(RectangleF bounds, RectangleF? logoBounds)
    {
        if (logoBounds is not { } логотип)
        {
            return;
        }

        if (bounds.IntersectsWith(логотип))
        {
            throw new QrRenderException(
                "Зона QR-кода пересекается с логотипом: сдвиньте поле кода или логотипа.");
        }
    }
}
