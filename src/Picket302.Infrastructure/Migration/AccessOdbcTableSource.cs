using System.Data;
using System.Data.Odbc;
using System.Globalization;

namespace Picket302.Infrastructure.Migration;

/// <summary>
/// Источник данных исходной Access-схемы: файл .mdb через ODBC-провайдер
/// Microsoft Access Database Engine (ТЗ §8.9, функция 118: «Импорт базы из
/// исходной Access-схемы (14 таблиц)»).
/// </summary>
/// <remarks>
/// <para>
/// Чтение выполняется драйвером <c>Microsoft Access Driver (*.mdb, *.accdb)</c>.
/// Если драйвер не установлен, источник сообщает об этом отдельным понятным
/// сообщением на русском языке.
/// </para>
/// <para>
/// Занятость файла проверяется до подключения: файл, открытый другим процессом,
/// не читается ODBC-драйвером, поэтому источник сообщает об этом заранее —
/// с указанием закрыть приложение-владелец.
/// </para>
/// </remarks>
public sealed class AccessOdbcTableSource : ILegacyTableSource, IDisposable
{
    /// <summary>
    /// Сообщение об отсутствии драйвера ODBC для файлов .mdb.
    /// </summary>
    public const string СообщениеНетДрайвера =
        "Для импорта .mdb требуется драйвер Microsoft Access Database Engine (ODBC). "
        + "Установите его и повторите миграцию.";

    /// <summary>
    /// Сообщение о занятости файла базы другим процессом.
    /// </summary>
    public const string СообщениеФайлЗанят =
        "Файл базы занят другим процессом. Закройте приложение-владелец и повторите миграцию.";

    private static readonly string[] ШаблоныСтрокПодключения =
    [
        "Driver={Microsoft Access Driver (*.mdb, *.accdb)};DBQ={0};",
        "Driver={Microsoft Access Driver (*.mdb, *.accdb)};Dbq={0};",
        "Driver={Microsoft Access Driver (*.mdb)};DBQ={0};",
        "DSN=MS Access Database;DBQ={0};",
        "Driver={Microsoft Jet 4.0 Database Engine};DBQ={0};",
    ];

    private readonly string _путь;
    private OdbcConnection? _соединение;
    private bool _освобождено;

    /// <summary>
    /// Создаёт источник данных исходной Access-схемы.
    /// </summary>
    /// <param name="mdbPath">Путь к файлу .mdb.</param>
    public AccessOdbcTableSource(string mdbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mdbPath);

