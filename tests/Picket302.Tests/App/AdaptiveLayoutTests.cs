using System.Reflection;
using System.Windows.Forms;
using Picket302.App;
using Picket302.App.UI.Components;
using Picket302.App.UI.Forms;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Безопасные layout-проверки адаптивной компоновки (аудит UX 2026-11-03,
/// G2): гибкие контейнеры вместо координат, кнопки не уходят за границу при
/// минимальном размере, переключатели не перекрывают поля. Визуальный запуск
/// приложения не выполняется — проверяются свойства компоновки; база —
/// временная изолированная.
/// </summary>
public sealed class AdaptiveLayoutTests
{
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

    private static T Поле<T>(Form форма, string имя)
        where T : Control
    {
        var поле = typeof(FormDistributionEditor)
            .GetField(имя, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(форма);

        return Assert.IsType<T>(поле!);
    }

    private static async Task<FormDistributionEditor> СоздатьРедактор(
        ИзолированныйПрофиль профиль)
    {
        var состав = await AppServices.СоздатьAsync(профиль.Пути, создатьДемоБазу: false);

        return new FormDistributionEditor(состав);
    }

    [Fact]
    public async Task РедакторРассылок_КнопкиИтогаВПотоке_НеИсчезаютПриУзкомОкне()
    {
        using var профиль = new ИзолированныйПрофиль();
        using var форма = await СоздатьРедактор(профиль);

        var сохранить = Поле<Button>(форма, "_сохранить");
        var отмена = Поле<Button>(форма, "_отмена");

        // Кнопки живут в правом потоке: при узком окне поток сжимает
        // подписи, но кнопки остаются в клиентской области (G2).
        Assert.IsType<FlowLayoutPanel>(сохранить.Parent);
        Assert.IsType<FlowLayoutPanel>(отмена.Parent);
        Assert.Equal(DockStyle.Right, сохранить.Parent!.Dock);

        // Минимальный размер ограничивает сжатие окна.
        Assert.True(форма.MinimumSize.Width >= 760, $"MinimumSize.Width = {форма.MinimumSize.Width}");
    }

    [Fact]
    public async Task РедакторРассылок_ШапкаПараметровТаблицей()
    {
        using var профиль = new ИзолированныйПрофиль();
        using var форма = await СоздатьРедактор(профиль);

        var наименование = Поле<TextBox>(форма, "_наименование");

        // Поле лежит в таблице шапки, а не на фиксированных координатах (G2).
        Assert.IsType<TableLayoutPanel>(наименование.Parent);
    }

    [Fact]
    public async Task РедакторРассылок_ПриМинимальномРазмереКнопкиВКлиентскойОбласти()
    {
        using var профиль = new ИзолированныйПрофиль();
        using var форма = await СоздатьРедактор(профиль);

        форма.ClientSize = форма.MinimumSize;
        форма.PerformLayout();

        var кнопки = Обойти(форма).OfType<Button>()
            .Where(кнопка => кнопка.Text is "Сохранить" or "Отмена")
            .ToList();

        Assert.Equal(2, кнопки.Count);
        Assert.All(кнопки, кнопка =>
        {
            Assert.True(
                форма.ClientRectangle.Contains(кнопка.Bounds),
                $"Кнопка «{кнопка.Text}» выходит за клиентскую область: {кнопка.Bounds} против {форма.ClientRectangle}.");
        });
    }

    [Fact]
    public void РедакторАдреса_ПереключательОсновногоВСеткеНеПерекрываетИндекс()
    {
        using var редактор = new AddressEditor("Тест", показыватьЗаголовок: false);

        // Переключатель перенесён в таблицу полей (отдельная ячейка): прежнее
        // координатное размещение накладывалось на поле индекса (G2).
        // Проверяется именно вхождение в таблицу — раскладка ячеек без
        // дескриптора окна не вычисляется достоверно.
        var сетка = Assert.IsType<TableLayoutPanel>(редактор.ПереключательОсновного.Parent);

        Assert.Equal(редактор.ПолеИндекса.Parent, сетка);

        var позицияРадио = сетка.GetCellPosition(редактор.ПереключательОсновного);
        var позицияИндекса = сетка.GetCellPosition(редактор.ПолеИндекса);

        Assert.NotEqual(позицияРадио, позицияИндекса);
        Assert.True(позицияРадио.Column > позицияИндекса.Column, "Радио должно занимать собственный столбец справа от индекса.");
    }

    [Fact]
    public void РедакторАдреса_ПоляСтрокИмеютДоступныеИменаСПодсказками()
    {
        using var редактор = new AddressEditor("Тест");

        Assert.Equal(6, редактор.ПоляСтрок.Count);

        Assert.All(редактор.ПоляСтрок, поле =>
        {
            Assert.False(string.IsNullOrWhiteSpace(поле.AccessibleName));
            Assert.StartsWith("Строка", поле.AccessibleName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainShell_ПанельЛоготипаСтруктурированаПоГруппам()
    {
        using var окно = new MainShell();

        var сЛоготипом = Обойти(окно).OfType<RadioButton>()
            .Single(радио => радио.Text == "С логотипом");
        var адресОтправителя = Обойти(окно).OfType<RadioButton>()
            .Single(радио => радио.Text == "Отправителя");

        // Пары переключателей разложены по собственным строкам-потокам
        // таблицей, без координат (A4/G2).
        Assert.IsType<FlowLayoutPanel>(сЛоготипом.Parent);
        Assert.IsType<FlowLayoutPanel>(адресОтправителя.Parent);
        Assert.NotSame(сЛоготипом.Parent, адресОтправителя.Parent);

        // Действия клипарта доступны с экрана (№158).
        var кнопки = Обойти(окно).OfType<Button>()
            .Select(кнопка => кнопка.Text)
            .ToList();

        Assert.Contains("Клипарт…", кнопки);
        Assert.Contains("Убрать логотип", кнопки);
    }

    [Fact]
    public void MainShell_ПоискОтправителяПодписанКакПоиск()
    {
        using var окно = new MainShell();

        var метки = Обойти(окно).OfType<Label>().Select(метка => метка.Text).ToList();

        Assert.Contains("Поиск по названию:", метки);
        Assert.DoesNotContain("Быстрый выбор:", метки);
    }
}
