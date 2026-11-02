using Picket302.Printing.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace Picket302.Tests.Printing;

/// <summary>
/// Регрессия F-A2: порядок трансформаций в DrawStringMm. Прежде масштаб
/// применялся раньше переноса, и точка поля умножалась на масштаб — текст
/// рисовался в другом месте листа.
/// </summary>
public sealed class DrawStringTransformTests(ITestOutputHelper вывод)
{
    [Fact]
    public void DrawStringMm_ТекстОстаётсяВНазначенномПоле()
    {
        // Растр 120 × 60 мм при 96 dpi.
        var dpi = 96f;
        var пикселейНаМм = dpi / 25.4f;
        var ширина = (int)(120 * пикселейНаМм);
        var высота = (int)(60 * пикселейНаМм);

        using var растр = new System.Drawing.Bitmap(ширина, высота);

        using (var графика = System.Drawing.Graphics.FromImage(растр))
        {
            графика.Clear(System.Drawing.Color.White);

            var контекст = new GdiGraphicsContext(графика);

            контекст.DrawStringMm("W", "Tahoma", 20f, 5f, 50f, 30f);
        }

        // Ожидаемый прямоугольник поля: x 50..70 мм, y 30..35 мм.
        var лево = (int)MathF.Floor(50f * пикселейНаМм) - 2;
        var право = (int)MathF.Ceiling(70f * пикселейНаМм) + 2;
        var верх = (int)MathF.Floor(30f * пикселейНаМм) - 2;
        var низ = (int)MathF.Ceiling(35f * пикселейНаМм) + 2;

        int минX = int.MaxValue, минY = int.MaxValue, максX = -1, максY = -1, закрашенных = 0;

        for (var y = 0; y < высота; y++)
        {
            for (var x = 0; x < ширина; x++)
            {
                var пиксель = растр.GetPixel(x, y);

                if (пиксель.R < 128)
                {
                    закрашенных++;
                    минX = Math.Min(минX, x);
                    минY = Math.Min(минY, y);
                    максX = Math.Max(максX, x);
                    максY = Math.Max(максY, y);
                }
            }
        }

        вывод.WriteLine($"Закрашенных {закрашенных}, границы [{минX}..{максX}]×[{минY}..{максY}], поле [{лево}..{право}]×[{верх}..{низ}]");

        Assert.True(закрашенных > 0, "Текст должен быть отрисован.");

        Assert.True(минX >= лево, $"Левая граница текста {минX} вне поля ({лево}..).");
        Assert.True(максX <= право, $"Правая граница текста {максX} вне поля (..{право}).");
        Assert.True(минY >= верх, $"Верхняя граница текста {минY} вне поля ({верх}..).");
        Assert.True(максY <= низ, $"Нижняя граница текста {максY} вне поля (..{низ}).");
    }
}
