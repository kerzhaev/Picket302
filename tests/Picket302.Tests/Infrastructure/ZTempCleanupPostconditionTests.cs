using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Постусловие прогона reparse-тестов: каталоги
/// <c>%TEMP%\P302GuardTest_*</c> не остаются после тестов гарда.
/// </summary>
/// <remarks>
/// Класс назван с префиксом «Z»: внутри коллекции xUnit упорядочивает
/// тесты по имени, поэтому постусловие выполняется после
/// <see cref="ProfileIsolationGuardTests"/>, но до классов с кириллическими
/// именами. Промежуточная проверка дополняет собственную проверку
/// отсутствия базы в каждом reparse-тесте (fail-closed cleanup).
/// </remarks>
[Collection("ИнфраструктурнаяУборка")]
public sealed class ZTempCleanupPostconditionTests
{
    /// <summary>
    /// После reparse-тестов гарда не остаётся ни одного каталога
    /// <c>P302GuardTest_*</c> в %TEMP%.
    /// </summary>
    [Fact]
    public void Прогон_ГардаНеОставляетКаталоговP302GuardTest()
    {
        var остатки = Directory.GetDirectories(Path.GetTempPath(), "P302GuardTest_*");

        Assert.Empty(остатки);
    }
}
