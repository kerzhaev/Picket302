using System.Runtime.InteropServices;

namespace Picket302.Infrastructure;

/// <summary>
/// Гард изоляции профиля для acceptance- и тестовых контуров: проверяет,
/// что путь кандидата не совпадает с производственным каталогом, не лежит
/// внутри него, не содержит его и не ведёт к нему через переопределение
/// (symlink/junction/reparse point).
/// </summary>
/// <remarks>
/// <para>
/// Helper сознательно не запрещает рабочему приложению использовать
/// собственный профиль: проверка вызывается только изолированными
/// контурами (acceptance harness, тестовые оболочки) до создания
/// состава, каталогов и окна.
/// </para>
/// <para>
/// Сравнение выполняется <em>по сегментам</em> нормализованных путей
/// без учёта регистра: строковый префикс «Picket302» есть и у
/// sibling-каталога «Picket302-acceptance», поэтому простой
/// <c>StartsWith</c> нельзя использовать. Кандидат, находящийся внутри
/// production (любая глубина), и production, находящийся внутри
/// кандидата (например, передан родительский каталог), отклоняются.
/// </para>
/// <para>
/// Reparse-безопасность: каждый существующий префикс пути кандидата и
/// production проверяется на признак
/// <see cref="FileAttributes.ReparsePoint"/>; ссылка разрешается к
/// конечному целевому объекту
/// (<see cref="FileSystemInfo.ResolveLinkTarget(bool)"/>), и сравнение
/// идёт уже по канонизированному пути. Если ссылку разрешить не
/// удалось, путь отклоняется (fail-closed), а не считается безопасным.
/// </para>
/// </remarks>
public static class ProfileIsolationGuard
{
    /// <summary>
    /// Возвращает производственный каталог данных приложения,
    /// канонизированный тем же способом, что и проверяемые пути.
    /// </summary>
    /// <returns>Канонизированный путь.</returns>
    public static string ПроизводственныйКаталог() =>
        Канонизировать(Picket302ProfilePaths.ПоУмолчанию().DataDirectory);

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
    /// Проверяет каталог данных кандидата против производственного.
    /// </summary>
    /// <param name="каталогДанных">Проверяемый каталог данных.</param>
    /// <param name="производственный">Производственный каталог данных.</param>
    /// <returns>Сообщение об отказе либо <see langword="null"/>.</returns>
    public static string? ПричинаОтказа(string каталогДанных, string производственный)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогДанных);
        ArgumentException.ThrowIfNullOrWhiteSpace(производственный);

        var кандидат = Канонизировать(каталогДанных);
        var эталон = Канонизировать(производственный);

        var сегментыКандидата = Разбить(кандидат);
        var сегментыЭталона = Разбить(эталон);

        if (СегментноеРавенство(сегментыКандидата, сегментыЭталона))
        {
            return $"Каталог данных профиля {кандидат} совпадает с производственным {эталон}.";
        }

        if (ЯвляетсяПрефиксом(сегментыЭталона, сегментыКандидата))
        {
            return
                $"Каталог данных профиля {кандидат} находится внутри производственного {эталон}.";
        }

        if (ЯвляетсяПрефиксом(сегментыКандидата, сегментыЭталона))
        {
            return
                $"Производственный каталог {эталон} находится внутри каталога профиля {кандидат}.";
        }

        return null;
    }

    /// <summary>
    /// Канонизирует путь: полная форма, посегментное разрешение
    /// reparse-ссылок (включая ссылки на несуществующие цели), без
    /// завершающих разделителей.
    /// </summary>
    /// <param name="путь">Исходный путь.</param>
    /// <returns>Канонизированный путь.</returns>
    /// <exception cref="InvalidOperationException">
    /// Сегмент пути является переопределением (symlink/junction/reparse
    /// point), конечный объект которого не удалось разрешить, либо цепочка
    /// ссылок превышает допустимую глубину, — путь не может быть признан
    /// безопасным.
    /// </exception>
    public static string Канонизировать(string путь)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путь);

        var полный = Path.GetFullPath(путь).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var разделитель = Path.DirectorySeparatorChar;
        var сегменты = полный.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var накоплено = string.Empty;

        for (var индекс = 0; индекс < сегменты.Length; индекс++)
        {
            var префикс = накоплено.Length == 0
                ? сегменты[индекс]
                : накоплено + разделитель + сегменты[индекс];

            var атрибуты = ПрочитатьАтрибуты(префикс);

            if ((атрибуты & FileAttributes.ReparsePoint) != 0)
            {
                // Ссылка (в том числе на несуществующую цель): конечный
                // объект обязан разрешаться, иначе путь fail-closed.
                var цель = ResolveLinkTarget(префикс);

                if (цель is null)
                {
                    throw new InvalidOperationException(
                        $"Путь {путь}: сегмент {префикс} является переопределением " +
                        "(symlink/junction/reparse point), конечный объект которого не удалось разрешить.");
                }

                var остаток = string.Join(
                    разделитель,
                    сегменты[(индекс + 1)..]);

                return Канонизировать(остаток.Length == 0
                    ? цель.FullName
                    : Path.Combine(цель.FullName, остаток));
            }

            накоплено = префикс;
        }

        return НормализоватьРазделители(полный);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFileAttributesW(string lpFileName);

    /// <summary>
    /// Читает атрибуты пути напрямую через GetFileAttributesW: в отличие
    /// от <see cref="File.GetAttributes"/>, для dangling symlink/junction
    /// возвращаются атрибуты самой ссылки (включая ReparsePoint), что
    /// позволяет fail-closed отклонять недоступные переопределения.
    /// Отсутствующий обычный путь даёт 0.
    /// </summary>
    /// <param name="путь">Путь.</param>
    /// <returns>Атрибуты или 0 — путь не существует.</returns>
    private static FileAttributes ПрочитатьАтрибуты(string путь)
    {
        var атрибуты = GetFileAttributesW(путь);

        return атрибуты == 0xFFFFFFFF
            ? 0
            : (FileAttributes)атрибуты;
    }

    /// <summary>
    /// Разрешает reparse-ссылку к конечному целевому объекту с защитой от
    /// зацикленных цепочек.
    /// </summary>
    /// <param name="путь">Путь ссылки.</param>
    /// <returns>Цель либо <see langword="null"/>, если разрешить не удалось.</returns>
    private static FileSystemInfo? ResolveLinkTarget(string путь)
    {
        var текущий = путь;

        for (var глубина = 0; глубина < 32; глубина++)
        {
            FileSystemInfo? цель;

            try
            {
                цель = Directory.Exists(текущий)
                    ? new DirectoryInfo(текущий).ResolveLinkTarget(returnFinalTarget: true)
                    : new FileInfo(текущий).ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (Exception ошибка) when (ошибка is FileNotFoundException or IOException)
            {
                return null;
            }

            if (цель is null)
            {
                return null;
            }

            var атрибутыЦели = ПрочитатьАтрибуты(цель.FullName);

            if ((атрибутыЦели & FileAttributes.ReparsePoint) == 0)
            {
                return цель;
            }

            // Цель сама является ссылкой — продолжаем цепочку вручную.
            текущий = цель.FullName;
        }

        return null;
    }

    /// <summary>Нормализует разделители к основному.</summary>
    /// <param name="путь">Путь.</param>
    /// <returns>Путь с основным разделителем.</returns>
    private static string НормализоватьРазделители(string путь) =>
        путь.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    /// <summary>Разбивает путь на непустые сегменты без учёта регистра.</summary>
    /// <param name="путь">Канонизированный путь.</param>
    /// <returns>Массив сегментов.</returns>
    private static string[] Разбить(string путь)
    {
        var разделители = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        return путь
            .Split(разделители, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(сегмент => сегмент.ToUpperInvariant())
            .ToArray();
    }

    /// <summary>Сегментное равенство.</summary>
    /// <param name="левые">Сегменты первого пути.</param>
    /// <param name="правые">Сегменты второго пути.</param>
    /// <returns><see langword="true"/> при равенстве.</returns>
    private static bool СегментноеРавенство(string[] левые, string[] правые) =>
        левые.Length == правые.Length && ЯвляетсяПрефиксом(левые, правые);

    /// <summary>
    /// Проверяет, что <paramref name="префикс"/> является префиксом
    /// <paramref name="полный"/> по сегментам.
    /// </summary>
    /// <param name="префикс">Сегменты префикса.</param>
    /// <param name="полный">Сегменты полного пути.</param>
    /// <returns><see langword="true"/>, если префикс совпадает.</returns>
    private static bool ЯвляетсяПрефиксом(string[] префикс, string[] полный)
    {
        if (префикс.Length > полный.Length || префикс.Length == 0)
        {
            return false;
        }

        for (var индекс = 0; индекс < префикс.Length; индекс++)
        {
            if (!string.Equals(префикс[индекс], полный[индекс], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
