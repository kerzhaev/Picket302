using System.Drawing.Text;
using System.Reflection;
using Xunit;

namespace Picket302.Tests.Packaging;

/// <summary>
/// Контракт поставки шрифта кодового штампа ZIPcode.
/// </summary>
/// <remarks>
/// <para>
/// Печать конверта с кодовым штампом блокируется, если шрифт ZIPcode не
/// установлен в системе (ТЗ §6.3; §8.6, функция 90). Шрифт входит в
/// поставку: без него конверт напечатать нельзя, а отсутствие файла должно
/// обнаруживаться ДО сборки пакета, а не у владельца на машине.
/// </para>
/// <para>
/// Проверяется, что файл шрифта лежит в репозитории, что его внутреннее имя
/// семейства — именно «ZIPcode» (гард сравнивает имя семейства с этим
/// значением), и что публикация оболочки обязана включать шрифт.
/// </para>
/// </remarks>
public sealed class ZipcodeFontSupplyTests
{
    /// <summary>Путь к файлу шрифта относительно корня репозитория.</summary>
    private const string FontRelativePath = @"src\Picket302.App\Fonts\ZIPcode.ttf";

    /// <summary>Имя семейства шрифта, которое ищет гард печати.</summary>
    private const string ExpectedFamilyName = "ZIPcode";

    /// <summary>
    /// Шрифт лежит в репозитории рядом с исходниками оболочки.
    /// </summary>
    [Fact]
    public void Шрифт_Есть_В_Репозитории()
    {
        var путь = RepositoryRoot.Resolve() + "\\" + FontRelativePath;

        Assert.True(File.Exists(путь), $"Шрифт кодового штампа не найден: {путь}");
        Assert.True(new FileInfo(путь).Length > 0, "Файл шрифта пуст.");
    }

    /// <summary>
    /// Внутреннее имя семейства шрифта — «ZIPcode»: гард печати сравнивает
    /// имя семейства установленных шрифтов с этим значением, и подмена
    /// (например, «PrintAdr ZIPcode») не проходит проверку.
    /// </summary>
    [Fact]
    public void Имя_Семейства_Шрифта_Совпадает_С_Ожидаемым()
    {
        var путь = RepositoryRoot.Resolve() + "\\" + FontRelativePath;

        Assert.True(File.Exists(путь), $"Шрифт кодового штампа не найден: {путь}");

        using var коллекция = new PrivateFontCollection();
        коллекция.AddFontFile(путь);

        var имена = коллекция.Families.Select(семейство => семейство.Name).ToArray();

        Assert.Contains(ExpectedFamilyName, имена);
    }

    /// <summary>
    /// Публикация оболочки обязана включать шрифт: без него собранный пакет
    /// уехал бы к владельцу с блокированной печатью.
    /// </summary>
    [Fact]
    public void Публикация_Объявляет_Шрифт_Обязательным()
    {
        var проект = RepositoryRoot.Resolve() + @"\src\Picket302.App\Picket302.App.csproj";

        Assert.True(File.Exists(проект), $"Файл проекта не найден: {проект}");

        var текст = File.ReadAllText(проект);

        Assert.Contains("Fonts\\ZIPcode.ttf", текст, StringComparison.Ordinal);
    }

    /// <summary>
    /// Файл шрифта копируется в выход сборки и в публикацию рядом с программой.
    /// </summary>
    [Fact]
    public void Файл_Шрифта_Копируется_В_Вывод()
    {
        var проект = RepositoryRoot.Resolve() + @"\src\Picket302.App\Picket302.App.csproj";
        var текст = File.ReadAllText(проект);

        Assert.Contains("Fonts\\ZIPcode.ttf", текст, StringComparison.Ordinal);
        Assert.Contains("CopyToOutputDirectory=\"PreserveNewest\"", текст, StringComparison.Ordinal);
    }
}

/// <summary>
/// Корень репозитория для контрактных проверок поставки.
/// </summary>
public static class RepositoryRoot
{
    /// <summary>
    /// Определяет корень репозитория по расположению файла проекта оболочки.
    /// </summary>
    /// <returns>Полный путь к корню репозитория.</returns>
    public static string Resolve()
    {
        var каталог = Path.GetDirectoryName(
            typeof(ZipcodeFontSupplyTests).Assembly.Location) ?? string.Empty;

        var сборка = new DirectoryInfo(каталог);

        while (сборка is not null)
        {
            if (File.Exists(Path.Combine(сборка.FullName, "Picket302.sln")))
            {
                return сборка.FullName;
            }

            сборка = сборка.Parent;
        }

        throw new InvalidOperationException(
            "Корень репозитория не найден: выше каталога сборки нет Picket302.sln.");
    }
}
