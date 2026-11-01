using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Picket302.Infrastructure.Integration;

/// <summary>
/// Поиск адреса контрагента по ИНН/УНН в интернете (ТЗ §8.10, функция 108:
/// «Поиск по ИНН/УНН в интернете»).
/// </summary>
/// <remarks>
/// <para>
/// Адрес поиска строится по настраиваемому шаблону из настроек программы
/// (ключ <c>WebLookupUrlTemplate</c>), например
/// <c>https://&lt;агрегатор&gt;/search?q={INN}</c>. Плейсхолдер <c>{INN}</c>
/// подставляется без учёта регистра, значение ИНН экранируется.
/// </para>
/// <para>
/// Для белорусской локали по умолчанию используется прямой запрос к ЕГР
/// (см. <see cref="ШаблонЕгрБеларусь"/>), однако основной механизм — настраиваемый
/// шаблон.
/// </para>
/// <para>
/// Открывается только адрес со схемой <c>http</c> либо <c>https</c>: запуск
/// произвольного протокола исключён.
/// </para>
/// </remarks>
public sealed class WebLookupLauncher
{
    /// <summary>Ключ настройки с шаблоном адреса поиска.</summary>
    public const string КлючШаблона = "WebLookupUrlTemplate";

    /// <summary>Плейсхолдер ИНН в шаблоне адреса поиска.</summary>
    public const string ПлейсхолдерИнн = "{INN}";

    /// <summary>
    /// Шаблон прямого запроса к ЕГР Республики Беларусь — значение по умолчанию
    /// для белорусской локали.
    /// </summary>
    public const string ШаблонЕгрБеларусь =
        "https://egrc.nalog.by/index.jsp?view=FindEgrEntity&fnd_inn={INN}";

    /// <summary>
    /// Сообщение о незаданном шаблоне поиска.
    /// </summary>
    public const string СообщениеБезШаблона =
        "Шаблон поиска по ИНН не настроен. Задайте его на странице «Сервис».";

    private readonly Func<string, string?> _получитьНастройку;
    private readonly Func<string, bool> _открыть;
    private readonly Func<string?> _шаблонПоУмолчанию;

    /// <summary>
    /// Создаёт запускатель поиска по ИНН.
    /// </summary>
    /// <param name="получитьНастройку">
    /// Чтение настройки программы по имени; по умолчанию — пустое значение.
    /// </param>
    /// <param name="открыть">
    /// Открытие адреса в браузере; по умолчанию — системный браузер через
    /// <see cref="Process.Start(ProcessStartInfo)"/>.
    /// </param>
    /// <param name="шаблонПоУмолчанию">
    /// Шаблон поиска по умолчанию; по умолчанию — ЕГР для белорусской локали
    /// и пустое значение для остальных локалей.
    /// </param>
    public WebLookupLauncher(
        Func<string, string?>? получитьНастройку = null,
        Func<string, bool>? открыть = null,
        Func<string?>? шаблонПоУмолчанию = null)
    {
        _получитьНастройку = получитьНастройку ?? (_ => null);
        _открыть = открыть ?? ОткрытьПоУмолчанию;
        _шаблонПоУмолчанию = шаблонПоУмолчанию
            ?? (() => ШаблонПоУмолчаниюДляЛокализации(CultureInfo.CurrentUICulture.Name));
    }

    /// <summary>
    /// Текст последней ошибки; <see langword="null"/>, если ошибки не было.
    /// </summary>
    public string? ПоследняяОшибка { get; private set; }

    /// <summary>
    /// Открывает поиск адреса контрагента по ИНН/УНН в интернете.
    /// </summary>
    /// <param name="inn">ИНН или УНН контрагента.</param>
    /// <remarks>
    /// Метод не выбрасывает исключения: текст ошибки доступен в свойстве
    /// <see cref="ПоследняяОшибка"/>.
    /// </remarks>
    public void OpenWebLookup(string inn) => TryOpenWebLookup(inn);

