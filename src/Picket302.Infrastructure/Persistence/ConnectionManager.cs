using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Picket302.Infrastructure.Integration;

namespace Picket302.Infrastructure.Persistence;

/// <summary>
/// Тип подключаемой базы данных (ТЗ §8.8, функция 102: «Хранит реквизиты
/// подключения (тип СУБД, сервер, порт, база, пользователь)»).
/// </summary>
public enum DatabaseKind
{
    /// <summary>Локальный переносимый режим: файл SQLite.</summary>
    LocalSqlite = 0,

    /// <summary>Клиент-серверный режим: общая БД на сервере.</summary>
    ServerPostgres = 1,
}

/// <summary>
/// Реквизиты подключения к базе данных (ТЗ §8.8, функция 102).
/// </summary>
/// <remarks>
/// Пароль хранится в защищённом виде: в свойстве <see cref="ProtectedPassword"/>
/// находится блок DPAPI, а не открытый текст. Открытое значение пароля в
/// реквизитах не сохраняется.
/// </remarks>
/// <param name="Kind">Тип базы данных.</param>
/// <param name="Server">Узел сервера БД; для локального режима — пустая строка.</param>
/// <param name="Port">Порт службы БД.</param>
/// <param name="DatabaseName">Имя базы данных на сервере.</param>
/// <param name="User">Пользователь базы данных.</param>
/// <param name="ProtectedPassword">Пароль в защищённом виде (DPAPI).</param>
/// <param name="DatabasePath">Путь к файлу базы данных для локального режима.</param>
public sealed record ConnectionDescriptor(
    DatabaseKind Kind,
    string Server,
    int Port,
    string DatabaseName,
    string User,
    string? ProtectedPassword,
    string DatabasePath)
{
    /// <summary>Порт службы БД по умолчанию.</summary>
    public const int DefaultPort = 5432;

    /// <summary>
    /// Создаёт реквизиты локального режима (файл SQLite).
    /// </summary>
    /// <param name="путьБазы">Путь к файлу базы данных.</param>
    /// <returns>Реквизиты локального режима.</returns>
    public static ConnectionDescriptor Local(string путьБазы) => new(
        DatabaseKind.LocalSqlite,
        string.Empty,
        DefaultPort,
        string.Empty,
        string.Empty,
        null,
        путьБазы);

    /// <summary>Признак локального режима.</summary>
    public bool IsLocal => Kind == DatabaseKind.LocalSqlite;

    /// <summary>
    /// Признак выполненного первого подключения, сохраняемый в
    /// connection.json (F-B11): переживает перезапуск приложения.
    /// </summary>
    public bool FirstConnectionDone { get; init; }
}

/// <summary>
/// Результат проверки реквизитов подключения (ТЗ §8.8, функция 102).
/// </summary>
/// <param name="IsValid">Признак корректности реквизитов.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при корректности.</param>
public sealed record ConnectionValidationResult(bool IsValid, string? Error)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsValid;

    /// <summary>Создаёт успешный результат.</summary>
    /// <returns>Успешный результат проверки.</returns>
    public static ConnectionValidationResult Success() => new(true, null);

    /// <summary>Создаёт неуспешный результат.</summary>
    /// <param name="error">Текст ошибки на русском языке.</param>
    /// <returns>Неуспешный результат проверки.</returns>
    public static ConnectionValidationResult Failure(string error) => new(false, error);
}

/// <summary>
/// Смена подключения к базе данных (ТЗ §8.8, функция 102:
/// <c>public void SwitchConnection(ConnectionDescriptor descriptor)</c>;
/// §8.11, функция 127 — единые реквизиты подключения).
/// </summary>
/// <remarks>
/// <para>
/// Реквизиты хранятся в файле настроек подключения в профиле пользователя;
/// пароль выносится в отдельное хранилище DPAPI. Смена подключения применяется
/// после перезапуска приложения.
/// </para>
/// <para>
/// Кириллица в имени базы данных и в пути к файлу базы данных запрещена:
/// ограничение связано с особенностями драйверов и файловой системы, из-за
/// которых реквизиты с кириллицей не переносятся между рабочими местами.
/// </para>
/// </remarks>
public sealed class ConnectionManager
{
    /// <summary>Имя файла настроек подключения.</summary>
    public const string SettingsFileName = "connection.json";

    /// <summary>Имя секрета с паролем в хранилище DPAPI.</summary>
    public const string PasswordSecretName = "connection-password";

    /// <summary>
    /// Текст сообщения о запрете кириллицы в имени базы данных
    /// (ТЗ §8.8, функция 102).
    /// </summary>
    public const string CyrillicDatabaseNameMessage =
        "Имя базы данных не должно содержать кириллические символы.";

