using System.Drawing;
using Picket302.Domain.Entities;
using Picket302.Printing;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Rendering;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Регрессия отрисовки текста в бланке (21.11.2026): строки адреса
/// наезжали друг на друга, а буквы искажались — каждая строка вписывалась в
/// габарит разными коэффициентами по X и Y.
/// </summary>
public sealed class EnvelopeTextLayoutTests
{
    private sealed class ФиксирующийКонтекст : IGraphicsContext
    {
        public List<(string Текст, float Кегль, float X, float Y)> Строки { get; } = new();

        public Color Color { get; set; } = Color.Black;

        public void FillRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawRectangleMm(float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawImageMm(Image image, float xMm, float yMm, float widthMm, float heightMm)
        {
        }

        public void DrawStringMm(string text, string fontName, float widthMm, float heightMm, float xMm, float yMm)
        {
            // Вариант «вписать в габарит» — больше не используется рендерером.
            Строки.Add((text, 0f, xMm, yMm));
        }

        public void DrawStringMm(string text, FontFamily family, float widthMm, float heightMm, float xMm, float yMm)
        {
            Строки.Add((text, 0f, xMm, yMm));
        }

        public void DrawStringMm(string text, string fontName, float sizePt, bool bold, float xMm, float yMm)
        {
            Строки.Add((text, sizePt, xMm, yMm));
        }
    }

    [Fact]
    public void Строки_Адреса_Не_Наезжают_И_Не_Искажаются()
    {
        var измеритель = new GdiTextMeasurer();
        var контекст = new ФиксирующийКонтекст();

        var поле = new BlankField
        {
            Name = "строки «Кому»",
            Kind = BlankFieldKind.Text,
            XMm = 161,
            YMm = 116,
            LengthMm = 40,
            HeightMm = 18,
            MaxChars = 255,
            FontHeightMaxPt = 11,
            FontHeightMinPt = 6,
            IsPrintable = true,
            IsVisible = true,
        };

        var job = new PrintJobContext
        {
            Fields = new[] { поле },
            Items = new[]
            {
                new PrintableItem
                {
                    Id = 1,
                    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["строки «Кому»"] = "ООО «Ромашка», улица Тверская, дом 1, город Москва, индекс 125009",
                    },
                },
            },
        };

        new EnvelopeRenderer(измеритель).Render(контекст, job, job.Items[0]);

        Assert.True(контекст.Строки.Count >= 2, "Ожидалось несколько строк адреса");

        // Кегль задан явно (нет вписывания в габарит) и одинаков для всех строк.
        var кегли = контекст.Строки.Select(с => с.Кегль).Distinct().ToArray();
        Assert.Single(кегли);
        Assert.True(кегли[0] > 0f, "Текст должен рисоваться явным кеглем");

        // Шаг строк не меньше высоты строки — наложения нет.
        var шаг = контекст.Строки[1].Y - контекст.Строки[0].Y;
        var высотаСтроки = измеритель.MeasureLineHeight(поле.FontName, (decimal)кегли[0]);

        Assert.True(шаг >= высотаСтроки - 0.01, $"Шаг строк {шаг:0.##} мм меньше высоты строки {высотаСтроки:0.##} мм");
    }
}