    /// <summary>
    /// Открывает поиск адреса контрагента по ИНН/УНН в интернете.
    /// </summary>
    /// <param name="inn">ИНН или УНН контрагента.</param>
    /// <returns>
    /// <see langword="true"/>, если поиск открыт; иначе <see langword="false"/>,
    /// а текст ошибки доступен в свойстве <see cref="ПоследняяОшибка"/>.
    /// </returns>
    public bool TryOpenWebLookup(string inn)
    {
        ПоследняяОшибка = null;

        if (string.IsNullOrWhiteSpace(inn))
        {
            ПоследняяОшибка = "ИНН (УНП) контрагента не задан — поиск не выполнен.";

            return false;
        }

        var шаблон = ПолучитьШаблон();

        if (string.IsNullOrWhiteSpace(шаблон))
        {
            ПоследняяОшибка = СообщениеБезШаблона;

            return false;
        }

        var адрес = ПостроитьАдрес(шаблон, inn);

        if (адрес is null)
        {
            ПоследняяОшибка = СообщениеБезШаблона;

            return false;
        }

        if (!Uri.TryCreate(адрес, UriKind.Absolute, out var разобранный)
            || (!разобранный.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !разобранный.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            ПоследняяОшибка =
                "Недопустимый адрес поиска: допускаются только http и https. "
                + "Проверьте шаблон на странице «Сервис».";

            return false;
        }

        bool открыто;

        try
        {
            открыто = _открыть(адрес);
        }
        catch (Win32Exception ошибка)
        {
            ПоследняяОшибка = $"Не удалось открыть браузер: {ошибка.Message}";

            return false;
        }
        catch (InvalidOperationException ошибка)
        {
            ПоследняяОшибка = $"Не удалось открыть браузер: {ошибка.Message}";

            return false;
        }

        if (!открыто)
        {
            ПоследняяОшибка = "Не удалось открыть браузер с адресом поиска.";

            return false;
        }

        return true;
    }

    /// <summary>
    /// Подставляет ИНН в шаблон адреса поиска. Плейсхолдер <c>{INN}</c>
    /// распознаётся без учёта регистра.
    /// </summary>
    /// <param name="шаблон">Шаблон адреса поиска.</param>
    /// <param name="inn">ИНН или УНН контрагента.</param>
    /// <returns>Адрес поиска либо <see langword="null"/>, если шаблон пуст.</returns>
    public static string? ПостроитьАдрес(string? шаблон, string inn)
    {
        if (string.IsNullOrWhiteSpace(шаблон))
        {
            return null;
        }

        var значение = Uri.EscapeDataString(inn.Trim());

        return шаблон.Replace(ПлейсхолдерИнн, значение, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Возвращает шаблон поиска по умолчанию для указанной локали.
    /// </summary>
    /// <param name="кодЛокализации">Код локали (например, <c>ru-RU</c>, <c>be-BY</c>).</param>
    /// <returns>Шаблон поиска либо <see langword="null"/>, если для локали нет значения.</returns>
    public static string? ШаблонПоУмолчаниюДляЛокализации(string кодЛокализации)
    {
        if (string.IsNullOrWhiteSpace(кодЛокализации))
        {
            return null;
        }

        var язык = кодЛокализации.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return язык is not null && язык.Equals("be", StringComparison.OrdinalIgnoreCase)
            ? ШаблонЕгрБеларусь
            : null;
    }

    /// <summary>
    /// Возвращает шаблон поиска: настройка программы, а при её отсутствии —
    /// значение по умолчанию.
    /// </summary>
    /// <returns>Шаблон поиска либо пустая строка.</returns>
    private string ПолучитьШаблон()
    {
        var изНастроек = _получитьНастройку(КлючШаблона);

        if (!string.IsNullOrWhiteSpace(изНастроек))
        {
            return изНастроек.Trim();
        }

        return _шаблонПоУмолчанию()?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Открывает адрес в системном браузере.
    /// </summary>
    /// <param name="адрес">Адрес поиска.</param>
    /// <returns><see langword="true"/>, если браузер запущен.</returns>
    private static bool ОткрытьПоУмолчанию(string адрес)
    {
        try
        {
            var информация = new ProcessStartInfo(адрес)
            {
                UseShellExecute = true,
            };

            using var процесс = Process.Start(информация);

            return процесс is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}
