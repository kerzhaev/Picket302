using Picket302.App.UI;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Индикация текущей базы в строке состояния (дефект D2 приёмки 16.11.2026).
/// </summary>
public sealed class DatabaseStateTextTests
{
    [Fact]
    public void Общий_режим_показывает_адрес_службы()
    {
        var тексты = DatabaseStateText.Построить("192.168.1.42:30242", string.Empty);

        Assert.Equal("Общая база: 192.168.1.42:30242", тексты.Text);
        Assert.Contains("192.168.1.42:30242", тексты.Tooltip);
        Assert.Equal(тексты.Text, тексты.AccessibleName);
    }

    [Fact]
    public void Общий_режим_не_показывает_старый_локальный_файл()
    {
        // В профиле мог остаться путь прежней локальной базы: в общем режиме
        // он не должен вводить оператора в заблуждение.
        var тексты = DatabaseStateText.Построить(
            "10.0.0.5:30242",
            @"C:\Users\operator\AppData\Roaming\Picket302\picket302.db");

        Assert.StartsWith("Общая база:", тексты.Text);
        Assert.DoesNotContain("picket302.db", тексты.Text);
        Assert.DoesNotContain("picket302.db", тексты.AccessibleName);
    }

    [Fact]
    public void Локальный_режим_показывает_имя_файла_и_полный_путь_в_подсказке()
    {
        var путь = @"C:\Users\operator\AppData\Roaming\Picket302\picket302.db";

        var тексты = DatabaseStateText.Построить(null, путь);

        Assert.Equal("Локальная база: picket302.db", тексты.Text);
        Assert.Equal(путь, тексты.Tooltip);
        Assert.Contains(путь, тексты.AccessibleName);
    }

    [Fact]
    public void Пустой_путь_не_оставляет_строку_без_значения()
    {
        var тексты = DatabaseStateText.Построить(null, string.Empty);

        Assert.Equal("Локальная база: не выбрана", тексты.Text);
        Assert.False(string.IsNullOrWhiteSpace(тексты.AccessibleName));
    }

    [Fact]
    public void Пробелы_вместо_адреса_считаются_локальным_режимом()
    {
        var тексты = DatabaseStateText.Построить("   ", @"C:\data\picket302.db");

        Assert.Equal("Локальная база: picket302.db", тексты.Text);
    }
}
