using System.Text;

namespace Picket302.Printing.Codes;

/// <summary>
/// Кодировщик QR-кодов (ГОСТ Р 56042-2014; ISO/IEC 18004).
/// </summary>
/// <remarks>
/// <para>Реализован побайтовый (byte) режим кодирования данных, блоки коррекции
/// ошибок Рида — Соломона, восемь масок с выбором по штрафным оценкам, служебные
/// шаблоны (поисковые, временной, выравнивания), форматная и версионная
/// информация.</para>
/// <para>Если данные не помещаются в выбранную версию кода, выбрасывается
/// <see cref="QrPayloadTooLongException"/> (ТЗ §8.4, функция 48).</para>
/// </remarks>
public static class QrEncoder
{
    /// <summary>Индикатор режима «байт».</summary>
    private const int РежимБайт = 0b0100;

    /// <summary>Штраф за серию из пяти и более одинаковых модулей.</summary>
    private const int ШтрафСерии = 3;

    /// <summary>Штраф за блок 2×2 одинаковых модулей.</summary>
    private const int ШтрафБлок = 3;

    /// <summary>Штраф за шаблон 1:1:3:1:1.</summary>
    private const int ШтрафПоиск = 40;

    /// <summary>Штраф за отклонение доли тёмных модулей от половины.</summary>
    private const int ШтрафБаланс = 10;

    /// <summary>
    /// Кодирует строку данных в QR-код.
    /// </summary>
    /// <param name="payload">Кодируемая строка.</param>
    /// <param name="level">Уровень коррекции ошибок.</param>
    /// <returns>Сформированный QR-код.</returns>
    /// <exception cref="QrPayloadTooLongException">Данные не помещаются в версию кода.</exception>
    public static QrCode Encode(string payload, QrErrorCorrectionLevel level = QrErrorCorrectionLevel.Medium)
    {
        var данные = Encoding.UTF8.GetBytes(payload ?? string.Empty);
        var версия = ВыбратьВерсию(данные.Length, level);
        var поток = ПостроитьПотокДанных(данные, версия, level);
        var блоки = РазбитьНаБлоки(поток, версия, level);

        var матрица = СоздатьМатрицу(версия);
        матрица.РазместитьКодовыеСлова(блоки);

        var маска = ВыбратьМаску(матрица, level);

        матрица.НарисоватьСлужебныеЭлементы();
        матрица.РазместитьКодовыеСлова(матрица.КодовыеСлова);
        матрица.ПрименитьМаску(маска);
        матрица.НарисоватьФорматнуюИнформацию(level, маска);

        if (версия >= 7)
        {
            матрица.НарисоватьИнформациюОВерсии(версия);
        }

        return new QrCode(версия, level, маска, матрица.Модули);
    }

    /// <summary>
    /// Возвращает наибольшее число байт, помещающихся в версию кода
    /// при заданном уровне коррекции ошибок.
    /// </summary>
    /// <param name="level">Уровень коррекции ошибок.</param>
    /// <returns>Предельная длина полезной нагрузки в байтах.</returns>
    public static int МаксимальнаяДлинаПолезнойНагрузки(QrErrorCorrectionLevel level)
    {
        for (var версия = QrTables.MaxVersion; версия >= 1; версия--)
        {
            var ёмкостьБиты = 8 * QrTables.КодовыхСловДанных(версия, level);
            var счётчикБиты = ДлинаСчётчика(версия);

            if (8 + счётчикБиты <= ёмкостьБиты)
            {
                return (ёмкостьБиты - 4 - счётчикБиты) / 8;
            }
        }

        return 0;
    }

    /// <summary>
    /// Возвращает перемешанные кодовые слова кода (данные и корректирующие) для
    /// указанной строки и уровня коррекции.
    /// </summary>
    /// <param name="payload">Кодируемая строка.</param>
    /// <param name="level">Уровень коррекции ошибок.</param>
    /// <returns>Кодовые слова кода.</returns>
    public static byte[] ПостроитьКодовыеСлова(
        string payload,
        QrErrorCorrectionLevel level = QrErrorCorrectionLevel.Medium)
    {
        var данные = Encoding.UTF8.GetBytes(payload ?? string.Empty);
        var версия = ВыбратьВерсию(данные.Length, level);
        var поток = ПостроитьПотокДанных(данные, версия, level);

        return РазбитьНаБлоки(поток, версия, level);
    }

