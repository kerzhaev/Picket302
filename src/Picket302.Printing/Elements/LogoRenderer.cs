using System.Drawing;
using System.Drawing.Imaging;

namespace Picket302.Printing.Elements;

/// <summary>
/// Сведения о размере логотипа (ТЗ §8.4, функция 47; справка программы:
/// «Максимальный размер наибольшей стороны загружаемой картинки-логотипа»).
/// </summary>
/// <param name="Width">Ширина изображения, пикселей.</param>
/// <param name="Height">Высота изображения, пикселей.</param>
/// <param name="ExceedsLimit">Признак превышения предельного размера.</param>
/// <param name="Warning">Текст предложения уменьшить изображение.</param>
public sealed record LogoSizeInfo(int Width, int Height, bool ExceedsLimit, string? Warning);

/// <summary>
/// Совместное владение изображением логотипа и его источником (F-A10).
/// </summary>
/// <remarks>
/// GDI+ требует, чтобы поток, из которого создано изображение, оставался
/// открытым всё время жизни изображения: прежде поток закрывался при выходе
/// из <c>LoadImage</c>, и последующий вывод растра падал с ошибкой GDI+.
/// Держатель освобождает изображение и поток вместе.
/// </remarks>
public sealed class LogoImageHolder : IDisposable
{
    private readonly Stream _поток;

    /// <summary>
    /// Создаёт держатель изображения и его источника.
    /// </summary>
    /// <param name="изображение">Загруженное изображение.</param>
    /// <param name="поток">Поток-источник изображения.</param>
    public LogoImageHolder(Image изображение, Stream поток)
    {
        ArgumentNullException.ThrowIfNull(изображение);
        ArgumentNullException.ThrowIfNull(поток);

        Изображение = изображение;
        _поток = поток;
    }

    /// <summary>Загруженное изображение.</summary>
    public Image Изображение { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Изображение.Dispose();
        _поток.Dispose();
    }
}

/// <summary>
/// Наложение растрового логотипа (ТЗ §8.4, функция 47:
/// <c>Printing.Elements.LogoRenderer.DrawLogo</c>).
/// </summary>
/// <remarks>
/// Растр (PNG, BMP, JPEG) загружается из хранилища BLOB, пропорционально
/// масштабируется в границы поля и накладывается с учётом прозрачности.
/// Повреждённый файл замещается заглушкой с предупреждением.
/// </remarks>
public sealed class LogoRenderer
{
    /// <summary>Предельный размер наибольшей стороны логотипа по умолчанию, пикселей.</summary>
    public const int DefaultMaxSidePx = 130;

    /// <summary>
    /// Возвращает сведения о размере логотипа и предложение уменьшить его,
    /// если наибольшая сторона превышает предел.
    /// </summary>
    /// <param name="logoBytes">Содержимое логотипа.</param>
    /// <param name="maxSidePx">Предельный размер наибольшей стороны, пикселей.</param>
    /// <returns>Сведения о размере логотипа.</returns>
    public static LogoSizeInfo Inspect(byte[] logoBytes, int maxSidePx = DefaultMaxSidePx)
    {
        ArgumentNullException.ThrowIfNull(logoBytes);

        using var держатель = LoadImage(logoBytes);
        var изображение = держатель.Изображение;

        var наибольшаяСторона = Math.Max(изображение.Width, изображение.Height);
        var превышает = наибольшаяСторона > maxSidePx;

        var предупреждение = превышает
            ? $"Размер логотипа ({изображение.Width}×{изображение.Height} пикселей) превышает " +
              $"предел {maxSidePx} пикселей по наибольшей стороне. Предлагается уменьшить изображение."
            : null;

        return new LogoSizeInfo(изображение.Width, изображение.Height, превышает, предупреждение);
    }

