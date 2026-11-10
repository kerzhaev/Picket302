using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Picket302.Domain.Contracts;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Регрессии компоновки диалога «Настройка вида таблиц» (дефект V-01
/// stage5-verify): метки «Кегль шрифта, pt:» и «Высота строки, px:» должны
/// лежать на одной горизонтальной линии со своими полями NumericUpDown.
/// Исторически (снимки 2026-11-03 и 2026-11-10) метка высоты строки
/// оказывалась на строку ниже своего поля.
/// </summary>
public sealed class FormGridAppearanceLayoutTests
{
    private static (Label метка, NumericUpDown поле) НайтиПару(Form форма, string текстМетки, decimal максимумПоля)
    {
        var метка = форма.Controls
            .OfType<Control>()
            .SelectMany(Развернуть)
            .OfType<Label>()
            .FirstOrDefault(элемент => элемент.Text == текстМетки);

        Assert.NotNull(метка);

        var поле = форма.Controls
            .OfType<Control>()
            .SelectMany(Развернуть)
            .OfType<NumericUpDown>()
            .FirstOrDefault(элемент => элемент.Maximum == максимумПоля);

        Assert.NotNull(поле);

        return (метка, поле);
    }

    private static IEnumerable<Control> Развернуть(Control корень)
    {
        foreach (Control потомок in корень.Controls)
        {
            yield return потомок;

            foreach (var глубокий in Развернуть(потомок))
            {
                yield return глубокий;
            }
        }
    }

    [Theory]
    [InlineData("Кегль шрифта, pt:", 14)]
    [InlineData("Высота строки, px:", 48)]
    public void МеткаИПоле_ВыровненыПоГоризонтали(string текстМетки, decimal максимумПоля)
    {
        using var форма = new FormGridAppearance(new GridAppearance());

        var (метка, поле) = НайтиПару(форма, текстМетки, максимумПоля);

        // Приводим координаты к общему родителю: метка и поле могут лежать
        // в разных контейнерах вкладки.
        var центрМетки = метка.Parent!.PointToScreen(
            new Point(метка.Left, метка.Top + метка.Height / 2));
        var центрПоля = поле.Parent!.PointToScreen(
            new Point(поле.Left, поле.Top + поле.Height / 2));

        var расхождение = Math.Abs(центрМетки.Y - центрПоля.Y);

        Assert.True(
            расхождение <= 4,
            $"Метка «{текстМетки}» и её поле расходятся по вертикали на {расхождение}px " +
            $"(метка Y={центрМетки.Y}, поле Y={центрПоля.Y}).");
    }

    [Theory]
    [InlineData("Кегль шрифта, pt:", 14)]
    [InlineData("Высота строки, px:", 48)]
    public void Поле_СправаОтМетки_ВПределахКлиентскойОбласти(string текстМетки, decimal максимумПоля)
    {
        using var форма = new FormGridAppearance(new GridAppearance());

        var (метка, поле) = НайтиПару(форма, текстМетки, максимумПоля);

        var левоМетки = метка.Parent!.PointToScreen(new Point(метка.Left, 0)).X;
        var левоПоля = поле.Parent!.PointToScreen(new Point(поле.Left, 0)).X;
        var правоПоляЭкран = поле.Parent!.PointToScreen(new Point(поле.Right, поле.Bottom));

        Assert.True(левоПоля > левоМетки, "Поле должно стоять правее своей метки.");
        Assert.True(
            правоПоляЭкран.Y <= форма.PointToScreen(new Point(0, форма.ClientSize.Height)).Y,
            "Поле не должно выходить за нижнюю границу клиентской области формы.");
    }
}
