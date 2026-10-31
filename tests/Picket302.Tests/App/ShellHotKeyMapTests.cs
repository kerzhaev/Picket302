using System.Windows.Forms;
using Picket302.App.UI.Components;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверка системных горячих клавиш главного окна (ТЗ §7.4: «Системные горячие
/// клавиши»).
/// </summary>
public sealed class ShellHotKeyMapTests
{
    [Theory]
    [InlineData(Keys.F1, ShellAction.ОткрытьСправку)]
    [InlineData(Keys.F4, ShellAction.РаскрытьФильтрКатегорий)]
    [InlineData(Keys.F5, ShellAction.ПечатьТекущийКонверт)]
    [InlineData(Keys.F6, ShellAction.ПечатьВыделенныеСПредпросмотром)]
    [InlineData(Keys.F7, ShellAction.ПечатьВыделенныеБезПредпросмотра)]
    [InlineData(Keys.F8, ShellAction.ОткрытьРедакторБланков)]
    [InlineData(Keys.Escape, ShellAction.ОтменитьДействие)]
    [InlineData(Keys.Control | Keys.Space, ShellAction.СвернутьВОбластьУведомлений)]
    [InlineData(Keys.Control | Keys.A, ShellAction.ВыделитьВсеЗаписи)]
    [InlineData(Keys.Control | Keys.Alt | Keys.F8, ShellAction.УдалитьВыделенныеБезвозвратно)]
    public void Найти_СочетаниеИзСпецификации_ВозвращаетДействие(Keys сочетание, ShellAction ожидаемое)
    {
        Assert.Equal(ожидаемое, ShellHotKeyMap.Найти(сочетание));
    }

    [Fact]
    public void Действия_СодержатВсеСочетанияСпецификации()
    {
        Assert.Equal(10, ShellHotKeyMap.Сочетания.Count);
    }

    [Fact]
    public void Найти_НезаданноеСочетание_ВозвращаетNull()
    {
        Assert.Null(ShellHotKeyMap.Найти(Keys.F9));
        Assert.Null(ShellHotKeyMap.Найти(Keys.Control | Keys.C));
    }

    [Fact]
    public void МодификаторыГлобальнойКлавиши_СоответствуютСпецификации()
    {
        Assert.Equal(0x0002, GlobalHotKey.МодификаторCtrl);
        Assert.Equal(0x0001, GlobalHotKey.МодификаторAlt);
        Assert.Equal(0x0004, GlobalHotKey.МодификаторShift);
        Assert.Equal(0x20, GlobalHotKey.ВиртуальнаяКлавишаПробел);
    }

    [Fact]
    public void GlobalHotKey_ПовторнаяРегистрация_НеВыбрасываетИсключение()
    {
        using var клавиша = GlobalHotKey.CtrlSpace(() => { });

        var первая = клавиша.Зарегистрировать();
        var вторая = клавиша.Зарегистрировать();

        Assert.Equal(первая, вторая);
        Assert.Equal(первая, клавиша.Зарегистрирована);
    }
}
