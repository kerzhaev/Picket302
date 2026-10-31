using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Picket302.Printing.Elements;

/// <summary>
/// Кодирование и декодирование монохромного растра 1 бит на пиксель, используемого
/// для кэша QR-кодов (ТЗ §6.4; §3.1.8: «в кэш попадает несжатый растр
/// 1-bpp/8-bpp в исходном DPI без антиалиасинга»).
/// </summary>
/// <remarks>
/// <para>Формат блоба: сигнатура <c>QR01</c>, ширина и высота (int32), разрешение
/// (int32), число бит на пиксель (1), служебные байты, затем строки растра,
/// каждая выровнена по границе байта; единичный бит означает тёмный модуль.</para>
/// <para>Палитра растра задаётся явно: индекс 0 — белый, индекс 1 — чёрный.
/// Стандартная палитра GDI+ для 1-bpp обратна (индекс 0 — чёрный), поэтому без
/// явного задания палитры установленный бит означал бы белый модуль, а весь код
/// выводился сплошным чёрным квадратом.</para>
/// </remarks>
public static class QrBitmapCodec
{
    /// <summary>Сигнатура формата блоба.</summary>
    private static readonly byte[] Сигнатура = { (byte)'Q', (byte)'R', (byte)'0', (byte)'1' };

    /// <summary>Размер заголовка блоба, байт.</summary>
    private const int РазмерЗаголовка = 20;

    /// <summary>
    /// Индекс палитры, соответствующий чёрному цвету: палитра монохромного
    /// растра задаётся явно (0 — белый, 1 — чёрный).
    /// </summary>
    public const byte ИндексЧёрного = 1;

    /// <summary>Индекс палитры, соответствующий чёрному цвету.</summary>
    public static byte ЧёрныйИндекс => ИндексЧёрного;

    /// <summary>
    /// Создаёт монохромный растр с явно заданной палитрой: индекс 0 — белый,
    /// индекс 1 — чёрный.
    /// </summary>
    /// <param name="width">Ширина растра, пикселей.</param>
    /// <param name="height">Высота растра, пикселей.</param>
    /// <param name="dpi">Разрешение растра.</param>
    /// <returns>Растр 1 бит на пиксель.</returns>
    public static Bitmap CreateMonochromeBitmap(int width, int height, int dpi = 300)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Размеры растра должны быть положительными.");
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format1bppIndexed);

        var палитра = bitmap.Palette;
        палитра.Entries[0] = Color.White;
        палитра.Entries[1] = Color.Black;
        bitmap.Palette = палитра;

        bitmap.SetResolution(dpi, dpi);

        return bitmap;
    }

    /// <summary>
    /// Кодирует монохромное изображение в блоб кэша.
    /// </summary>
    /// <param name="bitmap">Изображение 1 бит на пиксель.</param>
    /// <param name="dpi">Разрешение растра.</param>
    /// <returns>Блоб кэша.</returns>
    public static byte[] Encode(Bitmap bitmap, int dpi)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var ширина = bitmap.Width;
        var высота = bitmap.Height;
        var шаг = (ширина + 7) / 8;

        var данные = new byte[РазмерЗаголовка + (шаг * высота)];

        Array.Copy(Сигнатура, 0, данные, 0, Сигнатура.Length);
        BitConverter.GetBytes(ширина).CopyTo(данные, 4);
        BitConverter.GetBytes(высота).CopyTo(данные, 8);
        BitConverter.GetBytes(dpi).CopyTo(данные, 12);
        данные[16] = 1;

        var заблокировано = bitmap.LockBits(
            new Rectangle(0, 0, ширина, высота),
            ImageLockMode.ReadOnly,
            PixelFormat.Format1bppIndexed);

        try
        {
            var буфер = new byte[заблокировано.Stride * высота];
            Marshal.Copy(заблокировано.Scan0, буфер, 0, буфер.Length);

            var тёмныйИндекс = ОпределитьИндексЧёрного(bitmap);

            for (var y = 0; y < высота; y++)
            {
                for (var x = 0; x < ширина; x++)
                {
                    var бит = (буфер[(y * заблокировано.Stride) + (x / 8)] >> (7 - (x % 8))) & 1;

                    if (бит == тёмныйИндекс)
                    {
                        данные[РазмерЗаголовка + (y * шаг) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(заблокировано);
        }

        return данные;
    }

    /// <summary>
    /// Декодирует блоб кэша в монохромное изображение.
    /// </summary>
    /// <param name="blob">Блоб кэша.</param>
    /// <returns>Изображение 1 бит на пиксель.</returns>
    public static Bitmap Decode(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        if (blob.Length < РазмерЗаголовка)
        {
            throw new InvalidOperationException("Блоб кэша QR-кода повреждён: слишком короткий.");
        }

        for (var i = 0; i < Сигнатура.Length; i++)
        {
            if (blob[i] != Сигнатура[i])
            {
                throw new InvalidOperationException("Блоб кэша QR-кода повреждён: неверная сигнатура.");
            }
        }

        var ширина = BitConverter.ToInt32(blob, 4);
        var высота = BitConverter.ToInt32(blob, 8);
        var dpi = BitConverter.ToInt32(blob, 12);
        var шаг = (ширина + 7) / 8;

        if (ширина <= 0 || высота <= 0 || blob.Length < РазмерЗаголовка + (шаг * высота))
        {
            throw new InvalidOperationException("Блоб кэша QR-кода повреждён: неверные размеры растра.");
        }

        var bitmap = CreateMonochromeBitmap(ширина, высота, dpi);

        var заблокировано = bitmap.LockBits(
            new Rectangle(0, 0, ширина, высота),
            ImageLockMode.WriteOnly,
            PixelFormat.Format1bppIndexed);

        try
        {
            var буфер = new byte[заблокировано.Stride * высота];
            Array.Clear(буфер);

            for (var y = 0; y < высота; y++)
            {
                for (var x = 0; x < ширина; x++)
                {
                    var тёмный = (blob[РазмерЗаголовка + (y * шаг) + (x / 8)] >> (7 - (x % 8))) & 1;

                    if (тёмный != 0)
                    {
                        буфер[(y * заблокировано.Stride) + (x / 8)] |= (byte)(ИндексЧёрного << (7 - (x % 8)));
                    }
                }
            }

            Marshal.Copy(буфер, 0, заблокировано.Scan0, буфер.Length);
        }
        finally
        {
            bitmap.UnlockBits(заблокировано);
        }

        return bitmap;
    }

    /// <summary>
    /// Определяет индекс палитры, соответствующий тёмному цвету, по палитре
    /// конкретного растра.
    /// </summary>
    /// <param name="bitmap">Растр 1 бит на пиксель.</param>
    /// <returns>Индекс тёмного цвета в палитре растра.</returns>
    private static byte ОпределитьИндексЧёрного(Bitmap bitmap)
    {
        var палитра = bitmap.Palette.Entries;

        for (var i = 0; i < палитра.Length; i++)
        {
            if (палитра[i].R < 128 && палитра[i].G < 128 && палитра[i].B < 128)
            {
                return (byte)i;
            }
        }

        return ИндексЧёрного;
    }
}
