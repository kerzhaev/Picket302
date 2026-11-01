using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Picket302.Infrastructure.Persistence;

/// <summary>
/// Запись справочника серверов БД (ТЗ §8.8, функция 101: «список серверов
/// хранится в отдельном справочнике (аналог listIp.ini — файл servers.ini)»).
/// </summary>
/// <param name="Name">Отображаемое имя сервера.</param>
/// <param name="Host">Имя узла или IP-адрес.</param>
/// <param name="Port">Порт службы БД.</param>
/// <param name="Description">Пояснение для оператора.</param>
public sealed record ServerEntry(string Name, string Host, int Port, string Description)
{
    /// <summary>Порт службы БД по умолчанию (ТЗ §8.8, функция 101).</summary>
    public const int DefaultPort = 5432;

    /// <summary>Создаёт запись с портом по умолчанию.</summary>
    /// <param name="name">Отображаемое имя сервера.</param>
    /// <param name="host">Имя узла или IP-адрес.</param>
    /// <param name="description">Пояснение для оператора.</param>
    /// <returns>Запись справочника.</returns>
    public static ServerEntry Create(string name, string host, string description)
        => new(name, host, DefaultPort, description);

    /// <summary>Текстовое представление записи для файла servers.ini.</summary>
    public string ToIniLine()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{Name}={Host};{Port};{Description}");
}

/// <summary>
/// Результат проверки доступности сервера БД (ТЗ §8.8, функция 101:
/// <c>public ConnectionCheckResult CheckServer(ServerEntry entry)</c>).
/// </summary>
/// <param name="IsSuccess">Признак успешной проверки.</param>
/// <param name="Error">Текст ошибки; <see langword="null"/> при успехе.</param>
/// <param name="LatencyMilliseconds">Задержка отклика, мс.</param>
public sealed record ConnectionCheckResult(bool IsSuccess, string? Error, long LatencyMilliseconds)
{
    /// <summary>Признак неуспешного исхода.</summary>
    public bool IsFailure => !IsSuccess;
}

/// <summary>
/// Роль текущей машины в клиент-серверной топологии (ТЗ §8.11, функция 127).
/// </summary>
public enum TopologyRole
{
    /// <summary>Локальный режим: сервера БД нет.</summary>
    LocalOnly = 0,

    /// <summary>Машина — сервер общей БД, клиентов не подключено.</summary>
    ServerWithoutClients = 1,

    /// <summary>Машина — клиент общей БД.</summary>
    ClientWithServer = 2,

    /// <summary>Роль определить не удалось.</summary>
    Unknown = 3,

    /// <summary>Машина — сервер общей БД, клиенты подключены.</summary>
    ServerWithClients = 4,
}

/// <summary>
/// Состояние клиент-серверной топологии (ТЗ §8.11, функция 127:
/// <c>public TopologyInfo DiagnoseTopology()</c>).
/// </summary>
/// <param name="Role">Роль текущей машины.</param>
/// <param name="IsConnected">Признак установленной связи с сервером БД.</param>
/// <param name="ServerName">Имя сервера, с которым установлена связь.</param>
/// <param name="Message">Пояснение для оператора.</param>
/// <param name="SchemaMismatch">
/// Описание несовпадения версий схемы клиента и сервера; <see langword="null"/>
/// при совпадении.
/// </param>
public sealed record TopologyInfo(
    TopologyRole Role,
    bool IsConnected,
    string? ServerName,
    string Message,
    string? SchemaMismatch)
{
    /// <summary>Признак блокировки работы из-за несовпадения версий схемы.</summary>
    public bool IsBlockedBySchema => SchemaMismatch is not null;
}

/// <summary>
/// Справочник серверов БД: чтение и запись файла servers.ini, проверка
/// доступности сервера и диагностика топологии (ТЗ §8.8, функция 101;
/// §8.11, функция 127).
/// </summary>
/// <remarks>
/// <para>
/// Формат файла <c>servers.ini</c> — построчный INI: секция <c>[Servers]</c>,
/// внутри строки <c>Имя=Узел;Порт;Описание</c>. Пустые строки и строки с
/// символом «#» пропускаются. Такой формат переносим между рабочими местами
/// и читается без специальных средств.
/// </para>
/// <para>
/// Проверка доступности выполняется обычным TCP-соединением: так проверяется
/// именно сетевая доступность службы, а не протокол СУБД.
/// </para>
/// </remarks>
public sealed class ServerRegistry
{
    /// <summary>Имя файла справочника серверов.</summary>
    public const string FileName = "servers.ini";

    /// <summary>Имя секции файла справочника.</summary>
    public const string SectionName = "Servers";

    /// <summary>Порт службы БД по умолчанию.</summary>
    public const int DefaultPort = ServerEntry.DefaultPort;

