using System.Drawing;
using System.Windows.Forms;
using Picket302.App;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Регрессионные проверки исправлений UX-аудита от 2026-11-03
/// (docs/UX-AUDIT-2026-11-03.md: U1, U2, U4, C1, C2, E1, F1).
/// </summary>
public sealed class MainShellUxFixesTests
{
    /// <summary>
    /// U1: главное меню обязано присутствовать в контролах формы и
    /// прилегать сверху — прежде оно строилось и регистрировалось
    /// через MainMenuStrip, но в Controls не добавлялось и было невидимо.
    /// </summary>
    [Fact]
    public void MainShell_ГлавноеМеню_ДобавленоВКонтролыФормы()
    {
        using var окно = new MainShell();

        var меню = окно.Controls.OfType<MenuStrip>().SingleOrDefault();

        Assert.NotNull(меню);
        Assert.Same(окно.MainMenuStrip, меню);
        Assert.Equal(DockStyle.Top, меню!.Dock);
    }

    /// <summary>
    /// C1: вкладка «Рассылки» содержит полосу действий с созданием рассылки —
    /// прежде действия жили только в невидимом меню.
    /// </summary>
    [Fact]
    public void MainShell_ВкладкаРассылок_СодержитПолосуДействий()
    {
        using var окно = new MainShell();

        var пункты = ПунктыПолосыДействий(окно, "Рассылки");

        Assert.Contains("Новая рассылка…", пункты);
        Assert.Contains("Повторная отправка…", пункты);
        Assert.Contains("Электронная рассылка…", пункты);
    }

    /// <summary>
    /// B1: вкладка «Конверты» содержит редактор бланков на экране.
    /// </summary>
    [Fact]
    public void MainShell_ВкладкаКонвертов_СодержитРедакторБланков()
    {
        using var окно = new MainShell();

        var пункты = ПунктыПолосыДействий(окно, "Конверты");

        Assert.Contains("Редактор бланков (F8)", пункты);
    }

    /// <summary>
    /// C2: пустая таблица рассылок сопровождается подсказкой с первым
    /// действием. Поведение переключения видимости проверяется отдельно
    /// в <see cref="EmptyStateHintTests"/>; окно не показывается, потому что
    /// OnLoad запускает полную инициализацию с базой (см. MainShellMenuTests).
    /// </summary>
    [Fact]
    public void MainShell_ВкладкаРассылок_СодержитПодсказкуПустогоСписка()
    {
        using var окно = new MainShell();

        var страница = Вкладка(окно, "Рассылки");

        var подсказка = Обойти(страница).OfType<Label>()
            .Single(метка => метка.Text.Contains("Рассылок пока нет", StringComparison.Ordinal));
        var таблица = Обойти(страница).OfType<DataGridView>().Single();

        Assert.False(string.IsNullOrWhiteSpace(подсказка.Text));
        Assert.Equal(DockStyle.Fill, подсказка.Dock);
        Assert.Equal(DockStyle.Fill, таблица.Dock);
    }

    /// <summary>
    /// U4: кнопки таблиц русифицированы — английских «+ / Edit / Del» и
    /// значка «⚙» в интерфейсе быть не должно.
    /// </summary>
    [Fact]
    public void MainShell_КнопкиТаблиц_Русифицированы()
    {
        using var окно = new MainShell();

        var подписи = Обойти(окно).OfType<Button>().Select(кнопка => кнопка.Text).ToArray();

        Assert.DoesNotContain("+", подписи);
        Assert.DoesNotContain("Edit", подписи);
        Assert.DoesNotContain("Del", подписи);
        Assert.DoesNotContain("⚙", подписи);
        Assert.Contains("Добавить", подписи);
        Assert.Contains("Изменить", подписи);
        Assert.Contains("Удалить", подписи);
        Assert.Contains("Сохранить вид таблицы", подписи);
        Assert.Contains("Вид таблиц…", подписи);
    }

    /// <summary>
    /// F1: счётчики строки состояния живут в потоке с отступами, а пробный
    /// период прижат к правому краю.
    /// </summary>
    [Fact]
    public void MainShell_СтрокаСостояния_СчётчикиВПотокеЛицензияСправа()
    {
        using var окно = new MainShell();

        var поток = окно.ПанельСостояния.Controls.OfType<FlowLayoutPanel>().SingleOrDefault();

        Assert.NotNull(поток);
        Assert.Equal(DockStyle.Right, окно.ИндикаторЛицензии.Dock);
    }

    /// <summary>
    /// Возвращает вкладку главного окна по названию.
    /// </summary>
    private static TabPage Вкладка(MainShell окно, string название) => окно.Вкладки.TabPages
        .Cast<TabPage>()
        .Single(страница => страница.Text == название);

    /// <summary>
    /// Возвращает подписи кнопок полосы действий указанной вкладки.
    /// </summary>
    private static string[] ПунктыПолосыДействий(MainShell окно, string вкладка) => Обойти(Вкладка(окно, вкладка))
        .OfType<ToolStrip>()
        .SelectMany(полоса => полоса.Items.Cast<ToolStripItem>())
        .Select(пункт => пункт.Text)
        .Where(текст => !string.IsNullOrEmpty(текст))
        .Cast<string>()
        .ToArray();

    /// <summary>
    /// Обходит контейнеры и возвращает все вложенные элементы управления.
    /// </summary>
    private static IEnumerable<Control> Обойти(Control контейнер)
    {
        foreach (Control элемент in контейнер.Controls)
        {
            yield return элемент;

            foreach (var вложенный in Обойти(элемент))
            {
                yield return вложенный;
            }
        }
    }
}
