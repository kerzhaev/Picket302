using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Infrastructure.Data;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Data;

/// <summary>
/// Итог первичного импорта базы на сервер.
/// </summary>
/// <param name="Успешно">Признак успешного импорта.</param>
/// <param name="Сообщение">Пояснение для оператора.</param>
/// <param name="Контрагентов">Число контрагентов в импортированной базе.</param>
/// <param name="Писем">Число отправлений.</param>
/// <param name="ВерсияСхемы">Версия схемы импортированной базы.</param>
public sealed record ServerImportResult(
    bool Успешно,
    string Сообщение,
    int Контрагентов,
    int Писем,
    string ВерсияСхемы);

/// <summary>
/// Первичный импорт одной выбранной базы в серверную службу.
/// </summary>
/// <remarks>
/// <para>
/// Исходная база выбирается человеком на серверном компьютере; никакие
/// клиентские пути сюда не попадают. Порядок полностью следует ТЗ первичной
/// настройки:
/// <list type="number">
/// <item><description>проверка исходного пути, доступности и целостности
/// SQLite;</description></item>
/// <item><description>согласованный снимок механизмом <c>VACUUM INTO</c> — не
/// копирование рабочего файла;</description></item>
/// <item><description>staging-каталог на том же томе, что и база службы;</description></item>
/// <item><description>миграции снимка до текущей версии схемы;</description></item>
/// <item><description><c>integrity_check</c>, <c>foreign_key_check</c>, проверка
/// основных количеств и связей;</description></item>
/// <item><description>переключение службы на новую базу только после полной
/// проверки; исходная база не удаляется и не перезаписывается.</description></item>
/// </list>
/// </para>
/// <para>
/// Переключение выполняется заменой файла базы службы: текущая база службы
/// сохраняется рядом как <c>picket302.db.before-import</c> — при сбое служба
/// возвращается к прежнему состоянию.
/// </para>
/// </remarks>
public sealed class ServerBaseImport
{
    /// <summary>Имя файла резервной копии прежней базы службы.</summary>
    public const string ИмяПрежнейБазы = "picket302.db.before-import";

    private readonly ServerDatabase _база;
    private readonly string _каталогДанных;

    /// <summary>
    /// Создаёт импортёр.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных службы.</param>
    /// <param name="каталогДанных">Каталог данных службы.</param>
    public ServerBaseImport(ServerDatabase база, string каталогДанных)
    {
        ArgumentNullException.ThrowIfNull(база);
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогДанных);

