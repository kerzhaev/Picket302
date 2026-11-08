using Picket302.App.UI.Forms;
using Picket302.Domain.Contracts;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверки диалога настройки вида таблиц (ТЗ §8.11, функция 131:
/// «Настройка вида таблиц: цвета, заголовки столбцов, скрытие колонок,
/// «полосатая таблица» глобально, сброс к умолчаниям»).
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class FormGridAppearanceTests
{
    /// <summary>
    /// Переключатель заголовков столбцов доходит до настроек оформления:
    /// выключенный переключатель возвращается выключенным.
    /// </summary>
    [Fact]
    public void ПереключательЗаголовков_ВозвращаетсяВНастройкахОформления()
    {
        using var форма = new FormGridAppearance(GridAppearance.Default with { HeadersVisible = false });

        Assert.False(форма.Оформление.HeadersVisible);
    }

    /// <summary>
    /// Включённый переключатель заголовков сохраняет своё состояние.
    /// </summary>
    [Fact]
    public void ПереключательЗаголовков_ВключённыйСохраняется()
    {
        using var форма = new FormGridAppearance(GridAppearance.Default with { HeadersVisible = true });

        Assert.True(форма.Оформление.HeadersVisible);
    }

    /// <summary>
    /// Сброс к умолчаниям возвращает заголовки столбцов и сигнализирует
    /// оболочке о необходимости сбросить состояние столбцов.
    /// </summary>
    [Fact]
    public void СбросКУмолчаниям_ВозвращаетЗаголовкиИСигналитОболочке()
    {
        using var форма = new FormGridAppearance(GridAppearance.Default with { HeadersVisible = false, ZebraEnabled = true });

        Assert.False(форма.СброшеноКУмолчаниям);

        форма.ResetToDefaults();

        Assert.True(форма.СброшеноКУмолчаниям);
        Assert.True(форма.Оформление.HeadersVisible);
        Assert.False(форма.Оформление.ZebraEnabled);
    }

    [Fact]
    public void ПояснениеЦветов_НеВыходитЗаПределыВкладкиИПереносится()
    {
        using var форма = new FormGridAppearance(GridAppearance.Default);

        var пояснение = Обойти(форма)
            .OfType<Label>()
            .Single(метка => метка.Text.StartsWith("Три независимых цвета", StringComparison.Ordinal));

        Assert.False(пояснение.AutoSize);
        Assert.True(пояснение.Height >= пояснение.Font.Height * 2);
        Assert.NotNull(пояснение.Parent);
        Assert.InRange(пояснение.Right, 0, пояснение.Parent!.ClientSize.Width);
    }

    private static IEnumerable<Control> Обойти(Control корень)
    {
        foreach (Control дочерний in корень.Controls)
        {
            yield return дочерний;

            foreach (var вложенный in Обойти(дочерний))
            {
                yield return вложенный;
            }
        }
    }
}
