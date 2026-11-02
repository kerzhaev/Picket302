using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Picket302.Domain.Licensing;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Идентификатор оборудования для привязки лицензии
/// (ТЗ §8.10, функция 120: «Вычисляет аппаратный идентификатор несколькими
/// способами (серийный номер тома, UUID платы, составной отпечаток), результат
/// — в реестр HKCU»).
/// </summary>
/// <remarks>
/// <para>
/// Идентификатор имеет стабильный текстовый формат: 64 шестнадцатеричных
/// символа (SHA-256). Варианты вычисления различаются областью имён в
/// хеш-функции, поэтому значения разных вариантов никогда не совпадают.
/// </para>
/// <para>
/// Источники данных — ветка реестра <c>HKLM\SYSTEM\MountedDevices</c>
/// (идентификатор тома системного диска, который ведёт диспетчер томов) и
/// ветка <c>HKLM\HARDWARE\DESCRIPTION\System\BIOS</c> (сведения о
/// материнской плате). Обращения к WMI не выполняются: пакет
/// <c>System.Management</c> в решение не подключён.
/// </para>
/// <para>
/// Вычисленное значение сохраняется в ветке реестра
/// <c>HKCU\Software\Picket302\Licensing</c> и при повторном чтении
/// возвращается из неё без повторного обследования оборудования.
/// </para>
/// </remarks>
public sealed class MachineIdentity
{
    /// <summary>Имя значения с составным отпечатком оборудования.</summary>
    public const string ИмяСоставногоОтпечатка = "MachineId";

    /// <summary>Имя значения с отпечатком тома системного диска.</summary>
    public const string ИмяОтпечаткаТома = "MachineId.VolumeSerial";

    /// <summary>Имя значения с отпечатком материнской платы.</summary>
    public const string ИмяОтпечаткаПлаты = "MachineId.BoardUuid";

    private const string РазделительОбластиИмён = "Picket302/MachineId/";

    private static readonly string[] ИменаПараметровПлаты =
    [
        "BaseBoardManufacturer",
        "BaseBoardProduct",
        "BaseBoardVersion",
        "SystemManufacturer",
        "SystemProductName",
        "SystemFamily",
        "SystemSKU",
        "BIOSVendor",
        "BIOSVersion",
    ];

    private readonly LicensingRegistry _реестр;
    private readonly Action<string>? _журнал;
    private readonly Dictionary<MachineIdVariant, string> _кэш = [];

    /// <summary>
    /// Создаёт вычислитель идентификатора оборудования.
    /// </summary>
    /// <param name="журнал">
    /// Приёмник сообщений о невозможности вычислить выбранный вариант;
    /// <see langword="null"/> — сообщения не фиксируются.
    /// </param>
    /// <param name="реестр">
    /// Хранилище значений ветки лицензирования; по умолчанию — реестр Windows
    /// (<c>HKCU\Software\Picket302\Licensing</c>).
    /// </param>
    public MachineIdentity(Action<string>? журнал = null, LicensingRegistry? реестр = null)
    {
        _журнал = журнал;
        _реестр = реестр ?? LicensingRegistry.Windows();
    }

    /// <summary>
    /// Возвращает идентификатор оборудования выбранного варианта.
    /// </summary>
    /// <param name="variant">Вариант вычисления.</param>
    /// <returns>
    /// Стабильная строка из 64 шестнадцатеричных символов (SHA-256).
    /// </returns>
    public string GetMachineId(MachineIdVariant variant)
    {
        if (_кэш.TryGetValue(variant, out var вычисленное))
        {
            return вычисленное;
        }

        var имяЗначения = ИмяЗначения(variant);
        var сохранённое = _реестр.Прочитать(имяЗначения);

        if (!string.IsNullOrWhiteSpace(сохранённое))
        {
            _кэш[variant] = сохранённое;

            return сохранённое;
        }

        var значение = Вычислить(variant);

        _реестр.Записать(имяЗначения, значение);
        _кэш[variant] = значение;

        return значение;
    }

    /// <summary>
    /// Возвращает составной отпечаток оборудования — вариант, применяемый
    /// по умолчанию при привязке лицензии.
    /// </summary>
    /// <returns>Составной отпечаток оборудования.</returns>
    public string GetCompositeMachineId() => GetMachineId(MachineIdVariant.Composite);

    /// <summary>
    /// Имя значения реестра для варианта вычисления.
    /// </summary>
    /// <param name="variant">Вариант вычисления.</param>
    /// <returns>Имя значения в ветке <c>HKCU\Software\Picket302\Licensing</c>.</returns>
    public static string ИмяЗначения(MachineIdVariant variant) => variant switch
    {
        MachineIdVariant.VolumeSerial => ИмяОтпечаткаТома,
        MachineIdVariant.BoardUuid => ИмяОтпечаткаПлаты,
        _ => ИмяСоставногоОтпечатка,
    };

