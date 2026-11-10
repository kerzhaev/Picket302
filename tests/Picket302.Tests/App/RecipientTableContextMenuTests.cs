using System.Windows.Forms;
using Picket302.App;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Контракт контекстного меню строк адресной книги. Меню сопоставляется с
/// legacy-поверхностью KonvertProNet и проверяется как рабочий WinForms-контрол,
/// а не как набор строк в документации.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class RecipientTableContextMenuTests
{
    [Fact]
    public void MainShell_МенюСтроки_СодержитLegacyКомандыИРабочиеРазделы()
    {
        using var окно = new MainShell();
        var меню = окно.ТаблицаПолучателей.ContextMenuStrip;

        Assert.NotNull(меню);
        Assert.Equal("Контекстное меню адресной книги", меню!.AccessibleName);

        var пункты = меню.Items.OfType<ToolStripMenuItem>().ToList();
        var подписи = пункты.Select(пункт => пункт.Text).ToList();

        Assert.Contains("Поиск", подписи);
        Assert.Contains("История печати конвертов", подписи);
        Assert.Contains("Группы конвертов", подписи);
        Assert.Contains("Создать копию строки", подписи);
        Assert.Contains("Скопировать в таблицу «От кого»", подписи);
        Assert.Contains("Копировать ячейку", подписи);
        Assert.Contains("Экспорт/импорт таблицы", подписи);
        Assert.Contains("Настройка таблицы", подписи);

        Assert.Contains("Избранное (VIP)", подписи);
        Assert.Contains("Объединить в рассылку", подписи);
        Assert.Contains("Переместить в категорию", подписи);
    }

    [Fact]
    public void MainShell_МенюСтроки_ГорячиеКлавишиИДоступностьНастроены()
    {
        using var окно = new MainShell();
        var меню = окно.ТаблицаПолучателей.ContextMenuStrip!;

        var поиск = меню.Items.OfType<ToolStripMenuItem>().Single(пункт => пункт.Text == "Поиск");
        var история = меню.Items.OfType<ToolStripMenuItem>().Single(пункт => пункт.Text == "История печати конвертов");
        var группы = меню.Items.OfType<ToolStripMenuItem>().Single(пункт => пункт.Text == "Группы конвертов");
        var ячейка = меню.Items.OfType<ToolStripMenuItem>().Single(пункт => пункт.Text == "Копировать ячейку");

        Assert.Equal(Keys.Control | Keys.F, поиск.ShortcutKeys);
        Assert.Equal(Keys.Control | Keys.H, история.ShortcutKeys);
        Assert.Equal(Keys.Control | Keys.G, группы.ShortcutKeys);
        Assert.Equal(Keys.Control | Keys.C, ячейка.ShortcutKeys);

        foreach (var пункт in меню.Items.OfType<ToolStripMenuItem>())
        {
            Assert.False(string.IsNullOrWhiteSpace(пункт.AccessibleName), $"Пункт «{пункт.Text}» без AccessibleName.");
            Assert.False(string.IsNullOrWhiteSpace(пункт.AccessibleDescription), $"Пункт «{пункт.Text}» без AccessibleDescription.");
        }
    }

    [Fact]
    public void MainShell_МенюОбмена_СодержитРеальныеИмпортИЭкспортПотоки()
    {
        using var окно = new MainShell();
        var меню = окно.ТаблицаПолучателей.ContextMenuStrip!;
        var обмен = меню.Items
            .OfType<ToolStripMenuItem>()
            .Single(пункт => пункт.Text == "Экспорт/импорт таблицы");

        var подписи = обмен.DropDownItems
            .OfType<ToolStripMenuItem>()
            .Select(пункт => пункт.Text)
            .ToList();

        Assert.Contains("Открыть список в Excel", подписи);
        Assert.Contains("Выгрузить данные…", подписи);
        Assert.Contains("Мастер импорта адресной книги…", подписи);
        Assert.Contains("Импорт из 1С…", подписи);
    }
}
