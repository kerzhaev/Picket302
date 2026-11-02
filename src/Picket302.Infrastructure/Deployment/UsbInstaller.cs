using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Picket302.Infrastructure.Deployment;

/// <summary>
/// Режим установки на USB-флешку (ТЗ §10.6, функции 105 и 130).
/// </summary>
public enum UsbInstallMode
{
    /// <summary>
    /// «Всё целиком»: на флешку копируются исполняемый файл оболочки,
    /// сопутствующие файлы (сборки, конфигурации), переносная СУБД и файл базы.
    /// </summary>
    Full = 0,

    /// <summary>
    /// «Только база данных» (рекомендуемый режим): на флешку копируется только
    /// файл базы данных, программа остаётся на рабочих станциях.
    /// </summary>
    DatabaseOnly = 1,
}

/// <summary>
/// Результат установки на USB-флешку (ТЗ §10.6, функции 105 и 130).
/// </summary>
/// <param name="IsSuccess">Признак успешной установки.</param>
/// <param name="Error">Текст ошибки на русском языке; при успехе — <see langword="null"/>.</param>
/// <param name="InstalledPath">Путь установки на флешке; при ошибке — <see langword="null"/>.</param>
/// <param name="RequiredBytes">Требуемый объём, байт.</param>
/// <param name="AvailableBytes">Доступный объём, байт.</param>
public sealed record UsbInstallResult(
    bool IsSuccess,
    string? Error,
    string? InstalledPath,
    long RequiredBytes,
    long AvailableBytes);

/// <summary>
/// План установки на USB-флешку: требуемый и доступный объём, перечень файлов
/// (ТЗ §10.6: «Мастер показывает требуемый и доступный объем»).
/// </summary>
/// <param name="Mode">Режим установки.</param>
/// <param name="RequiredBytes">Требуемый объём, байт.</param>
/// <param name="AvailableBytes">Доступный объём, байт.</param>
/// <param name="IsEnoughSpace">Признак достаточности свободного места.</param>
/// <param name="Files">Перечень копируемых файлов (пути относительно целевой папки).</param>
public sealed record UsbInstallPlan(
    UsbInstallMode Mode,
    long RequiredBytes,
    long AvailableBytes,
    bool IsEnoughSpace,
    IReadOnlyList<string> Files);

/// <summary>
/// Установка программы и базы данных на USB-флешку (ТЗ §10.6, функции 105 и 130).
/// </summary>
/// <remarks>
/// <para>
/// Предусмотрены два режима: «Всё целиком» и «Только база данных» (рекомендуемый).
/// В обоих режимах база копируется не файловым копированием, а механизмом
/// <c>VACUUM INTO</c> — образ гарантированно целостен и не содержит незакрытых
/// страниц (приём взят из <c>Picket302.Infrastructure.Backup.BackupService</c>).
/// </para>
/// <para>
/// Недостаток места на флешке блокирует установку до освобождения места. Извлечение
/// флешки в процессе копирования приводит к откату: файлы, скопированные в рамках
/// текущей операции, удаляются, а пользователю возвращается текст ошибки.
/// </para>
/// </remarks>
public sealed class UsbInstaller
{
    /// <summary>Имя папки установки на флешке.</summary>
    public const string ИмяПапкиУстановки = "Picket302";

    /// <summary>Имя файла базы данных на флешке.</summary>
    public const string ИмяФайлаБазы = "picket302.db";

    /// <summary>
    /// Расширения файлов, входящих в поставку программы: исполняемый файл,
    /// сборки, конфигурации, словари и шрифты.
    /// </summary>
    private static readonly string[] РасширенияПоставки =
    [
        ".exe",
        ".dll",
        ".json",
        ".config",
        ".xml",
        ".lng",
        ".dat",
        ".ttf",
        ".txt",
        ".md",
        ".ico",
        ".png",
    ];

    /// <summary>Имена служебных каталогов сборки, исключаемых из поставки.</summary>
    private static readonly string[] СлужебныеКаталоги = ["obj", "bin"];

    private readonly string _путьБазы;
    private readonly string _каталогПрограммы;
    private readonly Func<string, long> _определитьДоступныйОбъём;
    private readonly Action<string, string> _скопироватьФайл;

