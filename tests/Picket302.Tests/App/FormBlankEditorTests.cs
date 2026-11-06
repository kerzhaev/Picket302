using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Picket302.Domain.Serialization;
using Picket302.Printing.Editor;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Встроенный редактор бланков — форма (ТЗ §7.5, §5.2; §8.3, функция 38).
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormBlankEditorTests
{
    private static CustomBlank Заготовка()
    {
        var бланк = new CustomBlank
        {
            Name = "Бланк для редактора",
            Geometry = new BlankGeometry { WidthMm = 220m, HeightMm = 110m, Orientation = BlankOrientation.Landscape },
        };

        var страница = new BlankPage { Number = 1 };

        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "str_to1",
            Kind = BlankFieldKinds.Text,
            Bounds = new BlankBounds { XMm = 10m, YMm = 10m, WidthMm = 40m, HeightMm = 6m },
        });

        страница.Fields.Add(new BlankFieldSpec
        {
            Name = "edtIndeks_to",
            Kind = BlankFieldKinds.PostIndex,
            Bounds = new BlankBounds { XMm = 160m, YMm = 90m, WidthMm = 31m, HeightMm = 6m },
            PostIndex = new BlankPostIndexSpec(),
        });

        бланк.Pages.Add(страница);

        return бланк;
    }

    [Fact]
    public void Редактор_ОткрываетМакет_ГотовоеСостояние()
    {
        using var редактор = new FormBlankEditor(Заготовка());

        Assert.Equal(EditorState.Ready, редактор.Controller.State);
        Assert.NotNull(редактор.Blank);
        Assert.Equal("Бланк для редактора", редактор.Blank!.Name);
    }

    [Fact]
    public void Редактор_БезМакета_СерыйХолст()
    {
        using var редактор = new FormBlankEditor();

        Assert.Equal(EditorState.NoLayout, редактор.Controller.State);
        Assert.Null(редактор.Blank);
        Assert.Null(редактор.TrySave());
    }

    [Fact]
    public void Редактор_Палитра_ПятьОбъектов()
    {
        using var редактор = new FormBlankEditor(Заготовка());

        Assert.Equal(5, редактор.Controls.OfType<ListBox>().First().Items.Count);
    }

    [Fact]
    public void Редактор_Инспектор_ОтражаетВыделенное()
    {
        using var редактор = new FormBlankEditor(Заготовка());

        редактор.Select("str_to1");

        Assert.NotNull(редактор.Controller.GetSelected());
        Assert.Equal("str_to1", редактор.Controller.GetSelected()!.Name);

        редактор.Select(null);

        Assert.Null(редактор.Controller.GetSelected());
    }

    [Fact]
    public void Редактор_События_OnBeforePrint()
    {
        using var редактор = new FormBlankEditor(Заготовка());

        редактор.Select("edtIndeks_to");
        редактор.ApplyScript("ZipCode_to.text := <edtIndeks_to>;");

        Assert.Equal("ZipCode_to.text := <edtIndeks_to>;", редактор.Controller.GetSelected()!.OnBeforePrint);
    }

    [Fact]
    public void Редактор_Сохранение_Разрешено_ВГраницах()
    {
        using var редактор = new FormBlankEditor(Заготовка());

        var xml = редактор.TrySave();

        Assert.NotNull(xml);
        Assert.Contains("str_to1", xml);
        Assert.Contains("edtIndeks_to", xml);
    }

    [Fact]
    public void Редактор_Сохранение_Запрещено_ЗаГраницами()
    {
        using var редактор = new FormBlankEditor(Заготовка());

        // Перемещение поля за границу печатной области: состояние
        // «Сохранение запрещено» (ТЗ §7.5).
        редактор.Controller.MoveField("str_to1", 215m, 105m);

        Assert.Equal(EditorState.SaveForbidden, редактор.Controller.State);
        Assert.False(редактор.Controller.CanSave());

        Assert.Null(редактор.TrySave());
    }

    [Fact]
    public void Редактор_Переключениеединиц_МмПтПиксели()
    {
        using var редактор = new FormBlankEditor(Заготовка());

        редактор.SetUnit(EditorUnit.Points);
        Assert.Equal(EditorUnit.Points, редактор.Controller.Unit);

        редактор.SetUnit(EditorUnit.Pixels, 120);
        Assert.Equal(EditorUnit.Pixels, редактор.Controller.Unit);
        Assert.Equal(120, редактор.Controller.ScreenDpi);
    }
}
