using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Picket302.Domain.Licensing;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Хранилище значений ветки лицензирования
/// (<c>HKCU\Software\Picket302\Licensing</c>; ТЗ §8.10, функция 119:
/// «Данные: Реестр HKCU + подписанный файл лицензии»).
/// </summary>
/// <remarks>
/// <para>
/// Класс поддерживает два режима: ветку реестра Windows (по умолчанию) и
/// файл в каталоге. Файловый режим применяется в переносимых поставках и в
/// проверочных сценариях, где состояние не должно попадать в профиль
/// пользователя Windows.
/// </para>
/// <para>
/// Ветка реестра хранит: отпечаток оборудования (<c>MachineId</c> и его
/// варианты), начало пробного периода (<c>TrialStartedAt</c>) и сведения о
/// привязке лицензии (<c>License.*</c>). Снятие ключа удаляет только значения
/// <c>License.*</c>: начало пробного периода и отпечаток оборудования
/// сохраняются, поэтому снятие ключа возвращает пробный режим с тем же
/// отсчётом срока.
/// </para>
/// </remarks>
public sealed class LicensingRegistry
{
    /// <summary>Путь ветки реестра лицензирования.</summary>
    public const string ПутьВетки = @"Software\Picket302\Licensing";

    /// <summary>Имя файла хранилища в файловом режиме.</summary>
    public const string ИмяФайла = "licensing-state.json";

    private readonly object _блокировка = new();
    private readonly Action<string>? _журнал;

    /// <summary>
    /// Путь к файлу хранилища; пустая строка — работа с веткой реестра Windows.
    /// </summary>
    private readonly string _путьФайла;

    private LicensingRegistry(string путьФайла, Action<string>? журнал)
    {
        _путьФайла = путьФайла;
        _журнал = журнал;
    }

    /// <summary>
    /// Создаёт хранилище значений в ветке реестра Windows
    /// (<c>HKCU\Software\Picket302\Licensing</c>).
    /// </summary>
    /// <param name="журнал">Приёмник сообщений о невозможности прочитать значения.</param>
    /// <returns>Хранилище значений реестра.</returns>
    public static LicensingRegistry Windows(Action<string>? журнал = null) => new(string.Empty, журнал);

    /// <summary>
    /// Создаёт хранилище значений в файле каталога.
    /// </summary>
    /// <param name="каталог">Каталог файла хранилища.</param>
    /// <param name="журнал">Приёмник сообщений о невозможности прочитать значения.</param>
    /// <returns>Хранилище значений файла.</returns>
    /// <exception cref="ArgumentException">Каталог не задан.</exception>
    public static LicensingRegistry File(string каталог, Action<string>? журнал = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталог);