    /// <summary>
    /// Создаёт установщик на USB-флешку.
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных текущей установки.</param>
    /// <param name="каталогПрограммы">
    /// Каталог поставки программы; по умолчанию — каталог исполняемого файла
    /// (<see cref="AppContext.BaseDirectory"/>).
    /// </param>
    /// <param name="определитьДоступныйОбъём">
    /// Определение свободного места на носителе по пути; по умолчанию —
    /// свойство <see cref="DriveInfo.AvailableFreeSpace"/> корневого каталога.
    /// </param>
    /// <param name="скопироватьФайл">
    /// Копирование файла программы; по умолчанию — <see cref="File.Copy(string, string)"/>.
    /// </param>
    public UsbInstaller(
        string путьБазы,
        string? каталогПрограммы = null,
        Func<string, long>? определитьДоступныйОбъём = null,
        Action<string, string>? скопироватьФайл = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьБазы);

        _путьБазы = Path.GetFullPath(путьБазы);
        _каталогПрограммы = Path.GetFullPath(каталогПрограммы ?? AppContext.BaseDirectory);
        _определитьДоступныйОбъём = определитьДоступныйОбъём ?? ОпределитьДоступныйОбъёмПоУмолчанию;
        // Повторная установка перезаписывает файлы (F-C6): File.Copy без
        // overwrite падал IOException при существующем файле на флешке.
        _скопироватьФайл = скопироватьФайл ?? ((источник, приёмник) => File.Copy(источник, приёмник, overwrite: true));
    }

    /// <summary>Путь к файлу базы данных текущей установки.</summary>
    public string ПутьБазы => _путьБазы;

    /// <summary>Каталог поставки программы.</summary>
    public string КаталогПрограммы => _каталогПрограммы;

    /// <summary>
    /// Рассчитывает план установки на флешку: требуемый объём, доступный объём
    /// и перечень копируемых файлов.
    /// </summary>
    /// <param name="usbRoot">Корень флешки (например, <c>E:\</c>).</param>
    /// <param name="mode">Режим установки.</param>
    /// <returns>План установки.</returns>
    public UsbInstallPlan PlanInstall(string usbRoot, UsbInstallMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usbRoot);

        var целеваяПапка = ЦелеваяПапка(usbRoot);
        var файлы = new List<string>();
        long требуется = 0;

        if (mode == UsbInstallMode.Full)
        {
            foreach (var (источник, относительный) in СобратьФайлыПоставки())
            {
                файлы.Add(относительный);
                требуется += РазмерФайла(источник);
            }
        }

        файлы.Add(ИмяФайлаБазы);
        требуется += РазмерФайла(_путьБазы);

        var доступно = _определитьДоступныйОбъём(целеваяПапка);

        return new UsbInstallPlan(
            mode,
            требуется,
            доступно,
            доступно >= требуется,
            файлы);
    }

    /// <summary>
    /// Устанавливает программу и базу данных на флешку.
    /// </summary>
    /// <param name="usbRoot">Корень флешки (например, <c>E:\</c>).</param>
    /// <param name="mode">Режим установки.</param>
    /// <returns>
    /// Результат установки: при нехватке места и при сбое копирования —
    /// <see cref="UsbInstallResult.IsSuccess"/> равно <see langword="false"/>
    /// и заполнен текст ошибки.
    /// </returns>
    public UsbInstallResult InstallToUsb(string usbRoot, UsbInstallMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usbRoot);

        var план = PlanInstall(usbRoot, mode);

        if (!план.IsEnoughSpace)
        {
            return new UsbInstallResult(
                false,
                $"Недостаточно места на USB-флешке: требуется {ФорматОбъёма(план.RequiredBytes)}, "
                    + $"доступно {ФорматОбъёма(план.AvailableBytes)}. "
                    + "Освободите место на флешке и повторите установку.",
                null,
                план.RequiredBytes,
                план.AvailableBytes);
        }

        var целеваяПапка = ЦелеваяПапка(usbRoot);
        var скопированныеФайлы = new List<string>();
        var созданныеПапки = new List<string>();

        try
        {
            Directory.CreateDirectory(целеваяПапка);
            созданныеПапки.Add(целеваяПапка);

            if (mode == UsbInstallMode.Full)
            {
                foreach (var (источник, относительный) in СобратьФайлыПоставки())
                {
                    var цель = Path.Combine(целеваяПапка, относительный);
                    var каталогЦели = Path.GetDirectoryName(цель);

                    if (!string.IsNullOrEmpty(каталогЦели) && !Directory.Exists(каталогЦели))
                    {
                        Directory.CreateDirectory(каталогЦели);
                        созданныеПапки.Add(каталогЦели);
                    }

                    _скопироватьФайл(источник, цель);
                    скопированныеФайлы.Add(цель);
                }
            }

            var цельБазы = Path.Combine(целеваяПапка, ИмяФайлаБазы);

            СкопироватьОбразБазы(цельБазы);
            скопированныеФайлы.Add(цельБазы);

            return new UsbInstallResult(true, null, целеваяПапка, план.RequiredBytes, план.AvailableBytes);
        }
        catch (IOException ошибка)
        {
            Откатить(скопированныеФайлы, созданныеПапки);

            return new UsbInstallResult(
                false,
                $"Установка прервана: копирование на флешку не удалось ({ошибка.Message}). "
                    + "Проверьте, что флешка подключена, и повторите установку.",
                null,
                план.RequiredBytes,
                план.AvailableBytes);
        }
        catch (UnauthorizedAccessException ошибка)
        {
            Откатить(скопированныеФайлы, созданныеПапки);

            return new UsbInstallResult(
                false,
                $"Установка прервана: нет доступа к флешке ({ошибка.Message}). "
                    + "Проверьте права записи на носитель и повторите установку.",
                null,
                план.RequiredBytes,
                план.AvailableBytes);
        }
        catch (SqliteException ошибка)
        {
            Откатить(скопированныеФайлы, созданныеПапки);

            return new UsbInstallResult(
                false,
                $"Установка прервана: не удалось снять образ базы данных ({ошибка.Message}).",
                null,
                план.RequiredBytes,
                план.AvailableBytes);
        }
    }

    /// <summary>
    /// Возвращает путь целевой папки установки на флешке.
    /// </summary>
    /// <param name="usbRoot">Корень флешки.</param>
    /// <returns>Путь целевой папки.</returns>
    private static string ЦелеваяПапка(string usbRoot)
        => Path.Combine(Path.GetFullPath(usbRoot), ИмяПапкиУстановки);

    /// <summary>
    /// Собирает перечень файлов поставки программы: исполняемый файл,
    /// сопутствующие сборки и конфигурации; служебные каталоги сборки и файл
    /// базы данных исключаются (база копируется отдельно).
    /// </summary>
    /// <returns>Перечень пар «источник — путь относительно целевой папки».</returns>
    private IReadOnlyList<(string Источник, string ОтносительныйПуть)> СобратьФайлыПоставки()
    {
        if (!Directory.Exists(_каталогПрограммы))
        {
            return Array.Empty<(string, string)>();
        }

        var корень = _каталогПрограммы;
        var файлы = new List<(string Источник, string ОтносительныйПуть)>();

        foreach (var файл in Directory.EnumerateFiles(корень, "*", SearchOption.AllDirectories))
        {
            var расширение = Path.GetExtension(файл);

            if (!РасширенияПоставки.Contains(расширение, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var относительный = Path.GetRelativePath(корень, файл);

            if (СодержитСлужебныйКаталог(относительный))
            {
                continue;
            }

            if (string.Equals(файл, _путьБазы, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            файлы.Add((файл, относительный));
        }

        return файлы
            .OrderBy(пара => пара.ОтносительныйПуть, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Проверяет, содержит ли относительный путь служебный каталог сборки
    /// (<c>obj</c> либо <c>bin</c>).
    /// </summary>
    /// <param name="относительныйПуть">Путь относительно каталога поставки.</param>
    /// <returns><see langword="true"/>, если путь содержит служебный каталог.</returns>
    private static bool СодержитСлужебныйКаталог(string относительныйПуть)
    {
        var части = относительныйПуть.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return части.Any(часть => СлужебныеКаталоги.Contains(часть, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Возвращает размер файла; отсутствующий файл даёт нулевой размер.
    /// </summary>
    /// <param name="путь">Путь к файлу.</param>
    /// <returns>Размер файла, байт.</returns>
    private static long РазмерФайла(string путь)
    {
        var информация = new FileInfo(путь);

        return информация.Exists ? информация.Length : 0;
    }

    /// <summary>
    /// Копирует базу данных на флешку механизмом <c>VACUUM INTO</c>: образ
    /// гарантированно целостен и не содержит незакрытых страниц.
    /// </summary>
    /// <param name="цельБазы">Путь файла образа на флешке.</param>
    private void СкопироватьОбразБазы(string цельБазы)
    {
        if (!File.Exists(_путьБазы))
        {
            throw new FileNotFoundException(
                $"Файл базы данных не найден: {_путьБазы}.",
                _путьБазы);
        }

        if (File.Exists(цельБазы))
        {
            File.Delete(цельБазы);
        }

        using var соединение = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _путьБазы,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());

        соединение.Open();

        using var команда = соединение.CreateCommand();
        команда.CommandText = "VACUUM INTO $path;";
        команда.Parameters.AddWithValue("$path", цельБазы);
        команда.ExecuteNonQuery();
    }

    /// <summary>
    /// Откатывает частично выполненное копирование: удаляет файлы, скопированные
    /// в рамках текущей операции, и созданные при ней каталоги.
    /// </summary>
    /// <param name="скопированныеФайлы">Скопированные файлы.</param>
    /// <param name="созданныеПапки">Созданные каталоги.</param>
    private static void Откатить(List<string> скопированныеФайлы, List<string> созданныеПапки)
    {
        foreach (var файл in скопированныеФайлы)
        {
            try
            {
                if (File.Exists(файл))
                {
                    File.Delete(файл);
                }
            }
            catch (IOException)
            {
                // Откат не должен прерывать сообщение об исходной ошибке.
            }
            catch (UnauthorizedAccessException)
            {
                // Откат не должен прерывать сообщение об исходной ошибке.
            }
        }

        foreach (var папка in созданныеПапки.AsEnumerable().Reverse())
        {
            try
            {
                if (Directory.Exists(папка) && !Directory.EnumerateFileSystemEntries(папка).Any())
                {
                    Directory.Delete(папка);
                }
            }
            catch (IOException)
            {
                // Откат не должен прерывать сообщение об исходной ошибке.
            }
            catch (UnauthorizedAccessException)
            {
                // Откат не должен прерывать сообщение об исходной ошибке.
            }
        }
    }

    /// <summary>
    /// Определяет свободное место на носителе по свойству корневого каталога.
    /// </summary>
    /// <param name="путь">Путь внутри носителя.</param>
    /// <returns>Свободное место, байт; при недоступности — ноль.</returns>
    private static long ОпределитьДоступныйОбъёмПоУмолчанию(string путь)
    {
        try
        {
            var корень = Path.GetPathRoot(Path.GetFullPath(путь));

            if (!string.IsNullOrEmpty(корень))
            {
                return new DriveInfo(корень).AvailableFreeSpace;
            }
        }
        catch (ArgumentException)
        {
            // Носитель не определён: свободное место неизвестно.
        }
        catch (IOException)
        {
            // Носитель недоступен: свободное место неизвестно.
        }
        catch (UnauthorizedAccessException)
        {
            // Нет доступа к носителю: свободное место неизвестно.
        }

        return 0;
    }

    /// <summary>
    /// Форматирует объём для показа пользователю.
    /// </summary>
    /// <param name="байты">Объём, байт.</param>
    /// <returns>Текст объёма.</returns>
    public static string ФорматОбъёма(long байты)
    {
        string[] единицы = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

        double значение = байты;
        var индекс = 0;

        while (значение >= 1024 && индекс < единицы.Length - 1)
        {
            значение /= 1024;
            индекс++;
        }

        return индекс == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{значение:0} {единицы[индекс]}")
            : string.Create(CultureInfo.CurrentCulture, $"{значение:0.#} {единицы[индекс]}");
    }
}
