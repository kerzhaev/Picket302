using Picket302.Domain.Serialization;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// S26 (функция 158, остаток): клипарт в произвольном макете blank.xml —
/// round-trip сериализации атрибута clipartId у picture-полей и
/// совместимость со старыми макетами (атрибут отсутствует → null).
/// </summary>
public sealed class ClipartInBlankXmlRoundTripTests
{
    private static CustomBlank МакетСКлипартом()
    {
        var бланк = new CustomBlank
        {
            Name = "Макет с клипартом",
            Geometry = new BlankGeometry
            {
                WidthMm = 162m,
                HeightMm = 114m,
                Orientation = BlankOrientation.Landscape,
            },
        };

        var страница = new BlankPage { Number = 1 };
        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "Клипарт-конверт",
            Kind = BlankFieldKinds.Picture,
            ClipartId = "конверт",
            Bounds = new BlankBounds
            {
                XMm = 10m,
                YMm = 10m,
                WidthMm = 30m,
                HeightMm = 25m,
            },
            Printable = true,
        });
        бланк.Pages.Add(страница);
        return бланк;
    }

    [Fact]
    public void RoundTrip_СохраняетClipartId()
    {
        var бланк = МакетСКлипартом();

        var xml = new BlankXmlSerializer().SerializeBlank(бланк);
        Assert.Contains("clipartId=\"конверт\"", xml);

        var восстановленный = new BlankXmlSerializer().Deserialize(xml);
        var поле = Assert.Single(восстановленный.AllFields());
        Assert.Equal(BlankFieldKinds.Picture, поле.Kind);
        Assert.Equal("конверт", поле.ClipartId);
    }

    [Fact]
    public void СтарыйМакетБезКлипарта_ЧитаетсяБезClipartId()
    {
        var сериализатор = new BlankXmlSerializer();
        var макет = new CustomBlank
        {
            Geometry = new BlankGeometry
            {
                WidthMm = 162m,
                HeightMm = 114m,
                Orientation = BlankOrientation.Landscape,
            },
        };
        var страница = new BlankPage { Number = 1 };
        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "Старое поле",
            Kind = BlankFieldKinds.Picture,
            Bounds = new BlankBounds { XMm = 5m, YMm = 5m, WidthMm = 30m, HeightMm = 25m },
        });
        макет.Pages.Add(страница);

        var xml = сериализатор.SerializeBlank(макет);
        Assert.DoesNotContain("clipartId", xml, StringComparison.Ordinal);

        var бланк = сериализатор.Deserialize(xml);
        var поле = Assert.Single(бланк.AllFields());
        Assert.Equal(BlankFieldKinds.Picture, поле.Kind);
        Assert.Null(поле.ClipartId);
    }

    [Fact]
    public void RoundTrip_ПустойClipartId_НеПишетАтрибут()
    {
        var бланк = new CustomBlank
        {
            Geometry = new BlankGeometry { WidthMm = 162m, HeightMm = 114m },
        };
        var страница = new BlankPage { Number = 1 };
        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "Клипарт без выбора",
            Kind = BlankFieldKinds.Picture,
            Bounds = new BlankBounds { XMm = 0m, YMm = 0m, WidthMm = 10m, HeightMm = 10m },
        });
        бланк.Pages.Add(страница);

        var xml = new BlankXmlSerializer().SerializeBlank(бланк);

        Assert.DoesNotContain("clipartId", xml, StringComparison.Ordinal);
    }
}
