using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Общая коллекция сериализации UI- и профильных тестов слоя App.
/// </summary>
/// <remarks>
/// <para>
/// Классы этой коллекции создают WinForms-формы, составы
/// <c>AppServices</c> или изолированные профили. Конструирование форм
/// на нескольких потоках одновременно и глобальные операции
/// SQLite-пула (<c>SqliteConnection.ClearAllPools</c> в утилизации
/// профиля) при параллельном прогоне коллекций давали редкий
/// order-dependent сбой конструктора формы — воспроизведён при
/// совместном прогоне ResponsiveUiContractTests и ProfileIsolationTests.
/// </para>
/// <para>
/// Коллекция ограничена слоем App: доменные и инфраструктурные тесты,
/// не создающие форм и профилей, продолжают выполняться параллельно.
/// </para>
/// </remarks>
[CollectionDefinition("WinFormsПрофиль")]
public sealed class WinFormsПрофильОпределение
{
}
