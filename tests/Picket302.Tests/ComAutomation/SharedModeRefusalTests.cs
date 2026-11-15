using Picket302.App.Shared;
using Picket302.ComAutomation;
using Picket302.Infrastructure.Integration;
using Xunit;

namespace Picket302.Tests.ComAutomation;

/// <summary>
/// Отказ COM-сервера автоматизации в общем режиме: сопряжённое рабочее
/// место не должно открывать устаревшую локальную базу вместо общей.
/// </summary>
/// <remarks>
/// Регрессия: правило сопряжения искало в файле реквизитов имена свойств в
/// другом регистре, чем их записывает оболочка, поэтому отказ не
/// срабатывал — COM-сервер молча открывал локальную копию данных.
/// </remarks>
public sealed class SharedModeRefusalTests : IDisposable
{
    private readonly string _каталог = Path.Combine(
        Path.GetTempPath(),
        "picket302-com-" + Guid.NewGuid().ToString("N"));

    public SharedModeRefusalTests() => Directory.CreateDirectory(_каталог);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_каталог))
            {
                Directory.Delete(_каталог, true);
            }
        }
        catch (IOException)
        {
            // Временный каталог остаётся: это не влияет на проверку.
        }
    }

    [Fact]
    public void ПрофильСопряжения_РабочееМестоСчитаетсяСопряжённым()
    {
        var профиль = new SharedClientProfile(_каталог);

        профиль.Сохранить(new SharedClientSettings(
            "192.168.1.117",
            30242,
            "устройство-1",
            "сервер-1",
            "AB12CD34"));

        Assert.True(SharedWorkstationMode.СопряженоСОбщейБазой(_каталог));
    }

    [Fact]
    public void ПрофильБезУстройства_РабочееМестоНеСчитаетсяСопряжённым()
    {
        var профиль = new SharedClientProfile(_каталог);

        профиль.Сохранить(new SharedClientSettings(
            "192.168.1.117",
            30242,
            string.Empty,
            "сервер-1",
            "AB12CD34"));

        Assert.False(SharedWorkstationMode.СопряженоСОбщейБазой(_каталог));
    }

    [Fact]
    public void БезФайлаРеквизитов_РабочееМестоНеСчитаетсяСопряжённым()
    {
        Assert.False(SharedWorkstationMode.СопряженоСОбщейБазой(_каталог));
    }

    [Fact]
    public async Task ComСервер_СопряжённоеРабочееМесто_НеОткрываетЛокальнуюБазу()
    {
        // Локальная база существует и вполне работоспособна — но рабочее
        // место сопряжено с общей базой, поэтому COM-сервер обязан отказать,
        // а не открыть устаревшую локальную копию. База создаётся настоящей,
        // чтобы единственной причиной отказа был режим работы.
        var путьБазы = Path.Combine(_каталог, "picket302.db");

        using (var соединение = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={путьБазы}"))
        {
            await соединение.OpenAsync();
        }

        var профиль = new SharedClientProfile(_каталог);

        профиль.Сохранить(new SharedClientSettings(
            "192.168.1.117",
            30242,
            "устройство-1",
            "сервер-1",
            "AB12CD34"));

        using var среда = new ComAutomationEnvironment(путьБазы, null, _каталог);

        var загружена = await среда.SafeLoadDatabaseAsync();

        Assert.False(загружена);
        Assert.False(среда.IsDatabaseLoaded);
    }
}
