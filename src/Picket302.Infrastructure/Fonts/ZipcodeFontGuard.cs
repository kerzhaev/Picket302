using System.Drawing.Text;
using System.Runtime.InteropServices;
using Picket302.Printing.Elements;

namespace Picket302.Infrastructure.Fonts;

/// <summary>
/// Контроль наличия шрифта ZIPcode (ТЗ §6.3; §8.6, функция 90:
/// <c>Infrastructure.Fonts.ZipcodeFontGuard</c>).
/// </summary>
/// <remarks>
/// Шрифт ZIPcode поставляется с программой и регистрируется в текущем сеансе
/// вызовом <c>AddFontResourceW</c> и записью в раздел
/// <c>HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts</c>. Скрытая
/// подмена на Arial/Times не допускается: при отсутствии шрифта печать
/// конверта блокируется.
/// </remarks>
public sealed class ZipcodeFontGuard : IZipcodeFontGuard
{
    /// <summary>Имя шрифта кодового штампа.</summary>
    public const string FontName = "ZIPcode";

    /// <summary>Имя файла шрифта в комплекте поставки.</summary>
    public const string FontFileName = "ZIPcode.ttf";

    private const string RegistryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

    private readonly string? _каталогШрифтов;

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

        var добавлено = AddFontResourceW(путь);

        if (добавлено == 0)
        {
            return false;
        }

        RegisterInUserRegistry(путь);

        return IsZipcodeFontAvailable;
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
            // записи не отменяет регистрацию шрифта в сеансе.
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
