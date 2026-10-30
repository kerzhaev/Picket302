using System.Reflection;
using Xunit;

namespace Picket302.Tests.Architecture;

/// <summary>
/// Контроль допустимых зависимостей между слоями решения (ТЗ §2.1, §2.2).
/// </summary>
public sealed class LayerDependencyTests
{
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

    private static Assembly ЗагрузитьСборку(string имяСборки)
    {
        var путь = Path.Combine(AppContext.BaseDirectory, имяСборки + ".dll");
        Assert.True(File.Exists(путь), $"Сборка {имяСборки} отсутствует рядом с тестами.");

        return Assembly.LoadFrom(путь);
    }
}
