using Picket302.Printing.Devices;
using Xunit;
using Xunit.Abstractions;

namespace Picket302.Tests.Printing;

public sealed class TempEnumProbe
{
    private readonly ITestOutputHelper _вывод;

    public TempEnumProbe(ITestOutputHelper вывод) => _вывод = вывод;

    [Fact]
    public void Перечислить_принтеры_не_падает()
    {
        var менеджер = new PrinterManager();
        var принтеры = менеджер.EnumeratePrinters();
        _вывод.WriteLine($"принтеров: {принтеры.Count}");
        foreach (var п in принтеры)
        {
            _вывод.WriteLine($"  {п.Name} / {п.PortName} / {п.IsDefault}");
        }
    }
}
