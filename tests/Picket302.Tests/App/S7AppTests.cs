using System.Windows.Forms;
using Picket302.App.UI.Components;
using Picket302.App.UI.Shell;
using Picket302.Domain.Contracts;
using Picket302.Infrastructure.Integration;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Проверки скрытого входа в настройки подключения (ТЗ §8.11, функция 134).
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class ShortcutInterceptorTests
{
    [Fact]
    public void CtrlИДвойнойЩелчок_ОткрываетСкрытыйВход()
    {
        var перехватчик = new ShortcutInterceptor();

        var результат = перехватчик.HandleShortcutLaunch(new LaunchArgs(true, true, ["--shortcut"]));

        Assert.True(результат);
    }

    [Fact]
    public void ОбычныйЗапуск_НеОткрываетСкрытыйВход()
    {
        var перехватчик = new ShortcutInterceptor();

        Assert.False(перехватчик.HandleShortcutLaunch(new LaunchArgs(false, true, ["--shortcut"])));
        Assert.False(перехватчик.HandleShortcutLaunch(new LaunchArgs(true, false, ["--shortcut"])));
        Assert.False(перехватчик.HandleShortcutLaunch(new LaunchArgs(false, false, [])));
    }

    [Fact]
    public void МаркерЗапускаПоЯрлыку_ОпределяетсяВАргументах()
    {
        Assert.True(ShortcutInterceptor.IsShortcutLaunch(new LaunchArgs(false, false, ["--shortcut"])));
        Assert.False(ShortcutInterceptor.IsShortcutLaunch(new LaunchArgs(false, false, ["--import", "file.csv"])));
    }

    [Fact]
    public void ПостроениеПараметров_ОпределяетЗапускПоЯрлыку()
    {
        var параметры = ShortcutInterceptor.Build(["--shortcut"], isCtrlPressed: true);

        Assert.True(параметры.IsCtrlPressed);
        Assert.True(параметры.IsDoubleClick);
    }
}

/// <summary>
/// Проверки настройки вида таблиц: единообразное применение оформления и
/// сброс с предупреждением о перезапуске (ТЗ §8.11, функция 131).
/// </summary>
public sealed class GridAppearanceTests
{
    [Fact]
    public void СбросГлавнойФормы_ТребуетПерезапуска()
    {
        var результат = GridAppearanceManager.ResetToDefaults(GridAppearanceManager.GridAppearanceScope.MainForm);

        Assert.True(результат.RequiresRestart);
        Assert.Contains("перезапуск", результат.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GridAppearance.DefaultFocusColor, результат.Appearance.FocusColor);
        Assert.Equal(GridAppearance.DefaultSelectedColor, результат.Appearance.SelectedColor);
        Assert.Equal(GridAppearance.DefaultHintColor, результат.Appearance.HintColor);
    }

    [Fact]
    public void СбросТаблицы_НеТребуетПерезапуска()
    {
        var результат = GridAppearanceManager.ResetToDefaults(GridAppearanceManager.GridAppearanceScope.Grid);

        Assert.False(результат.RequiresRestart);
    }

    [Fact]
    public void ПрименениеОформления_МеняетЦветаТаблицы()
    {
        using var таблица = new DataGridView();

        GridAppearanceManager.Применить(таблица, GridAppearance.Default with { SelectedColor = 0x00FF00 });

        Assert.Equal(
            GridAppearanceManager.Цвет(0x00FF00),
            таблица.DefaultCellStyle.SelectionBackColor);
    }
}

/// <summary>
/// Проверки защищённого хранения паролей (ТЗ §8.7, функция 93; §8.8,
/// функция 102) — обязательное требование проверки приёмки: круговой
/// обмен через DPAPI.
/// </summary>
public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _временнаяПапка =
        Path.Combine(Path.GetTempPath(), "Picket302Dpapi_" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_временнаяПапка))
        {
            Directory.Delete(_временнаяПапка, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void КруговойОбменПароляЧерезDpapi()
    {
        var хранилище = new DpapiSecretStore(_временнаяПапка);

        var защищённый = хранилище.Protect("тайный-пароль");

        Assert.NotEqual("тайный-пароль", Convert.ToBase64String(защищённый));
        Assert.Equal("тайный-пароль", хранилище.Unprotect(защищённый));
    }

    [Fact]
    public void СохранённыйПарольНеЧитаетсяОткрытымТекстом()
    {
        var хранилище = new DpapiSecretStore(_временнаяПапка);

        var путь = хранилище.Save("test-password", "тайный-пароль");

        Assert.True(File.Exists(путь));
        Assert.DoesNotContain("тайный-пароль", File.ReadAllText(путь), StringComparison.Ordinal);
        Assert.Equal("тайный-пароль", хранилище.Load("test-password"));
    }

    [Fact]
    public void ПовреждённыйБлокНеВосстанавливаетПароль()
    {
        var хранилище = new DpapiSecretStore(_временнаяПапка);

        var защищённый = хранилище.Protect("тайный-пароль");

        защищённый[0] ^= 0xFF;

        Assert.ThrowsAny<SystemException>(() => хранилище.Unprotect(защищённый));
    }
}
