using System.Globalization;
using System.Text;

namespace Picket302.Infrastructure.Update;

/// <summary>
/// Сведения о доступном обновлении (ТЗ §8.12, функция 163:
/// <c>public UpdateInfo CheckForUpdates()</c>).
/// </summary>
/// <param name="Version">Номер доступной версии.</param>
/// <param name="DownloadUrl">Адрес загрузки обновления.</param>
/// <param name="Notes">Пояснение к обновлению.</param>
/// <param name="IsUpdateAvailable">Признак наличия обновления.</param>
/// <param name="DownloadedPath">Путь к скачанному файлу; <see langword="null"/> — не скачан.</param>
public sealed record UpdateInfo(
    string Version,
    string DownloadUrl,
    string Notes,
    bool IsUpdateAvailable,
    string? DownloadedPath)
{
    /// <summary>Создаёт сведения об отсутствии обновления.</summary>
    /// <returns>Сведения без обновления.</returns>
    public static UpdateInfo None(string version = "")
        => new(version, string.Empty, string.Empty, false, null);
}

/// <summary>
/// Проверка наличия обновлений по HTTPS и их загрузка (ТЗ §8.12, функция 163:
/// <c>public UpdateInfo CheckForUpdates(); public void DownloadUpdate(UpdateInfo info)</c>).
/// </summary>
/// <remarks>
/// <para>
/// Файл <c>updates.txt</c> содержит ключи в формате «имя = значение»:
/// <c>version</c>, <c>url</c>, <c>notes</c>. Проверка выполняется по HTTPS;
/// недоступность сервера обновлений приводит к тихой деградации с записью в
/// журнал ошибок — программа продолжает работать штатно.
/// </para>
/// <para>
/// Загрузка обновления выполняется от имени пользователя и не требует
/// отключения защитника Windows.
/// </para>
/// </remarks>
public sealed class UpdateChecker
{
    /// <summary>Имя файла с описанием обновления.</summary>
    public const string UpdatesFileName = "updates.txt";

    /// <summary>Ключ номера версии в файле обновлений.</summary>
    public const string VersionKey = "version";

    /// <summary>Ключ адреса загрузки в файле обновлений.</summary>
    public const string UrlKey = "url";

    /// <summary>Ключ пояснения к обновлению в файле обновлений.</summary>
    public const string NotesKey = "notes";

    private readonly string _адресОбновлений;
    private readonly string _текущаяВерсия;
    private readonly Func<string, CancellationToken, Task<string>> _запрос;
    private readonly Action<string>? _журнал;

    /// <summary>
    /// Создаёт проверку обновлений с сетевым адаптером по умолчанию.
    /// </summary>
    /// <param name="адресОбновлений">Адрес файла обновлений (HTTPS).</param>
    /// <param name="текущаяВерсия">Номер текущей версии программы.</param>
    /// <param name="журнал">Приёмник записей в журнал ошибок.</param>
    public UpdateChecker(string адресОбновлений, string текущаяВерсия, Action<string>? журнал = null)
        : this(адресОбновлений, текущаяВерсия, null, журнал)
    {
    }

    /// <summary>
    /// Создаёт проверку обновлений с внедряемым сетевым адаптером.
    /// </summary>
    /// <param name="адресОбновлений">Адрес файла обновлений (HTTPS).</param>
    /// <param name="текущаяВерсия">Номер текущей версии программы.</param>
    /// <param name="запрос">
    /// Адаптер сетевого обмена: принимает адрес и возвращает текст файла
    /// обновлений. При <see langword="null"/> используется HTTP-клиент.
    /// </param>
    /// <param name="журнал">Приёмник записей в журнал ошибок.</param>
    public UpdateChecker(
        string адресОбновлений,
        string текущаяВерсия,
        Func<string, CancellationToken, Task<string>>? запрос,
        Action<string>? журнал = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(адресОбновлений);
        ArgumentException.ThrowIfNullOrWhiteSpace(текущаяВерсия);

        _адресОбновлений = адресОбновлений;
        _текущаяВерсия = текущаяВерсия;
        _запрос = запрос ?? ЗапроситьПоHttp;
        _журнал = журнал;
    }

    /// <summary>Адрес файла обновлений.</summary>
    public string UpdateUrl => _адресОбновлений;

    /// <summary>Номер текущей версии программы.</summary>
    public string CurrentVersion => _текущаяВерсия;

    /// <summary>
    /// Проверяет наличие обновления (ТЗ §8.12, функция 163:
    /// <c>public UpdateInfo CheckForUpdates()</c>).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Сведения об обновлении; при недоступности сервера — без обновления.</returns>
    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        string содержимое;

        try
        {
            содержимое = await _запрос(_адресОбновлений, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.Net.Http.HttpRequestException ошибка)
        {
            ЗаписатьВЖурнал($"Сервер обновлений недоступен: {ошибка.Message}");

            return UpdateInfo.None(_текущаяВерсия);
        }
        catch (System.IO.IOException ошибка)
        {
            ЗаписатьВЖурнал($"Сбой связи с сервером обновлений: {ошибка.Message}");

            return UpdateInfo.None(_текущаяВерсия);
        }

        var параметры = Parse(содержимое);

        var версия = параметры.TryGetValue(VersionKey, out var значениеВерсии)
            ? значениеВерсии
            : string.Empty;

        if (версия.Length == 0 || !СравнитьВерсии(версия, _текущаяВерсия))
        {
            return UpdateInfo.None(_текущаяВерсия);
        }

        var адрес = параметры.TryGetValue(UrlKey, out var значениеАдреса) ? значениеАдреса : string.Empty;
        var пояснение = параметры.TryGetValue(NotesKey, out var значениеПояснения) ? значениеПояснения : string.Empty;

        return new UpdateInfo(версия, адрес, пояснение, true, null);
    }