        return new LicensingRegistry(Path.Combine(Path.GetFullPath(каталог), ИмяФайла), журнал);
    }

    /// <summary>
    /// Читает значение хранилища.
    /// </summary>
    /// <param name="имяЗначения">Имя значения.</param>
    /// <returns>Значение либо <see langword="null"/>, если его нет.</returns>
    public string? Прочитать(string имяЗначения)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяЗначения);

        if (_путьФайла.Length == 0)
        {
            using var ветка = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ПутьВетки);

            return ветка?.GetValue(имяЗначения) as string;
        }

        lock (_блокировка)
        {
            var значения = ПрочитатьФайл();

            return значения.TryGetValue(имяЗначения, out var значение) ? значение : null;
        }
    }

    /// <summary>
    /// Записывает значение хранилища.
    /// </summary>
    /// <param name="имяЗначения">Имя значения.</param>
    /// <param name="значение">Значение.</param>
    public void Записать(string имяЗначения, string значение)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяЗначения);
        ArgumentNullException.ThrowIfNull(значение);

        if (_путьФайла.Length == 0)
        {
            using var ветка = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(ПутьВетки, writable: true);

            ветка?.SetValue(имяЗначения, значение, RegistryValueKind.String);

            return;
        }

        lock (_блокировка)
        {
            var значения = ПрочитатьФайл();

            значения[имяЗначения] = значение;

            ЗаписатьФайл(значения);
        }
    }

    /// <summary>
    /// Удаляет значение хранилища.
    /// </summary>
    /// <param name="имяЗначения">Имя значения.</param>
    public void Удалить(string имяЗначения)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имяЗначения);

        if (_путьФайла.Length == 0)
        {
            using var ветка = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ПутьВетки, writable: true);

            ветка?.DeleteValue(имяЗначения, throwOnMissingValue: false);

            return;
        }

        lock (_блокировка)
        {
            var значения = ПрочитатьФайл();

            if (значения.Remove(имяЗначения))
            {
                ЗаписатьФайл(значения);
            }
        }
    }

    /// <summary>
    /// Читает значения из файла хранилища.
    /// </summary>
    /// <returns>Словарь значений.</returns>
    private Dictionary<string, string> ПрочитатьФайл()
    {
        if (!System.IO.File.Exists(_путьФайла))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var текст = System.IO.File.ReadAllText(_путьФайла, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(текст))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(текст) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception исключение) when (исключение is IOException or JsonException or UnauthorizedAccessException)
        {
            // Повреждённый файл состояния заменяется пустым набором значений:
            // лицензия при этом считается отсутствующей (состояние trial).
            _журнал?.Invoke("Файл состояния лицензирования не читается: " + исключение.Message);

            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Записывает значения в файл хранилища.
    /// </summary>
    /// <param name="значения">Словарь значений.</param>
    private void ЗаписатьФайл(Dictionary<string, string> значения)
    {
        var каталог = Path.GetDirectoryName(_путьФайла);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        var текст = JsonSerializer.Serialize(значения);

        System.IO.File.WriteAllText(_путьФайла, текст, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

/// <summary>
/// Сведения о лицензии, сохранённые локально
/// (ТЗ §8.10, функции 119, 122 и 123).
/// </summary>
/// <param name="Edition">Редакция лицензии.</param>
/// <param name="ExpiresAtUtc">Момент истечения срока (UTC); <see langword="null"/> — срок не ограничен.</param>
/// <param name="Owner">Владелец лицензии.</param>
/// <param name="MachineId">
/// Идентификатор оборудования, на которое выписан ключ; пустая строка —
/// ключ допускает привязку к нескольким машинам в пределах лимита привязок.
/// </param>
/// <param name="Bindings">Привязки к управляющим машинам.</param>
/// <param name="IssuedAtUtc">Момент выдачи лицензии (UTC).</param>
/// <param name="FeatureFlags">Доступные возможности редакции.</param>
/// <param name="MaxBindings">
/// Предельное число привязок; <see langword="null"/> — без ограничения
/// (ТЗ §8.10, функция 123: «один ключ — несколько привязок по MachineID
/// в пределах лимита лицензии»).
/// </param>
public sealed record LicensePayload(
    Edition Edition,
    DateTime? ExpiresAtUtc,
    string? Owner,
    string MachineId,
    IReadOnlyList<LicenseBinding> Bindings,
    DateTime IssuedAtUtc,
    IReadOnlyList<LicenseFeature> FeatureFlags,
    int? MaxBindings);

/// <summary>
/// Хранилище данных лицензии: файл лицензии рядом с данными приложения и
/// сведения о привязке в реестре (ТЗ §8.10, функции 122 и 123).
/// </summary>
/// <remarks>
/// <para>
/// Тело лицензии хранится в зашифрованном виде (AES-256-GCM, ключ выводится
/// из MachineID). Неудачная расшифровка не приводит к исключению наружу:
/// данные лицензии считаются отсутствующими, состояние — trial
/// (ТЗ §8.10, функция 124).
/// </para>
/// <para>
/// Снятие ключа удаляет файл лицензии и сведения о привязке, но не затрагивает
/// пользовательские данные приложения (ТЗ §8.10, функция 123: «снятие ключа
/// возвращает trial с сохранением данных»).
/// </para>
/// </remarks>
public sealed class LicenseStore
{
    /// <summary>Имя файла лицензии по умолчанию.</summary>
    public const string ИмяФайлаЛицензии = "license.dat";

    /// <summary>Префикс имён значений реестра со сведениями о лицензии.</summary>
    public const string ПрефиксЗначенийЛицензии = "License.";

    private readonly LicensingRegistry _реестр;
    private readonly Action<string>? _журнал;

    /// <summary>Путь к файлу лицензии.</summary>
    private readonly string _путьФайла;

    /// <summary>
    /// Создаёт хранилище данных лицензии.
    /// </summary>
    /// <param name="путьФайлаЛицензии">Путь к файлу лицензии.</param>
    /// <param name="реестр">
    /// Хранилище значений ветки лицензирования; по умолчанию — реестр Windows.
    /// </param>
    /// <param name="машина">
    /// Вычислитель идентификатора оборудования; по умолчанию создаётся на
    /// основе <paramref name="реестр"/>.
    /// </param>
    /// <param name="журнал">Приёмник сообщений о невозможности прочитать данные лицензии.</param>
    /// <exception cref="ArgumentException">Путь к файлу не задан.</exception>
    public LicenseStore(
        string путьФайлаЛицензии,
        LicensingRegistry? реестр = null,
        MachineIdentity? машина = null,
        Action<string>? журнал = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьФайлаЛицензии);

        _путьФайла = Path.GetFullPath(путьФайлаЛицензии);
        _реестр = реестр ?? LicensingRegistry.Windows();
        _журнал = журнал;

        Машина = машина ?? new MachineIdentity(журнал, _реестр);

        MachineId = Машина.GetMachineId(MachineIdVariant.Composite);
        Crypto = new LicenseCrypto(MachineId);
    }

    /// <summary>Путь к файлу лицензии.</summary>
    public string ПутьФайла => _путьФайла;

    /// <summary>Составной отпечаток оборудования текущей машины.</summary>
    public string MachineId { get; }

    /// <summary>Шифровальщик данных лицензии текущей машины.</summary>
    public LicenseCrypto Crypto { get; }

    /// <summary>Вычислитель идентификатора оборудования.</summary>
    public MachineIdentity Машина { get; }

    /// <summary>
    /// Возвращает путь к файлу лицензии по умолчанию —
    /// <c>%AppData%\Picket302\license.dat</c>.
    /// </summary>
    /// <returns>Полный путь к файлу лицензии.</returns>
    public static string ПутьФайлаПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Picket302",
        ИмяФайлаЛицензии);

    /// <summary>
    /// Сохраняет данные лицензии: защищённый блок — в файл лицензии,
    /// сведения о привязке — в реестр.
    /// </summary>
    /// <param name="protectedPayload">Защищённый блок данных лицензии.</param>
    /// <param name="binding">Привязка к управляющей машине.</param>
    /// <param name="edition">Редакция лицензии.</param>
    /// <param name="expiresAtUtc">Момент истечения срока (UTC).</param>
    /// <param name="owner">Владелец лицензии.</param>
    /// <exception cref="ArgumentNullException">Защищённый блок или привязка не заданы.</exception>
    public void Save(
        byte[] protectedPayload,
        LicenseBinding binding,
        Edition edition,
        DateTime? expiresAtUtc,
        string? owner)
    {
        ArgumentNullException.ThrowIfNull(protectedPayload);
        ArgumentNullException.ThrowIfNull(binding);

        var каталог = Path.GetDirectoryName(_путьФайла);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        System.IO.File.WriteAllBytes(_путьФайла, protectedPayload);

        _реестр.Записать(ПрефиксЗначенийЛицензии + "Edition", edition.ToString());
        _реестр.Записать(
            ПрефиксЗначенийЛицензии + "ExpiresAtUtc",
            expiresAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        _реестр.Записать(ПрефиксЗначенийЛицензии + "Owner", owner ?? string.Empty);
        _реестр.Записать(ПрефиксЗначенийЛицензии + "MachineId", binding.MachineId);

        var привязки = Bindings.ToList();

        if (!привязки.Any(привязка => string.Equals(привязка.MachineId, binding.MachineId, StringComparison.Ordinal)))
        {
            привязки.Add(binding);
        }

        СохранитьПривязки(привязки);
    }

    /// <summary>
    /// Читает данные лицензии.
    /// </summary>
    /// <param name="payload">Прочитанные сведения о лицензии.</param>
    /// <returns>
    /// <see langword="true"/> — лицензия прочитана; <see langword="false"/> —
    /// лицензии нет либо данные повреждены (состояние trial).
    /// </returns>
    public bool TryLoad(out LicensePayload payload)
    {
        payload = null!;

        if (!System.IO.File.Exists(_путьФайла))
        {
            return false;
        }

        byte[] защищённый;

        try
        {
            защищённый = System.IO.File.ReadAllBytes(_путьФайла);
        }
        catch (Exception исключение) when (исключение is IOException or UnauthorizedAccessException)
        {
            _журнал?.Invoke($"Не удалось прочитать файл лицензии: {исключение.Message}");

            return false;
        }

        byte[] открытые;

        try
        {
            открытые = Crypto.Unprotect(защищённый);
        }
        catch (System.Security.Cryptography.CryptographicException исключение)
        {
            // Данные лицензии считаются отсутствующими: состояние trial
            // (ТЗ §8.10, функция 124).
            _журнал?.Invoke($"Данные лицензии не приняты: {исключение.Message}");

            return false;
        }

        try
        {
            var прочитанное = JsonSerializer.Deserialize<LicensePayload>(
                открытые,
                ПараметрыJson);

            if (прочитанное is null)
            {
                return false;
            }

            var привязки = Bindings;

            payload = прочитанное with
            {
                Bindings = привязки.Count > 0 ? привязки : прочитанное.Bindings,
            };

            return true;
        }
        catch (JsonException исключение)
        {
            _журнал?.Invoke($"Данные лицензии не читаются: {исключение.Message}");

            return false;
        }
    }

    /// <summary>
    /// Снимает ключ: удаляет данные лицензии и сведения о привязке.
    /// Пользовательские данные приложения не затрагиваются.
    /// </summary>
    public void RemoveKey()
    {
        if (System.IO.File.Exists(_путьФайла))
        {
            try
            {
                System.IO.File.Delete(_путьФайла);
            }
            catch (Exception исключение) when (исключение is IOException or UnauthorizedAccessException)
            {
                _журнал?.Invoke($"Не удалось удалить файл лицензии: {исключение.Message}");
            }
        }

        foreach (var имя in new[]
        {
            ПрефиксЗначенийЛицензии + "Edition",
            ПрефиксЗначенийЛицензии + "ExpiresAtUtc",
            ПрефиксЗначенийЛицензии + "Owner",
            ПрефиксЗначенийЛицензии + "MachineId",
            ПрефиксЗначенийЛицензии + "Bindings",
        })
        {
            _реестр.Удалить(имя);
        }
    }

    /// <summary>
    /// Привязки лицензии к управляющим машинам.
    /// </summary>
    public IReadOnlyList<LicenseBinding> Bindings
    {
        get
        {
            var сохранённое = _реестр.Прочитать(ПрефиксЗначенийЛицензии + "Bindings");

            if (string.IsNullOrWhiteSpace(сохранённое))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<LicenseBinding>>(
                    сохранённое,
                    ПараметрыJson) ?? new List<LicenseBinding>();
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    /// <summary>
    /// Добавляет привязку лицензии к управляющей машине.
    /// </summary>
    /// <param name="machineId">Идентификатор оборудования.</param>
    /// <param name="активированоUtc">Момент активации (UTC).</param>
    /// <param name="пределПривязок">
    /// Предельное число привязок; <see langword="null"/> — без ограничения.
    /// </param>
    /// <returns>
    /// <see langword="true"/> — привязка зарегистрирована либо уже была
    /// зарегистрирована; <see langword="false"/> — исчерпан предел привязок.
    /// </returns>
    public bool AddBinding(string machineId, DateTime? активированоUtc = null, int? пределПривязок = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);

        var привязки = Bindings.ToList();

        if (привязки.Any(привязка => string.Equals(привязка.MachineId, machineId, StringComparison.Ordinal)))
        {
            return true;
        }

        if (пределПривязок is int предел && привязки.Count >= предел)
        {
            return false;
        }

        привязки.Add(new LicenseBinding(machineId, активированоUtc ?? DateTime.UtcNow));

        СохранитьПривязки(привязки);

        return true;
    }

    /// <summary>
    /// Сохраняет перечень привязок в реестре.
    /// </summary>
    /// <param name="привязки">Перечень привязок.</param>
    private void СохранитьПривязки(IReadOnlyList<LicenseBinding> привязки)
    {
        var текст = JsonSerializer.Serialize(привязки, ПараметрыJson);

        _реестр.Записать(ПрефиксЗначенийЛицензии + "Bindings", текст);
    }

    /// <summary>
    /// Параметры сериализации сведений о лицензии.
    /// </summary>
    internal static JsonSerializerOptions ПараметрыJson { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
