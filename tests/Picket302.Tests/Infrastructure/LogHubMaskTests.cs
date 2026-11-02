using Picket302.Infrastructure.Logging;
using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Регрессия F-C4: поиск текущего файла журнала. Прежде маска строилась
/// как «app--*» (дефис шаблона «app-.log» не срезался) и не совпадала
/// с фактическим именем Serilog «app-20261102.log».
/// </summary>
public sealed class LogHubMaskTests
{
    [Fact]
    public void НайтиТекущийФайл_ФайлSerilogВКаталоге_Находится()
    {
        var каталог = Path.Combine(Path.GetTempPath(), "picket302-логи-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(каталог);

            var файл = Path.Combine(каталог, "app-20261102.log");
            File.WriteAllText(файл, "запись журнала");

            var метод = typeof(LogHub).GetMethod(
                "НайтиТекущийФайл",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(метод);

            var найденный = (string?)метод!.Invoke(null, [Path.Combine(каталог, "app-.log")]);

            Assert.Equal(файл, найденный);
        }
        finally
        {
            if (Directory.Exists(каталог))
            {
                Directory.Delete(каталог, recursive: true);
            }
        }
    }
}