        _путь = Path.GetFullPath(mdbPath);
    }

    /// <summary>
    /// Проверяет, что файл базы не открыт другим процессом.
    /// </summary>
    /// <param name="mdbPath">Путь к файлу .mdb.</param>
    /// <exception cref="LegacyMigrationException">
    /// Файл занят другим процессом либо недоступен для чтения.
    /// </exception>
    public static void ПроверитьДоступностьФайла(string mdbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mdbPath);

        var путь = Path.GetFullPath(mdbPath);

        if (!File.Exists(путь))
        {
            throw new LegacyMigrationException($"Файл базы «{путь}» не найден.");
        }

        try
        {
            using var поток = new FileStream(путь, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ошибка)
        {
            throw new LegacyMigrationException(СообщениеФайлЗанят, ошибка);
        }
        catch (UnauthorizedAccessException ошибка)
        {
            throw new LegacyMigrationException(СообщениеФайлЗанят, ошибка);
        }
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
#pragma warning disable CA1031 // Результат проверки — признак доступности источника.
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
        var таблицы = new List<LegacyTable>();

        foreach (var имя in ПрочитатьИменаТаблиц(соединение))
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
        command.CommandText = $"SELECT * FROM [{table.Replace("]", "]]", StringComparison.Ordinal)}];";

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

                значения[имя] = ПривестиЗначение(reader.GetValue(номер));
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

    /// <summary>Открывает соединение с файлом .mdb через ODBC.</summary>
    private async Task<OdbcConnection> ОткрытьСоединениеAsync(CancellationToken cancellationToken)
    {
        if (_соединение is { State: ConnectionState.Open })
        {
            return _соединение;
        }

        Exception? последняяОшибка = null;

        foreach (var шаблон in ШаблоныСтрокПодключения)
        {
            var строкаПодключения = string.Format(CultureInfo.InvariantCulture, шаблон, _путь);
            var соединение = new OdbcConnection(строкаПодключения);

            try
            {
                await соединение.OpenAsync(cancellationToken).ConfigureAwait(false);

                _соединение?.Dispose();
                _соединение = соединение;

                return соединение;
            }
#pragma warning disable CA1031 // Перебираются все известные варианты строки подключения.
            catch (Exception ошибка)
#pragma warning restore CA1031
            {
                последняяОшибка = ошибка;
                await соединение.DisposeAsync().ConfigureAwait(false);
            }
        }

        throw new LegacyMigrationException(СообщениеНетДрайвера, последняяОшибка!);
    }

    /// <summary>Читает имена таблиц файла .mdb.</summary>
    private static IReadOnlyList<string> ПрочитатьИменаТаблиц(OdbcConnection соединение)
    {
        var таблицы = new List<string>();

        try
        {
            var схема = соединение.GetSchema("Tables");

            foreach (DataRow строка in схема.Rows)
            {
                var имя = строка["TABLE_NAME"]?.ToString();
                var тип = строка["TABLE_TYPE"]?.ToString();

                if (string.IsNullOrWhiteSpace(имя))
                {
                    continue;
                }

                if (тип is not null && !string.Equals(тип, "TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                таблицы.Add(имя);
            }

            return таблицы;
        }
#pragma warning disable CA1031 // Метаданные могут быть недоступны: используется запасной способ.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Некоторые драйверы не отдают метаданные: используется системная
            // таблица MSysObjects с тем же набором имён.
            таблицы.Clear();
        }

        using var command = соединение.CreateCommand();
        command.CommandText = "SELECT table_name FROM MSysObjects WHERE type = 'TABLE' AND flags IN (0, 1);";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var имя = reader.IsDBNull(0) ? null : reader.GetString(0);

            if (!string.IsNullOrWhiteSpace(имя))
            {
                таблицы.Add(имя);
            }
        }

        return таблицы;
    }

    /// <summary>Читает имена столбцов таблицы файла .mdb.</summary>
    private static async Task<IReadOnlyList<string>> ПрочитатьСтолбцыAsync(
        OdbcConnection соединение,
        string table,
        CancellationToken cancellationToken)
    {
        var столбцы = new List<string>();

        using var command = соединение.CreateCommand();
        command.CommandText = $"SELECT * FROM [{table.Replace("]", "]]", StringComparison.Ordinal)}] WHERE 1 = 0;";

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        for (var номер = 0; номер < reader.FieldCount; номер++)
        {
            столбцы.Add(reader.GetName(номер));
        }

        return столбцы;
    }

    /// <summary>
    /// Приводит значение ячейки к тексту нейтральной модели: двоичные значения
    /// (в исходной схеме — идентификаторы связей) приводятся к строке, числа и
    /// даты — к инвариантному тексту.
    /// </summary>
    /// <param name="значение">Значение ячейки.</param>
    /// <returns>Текстовое значение.</returns>
    private static string? ПривестиЗначение(object значение)
    {
        switch (значение)
        {
            case null:
                return null;

            case byte[] байты when байты.Length == 16:
                return new Guid(байты).ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();

            case byte[] байты:
                return Convert.ToBase64String(байты);

            case bool признак:
                return признак ? "1" : "0";

            case DateTime дата:
                return дата.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            case decimal число:
                return число.ToString(CultureInfo.InvariantCulture);

            case double вещественное:
                return вещественное.ToString("R", CultureInfo.InvariantCulture);

            default:
                return ПривестиИдентификатор(Convert.ToString(значение, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Приводит значение к единому виду, если это идентификатор связи: строки
    /// вида GUID приводятся к каноническому формату с дефисами.
    /// </summary>
    /// <param name="текст">Текст значения.</param>
    /// <returns>Нормализованный текст значения.</returns>
    private static string? ПривестиИдентификатор(string? текст)
    {
        if (текст is null)
        {
            return null;
        }

        var обрезанный = текст.Trim();

        if (обрезанный.Length == 0)
        {
            return null;
        }

        if (обрезанный.Length is 32 or 36 && Guid.TryParse(обрезанный, out var идентификатор))
        {
            return идентификатор.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
        }

        return обрезанный;
    }
}
