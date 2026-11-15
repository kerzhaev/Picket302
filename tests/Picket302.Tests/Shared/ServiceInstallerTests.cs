using Picket302.Service.Hosting;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Разбор кодов ошибок Windows при регистрации и снятии регистрации службы.
/// </summary>
/// <remarks>
/// Проверка не вызывает диспетчер служб: права администратора в контуре
/// прогонов не выдаются, а решение о повторной регистрации принимается по
/// коду ошибки, который Windows возвращает при попытке создать уже
/// существующую службу.
/// </remarks>
public sealed class ServiceInstallerTests
{
    [Fact]
    public void КодСлужбыУжеСуществует_РаспознаётсяКакПовторнаяРегистрация()
    {
        Assert.True(ServiceInstaller.УжеЗарегистрирована(ServiceInstaller.ErrorServiceExists));
        Assert.False(ServiceInstaller.НеЗарегистрирована(ServiceInstaller.ErrorServiceExists));
    }

    [Fact]
    public void КодСлужбыНет_РаспознаётсяКакОтсутствиеСлужбы()
    {
        Assert.True(ServiceInstaller.НеЗарегистрирована(ServiceInstaller.ErrorServiceDoesNotExist));
        Assert.False(ServiceInstaller.УжеЗарегистрирована(ServiceInstaller.ErrorServiceDoesNotExist));
    }

    [Fact]
    public void ПрочиеКодыОшибок_НеВыдаютсяЗаПовторнуюРегистрацию()
    {
        Assert.False(ServiceInstaller.УжеЗарегистрирована(5));
        Assert.False(ServiceInstaller.НеЗарегистрирована(5));
    }

    [Fact]
    public void КонстантыСлужбы_СоответствуютУстановщику()
    {
        // Имя службы используется установщиком в командах sc.exe и должно
        // совпадать с именем, под которым служба регистрируется в SCM.
        Assert.Equal("Picket302Service", WindowsServiceRunner.ИмяСлужбы);
        Assert.Equal("NT AUTHORITY\\NetworkService", ServiceInstaller.УчётнаяЗапись);
    }
}
