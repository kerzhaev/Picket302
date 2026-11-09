using System.Windows.Forms;
using Picket302.App;
using Picket302.Infrastructure.Persistence;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Регрессия ENV3-02: диалог «Открыть базу…», созданный для оболочки с
/// изолированным составом, обязан читать реквизиты и справочник серверов из
/// того же профиля, а не из production %AppData%.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class MainShellConnectionProfileTests
{
    [Fact]
    public void ДиалогПодключения_ДоИнициализации_ОтказываетБезПроизводственногоFallback()
    {
        using var окно = new MainShell();

        var ошибка = Assert.Throws<InvalidOperationException>(
            () => окно.СоздатьДиалогПодключения());

        Assert.Contains("до инициализации состава", ошибка.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ДиалогПодключения_ИспользуетПрофильПереданногоСостава()
    {
        using var профиль = new ИзолированныйПрофиль();

        var реестр = new ServerRegistry(профиль.Пути.ServerRegistryPath);
        реестр.SaveServers(
        [
            new ServerEntry(
                "Изолированный сервер",
                "127.0.0.1",
                55432,
                "Только для проверки профиля"),
        ]);

        await using var состав = await AppServices.СоздатьAsync(
            профиль.Пути,
            создатьДемоБазу: false);

        using var окно = new MainShell(состав);
        using var диалог = окно.СоздатьДиалогПодключения();

        Assert.Equal(профиль.Пути.DatabasePath, диалог.Descriptor.DatabasePath);

        var списокСерверов = Обойти(диалог)
            .OfType<ComboBox>()
            .Single(поле => поле.Items.Cast<object>()
                .OfType<ServerEntry>()
                .Any(сервер => сервер.Host == "127.0.0.1"));

        var серверВДиалоге = списокСерверов.Items
            .Cast<object>()
            .OfType<ServerEntry>()
            .Single(сервер => сервер.Name == "Изолированный сервер");

        Assert.Equal(55432, серверВДиалоге.Port);
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
