using Picket302.Printing.Elements;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Отметки «Заказное»/«С уведомлением» (ТЗ §8.1, функция 26; рамка 10 мм,
/// кегль 12pt, полужирный).
/// </summary>
public sealed class ServiceStampRendererTests
{
    /// <summary>Контекст, записывающий вызовы.</summary>
    private sealed class ЗаписывающийКонтекст : IGraphicsContext
    {
        public List<(string Текст, string Шрифт, float Кегль, bool Полужирный, float X, float Y)> Тексты { get; } = new();

        public List<(float X, float Y, float Ширина, float Высота)> Рамки { get; } = new();

        public System.Drawing.Color Color { get; set; } = System.Drawing.Color.Black;

        public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawStringMm(string text, string fontName, float widthMm, float heightMm, float xMm, float yMm)
        {
        }

        public void DrawStringMm(string text, string fontName, float sizePt, bool bold, float xMm, float yMm) =>
            Тексты.Add((text, fontName, sizePt, bold, xMm, yMm));

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm) =>
            Рамки.Add((xMm, yMm, widthMm, heightMm));

        public void DrawImageMm(System.Drawing.Image image, float xMm, float yMm, float widthMm, float heightMm)
        {
        }
    }

    [Fact]
    public void Render_Заказное_Рамка10МмИ12ПтПолужирный()
    {
        var контекст = new ЗаписывающийКонтекст();
        var отрисовка = new ServiceStampRenderer();

        отрисовка.Render(контекст, ServiceStampKind.Registered, 15f, 20f);

        var рамка = Assert.Single(контекст.Рамки);
        Assert.Equal(15f, рамка.X);
        Assert.Equal(20f, рамка.Y);
        Assert.Equal((float)ServiceStampRenderer.FrameSizeMm, рамка.Ширина);
        Assert.Equal((float)ServiceStampRenderer.FrameSizeMm, рамка.Высота);

        var текст = Assert.Single(контекст.Тексты);
        Assert.Equal(ServiceStampRenderer.RegisteredText, текст.Текст);
        Assert.Equal((float)ServiceStampRenderer.FontSizePt, текст.Кегль);
        Assert.True(текст.Полужирный);
    }

    [Fact]
    public void Render_СУведомлением_ТекстОтметки()
    {
        var контекст = new ЗаписывающийКонтекст();
        new ServiceStampRenderer().Render(контекст, ServiceStampKind.RegisteredWithNotification, 0f, 0f);

        Assert.Equal(ServiceStampRenderer.NotificationText, Assert.Single(контекст.Тексты).Текст);
    }

    [Fact]
    public void Render_Простое_НичегоНеРисуется()
    {
        var контекст = new ЗаписывающийКонтекст();
        new ServiceStampRenderer().Render(контекст, ServiceStampKind.None, 0f, 0f);

        Assert.Empty(контекст.Тексты);
        Assert.Empty(контекст.Рамки);
    }

    [Fact]
    public void Render_ПоКлассификации_Комбинации()
    {
        var контекст = new ЗаписывающийКонтекст();

        new ServiceStampRenderer().Render(контекст, isRegistered: true, needNotification: false, 0f, 0f);
        Assert.Equal(ServiceStampRenderer.RegisteredText, контекст.Тексты[^1].Текст);

        new ServiceStampRenderer().Render(контекст, isRegistered: true, needNotification: true, 0f, 0f);
        Assert.Equal(ServiceStampRenderer.NotificationText, контекст.Тексты[^1].Текст);

        var до = контекст.Тексты.Count;
        new ServiceStampRenderer().Render(контекст, isRegistered: false, needNotification: true, 0f, 0f);
        Assert.Equal(до, контекст.Тексты.Count);
    }

    [Fact]
    public void ResolveText_МатрицаОтметок()
    {
        Assert.Null(ServiceStampRenderer.ResolveText(false, false));
        Assert.Null(ServiceStampRenderer.ResolveText(false, true));
        Assert.Equal(ServiceStampRenderer.RegisteredText, ServiceStampRenderer.ResolveText(true, false));
        Assert.Equal(ServiceStampRenderer.NotificationText, ServiceStampRenderer.ResolveText(true, true));
    }
}
