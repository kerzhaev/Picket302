using Picket302.Printing.Codes;

namespace Picket302.Tests.Printing;

/// <summary>
/// Независимый декодер QR-кода для проверки кодировщика: читает матрицу модулей
/// обратно в полезную нагрузку (ГОСТ Р 56042-2014; ISO/IEC 18004).
/// </summary>
/// <remarks>
/// <para>Размещение модулей, маскирование и разбор блоков реализованы независимо от
/// кодировщика, чтобы проверка не воспроизводила его ошибки.</para>
/// <para>Арифметика поля GF(256) и коррекция ошибок Рида — Соломона (синдромы,
/// алгоритм Берлекэмп — Мэсси, поиск корней Чен, оценка Форни) реализованы здесь
/// же: без них нельзя проверить, что корректирующие кодовые слова блока рассчитаны
/// верно, и что повреждённый код остаётся читаемым.</para>
/// </remarks>
internal static class Декодер
{
    /// <summary>Порождающий многочлен поля: x^8 + x^4 + x^3 + x^2 + 1 (0x11D).</summary>
    private const int ПорождающийПолином = 0x11D;

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static Декодер()
    {
        var значение = 1;

        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)значение;
            Log[значение] = (byte)i;
            значение <<= 1;

            if ((значение & 0x100) != 0)
            {
                значение ^= ПорождающийПолином;
            }
        }

        for (var i = 255; i < 512; i++)
        {
            Exp[i] = Exp[i - 255];
        }
    }

    /// <summary>
    /// Читает кодовые слова данных кода из матрицы.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <returns>Кодовые слова данных.</returns>
    public static byte[] ПрочитатьКодовыеСлова(QrCode code)
    {
        var блоки = ПрочитатьБлоки(code, исправлять: false);
        var результат = new List<byte>();

        foreach (var блок in блоки.Блоки)
        {
            for (var i = 0; i < блок.Данные.Length; i++)
            {
                результат.Add(блок.Данные[i]);
            }
        }

        return результат.ToArray();
    }

    /// <summary>
    /// Восстанавливает полезную нагрузку из матрицы QR-кода с проверкой и
    /// исправлением ошибок по корректирующим кодовым словам.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <returns>Восстановленная строка.</returns>
    public static string ПрочитатьПолезнуюНагрузку(QrCode code)
    {
        var блоки = ПрочитатьБлоки(code, исправлять: true);
        var кодовыеСлова = new List<byte>();

        foreach (var блок in блоки.Блоки)
        {
            кодовыеСлова.AddRange(блок.Данные);
        }

        var данные = кодовыеСлова.ToArray();
        var счётчикДлина = code.Version <= 9 ? 8 : 16;
        var режим = ReadBits(данные, 0, 4);
        var длина = ReadBits(данные, 4, счётчикДлина);

        if (режим != 0b0100)
        {
            throw new InvalidOperationException($"Ожидался байтовый режим кодирования, получен {режим}.");
        }

        var байты = new byte[длина];

        for (var i = 0; i < длина; i++)
        {
            var смещение = 4 + счётчикДлина + (i * 8);
            байты[i] = (byte)ReadBits(данные, смещение, 8);
        }

        return System.Text.Encoding.UTF8.GetString(байты);
    }

    /// <summary>
    /// Проверяет корректирующие кодовые слова блоков: все синдромы
    /// Рида — Соломона должны быть нулевыми.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <param name="исправлять">Если задано <see langword="true"/>, ошибки
    /// сначала исправляются, и синдромы проверяются у исправленных слов.</param>
    /// <returns><see langword="true"/>, если кодовое слово каждого блока —
    /// корректное кодовое слово Рида — Соломона.</returns>
    public static bool СиндромыНулевые(QrCode code, bool исправлять = false)
    {
        var блоки = ПрочитатьБлоки(code, исправлять);

        foreach (var блок in блоки.Блоки)
        {
            foreach (var синдром in Синдромы(блок.ПолноеСлово, блоки.КорректирующихНаБлок))
            {
                if (синдром != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Возвращает число ошибок, исправленных в кодовых словах кода.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <returns>Число исправленных ошибок по всем блокам.</returns>
    public static int ИсправитьОшибки(QrCode code)
    {
        var блоки = ПрочитатьБлоки(code, исправлять: true);

        var всего = 0;

        foreach (var блок in блоки.Блоки)
        {
            всего += блок.ИсправленоОшибок;
        }

        return всего;
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

    /// <summary>
    /// Возвращает позиции модулей данных в порядке чтения кода.
    /// </summary>
    /// <param name="code">QR-код.</param>
    /// <returns>Координаты модулей данных в порядке следования кодовых слов.</returns>
    /// <remarks>
    /// Позволяет повредить код в заранее известных местах: поворот модуля
    /// данных меняет один бит одного кодового слова блока.
    /// </remarks>
    public static List<(int X, int Y)> ПозицииМодулейДанных(QrCode code)
    {
        var служебные = КартаСлужебных(code.Version);

        return ПозицииЧтения(code.Size, служебные);
    }

    private static List<bool> ПрочитатьБиты(bool[] модули, bool[] служебные, int размер)
    {
        var позиции = ПозицииЧтения(размер, служебные);
        var биты = new List<bool>(позиции.Count);

        foreach (var (x, y) in позиции)
        {
            биты.Add(модули[(y * размер) + x]);
        }

        return биты;
    }

    /// <summary>
    /// Строит порядок чтения модулей данных (снизу вверх, справа налево,
    /// парными столбцами) и исключает служебные модули.
    /// </summary>
    private static List<(int X, int Y)> ПозицииЧтения(int размер, bool[] служебные)
    {
        var столбцы = Enumerable.Range(0, размер).Reverse().ToList();
        столбцы.Remove(6);

        var пары = new List<(int Правый, int Левый)>();

        for (var i = 0; i + 1 < столбцы.Count; i += 2)
        {
            пары.Add((столбцы[i], столбцы[i + 1]));
        }

        var позиции = new List<(int X, int Y)>();

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
                        позиции.Add((x, y));
                    }
                }
            }
        }

        return позиции;
    }

    /// <summary>
    /// Блок кода: кодовые слова данных и корректирующие кодовые слова.
    /// </summary>
    private sealed class Блок
    {
        public byte[] Данные { get; init; } = Array.Empty<byte>();

        public byte[] Коррекция { get; init; } = Array.Empty<byte>();

        public int ИсправленоОшибок { get; set; }

        /// <summary>Кодовое слово целиком: данные и корректирующие слова.</summary>
        public byte[] ПолноеСлово
        {
            get
            {
                var полное = new byte[Данные.Length + Коррекция.Length];
                Buffer.BlockCopy(Данные, 0, полное, 0, Данные.Length);
                Buffer.BlockCopy(Коррекция, 0, полное, Данные.Length, Коррекция.Length);

                return полное;
            }
        }
    }

    /// <summary>
    /// Разобранные блоки кода.
    /// </summary>
    private sealed class Разбор
    {
        public List<Блок> Блоки { get; init; } = new();

        public int КорректирующихНаБлок { get; init; }
    }

    private static Разбор ПрочитатьБлоки(QrCode code, bool исправлять)
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

        return РазобратьБлоки(биты, code.Version, code.ErrorCorrection, исправлять);
    }

    private static Разбор РазобратьБлоки(
        List<bool> биты,
        int версия,
        QrErrorCorrectionLevel уровень,
        bool исправлять)
    {
        var (ecPerBlock, group1Blocks, group1Data, group2Blocks, group2Data) = QrTables.Блоков(версия, уровень);
        var всегоБлоков = group1Blocks + group2Blocks;

        var длины = new int[всегоБлоков];

        for (var i = 0; i < всегоБлоков; i++)
        {
            длины[i] = i < group1Blocks ? group1Data : group2Data;
        }

        var блокиДанных = new byte[всегоБлоков][];

        for (var i = 0; i < всегоБлоков; i++)
        {
            блокиДанных[i] = new byte[длины[i]];
        }

        var блокиКоррекции = new byte[всегоБлоков][];

        for (var i = 0; i < всегоБлоков; i++)
        {
            блокиКоррекции[i] = new byte[ecPerBlock];
        }

        // Данные и корректирующие слова чередуются блоками (перемежение).
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

                блокиДанных[b][i] = ПрочитатьБайт(биты, позиция);
                позиция += 8;
            }
        }

        for (var i = 0; i < ecPerBlock; i++)
        {
            for (var b = 0; b < всегоБлоков; b++)
            {
                блокиКоррекции[b][i] = ПрочитатьБайт(биты, позиция);
                позиция += 8;
            }
        }

        var разбор = new Разбор { КорректирующихНаБлок = ecPerBlock };

        for (var b = 0; b < всегоБлоков; b++)
        {
            var блок = new Блок { Данные = блокиДанных[b], Коррекция = блокиКоррекции[b] };

            if (исправлять)
            {
                блок.ИсправленоОшибок = ИсправитьБлок(блок, ecPerBlock);
            }

            разбор.Блоки.Add(блок);
        }

        return разбор;
    }

    private static byte ПрочитатьБайт(List<bool> биты, int смещение)
    {
        var значение = 0;

        for (var бит = 0; бит < 8; бит++)
        {
            значение <<= 1;

            var позиция = смещение + бит;

            if (позиция < биты.Count && биты[позиция])
            {
                значение |= 1;
            }
        }

        return (byte)значение;
    }

    /// <summary>
    /// Вычисляет синдромы кодового слова: S_j = c(α^j), где α — корень
    /// порождающего многочлена (α^0 — первый корень).
    /// </summary>
    /// <param name="кодовоеСлово">Кодовое слово блока.</param>
    /// <param name="корректирующих">Число корректирующих кодовых слов.</param>
    /// <returns>Массив синдромов.</returns>
    private static byte[] Синдромы(byte[] кодовоеСлово, int корректирующих)
    {
        var синдромы = new byte[корректирующих];

        for (var j = 0; j < корректирующих; j++)
        {
            var корень = Exp[j];
            var значение = (byte)0;

            foreach (var байт in кодовоеСлово)
            {
                значение = (byte)((Умножить(значение, корень)) ^ байт);
            }

            синдромы[j] = значение;
        }

        return синдромы;
    }

    /// <summary>
    /// Исправляет ошибки в кодовом слове блока по корректирующим кодовым словам.
    /// </summary>
    /// <param name="блок">Блок кода.</param>
    /// <param name="корректирующих">Число корректирующих кодовых слов.</param>
    /// <returns>Число исправленных ошибок.</returns>
    private static int ИсправитьБлок(Блок блок, int корректирующих)
    {
        var кодовоеСлово = блок.ПолноеСлово;
        var синдромы = Синдромы(кодовоеСлово, корректирующих);

        if (синдромы.All(с => с == 0))
        {
            return 0;
        }

        var локатор = НайтиЛокаторОшибок(синдромы);

        if (локатор is null)
        {
            return 0;
        }

        var позиции = НайтиПозицииОшибок(локатор, кодовоеСлово.Length);

        if (позиции.Count == 0)
        {
            return 0;
        }

        var величины = ОценитьВеличиныОшибок(синдромы, локатор, позиции, кодовоеСлово.Length);

        var исправлено = 0;

        foreach (var позиция in позиции)
        {
            var величина = величины[позиция];

            if (величина == 0)
            {
                continue;
            }

            кодовоеСлово[позиция] ^= величина;
            исправлено++;
        }

        if (исправлено > 0)
        {
            Buffer.BlockCopy(
                кодовоеСлово,
                0,
                блок.Данные,
                0,
                блок.Данные.Length);
            Buffer.BlockCopy(
                кодовоеСлово,
                блок.Данные.Length,
                блок.Коррекция,
                0,
                блок.Коррекция.Length);
        }

        return исправлено;
    }

    /// <summary>
    /// Алгоритм Берлекэмп — Мэсси: строит многочлен локатора ошибок.
    /// </summary>
    /// <param name="синдромы">Синдромы кодового слова.</param>
    /// <returns>Коэффициенты локатора (индекс — степень) либо <see langword="null"/>.</returns>
    private static byte[]? НайтиЛокаторОшибок(byte[] синдромы)
    {
        var c = new byte[] { 1 };
        var b = new byte[] { 1 };
        var l = 0;
        var m = 1;
        var bЗначение = (byte)1;

        for (var n = 0; n < синдромы.Length; n++)
        {
            var дельта = синдромы[n];

            for (var i = 1; i <= l && i < c.Length; i++)
            {
                дельта ^= Умножить(c[i], синдромы[n - i]);
            }

            if (дельта == 0)
            {
                m++;

                continue;
            }

            var коэффициент = Умножить(дельта, Обратное(bЗначение));
            var t = (byte[])c.Clone();

            var длина = Math.Max(c.Length, b.Length + m);
            Array.Resize(ref c, длина);

            for (var i = 0; i < b.Length; i++)
            {
                c[i + m] ^= Умножить(коэффициент, b[i]);
            }

            if (2 * l <= n)
            {
                l = n + 1 - l;
                b = t;
                bЗначение = дельта;
                m = 1;
            }
            else
            {
                m++;
            }
        }

        Array.Resize(ref c, l + 1);

        return c;
    }

    /// <summary>
    /// Поиск корней Чен: определяет позиции ошибочных кодовых слов.
    /// </summary>
    /// <param name="локатор">Коэффициенты локатора ошибок.</param>
    /// <param name="длина">Длина кодового слова.</param>
    /// <returns>Позиции ошибок в кодовом слове.</returns>
    private static List<int> НайтиПозицииОшибок(byte[] локатор, int длина)
    {
        var позиции = new List<int>();

        for (var j = 0; j < длина; j++)
        {
            // Корень локатора: x = α^(-j).
            var корень = Exp[255 - (j % 255)];
            var значение = (byte)0;

            for (var i = локатор.Length - 1; i >= 0; i--)
            {
                значение = (byte)((Умножить(значение, корень)) ^ локатор[i]);
            }

            if (значение == 0)
            {
                позиции.Add(длина - 1 - j);
            }
        }

        return позиции;
    }

    /// <summary>
    /// Оценка величин ошибок по Форни.
    /// </summary>
    /// <param name="синдромы">Синдромы кодового слова.</param>
    /// <param name="локатор">Коэффициенты локатора ошибок.</param>
    /// <param name="позиции">Позиции ошибок.</param>
    /// <param name="длина">Длина кодового слова.</param>
    /// <returns>Величина ошибки для каждой позиции.</returns>
    private static Dictionary<int, byte> ОценитьВеличиныОшибок(
        byte[] синдромы,
        byte[] локатор,
        List<int> позиции,
        int длина)
    {
        var результат = new Dictionary<int, byte>();

        // Ω(x) = (S(x) · σ(x)) mod x^n, где S(x) = Σ S_j x^j.
        var омега = new byte[синдромы.Length];

        for (var i = 0; i < синдромы.Length; i++)
        {
            var значение = (byte)0;

            for (var j = 0; j <= i && j < локатор.Length; j++)
            {
                значение ^= Умножить(локатор[j], синдромы[i - j]);
            }

            омега[i] = значение;
        }

        // Формальная производная локатора: σ'(x) = Σ (i mod 2) · σ_i · x^(i-1).
        var производная = new byte[Math.Max(1, локатор.Length - 1)];

        for (var i = 1; i < локатор.Length; i++)
        {
            if ((i % 2) == 1)
            {
                производная[i - 1] = локатор[i];
            }
        }

        foreach (var позиция in позиции)
        {
            var j = длина - 1 - позиция;
            var xInverse = Exp[(255 - (j % 255)) % 255];

            var значениеОмега = (byte)0;

            for (var i = омега.Length - 1; i >= 0; i--)
            {
                значениеОмега = (byte)((Умножить(значениеОмега, xInverse)) ^ омега[i]);
            }

            var значениеПроизводной = (byte)0;

            for (var i = производная.Length - 1; i >= 0; i--)
            {
                значениеПроизводной = (byte)((Умножить(значениеПроизводной, xInverse)) ^ производная[i]);
            }

            if (значениеПроизводной == 0)
            {
                continue;
            }

            // Y = Ω(X^-1) / σ'(X^-1) · X.
            var x = Exp[j % 255];
            var величина = Умножить(
                Умножить(значениеОмега, Обратное(значениеПроизводной)),
                x);

            результат[позиция] = величина;
        }

        return результат;
    }

    /// <summary>
    /// Умножает два элемента поля GF(256).
    /// </summary>
    private static byte Умножить(byte x, byte y)
    {
        if (x == 0 || y == 0)
        {
            return 0;
        }

        return Exp[Log[x] + Log[y]];
    }

    /// <summary>
    /// Возвращает обратный элемент поля GF(256).
    /// </summary>
    private static byte Обратное(byte значение)
    {
        if (значение == 0)
        {
            throw new InvalidOperationException("Нулевой элемент поля не обратим.");
        }

        return Exp[255 - Log[значение]];
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