    /// <summary>
    /// Текст сообщения о запрете кириллицы в пути к базе данных
    /// (ТЗ §8.8, функция 102).
    /// </summary>
    public const string CyrillicDatabasePathMessage =
        "Путь к файлу базы данных не должен содержать кириллические символы.";

    /// <summary>
    /// Текст сообщения о запрете смены имени базы данных после первого
    /// подключения (ТЗ §8.11, функция 127).
    /// </summary>
    public const string DatabaseNameLockedMessage =
        "Имя базы данных можно изменить только до первого подключения.";

    private static readonly JsonSerializerOptions ПараметрыJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _путьНастроек;
    private readonly string? _путьБазыПоУмолчанию;
    private readonly DpapiSecretStore _секреты;
    private ConnectionDescriptor _текущий;

    /// <summary>
    /// Создаёт менеджер подключения с файлом настроек в профиле пользователя.
    /// </summary>
    public ConnectionManager()
        : this(null, null)
    {
    }

    /// <summary>
    /// Создаёт менеджер подключения с указанными путями (для проверок и тестов).
    /// </summary>
    /// <param name="путьНастроек">Путь к файлу настроек подключения.</param>
    /// <param name="каталогСекретов">Каталог хранилища DPAPI.</param>
    public ConnectionManager(string? путьНастроек, string? каталогСекретов = null)
        : this(путьНастроек, каталогСекретов, null)
    {
    }

    /// <summary>
    /// Создаёт менеджер подключения с указанными путями и базой по умолчанию
    /// профиля: изолированный тестовый профиль получает собственный путь базы
    /// вместо реального профиля пользователя.
    /// </summary>
    /// <param name="путьНастроек">Путь к файлу настроек подключения.</param>
    /// <param name="каталогСекретов">Каталог хранилища DPAPI.</param>
    /// <param name="путьБазыПоУмолчанию">
    /// Путь к базе по умолчанию, записываемый в реквизиты при отсутствии
    /// файла настроек; <see langword="null"/> — реальный профиль пользователя.
    /// </param>
    public ConnectionManager(
        string? путьНастроек,
        string? каталогСекретов,
        string? путьБазыПоУмолчанию)
    {
        _путьНастроек = путьНастроек ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Picket302",
            SettingsFileName);

