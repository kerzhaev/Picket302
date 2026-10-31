using System.Runtime.InteropServices;

namespace Picket302.Printing.Devices;

/// <summary>
/// Структура DEVMODE драйвера принтера (Windows SDK, <c>DEVMODEW</c>).
/// </summary>
/// <remarks>
/// Структура повторяет разметку Win32: 32 символа имени устройства, сведения о
/// версиях, флаги полей, объединённый блок параметров носителя, параметры
/// цвета и дуплекса, имя формы и разрешение. Размер структуры на x64 — 220 байт,
/// что совпадает с <c>sizeof(DEVMODEW)</c>.
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DevMode
{
    /// <summary>Имя устройства (CCHDEVICENAME = 32 символа).</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;

    /// <summary>Номер версии спецификации драйвера.</summary>
    public short SpecVersion;

    /// <summary>Номер версии драйвера.</summary>
    public short DriverVersion;

    /// <summary>Размер структуры DEVMODE в байтах.</summary>
    public short Size;

    /// <summary>Размер дополнительных данных драйвера.</summary>
    public short DriverExtra;

    /// <summary>Маска заполненных полей (DM_*).</summary>
    public int Fields;

    /// <summary>Ориентация листа (DMORIENT_*).</summary>
    public short Orientation;

    /// <summary>Формат бумаги (DMPAPER_*).</summary>
    public short PaperSize;

    /// <summary>Длина бумаги (в десятых долях миллиметра).</summary>
    public short PaperLength;

    /// <summary>Ширина бумаги (в десятых долях миллиметра).</summary>
    public short PaperWidth;

    /// <summary>Масштаб печати, проценты.</summary>
    public short Scale;

    /// <summary>Число копий.</summary>
    public short Copies;

    /// <summary>Лоток подачи по умолчанию (DMBIN_*).</summary>
    public short DefaultSource;

    /// <summary>Качество печати (DMRES_*).</summary>
    public short PrintQuality;

    /// <summary>Цветная печать (DMCOLOR_*).</summary>
    public short Color;

    /// <summary>Режим двусторонней печати (DMDUP_*).</summary>
    public short Duplex;

    /// <summary>Разрешение по вертикали, точек на дюйм.</summary>
    public short YResolution;

    /// <summary>Параметры сглаживания шрифтов (DMTT_*).</summary>
    public short TTOption;

    /// <summary>Порядок сортировки копий (DMCOLLATE_*).</summary>
    public short Collate;

    /// <summary>Имя формы носителя, сообщаемое драйвером (32 символа).</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string FormName;

    /// <summary>Разрешение дисплея (логических точек на дюйм).</summary>
    public short LogPixels;

    /// <summary>Число бит на пиксель.</summary>
    public int BitsPerPel;

    /// <summary>Ширина растра в пикселях.</summary>
    public int PelsWidth;

    /// <summary>Высота растра в пикселях.</summary>
    public int PelsHeight;

    /// <summary>Флаги отображения.</summary>
    public int DisplayFlags;

    /// <summary>Частота обновления дисплея.</summary>
    public int DisplayFrequency;

    /// <summary>Метод управления цветом.</summary>
    public int ICMMethod;

    /// <summary>Намерение системы управления цветом.</summary>
    public int ICMIntent;

    /// <summary>Тип носителя.</summary>
    public int MediaType;

    /// <summary>Тип сглаживания цветов.</summary>
    public int DitherType;

    /// <summary>Зарезервированное поле 1.</summary>
    public int Reserved1;

    /// <summary>Зарезервированное поле 2.</summary>
    public int Reserved2;

    /// <summary>Ширина панорамирования.</summary>
    public int PanningWidth;

    /// <summary>Высота панорамирования.</summary>
    public int PanningHeight;
}

/// <summary>
/// Константы драйвера принтера: форматы бумаги (DMPAPER_*), ориентация
/// (DMORIENT_*), двусторонняя печать (DMDUP_*) и возможности устройства,
/// запрашиваемые функцией <c>DeviceCapabilitiesW</c>.
/// </summary>
public static class DevModeConstants
{
    /// <summary>Формат Letter (215,9×279,4 мм).</summary>
    public const short DMPAPER_LETTER = 1;

    /// <summary>Формат Legal (215,9×355,6 мм).</summary>
    public const short DMPAPER_LEGAL = 5;

    /// <summary>Формат Executive (184,1×266,7 мм).</summary>
    public const short DMPAPER_EXECUTIVE = 7;

    /// <summary>Формат A3 (297×420 мм).</summary>
    public const short DMPAPER_A3 = 8;

    /// <summary>Формат A4 (210×297 мм).</summary>
    public const short DMPAPER_A4 = 9;

    /// <summary>Формат A4 малый (203×279 мм).</summary>
    public const short DMPAPER_A4SMALL = 10;

