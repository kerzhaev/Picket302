using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Picket302.Printing.Elements;

namespace Picket302.Infrastructure.Fonts;

/// <summary>
/// Контроль наличия шрифта ZIPcode (ТЗ §6.3; §8.6, функция 90:
/// <c>Infrastructure.Fonts.ZipcodeFontGuard</c>).
/// </summary>
/// <remarks>
/// <para>
/// Шрифт ZIPcode поставляется с программой и доступен в двух видах:
/// установлен в системе (тогда он виден в перечислении гарнитур) либо
/// лежит файлом в комплекте поставки (тогда он загружается приватной
/// коллекцией). Скрытая подмена на Arial/Times не допускается: при
/// отсутствии шрифта печать конверта блокируется.
/// </para>
/// <para>
/// Регистрация в сеансе (<c>AddFontResourceW</c> + запись в
/// <c>HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts</c>) выполняется
/// для других процессов (COM-сервер, предпросмотр). Собственный процесс
/// GDI+ шрифт, зарегистрированный таким способом, НЕ видит: гарнитура не
/// появляется в перечислении установленных шрифтов, и создание
/// <c>Font("ZIPcode")</c> молча даёт «Microsoft Sans Serif». Поэтому
/// отрисовка идёт семейством <see cref="PostIndexFontFamily"/>, а не поиском
/// по имени.
/// </para>
/// </remarks>
public sealed class ZipcodeFontGuard : IZipcodeFontGuard
{
    /// <summary>Имя шрифта кодового штампа.</summary>
    public const string FontName = "ZIPcode";

    /// <summary>Имя файла шрифта в комплекте поставки.</summary>
    public const string FontFileName = "ZIPcode.ttf";

    private const string RegistryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

    private readonly string? _каталогШрифтов;

    /// <summary>Приватная коллекция шрифтов комплекта поставки.</summary>
    private PrivateFontCollection? _приватныеШрифты;

    /// <summary>Семейство, загруженное из файла комплекта поставки.</summary>
    private FontFamily? _семействоИзФайла;

    /// <summary>
    /// Создаёт контроль шрифта.
    /// </summary>
    /// <param name="fontsDirectory">
    /// Каталог со шрифтами комплекта поставки; при отсутствии используется
    /// каталог файлов программы.
    /// </param>
    public ZipcodeFontGuard(string? fontsDirectory = null)
    {
        _каталогШрифтов = fontsDirectory;
    }

    /// <inheritdoc />
    public bool IsZipcodeFontAvailable
    {
        get
        {
            if (СистемныйШрифтДоступен())
            {
                return true;
            }

            // Файл комплекта поставки — полноценный источник шрифта: приватная
            // коллекция видит его сразу, без установки в системе.
            return ЗагрузитьШрифтИзФайла() is not null;
        }
    }

    /// <inheritdoc />
    public FontFamily? PostIndexFontFamily
    {
        get
        {
            if (СистемныйШрифтДоступен())
            {
                using var коллекция = new InstalledFontCollection();

                foreach (var семейство in коллекция.Families)
                {
                    if (string.Equals(семейство.Name, FontName, StringComparison.OrdinalIgnoreCase))
                    {
                        return семейство;
                    }
                }
            }

            return ЗагрузитьШрифтИзФайла();
        }
    }

    /// <inheritdoc />
    public bool EnsureZipcodeFontInstalled()
    {
        if (IsZipcodeFontAvailable)
        {
            return true;
        }

        var путь = ResolveFontPath();

        if (путь is null)
        {
            return false;
        }

        // Регистрация в сеансе нужна другим процессам; собственный процесс
        // использует приватную коллекцию (см. PostIndexFontFamily).
        var добавлено = AddFontResourceW(путь);

        if (добавлено == 0)
        {
            return false;
        }

        RegisterInUserRegistry(путь);

        return IsZipcodeFontAvailable;
    }

    /// <summary>
    /// Проверяет, установлен ли шрифт в системе (перечисление GDI+).
    /// </summary>
    /// <returns><see langword="true"/>, если гарнитура есть в системе.</returns>
    private static bool СистемныйШрифтДоступен()
    {
        using var коллекция = new InstalledFontCollection();

        foreach (var семейство in коллекция.Families)
        {
            if (string.Equals(семейство.Name, FontName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Загружает шрифт из файла комплекта поставки в приватную коллекцию.
    /// </summary>
    /// <returns>Семейство шрифта либо <see langword="null"/>.</returns>
    private FontFamily? ЗагрузитьШрифтИзФайла()
    {
        if (_семействоИзФайла is not null)
        {
            return _семействоИзФайла;
        }

        var путь = ResolveFontPath();

        if (путь is null)
        {
            return null;
        }

        try
        {
            // Коллекция живёт вместе с гардом: семейство из неё нельзя
            // использовать после освобождения коллекции.
            _приватныеШрифты ??= new PrivateFontCollection();
            _приватныеШрифты.AddFontFile(путь);

            foreach (var семейство in _приватныеШрифты.Families)
            {
                if (string.Equals(семейство.Name, FontName, StringComparison.OrdinalIgnoreCase))
                {
                    _семействоИзФайла = семейство;
                    return _семействоИзФайла;
                }
            }
        }
#pragma warning disable CA1031 // Ошибка загрузки шрифта означает его недоступность.
        catch (Exception)
#pragma warning restore CA1031
        {
            _семействоИзФайла = null;
        }

        return null;
    }

    private string? ResolveFontPath()
    {
        var каталоги = new List<string>();

        if (!string.IsNullOrWhiteSpace(_каталогШрифтов))
        {
            каталоги.Add(_каталогШрифтов);
        }

        каталоги.Add(AppContext.BaseDirectory);
        каталоги.Add(Path.Combine(AppContext.BaseDirectory, "Fonts"));

        foreach (var каталог in каталоги)
        {
            var путь = Path.Combine(каталог, FontFileName);

            if (File.Exists(путь))
            {
                return путь;
            }
        }

        return null;
    }

    private static void RegisterInUserRegistry(string путь)
    {
        try
        {
            using var раздел = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);

            if (раздел is not null)
            {
                раздел.SetValue($"{FontName} (TrueType)", путь);
            }
        }
#pragma warning disable CA1031 // Регистрация в реестре — вспомогательное действие.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Регистрация в реестре — вспомогательное действие: отсутствие
            // записи не отменяет использование шрифта из комплекта поставки.
        }
    }

    /// <summary>
    /// Регистрирует шрифт в текущем сеансе (вызов <c>AddFontResourceW</c>).
    /// </summary>
    /// <param name="path">Путь к файлу шрифта.</param>
    /// <returns>Число добавленных шрифтов.</returns>
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceW(string path);
}