    /// <summary>
    /// Пропорционально уменьшает логотип до предельного размера наибольшей стороны.
    /// </summary>
    /// <param name="logoBytes">Содержимое логотипа.</param>
    /// <param name="maxSidePx">Предельный размер наибольшей стороны, пикселей.</param>
    /// <returns>Уменьшенное изображение в формате PNG.</returns>
    public static byte[] Resize(byte[] logoBytes, int maxSidePx = DefaultMaxSidePx)
    {
        ArgumentNullException.ThrowIfNull(logoBytes);

        using var держатель = LoadImage(logoBytes);
        var изображение = держатель.Изображение;

        var наибольшаяСторона = Math.Max(изображение.Width, изображение.Height);

        if (наибольшаяСторона <= maxSidePx)
        {
            return logoBytes;
        }

        var коэффициент = (double)maxSidePx / наибольшаяСторона;
        var ширина = Math.Max(1, (int)Math.Round(изображение.Width * коэффициент));
        var высота = Math.Max(1, (int)Math.Round(изображение.Height * коэффициент));

        using var уменьшенное = new Bitmap(ширина, высота, PixelFormat.Format32bppArgb);

        using (var графика = Graphics.FromImage(уменьшенное))
        {
            графика.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            графика.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            графика.Clear(Color.Transparent);
            графика.DrawImage(изображение, new Rectangle(0, 0, ширина, высота));
        }

        using var поток = new MemoryStream();
        уменьшенное.Save(поток, ImageFormat.Png);

        return поток.ToArray();
    }

    /// <summary>
    /// Выводит логотип в заданные границы.
    /// </summary>
    /// <param name="context">Графический контекст.</param>
    /// <param name="logoBytes">Содержимое логотипа.</param>
    /// <param name="targetBounds">Границы размещения логотипа, мм.</param>
    /// <param name="keepAspectRatio">Признак сохранения пропорций.</param>
    /// <returns>Текст предупреждения, если логотип повреждён.</returns>
    public string? DrawLogo(
        IGraphicsContext context,
        byte[] logoBytes,
        RectangleF targetBounds,
        bool keepAspectRatio = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logoBytes);

        LogoImageHolder? держатель = null;

        try
        {
            держатель = LoadImage(logoBytes);
        }
#pragma warning disable CA1031 // Повреждённый логотип любого формата заменяется заглушкой.
        catch (Exception)
#pragma warning restore CA1031
        {
            DrawPlaceholder(context, targetBounds);
            return "Файл логотипа повреждён: вместо изображения выведена заглушка.";
        }

        // Поток-источник живёт вместе с изображением в держателе: вывод
        // растра выполняется внутри области его существования (F-A10).
        using (держатель)
        {
            var изображение = держатель.Изображение;
            var границы = targetBounds;

            if (keepAspectRatio)
            {
                var коэффициент = Math.Min(
                    targetBounds.Width / (float)изображение.Width,
                    targetBounds.Height / (float)изображение.Height);

                var ширина = изображение.Width * коэффициент;
                var высота = изображение.Height * коэффициент;

                границы = new RectangleF(
                    targetBounds.X + ((targetBounds.Width - ширина) / 2f),
                    targetBounds.Y + ((targetBounds.Height - высота) / 2f),
                    ширина,
                    высота);
            }

            context.DrawImageMm(
                изображение,
                границы.X,
                границы.Y,
                границы.Width,
                границы.Height);
        }

        return null;
    }

    /// <summary>
    /// Загружает растровое изображение из BLOB вместе с потоком-источником.
    /// </summary>
    /// <param name="logoBytes">Содержимое логотипа.</param>
    /// <returns>Держатель изображения и потока.</returns>
    /// <remarks>
    /// GDI+ требует открытого потока на всё время жизни изображения (F-A10):
    /// возвращается держатель, освобождающий оба объекта.
    /// </remarks>
    public static LogoImageHolder LoadImage(byte[] logoBytes)
    {
        ArgumentNullException.ThrowIfNull(logoBytes);

        var поток = new MemoryStream(logoBytes);
        Image изображение;

        try
        {
            изображение = Image.FromStream(поток);
        }
        catch
        {
            поток.Dispose();
            throw;
        }

        if (изображение.Width <= 0 || изображение.Height <= 0)
        {
            изображение.Dispose();
            поток.Dispose();

            throw new InvalidOperationException("Файл логотипа повреждён.");
        }

        return new LogoImageHolder(изображение, поток);
    }

    private static void DrawPlaceholder(IGraphicsContext context, RectangleF targetBounds)
    {
        context.FillRectangleMm(
            targetBounds.X,
            targetBounds.Y,
            targetBounds.Width,
            targetBounds.Height);
    }
}
