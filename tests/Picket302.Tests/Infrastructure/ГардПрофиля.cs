using Picket302.Infrastructure;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Гард изоляции профиля: любой тестовый или acceptance-контур обязан
/// работать с профилем, отличным от производственного каталога
/// <c>%AppData%\Picket302</c>. Отказ — понятное исключение до создания
/// состава, а не запись в реальный профиль.
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

        var производственный = Нормализовать(Picket302ProfilePaths.ПоУмолчанию().DataDirectory);
        var проверяемый = Нормализовать(пути.DataDirectory);

        if (string.Equals(производственный, проверяемый, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Профиль изоляции совпадает с производственным "
                + $"{производственный}. Тестовые и acceptance-контуры обязаны "
                + "работать с временным каталогом профиля.");
        }
    }

    private static string Нормализовать(string путь) =>
        Path.GetFullPath(путь).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