    /// <summary>
    /// Возвращает кодовые слова данных блоков (после разбора блоков, без
    /// корректирующих слов) — это и есть поток данных QR-кода.
    /// </summary>
    /// <param name="payload">Кодируемая строка.</param>
    /// <param name="level">Уровень коррекции ошибок.</param>
    /// <returns>Кодовые слова данных.</returns>
    public static byte[] ПостроитьБлокиДанных(
        string payload,
        QrErrorCorrectionLevel level = QrErrorCorrectionLevel.Medium)
    {
        var данные = Encoding.UTF8.GetBytes(payload ?? string.Empty);
        var версия = ВыбратьВерсию(данные.Length, level);
        var поток = ПостроитьПотокДанных(данные, версия, level);
        var блоки = РазделитьНаБлоки(поток, версия, level);

        var результат = new List<byte>();

        foreach (var блок in блоки)
        {
            результат.AddRange(блок);
        }

        return результат.ToArray();
    }

    /// <summary>
    /// Возвращает номер версии кода для строки данных.
    /// </summary>
    /// <param name="payload">Кодируемая строка.</param>
    /// <param name="level">Уровень коррекции ошибок.</param>
    /// <returns>Номер версии.</returns>
    public static int ВыбратьВерсиюДляСтроки(
        string payload,
        QrErrorCorrectionLevel level = QrErrorCorrectionLevel.Medium)
    {
        var данные = Encoding.UTF8.GetBytes(payload ?? string.Empty);

        return ВыбратьВерсию(данные.Length, level);
    }