    /// <summary>Таймаут проверки доступности сервера, мс.</summary>
    public const int CheckTimeoutMilliseconds = 3000;

    private readonly string _путьФайла;

    /// <summary>
    /// Создаёт справочник серверов с файлом в профиле пользователя.
    /// </summary>
    public ServerRegistry()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Picket302",
            FileName))
    {
    }

    /// <summary>
    /// Создаёт справочник серверов с указанным файлом.
    /// </summary>
    /// <param name="путьФайла">Путь к файлу servers.ini.</param>
    public ServerRegistry(string путьФайла)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(путьФайла);
        _путьФайла = Path.GetFullPath(путьФайла);
    }

    /// <summary>Путь к файлу справочника.</summary>
    public string FilePath => _путьФайла;

    /// <summary>
    /// Возвращает перечень серверов из справочника (ТЗ §8.8, функция 101:
    /// <c>public IReadOnlyList&lt;ServerEntry&gt; ListServers()</c>).
    /// </summary>
    /// <returns>Перечень серверов; пустой — справочник пуст или отсутствует.</returns>
    public IReadOnlyList<ServerEntry> ListServers()
    {
        if (!File.Exists(_путьФайла))
        {
            return Array.Empty<ServerEntry>();
        }

        var серверы = new List<ServerEntry>();
        var внутриСекции = false;

        foreach (var сыраяСтрока in File.ReadAllLines(_путьФайла, Encoding.UTF8))
        {
            var строка = сыраяСтрока.Trim();

            if (строка.Length == 0 || строка.StartsWith('#'))
            {
                continue;
            }

            if (строка.StartsWith('['))
            {
                внутриСекции = string.Equals(
                    строка.Trim('[', ']').Trim(),
                    SectionName,
                    StringComparison.OrdinalIgnoreCase);

                continue;
            }

            if (!внутриСекции)
            {
                continue;
            }

            var сервер = РазобратьСтроку(строка);

            if (сервер is not null)
            {
                серверы.Add(сервер);
            }
        }

        return серверы;
    }

    /// <summary>
    /// Сохраняет перечень серверов в справочник.
    /// </summary>
    /// <param name="серверы">Сохраняемый перечень серверов.</param>
    public void SaveServers(IEnumerable<ServerEntry> серверы)
    {
        ArgumentNullException.ThrowIfNull(серверы);

        var каталог = Path.GetDirectoryName(_путьФайла);

        if (!string.IsNullOrEmpty(каталог))
        {
            Directory.CreateDirectory(каталог);
        }

        var строки = new List<string> { $"[{SectionName}]" };

        foreach (var сервер in серверы)
        {
            строки.Add(сервер.ToIniLine());
        }

        File.WriteAllLines(_путьФайла, строки, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Добавляет сервер в справочник (запись с тем же именем заменяется).
    /// </summary>
    /// <param name="сервер">Добавляемый сервер.</param>
    public void AddServer(ServerEntry сервер)
    {
        ArgumentNullException.ThrowIfNull(сервер);

        var серверы = ListServers().ToList();

        серверы.RemoveAll(существующий =>
            string.Equals(существующий.Name, сервер.Name, StringComparison.OrdinalIgnoreCase));

        серверы.Add(сервер);

        SaveServers(серверы);
    }

    /// <summary>
    /// Удаляет сервер из справочника.
    /// </summary>
    /// <param name="имя">Имя удаляемого сервера.</param>
    /// <returns><see langword="true"/>, если сервер удалён.</returns>
    public bool RemoveServer(string имя)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(имя);

        var серверы = ListServers().ToList();
        var удалено = серверы.RemoveAll(сервер =>
            string.Equals(сервер.Name, имя, StringComparison.OrdinalIgnoreCase));

        if (удалено == 0)
        {
            return false;
        }

        SaveServers(серверы);

        return true;
    }

    /// <summary>
    /// Проверяет доступность сервера БД (ТЗ §8.8, функция 101:
    /// <c>public ConnectionCheckResult CheckServer(ServerEntry entry)</c>).
    /// </summary>
    /// <param name="сервер">Проверяемый сервер.</param>
    /// <returns>Результат проверки с текстом ошибки при недоступности.</returns>
    public ConnectionCheckResult CheckServer(ServerEntry сервер)
    {
        ArgumentNullException.ThrowIfNull(сервер);

        if (string.IsNullOrWhiteSpace(сервер.Host))
        {
            return new ConnectionCheckResult(
                false,
                $"Не удалось подключиться к серверу БД {сервер.Name}:{сервер.Port}. Не задан адрес сервера.",
                0);
        }

        var порт = сервер.Port > 0 ? сервер.Port : DefaultPort;
        var замер = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var клиент = new TcpClient();

            var подключение = клиент.ConnectAsync(сервер.Host, порт)
                .Wait(CheckTimeoutMilliseconds);

            if (!подключение)
            {
                return new ConnectionCheckResult(
                    false,
                    $"Не удалось подключиться к серверу БД {сервер.Name}:{порт}. "
                    + "Проверьте сеть, адрес сервера и настройки брандмауэра.",
                    замер.ElapsedMilliseconds);
            }

            замер.Stop();

            return new ConnectionCheckResult(true, null, замер.ElapsedMilliseconds);
        }
        catch (AggregateException)
        {
            return new ConnectionCheckResult(
                false,
                $"Не удалось подключиться к серверу БД {сервер.Name}:{порт}. "
                + "Проверьте сеть, адрес сервера и настройки брандмауэра.",
                замер.ElapsedMilliseconds);
        }
        catch (SocketException)
        {
            return new ConnectionCheckResult(
                false,
                $"Не удалось подключиться к серверу БД {сервер.Name}:{порт}. "
                + "Проверьте сеть, адрес сервера и настройки брандмауэра.",
                замер.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Определяет роль текущей машины и состояние связи (ТЗ §8.11, функция 127:
    /// <c>public TopologyInfo DiagnoseTopology()</c>).
    /// </summary>
    /// <param name="текущийХост">
    /// Узел текущего подключения; <see langword="null"/> — подключение локальное.
    /// </param>
    /// <param name="схемаКлиента">Версия схемы на клиенте.</param>
    /// <param name="схемаСервера">
    /// Версия схемы на сервере; <see langword="null"/> — неизвестна.
    /// </param>
    /// <returns>Состояние топологии.</returns>
    public TopologyInfo DiagnoseTopology(
        string? текущийХост = null,
        string? схемаКлиента = null,
        string? схемаСервера = null)
    {
        var серверы = ListServers();

        if (string.IsNullOrWhiteSpace(текущийХост))
        {
            return new TopologyInfo(
                TopologyRole.LocalOnly,
                false,
                null,
                серверы.Count > 0
                    ? $"Локальный режим: подключение к файлу базы данных. В справочнике указано серверов: {серверы.Count}."
                    : "Локальный режим: подключение к файлу базы данных.",
                null);
        }

        var сервер = серверы.FirstOrDefault(запись =>
            string.Equals(запись.Host, текущийХост, StringComparison.OrdinalIgnoreCase));

        var имяСервера = сервер?.Name ?? текущийХост;
        var проверка = сервер is null
            ? new ConnectionCheckResult(false, "Сервер не найден в справочнике servers.ini.", 0)
            : CheckServer(сервер);

        var несовпадение = СравнитьСхемы(схемаКлиента, схемаСервера);

        var сообщение = проверка.IsSuccess
            ? $"Клиент общей БД: сервер «{имяСервера}» доступен."
            : $"Клиент общей БД: сервер «{имяСервера}» недоступен. {проверка.Error}";

        if (несовпадение is not null)
        {
            сообщение += " " + несовпадение;
        }

        return new TopologyInfo(
            TopologyRole.ClientWithServer,
            проверка.IsSuccess,
            имяСервера,
            сообщение,
            несовпадение);
    }

    /// <summary>
    /// Определяет роль текущей машины по реквизитам подключения (ТЗ §8.11,
    /// функция 127: «Диагностика сообщает роль текущей машины и состояние
    /// связи»; поддерживаются конфигурации «сервер с общей БД + N клиентов»,
    /// «клиент без сервера (локальная SQLite)», «сервер без клиентов
    /// (головной офис)»).
    /// </summary>
    /// <param name="подключение">Реквизиты текущего подключения.</param>
    /// <param name="подключённыхКлиентов">Число клиентов, подключённых к общей БД.</param>
    /// <param name="схемаКлиента">Версия схемы на клиенте.</param>
    /// <param name="схемаСервера">Версия схемы на сервере.</param>
    /// <returns>Состояние топологии.</returns>
    /// <exception cref="ArgumentNullException">Реквизиты подключения не заданы.</exception>
    public TopologyInfo DiagnoseTopology(
        ConnectionDescriptor подключение,
        int подключённыхКлиентов,
        string? схемаКлиента = null,
        string? схемаСервера = null)
    {
        ArgumentNullException.ThrowIfNull(подключение);

        var серверы = ListServers();

        if (ЭтоЛокальныйУзел(подключение.Server))
        {
            // Машина размещает общую БД: сервер с клиентами либо сервер
            // без клиентов (головной офис) (ТЗ §8.11, функция 127).
            var имяСервера = серверы.FirstOrDefault(запись => ЭтоЛокальныйУзел(запись.Host))?.Name
                ?? подключение.Server;

            var роль = подключённыхКлиентов > 0
                ? TopologyRole.ServerWithClients
                : TopologyRole.ServerWithoutClients;

            var сообщение = подключённыхКлиентов > 0
                ? $"Сервер общей БД «{имяСервера}»: подключено клиентов — {подключённыхКлиентов}."
                : $"Сервер общей БД «{имяСервера}»: клиенты не подключены (головной офис).";

            return new TopologyInfo(роль, true, имяСервера, сообщение, null);
        }

        if (подключение.IsLocal)
        {
            // Клиент без сервера БД: локальная SQLite (ТЗ §9.5).
            var сообщение = серверы.Count > 0
                ? $"Локальный режим: подключение к файлу базы данных. В справочнике указано серверов: {серверы.Count}."
                : "Локальный режим: подключение к файлу базы данных.";

            return new TopologyInfo(TopologyRole.LocalOnly, false, null, сообщение, null);
        }

        // Клиент общей БД: сервер указан один раз, логин и пароль берутся
        // из защищённого файла подключения (ТЗ §9.5).
        var сервер = серверы.FirstOrDefault(запись =>
            string.Equals(запись.Host, подключение.Server, StringComparison.OrdinalIgnoreCase));

        var имя = сервер?.Name ?? подключение.Server;

        var проверка = сервер is null
            ? new ConnectionCheckResult(false, "Сервер не найден в справочнике servers.ini.", 0)
            : CheckServer(сервер);

        var несовпадение = СравнитьСхемы(схемаКлиента, схемаСервера);

        var текст = проверка.IsSuccess
            ? $"Клиент общей БД: сервер «{имя}» доступен."
            : $"Клиент общей БД: сервер «{имя}» недоступен. {проверка.Error}";

        if (несовпадение is not null)
        {
            текст += " " + несовпадение;
        }

        return new TopologyInfo(
            TopologyRole.ClientWithServer,
            проверка.IsSuccess,
            имя,
            текст,
            несовпадение);
    }

    /// <summary>
    /// Определяет, указывает ли адрес на текущую машину: сервер общей БД
    /// размещается на этом же компьютере (ТЗ §8.11, функция 127).
    /// </summary>
    /// <param name="узел">Проверяемый узел.</param>
    /// <returns><see langword="true"/>, если узел — текущая машина.</returns>
    private static bool ЭтоЛокальныйУзел(string узел)
    {
        if (string.IsNullOrWhiteSpace(узел))
        {
            return false;
        }

        if (string.Equals(узел, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(узел, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(узел, "::1", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(узел, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            foreach (var адрес in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
            {
                if (string.Equals(адрес.ToString(), узел, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Не удалось определить адреса машины — считаем узел удалённым.
        }

        return false;
    }

    /// <summary>
    /// Сравнивает версии схемы клиента и сервера (ТЗ §8.11, функция 127:
    /// несовпадение версий схемы — блокировка с указанием версий).
    /// </summary>
    /// <param name="схемаКлиента">Версия схемы на клиенте.</param>
    /// <param name="схемаСервера">Версия схемы на сервере.</param>
    /// <returns>Описание несовпадения либо <see langword="null"/>.</returns>
    public static string? СравнитьСхемы(string? схемаКлиента, string? схемаСервера)
    {
        if (string.IsNullOrWhiteSpace(схемаКлиента) || string.IsNullOrWhiteSpace(схемаСервера))
        {
            return null;
        }

        if (string.Equals(схемаКлиента, схемаСервера, StringComparison.Ordinal))
        {
            return null;
        }

        return $"Несовпадение версий схемы: клиент — {схемаКлиента}, сервер — {схемаСервера}.";
    }

    /// <summary>
    /// Разбирает строку справочника вида <c>Имя=Узел;Порт;Описание</c>.
    /// </summary>
    private static ServerEntry? РазобратьСтроку(string строка)
    {
        var позиция = строка.IndexOf('=');

        if (позиция <= 0)
        {
            return null;
        }

        var имя = строка[..позиция].Trim();
        var значения = строка[(позиция + 1)..].Split(';');

        if (имя.Length == 0 || значения.Length == 0 || string.IsNullOrWhiteSpace(значения[0]))
        {
            return null;
        }

        var узел = значения[0].Trim();
        var порт = DefaultPort;

        if (значения.Length > 1
            && int.TryParse(значения[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var разобранныйПорт)
            && разобранныйПорт is > 0 and <= 65535)
        {
            порт = разобранныйПорт;
        }

        var описание = значения.Length > 2 ? значения[2].Trim() : string.Empty;

        return new ServerEntry(имя, узел, порт, описание);
    }
}
