using Picket302.Printing.Codes;

namespace Picket302.Tests.Printing;

/// <summary>
/// Независимый декодер QR-кода для проверки кодировщика: читает матрицу модулей
/// обратно в полезную нагрузку (ГОСТ Р 56042-2014; ISO/IEC 18004).
/// </summary>
/// <remarks>
/// Размещение модулей, маскирование и разбор блоков реализованы независимо от
/// кодировщика, чтобы проверка не воспроизводила его ошибки.
/// </remarks>
internal static class Декодер
{
    /// <summary>
    /// Читает кодовые слова кода из матрицы.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <returns>Кодовые слова данных.</returns>
    public static byte[] ПрочитатьКодовыеСлова(QrCode code)
    {
        var размер = code.Size;
        var служебные = КартаСлужебных(code.Version);
        var модули = new bool[размер * размер];

        for (var y = 0; y < размер; y++)
        {
            for (var x = 0; x < размер; x++)
            {
                модули[(y * размер) + x] = code.GetModule(x, y);
            }
        }

        var маска = ПрочитатьМаску(code);

        for (var y = 0; y < размер; y++)
        {
            for (var x = 0; x < размер; x++)
            {
                if (!служебные[(y *размер) + x] && БитМаски(маска, x, y))
                {
                    модули[(y * размер) + x] = !модули[(y * размер) + x];
                }
            }
        }

        var биты = ПрочитатьБиты(модули, служебные, размер);

        return СобратьКодовыеСлова(биты, code.Version, code.ErrorCorrection);
    }

    /// <summary>
    /// Восстанавливает полезную нагрузку из матрицы QR-кода.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <returns>Восстановленная строка.</returns>
    public static string ПрочитатьПолезнуюНагрузку(QrCode code)
    {
        var кодовыеСлова = ПрочитатьКодовыеСлова(code);
        var счётчикДлина = code.Version <= 9 ? 8 : 16;
        var режим = ReadBits(кодовыеСлова, 0, 4);
        var длина = ReadBits(кодовыеСлова, 4, счётчикДлина);

        if (режим != 0b0100)
        {
            throw new InvalidOperationException($"Ожидался байтовый режим кодирования, получен {режим}.");
        }

        var байты = new byte[длина];

        for (var i = 0; i < длина; i++)
        {
            var смещение = 4 + счётчикДлина + (i * 8);
            байты[i] = (byte)ReadBits(кодовыеСлова, смещение, 8);
        }

        return System.Text.Encoding.UTF8.GetString(байты);
    }

    /// <summary>
    /// Читает форматную информацию кода из матрицы.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <returns>15-разрядное значение форматной информации.</returns>
    public static int ПрочитатьФорматнуюИнформацию(QrCode code)
    {
        var биты = new int[15];

        for (var i = 0; i <= 5; i++)
        {
            биты[i] = code.GetModule(8, i) ? 1 : 0;
        }

        биты[6] = code.GetModule(8, 7) ? 1 : 0;
        биты[7] = code.GetModule(8, 8) ? 1 : 0;
        биты[8] = code.GetModule(7, 8) ? 1 : 0;

        for (var i = 9; i < 15; i++)
        {
            биты[i] = code.GetModule(14 - i, 8) ? 1 : 0;
        }

        var значение = 0;

        for (var i = 0; i < 15; i++)
        {
            значение |= биты[i] << i;
        }

        return значение;
    }

    private static int ПрочитатьМаску(QrCode code)
    {
        var значение = ПрочитатьФорматнуюИнформацию(code);
        var данные = (значение ^ 0x5412) >> 10;

        return данные & 0b111;
    }

