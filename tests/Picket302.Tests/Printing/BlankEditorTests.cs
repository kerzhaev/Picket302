using Picket302.Domain.Serialization;
using Picket302.Printing.Editor;
using Xunit;

namespace Picket302.Tests.Printing;

/// <summary>
/// Редактор бланков (ТЗ §5.2: AABB-детекция, единицы мм/pt/px, 0.01 мм;
/// §7.5: семантика Visible/Printable, состояния, запрет сохранения).
/// </summary>
public sealed class BlankEditorTests
{
    private static BlankBounds Габарит(decimal x, decimal y, decimal ширина, decimal высота) => new()
    {
        XMm = x,
        YMm = y,
        WidthMm = ширина,
        HeightMm = высота,
    };

    [Fact]
    public void IntersectsAabb_ФормулаПо52()
    {
        // Intersect(A,B)=(A.x<B.x+B.w)∧(A.x+A.w>B.x)∧(A.y<B.y+B.h)∧(A.y+A.h>B.y)
        var a = Габарит(10m, 10m, 30m, 8m);
        var b = Габарит(35m, 14m, 30m, 8m);

        Assert.True(BlankLayoutValidator.IntersectsAabb(a, b));

        var c = Габарит(40.5m, 10m, 30m, 8m);

        Assert.False(BlankLayoutValidator.IntersectsAabb(a, c));

        var касаетсяГраницей = Габарит(40m, 10m, 30m, 8m);

        Assert.False(BlankLayoutValidator.IntersectsAabb(a, касаетсяГраницей));
    }

