using Microsoft.Win32;

using Picket302.Infrastructure.Shell;

using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка интеграции с оболочкой Windows: пункт контекстного меню Проводника
/// (ТЗ §8.10, функция 106; §10.6, функция 135).
/// </summary>
/// <remarks>
/// <para>
/// Проверка выполняется в отдельном тестовом подразделе реестра
/// (<c>Picket302Test…</c>) с уникальным суффиксом: записи, созданные тестом,
/// удаляются по его завершению.
/// </para>
/// <para>
/// Если запись в реестр недоступна (нет прав на ветку профиля), тест не падает:
/// проверяется ожидаемое сообщение об отказе доступа.
/// </para>
/// </remarks>
public sealed class ExplorerIntegrationTests
{
    private const string ПутьОболочки = @"C:\Program Files\Picket302\Picket302.App.exe";

    /// <summary>
    /// Команда запуска оболочки передаёт выбранный объект параметром импорта.
    /// </summary>
    [Fact]
    public void КомандаЗапуска_ПередаётВыбранныйОбъектПараметромИмпорта()
    {
        var команда = ExplorerIntegration.BuildCommand(ПутьОболочки);

        Assert.Equal($"\"{ПутьОболочки}\" --import \"%1\"", команда);
        Assert.Contains("--import", команда, StringComparison.Ordinal);
        Assert.Contains("\"%1\"", команда, StringComparison.Ordinal);
    }

    /// <summary>
    /// Пути регистрации пункта меню: ветка файлов и ветка папок.
    /// </summary>
    [Fact]
    public void ПутиРегистрации_СодержатВеткуФайловИВеткуПапок()
    {
        var пути = ExplorerIntegration.BuildRegistryPaths("Picket302Import");

        Assert.Equal(2, пути.Count);
        Assert.Contains(@"*\shell\Picket302Import", пути);
        Assert.Contains(@"Directory\shell\Picket302Import", пути);
    }

    /// <summary>
    /// Регистрация создаёт записи в ветке пользователя; удаление убирает их.
    /// </summary>
    [Fact]
    public void РегистрацияПишетКлючиВПрофилеПользователя_ИУдаляетИх()
    {
        var имяПункта = "Picket302Test" + Guid.NewGuid().ToString("N")[..8];
        var интеграция = new ExplorerIntegration(ПутьОболочки, имяПункта);

        try
        {
            интеграция.RegisterShellMenu();
        }
        catch (ShellIntegrationException ошибка)
        {
            Assert.Contains("HKCU", ошибка.Message, StringComparison.Ordinal);

            return;
        }

        try
        {
            Assert.True(интеграция.IsRegistered());

            using var пункт = Registry.CurrentUser.OpenSubKey(
                $@"{ExplorerIntegration.КореньКлассов}\*\shell\{имяПункта}");

            Assert.NotNull(пункт);
            Assert.Equal(ExplorerIntegration.НазваниеПунктаПоУмолчанию, пункт!.GetValue("MUIVerb"));
            Assert.Equal("csv;xls;xlsx;xml", пункт.GetValue("Extensions"));

            using var команда = пункт.OpenSubKey("command");
            Assert.NotNull(команда);
            Assert.Equal(ExplorerIntegration.BuildCommand(ПутьОболочки), команда!.GetValue(""));

            using var папка = Registry.CurrentUser.OpenSubKey(
                $@"{ExplorerIntegration.КореньКлассов}\Directory\shell\{имяПункта}");

            Assert.NotNull(папка);
        }
        finally
        {
            try
            {
                интеграция.UnregisterShellMenu();
            }
            catch (ShellIntegrationException)
            {
                // Удаление тестовых ключей не должно ломать тест.
            }
        }

        Assert.False(интеграция.IsRegistered());
    }

    /// <summary>
    /// Отказ доступа к ветке пользователя сопровождается сообщением о том, от
    /// имени какого пользователя следует запустить программу.
    /// </summary>
    [Fact]
    public void СообщениеОбОтказеДоступа_ОбъясняетНеобходимостьЗапускаОтИмениПользователя()
    {
        Assert.Contains("HKCU", ExplorerIntegration.СообщениеОбОтказеДоступа, StringComparison.Ordinal);
        Assert.Contains(
            "от имени пользователя",
            ExplorerIntegration.СообщениеОбОтказеДоступа,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Пункт меню не зарегистрирован до регистрации: проверка состояния
    /// выполняется по отдельному тестовому подразделу.
    /// </summary>
    [Fact]
    public void ДоРегистрации_ПунктОтсутствует()
    {
        var имяПункта = "Picket302Test" + Guid.NewGuid().ToString("N")[..8];
        var интеграция = new ExplorerIntegration(ПутьОболочки, имяПункта);

        Assert.False(интеграция.IsRegistered());
    }
}
