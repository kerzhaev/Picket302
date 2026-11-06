using Microsoft.Data.Sqlite;
using Picket302.App;
using Picket302.Infrastructure;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Изолированный тестовый профиль приложения: все файлы состава — база
/// данных, реквизиты подключения, журналы, лицензия, настройки интерфейса —
/// создаются под уникальным временным корнем и удаляются после теста.
/// Реальные файлы пользователя в <c>%AppData%\Picket302</c> не затрагиваются.
/// </summary>
public sealed class ИзолированныйПрофиль : IDisposable
{
    /// <summary>Временный корень профиля теста.</summary>
    public string Корень { get; }

    /// <summary>Пути профиля приложения внутри временного корня.</summary>
    public Picket302ProfilePaths Пути { get; }

    private MainShell? _оболочка;
    private AppServices? _состав;

    /// <summary>
    /// Создаёт уникальный временный корень профиля.
    /// </summary>
    public ИзолированныйПрофиль()
        : this(null)
    {
    }

    /// <summary>
    /// Создаёт профиль в указанном корне: точка acceptance-проверок,
    /// которым нужно знать путь заранее (скриншоты, драйверы модальных
    /// окон). Путь обязан отличаться от производственного профиля.
    /// </summary>
    /// <param name="корень">
    /// Корень профиля; <see langword="null"/> — уникальный временный каталог.
    /// </param>
    public ИзолированныйПрофиль(string? корень)
    {
        Корень = корень ?? Path.Combine(Path.GetTempPath(), "Picket302Tests", Guid.NewGuid().ToString("N"));
        Пути = Picket302ProfilePaths.ДляКорня(Корень);

        ГардПрофиля.ГарантироватьИзоляцию(Пути);

        // Полная структура профиля создаётся сразу: журналы Serilog и
        // хранилище секретов не создают промежуточных каталогов сами.
        Directory.CreateDirectory(Пути.DataDirectory);
        Directory.CreateDirectory(Пути.LogsDirectory);
        Directory.CreateDirectory(Пути.SecretsDirectory);
    }

    /// <summary>
    /// Создаёт состав приложения в изолированном профиле и главное окно
    /// поверх него. Состав освобождается вместе с профилем; владение окном
    /// остаётся у вызывающего.
    /// </summary>
    /// <param name="создатьДемоБазу">Наполнять ли базу демо-данными.</param>
    /// <returns>Главное окно с готовым составом.</returns>
    public async Task<MainShell> СоздатьОболочкуAsync(bool создатьДемоБазу = true)
    {
        _состав = await AppServices.СоздатьAsync(Пути, создатьДемоБазу).ConfigureAwait(false);

        _оболочка = new MainShell(_состав);

        return _оболочка;
    }