    /// <summary>Формат A5 (148×210 мм).</summary>
    public const short DMPAPER_A5 = 11;

    /// <summary>Формат B4 (250×353 мм).</summary>
    public const short DMPAPER_B4 = 12;

    /// <summary>Формат B5 (176×250 мм).</summary>
    public const short DMPAPER_B5 = 13;

    /// <summary>Формат Folio (215,9×330,2 мм).</summary>
    public const short DMPAPER_FOLIO = 14;

    /// <summary>Формат Quarto (215,9×275,2 мм).</summary>
    public const short DMPAPER_QUARTO = 15;

    /// <summary>Конверт «приглашение» (216,9×279,4 мм).</summary>
    public const short DMPAPER_ENV_INVITE = 47;

    /// <summary>Конверт №10 (229×114 мм).</summary>
    public const short DMPAPER_ENV_10 = 20;

    /// <summary>Конверт №11 (244×114 мм).</summary>
    public const short DMPAPER_ENV_11 = 21;

    /// <summary>Конверт №12 (275×114 мм).</summary>
    public const short DMPAPER_ENV_12 = 22;

    /// <summary>Конверт №9 (225×114 мм).</summary>
    public const short DMPAPER_ENV_9 = 19;

    /// <summary>Конверт №14 (235×125 мм).</summary>
    public const short DMPAPER_ENV_14 = 23;

    /// <summary>Конверт DL (110×220 мм).</summary>
    public const short DMPAPER_ENV_DL = 27;

    /// <summary>Конверт C5 (162×229 мм).</summary>
    public const short DMPAPER_ENV_C5 = 28;

    /// <summary>Конверт C3 (324×458 мм).</summary>
    public const short DMPAPER_ENV_C3 = 29;

    /// <summary>Конверт C4 (229×324 мм).</summary>
    public const short DMPAPER_ENV_C4 = 30;

    /// <summary>Конверт C6 (114×162 мм).</summary>
    public const short DMPAPER_ENV_C6 = 31;

    /// <summary>Конверт C65 (114×229 мм).</summary>
    public const short DMPAPER_ENV_C65 = 32;

    /// <summary>Конверт B4 (250×353 мм).</summary>
    public const short DMPAPER_ENV_B4 = 33;

    /// <summary>Конверт B5 (176×250 мм).</summary>
    public const short DMPAPER_ENV_B5 = 34;

    /// <summary>Конверт B6 (176×125 мм).</summary>
    public const short DMPAPER_ENV_B6 = 35;

    /// <summary>Пользовательский формат носителя.</summary>
    public const short DMPAPER_USER = 256;

    /// <summary>Книжная ориентация.</summary>
    public const short DMORIENT_PORTRAIT = 1;

    /// <summary>Альбомная ориентация.</summary>
    public const short DMORIENT_LANDSCAPE = 2;

    /// <summary>Односторонняя печать.</summary>
    public const short DMDUP_SIMPLEX = 1;

    /// <summary>Двусторонняя печать с переворотом по длинному краю.</summary>
    public const short DMDUP_VERTICAL = 2;

    /// <summary>Двусторонняя печать с переворотом по короткому краю.</summary>
    public const short DMDUP_HORIZONTAL = 3;

    /// <summary>
    /// Константа <c>DC_PAPERS</c> функции <c>DeviceCapabilitiesW</c>:
    /// перечисление поддерживаемых драйвером форматов бумаги
    /// (wingdi.h: <c>#define DC_PAPERS 2</c>).
    /// </summary>
    public const int DC_PAPERS = 2;

    /// <summary>
    /// Константа <c>DC_PAPERSIZE</c> функции <c>DeviceCapabilitiesW</c>:
    /// размер текущего формата носителя в сотых долях миллиметра
    /// (wingdi.h: <c>#define DC_PAPERSIZE 3</c>).
    /// </summary>
    public const int DC_PAPERSIZE = 3;

    /// <summary>
    /// Константа <c>DC_DUPLEX</c> функции <c>DeviceCapabilitiesW</c>:
    /// перечисление поддерживаемых режимов двусторонней печати
    /// (wingdi.h: <c>#define DC_DUPLEX 7</c>).
    /// </summary>
    public const int DC_DUPLEX = 7;

    /// <summary>
    /// Константа <c>DC_BINS</c> функции <c>DeviceCapabilitiesW</c>:
    /// перечисление поддерживаемых лотков подачи
    /// (wingdi.h: <c>#define DC_BINS 6</c>).
    /// </summary>
    public const int DC_BINS = 6;

    /// <summary>
    /// Константа <c>DC_PAPERNAMES</c> функции <c>DeviceCapabilitiesW</c>:
    /// наименования поддерживаемых форматов бумаги
    /// (wingdi.h: <c>#define DC_PAPERNAMES 16</c>).
    /// </summary>
    public const int DC_PAPERNAMES = 16;
}
