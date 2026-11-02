using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace Picket302.Infrastructure.Support;

/// <summary>
/// Запуск утилиты удалённого доступа (ТЗ §8.10, функция 109: «Утилита
/// удалённого доступа»).
/// </summary>
/// <remarks>
/// <para>
/// Параметры утилиты задаёт администратор: адрес загрузки, имя файла и папка
/// размещения. Если утилита ещё не загружена, она скачивается по HTTPS и
/// запускается.
/// </para>
/// <para>
/// Загрузка выполняется только по схеме <c>https</c>; произвольные протоколы
/// и небезопасная загрузка исключены.
/// </para>
/// </remarks>
public sealed class RemoteAssistantLauncher
{
    /// <summary>Ключ настройки с адресом загрузки утилиты.</summary>
    public const string КлючUrl = "RemoteAssistantUrl";

    /// <summary>Ключ настройки с именем файла утилиты.</summary>
    public const string КлючИмениФайла = "RemoteAssistantFileName";

    /// <summary>Ключ настройки с папкой размещения утилиты.</summary>
    public const string КлючПапки = "RemoteAssistantFolder";

    /// <summary>
    /// Сообщение о том, что утилита удалённого доступа не настроена.
    /// </summary>
    public const string СообщениеНеНастроено =
        "Утилита удалённого доступа не настроена администратором";

    private readonly Func<string, string?> _получитьНастройку;
    private readonly Func<string, string, CancellationToken, Task<bool>> _скачать;
    private readonly Func<string, bool> _запустить;
    private readonly string _папкаПоУмолчанию;
    private string _папкаЗагрузки;

    /// <summary>
    /// Создаёт запускатель утилиты удалённого доступа.
    /// </summary>
    /// <param name="получитьНастройку">
    /// Чтение настройки программы по имени; по умолчанию — пустое значение.
    /// </param>
    /// <param name="скачать">
    /// Загрузка утилиты: адрес и целевой путь файла (F-C5 — прежде делегат
    /// получал только адрес, и целевой файл настраиваемой папки не создавался);
    /// по умолчанию — загрузка по HTTPS средствами <see cref="HttpClient"/>.
    /// </param>
    /// <param name="запустить">
    /// Запуск утилиты; по умолчанию — системный запуск файла.
    /// </param>
    /// <param name="папкаПоУмолчанию">
    /// Папка размещения утилиты; по умолчанию —
    /// <c>%ProgramData%\Picket302\Support</c>.
    /// </param>
    public RemoteAssistantLauncher(
        Func<string, string?>? получитьНастройку = null,
        Func<string, string, CancellationToken, Task<bool>>? скачать = null,
        Func<string, bool>? запустить = null,
        string? папкаПоУмолчанию = null)
    {
        _получитьНастройку = получитьНастройку ?? (_ => null);
        _скачать = скачать ?? СкачатьПоУмолчаниюAsync;
        _запустить = запустить ?? ЗапуститьПоУмолчанию;
        _папкаПоУмолчанию = папкаПоУмолчанию ?? ПапкаПоУмолчанию();
        _папкаЗагрузки = _папкаПоУмолчанию;
    }

    /// <summary>
    /// Текст последней ошибки; <see langword="null"/>, если ошибки не было.
    /// </summary>
    public string? ПоследняяОшибка { get; private set; }

    /// <summary>
    /// Запускает настроенную утилиту удалённого доступа, при необходимости
    /// предварительно загрузив её.
    /// </summary>
    /// <remarks>
    /// Метод не выбрасывает исключения: текст ошибки доступен в свойстве
    /// <see cref="ПоследняяОшибка"/>.
    /// </remarks>
    public void LaunchOrUpdateRemoteAssistant()
        => TryLaunchRemoteAssistantAsync().ConfigureAwait(false).GetAwaiter().GetResult();

