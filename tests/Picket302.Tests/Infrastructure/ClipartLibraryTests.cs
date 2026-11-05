using Picket302.Infrastructure.Templates;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Локальный каталог клипартов (ТЗ §8.10, функция 158): встроенные элементы
/// вычерчиваются кодом, без сети и внешних файлов; отсутствующий элемент не
/// приводит к падению.
/// </summary>
public sealed class ClipartLibraryTests
{
    /// <summary>Сигнатура PNG-файла.</summary>
    private static readonly byte[] ПодписьPng = [0x89, 0x50, 0x4E, 0x47];

    private static bool НачинаетсяСПодписиPng(byte[] байты)
        => байты.Length >= ПодписьPng.Length
            && байты.Take(ПодписьPng.Length).SequenceEqual(ПодписьPng);

    [Fact]
    public void Каталог_СодержитВосемьИБолееЭлементовСУникальнымиИдентификаторами()
    {
        var каталог = new ClipartLibrary();

        Assert.True(каталог.Items.Count >= 8, $"Элементов в каталоге: {каталог.Items.Count}, требуется не меньше 8.");

        var идентификаторы = каталог.Items.Select(элемент => элемент.Id).ToList();

        Assert.Equal(идентификаторы.Count, идентификаторы.Distinct().Count());

        Assert.All(каталог.Items, элемент =>
        {
            Assert.False(string.IsNullOrWhiteSpace(элемент.Id));
            Assert.False(string.IsNullOrWhiteSpace(элемент.Name));
        });
    }

    [Fact]
    public void TryGet_ИзвестныйЭлемент_ВозвращаетPngСоСтандартнойСигнатурой()
    {
        var каталог = new ClipartLibrary();

        var найден = каталог.TryGet("конверт", out var png);

        Assert.True(найден);
        Assert.True(png.Length > 8, "PNG-растр не может быть пустым.");
        Assert.True(НачинаетсяСПодписиPng(png), "Растр обязан начинаться сигнатурой PNG.");
    }

    [Fact]
    public void TryGet_ОтсутствующийЭлемент_ВозвращаетЛожьБезИсключения()
    {
        var каталог = new ClipartLibrary();

        var найден = каталог.TryGet("несуществующий", out var png);

        Assert.False(найден);
        Assert.Empty(png);

        // Повреждённый вход — null-идентификатор — также отвергается
        // аргументом, а не падением рисования.
        Assert.Throws<ArgumentNullException>(() => каталог.TryGet(null!, out _));
    }

    [Fact]
    public void Каталог_КаждыйЭлементВычерчивается()
    {
        var каталог = new ClipartLibrary();

        Assert.All(каталог.Items, элемент =>
        {
            var найден = каталог.TryGet(элемент.Id, out var png);

            Assert.True(найден, $"Элемент «{элемент.Id}» обязан вычерчиваться.");
            Assert.True(НачинаетсяСПодписиPng(png), $"Растр «{элемент.Id}» обязан быть PNG.");
        });
    }
}