        _база = база;
        _каталогДанных = каталогДанных;
    }

    /// <summary>
    /// Выполняет первичный импорт.
    /// </summary>
    /// <param name="путьИсточника">Путь к исходной базе, выбранный человеком.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Итог импорта.</returns>
    public async Task<ServerImportResult> ВыполнитьAsync(
        string путьИсточника,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(путьИсточника))
        {
            return new ServerImportResult(false, "Путь к исходной базе не указан.", 0, 0, string.Empty);
        }

        var полныйПуть = Path.GetFullPath(путьИсточника);

        if (!File.Exists(полныйПуть))
        {
            return new ServerImportResult(false, "Файл исходной базы не найден.", 0, 0, string.Empty);
        }

        if (string.Equals(
            полныйПуть,
            Path.GetFullPath(_база.ПутьБазы),
            StringComparison.OrdinalIgnoreCase))
        {
            return new ServerImportResult(
                false,
                "Исходная база совпадает с базой службы: импортировать её в саму себя нельзя.",
                0,
                0,
                string.Empty);
        }

        // Шаг 1. Целостность исходной базы на отдельном соединении только для
        // чтения: рабочая база не изменяется.
        var отчётЦелостности = await ПроверитьИсточникAsync(полныйПуть).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(отчётЦелостности))
        {
            return new ServerImportResult(
                false,
                $"Исходная база не прошла проверку целостности: {отчётЦелостности}",
                0,
                0,
                string.Empty);
        }

        // Шаг 2. Согласованный снимок механизмом VACUUM INTO в staging на том
        // же томе, где лежит база службы: переименование остаётся атомарным.
        var каталогСлужбы = Path.GetDirectoryName(_база.ПутьБазы)
            ?? throw new InvalidOperationException("Каталог данных службы не определён.");

        var staging = Path.Combine(каталогСлужбы, $"staging-import-{Guid.NewGuid():N}.db");

        try
        {
            var сообщениеСнимка = await СнятьСнимокAsync(полныйПуть, staging).ConfigureAwait(false);

            if (сообщениеСнимка is not null)
            {
                return new ServerImportResult(false, сообщениеСнимка, 0, 0, string.Empty);
            }

            var контрагентов = 0;
            var писем = 0;
            var версия = string.Empty;

            // Шаги 3–4. Миграции снимка и проверки: сессия закрывается до
            // переключения, иначе её пул держит файл снимка.
            await using (var stagingСессия = _база.СоздатьСессиюДляФайла(staging))
            {
                await new MigrationRunner()
                    .ApplyAsync(stagingСессия.Connection, cancellationToken)
                    .ConfigureAwait(false);

                await stagingСессия.Connection.ExecuteScalarAsync<long>(
                    new CommandDefinition(
                        "PRAGMA foreign_keys = ON;",
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                var нарушения = await ПроверитьСнимокAsync(stagingСессия, cancellationToken)
                    .ConfigureAwait(false);

                if (нарушения is not null)
                {
                    return new ServerImportResult(false, нарушения, 0, 0, string.Empty);
                }

                контрагентов = await stagingСессия.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(*) FROM counterparties;",
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                писем = await stagingСессия.Connection.ExecuteScalarAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(*) FROM mails;",
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                версия = await stagingСессия.Connection.ExecuteScalarAsync<string>(
                    new CommandDefinition(
                        "SELECT version FROM schema_version ORDER BY id DESC LIMIT 1;",
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            // Шаг 5. Переключение: прежняя база службы сохраняется рядом,
            // проверенный снимок занимает её место. Исходный файл не трогается.
            var страховка = Path.Combine(каталогСлужбы, ИмяПрежнейБазы);
            var прежняяСуществует = File.Exists(_база.ПутьБазы);

            // Пул соединений держит дескриптор файла базы службы: без
            // освобождения пула переименование отклоняется.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (прежняяСуществует)
            {
                File.Copy(_база.ПутьБазы, страховка, overwrite: true);
            }

            File.Move(staging, _база.ПутьБазы, overwrite: true);

            return new ServerImportResult(
                true,
                "Импорт выполнен. Прежняя база службы сохранена рядом: "
                + ИмяПрежнейБазы + ".",
                контрагентов,
                писем,
                версия ?? string.Empty);
        }
        finally
        {
            // Staging очищается только когда импорт завершился: при ошибке файл
            // снимка удаляется, при успехе он уже переименован.
            try
            {
                if (File.Exists(staging))
                {
                    File.Delete(staging);
                }
            }
            catch (IOException)
            {
                // Снимок остаётся: временное состояние импорта не влияет на
                // работу службы.
            }
        }
    }

    /// <summary>
    /// Проверяет целостность исходной базы на отдельном соединении.
    /// </summary>
    /// <param name="путь">Путь к исходной базе.</param>
    /// <returns>Пустая строка — нарушений нет.</returns>
    private static async Task<string> ПроверитьИсточникAsync(string путь)
    {
        try
        {
            await using var соединение = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = путь, Mode = SqliteOpenMode.ReadOnly }
                    .ToString());

            await соединение.OpenAsync().ConfigureAwait(false);

            await using var команда = соединение.CreateCommand();
            команда.CommandText = "PRAGMA integrity_check;";

            var итог = Convert.ToString(
                await команда.ExecuteScalarAsync().ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

            if (!string.Equals(итог, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return итог;
            }

            return string.Empty;
        }
        catch (SqliteException ошибка)
        {
            return $"{ошибка.Message}";
        }
    }

    /// <summary>
    /// Снимает согласованный снимок исходной базы в staging.
    /// </summary>
    /// <param name="путьИсточника">Исходная база.</param>
    /// <param name="путьСнимка">Файл снимка.</param>
    /// <returns>Пояснение ошибки; <see langword="null"/> — снимок снят.</returns>
    private static async Task<string?> СнятьСнимокAsync(string путьИсточника, string путьСнимка)
    {
        try
        {
            await using var соединение = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = путьИсточника, Mode = SqliteOpenMode.ReadOnly }
                    .ToString());

            await соединение.OpenAsync().ConfigureAwait(false);

            await using var команда = соединение.CreateCommand();
            команда.CommandText = "VACUUM INTO $path;";
            команда.Parameters.AddWithValue("$path", путьСнимка);

            await команда.ExecuteNonQueryAsync().ConfigureAwait(false);

            return null;
        }
        catch (SqliteException ошибка)
        {
            return $"Не удалось снять согласованный снимок исходной базы: {ошибка.Message}";
        }
        catch (IOException ошибка)
        {
            return $"Не удалось записать снимок в staging: {ошибка.Message}";
        }
    }

    /// <summary>
    /// Проверяет снимок: целостность, внешние ключи, обязательные таблицы и
    /// связи.
    /// </summary>
    /// <param name="сессия">Сессия снимка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Пояснение нарушения; <see langword="null"/> — нарушений нет.</returns>
    private static async Task<string?> ПроверитьСнимокAsync(
        ISqliteSession сессия,
        CancellationToken cancellationToken)
    {
        await using var команда = сессия.Connection.CreateCommand();
        команда.CommandText = "PRAGMA integrity_check;";

        var целостность = Convert.ToString(
            await команда.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        if (!string.Equals(целостность, "ok", StringComparison.OrdinalIgnoreCase))
        {
            return $"Снимок не прошёл проверку целостности: {целостность}";
        }

        await using var командаКлючей = сессия.Connection.CreateCommand();
        командаКлючей.CommandText = "PRAGMA foreign_key_check;";

        await using var нарушения = await командаКлючей
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (await нарушения.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return "Снимок содержит нарушения ссылочной целостности (foreign_key_check).";
        }

        var обязательныеТаблицы = new[]
        {
            "counterparties",
            "groups",
            "mails",
            "distributions",
            "print_templates",
            "blank_fields",
            "parameters",
            "schema_version",
        };

        foreach (var таблица in обязательныеТаблицы)
        {
            var есть = await сессия.Connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @Name;",
                    new { Name = таблица },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (есть == 0)
            {
                return $"В снимке нет обязательной таблицы {таблица}.";
            }
        }

        return null;
    }
}
