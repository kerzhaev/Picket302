using System.Drawing;
using System.Windows.Forms;
using Picket302.App;
using Picket302.App.UI.Components;
using Picket302.App.UI.Forms;
using Picket302.Domain.Contracts;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверка компоновки главного окна по схеме «3+2» (ТЗ §7.1: «Главное окно
/// приложения (Схема «3+2»)»; §7.10: «Правила цветовой схемы и плотности»;
/// §8.12, функция 152: «Четыре вкладки главного окна»).
/// </summary>
public sealed class MainShellLayoutTests
{
    [Fact]
    public void MainShell_ЧетыреВкладки_СоответствуютСпецификации()
    {
        using var окно = new MainShell();

        var названия = окно.Вкладки.TabPages.Cast<TabPage>().Select(страница => страница.Text).ToArray();

        Assert.Equal(["Адресная книга", "Конверты", "Рассылки", "Отправленные"], названия);
    }

    [Fact]
    public void MainShell_СтрокаСостояния_СодержитЧетыреСчётчика()
    {
        using var окно = new MainShell();

        Assert.StartsWith("Всего:", окно.СчётчикВсего.Text, StringComparison.Ordinal);
        Assert.StartsWith("Выбрано:", окно.СчётчикВыбрано.Text, StringComparison.Ordinal);
        Assert.StartsWith("Выведено:", окно.СчётчикВыведено.Text, StringComparison.Ordinal);
        Assert.StartsWith("Копий:", окно.СчётчикКопий.Text, StringComparison.Ordinal);
        Assert.StartsWith("База:", окно.СчётчикБаза.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainShell_СтрокаСостояния_РасположенаВнизуОкна()
    {
        using var окно = new MainShell();

        Assert.Equal(DockStyle.Bottom, окно.ПанельСостояния.Dock);
    }

    [Fact]
    public void MainShell_ДвеТаблицы_АдреснаяКнигаИСправочникОтправителей()
    {
        using var окно = new MainShell();

        Assert.Equal("Индекс", окно.ТаблицаОтправителей.Columns[0].Name);
        Assert.Equal("Наименование", окно.ТаблицаОтправителей.Columns[1].Name);

        Assert.Equal("VIP", окно.ТаблицаПолучателей.Columns[0].Name);
        Assert.Equal("Индекс", окно.ТаблицаПолучателей.Columns[1].Name);
        Assert.Equal("Получатель", окно.ТаблицаПолучателей.Columns[2].Name);
        Assert.Equal("Город", окно.ТаблицаПолучателей.Columns[3].Name);
        Assert.Equal("Улица", окно.ТаблицаПолучателей.Columns[4].Name);
        Assert.Equal("Копий", окно.ТаблицаПолучателей.Columns[5].Name);
        Assert.Equal("Группа", окно.ТаблицаПолучателей.Columns[6].Name);
    }

    [Fact]
    public void MainShell_РедакторыАдреса_СодержатШестьСтрок()
    {
        using var окно = new MainShell();

        Assert.Equal(6, окно.АдресОтправителя.ПоляСтрок.Count);
        Assert.Equal(6, окно.АдресПолучателя.ПоляСтрок.Count);
    }

    [Fact]
    public void MainShell_РабочийЭкран_СодержитГруппыРеференса()
    {
        using var окно = new MainShell();

        var заголовки = Обойти(окно)
            .OfType<GroupBox>()
            .Select(группа => группа.Text)
            .ToArray();

        Assert.Contains("ОТПРАВИТЕЛЬ:", заголовки);
        Assert.Contains("ПОЛУЧАТЕЛЬ:", заголовки);
        Assert.Contains("ВИД ЛОГОТИПА:", заголовки);
        Assert.Contains("ПРИМЕЧАНИЕ К АДРЕСУ:", заголовки);
        Assert.Contains("ОТ КОГО:", заголовки);
        Assert.Contains("КОМУ:", заголовки);
        Assert.True(окно.АдресОтправителя.Height >= 180);
        Assert.True(окно.АдресПолучателя.Height >= 180);
    }

    [Fact]
    public void MainShell_ПлотныйШрифтТаблиц_СоответствуетСпецификации()
    {
        using var окно = new MainShell();

        Assert.Equal("Tahoma", окно.Font.Name);
        Assert.Equal(8f, окно.Font.Size);
    }

    [Fact]
    public void GridAppearanceManager_ПрименяетПлотностьИЦвета()
    {
        using var таблица = new DataGridView();

        GridAppearanceManager.Применить(таблица, GridAppearance.Default);

        Assert.Equal("Tahoma", таблица.Font.Name);
        Assert.Equal(8f, таблица.Font.Size);
        Assert.Equal(17, таблица.RowTemplate.Height);
        Assert.Equal(
            GridAppearanceManager.Цвет(GridAppearance.DefaultSelectedColor).ToArgb(),
            таблица.DefaultCellStyle.SelectionBackColor.ToArgb());
    }

    [Fact]
    public void GridAppearanceManager_Зебра_ЧередуетСтроки()
    {
        using var таблица = new DataGridView();

        GridAppearanceManager.Применить(таблица, GridAppearance.Default with { ZebraEnabled = true });

        Assert.NotEqual(
            таблица.RowsDefaultCellStyle.BackColor.ToArgb(),
            таблица.AlternatingRowsDefaultCellStyle.BackColor.ToArgb());
    }

    [Fact]
    public void FormCounterpartyEdit_Вкладки_СоответствуютСпецификации()
    {
        using var форма = new FormCounterpartyEdit();

        var вкладки = форма.Controls.OfType<TabControl>().Single();

        Assert.Equal(
            ["Реквизиты", "Банк", "Паспорт"],
            вкладки.TabPages.Cast<TabPage>().Select(страница => страница.Text).ToArray());
    }

    [Fact]
    public void FormCounterpartyEdit_ТриАдреса_СПереключателемОсновного()
    {
        using var форма = new FormCounterpartyEdit();

        var редакторы = форма.Controls.OfType<TabControl>()
            .Single()
            .TabPages.Cast<TabPage>()
            .SelectMany(страница => Обойти(страница))
            .OfType<AddressEditor>()
            .ToArray();

        Assert.Equal(3, редакторы.Length);
        Assert.All(редакторы, редактор => Assert.NotNull(редактор.ПереключательОсновного));
    }

    /// <summary>
    /// Обходит контейнеры формы и возвращает все вложенные элементы управления.
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

    [Fact]
    public void FormCounterpartyEdit_НекорректныйИНН_БлокируетКнопкуСохранить()
    {
        using var форма = new FormCounterpartyEdit();

        форма.ПолеНаименование.Text = "ООО «Техно»";
        форма.ПолеИНН.Text = "7707083893";
        Assert.True(форма.СохранениеДоступно);

        // Заведомо ошибочный ИНН: кнопка «Сохранить» блокируется.
        форма.ПолеИНН.Text = "7701234561";
        Assert.False(форма.СохранениеДоступно);
    }

    [Fact]
    public void FormCounterpartyEdit_НекорректныйБИК_БлокируетКнопкуСохранить()
    {
        using var форма = new FormCounterpartyEdit();

        форма.ПолеНаименование.Text = "ООО «Техно»";
        форма.ПолеБИК.Text = "044525225";
        Assert.True(форма.СохранениеДоступно);

        // Восемь цифр вместо девяти — БИК некорректен.
        форма.ПолеБИК.Text = "12345678";
        Assert.False(форма.СохранениеДоступно);
    }

    [Fact]
    public void FormClearDatabase_ЧетыреОбластиОчистки_СоответствуютСпецификации()
    {
        Assert.Equal(4, Enum.GetValues<ClearScope>().Length);

        Assert.Equal("Категории контрагентов", FormClearDatabase.НазваниеОбласти(ClearScope.CounterpartyCategories));
        Assert.Equal("База рассылок", FormClearDatabase.НазваниеОбласти(ClearScope.Distributions));
        Assert.Equal("База контрагентов", FormClearDatabase.НазваниеОбласти(ClearScope.Counterparties));
        Assert.Equal("База отправленных писем", FormClearDatabase.НазваниеОбласти(ClearScope.SentMails));
    }

    [Fact]
    public void FormGridAppearance_ЗначенияПоУмолчанию_СоответствуютСпецификации()
    {
        using var форма = new FormGridAppearance(GridAppearance.Default);

        var оформление = форма.Оформление;

        Assert.Equal(4227327, оформление.FocusColor);
        Assert.Equal(6205183, оформление.SelectedColor);
        Assert.Equal(3723516, оформление.HintColor);
    }

    [Fact]
    public void FormSearch_ЗапросИРежимПоиска_ЗадаютсяПользователем()
    {
        using var форма = new FormSearch("Техно", 3);

        Assert.Equal("Техно", форма.Запрос);
        Assert.False(форма.НайтиСНачала);
    }

    [Fact]
    public void FormValuePicker_ВыбранноеЗначение_БерётсяИзСписка()
    {
        using var форма = new FormValuePicker("Подбор", ["101000", "125009"], "125009");

        Assert.Equal("125009", форма.ВыбранноеЗначение);
    }

    [Fact]
    public void FormValuePicker_БезВыбора_ВозвращаетсяПустоеЗначение()
    {
        using var форма = new FormValuePicker("Подбор", []);

        Assert.Equal(string.Empty, форма.ВыбранноеЗначение);
    }

    [Fact]
    public void MainShell_ДеревоПапок_ПрисутствуетНаВкладкеОтправленных()
    {
        using var окно = new MainShell();

        var страница = окно.Вкладки.TabPages.Cast<TabPage>()
            .Single(вкладка => вкладка.Text == "Отправленные");

        Assert.Contains(
            страница.Controls.Cast<Control>(),
            элемент => элемент is SplitContainer);
    }
}