    /// <summary>
    /// Вычисляет форматную информацию кода (15 бит с кодом Баррона — Хилла).
    /// </summary>
    /// <param name="level">Уровень коррекции ошибок.</param>
    /// <param name="mask">Номер маски.</param>
    /// <returns>Значение форматной информации.</returns>
    public static int ФорматнаяИнформация(QrErrorCorrectionLevel level, int mask)
    {
        var уровеньБиты = level switch
        {
            QrErrorCorrectionLevel.Low => 1,
            QrErrorCorrectionLevel.Medium => 0,
            QrErrorCorrectionLevel.Quartile => 3,
            QrErrorCorrectionLevel.High => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

        var данные = (уровеньБиты << 3) | mask;
        var остаток = данные;

        for (var i = 0; i < 10; i++)
        {
            остаток = (остаток << 1) ^ ((остаток >> 9) * 0x537);
        }

        return ((данные << 10) | остаток) ^ 0x5412;
    }

    /// <summary>
    /// Вычисляет информацию о версии кода (18 бит с кодом Баррона — Хилла).
    /// </summary>
    /// <param name="version">Версия кода (7–40).</param>
    /// <returns>Значение версионной информации.</returns>
    public static int ИнформацияОВерсии(int version)
    {
        if (version is < 7 or > QrTables.MaxVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        var остаток = version;

        for (var i = 0; i < 12; i++)
        {
            остаток = (остаток << 1) ^ ((остаток >> 11) * 0x1F25);
        }

        return (version << 12) | остаток;
    }

    private static int ДлинаСчётчика(int версия) => версия <= 9 ? 8 : 16;

    private static int ВыбратьВерсию(int длинаДанных, QrErrorCorrectionLevel level)
    {
        for (var версия = 1; версия <= QrTables.MaxVersion; версия++)
        {
            var ёмкостьБиты = 8 * QrTables.КодовыхСловДанных(версия, level);
            var используемыеБиты = 4 + ДлинаСчётчика(версия) + (8 * длинаДанных);

            if (используемыеБиты <= ёмкостьБиты)
            {
                return версия;
            }
        }

        throw new QrPayloadTooLongException(
            $"Строка длиной {длинаДанных} байт не помещается в QR-код: превышен предел " +
            $"версии {QrTables.MaxVersion} для уровня коррекции {level}.");
    }

    private static List<bool> ПостроитьПотокДанных(byte[] данные, int версия, QrErrorCorrectionLevel level)
    {
        var ёмкостьБиты = 8 * QrTables.КодовыхСловДанных(версия, level);
        var биты = new List<bool>(ёмкостьБиты);

        ДобавитьБиты(биты, РежимБайт, 4);
        ДобавитьБиты(биты, данные.Length, ДлинаСчётчика(версия));

        foreach (var байт in данные)
        {
            ДобавитьБиты(биты, байт, 8);
        }

        var терминатор = Math.Min(4, ёмкостьБиты - биты.Count);
        for (var i = 0; i < терминатор; i++)
        {
            биты.Add(false);
        }

        while (биты.Count % 8 != 0)
        {
            биты.Add(false);
        }

        var чередовать = true;

        while (биты.Count < ёмкостьБиты)
        {
            ДобавитьБиты(биты, чередовать ? 0xEC : 0x11, 8);
            чередовать = !чередовать;
        }

        return биты;
    }

    private static void ДобавитьБиты(List<bool> биты, int значение, int длина)
    {
        for (var i = длина - 1; i >= 0; i--)
        {
            биты.Add(((значение >> i) & 1) != 0);
        }
    }

    private static byte[][] РазделитьНаБлоки(List<bool> биты, int версия, QrErrorCorrectionLevel level)
    {
        var (ecPerBlock, group1Blocks, group1Data, group2Blocks, group2Data) = QrTables.Блоков(версия, level);
        var всегоБлоков = group1Blocks + group2Blocks;

        var блокиДанных = new byte[всегоБлоков][];
        var позиция = 0;

        for (var блок = 0; блок < всегоБлоков; блок++)
        {
            var длина = блок < group1Blocks ? group1Data : group2Data;
            var данныеБлока = new byte[длина];

            for (var i = 0; i < длина; i++)
            {
                данныеБлока[i] = 0;

                for (var бит = 0; бит < 8; бит++)
                {
                    if (биты[позиция])
                    {
                        данныеБлока[i] |= (byte)(1 << (7 - бит));
                    }

                    позиция++;
                }
            }

            блокиДанных[блок] = данныеБлока;
        }

        return блокиДанных;
    }

    private static byte[] РазбитьНаБлоки(List<bool> биты, int версия, QrErrorCorrectionLevel level)
    {
        var (ecPerBlock, group1Blocks, group1Data, group2Blocks, group2Data) = QrTables.Блоков(версия, level);
        var всегоБлоков = group1Blocks + group2Blocks;

        var блокиДанных = РазделитьНаБлоки(биты, версия, level);

        var делитель = ReedSolomon.BuildDivisor(ecPerBlock);
        var блокиКодов = new byte[всегоБлоков][];

        for (var блок = 0; блок < всегоБлоков; блок++)
        {
            блокиКодов[блок] = ReedSolomon.ComputeRemainder(блокиДанных[блок], делитель);
        }

        var результат = new List<byte>(биты.Count / 8 + (ecPerBlock * всегоБлоков));
        var максимальнаяДлинаДанных = Math.Max(group1Data, group2Data);

        for (var i = 0; i < максимальнаяДлинаДанных; i++)
        {
            foreach (var блок in блокиДанных)
            {
                if (i < блок.Length)
                {
                    результат.Add(блок[i]);
                }
            }
        }

        for (var i = 0; i < ecPerBlock; i++)
        {
            foreach (var блок in блокиКодов)
            {
                результат.Add(блок[i]);
            }
        }

        return результат.ToArray();
    }

    private static int ВыбратьМаску(QrMatrix матрица, QrErrorCorrectionLevel level)
    {
        var лучшаяМаска = 0;
        var лучшийШтраф = int.MaxValue;

        for (var маска = 0; маска < 8; маска++)
        {
            матрица.НарисоватьСлужебныеЭлементы();
            матрица.РазместитьКодовыеСлова(матрица.КодовыеСлова);
            матрица.ПрименитьМаску(маска);
            матрица.НарисоватьФорматнуюИнформацию(level, маска);

            var штраф = матрица.ШтрафнаяОценка();

            if (штраф < лучшийШтраф)
            {
                лучшийШтраф = штраф;
                лучшаяМаска = маска;
            }
        }

        return лучшаяМаска;
    }

    private static QrMatrix СоздатьМатрицу(int версия) => new(версия);

    /// <summary>
    /// Матрица модулей QR-кода со служебными элементами.
    /// </summary>
    private sealed class QrMatrix
    {
        private readonly bool[] _функциональные;

        public QrMatrix(int версия)
        {
            Версия = версия;
            Размер = (версия * 4) + 17;
            Модули = new bool[Размер * Размер];
            _функциональные = new bool[Размер * Размер];
            КодовыеСлова = Array.Empty<byte>();
        }

        public int Версия { get; }

        public int Размер { get; }

        public bool[] Модули { get; }

        public byte[] КодовыеСлова { get; set; }

        public void НарисоватьСлужебныеЭлементы()
        {
            Array.Clear(Модули);
            Array.Clear(_функциональные);

            for (var i = 0; i < Размер; i++)
            {
                УстановитьФункциональный(6, i, i % 2 == 0);
                УстановитьФункциональный(i, 6, i % 2 == 0);
            }

            НарисоватьПоисковый(0, 0);
            НарисоватьПоисковый(Размер - 7, 0);
            НарисоватьПоисковый(0, Размер - 7);

            var позиции = QrTables.ПозицииВыравнивания(Версия);

            foreach (var cy in позиции)
            {
                foreach (var cx in позиции)
                {
                    var пересекаетПоисковый =
                        (cx == 6 && cy == 6)
                        || (cx == 6 && cy == Размер - 7)
                        || (cx == Размер - 7 && cy == 6);

                    if (!пересекаетПоисковый)
                    {
                        НарисоватьВыравнивание(cx, cy);
                    }
                }
            }

            УстановитьФункциональный(8, Размер - 8, true);

            // Резервируются места под форматную информацию: первый экземпляр —
            // столбец x = 8 (строки 0..5, 7, 8) и строка y = 8 (столбцы 0..5, 7, 8);
            // второй экземпляр — столбец x = 8 и строка y = 8 со стороны нижнего
            // и правого краёв. Модуль с индексом 6 в этих местах принадлежит
            // временному шаблону и не резервируется.
            for (var i = 0; i <= 8; i++)
            {
                if (i == 6)
                {
                    continue;
                }

                УстановитьФункциональный(8, i, false);
                УстановитьФункциональный(i, 8, false);
            }

            for (var i = Размер - 8; i < Размер; i++)
            {
                УстановитьФункциональный(8, i, false);
                УстановитьФункциональный(i, 8, false);
            }

            // Для версий 7 и выше резервируются места под информацию о версии
            // (два блока 3×6 в правом верхнем и левом нижнем углах).
            if (Версия >= 7)
            {
                for (var i = 0; i < 18; i++)
                {
                    var a = Размер - 11 + (i % 3);
                    var b = i / 3;

                    УстановитьФункциональный(a, b, false);
                    УстановитьФункциональный(b, a, false);
                }
            }
        }

        public void РазместитьКодовыеСлова(byte[] кодовыеСлова)
        {
            КодовыеСлова = кодовыеСлова;

            var индекс = 0;
            var всегоБиты = кодовыеСлова.Length * 8;

            for (var правый = Размер - 1; правый >= 1; правый -= 2)
            {
                if (правый == 6)
                {
                    правый = 5;
                }

                for (var вертикаль = 0; вертикаль < Размер; вертикаль++)
                {
                    for (var j = 0; j < 2; j++)
                    {
                        var x = правый - j;
                        var вверх = ((правый + 1) & 2) == 0;
                        var y = вверх ? Размер - 1 - вертикаль : вертикаль;

                        if (!_функциональные[(y * Размер) + x] && индекс < всегоБиты)
                        {
                            Модули[(y * Размер) + x] = ((кодовыеСлова[индекс >> 3] >> (7 - (индекс & 7))) & 1) != 0;
                            индекс++;
                        }
                    }
                }
            }
        }

        public void ПрименитьМаску(int маска)
        {
            for (var y = 0; y < Размер; y++)
            {
                for (var x = 0; x < Размер; x++)
                {
                    if (_функциональные[(y * Размер) + x])
                    {
                        continue;
                    }

                    var инвертировать = маска switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => ((x / 3) + (y / 2)) % 2 == 0,
                        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
                        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
                        7 => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
                        _ => throw new ArgumentOutOfRangeException(nameof(маска)),
                    };

                    if (инвертировать)
                    {
                        Модули[(y * Размер) + x] = !Модули[(y * Размер) + x];
                    }
                }
            }
        }

        public void НарисоватьФорматнуюИнформацию(QrErrorCorrectionLevel level, int маска)
        {
            var биты = ФорматнаяИнформация(level, маска);

            for (var i = 0; i <= 5; i++)
            {
                УстановитьФункциональный(8, i, ПолучитьБит(биты, i));
            }

            УстановитьФункциональный(8, 7, ПолучитьБит(биты, 6));
            УстановитьФункциональный(8, 8, ПолучитьБит(биты, 7));
            УстановитьФункциональный(7, 8, ПолучитьБит(биты, 8));

            for (var i = 9; i < 15; i++)
            {
                УстановитьФункциональный(14 - i, 8, ПолучитьБит(биты, i));
            }

            for (var i = 0; i < 8; i++)
            {
                УстановитьФункциональный(Размер - 1 - i, 8, ПолучитьБит(биты, i));
            }

            for (var i = 8; i < 15; i++)
            {
                УстановитьФункциональный(8, Размер - 15 + i, ПолучитьБит(биты, i));
            }

            УстановитьФункциональный(8, Размер - 8, true);
        }

        public void НарисоватьИнформациюОВерсии(int версия)
        {
            var биты = ИнформацияОВерсии(версия);

            for (var i = 0; i < 18; i++)
            {
                var тёмный = ПолучитьБит(биты, i);
                var a = Размер - 11 + (i % 3);
                var b = i / 3;

                УстановитьФункциональный(a, b, тёмный);
                УстановитьФункциональный(b, a, тёмный);
            }
        }

        public int ШтрафнаяОценка()
        {
            var штраф = 0;

            for (var y = 0; y < Размер; y++)
            {
                штраф += ШтрафСтроки(ПолучитьСтроку(y));
            }

            for (var x = 0; x < Размер; x++)
            {
                var столбец = new bool[Размер];

                for (var y = 0; y < Размер; y++)
                {
                    столбец[y] = Модули[(y * Размер) + x];
                }

                штраф += ШтрафСтроки(столбец);
            }

            for (var y = 0; y < Размер - 1; y++)
            {
                for (var x = 0; x < Размер - 1; x++)
                {
                    var цвет = Модули[(y * Размер) + x];

                    if (цвет == Модули[(y * Размер) + x + 1]
                        && цвет == Модули[((y + 1) * Размер) + x]
                        && цвет == Модули[((y + 1) * Размер) + x + 1])
                    {
                        штраф += ШтрафБлок;
                    }
                }
            }

            var тёмных = 0;

            foreach (var модуль in Модули)
            {
                if (модуль)
                {
                    тёмных++;
                }
            }

            var всего = Размер * Размер;
            var к = ((Math.Abs((тёмных * 20) - (всего * 10)) + всего - 1) / всего) - 1;
            штраф += к * ШтрафБаланс;

            return штраф;
        }

        private int ШтрафСтроки(bool[] линия)
        {
            var штраф = 0;
            var длинаСерии = 1;

            for (var i = 1; i < линия.Length; i++)
            {
                if (линия[i] == линия[i - 1])
                {
                    длинаСерии++;

                    if (длинаСерии == 5)
                    {
                        штраф += ШтрафСерии;
                    }
                    else if (длинаСерии > 5)
                    {
                        штраф++;
                    }
                }
                else
                {
                    длинаСерии = 1;
                }
            }

            for (var i = 0; i + 11 <= линия.Length; i++)
            {
                if (ЕстьПоисковыйШаблон(линия, i) || ЕстьПоисковыйШаблон(линия, i, true))
                {
                    штраф += ШтрафПоиск;
                }
            }

            return штраф;
        }

        private static bool ЕстьПоисковыйШаблон(bool[] линия, int начало, bool обратный = false)
        {
            bool[] шаблон = { false, false, false, false, true, false, true, true, true, false, true };

            for (var i = 0; i < шаблон.Length; i++)
            {
                var индекс = обратный ? начало + (шаблон.Length - 1 - i) : начало + i;

                if (линия[индекс] != шаблон[i])
                {
                    return false;
                }
            }

            return true;
        }

        private bool[] ПолучитьСтроку(int y)
        {
            var строка = new bool[Размер];
            Array.Copy(Модули, y * Размер, строка, 0, Размер);
            return строка;
        }

        private void НарисоватьПоисковый(int cx, int cy)
        {
            for (var dy = -1; dy <= 7; dy++)
            {
                for (var dx = -1; dx <= 7; dx++)
                {
                    var x = cx + dx;
                    var y = cy + dy;

                    if (x < 0 || x >= Размер || y < 0 || y >= Размер)
                    {
                        continue;
                    }

                    var расстояние = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    var тёмный = расстояние <= 3 && расстояние != 2;

                    УстановитьФункциональный(x, y, тёмный);
                }
            }
        }

        private void НарисоватьВыравнивание(int cx, int cy)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    var расстояние = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    УстановитьФункциональный(cx + dx, cy + dy, расстояние != 1);
                }
            }
        }

        private void УстановитьФункциональный(int x, int y, bool тёмный)
        {
            Модули[(y * Размер) + x] = тёмный;
            _функциональные[(y * Размер) + x] = true;
        }

        private static bool ПолучитьБит(int значение, int индекс) => ((значение >> индекс) & 1) != 0;
    }
}
