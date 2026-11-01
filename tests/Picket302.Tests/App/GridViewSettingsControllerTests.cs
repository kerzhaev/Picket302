using System.Drawing;
using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.Application.Services;
using Picket302.Domain.Contracts;
using Picket302.Infrastructure.Storage;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверки настройки вида таблиц на настоящем <see cref="DataGridView"/>
/// (ТЗ §8.11, функция 131: «Настройка вида таблиц: цвета, заголовки столбцов,
/// скрытие колонок, «полосатая таблица» глобально, сброс к умолчаниям»).
/// </summary>
public sealed class GridViewSettingsControllerTests : IDisposable
{
    private const string ИдентификаторТаблицы = "Адресная книга";

    private readonly string _путьФайла = Path.Combine(
        Path.GetTempPath(),
        $"picket302_appearance_{Guid.NewGuid():N}.json");

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(_путьФайла))
        {
            File.Delete(_путьФайла);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Создаёт таблицу адресной книги с тремя столбцами.
    /// </summary>
    /// <returns>Таблица.</returns>
    private static DataGridView Таблица()
    {
        var таблица = new DataGridView();
        таблица.Columns.Add("VIP", "VIP");
        таблица.Columns.Add("Индекс", "Индекс");
        таблица.Columns.Add("Получатель", "Получатель");

        return таблица;
    }

    /// <summary>
    /// Создаёт контроллер настроек вида таблиц над временным файлом настроек.
    /// </summary>
    /// <returns>Контроллер.</returns>
    private GridViewSettingsController Контроллер() =>
        new(new UiSettingsStorage(new JsonUiSettingsStore(_путьФайла)));

    /// <summary>
    /// Загружает настройки и применяет их к новой таблице. Таблица создаётся
    /// после чтения файла: элементы таблицы изменяются в том же потоке, в
    /// котором созданы.
    /// </summary>
    /// <param name="контроллер">Контроллер настроек вида таблиц.</param>
    /// <returns>Таблица с применёнными настройками.</returns>
    private static async Task<DataGridView> ЗагрузитьИПрименить(
        GridViewSettingsController контроллер)
    {
        var настройки = await контроллер.ЗагрузитьAsync(ИдентификаторТаблицы);

        var таблица = Таблица();
        контроллер.Применить(таблица, настройки);

        return таблица;
    }

    /// <summary>
    /// Сохранённые настройки применяются к таблице: столбец скрывается, подпись
    /// столбца меняется.
    /// </summary>
    [Fact]
    public async Task НастройкиВидаТаблицы_СкрываютСтолбецИМеняютПодпись()
    {
        var контроллер = Контроллер();

        using (var исходная = Таблица())
        {
            исходная.Columns["Индекс"].Visible = false;
            исходная.Columns["Получатель"].HeaderText = "Контрагент";

            await контроллер.СохранитьAsync(исходная, ИдентификаторТаблицы);
        }

        using var таблица = await ЗагрузитьИПрименить(контроллер);

        Assert.False(таблица.Columns["Индекс"].Visible);
        Assert.Equal("Контрагент", таблица.Columns["Получатель"].HeaderText);
        Assert.True(таблица.Columns["VIP"].Visible);
    }

    /// <summary>
    /// Ширина столбца сохраняется и восстанавливается (ТЗ §7.6; §8.11,
    /// функция 131).
    /// </summary>
    [Fact]
    public async Task НастройкиВидаТаблицы_СохраняютШиринуСтолбца()
    {
        var контроллер = Контроллер();

        using (var исходная = Таблица())
        {
            исходная.Columns["Получатель"].Width = 321;

            await контроллер.СохранитьAsync(исходная, ИдентификаторТаблицы);
        }

        using var таблица = await ЗагрузитьИПрименить(контроллер);

        Assert.Equal(321, таблица.Columns["Получатель"].Width);
    }

    /// <summary>
    /// Скрытие столбца сохраняется: после перезапуска столбец остаётся скрытым.
    /// </summary>
    [Fact]
    public async Task СкрытиеСтолбца_СохраняетсяИВосстанавливается()
    {
        var контроллер = Контроллер();

        using (var исходная = Таблица())
        {
            await контроллер.СкрытьСтолбецAsync(исходная, ИдентификаторТаблицы, 1);
        }

        using var таблица = await ЗагрузитьИПрименить(контроллер);

        Assert.False(таблица.Columns["Индекс"].Visible);
        Assert.True(таблица.Columns["VIP"].Visible);
    }

    /// <summary>
    /// Возврат всех столбцов отменяет скрытие и сохраняется.
    /// </summary>
    [Fact]
    public async Task ВозвратВсехСтолбцов_ОтменяетСкрытиеИСохраняется()
    {
        var контроллер = Контроллер();

        using var исходная = Таблица();
        await контроллер.СкрытьСтолбецAsync(исходная, ИдентификаторТаблицы, 1);

        Assert.False(исходная.Columns["Индекс"].Visible);

        await контроллер.ПоказатьВсеСтолбцыAsync(исходная, ИдентификаторТаблицы);

        Assert.True(исходная.Columns["Индекс"].Visible);

        using var таблица = await ЗагрузитьИПрименить(контроллер);

        Assert.True(таблица.Columns["Индекс"].Visible);
    }

    /// <summary>
    /// Переключатель заголовков столбцов сохраняется и применяется.
    /// </summary>
    [Fact]
    public async Task ПереключательЗаголовковСтолбцов_СохраняетсяИПрименяется()
    {
        var контроллер = Контроллер();

        using (var исходная = Таблица())
        {
            исходная.ColumnHeadersVisible = false;

            await контроллер.СохранитьAsync(исходная, ИдентификаторТаблицы);
            await new UiSettingsStorage(new JsonUiSettingsStore(_путьФайла))
                .SaveAppearanceAsync(GridAppearance.Default with { HeadersVisible = false });
        }

        using var таблица = await ЗагрузитьИПрименить(контроллер);

        Assert.False(таблица.ColumnHeadersVisible);
    }

    /// <summary>
    /// «Полосатая таблица» включается и выключается глобально.
    /// </summary>
    [Fact]
    public async Task ПолосатаяТаблица_ВключаетсяИВыключается()
    {
        var контроллер = Контроллер();

        using (var таблица = await ЗагрузитьИПрименить(контроллер))
        {
            Assert.Equal(SystemColors.Window, таблица.AlternatingRowsDefaultCellStyle.BackColor);
        }

        await new UiSettingsStorage(new JsonUiSettingsStore(_путьФайла))
            .SaveAppearanceAsync(GridAppearance.Default with { ZebraEnabled = true });

        using (var другая = await ЗагрузитьИПрименить(контроллер))
        {
            Assert.Equal(Color.FromArgb(240, 240, 240), другая.AlternatingRowsDefaultCellStyle.BackColor);
        }
    }

    /// <summary>
    /// Сброс к умолчаниям возвращает штатные настройки и сохраняет их.
    /// </summary>
    [Fact]
    public async Task СбросНастроекВидаТаблицы_ВозвращаетУмолчания()
    {
        var контроллер = Контроллер();

        using (var исходная = Таблица())
        {
            исходная.Columns["Индекс"].Visible = false;
            исходная.Columns["Получатель"].HeaderText = "Контрагент";

            await контроллер.СохранитьAsync(исходная, ИдентификаторТаблицы);
            await new UiSettingsStorage(new JsonUiSettingsStore(_путьФайла))
                .SaveAppearanceAsync(GridAppearance.Default with { ZebraEnabled = true, HeadersVisible = false });
        }

        using var таблица = await ЗагрузитьИПрименить(контроллер);

        Assert.False(таблица.Columns["Индекс"].Visible);
        Assert.False(таблица.ColumnHeadersVisible);

        var оформление = await контроллер.СброситьКУмолчаниямAsync(таблица, ИдентификаторТаблицы);

        Assert.True(таблица.Columns["Индекс"].Visible);
        Assert.Equal("Получатель", таблица.Columns["Получатель"].HeaderText);
        Assert.True(таблица.ColumnHeadersVisible);
        Assert.Equal(SystemColors.Window, таблица.AlternatingRowsDefaultCellStyle.BackColor);
        Assert.False(оформление.ZebraEnabled);
        Assert.True(оформление.HeadersVisible);

        // Штатные настройки сохранены — новая таблица после перезапуска получает
        // их же.
        using var послеПерезапуска = await ЗагрузитьИПрименить(контроллер);

        Assert.True(послеПерезапуска.Columns["Индекс"].Visible);
        Assert.Equal("Получатель", послеПерезапуска.Columns["Получатель"].HeaderText);
        Assert.True(послеПерезапуска.ColumnHeadersVisible);
    }
}
