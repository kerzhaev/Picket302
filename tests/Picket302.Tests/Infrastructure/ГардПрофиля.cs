using Picket302.Infrastructure;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Гард изоляции профиля тестов: делегирует общему production-safe
/// <see cref="ProfileIsolationGuard"/>; отказ — понятное исключение до
/// создания состава, а не запись в реальный профиль.
/// </summary>
public static class ГардПрофиля
{
    /// <summary>
    /// Проверяет, что пути профиля не совпадают с производственными.
    /// </summary>
    /// <param name="пути">Проверяемые пути профиля.</param>
    /// <exception cref="InvalidOperationException">
    /// Каталог данных профиля совпадает с производственным.
    /// </exception>
    public static void ГарантироватьИзоляцию(Picket302ProfilePaths пути)
    {
        ArgumentNullException.ThrowIfNull(пути);

        var причина = ProfileIsolationGuard.ПричинаОтказа(пути);

        if (причина is not null)
        {
            throw new InvalidOperationException(причина);
        }
    }
}
