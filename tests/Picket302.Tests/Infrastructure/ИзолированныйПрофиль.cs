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
        _состав = await AppServices.СоздатьAsync(
            Пути,
            создатьДемоБазу,
            реестрЛицензирования: РеестрЛицензирования).ConfigureAwait(false);

        _оболочка = new MainShell(_состав);

        return _оболочка;
    }

    /// <summary>
    /// Реестр лицензирования изолированного профиля: состояние пробного
    /// периода пишется в файл временного профиля, а не в реальный
    /// HKCU\Software\Picket302\Licensing рабочей машины.
    /// </summary>
    public Picket302.Infrastructure.Licensing.LicensingRegistry РеестрЛицензирования { get; } =
        Picket302.Infrastructure.Licensing.LicensingRegistry.File(
            Picket302ProfilePaths.ДляКорня(Guid.NewGuid().ToString("N")).DataDirectory);

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
    /// Тестовый крючок: подменяет утилизацию состава, чтобы воспроизвести
    /// её провал. <see langword="null"/> — штатная утилизация. Крючок
    /// получает состав и обязан сам выполнить его утилизацию (инъекция
    /// отказа ставится после реальной утилизации, чтобы cleanup остальных
    /// шагов шёл в реалистичных условиях).
    /// </summary>
    internal Func<AppServices, Task>? ТестУтилизацияСостава { get; set; }

    /// <summary>
    /// Тестовый крючок: подменяет очистку пула соединений, чтобы
    /// воспроизвести её провал. <see langword="null"/> — штатная очистка.
    /// </summary>
    internal Action? ТестОчисткаПула { get; set; }

    /// <summary>
    /// Исключение утилизации профиля: cleanup завершился с ошибкой или
    /// временный каталог не удалён — среда не чистая. Бросается из
    /// <see cref="Dispose"/>, чтобы провал cleanup делал тест красным
    /// (машинно видимый), а не только печатался. Исходные причины всех
    /// неудавшихся шагов сохраняются во вложенном
    /// <see cref="AggregateException"/> и в <c>Data</c>.
    /// </summary>
    public sealed class ProfileCleanupException : InvalidOperationException
    {
        /// <summary>Создаёт исключение утилизации с полной диагностикой.</summary>
        /// <param name="каталог">Корень профиля.</param>
        /// <param name="причины">Ошибки отдельных шагов cleanup.</param>
        /// <param name="каталогОстался">Остался ли временный каталог.</param>
        /// <param name="попытокУдаления">Число выполненных попыток удаления.</param>
        public ProfileCleanupException(
            string каталог,
            IReadOnlyList<Exception> причины,
            bool каталогОстался,
            int попытокУдаления)
            : base(
                "ИзолированныйПрофиль: cleanup завершился с ошибкой (каталог: " + каталог
                + "; каталог остался: " + каталогОстался
                + "; попыток удаления: " + попытокУдаления
                + (причины.Count > 0
                    ? "; причины: " + string.Join(" | ", причины.Select(п => п.Message))
                    : string.Empty)
                + ")",
                причины.Count > 0 ? new AggregateException(причины) : null)
        {
            КаталогОстался = каталогОстался;
            ПопытокУдаления = попытокУдаления;
        }

        /// <summary>Остался ли временный каталог после всех попыток.</summary>
        public bool КаталогОстался { get; }

        /// <summary>Число выполненных попыток удаления каталога.</summary>
        public int ПопытокУдаления { get; }
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

        // Каждый шаг cleanup изолирован: провал одного не отменяет
        // остальные, а все причины собираются в общий итог — ничего не
        // теряется ни в Console.Error, ни при последующих шагах.
        var причины = new List<Exception>();
        string? сообщениеОболочки = null;
        string? сообщениеСостава = null;
        string? сообщениеПула = null;
        string? сообщениеУдаления = null;

        try
        {
            _оболочка?.Dispose();
        }
        catch (Exception ошибка)
        {
            сообщениеОболочки = ошибка.Message;
            причины.Add(Обернуть("утилизация оболочки", ошибка));
        }

        try
        {
            if (_состав is not null)
            {
                var утилизация = ТестУтилизацияСостава
                    ?? (состав => состав.DisposeAsync().AsTask());
                утилизация(_состав).GetAwaiter().GetResult();
            }
        }
        catch (Exception ошибка)
        {
            сообщениеСостава = ошибка.Message;
            причины.Add(Обернуть("утилизация состава", ошибка));
        }

        try
        {
            var очистка = ТестОчисткаПула
                ?? (() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools());
            очистка();
        }
        catch (Exception ошибка)
        {
            сообщениеПула = ошибка.Message;
            причины.Add(Обернуть("очистка пула соединений", ошибка));
        }

        // Каталог удаляется независимо, с повторными попытками: Windows
        // нередко отдаёт IOException «directory not empty» на первом
        // рекурсивном удалении. Удачная повторная попытка завершает шаг
        // успешно (штатное поведение повторов, а не потерянная ошибка);
        // неудача всех попыток — провал cleanup. Ошибка последней попытки
        // попадает в диагностику выброшенного исключения.
        var попытокУдаления = 0;
        Exception? последняяОшибкаУдаления = null;

        for (var попытка = 1; попытка <= 5; попытка++)
        {
            попытокУдаления = попытка;

            if (!Directory.Exists(Корень))
            {
                break;
            }

            try
            {
                Directory.Delete(Корень, recursive: true);
            }
            catch (Exception ошибка) when (ошибка is IOException or UnauthorizedAccessException)
            {
                последняяОшибкаУдаления = ошибка;
                Thread.Sleep(200);
            }
        }

        if (Directory.Exists(Корень))
        {
            ОставшийсяКаталог = Корень;

            if (последняяОшибкаУдаления is not null)
            {
                сообщениеУдаления = последняяОшибкаУдаления.Message;
                причины.Add(Обернуть("удаление временного каталога", последняяОшибкаУдаления));
            }
        }

        // Любой провал cleanup — машинно видимый: исключение, а не печать.
        if (причины.Count > 0 || ОставшийсяКаталог is not null)
        {
            throw new ProfileCleanupException(
                Корень, причины, ОставшийсяКаталог is not null, попытокУдаления)
            {
                Data =
                {
                    ["ошибкаОболочки"] = сообщениеОболочки ?? string.Empty,
                    ["ошибкаСостава"] = сообщениеСостава ?? string.Empty,
                    ["ошибкаПула"] = сообщениеПула ?? string.Empty,
                    ["ошибкаУдаления"] = сообщениеУдаления ?? string.Empty,
                    ["каталогОстался"] = ОставшийсяКаталог is not null,
                    ["попытокУдаления"] = попытокУдаления,
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
    }

    /// <summary>Оборачивает ошибку шага cleanup с именем шага.</summary>
    /// <param name="шаг">Имя шага cleanup.</param>
    /// <param name="ошибка">Исходная ошибка.</param>
    /// <returns>Исключение с контекстом шага и сохранённой причиной.</returns>
    private static InvalidOperationException Обернуть(string шаг, Exception ошибка) =>
        new("ИзолированныйПрофиль: шаг cleanup «" + шаг + "» завершился ошибкой: " + ошибка.Message, ошибка);

    private bool _утилизирован;
}
