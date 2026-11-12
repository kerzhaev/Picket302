using Picket302.App;
using System.Windows.Forms;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверка каркаса главного окна оператора (ТЗ §7.1 «Главное окно приложения»).
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class MainShellMenuTests
{
    /// <summary>
    /// Верхние пункты меню по ТЗ §7.1, в порядке следования.
    /// Пункт «Данные» добавлен для функций импорта (ТЗ §8.5, функция 72:
    /// «Пункт меню „Данные -&gt; Импорт из 1С“»).
    /// </summary>
    private static readonly string[] ОжидаемыеПунктыМеню =
    {
        "Файл",
        "Правка",
        "Вид",
        "Данные",
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
    public void MainShell_ИспользуетЕдиныйШрифтОболочки()
    {
        using var окно = new MainShell();

        Assert.Equal("Segoe UI", окно.Font.Name);
        Assert.Equal(9f, окно.Font.Size);
    }

    /// <summary>
    /// Пункт «Вид» содержит настройку вида таблиц и настройку таблицы
    /// (ТЗ §8.11, функция 131: «Настройка вида таблиц»; §7.6).
    /// </summary>
    [Fact]
    public void MainShell_ПунктВид_СодержитНастройкиВидаТаблицИТаблицы()
    {
        using var окно = new MainShell();

        var вид = окно.MainMenuStrip!.Items
            .Cast<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Вид");

        var пункты = вид.DropDownItems
            .Cast<ToolStripItem>()
            .Select(пункт => пункт.Text)
            .ToArray();

        Assert.Contains("Настройка вида таблиц…", пункты);
        Assert.Contains("Настройка таблицы…", пункты);
    }

    /// <summary>
    /// S27/S28: меню «Печать» содержит вход в пользовательские отчёты
    /// (ТЗ §8.4, функция 56) и галку дампа промежуточных данных реестров
    /// (ТЗ §8.4, функция 58).
    /// </summary>
    [Fact]
    public void MainShell_ПунктПечать_СодержитПользовательскиеОтчётыИДампДанных()
    {
        using var окно = new MainShell();

        var печать = окно.MainMenuStrip!.Items
            .Cast<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Печать");

        var пункты = печать.DropDownItems
            .Cast<ToolStripItem>()
            .Select(пункт => пункт.Text)
            .ToArray();

        Assert.Contains("Пользовательские отчёты…", пункты);

        var дамп = печать.DropDownItems
            .Cast<ToolStripItem>()
            .OfType<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Сохранять промежуточный XML данных реестров");

        // Опция дампа выключена по умолчанию и включается галкой.
        Assert.False(дамп.Checked);
        Assert.True(дамп.CheckOnClick);
    }
}
