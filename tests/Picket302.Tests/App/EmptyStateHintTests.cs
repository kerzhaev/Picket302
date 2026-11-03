using System.Windows.Forms;
using Picket302.App.UI.Components;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Подсказка пустого списка (аудит UX 2026-11-03, C2/D2): пустая таблица
/// скрывается, вместо неё виден текст с первым действием; при появлении
/// строк всё возвращается на место.
/// </summary>
public sealed class EmptyStateHintTests
{
    [Fact]
    public void ПустаяТаблица_Скрывается_ВместоНеЁПодсказка()
    {
        using var таблица = new DataGridView();

        using var подсказка = EmptyStateHint.Создать(таблица, "Рассылок пока нет.");

        Assert.True(подсказка.Visible);
        Assert.False(таблица.Visible);
    }

    [Fact]
    public void ПоявлениеСтроки_ВозвращаетТаблицу_ИПрячетПодсказку()
    {
        using var таблица = new DataGridView();
        таблица.Columns.Add("Имя", "Имя");

        using var подсказка = EmptyStateHint.Создать(таблица, "Рассылок пока нет.");
        таблица.Rows.Add("ООО «Техно»");

        Assert.False(подсказка.Visible);
        Assert.True(таблица.Visible);
    }

    [Fact]
    public void ОчисткаТаблицы_ВновьПоказываетПодсказку()
    {
        using var таблица = new DataGridView();
        таблица.Columns.Add("Имя", "Имя");
        таблица.Rows.Add("ООО «Техно»");

        using var подсказка = EmptyStateHint.Создать(таблица, "Рассылок пока нет.");
        Assert.False(подсказка.Visible);

        таблица.Rows.Clear();

        Assert.True(подсказка.Visible);
        Assert.False(таблица.Visible);
    }

    [Fact]
    public void НекорректныеАргументы_Отклоняются()
    {
        using var таблица = new DataGridView();

        Assert.Throws<ArgumentNullException>(() => EmptyStateHint.Создать(null!, "Текст"));
        Assert.Throws<ArgumentException>(() => EmptyStateHint.Создать(таблица, " "));
    }
}