        _путьБазыПоУмолчанию = путьБазыПоУмолчанию;
        _секреты = new DpapiSecretStore(каталогСекретов);
        _текущий = ПрочитатьНастройки();
        IsFirstConnectionDone = _текущий.FirstConnectionDone;
    }

    /// <summary>Путь к файлу настроек подключения.</summary>
    public string SettingsPath => _путьНастроек;

    /// <summary>Действующие реквизиты подключения.</summary>
    public ConnectionDescriptor Current => _текущий;

    /// <summary>
    /// Признак того, что подключение к базе уже выполнялось: после первого
    /// подключения имя базы данных изменять нельзя (ТЗ §8.11, функция 127).
    /// </summary>
    public bool IsFirstConnectionDone { get; private set; }

    /// <summary>
    /// Признак того, что смена подключения требует перезапуска приложения
    /// (ТЗ §8.8, функция 102).
    /// </summary>
    public bool RequiresRestart { get; private set; }

    /// <summary>
    /// Проверяет реквизиты подключения (ТЗ §8.8, функция 102: «Неверные
    /// реквизиты — сообщение с деталями отказа СУБД»; кириллица в имени БД —
    /// блокировка сохранения).
    /// </summary>
    /// <param name="descriptor">Проверяемые реквизиты.</param>
    /// <returns>Результат проверки.</returns>
    public ConnectionValidationResult Validate(ConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.IsLocal)
        {
            if (string.IsNullOrWhiteSpace(descriptor.DatabasePath))
            {
                return ConnectionValidationResult.Failure("Не указан путь к файлу базы данных.");
            }

            if (СодержитКириллицу(descriptor.DatabasePath))
            {
                return ConnectionValidationResult.Failure(CyrillicDatabasePathMessage);
            }

            return ConnectionValidationResult.Success();
        }

        if (string.IsNullOrWhiteSpace(descriptor.Server))
        {
            return ConnectionValidationResult.Failure("Не указан сервер базы данных.");
        }

        if (descriptor.Port is <= 0 or > 65535)
        {
            return ConnectionValidationResult.Failure(
                $"Недопустимый порт сервера базы данных: {descriptor.Port.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.DatabaseName))
        {
            return ConnectionValidationResult.Failure("Не указано имя базы данных.");
        }

        if (СодержитКириллицу(descriptor.DatabaseName))
        {
            return ConnectionValidationResult.Failure(CyrillicDatabaseNameMessage);
        }

        if (string.IsNullOrWhiteSpace(descriptor.User))
        {
            return ConnectionValidationResult.Failure("Не указан пользователь базы данных.");
        }

        return ConnectionValidationResult.Success();
    }

    /// <summary>
    /// Меняет подключение к базе данных (ТЗ §8.8, функция 102:
    /// <c>public void SwitchConnection(ConnectionDescriptor descriptor)</c>).
    /// </summary>
    /// <param name="descriptor">Новые реквизиты подключения.</param>
    /// <exception cref="InvalidOperationException">
    /// Реквизиты не прошли проверку либо имя базы данных изменяется после
    /// первого подключения.
    /// </exception>
    public void SwitchConnection(ConnectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var проверка = Validate(descriptor);

        if (проверка.IsFailure)
        {
            throw new InvalidOperationException(проверка.Error);
        }

        if (IsFirstConnectionDone
            && !string.Equals(_текущий.DatabaseName, descriptor.DatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(DatabaseNameLockedMessage);
        }

        _текущий = descriptor;

        СохранитьНастройки();
        RequiresRestart = true;
    }

    /// <summary>
    /// Сохраняет открытый пароль в защищённое хранилище и обновляет реквизиты.
    /// </summary>
    /// <param name="descriptor">Реквизиты подключения.</param>
    /// <param name="пароль">Открытый пароль; сохраняется только в защищённом виде.</param>
    public void SwitchConnectionWithPassword(ConnectionDescriptor descriptor, string? пароль)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var защищённый = string.IsNullOrEmpty(пароль)
            ? null
            : Convert.ToBase64String(_секреты.Protect(пароль));

        SwitchConnection(descriptor with { ProtectedPassword = защищённый });
    }

    /// <summary>
    /// Возвращает открытый пароль подключения из защищённого хранилища.
    /// </summary>
    /// <returns>Пароль либо <see langword="null"/>, если он не задан.</returns>
    public string? GetPassword()
    {
        if (string.IsNullOrEmpty(_текущий.ProtectedPassword))
        {
            return null;
        }

        try
        {
            return _секреты.Unprotect(Convert.FromBase64String(_текущий.ProtectedPassword));
        }
        catch (FormatException)
        {
            // Повреждённое защищённое значение: пароль считается незаданным.
            return null;
        }
        catch (CryptographicException)
        {
            // Блок не расшифровывается текущим пользователем: пароль считается незаданным.
            return null;
        }
    }

    /// <summary>
    /// Отмечает, что подключение к базе состоялось: после этого имя базы
    /// данных изменению не подлежит (ТЗ §8.11, функция 127).
    /// </summary>
    public void MarkConnectionEstablished()
    {
        IsFirstConnectionDone = true;

        // Признак переносится в сохраняемый дескриптор (F-B11).
        _текущий = _текущий with { FirstConnectionDone = true };

        СохранитьНастройки();
    }

    /// <summary>
    /// Определяет, содержит ли строка кириллические символы.
    /// </summary>
    /// <param name="значение">Проверяемая строка.</param>
    /// <returns><see langword="true"/>, если кириллица найдена.</returns>
    public static bool СодержитКириллицу(string? значение)
    {
        if (string.IsNullOrEmpty(значение))
        {
            return false;
        }

        foreach (var символ in значение)
        {
            if (символ is >= 'Ѐ' and <= 'ӿ')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Читает реквизиты подключения из файла настроек.
    /// </summary>
    private ConnectionDescriptor ПрочитатьНастройки()
    {
        if (!File.Exists(_путьНастроек))
        {
            return ConnectionDescriptor.Local(_путьБазыПоУмолчанию ?? ПутьБазыПоУмолчанию());
        }

        try
        {
            var текст = File.ReadAllText(_путьНастроек, Encoding.UTF8);
            var реквизиты = JsonSerializer.Deserialize<ConnectionDescriptor>(текст, ПараметрыJson);

            if (реквизиты is null)
            {
                return ConnectionDescriptor.Local(_путьБазыПоУмолчанию ?? ПутьБазыПоУмолчанию());
            }

            return реквизиты;
        }
        catch (JsonException)
        {
            return ConnectionDescriptor.Local(_путьБазыПоУмолчанию ?? ПутьБазыПоУмолчанию());
        }
        catch (IOException)
        {
            return ConnectionDescriptor.Local(_путьБазыПоУмолчанию ?? ПутьБазыПоУмолчанию());
        }
    }

    /// <summary>
    /// Сохраняет реквизиты подключения в файл настроек.
    /// </summary>
    private void СохранитьНастройки()
    {
        var каталог = Path.GetDirectoryName(_путьНастроек);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        File.WriteAllText(
            _путьНастроек,
            JsonSerializer.Serialize(_текущий, ПараметрыJson),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Путь к файлу базы данных по умолчанию в профиле пользователя.
    /// </summary>
    private static string ПутьБазыПоУмолчанию() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Picket302",
        "picket302.db");
}
