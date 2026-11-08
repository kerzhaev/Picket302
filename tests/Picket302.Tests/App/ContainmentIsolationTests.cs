using Microsoft.Win32;
using Picket302.App;
using Picket302.Domain.Services;
using Picket302.Infrastructure;
using Picket302.Infrastructure.Registry;
using Picket302.Printing.Devices;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.App;

/// <summary>
/// Контейнирование acceptance-записей за пределами изолированного профиля
/// (stage3.1, ENV3-02-соседние обходы): состав приложения в изолированном
/// режиме не пишет ни в реальный профиль, ни в реестр машины, а принтерный
/// менеджер подменяется для сценария «нет принтера» без изменения системы.
/// </summary>
[Collection("WinFormsПрофиль")]
public sealed class ContainmentIsolationTests
{
    /// <summary>Ветка реестра текущего отправителя производственной установки.</summary>
    private const string ВеткаРеестраОтправителя = WindowsCurrentSenderRegistryStore.RegistryPath;

    [Fact]
    public async Task ПодменённыйПринтерныйМенеджер_ВозвращаетNullВместоПринтера()
    {
        // Реальный стенд имеет хотя бы один принтер: null доказывает, что
        // используется подмена, а не системный принтер.
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(
            профиль.Пути,
            создатьДемоБазу: false,
            службаПринтеров: new ПустойПринтерныйМенеджер());

        Assert.Null(состав.СлужбаПринтеров.GetDefaultPrinter());
    }

    [Fact]
    public async Task СоставПоУмолчанию_ИспользуетСистемныйПринтерныйМенеджер()
    {
        // Производственное поведение не меняется: без подмены менеджер
        // возвращает реальный принтер по умолчанию (или null, если его нет).
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути);

        var принтер = состав.СлужбаПринтеров.GetDefaultPrinter();

        // Сверка с прямым чтением системы: состав не добавляет собственной
        // логики поверх winspool.
        var прямой = new PrinterManager().GetDefaultPrinter();

        Assert.Equal(прямой?.Name, принтер?.Name);
    }

    [Fact]
    public async Task ИзолированныйРеестрОтправителя_НеПишетВРеальныйРеестр()
    {
        // Реестр — общий ресурс машины: межпроцессная блокировка защищает
        // чтение до/после от параллельной активной сессии.
        using var блокировка = БлокировкаОбщихРесурсов.Захватить();

        var значениеДо = ПрочитатьSenderId();

        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(
            профиль.Пути,
            создатьДемоБазу: false,
            реестрОтправителяИзолирован: true);

        // Засев отправителя — ровно та операция acceptance-сеяния, которая
        // раньше писала SenderId в HKCU\Software\PEPro\CurrentSender.
        состав.ТекущийОтправитель.SetGlobalSender(12345);

        var значениеПосле = ПрочитатьSenderId();

        Assert.Equal(значениеДо, значениеПосле);

        // Круговорот работает без реестра: значение читается из таблицы
        // параметров изолированной базы.
        Assert.Equal(12345L, состав.ТекущийОтправитель.CurrentSenderId);
    }

    [Fact]
    public async Task РеквизитыПодключения_ХранятсяТолькоВПутиПрофиля()
    {
        // Контейнирование обхода MainShell → new ConnectionManager(): состав
        // обязан держать файл реквизитов в ЯВНОМ пути профиля, а не в
        // производственном %AppData%\Picket302\connection.json.
        using var профиль = new ИзолированныйПрофиль();

        await using var состав = await AppServices.СоздатьAsync(профиль.Пути);

        Assert.Equal(профиль.Пути.ConnectionSettingsPath, состав.МенеджерПодключения.SettingsPath);
        Assert.StartsWith(профиль.Корень, состав.МенеджерПодключения.SettingsPath, StringComparison.Ordinal);
    }

    [Fact]
    public void ПустоеХранилищеОтправителя_КонтрактБезОпераций()
    {
        var хранилище = new NullCurrentSenderStore();

        хранилище.Write(42);
        хранилище.Write(null);

        Assert.Null(хранилище.Read());
    }

    /// <summary>Читает SenderId реального реестра (только чтение).</summary>
    private static long? ПрочитатьSenderId()
    {
        using var ветка = Registry.CurrentUser.OpenSubKey(ВеткаРеестраОтправителя);

        if (ветка?.GetValue(WindowsCurrentSenderRegistryStore.ValueName) is not string значение)
        {
            return null;
        }

        return long.TryParse(значение, out var идентификатор) ? идентификатор : null;
    }

    /// <summary>Подменяемый принтерный менеджер acceptance-сценария.</summary>
    private sealed class ПустойПринтерныйМенеджер : PrinterManager
    {
        public override PrinterDescriptor? GetDefaultPrinter() => null;
    }
}
