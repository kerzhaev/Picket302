using Picket302.App;
using System.Windows.Forms;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверка каркаса главного окна оператора (ТЗ §7.1 «Главное окно приложения»).
/// </summary>
public sealed class MainShellMenuTests
{
    /// <summary>Верхние пункты меню по ТЗ §7.1, в порядке следования.</summary>
    private static readonly string[] ОжидаемыеПунктыМеню =
    {
        "Файл",
        "Правка",
        "Вид",
        "Сервис",
        "Печать",
        "Рассылки",
        "Справка",
    };

    [Fact]
    public void MainShell_СодержитВсеВерхниеПунктыМенюПоСхеме321()
    {
        using var окно = new MainShell();

        var пункты = окно.MainMenuStrip!.Items
            .Cast<ToolStripItem>()
            .Select(пункт => пункт.Text)
            .ToArray();

        Assert.Equal(ОжидаемыеПунктыМеню, пункты);
    }

    [Fact]
    public void MainShell_ИспользуетПлотныйШрифтОболочки8ptTahoma()
    {
        using var окно = new MainShell();

        Assert.Equal("Tahoma", окно.Font.Name);
        Assert.Equal(8f, окно.Font.Size);
    }
}