    private static bool БитМаски(int маска, int x, int y) => маска switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => ((x / 3) + (y / 2)) % 2 == 0,
        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
        7 => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
        _ => throw new InvalidOperationException($"Неизвестная маска {маска}."),
    };

    private static bool[] КартаСлужебных(int версия)
    {
        var размер = (версия * 4) + 17;
        var служебные = new bool[размер * размер];

        void Служебный(int x, int y) => служебные[(y * размер) + x] = true;

        for (var i = 0; i < размер; i++)
        {
            Служебный(6, i);
            Служебный(i, 6);
        }

        foreach (var (cx, cy) in new[] { (0, 0), (размер - 7, 0), (0, размер - 7) })
        {
            for (var dy = -1; dy <= 7; dy++)
            {
                for (var dx = -1; dx <= 7; dx++)
                {
                    var x = cx + dx;
                    var y = cy + dy;

                    if (x >= 0 && x < размер && y >= 0 && y < размер)
                    {
                        Служебный(x, y);
                    }
                }
            }
        }

        var позиции = QrTables.ПозицииВыравнивания(версия);

        foreach (var cy in позиции)
        {
            foreach (var cx in позиции)
            {
                var пересекает = (cx == 6 && cy == 6)
                    || (cx == 6 && cy == размер - 7)
                    || (cx == размер - 7 && cy == 6);

                if (пересекает)
                {
                    continue;
                }

                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        Служебный(cx + dx, cy + dy);
                    }
                }
            }
        }

        for (var i = 0; i <= 8; i++)
        {
            if (i == 6)
            {
                continue;
            }

            Служебный(8, i);
            Служебный(i, 8);
        }

        for (var i = размер - 8; i < размер; i++)
        {
            Служебный(8, i);
            Служебный(i, 8);
        }

        if (версия >= 7)
        {
            for (var i = 0; i < 18; i++)
            {
                var a = размер - 11 + (i % 3);
                var b = i / 3;
                Служебный(a, b);
                Служебный(b, a);
            }
        }

        return служебные;
    }

    private static List<bool> ПрочитатьБиты(bool[] модули, bool[] служебные, int размер)
    {
        var столбцы = Enumerable.Range(0, размер).Reverse().ToList();
        столбцы.Remove(6);

        var пары = new List<(int Правый, int Левый)>();

        for (var i = 0; i + 1 < столбцы.Count; i += 2)
        {
            пары.Add((столбцы[i], столбцы[i + 1]));
        }

        var биты = new List<bool>();

        for (var индексПары = 0; индексПары < пары.Count; индексПары++)
        {
            var (правый, левый) = пары[индексПары];
            var вверх = индексПары % 2 == 0;

            for (var вертикаль = 0; вертикаль < размер; вертикаль++)
            {
                foreach (var x in new[] { правый, левый })
                {
                    var y = вверх ? размер - 1 - вертикаль : вертикаль;

                    if (!служебные[(y * размер) + x])
                    {
                        биты.Add(модули[(y * размер) + x]);
                    }
                }
            }
        }

        return биты;
    }

    private static byte[] СобратьКодовыеСлова(List<bool> биты, int версия, QrErrorCorrectionLevel уровень)
    {
        var (ecPerBlock, group1Blocks, group1Data, group2Blocks, group2Data) = QrTables.Блоков(версия, уровень);
        var всегоБлоков = group1Blocks + group2Blocks;

        var длины = new int[всегоБлоков];

        for (var i = 0; i < всегоБлоков; i++)
        {
            длины[i] = i < group1Blocks ? group1Data : group2Data;
        }

        var блоки = new byte[всегоБлоков][];

        for (var i = 0; i < всегоБлоков; i++)
        {
            блоки[i] = new byte[длины[i]];
        }

        var позиция = 0;
        var максимальнаяДлина = Math.Max(group1Data, group2Data);

        for (var i = 0; i < максимальнаяДлина; i++)
        {
            for (var b = 0; b < всегоБлоков; b++)
            {
                if (i >= длины[b])
                {
                    continue;
                }

                var значение = 0;

                for (var бит = 0; бит < 8; бит++)
                {
                    значение <<= 1;

                    if (позиция < биты.Count && биты[позиция])
                    {
                        значение |= 1;
                    }

                    позиция++;
                }

                блоки[b][i] = (byte)значение;
            }
        }

        var результат = new List<byte>();

        for (var b = 0; b < всегоБлоков; b++)
        {
            результат.AddRange(блоки[b]);
        }

        _ = ecPerBlock;

        return результат.ToArray();
    }

    private static int ReadBits(byte[] данные, int смещение, int длина)
    {
        var значение = 0;

        for (var i = 0; i < длина; i++)
        {
            var позиция = смещение + i;
            var байт = позиция / 8;

            if (байт >= данные.Length)
            {
                return значение << (длина - i);
            }

            значение = (значение << 1) | ((данные[байт] >> (7 - (позиция % 8))) & 1);
        }

        return значение;
    }
}
