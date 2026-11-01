namespace Picket302.App.UI.Shell;

/// <summary>
/// Параметры запуска программы, определяющие скрытый вход в настройки
/// подключения (ТЗ §8.11, функция 134: <c>public bool HandleShortcutLaunch(LaunchArgs args)</c>).
/// </summary>
/// <param name="IsCtrlPressed">Признак того, что при запуске была нажата клавиша Ctrl.</param>
/// <param name="IsDoubleClick">
/// Признак запуска двойным щелчком по ярлыку. Ярлык передаёт программе
/// маркер запуска <see cref="ShortcutMarker"/>; иначе запуск считается обычным.
/// </param>
/// <param name="Arguments">Аргументы командной строки.</param>
public sealed record LaunchArgs(
    bool IsCtrlPressed,
    bool IsDoubleClick,
    IReadOnlyList<string> Arguments)
{
    /// <summary>Создаёт параметры запуска без аргументов.</summary>
    /// <param name="isCtrlPressed">Признак нажатой клавиши Ctrl.</param>
    /// <param name="isDoubleClick">Признак запуска двойным щелчком по ярлыку.</param>
    /// <returns>Параметры запуска.</returns>
    public static LaunchArgs Create(bool isCtrlPressed, bool isDoubleClick)
        => new(isCtrlPressed, isDoubleClick, Array.Empty<string>());
}

/// <summary>
/// Перехват запуска программы для скрытого входа в настройки подключения
/// (ТЗ §8.11, функция 134).
/// </summary>
/// <remarks>
/// <para>
/// Запуск с нажатой клавишей Ctrl двойным щелчком по ярлыку открывает диалог
/// настройки подключения к БД без загрузки главного окна: это осознанный
/// «скрытый» вход для администратора при недоступности сервера.
/// </para>
/// <para>
/// Двойной щелчок по ярлыку передаётся программе маркером запуска в
/// аргументах командной строки: операционная система не сообщает приложению
/// о числе щелчков по ярлыку.
/// </para>
/// </remarks>
public sealed class ShortcutInterceptor
{
    /// <summary>
    /// Маркер запуска по ярлыку в аргументах командной строки.
    /// </summary>
    public const string ShortcutMarker = "--shortcut";

    /// <summary>
    /// Определяет, требуется ли скрытый вход в настройки подключения
    /// (ТЗ §8.11, функция 134).
    /// </summary>
    /// <param name="args">Параметры запуска.</param>
    /// <returns><see langword="true"/>, если следует открыть только диалог подключения.</returns>
    public bool HandleShortcutLaunch(LaunchArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.IsCtrlPressed && args.IsDoubleClick;
    }

    /// <summary>
    /// Определяет наличие маркера запуска по ярлыку в аргументах командной строки.
    /// </summary>
    /// <param name="args">Параметры запуска.</param>
    /// <returns><see langword="true"/>, если маркер присутствует.</returns>
    public static bool IsShortcutLaunch(LaunchArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Arguments.Any(аргумент =>
            string.Equals(аргумент, ShortcutMarker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Формирует параметры запуска по фактическим аргументам командной строки
    /// и состоянию клавиш.
    /// </summary>
    /// <param name="arguments">Аргументы командной строки.</param>
    /// <param name="isCtrlPressed">Признак нажатой клавиши Ctrl.</param>
    /// <returns>Параметры запуска.</returns>
    public static LaunchArgs Build(string[]? arguments, bool isCtrlPressed)
    {
        var аргументы = arguments ?? Array.Empty<string>();

        return new LaunchArgs(
            isCtrlPressed,
            IsShortcutLaunch(new LaunchArgs(isCtrlPressed, false, аргументы)),
            аргументы);
    }
}
