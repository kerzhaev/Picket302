using Picket302.Infrastructure.Persistence;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверки справочника серверов БД и менеджера подключения (ТЗ §8.8,
/// функции 101 и 102; §8.11, функция 127).
/// </summary>
public sealed class ServerRegistryTests : IDisposable
{
    private readonly string _временнаяПапка =
        Path.Combine(Path.GetTempPath(), "Picket302ServerRegistry_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Создаёт тестовый каталог.
    /// </summary>
    public ServerRegistryTests()
    {
        Directory.CreateDirectory(_временнаяПапка);
    }

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
    public void СписокСерверов_ЧитаетсяИзФайлаServersIni()
    {
        var путь = Path.Combine(_временнаяПапка, "servers.ini");

        File.WriteAllLines(путь,
        [
            "[Servers]",
            "Главный=192.168.0.10;5432;Сервер головного офиса",
            "Резервный=backup.local;5433;",
        ]);

        var реестр = new ServerRegistry(путь);

        var серверы = реестр.ListServers();

        Assert.Equal(2, серверы.Count);
        Assert.Equal("Главный", серверы[0].Name);
        Assert.Equal("192.168.0.10", серверы[0].Host);
        Assert.Equal(5432, серверы[0].Port);
        Assert.Equal("Сервер головного офиса", серверы[0].Description);
        Assert.Equal("Резервный", серверы[1].Name);
        Assert.Equal(5433, серверы[1].Port);
    }

    [Fact]
    public void СписокСерверов_ПустойФайлВозвращаетПустойПеречень()
    {
        var реестр = new ServerRegistry(Path.Combine(_временнаяПапка, "servers.ini"));

        Assert.Empty(реестр.ListServers());
    }

    [Fact]
    public void СохранениеСерверов_ЗатемЧтениеВозвращаетТежеЗаписи()
    {
        var путь = Path.Combine(_временнаяПапка, "servers.ini");
        var реестр = new ServerRegistry(путь);

        реестр.AddServer(new ServerEntry("Основной", "10.0.0.5", 5432, "Основной сервер"));

        var сервер = Assert.Single(реестр.ListServers());

        Assert.Equal("Основной", сервер.Name);
        Assert.Equal("10.0.0.5", сервер.Host);
        Assert.Equal(5432, сервер.Port);
    }

    [Fact]
    public void ПроверкаСервера_НедоступныйСерверДаётПонятноеСообщение()
    {
        var реестр = new ServerRegistry(Path.Combine(_временнаяПапка, "servers.ini"));

        var результат = реестр.CheckServer(new ServerEntry("Недоступный", "127.0.0.1", 1, string.Empty));

        Assert.True(результат.IsFailure);
        Assert.Contains("Не удалось подключиться к серверу БД Недоступный:1", результат.Error, StringComparison.Ordinal);
        Assert.Contains("брандмауэра", результат.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ПроверкаСервера_ПустойАдресДаётОшибку()
    {
        var реестр = new ServerRegistry(Path.Combine(_временнаяПапка, "servers.ini"));

        var результат = реестр.CheckServer(new ServerEntry("Пустой", string.Empty, 5432, string.Empty));

        Assert.True(результат.IsFailure);
        Assert.Contains("Не задан адрес сервера", результат.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ДиагностикаТопологии_ЛокальныйРежимОпределяется()
    {
        var реестр = new ServerRegistry(Path.Combine(_временнаяПапка, "servers.ini"));

        var топология = реестр.DiagnoseTopology();

        Assert.Equal(TopologyRole.LocalOnly, топология.Role);
        Assert.False(топология.IsConnected);
    }

    [Fact]
    public void СравнениеСхем_РазныеВерсииДаютБлокировку()
    {
        var описание = ServerRegistry.СравнитьСхемы("006", "007");

        Assert.NotNull(описание);
        Assert.Contains("Несовпадение версий схемы", описание, StringComparison.Ordinal);
        Assert.Contains("006", описание, StringComparison.Ordinal);
        Assert.Contains("007", описание, StringComparison.Ordinal);
    }

    [Fact]
    public void СравнениеСхем_ОдинаковыеВерсииНеДаютБлокировки()
    {
        Assert.Null(ServerRegistry.СравнитьСхемы("006", "006"));
        Assert.Null(ServerRegistry.СравнитьСхемы(null, "006"));
    }
}

/// <summary>
/// Проверки менеджера подключения: защита пароля, запрет кириллицы, смена
/// подключения (ТЗ §8.8, функция 102; §8.11, функция 127).
/// </summary>
public sealed class ConnectionManagerTests : IDisposable
{
    private readonly string _временнаяПапка =
        Path.Combine(Path.GetTempPath(), "Picket302Connection_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Создаёт тестовый каталог.
    /// </summary>
    public ConnectionManagerTests()
    {
        Directory.CreateDirectory(_временнаяПапка);
    }

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
    public void КириллицаВИмениБазы_БлокируетСохранение()
    {
        var менеджер = СоздатьМенеджер();

        var реквизиты = new ConnectionDescriptor(
            DatabaseKind.ServerPostgres,
            "10.0.0.1",
            5432,
            "Почта_Краснодар",
            "operator",
            null,
            string.Empty);

        var проверка = менеджер.Validate(реквизиты);

        Assert.True(проверка.IsFailure);
        Assert.Equal(ConnectionManager.CyrillicDatabaseNameMessage, проверка.Error);
        Assert.Throws<InvalidOperationException>(() => менеджер.SwitchConnection(реквизиты));
    }

    [Fact]
    public void КириллицаВПутиКБазе_БлокируетСохранение()
    {
        var менеджер = СоздатьМенеджер();

        var реквизиты = ConnectionDescriptor.Local(@"C:\Данные\Почта\picket302.db");

        var проверка = менеджер.Validate(реквизиты);

        Assert.True(проверка.IsFailure);
        Assert.Equal(ConnectionManager.CyrillicDatabasePathMessage, проверка.Error);
    }

    [Fact]
    public void ПарольСохраняетсяВЗащищённомВиде()
    {
        var менеджер = СоздатьМенеджер();

        менеджер.SwitchConnectionWithPassword(
            new ConnectionDescriptor(
                DatabaseKind.ServerPostgres,
                "10.0.0.1",
                5432,
                "picket",
                "operator",
                null,
                string.Empty),
            "тайный-пароль");

        var текст = File.ReadAllText(менеджер.SettingsPath);

        Assert.DoesNotContain("тайный-пароль", текст, StringComparison.Ordinal);
        Assert.Equal("тайный-пароль", менеджер.GetPassword());
    }

    [Fact]
    public void СменаПодключенияТребуетПерезапуска()
    {
        var менеджер = СоздатьМенеджер();

        Assert.False(менеджер.RequiresRestart);

        менеджер.SwitchConnection(ConnectionDescriptor.Local(@"C:\Data\picket302.db"));

        Assert.True(менеджер.RequiresRestart);
    }

    [Fact]
    public void ИмяБазыМеняетсяТолькоДоПервогоПодключения()
    {
        var менеджер = СоздатьМенеджер();

        менеджер.SwitchConnection(new ConnectionDescriptor(
            DatabaseKind.ServerPostgres,
            "10.0.0.1",
            5432,
            "picket",
            "operator",
            null,
            string.Empty));

        менеджер.MarkConnectionEstablished();

        Assert.True(менеджер.IsFirstConnectionDone);

        var исключение = Assert.Throws<InvalidOperationException>(() =>
            менеджер.SwitchConnection(new ConnectionDescriptor(
                DatabaseKind.ServerPostgres,
                "10.0.0.1",
                5432,
                "picket2",
                "operator",
                null,
                string.Empty)));

        Assert.Equal(ConnectionManager.DatabaseNameLockedMessage, исключение.Message);
    }

    [Fact]
    public void ПовреждённыйФайлНастроек_ДаётЛокальныйРежим()
    {
        var путь = Path.Combine(_временнаяПапка, "connection.json");

        File.WriteAllText(путь, "{ это не json");

        var менеджер = new ConnectionManager(путь, _временнаяПапка);

        Assert.True(менеджер.Current.IsLocal);
    }

    [Fact]
    public void ОпределениеКириллицы_РаботаетПоСимволам()
    {
        Assert.True(ConnectionManager.СодержитКириллицу("Почта"));
        Assert.False(ConnectionManager.СодержитКириллицу("Picket302"));
        Assert.False(ConnectionManager.СодержитКириллицу(null));
    }

    /// <summary>
    /// Создаёт менеджер подключения во временном каталоге.
    /// </summary>
    private ConnectionManager СоздатьМенеджер() => new(
        Path.Combine(_временнаяПапка, "connection.json"),
        Path.Combine(_временнаяПапка, "secrets"));
}
