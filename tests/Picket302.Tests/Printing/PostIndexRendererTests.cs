using System.Drawing;
using Picket302.Printing.Elements;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Проверка параметров машиночитаемого индекса (ТЗ §6.3; §8.6, функция 90:
/// <c>Printing.Elements.PostIndexRenderer</c>).
/// </summary>
public sealed class PostIndexRendererTests
{
    /// <summary>Графический контекст, записывающий обращения к нему.</summary>
    private sealed class ЗаписывающийКонтекст : IGraphicsContext
    {
        public List<(string Текст, string Шрифт, float Ширина, float Высота, float X, float Y)> Вызовы { get; } = new();

        public Color Color { get; set; } = Color.Black;

        public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawStringMm(
            string text,
            string fontName,
            float widthMm,
            float heightMm,
            float xMm,
            float yMm)
        {
            Вызовы.Add((text, fontName, widthMm, heightMm, xMm, yMm));
        }

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm)
        {
        }
    }

    private sealed class ШрифтЕсть : IZipcodeFontGuard
    {
        public bool EnsureZipcodeFontInstalled() => true;

        public bool IsZipcodeFontAvailable => true;
    }

    private sealed class ШрифтаНет : IZipcodeFontGuard
    {
        public bool EnsureZipcodeFontInstalled() => false;

        public bool IsZipcodeFontAvailable => false;
    }

    [Fact]
    public void Validate_ТолщинаВНорме_БезПредупреждений()
    {
        var результат = new PostIndexRenderer(new ШрифтЕсть()).Validate(new PostIndexSpec());

        Assert.False(результат.IsBlocked);
        Assert.Empty(результат.Warnings);
    }

    [Fact]
    public void Validate_ТолщинаБольше0_5_БлокируетПечать()
    {
        var спецификация = new PostIndexSpec { LineWidthMm = 0.55m };

        var результат = new PostIndexRenderer(new ШрифтЕсть()).Validate(спецификация);

        Assert.True(результат.IsBlocked);
        Assert.Contains(результат.Warnings, текст => текст.Contains("0.2-0.5мм"));
    }

    [Fact]
    public void Validate_ТолщинаМеньше0_2_ПредупреждениеБезБлокировки()
    {
        var спецификация = new PostIndexSpec { LineWidthMm = 0.15m };

        var результат = new PostIndexRenderer(new ШрифтЕсть()).Validate(спецификация);

        Assert.False(результат.IsBlocked);
        Assert.Contains(результат.Warnings, текст => текст.Contains("0.2-0.5мм"));
    }

    [Fact]
    public void Validate_ШрифтОтсутствует_БлокируетПечать()
    {
        var результат = new PostIndexRenderer(new ШрифтаНет()).Validate(new PostIndexSpec());

        Assert.True(результат.IsBlocked);
        Assert.Contains(
            результат.Warnings,
            текст => текст.Contains("ZIPcode") && текст.Contains("переустанов"));
    }

    [Fact]
    public void RenderMachineReadableIndex_ШестизначныйИндекс_РисуетШестьЗнаков()
    {
        var контекст = new ЗаписывающийКонтекст();

        new PostIndexRenderer(new ШрифтЕсть())
            .RenderMachineReadableIndex(контекст, "101000", new PostIndexSpec());

        Assert.Equal(6, контекст.Вызовы.Count);
        Assert.All(контекст.Вызовы, вызов =>
        {
            Assert.Equal("ZIPcode", вызов.Шрифт);
            Assert.Equal(3.7f, вызов.Ширина, 3);
            Assert.Equal(5.5f, вызов.Высота, 3);
        });
    }

    [Fact]
    public void RenderMachineReadableIndex_ШагЗнаков_5_4Мм()
    {
        var контекст = new ЗаписывающийКонтекст();

        new PostIndexRenderer(new ШрифтЕсть())
            .RenderMachineReadableIndex(контекст, "101000", new PostIndexSpec());

        for (var индекс = 1; индекс < контекст.Вызовы.Count; индекс++)
        {
            Assert.Equal(5.4f, контекст.Вызовы[индекс].X - контекст.Вызовы[индекс - 1].X, 3);
        }
    }

    [Fact]
    public void RenderMachineReadableIndex_Международный_ТриЗнакаИндекс555()
    {
        var контекст = new ЗаписывающийКонтекст();
        var спецификация = new PostIndexSpec { DigitCount = 3, InternationalIndex = "555" };

        new PostIndexRenderer(new ШрифтЕсть())
            .RenderMachineReadableIndex(контекст, "101000", спецификация);

        Assert.Equal(3, контекст.Вызовы.Count);
        Assert.Equal("555", string.Concat(контекст.Вызовы.Select(вызов => вызов.Текст)));
    }

    [Fact]
    public void RenderMachineReadableIndex_ШрифтОтсутствует_БлокируетОтрисовку()
    {
        var контекст = new ЗаписывающийКонтекст();

        Assert.Throws<PostIndexBlockedException>(
            () => new PostIndexRenderer(new ШрифтаНет())
                .RenderMachineReadableIndex(контекст, "101000", new PostIndexSpec()));

        Assert.Empty(контекст.Вызовы);
    }

    [Fact]
    public void RenderMachineReadableIndex_НехватаетШирины_ПропорциональноУменьшаетШтамп()
    {
        var контекст = new ЗаписывающийКонтекст();
        var спецификация = new PostIndexSpec { AvailableWidthMm = 16.2m };

        new PostIndexRenderer(new ШрифтЕсть())
            .RenderMachineReadableIndex(контекст, "101000", спецификация);

        Assert.Equal(6, контекст.Вызовы.Count);
        Assert.Equal(3.7f * 0.5f, контекст.Вызовы[0].Ширина, 2);
        Assert.Equal(5.5f * 0.5f, контекст.Вызовы[0].Высота, 2);
    }
}
