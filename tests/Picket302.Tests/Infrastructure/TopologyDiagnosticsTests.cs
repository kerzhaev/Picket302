using Picket302.Infrastructure.Persistence;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Диагностика клиент-серверной топологии: роль текущей машины и состояние
/// связи (ТЗ §8.11, функция 127).
/// </summary>
public sealed class TopologyDiagnosticsTests : IDisposable
{
    private readonly string _временнаяПапка =
        Path.Combine(Path.GetTempPath(), "Picket302Topology_" + Guid.NewGuid().ToString("N"));

    public TopologyDiagnosticsTests()
    {
        Directory.CreateDirectory(_временнаяПапка);
    }

    [Fact]
    public void КлиентБезСервера_ЛокальнаяБаза_РольЛокальная()
    {
        var реестр = new ServerRegistry(Path.Combine(_временнаяПапка, "servers.ini"));

        var топология = реестр.DiagnoseTopology(
            ConnectionDescriptor.Local(Path.Combine(_временнаяПапка, "picket302.db")),
            подключённыхКлиентов: 0);

        Assert.Equal(TopologyRole.LocalOnly, топология.Role);
        Assert.False(топология.IsConnected);
        Assert.Contains("Локальный режим", топология.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void СерверБезКлиентов_ГоловнойОфис_РольСервера()
    {
        var реестр = new ServerRegistry(Path.Combine(_временнаяПапка, "servers.ini"));

        var топология = реестр.DiagnoseTopology(
            new ConnectionDescriptor(
                DatabaseKind.ServerPostgres,
                "localhost",
                5432,
                "picket302",
                "picket302",
                null,
                string.Empty),
            подключённыхКлиентов: 0);

        Assert.Equal(TopologyRole.ServerWithoutClients, топология.Role);
        Assert.True(топология.IsConnected);
        Assert.Contains("клиенты не подключены", топология.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void СерверСКлиентами_РольСервераСКлиентами()
    {
        var реестр = new ServerRegistry(Path.Combine(_временнаяПапка, "servers.ini"));

        var топология = реестр.DiagnoseTopology(
            new ConnectionDescriptor(
                DatabaseKind.ServerPostgres,
                "127.0.0.1",
                5432,
                "picket302",
                "picket302",
                null,
                string.Empty),
            подключённыхКлиентов: 3);

        Assert.Equal(TopologyRole.ServerWithClients, топология.Role);
        Assert.True(топология.IsConnected);
        Assert.Contains("подключено клиентов — 3", топология.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void КлиентОбщейБазы_РольКлиента_СерверНедоступен()
    {
        var реестр = new ServerRegistry(Path.Combine(_временнаяПапка, "servers.ini"));

        var топология = реестр.DiagnoseTopology(
            new ConnectionDescriptor(
                DatabaseKind.ServerPostgres,
                "192.0.2.10",
                5432,
                "picket302",
                "picket302",
                null,
                string.Empty),
            подключённыхКлиентов: 0);

        Assert.Equal(TopologyRole.ClientWithServer, топология.Role);
        Assert.False(топология.IsConnected);
        Assert.Contains("сервер не найден в справочнике", топология.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_временнаяПапка))
        {
            Directory.Delete(_временнаяПапка, recursive: true);
        }
    }
}