    [Fact]
    public void Validate_ПересечениеПолей_КонфликтИПредупреждение()
    {
        var бланк = Заготовка();
        var страница = бланк.Pages[0];

        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "str_to1",
            Kind = BlankFieldKinds.Text,
            Bounds = Габарит(10m, 10m, 40m, 6m),
        });

        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "str_to2",
            Kind = BlankFieldKinds.Text,
            Bounds = Габарит(30m, 14m, 40m, 6m),
        });

        var отчёт = new BlankLayoutValidator().Validate(бланк);

        Assert.False(отчёт.SaveBlocked);
        Assert.Single(отчёт.Overlaps);
        Assert.Equal(("str_to1", "str_to2"), отчёт.Overlaps[0]);
        Assert.Contains(отчёт.Messages, сообщение => сообщение.Contains("str_to1") && сообщение.Contains("str_to2"));
    }

    [Fact]
    public void Validate_ВыходЗаПечатнуюОбласть_СохранениеЗапрещено()
    {
        var бланк = Заготовка();
        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = "Кому",
            Kind = BlankFieldKinds.Text,
            Bounds = Габарит(180m, 100m, 40m, 6m), // 220 > 220? x+w = 220 = ширина; сместим за границу
        });

        бланк.Pages[0].Fields[0].Bounds.XMm = 181m; // 181+40 = 221 > 220

        var отчёт = new BlankLayoutValidator().Validate(бланк);

        Assert.True(отчёт.SaveBlocked);
        Assert.Contains(отчёт.Messages, сообщение => сообщение.Contains("Сохранение запрещено"));
    }

    [Fact]
    public void Validate_ТолщинаЛинииВнеГоста_Предупреждение()
    {
        var бланк = Заготовка();
        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = "edtIndeks_to",
            Kind = BlankFieldKinds.PostIndex,
            Bounds = Габарит(120m, 80m, 32m, 6m),
            PostIndex = new BlankPostIndexSpec { LineWidthMm = 0.55m },
        });

        var отчёт = new BlankLayoutValidator().Validate(бланк);

        Assert.False(отчёт.SaveBlocked);
        Assert.Contains(отчёт.Messages, сообщение => сообщение.Contains("0,2–0,5"));
    }

    [Fact]
    public void Validate_ШрифтZipcodeОтсутствует_Блокировка()
    {
        var бланк = Заготовка();
        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = "edtIndeks_to",
            Kind = BlankFieldKinds.PostIndex,
            Bounds = Габарит(120m, 80m, 32m, 6m),
            PostIndex = new BlankPostIndexSpec(),
        });

        var отчёт = new BlankLayoutValidator(zipcodeFontAvailable: () => false).Validate(бланк);

        Assert.True(отчёт.SaveBlocked);
        Assert.Contains(отчёт.Messages, сообщение => сообщение.Contains("ZIPcode"));
    }

    [Fact]
    public void EditorUnits_ПересчетМмПтПиксели()
    {
        Assert.Equal(25.4m, EditorUnits.PointsToMillimeters(EditorUnits.MillimetersToPoints(25.4m)));

        // 1 pt = 25,4/72 мм.
        Assert.Equal(72m, Math.Round(EditorUnits.MillimetersToPoints(25.4m), 6));

        // 96 DPI: 25,4 мм = 96 px.
        Assert.Equal(96m, EditorUnits.MillimetersToPixels(25.4m, 96));
        Assert.Equal(120m, EditorUnits.MillimetersToPixels(25.4m, 120));

        Assert.Equal(25.4m, EditorUnits.PixelsToMillimeters(96m, 96));
        Assert.Equal(25.4m, EditorUnits.PixelsToMillimeters(120m, 120));

        Assert.Contains("pt", EditorUnits.Format(25.4m, EditorUnit.Points));
        Assert.Contains("px", EditorUnits.Format(25.4m, EditorUnit.Pixels, 96));
        Assert.Contains("мм", EditorUnits.Format(25.4m, EditorUnit.Millimeters));

        Assert.Equal(31.5m, EditorUnits.Snap(31.53m, 0.1m));
    }

    [Fact]
    public void CanvasController_SemantikaVisible_ИсключенВезде()
    {
        var бланк = Заготовка();
        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = "str_to1",
            Kind = BlankFieldKinds.Text,
            Bounds = Габарит(10m, 10m, 40m, 6m),
            Visible = false,
            Printable = true,
        });

        var контроллер = new CanvasController();
        контроллер.Open(бланк);

        // Visible=false — элемент исключён из рендеринга везде.
        Assert.Empty(контроллер.GetCanvasFields(1));
        Assert.Empty(контроллер.GetPrintableFields(1));
        Assert.Null(контроллер.HitTest(1, 12m, 12m));
    }

    [Fact]
    public void CanvasController_SemantikaPrintable_НаЭкранеБезПечати()
    {
        var бланк = Заготовка();
        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = "Разлиновка",
            Kind = BlankFieldKinds.Zone,
            Bounds = Габарит(10m, 10m, 40m, 6m),
            Visible = true,
            Printable = false,
        });

        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = "str_to1",
            Kind = BlankFieldKinds.Text,
            Bounds = Габарит(10m, 40m, 40m, 6m),
        });

        var контроллер = new CanvasController();
        контроллер.Open(бланк);

        // Printable=false — элемент на экране, но отсекается при печати.
        Assert.Equal(2, контроллер.GetCanvasFields(1).Count);
        Assert.Single(контроллер.GetPrintableFields(1));
        Assert.Equal("str_to1", контроллер.GetPrintableFields(1)[0].Name);

        // Зона с Printable=false кликабельна на холсте.
        Assert.Equal("Разлиновка", контроллер.HitTest(1, 12m, 12m));
    }

    [Fact]
    public void CanvasController_Перемещение_СнапИПерепроверка()
    {
        var бланк = Заготовка();
        бланк.Pages[0].Fields.Add(new BlankFieldSpec
        {
            Name = "Кому",
            Kind = BlankFieldKinds.Text,
            Bounds = Габарит(10m, 10m, 40m, 6m),
        });

        var контроллер = new CanvasController();
        контроллер.Open(бланк);
        контроллер.Select("Кому");
        контроллер.SnapToGrid = false;

        Assert.True(контроллер.MoveField("Кому", 12.34m, 8.0m));

        var поле = контроллер.GetSelected()!;

        // Координаты движка — сотые доли миллиметра.
        Assert.Equal(12.34m, поле.Bounds.XMm);
        Assert.Equal(8.0m, поле.Bounds.YMm);
        Assert.Equal(EditorState.Ready, контроллер.State);
        Assert.True(контроллер.CanSave());

        // Перемещение за границы печатной области запрещает сохранение.
        контроллер.MoveField("Кому", 205m, 10m);

        Assert.Equal(EditorState.SaveForbidden, контроллер.State);
        Assert.False(контроллер.CanSave());
    }

    [Fact]
    public void CanvasController_Палитра_ПятьИнструментов()
    {
        Assert.Equal(5, Enum.GetValues<BlankTool>().Length);
        Assert.Contains(BlankTool.Text, Enum.GetValues<BlankTool>());
        Assert.Contains(BlankTool.PostIndex, Enum.GetValues<BlankTool>());
        Assert.Contains(BlankTool.Picture, Enum.GetValues<BlankTool>());
        Assert.Contains(BlankTool.QrCode, Enum.GetValues<BlankTool>());
        Assert.Contains(BlankTool.Zone, Enum.GetValues<BlankTool>());
    }

    [Fact]
    public void CanvasController_ДобавлениеПоля_КодовыйШтампПолучаетСпецификацию()
    {
        var бланк = Заготовка();
        var контроллер = new CanvasController();
        контроллер.Open(бланк);
        контроллер.Tool = BlankTool.PostIndex;

        var поле = контроллер.AddField(1, 120m, 90m, "Индекс_новый");

        Assert.NotNull(поле);
        Assert.Equal(BlankFieldKinds.PostIndex, поле!.Kind);
        Assert.NotNull(поле.PostIndex);
        Assert.Equal(0.45m, поле.PostIndex!.LineWidthMm);
    }

    private static CustomBlank Заготовка()
    {
        var бланк = new CustomBlank
        {
            Name = "Тестовый бланк",
            Geometry = new BlankGeometry
            {
                WidthMm = 220m,
                HeightMm = 110m,
                Orientation = BlankOrientation.Landscape,
            },
        };
        бланк.Pages.Add(new BlankPage { Number = 1 });

        return бланк;
    }
}
