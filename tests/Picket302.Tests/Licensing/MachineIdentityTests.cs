using Picket302.Domain.Licensing;
using Picket302.Infrastructure.Licensing;
using Xunit;

namespace Picket302.Tests.Licensing;

/// <summary>
/// Проверка вычисления идентификатора оборудования (ТЗ §8.10, функция 120:
/// «Вычисляет аппаратный идентификатор несколькими способами (серийный номер
/// тома, UUID платы, составной отпечаток), результат — в реестр HKCU»).
/// </summary>
public sealed class MachineIdentityTests
{
    [Fact]
    public void GetMachineId_ПовторныеВызовы_ВозвращаютОдноЗначение()
    {
        using var среда = new Среда();

        var первый = среда.Машина.GetMachineId(MachineIdVariant.Composite);
        var второй = среда.Машина.GetMachineId(MachineIdVariant.Composite);

        Assert.Equal(первый, второй);
        Assert.False(string.IsNullOrWhiteSpace(первый));
    }

    [Fact]
    public void GetMachineId_НовыйЭкземпляр_БерётЗначениеИзХранилища()
    {
        using var среда = new Среда();

        var ожидаемое = среда.Машина.GetMachineId(MachineIdVariant.Composite);

        var повторный = new MachineIdentity(журнал: null, реестр: среда.Реестр).GetMachineId(MachineIdVariant.Composite);

        Assert.Equal(ожидаемое, повторный);
    }

    [Fact]
    public void GetMachineId_РазныеВарианты_ДаютРазныеЗначения()
    {
        using var среда = new Среда();

        var том = среда.Машина.GetMachineId(MachineIdVariant.VolumeSerial);
        var плата = среда.Машина.GetMachineId(MachineIdVariant.BoardUuid);
        var составной = среда.Машина.GetMachineId(MachineIdVariant.Composite);

        Assert.NotEqual(том, плата);
        Assert.NotEqual(плата, составной);
    }

    [Fact]
    public void GetMachineId_СоставнойОтпечаток_ШестьдесятЧетыреHexСимвола()
    {
        using var среда = new Среда();

        var значение = среда.Машина.GetMachineId(MachineIdVariant.Composite);

        Assert.Equal(64, значение.Length);
        Assert.All(значение, символ => Assert.True(Uri.IsHexDigit(символ)));
    }

    [Fact]
    public void GetMachineId_ВсеВарианты_ИмеютФорматSha256()
    {
        using var среда = new Среда();

        foreach (var вариант in Enum.GetValues<MachineIdVariant>())
        {
            var значение = среда.Машина.GetMachineId(вариант);

            Assert.Equal(64, значение.Length);
            Assert.All(значение, символ => Assert.True(Uri.IsHexDigit(символ)));
        }
    }

    [Fact]
    public void GetMachineId_ЗначениеСохраняетсяВХранилищеЗначений()
    {
        using var среда = new Среда();

        var значение = среда.Машина.GetMachineId(MachineIdVariant.Composite);

        Assert.Equal(значение, среда.Реестр.Прочитать(MachineIdentity.ИмяСоставногоОтпечатка));
    }

    /// <summary>
    /// Изолированное окружение проверки: временный каталог и файловое
    /// хранилище значений вместо ветки реестра пользователя.
    /// </summary>
    private sealed class Среда : IDisposable
    {
        public Среда()
        {
            Каталог = Path.Combine(Path.GetTempPath(), "picket302_оборудование_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Каталог);

            Реестр = LicensingRegistry.File(Каталог);
            Машина = new MachineIdentity(журнал: null, реестр: Реестр);
        }

        public string Каталог { get; }

        public LicensingRegistry Реестр { get; }

        public MachineIdentity Машина { get; }

        public void Dispose()
        {
            if (Directory.Exists(Каталог))
            {
                Directory.Delete(Каталог, recursive: true);
            }
        }
    }
}
