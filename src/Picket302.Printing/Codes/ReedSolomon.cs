namespace Picket302.Printing.Codes;

/// <summary>
/// Арифметика поля Галуа GF(256) и расчёт корректирующих кодов Рида — Соломона
/// (ГОСТ Р 56042-2014; ISO/IEC 18004, раздел 8).
/// </summary>
internal static class ReedSolomon
{
    /// <summary>Порождающий многочлен поля: x^8 + x^4 + x^3 + x^2 + 1 (0x11D).</summary>
    private const int PrimitivePolynomial = 0x11D;

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static ReedSolomon()
    {
        var значение = 1;

        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)значение;
            Log[значение] = (byte)i;
            значение <<= 1;

            if ((значение & 0x100) != 0)
            {
                значение ^= PrimitivePolynomial;
            }
        }

        for (var i = 255; i < 512; i++)
        {
            Exp[i] = Exp[i - 255];
        }
    }

    /// <summary>
    /// Умножает два элемента поля.
    /// </summary>
    /// <param name="x">Первый множитель.</param>
    /// <param name="y">Второй множитель.</param>
    /// <returns>Произведение.</returns>
    public static byte Multiply(byte x, byte y)
    {
        if (x == 0 || y == 0)
        {
            return 0;
        }

        return Exp[Log[x] + Log[y]];
    }

    /// <summary>
    /// Рассчитывает порождающий многочлен степени <paramref name="degree"/>.
    /// </summary>
    /// <param name="degree">Степень многочлена.</param>
    /// <returns>Коэффициенты в порядке убывания степеней, без старшего коэффициента.</returns>
    public static byte[] BuildDivisor(int degree)
    {
        var результат = new byte[degree];
        результат[degree - 1] = 1;

        var корень = (byte)1;

        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < результат.Length; j++)
            {
                результат[j] = Multiply(результат[j], корень);

                if (j + 1 < результат.Length)
                {
                    результат[j] ^= результат[j + 1];
                }
            }

            корень = Multiply(корень, 0x02);
        }

        return результат;
    }

    /// <summary>
    /// Вычисляет остаток от деления данных на порождающий многочлен —
    /// корректирующие кодовые слова блока.
    /// </summary>
    /// <param name="data">Кодовые слова блока данных.</param>
    /// <param name="divisor">Порождающий многочлен.</param>
    /// <returns>Корректирующие кодовые слова.</returns>
    public static byte[] ComputeRemainder(byte[] data, byte[] divisor)
    {
        var остаток = new byte[divisor.Length];

        foreach (var байт in data)
        {
            var множитель = (byte)(байт ^ остаток[0]);
            Array.Copy(остаток, 1, остаток, 0, остаток.Length - 1);
            остаток[остаток.Length - 1] = 0;

            for (var i = 0; i < остаток.Length; i++)
            {
                остаток[i] ^= Multiply(divisor[i], множитель);
            }
        }

        return остаток;
    }
}
