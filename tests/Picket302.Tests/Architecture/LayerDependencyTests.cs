using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Picket302.Tests.Architecture;

/// <summary>
/// Контроль допустимых зависимостей между слоями решения (ТЗ §2.1, §2.2).
/// </summary>
/// <remarks>
/// Проверяются два вида связей: фактические ссылки сборок
/// (<c>Assembly.GetReferencedAssemblies</c>) и объявленные ссылки проектов
/// (<c>ProjectReference</c> в файлах <c>.csproj</c>). Фактические ссылки
/// образуются компилятором только для реально используемых типов, поэтому
/// объявленная, но не используемая ссылка в них не видна — без проверки
/// <c>.csproj</c> отклонение от матрицы §2.1 осталось бы незамеченным.
/// </remarks>
public sealed class LayerDependencyTests
{
    /// <summary>
    /// Имя файла решения — признак корня репозитория.
    /// </summary>
    private const string ИмяФайлаРешения = "Picket302.sln";

    /// <summary>
    /// Допустимые зависимости: имя сборки слоя — список сборок, на которые слой вправе ссылаться.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ДопустимыеЗависимости =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Picket302.Domain"] = Array.Empty<string>(),
            ["Picket302.Printing"] = new[] { "Picket302.Domain" },
            ["Picket302.Application"] = new[] { "Picket302.Domain", "Picket302.Printing" },
            ["Picket302.Infrastructure"] = new[] { "Picket302.Domain", "Picket302.Printing" },
            ["Picket302.App"] = new[]
            {
                "Picket302.Application",
                "Picket302.Domain",
                "Picket302.Infrastructure",
                "Picket302.Printing",
            },
        };

    [Fact]
    public void ТестовыйПроект_СсылаетсяНаВсеПроектыРешения()
    {
        foreach (var имяСборки in ДопустимыеЗависимости.Keys)
        {
            var путь = Path.Combine(AppContext.BaseDirectory, имяСборки + ".dll");

            Assert.True(File.Exists(путь), $"Сборка {имяСборки} отсутствует рядом с тестами.");
        }
    }

    [Fact]
    public void Слои_НеСсылаютсяНаСборкиВнеДопустимыхЗависимостей()
    {
        foreach (var (имяСборки, разрешенные) in ДопустимыеЗависимости)
        {
            var сборка = ЗагрузитьСборку(имяСборки);

            var запрещенные = сборка.GetReferencedAssemblies()
                .Select(ссылка => ссылка.Name!)
                .Where(имя => имя.StartsWith("Picket302.", StringComparison.Ordinal))
                .Where(имя => !разрешенные.Contains(имя, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(имя => имя, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                запрещенные.Length == 0,
                $"{имяСборки} ссылается на сборки вне допустимых зависимостей: {string.Join(", ", запрещенные)}.");
        }
    }

    [Fact]
    public void ДоменныйСлой_НеЗависитОтДругихСлоев()
    {
        var домен = ЗагрузитьСборку("Picket302.Domain");

        var ссылки = домен.GetReferencedAssemblies()
            .Select(ссылка => ссылка.Name!)
            .Where(имя => имя.StartsWith("Picket302.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(ссылки);
    }

    /// <summary>
    /// Объявленные в файлах проектов ссылки соответствуют матрице
    /// допустимых зависимостей: ссылка, объявленная, но не используемая
    /// кодом, тоже образует зависимость слоя.
    /// </summary>
    [Fact]
    public void Слои_ОбъявленныеВПроектахСсылкиСоответствуютМатрице()
    {
        var корень = НайтиКореньРешения();
        var каталогПроектов = Path.Combine(корень, "src");

        foreach (var (имяСборки, разрешенные) in ДопустимыеЗависимости)
        {
            var файлПроекта = НайтиФайлПроекта(каталогПроектов, имяСборки);

            Assert.NotNull(файлПроекта);

            var объявленные = ПрочитатьОбъявленныеСсылки(файлПроекта!);

            var запрещенные = объявленные
                .Where(имя => !разрешенные.Contains(имя, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(имя => имя, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                запрещенные.Length == 0,
                $"{имяСборки} объявляет запрещённые ссылки на проекты: {string.Join(", ", запрещенные)}.");
        }
    }

    /// <summary>
    /// Каждый слой объявляет в файле проекта только те ссылки, на которые
    /// вправе ссылаться; лишних объявлений нет.
    /// </summary>
    [Fact]
    public void Слои_ОбъявленныеСсылкиНеПустыДляЗависящихСлоев()
    {
        var корень = НайтиКореньРешения();
        var каталогПроектов = Path.Combine(корень, "src");

        foreach (var (имяСборки, разрешенные) in ДопустимыеЗависимости)
        {
            var файлПроекта = НайтиФайлПроекта(каталогПроектов, имяСборки);
            Assert.NotNull(файлПроекта);

            var объявленные = ПрочитатьОбъявленныеСсылки(файлПроекта!);

            // Доменный слой — корень: он не объявляет ссылок на другие слои.
            if (разрешенные.Length == 0)
            {
                Assert.Empty(объявленные);
                continue;
            }

            // Объявленные ссылки не выходят за пределы разрешённых.
            Assert.All(объявленные, имя =>
                Assert.Contains(имя, разрешенные, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Находит корень репозитория по файлу решения.
    /// </summary>
    /// <returns>Полный путь к корню репозитория.</returns>
    private static string НайтиКореньРешения()
    {
        var каталог = new DirectoryInfo(AppContext.BaseDirectory);

        while (каталог is not null)
        {
            if (File.Exists(Path.Combine(каталог.FullName, ИмяФайлаРешения)))
            {
                return каталог.FullName;
            }

            каталог = каталог.Parent;
        }

        throw new InvalidOperationException(
            $"Файл решения {ИмяФайлаРешения} не найден выше каталога {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Находит файл проекта слоя по имени сборки.
    /// </summary>
    /// <param name="каталогПроектов">Каталог с проектами решения.</param>
    /// <param name="имяСборки">Имя сборки (имени проекта) слоя.</param>
    /// <returns>Полный путь к файлу проекта либо <see langword="null"/>.</returns>
    private static string? НайтиФайлПроекта(string каталогПроектов, string имяСборки)
    {
        var имяФайла = имяСборки + ".csproj";

        return Directory.Exists(каталогПроектов)
            ? Directory.EnumerateFiles(каталогПроектов, имяФайла, SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    /// <summary>
    /// Читает имена проектов, на которые ссылается проект через
    /// <c>ProjectReference</c>.
    /// </summary>
    /// <param name="файлПроекта">Путь к файлу проекта.</param>
    /// <returns>Имена объявленных проектов.</returns>
    private static IReadOnlyList<string> ПрочитатьОбъявленныеСсылки(string файлПроекта)
    {
        var документ = XDocument.Load(файлПроекта);
        var имена = new List<string>();

        foreach (var элемент in документ.Descendants().Where(э => э.Name.LocalName == "ProjectReference"))
        {
            var включение = элемент.Attribute("Include")?.Value;

            if (string.IsNullOrWhiteSpace(включение))
            {
                включение = элемент.Elements()
                    .FirstOrDefault(э => э.Name.LocalName == "Project")?.Value;
            }

            if (string.IsNullOrWhiteSpace(включение))
            {
                continue;
            }

            var нормализованное = включение.Replace('\\', Path.DirectorySeparatorChar);

            имена.Add(Path.GetFileNameWithoutExtension(нормализованное));
        }

        return имена;
    }

    private static Assembly ЗагрузитьСборку(string имяСборки)
    {
        var путь = Path.Combine(AppContext.BaseDirectory, имяСборки + ".dll");
        Assert.True(File.Exists(путь), $"Сборка {имяСборки} отсутствует рядом с тестами.");

        return Assembly.LoadFrom(путь);
    }
}