    /// <summary>
    /// Передаёт профилю владение составом, созданным вызывающим кодом:
    /// утилизация профиля освобождает состав и очищает пул соединений,
    /// поэтому файл базы освобождается и временный каталог удаляется
    /// (без владения тест оставлял каталог с <c>picket302.db</c>).
    /// Повторная передача — ошибка: молчаливая замена состава теряла бы
    /// прежний без освобождения.
    /// </summary>
    /// <remarks>
    /// Состав обязан быть создан по путям <see cref="Пути"/>: передача
    /// состава чужого профиля отклоняется, иначе утилизация профиля
    /// закрыла бы чужую базу, а собственная осталась бы неосвобождённой.
    /// </remarks>
    /// <param name="состав">Состав, созданный по <see cref="Пути"/>.</param>
    /// <returns>Тот же состав — для fluent-использования.</returns>
    /// <exception cref="InvalidOperationException">
    /// Профиль уже владеет составом либо оболочкой, или состав создан по
    /// другому профилю.
    /// </exception>
    public AppServices ВзятьВладение(AppServices состав)
    {
        ArgumentNullException.ThrowIfNull(состав);

        if (_состав is not null || _оболочка is not null)
        {
            throw new InvalidOperationException(
                "Профиль уже владеет составом или оболочкой — повторная передача владения недопустима.");
        }

        var ожидаемаяБаза = Picket302ProfilePaths.ДляКорня(Корень).DatabasePath;

        if (!string.Equals(
                Path.GetFullPath(состав.ПутьБазы),
                Path.GetFullPath(ожидаемаяБаза),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Состав принадлежит другому профилю (база: " + состав.ПутьБазы
                + ", ожидалась: " + ожидаемаяБаза + ") — передача владения недопустима.");
        }

        _состав = состав;

        return состав;
    }

    /// <summary>
    /// Исключение утилизации профиля: временный каталог не удалён — среда
    /// не чистая. Бросается из <see cref="Dispose"/>, чтобы провал cleanup
    /// делал тест красным (машинно видимый), а не только печатался.
    /// </summary>
    public sealed class ProfileCleanupException : InvalidOperationException
    {
        /// <summary>Создаёт исключение с путём остаточного каталога.</summary>
        /// <param name="каталог">Неудалённый каталог.</param>
        public ProfileCleanupException(string каталог)
            : base("ИзолированныйПрофиль: временный каталог не удалён после 5 попыток и остался: " + каталог)
        {
        }
    }

    /// <summary>
    /// Неудалённый временный каталог после утилизации; заполняется перед
    /// выбросом <see cref="ProfileCleanupException"/>.
    /// </summary>
    public string? ОставшийсяКаталог { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_утилизирован)
        {
            return;
        }

        _утилизирован = true;

        Exception? ошибкаОболочки = null;
        Exception? ошибкаСостава = null;

        // Оболочка, состав, пул и каталог освобождаются независимо: провал
        // одного не должен оставить остальные необработанными.
        try
        {
            _оболочка?.Dispose();
        }
        catch (Exception ошибка) when (ошибка is InvalidOperationException or ObjectDisposedException)
        {
            ошибкаОболочки = ошибка;
        }

        try
        {
            _состав?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ошибка) when (ошибка is InvalidOperationException or ObjectDisposedException or SqliteException)
        {
            ошибкаСостава = ошибка;
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        catch (Exception ошибка)
        {
            Console.Error.WriteLine("ИзолированныйПрофиль: ошибка очистки пула: " + ошибка.Message);
        }

        for (var попытка = 0; попытка < 5; попытка++)
        {
            try
            {
                if (Directory.Exists(Корень))
                {
                    Directory.Delete(Корень, recursive: true);
                }

                break;
            }
            catch (IOException)
            {
                // Каталог ещё занят завершающимися соединениями — повторяем.
                Thread.Sleep(200);
            }
        }

        if (Directory.Exists(Корень))
        {
            ОставшийсяКаталог = Корень;
        }

        // Ошибки утилизации и cleanup сообщаются вместе, чтобы ни одна не
        // маскировала другую.
        if (ошибкаОболочки is not null
            || ошибкаСостава is not null
            || ОставшийсяКаталог is not null)
        {
            throw new ProfileCleanupException(ОставшийсяКаталог ?? Корень)
            {
                Data =
                {
                    ["ошибкаОболочки"] = ошибкаОболочки?.Message ?? string.Empty,
                    ["ошибкаСостава"] = ошибкаСостава?.Message ?? string.Empty,
                    ["каталогОстался"] = ОставшийсяКаталог is not null,
                },
            };
        }

        // Родительский каталог «Picket302Tests» удаляется, когда опустел:
        // параллельные тесты создают в нём свои уникальные корни.
        try
        {
            var родитель = Path.GetDirectoryName(Корень);

            if (родитель is not null
                && Directory.Exists(родитель)
                && !Directory.EnumerateFileSystemEntries(родитель).Any())
            {
                Directory.Delete(родитель);
            }
        }
        catch (Exception ошибка) when (ошибка is IOException or UnauthorizedAccessException)
        {
            // Занятый параллельным тестом каталог удалит последний тест.
        }

        if (ошибкаОболочки is not null || ошибкаСостава is not null)
        {
            throw new ProfileCleanupException(Корень)
            {
                Data =
                {
                    ["ошибкаОболочки"] = ошибкаОболочки?.Message ?? string.Empty,
                    ["ошибкаСостава"] = ошибкаСостава?.Message ?? string.Empty,
                    ["каталогОстался"] = false,
                },
            };
        }
    }

    private bool _утилизирован;
}