    /// <summary>
    /// Запускает настроенную утилиту удалённого доступа, при необходимости
    /// предварительно загрузив её.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>
    /// <see langword="true"/>, если утилита запущена; иначе
    /// <see langword="false"/>, а текст ошибки доступен в свойстве
    /// <see cref="ПоследняяОшибка"/>.
    /// </returns>
    public async Task<bool> TryLaunchRemoteAssistantAsync(CancellationToken cancellationToken = default)
    {
        ПоследняяОшибка = null;

        var адрес = _получитьНастройку(КлючUrl);

        if (string.IsNullOrWhiteSpace(адрес))
        {
            ПоследняяОшибка = СообщениеНеНастроено;

            return false;
        }

        адрес = адрес.Trim();

        if (!Uri.TryCreate(адрес, UriKind.Absolute, out var разобранный)
            || !разобранный.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            ПоследняяОшибка =
                "Адрес утилиты удалённого доступа должен быть https:// — загрузка по незащищённому протоколу запрещена.";

            return false;
        }

        var папка = _получитьНастройку(КлючПапки);

        if (string.IsNullOrWhiteSpace(папка))
        {
            папка = _папкаПоУмолчанию;
        }

        var имяФайла = _получитьНастройку(КлючИмениФайла);

        if (string.IsNullOrWhiteSpace(имяФайла))
        {
            имяФайла = Path.GetFileName(разобранный.LocalPath);
        }

        if (string.IsNullOrWhiteSpace(имяФайла))
        {
            ПоследняяОшибка =
                $"{СообщениеНеНастроено}: не задано имя файла утилиты удалённого доступа.";

            return false;
        }

        var путьУтилиты = Path.Combine(папка, имяФайла.Trim());

        if (!File.Exists(путьУтилиты))
        {
            _папкаЗагрузки = папка;

            bool загружена;

            try
            {
                // Целевой путь передаётся в загрузчик (F-C5).
                загружена = await _скачать(адрес, путьУтилиты, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ПоследняяОшибка = "Загрузка утилиты удалённого доступа отменена.";

                return false;
            }
            catch (HttpRequestException ошибка)
            {
                ПоследняяОшибка = $"Не удалось загрузить утилиту удалённого доступа: {ошибка.Message}";

                return false;
            }
            catch (IOException ошибка)
            {
                ПоследняяОшибка = $"Не удалось загрузить утилиту удалённого доступа: {ошибка.Message}";

                return false;
            }

            if (!загружена)
            {
                ПоследняяОшибка = "Не удалось загрузить утилиту удалённого доступа.";

                return false;
            }
        }

        bool запущена;

        try
        {
            запущена = _запустить(путьУтилиты);
        }
        catch (Win32Exception ошибка)
        {
            ПоследняяОшибка = $"Не удалось запустить утилиту удалённого доступа: {ошибка.Message}";

            return false;
        }
        catch (InvalidOperationException ошибка)
        {
            ПоследняяОшибка = $"Не удалось запустить утилиту удалённого доступа: {ошибка.Message}";

            return false;
        }

        if (!запущена)
        {
            ПоследняяОшибка = "Не удалось запустить утилиту удалённого доступа.";

            return false;
        }

        return true;
    }

    /// <summary>
    /// Возвращает папку размещения утилиты по умолчанию.
    /// </summary>
    /// <returns>Путь к папке.</returns>
    private static string ПапкаПоУмолчанию()
    {
        var общая = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        if (string.IsNullOrWhiteSpace(общая))
        {
            общая = AppContext.BaseDirectory;
        }

        return Path.Combine(общая, "Picket302", "Support");
    }

    /// <summary>
    /// Загружает утилиту по HTTPS в заданный путь (F-C5).
    /// </summary>
    /// <param name="адрес">Адрес загрузки.</param>
    /// <param name="путьФайла">Целевой путь файла утилиты.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если файл загружен.</returns>
    /// <remarks>
    /// Загрузка ведётся во временный файл <c>.part</c>, который по успехе
    /// атомарно переименовывается в целевой; при ошибке или отмене частичный
    /// файл удаляется, и целевой путь не остаётся повреждённым.
    /// </remarks>
    private async Task<bool> СкачатьПоУмолчаниюAsync(
        string адрес,
        string путьФайла,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(адрес, UriKind.Absolute, out var разобранный)
            || !разобранный.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using var клиент = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        using var поток = await клиент
            .GetAsync(разобранный, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        поток.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(путьФайла)!);

        await СохранитьПотокВФайлАтомарно(
            await поток.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            путьФайла,
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Записывает поток в файл через временный <c>.part</c> с атомарным
    /// переименованием (F-C5).
    /// </summary>
    /// <param name="содержимое">Поток содержимого.</param>
    /// <param name="путьФайла">Целевой путь файла.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача записи.</returns>
    /// <remarks>
    /// При ошибке или отмене частичный файл удаляется, и целевой путь
    /// не остаётся повреждённым: обрыв загрузки не должен выглядеть
    /// как готовая утилита.
    /// </remarks>
    public static async Task СохранитьПотокВФайлАтомарно(
        Stream содержимое,
        string путьФайла,
        CancellationToken cancellationToken)
    {
        var путьЧасти = путьФайла + ".part";

        try
        {
            await using (var файл = File.Create(путьЧасти))
            {
                await содержимое.CopyToAsync(файл, cancellationToken).ConfigureAwait(false);
            }

            File.Move(путьЧасти, путьФайла, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(путьЧасти))
                {
                    File.Delete(путьЧасти);
                }
            }
            catch (IOException)
            {
                // Файл занят: будет перезаписан следующей попыткой.
            }

            throw;
        }
    }

    /// <summary>
    /// Запускает файл утилиты.
    /// </summary>
    /// <param name="путь">Путь к файлу утилиты.</param>
    /// <returns><see langword="true"/>, если утилита запущена.</returns>
    private static bool ЗапуститьПоУмолчанию(string путь)
    {
        try
        {
            var информация = new ProcessStartInfo(путь)
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