    /// <summary>
    /// Вычисляет идентификатор выбранного варианта.
    /// </summary>
    /// <param name="variant">Вариант вычисления.</param>
    /// <returns>Идентификатор оборудования.</returns>
    private string Вычислить(MachineIdVariant variant)
    {
        var отпечатокТома = ПрочитатьОтпечатокТома();
        var отпечатокПлаты = ПрочитатьОтпечатокПлаты();

        var составной = Хеш(
            РазделительОбластиИмён + "Composite|" + отпечатокТома + "|" + отпечатокПлаты);

        switch (variant)
        {
            case MachineIdVariant.VolumeSerial:
                if (отпечатокТома is null)
                {
                    ЗаписатьВЖурнал(
                        "Отпечаток тома системного диска недоступен — используется составной отпечаток оборудования.");

                    return составной;
                }

                return Хеш(РазделительОбластиИмён + "VolumeSerial|" + отпечатокТома);

            case MachineIdVariant.BoardUuid:
                if (отпечатокПлаты is null)
                {
                    ЗаписатьВЖурнал(
                        "Сведения о материнской плате недоступны — используется составной отпечаток оборудования.");

                    return составной;
                }

                return Хеш(РазделительОбластиИмён + "BoardUuid|" + отпечатокПлаты);

            default:
                return составной;
        }
    }

    /// <summary>
    /// Читает отпечаток тома системного диска из ветки реестра
    /// <c>HKLM\SYSTEM\MountedDevices</c>.
    /// </summary>
    /// <returns>Отпечаток тома либо <see langword="null"/>.</returns>
    private string? ПрочитатьОтпечатокТома()
    {
        try
        {
            var корень = Path.GetPathRoot(Environment.SystemDirectory);

            if (string.IsNullOrEmpty(корень))
            {
                return null;
            }

            var буква = корень.TrimEnd('\\', '/');

            if (буква.Length < 2)
            {
                return null;
            }

            // Значения вида \DosDevices\C: лежат в ветке MountedDevices
            // (F-C1): прежде читалась ветка SYSTEM, в которой этих значений
            // нет, и отпечаток тома всегда был пустым.
            using var ветка = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\MountedDevices");

            if (ветка is null)
            {
                return null;
            }

            foreach (var имя in new[] { "\\DosDevices\\" + буква + ":", "\\??\\" + буква + ":" })
            {
                if (ветка.GetValue(имя) is byte[] данные && данные.Length > 0)
                {
                    return "том:" + буква + ":" + ПреобразоватьВHex(данные);
                }
            }

            return null;
        }
        catch (Exception исключение) when (исключение is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException)
        {
            ЗаписатьВЖурнал(
                "Отпечаток тома системного диска недоступен: " + исключение.Message);

            return null;
        }
    }

    /// <summary>
    /// Читает сведения о материнской плате из ветки реестра
    /// <c>HKLM\HARDWARE\DESCRIPTION\System\BIOS</c>.
    /// </summary>
    /// <returns>Отпечаток платы либо <see langword="null"/>.</returns>
    private string? ПрочитатьОтпечатокПлаты()
    {
        try
        {
            using var ветка = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");

            if (ветка is null)
            {
                return null;
            }

            var части = new List<string>();

            foreach (var имя in ИменаПараметровПлаты)
            {
                if (ветка.GetValue(имя) is string значение && !string.IsNullOrWhiteSpace(значение))
                {
                    части.Add(имя + "=" + значение.Trim());
                }
            }

            return части.Count == 0 ? null : string.Join("|", части);
        }
        catch (Exception исключение) when (исключение is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException)
        {
            ЗаписатьВЖурнал(
                "Сведения о материнской плате недоступны: " + исключение.Message);

            return null;
        }
    }

    /// <summary>
    /// Вычисляет шестнадцатеричный SHA-256 от строки.
    /// </summary>
    /// <param name="значение">Исходная строка.</param>
    /// <returns>64 шестнадцатеричных символа в нижнем регистре.</returns>
    private static string Хеш(string значение)
    {
        var хеш = SHA256.HashData(Encoding.UTF8.GetBytes(значение));

        return ПреобразоватьВHex(хеш);
    }

    /// <summary>
    /// Преобразует байты в шестнадцатеричную строку нижнего регистра.
    /// </summary>
    /// <param name="данные">Исходные байты.</param>
    /// <returns>Шестнадцатеричная строка.</returns>
    private static string ПреобразоватьВHex(byte[] данные)
    {
        var символы = new char[данные.Length * 2];

        for (var индекс = 0; индекс < данные.Length; индекс++)
        {
            var байт = данные[индекс];

            символы[индекс * 2] = HexСимвол(байт >> 4);
            символы[(индекс * 2) + 1] = HexСимвол(байт & 0x0F);
        }

        return new string(символы);
    }

    /// <summary>
    /// Возвращает шестнадцатеричный символ по значению 0..15.
    /// </summary>
    /// <param name="значение">Значение тетрады.</param>
    /// <returns>Символ шестнадцатеричного алфавита.</returns>
    private static char HexСимвол(int значение) => (char)(значение < 10
        ? '0' + значение
        : 'a' + (значение - 10));

    /// <summary>
    /// Фиксирует сообщение в журнале, если он передан конструктором.
    /// </summary>
    /// <param name="сообщение">Текст сообщения.</param>
    private void ЗаписатьВЖурнал(string сообщение) => _журнал?.Invoke(сообщение);
}