    /// <summary>
    /// Скачивает обновление (ТЗ §8.12, функция 163:
    /// <c>public void DownloadUpdate(UpdateInfo info)</c>).
    /// </summary>
    /// <param name="info">Сведения об обновлении.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Путь к скачанному файлу; <see langword="null"/> — загрузка не выполнена.</returns>
    public async Task<string?> DownloadUpdateAsync(UpdateInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (!info.IsUpdateAvailable || string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            return null;
        }

        var каталог = Path.Combine(Path.GetTempPath(), "Picket302Update");
        Directory.CreateDirectory(каталог);

        // Разбор адреса до файловых операций (F-C8): некорректный адрес
        // прежде падал UriFormatException либо создавал частичный файл.
        if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var адресЗагрузки)
            || (адресЗагрузки.Scheme != Uri.UriSchemeHttps && адресЗагрузки.Scheme != Uri.UriSchemeHttp))
        {
            ЗаписатьВЖурнал($"Некорректный адрес обновления: {info.DownloadUrl}");

            return null;
        }

        var имя = Path.GetFileName(адресЗагрузки.LocalPath);

        if (string.IsNullOrWhiteSpace(имя))
        {
            имя = "update.bin";
        }

        var путь = Path.Combine(каталог, имя);

        try
        {
            using var клиент = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            var поток = await клиент
                .GetStreamAsync(адресЗагрузки, cancellationToken)
                .ConfigureAwait(false);

            var целевойПоток = System.IO.File.Create(путь);

            await using var адаптерИсточника = поток.ConfigureAwait(false);
            await using var адаптерПриёмника = целевойПоток.ConfigureAwait(false);

            await поток.CopyToAsync(целевойПоток, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.Net.Http.HttpRequestException ошибка)
        {
            ЗаписатьВЖурнал($"Не удалось скачать обновление: {ошибка.Message}");

            УдалитьЧастичныйФайл(путь);

            return null;
        }
        catch (IOException ошибка)
        {
            ЗаписатьВЖурнал($"Сбой загрузки обновления: {ошибка.Message}");

            УдалитьЧастичныйФайл(путь);

            return null;
        }

        return путь;
    }

    /// <summary>
    /// Удаляет частичный файл загрузки после сбоя (F-C8).
    /// </summary>
    /// <param name="путь">Путь частичного файла.</param>
    private static void УдалитьЧастичныйФайл(string путь)
    {
        try
        {
            if (System.IO.File.Exists(путь))
            {
                System.IO.File.Delete(путь);
            }
        }
        catch (IOException)
        {
            // Файл занят: будет удалён следующей попыткой.
        }
    }

    /// <summary>
    /// Разбирает файл обновлений вида «имя = значение».
    /// </summary>
    /// <param name="содержимое">Текст файла обновлений.</param>
    /// <returns>Перечень параметров.</returns>
    public static IReadOnlyDictionary<string, string> Parse(string? содержимое)
    {
        var параметры = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(содержимое))
        {
            return параметры;
        }

        foreach (var сыраяСтрока in содержимое.Split('\n'))
        {
            var строка = сыраяСтрока.Trim();

            if (строка.Length == 0 || строка.StartsWith('#'))
            {
                continue;
            }

            var позиция = строка.IndexOf('=');

            if (позиция <= 0)
            {
                continue;
            }

            var ключ = строка[..позиция].Trim();
            var значение = строка[(позиция + 1)..].Trim();

            параметры[ключ] = значение;
        }

        return параметры;
    }

    /// <summary>
    /// Сравнивает номера версий: доступной и текущей.
    /// </summary>
    /// <param name="доступная">Номер доступной версии.</param>
    /// <param name="текущая">Номер текущей версии.</param>
    /// <returns><see langword="true"/>, если доступная версия новее текущей.</returns>
    public static bool СравнитьВерсии(string доступная, string текущая)
    {
        if (string.IsNullOrWhiteSpace(доступная))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(текущая))
        {
            return true;
        }

        var частиДоступной = РазбитьВерсию(доступная);
        var частиТекущей = РазбитьВерсию(текущая);

        var длина = Math.Max(частиДоступной.Length, частиТекущей.Length);

        for (var индекс = 0; индекс < длина; индекс++)
        {
            var числоДоступной = индекс < частиДоступной.Length ? частиДоступной[индекс] : 0;
            var числоТекущей = индекс < частиТекущей.Length ? частиТекущей[индекс] : 0;

            if (числоДоступной != числоТекущей)
            {
                return числоДоступной > числоТекущей;
            }
        }

        return false;
    }

    /// <summary>
    /// Разбивает номер версии на числовые части.
    /// </summary>
    private static int[] РазбитьВерсию(string версия)
    {
        var части = версия.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var числа = new int[части.Length];

        for (var индекс = 0; индекс < части.Length; индекс++)
        {
            _ = int.TryParse(
                части[индекс],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out числа[индекс]);
        }

        return числа;
    }

    /// <summary>
    /// Запрашивает файл обновлений по HTTPS.
    /// </summary>
    private static async Task<string> ЗапроситьПоHttp(string адрес, CancellationToken cancellationToken)
    {
        using var клиент = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        using var ответ = await клиент.GetAsync(адрес, cancellationToken).ConfigureAwait(false);

        ответ.EnsureSuccessStatusCode();

        var байты = await ответ.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        return Encoding.UTF8.GetString(байты);
    }

    /// <summary>
    /// Записывает сообщение в журнал ошибок.
    /// </summary>
    private void ЗаписатьВЖурнал(string сообщение) => _журнал?.Invoke(сообщение);
}
