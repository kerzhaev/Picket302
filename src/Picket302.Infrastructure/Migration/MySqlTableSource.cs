using System.Data;
using System.Globalization;
using System.Text;

using MySqlConnector;

namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Источник данных исходной MySQL-схемы (ТЗ §8.9, функция 117: «Импорт базы из
/// исходной MySQL-схемы (8 таблиц)»).
/// </summary>
/// <remarks>
/// <para>
/// Чтение выполняется драйвером MySqlConnector в кодировке <c>cp1251</c>, которой
/// размечена исходная база (EVIDENCE/schema_reference.sql: <c>DEFAULT CHARSET=cp1251</c>).
/// Таким образом текст сразу приходит декодированным, а в целевой базе хранится
/// в UTF-8 (SQLite хранит текст в UTF-8 по определению).
/// </para>
/// <para>
/// Пароль не попадает ни в журнал, ни в текст ошибки: в сообщениях об ошибке
/// указываются только хост, порт, имя базы и пользователь.
/// </para>
/// </remarks>
public sealed class MySqlTableSource : ILegacyTableSource, IDisposable
{
    /// <summary>Кодировка исходной базы (EVIDENCE/schema_reference.sql).</summary>
    public const string КодировкаИсходнойБазы = "cp1251";

    /// <summary>Номер кодовой страницы cp1251.</summary>
    public const int НомерКодовойСтраницы = 1251;

    private static readonly object ЗамокПровайдера = new();

    private static bool _провайдерЗарегистрирован;

    private readonly string _строкаПодключения;
    private readonly string _хост;
    private readonly int _порт;
    private readonly string _база;
    private readonly string _пользователь;

    private MySqlConnection? _соединение;
    private bool _освобождено;

    /// <summary>
    /// Создаёт источник данных исходной MySQL-схемы.
    /// </summary>
    /// <param name="host">Хост сервера MySQL.</param>
    /// <param name="port">Порт сервера MySQL.</param>
    /// <param name="db">Имя базы данных.</param>
    /// <param name="user">Имя пользователя.</param>
    /// <param name="password">Пароль пользователя; в журнал не выводится.</param>
    public MySqlTableSource(string host, int port, string db, string user, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Порт сервера MySQL задан неверно.");
        }

        _хост = host.Trim();
        _порт = port;
        _база = db.Trim();
        _пользователь = user.Trim();

        var builder = new MySqlConnectionStringBuilder
        {
            Server = _хост,
            Port = (uint)port,
            Database = _база,
            UserID = _пользователь,
            Password = password ?? string.Empty,
            CharacterSet = КодировкаИсходнойБазы,
            Pooling = false,
            AllowUserVariables = false,
        };

        _строкаПодключения = builder.ConnectionString;
    }

    /// <summary>
    /// Преобразует байты исходной базы (cp1251) в текст.
    /// </summary>
    /// <param name="bytes">Байты в кодировке cp1251.</param>
    /// <returns>Текст в Unicode.</returns>
    public static string ConvertCp1251ToText(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        ЗарегистрироватьПровайдер();

        return Encoding.GetEncoding(НомерКодовойСтраницы).GetString(bytes);
    }

    /// <summary>
    /// Преобразует текст в байты кодировки cp1251 исходной базы.
    /// </summary>
    /// <param name="text">Текст.</param>
    /// <returns>Байты в кодировке cp1251.</returns>
    public static byte[] EncodeCp1251(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ЗарегистрироватьПровайдер();

        return Encoding.GetEncoding(НомерКодовойСтраницы).GetBytes(text);
    }

    /// <summary>
    /// Преобразует текст в байты UTF-8 — формат хранения текста в целевой базе.
    /// </summary>
    /// <param name="text">Текст.</param>
    /// <returns>Байты UTF-8.</returns>
    public static byte[] ConvertTextToUtf8(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Encoding.UTF8.GetBytes(text);
    }

    /// <inheritdoc />
    public async Task<bool> TryOpenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_освобождено, this);

        try
        {
            await ОткрытьСоединениеAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
#pragma warning disable CA1031 // Подключение проверяется без исключения: результат проверки — признак доступности.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LegacyTable>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_освобождено, this);

        var соединение = await ОткрытьСоединениеAsync(cancellationToken).ConfigureAwait(false);

        // Список имён таблиц материализуется до чтения столбцов (F-B3):
        // прежде чтение столбцов выполнялось при открытом reader SHOW TABLES,
        // а соединение MySql не допускает вложенных читателей.
        var имена = new List<string>();

        using (var command = соединение.CreateCommand())
        {
            command.CommandText = "SHOW TABLES;";

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var имя = reader.IsDBNull(0) ? null : reader.GetString(0);

                if (!string.IsNullOrWhiteSpace(имя))
                {
                    имена.Add(имя);
                }
            }
        }

        var таблицы = new List<LegacyTable>(имена.Count);

        foreach (var имя in имена)
        {
            var столбцы = await ПрочитатьСтолбцыAsync(соединение, имя, cancellationToken).ConfigureAwait(false);

            таблицы.Add(new LegacyTable(имя, столбцы));
        }

        return таблицы;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LegacyRow>> GetRowsAsync(
        string table,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_освобождено, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var соединение = await ОткрытьСоединениеAsync(cancellationToken).ConfigureAwait(false);

        var строки = new List<LegacyRow>();

        using var command = соединение.CreateCommand();
        command.CommandText = $"SELECT * FROM `{Экранировать(table)}`;";

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var значения = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (var номер = 0; номер < reader.FieldCount; номер++)
            {
                var имя = reader.GetName(номер);

                if (reader.IsDBNull(номер))
                {
                    значения[имя] = null;
                    continue;
                }

                var значение = reader.GetValue(номер);

                значения[имя] = значение switch
                {
                    // Двоичные значения передаются в нейтральной модели текстом Base64.
                    byte[] байты => Convert.ToBase64String(байты),
                    _ => Convert.ToString(значение, CultureInfo.InvariantCulture),
                };
            }

            строки.Add(new LegacyRow(значения));
        }

        return строки;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_освобождено)
        {
            return;
        }

        _освобождено = true;
        _соединение?.Dispose();
        _соединение = null;
    }

    /// <summary>Открывает соединение с исходной базой.</summary>
    private async Task<MySqlConnection> ОткрытьСоединениеAsync(CancellationToken cancellationToken)
    {
        if (_соединение is { State: ConnectionState.Open })
        {
            return _соединение;
        }

        var соединение = new MySqlConnection(_строкаПодключения);

        try
        {
            await соединение.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException ошибка)
        {
            await соединение.DisposeAsync().ConfigureAwait(false);

            throw new LegacyMigrationException(
                $"Не удалось подключиться к исходной базе MySQL {_хост}:{_порт} (база «{_база}», пользователь «{_пользователь}»): {ошибка.Message}",
                ошибка);
        }
        catch (Exception)
        {
            // Отмена или прочий отказ открытия: соединение освобождается,
            // чтобы не остаться висящим (F-B12).
            await соединение.DisposeAsync().ConfigureAwait(false);

            throw;
        }

        _соединение?.Dispose();
        _соединение = соединение;

        return соединение;
    }

    /// <summary>Читает список столбцов таблицы.</summary>
    private static async Task<IReadOnlyList<string>> ПрочитатьСтолбцыAsync(
        MySqlConnection соединение,
        string table,
        CancellationToken cancellationToken)
    {
        var столбцы = new List<string>();

        using var command = соединение.CreateCommand();
        command.CommandText = $"SHOW COLUMNS FROM `{Экранировать(table)}`;";

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var имя = reader.IsDBNull(0) ? null : reader.GetString(0);

            if (!string.IsNullOrWhiteSpace(имя))
            {
                столбцы.Add(имя);
            }
        }

        return столбцы;
    }

    /// <summary>Экранирует имя таблицы для подстановки в текст запроса.</summary>
    private static string Экранировать(string имя) => имя.Replace("`", "``", StringComparison.Ordinal);

    /// <summary>
    /// Регистрирует провайдер кодовых страниц: без него недоступна кодировка
    /// cp1251, которой размечена исходная база.
    /// </summary>
    private static void ЗарегистрироватьПровайдер()
    {
        lock (ЗамокПровайдера)
        {
            if (_провайдерЗарегистрирован)
            {
                return;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _провайдерЗарегистрирован = true;
        }
    }
}
