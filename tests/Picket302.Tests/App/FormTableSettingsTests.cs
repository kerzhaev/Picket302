using System.Windows.Forms;
using Picket302.App.UI.Forms;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Контракт реального редактора состава и свойств таблицы адресной книги.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormTableSettingsTests
{
    [Fact]
    public void СписокСтолбцов_ПоказываетСоставТаблицыИПрименяетВидимость()
    {
        using var таблица = СоздатьТаблицу();
        using var форма = new FormTableSettings(таблица);

        Assert.Equal(3, форма.КоличествоСтолбцов);
        Assert.Equal(new[] { "Индекс", "Получатель", "Город" }, форма.ПодписиСтолбцов);

        var список = Обойти(форма)
            .OfType<CheckBox>()
            .Single(флажок => флажок.AccessibleName == "Столбец: Индекс");
        список.Checked = false;
        форма.Применить();

        Assert.False(таблица.Columns[0].Visible);
        Assert.True(таблица.Columns[1].Visible);
    }

    [Fact]
    public void СбросКУмолчаниям_ВозвращаетПодписиШириныИВидимость()
    {
        using var таблица = СоздатьТаблицу();
        using var форма = new FormTableSettings(таблица);

        var полеНазвания = Обойти(форма)
            .OfType<TextBox>()
            .Single(поле => поле.AccessibleName == "Название выбранного столбца");
        полеНазвания.Text = "Изменённый индекс";
        форма.Применить();
        Assert.Equal("Изменённый индекс", таблица.Columns[0].HeaderText);

        форма.СброситьКУмолчаниям();

        Assert.Equal(new[] { "Индекс", "Получатель", "Город" }, форма.ПодписиСтолбцов);
        форма.Применить();
        Assert.True(таблица.Columns[0].Visible);
        Assert.Equal("Индекс", таблица.Columns[0].HeaderText);
    }

    /// <summary>
    /// D-S06-1 (ревью S04 Q4-2/Q4-3): метка кнопки сброса «По умолчанию»
    /// не должна обрезаться при отрисовке, заголовок окна соответствует
    /// легаси-референсу «Настройки свойств таблиц».
    /// </summary>
    [Fact]
    public void КнопкаСброса_МеткаНеОбрезаетсяИЗаголовокЛегаси()
    {
        using var таблица = СоздатьТаблицу();
        using var форма = new FormTableSettings(таблица);
        форма.Show();

        Assert.Equal("Настройки свойств таблиц", форма.Text);

        var сброс = Обойти(форма)
            .OfType<Button>()
            .Single(кнопка => кнопка.AccessibleName == "Восстановить настройки таблицы по умолчанию");
        var желаемаяШирина = TextRenderer.MeasureText(сброс.Text, сброс.Font).Width;
        Assert.True(
            сброс.Width >= желаемаяШирина,
            $"кнопка сброса уже своего текста: {сброс.Width}px < {желаемаяШирина}px («{сброс.Text}»)");
    }

    /// <summary>
    /// Легаси-опции референса «Настройки свойств таблиц» (S08): высота
    /// заголовка, жирный шрифт заголовка, закреплённые столбцы.
    /// </summary>
    [Fact]
    public void ЛегасиОпцииЗаголовка_ПрименяютсяКТаблице()
    {
        using var таблица = СоздатьТаблицу();
        using var форма = new FormTableSettings(таблица);
        форма.Show();

        var высота = Обойти(форма).OfType<NumericUpDown>()
            .Single(поле => поле.AccessibleName == "Высота заголовка");
        высота.Value = 36;

        var жирный = Обойти(форма).OfType<CheckBox>()
            .Single(флажок => флажок.AccessibleName == "Жирный шрифт заголовка таблицы");
        жирный.Checked = true;

        var закреплено = Обойти(форма).OfType<NumericUpDown>()
            .Single(поле => поле.AccessibleName == "Зафиксированных столбцов");
        закреплено.Value = 2;

        форма.Применить();

        Assert.Equal(36, таблица.ColumnHeadersHeight);
        Assert.True(таблица.ColumnHeadersDefaultCellStyle.Font.Bold);
        Assert.True(таблица.Columns[0].Frozen);
        Assert.True(таблица.Columns[1].Frozen);
        Assert.False(таблица.Columns[2].Frozen);
    }

    /// <summary>
    /// Сброс к умолчаниям возвращает все легаси-опции заголовка (S08,
    /// расширено по ревью: высота, жирный шрифт и закрепление).
    /// </summary>
    [Fact]
    public void ЛегасиОпцииЗаголовка_СбрасываютсяКУмолчаниям()
    {
        using var таблица = СоздатьТаблицу();
        using var форма = new FormTableSettings(таблица);
        форма.Show();

        var исходнаяВысота = таблица.ColumnHeadersHeight;
        var исходныйЖирный = таблица.ColumnHeadersDefaultCellStyle.Font.Bold;
        var исходныеЗакреплены = таблица.Columns.Cast<DataGridViewColumn>()
            .Count(столбец => столбец.Frozen);

        var высота = Обойти(форма).OfType<NumericUpDown>()
            .Single(поле => поле.AccessibleName == "Высота заголовка");
        высота.Value = 36;
        var жирный = Обойти(форма).OfType<CheckBox>()
            .Single(флажок => флажок.AccessibleName == "Жирный шрифт заголовка таблицы");
        жирный.Checked = true;
        var закреплено = Обойти(форма).OfType<NumericUpDown>()
            .Single(поле => поле.AccessibleName == "Зафиксированных столбцов");
        закреплено.Value = 2;

        форма.СброситьКУмолчаниям();

        Assert.Equal(исходнаяВысота, (int)высота.Value);
        Assert.Equal(исходныйЖирный, жирный.Checked);
        Assert.Equal(исходныеЗакреплены, (int)закреплено.Value);
    }

    /// <summary>
    /// Пояснение вкладки «Другие свойства» не обрезается после добавления
    /// легаси-полей (P2 ревью S08: ClientSize увеличен под 13 строк).
    /// </summary>
    [Fact]
    public void ПояснениеВкладкиСвойств_ПолностьюВидимо()
    {
        using var таблица = СоздатьТаблицу();
        using var форма = new FormTableSettings(таблица);
        форма.Show();

        var пояснение = Обойти(форма).OfType<Label>()
            .Single(метка => метка.AccessibleName == "Пояснение свойств таблицы");
        var родитель = (Control)пояснение.Parent!;

        Assert.True(
            пояснение.Bottom <= родитель.ClientSize.Height,
            $"пояснение обрезано: bottom={пояснение.Bottom} > {родитель.ClientSize.Height}");
    }

    private static DataGridView СоздатьТаблицу()
    {
        var таблица = new DataGridView
        {
            RowHeadersVisible = true,
            AllowUserToOrderColumns = true,
            AllowUserToResizeColumns = true,
        };
        таблица.Columns.Add("Индекс", "Индекс");
        таблица.Columns.Add("Получатель", "Получатель");
        таблица.Columns.Add("Город", "Город");
        таблица.Columns[0].Width = 70;
        таблица.Columns[1].Width = 180;
        таблица.Columns[2].Width = 110;
        return таблица;
    }

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
