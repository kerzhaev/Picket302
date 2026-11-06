namespace Picket302.Infrastructure;

/// <summary>
/// Гард изоляции профиля для acceptance- и тестовых контуров: проверяет,
/// что рассчитанные пути профиля не совпадают с производственным каталогом
/// пользователя.
/// </summary>
/// <remarks>
/// <para>
/// Helper сознательно не запрещает рабочему приложению использовать
/// собственный профиль: проверка вызывается только изолированными
/// контурами (acceptance harness, тестовые оболочки) до создания
/// состава и каталогов.
/// </para>
/// <para>
/// Сравнение выполняется по нормализованным полным путям
/// (регистронезависимо, без завершающих разделителей), потому что
/// <see cref="Picket302ProfilePaths.ДляКорня"/> добавляет к корню каталог
/// приложения: корень, совпадающий с <em>родителем</em> производственного
/// каталога, превращается в производственный профиль.
/// </para>
/// </remarks>
public static class ProfileIsolationGuard
{
    /// <summary>
    /// Возвращает производственный каталог данных приложения,
    /// нормализованный тем же способом, что и проверяемые пути.
    /// </summary>
    /// <returns>Полный путь без завершающих разделителей.</returns>
    public static string ПроизводственныйКаталог() =>
        Нормализовать(Picket302ProfilePaths.ПоУмолчанию().DataDirectory);

    /// <summary>
    /// Проверяет изоляцию путей профиля от производственного каталога.
    /// </summary>
    /// <param name="пути">Рассчитанные пути профиля.</param>
    /// <returns>Сообщение об отказе либо <see langword="null"/> — изоляция соблюдена.</returns>
    public static string? ПричинаОтказа(Picket302ProfilePaths пути)
    {
        ArgumentNullException.ThrowIfNull(пути);

        return ПричинаОтказа(пути.DataDirectory, ПроизводственныйКаталог());
    }

    /// <summary>
    /// Проверяет, что нормализованный каталог данных совпадает с
    /// производственным.
    /// </summary>
    /// <param name="каталогДанных">Проверяемый каталог данных.</param>
    /// <param name="производственный">Нормализованный производственный каталог.</param>
    /// <returns>Сообщение об отказе либо <see langword="null"/>.</returns>
    public static string? ПричинаОтказа(string каталогДанных, string производственный)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогДанных);
        ArgumentException.ThrowIfNullOrWhiteSpace(производственный);

        var проверяемый = Нормализовать(каталогДанных);

        return string.Equals(проверяемый, производственный, StringComparison.OrdinalIgnoreCase)
            ? $"Каталог данных профиля {проверяемый} совпадает с производственным {производственный}."
            : null;
    }

    /// <summary>Нормализует путь: полный, без завершающих разделителей.</summary>
    /// <param name="путь">Исходный путь.</param>
    /// <returns>Нормализованный путь.</returns>
    public static string Нормализовать(string путь) =>
        Path.GetFullPath(путь).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
