using Picket302.Domain.Serialization;
using Picket302.Printing.Templates;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Применение пресета «стандартная разметка» к макету бланка и режим
/// «без стандартной разметки» (ТЗ §8.11, функция 145).
/// </summary>
public sealed class StandardLayoutApplierTests
{
    private static CustomBlank Бланк()
    {
        var бланк = new CustomBlank
        {
            Name = "Конверт C6",
            Country = "RUS",
            Geometry = new BlankGeometry { WidthMm = 162m, HeightMm = 114m },
        };

        бланк.Pages.Add(new BlankPage { Number = 1 });

        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = StandardLayoutApplier.ПолеКому,
            Kind = BlankFieldKinds.Text,
            Bounds = new BlankBounds { XMm = 1m, YMm = 1m, WidthMm = 10m, HeightMm = 5m },
        });

        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = StandardLayoutApplier.ПолеКуда,
            Kind = BlankFieldKinds.Text,
            Bounds = new BlankBounds { XMm = 1m, YMm = 1m, WidthMm = 10m, HeightMm = 5m },
        });

        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = StandardLayoutApplier.ПолеОтКого,
            Kind = BlankFieldKinds.Text,
            Bounds = new BlankBounds { XMm = 1m, YMm = 1m, WidthMm = 10m, HeightMm = 5m },
        });

        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = StandardLayoutApplier.ПолеОтКуда,
            Kind = BlankFieldKinds.Text,
            Bounds = new BlankBounds { XMm = 1m, YMm = 1m, WidthMm = 10m, HeightMm = 5m },
        });

        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = "Индекс для машинной обработки",
            Kind = BlankFieldKinds.PostIndex,
            Bounds = new BlankBounds { XMm = 1m, YMm = 1m, WidthMm = 10m, HeightMm = 5m },
        });

        return бланк;
    }

    [Fact]
    public void СтандартнаяРазметка_ПоляАдреса_РазмещеныПоЗонамПресвета()
    {
        var бланк = Бланк();
        var исполнитель = new StandardLayoutApplier();

        var итог = исполнитель.Применить(бланк, "C6", "RUS");

        Assert.True(итог.Применено);
        Assert.True(бланк.StandardLayout);
        Assert.Equal(5, итог.ПеремещеноПолей);

        var пресет = new StandardLayoutPresets().GetPreset("C6", "RUS");

        var кому = бланк.Pages[0].Fields.Single(поле => поле.Name == StandardLayoutApplier.ПолеКому);
        Assert.Equal(пресет.RecipientZone!.XMm, кому.Bounds.XMm);
        Assert.Equal(пресет.RecipientZone.YMm, кому.Bounds.YMm);
        Assert.Equal(пресет.RecipientZone.WidthMm, кому.Bounds.WidthMm);

        var отКого = бланк.Pages[0].Fields.Single(поле => поле.Name == StandardLayoutApplier.ПолеОтКого);
        Assert.Equal(пресет.SenderZone!.XMm, отКого.Bounds.XMm);
        Assert.Equal(пресет.SenderZone.YMm, отКого.Bounds.YMm);

        var штамп = бланк.Pages[0].Fields.Single(поле => поле.Kind == BlankFieldKinds.PostIndex);
        Assert.Equal(пресет.PostIndexZone!.XMm, штамп.Bounds.XMm);
        Assert.Equal(пресет.PostIndexZone.YMm, штамп.Bounds.YMm);
    }

    [Fact]
    public void БезСтандартнойРазметки_ПоложениеПолей_НеМеняется()
    {
        var бланк = Бланк();
        var исходное = бланк.Pages[0].Fields.Single(поле => поле.Name == StandardLayoutApplier.ПолеКому);
        var позиция = исходное.Bounds.XMm;

        var итог = new StandardLayoutApplier().Применить(бланк, "C6", "RUS", StandardLayoutMode.None);

        Assert.True(итог.Применено);
        Assert.False(бланк.StandardLayout);
        Assert.Equal(0, итог.ПеремещеноПолей);
        Assert.Equal(позиция, исходное.Bounds.XMm);
    }

    [Fact]
    public void БланкБезАдресныхПолей_ПресетПрименяется_СПредупреждением()
    {
        var бланк = new CustomBlank { Name = "Пустой макет" };
        бланк.Pages.Add(new BlankPage { Number = 1 });

        var итог = new StandardLayoutApplier().Применить(бланк, "C6", "RUS");

        Assert.True(итог.Применено);
        Assert.True(бланк.StandardLayout);
        Assert.Equal(0, итог.ПеремещеноПолей);
        Assert.NotNull(итог.Предупреждение);
    }

    [Fact]
    public void УкраинскаяСерия_ПресетСтраны_Применяется()
    {
        var бланк = Бланк();

        var итог = new StandardLayoutApplier().Применить(бланк, "DL", "UKR");

        Assert.True(итог.Применено);
        Assert.Equal("UKR", бланк.Country);
    }
}
