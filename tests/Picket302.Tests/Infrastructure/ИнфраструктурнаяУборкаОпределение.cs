using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Общая коллекция сериализации reparse-тестов гарда изоляции и
/// постусловия чистоты временных каталогов.
/// </summary>
/// <remarks>
/// Тесты <see cref="ProfileIsolationGuardTests"/> создают и удаляют
/// каталоги <c>%TEMP%\P302GuardTest_*</c>; постусловие чистоты
/// (<c>ZTempCleanupPostconditionTests</c>) обязано выполняться в той же
/// коллекции, чтобы его проверка не гонялась параллельно с созданием
/// таких каталогов.
/// </remarks>
[CollectionDefinition("ИнфраструктурнаяУборка")]
public sealed class ИнфраструктурнаяУборкаОпределение
{
}
