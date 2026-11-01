using Dapper;

using Microsoft.Data.Sqlite;

using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Migration;

using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка загрузки данных из предыдущей (бесплатной) версии Picket302
/// (ТЗ §8.9, функция 107).
/// </summary>
/// <remarks>
/// Источник прежней установки — подготовленная папка с файлом базы данных
/// SQLite: контрагенты, группы, пользовательские поля, письма, история печати и
/// настройки прежней версии. Целевая база — SQLite в памяти с применёнными
/// миграциями схемы.
/// </remarks>
public sealed class LegacyUpgradeImporterTests
{
    /// <summary>
    /// Контрагенты, группы, поля, письма и история печати прежней версии
    /// переносятся в текущую схему.
    /// </summary>
    [Fact]
    public async Task ИмпортИзПрежнейВерсии_ПереноситКонтрагентовГруппыПоляПисьмаИИсторию()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        using var прежняя = new ВременнаяПапка();

        СоздатьПрежнююБазу(Path.Combine(прежняя.Путь, "picket302.db"));

        var импортёр = new LegacyUpgradeImporter(база.Session);
        var отчёт = импортёр.ImportFromLegacy(прежняя.Путь);

        var счётчикКонтрагентов = отчёт.Tables.Single(счёт => счёт.LegacyTable == "counterparties");
        Assert.Equal(2, счётчикКонтрагентов.Transferred);
        Assert.Equal(0, счётчикКонтрагентов.Skipped);

        // Адресная книга и группы.
        var контрагенты = (await база.Connection.QueryAsync<(long Id, string Name, string? Inn, long? GroupId)>(
            "SELECT id, name, inn, group_id FROM counterparties ORDER BY id;"))
            .ToArray();

        Assert.Equal(2, контрагенты.Length);

        var отправитель = контрагенты.Single(запись => запись.Name == "ООО «Ромашка»");
        Assert.Equal("7707083893", отправитель.Inn);
        Assert.NotNull(отправитель.GroupId);

        var получатель = контрагенты.Single(запись => запись.Name == "Иванов Иван Иванович");
        Assert.Null(получатель.Inn);
        Assert.NotNull(получатель.GroupId);

        var группа = await база.Connection.QuerySingleAsync<string>("SELECT name FROM groups WHERE name = 'Основные';");
        Assert.Equal("Основные", группа);

        // Пользовательское поле переносится вместе с контрагентом.
        var поле = await база.Connection.QuerySingleAsync<string>(
            "SELECT field_value FROM counterparty_fields WHERE field_name = 'Договор';");

        Assert.Equal("Д-42", поле);

        // Письмо и история печати.
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM mails;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM print_history;"));

        var снимок = await база.Connection.QuerySingleAsync<string>(
            "SELECT recipient_address_snapshot FROM print_history;");

        Assert.Equal("101000 Иванов Иван Иванович", снимок);
    }

    /// <summary>
    /// Настройки подключения прежней системы не переносятся: они перечислены
    /// в отчёте и отсутствуют в целевой базе.
    /// </summary>
    [Fact]
    public async Task НастройкиПодключенияПрежнейВерсии_НеПереносятся()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        using var прежняя = new ВременнаяПапка();

        СоздатьПрежнююБазу(Path.Combine(прежняя.Путь, "picket302.db"));

        var импортёр = new LegacyUpgradeImporter(база.Session);
        var отчёт = импортёр.ImportFromLegacy(прежняя.Путь);

        // Обычная настройка переносится.
        Assert.Equal(
            "42",
            await база.Connection.QuerySingleAsync<string>("SELECT value FROM parameters WHERE name = 'LastOutgoingNumber';"));

        // Настройки подключения прежней системы не переносятся.
        Assert.Equal(0, await база.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM parameters WHERE name IN ('DatabasePath', 'ServerHost', 'SmtpPassword');"));

        Assert.Contains(отчёт.Warnings, предупреждение =>
            предупреждение.Contains("не переносятся", StringComparison.Ordinal)
            && предупреждение.Contains("DatabasePath", StringComparison.Ordinal));
    }

    /// <summary>
    /// Повторный запуск мастера не создаёт дубликатов: уже перенесённые записи
    /// пропускаются.
    /// </summary>
    [Fact]
    public async Task ПовторныйЗапуск_НеДублируетЗаписи()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        using var прежняя = new ВременнаяПапка();

        СоздатьПрежнююБазу(Path.Combine(прежняя.Путь, "picket302.db"));

        var импортёр = new LegacyUpgradeImporter(база.Session);

        импортёр.ImportFromLegacy(прежняя.Путь);

        var контрагентовПослеПервого = await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM counterparties;");
        var писемПослеПервого = await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM mails;");
        var группПослеПервого = await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM groups;");

        var отчёт = импортёр.ImportFromLegacy(прежняя.Путь);

        Assert.Equal(контрагентовПослеПервого, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM counterparties;"));
        Assert.Equal(писемПослеПервого, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM mails;"));
        Assert.Equal(группПослеПервого, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM groups;"));

        var счётчик = отчёт.Tables.Single(счёт => счёт.LegacyTable == "counterparties");
        Assert.Equal(0, счётчик.Transferred);
        Assert.Equal(2, счётчик.Skipped);
    }

    /// <summary>
    /// В папке без файла базы перенос отклоняется с перечнем ожидаемых файлов.
    /// </summary>
    [Fact]
    public async Task БезФайлаБазы_ОтказСПеречнемОжидаемыхФайлов()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        using var прежняя = new ВременнаяПапка();

        var импортёр = new LegacyUpgradeImporter(база.Session);

        var ошибка = await Assert.ThrowsAsync<LegacyMigrationException>(
            () => импортёр.ImportFromLegacyAsync(прежняя.Путь));

        Assert.Contains("picket302.db", ошибка.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Несовместимая версия схемы прежней версии: перенос отклоняется с
    /// указанием поддерживаемых версий.
    /// </summary>
    [Fact]
    public async Task НесовместимаяВерсия_ОтказССпискомПоддерживаемыхВерсий()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();
        using var прежняя = new ВременнаяПапка();

        var путьБазы = Path.Combine(прежняя.Путь, "picket302.db");
        СоздатьПрежнююБазу(путьБазы);

        using (var соединение = new SqliteConnection($"Data Source={путьБазы}"))
        {
            соединение.Open();

            using var команда = соединение.CreateCommand();
            команда.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version (version, description) VALUES ('999', 'будущая версия');";
            команда.ExecuteNonQuery();
        }

        var импортёр = new LegacyUpgradeImporter(база.Session);

        var ошибка = await Assert.ThrowsAsync<LegacyMigrationException>(
            () => импортёр.ImportFromLegacyAsync(прежняя.Путь));

        Assert.Contains("несовместима", ошибка.Message, StringComparison.Ordinal);
        Assert.Contains("Поддерживаются версии", ошибка.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Создаёт файл базы прежней версии с контрагентами, группами, полями,
    /// письмами, историей печати и настройками.
    /// </summary>
    /// <param name="путь">Путь к создаваемому файлу базы.</param>
    private static void СоздатьПрежнююБазу(string путь)
    {
        var строка = new SqliteConnectionStringBuilder
        {
            DataSource = путь,
        }.ToString();

        using var соединение = new SqliteConnection(строка);
        соединение.Open();

        using var команда = соединение.CreateCommand();
        команда.CommandText =
            """
            CREATE TABLE groups (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                note TEXT NULL,
                is_default INTEGER NOT NULL DEFAULT 0,
                is_closed INTEGER NOT NULL DEFAULT 0,
                sort_order INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE counterparties (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type INTEGER NOT NULL DEFAULT 0,
                name TEXT NOT NULL,
                short_name TEXT NULL,
                inn TEXT NULL,
                kpp TEXT NULL,
                ogrn TEXT NULL,
                okfs TEXT NULL,
                okpo TEXT NULL,
                okved TEXT NULL,
                bik TEXT NULL,
                ks TEXT NULL,
                rs TEXT NULL,
                rkc TEXT NULL,
                bank_name TEXT NULL,
                passport_series TEXT NULL,
                passport_number TEXT NULL,
                passport_issued_by TEXT NULL,
                passport_issued_date TEXT NULL,
                birth_date TEXT NULL,
                birth_place TEXT NULL,
                position TEXT NULL,
                director_fio TEXT NULL,
                email TEXT NULL,
                phone TEXT NULL,
                special_notes TEXT NULL,
                legal_zip TEXT NULL,
                legal_str1 TEXT NULL,
                legal_str2 TEXT NULL,
                legal_str3 TEXT NULL,
                legal_str4 TEXT NULL,
                legal_str5 TEXT NULL,
                legal_str6 TEXT NULL,
                legal_raw TEXT NULL,
                post_zip TEXT NULL,
                post_str1 TEXT NULL,
                post_str2 TEXT NULL,
                post_str3 TEXT NULL,
                post_str4 TEXT NULL,
                post_str5 TEXT NULL,
                post_str6 TEXT NULL,
                post_raw TEXT NULL,
                fact_zip TEXT NULL,
                fact_str1 TEXT NULL,
                fact_str2 TEXT NULL,
                fact_str3 TEXT NULL,
                fact_str4 TEXT NULL,
                fact_str5 TEXT NULL,
                fact_str6 TEXT NULL,
                fact_raw TEXT NULL,
                address_main_type INTEGER NOT NULL DEFAULT 1,
                group_id INTEGER NULL,
                is_vip INTEGER NOT NULL DEFAULT 0,
                is_sender INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NULL,
                updated_at TEXT NULL
            );

            CREATE TABLE counterparty_fields (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                counterparty_id INTEGER NOT NULL,
                field_name TEXT NOT NULL,
                field_value TEXT NULL,
                field_type INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NULL
            );

            CREATE TABLE mails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                counterparty_id INTEGER NOT NULL,
                sender_id INTEGER NOT NULL,
                envelope_type TEXT NULL,
                notice_type TEXT NULL,
                copy_count INTEGER NOT NULL DEFAULT 1,
                is_vip INTEGER NOT NULL DEFAULT 0,
                note TEXT NULL,
                declared_value NUMERIC NOT NULL DEFAULT 0,
                weight NUMERIC NOT NULL DEFAULT 0,
                fee NUMERIC NOT NULL DEFAULT 0,
                status INTEGER NOT NULL DEFAULT 0,
                need_notification INTEGER NOT NULL DEFAULT 0,
                need_return_address INTEGER NOT NULL DEFAULT 1,
                mail_category INTEGER NOT NULL DEFAULT 0,
                date_created TEXT NULL,
                date_printed TEXT NULL,
                date_dispatched TEXT NULL,
                date_confirmation TEXT NULL,
                date_canceled TEXT NULL,
                incoming_number TEXT NULL,
                outgoing_number TEXT NULL,
                russian_post_id TEXT NULL,
                russian_post_batch TEXT NULL,
                to_zip TEXT NULL,
                to_str1 TEXT NULL,
                to_str2 TEXT NULL,
                to_str3 TEXT NULL,
                to_str4 TEXT NULL,
                to_str5 TEXT NULL,
                to_str6 TEXT NULL
            );

            CREATE TABLE print_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                printed_at TEXT NULL,
                mail_id INTEGER NULL,
                recipient_address_snapshot TEXT NULL,
                sender_address_snapshot TEXT NULL,
                copy_count INTEGER NOT NULL DEFAULT 0,
                is_vip INTEGER NOT NULL DEFAULT 0,
                note TEXT NULL,
                template_description TEXT NULL
            );

            CREATE TABLE parameters (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                value TEXT NULL,
                updated_at TEXT NULL
            );

            CREATE TABLE schema_version (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                version TEXT NOT NULL,
                description TEXT NULL,
                applied_at TEXT NULL
            );

            INSERT INTO groups (id, name, note, is_default, is_closed, sort_order)
            VALUES (1, 'Основные', 'Первая группа', 0, 0, 0);

            INSERT INTO counterparties (
                id, type, name, inn, email, phone, group_id, is_vip, is_sender, is_active,
                post_zip, post_str1, post_str5, address_main_type, created_at)
            VALUES
                (1, 0, 'ООО «Ромашка»', '7707083893', 'romashka@example.com', '+7 495 000-00-00',
                    1, 0, 1, 1, '101000', 'ООО «Ромашка»', 'Москва', 1, '2024-01-01 09:00:00'),
                (2, 1, 'Иванов Иван Иванович', NULL, 'ivanov@example.com', '+7 495 111-11-11',
                    1, 1, 0, 1, '101000', 'Иванов Иван Иванович', 'Москва', 1, '2024-01-02 09:00:00');

            INSERT INTO counterparty_fields (id, counterparty_id, field_name, field_value, field_type, updated_at)
            VALUES (1, 2, 'Договор', 'Д-42', 0, '2024-01-02 09:00:00');

            INSERT INTO mails (
                id, counterparty_id, sender_id, envelope_type, copy_count, is_vip, note, status,
                need_notification, need_return_address, mail_category, date_created, to_zip, to_str1, to_str5)
            VALUES (1, 2, 1, 'C6', 2, 1, 'Срочно', 0, 0, 1, 0, '2024-05-06 10:00:00',
                '101000', 'Иванов Иван Иванович', 'Москва');

            INSERT INTO print_history (
                id, printed_at, mail_id, recipient_address_snapshot, sender_address_snapshot,
                copy_count, is_vip, note, template_description)
            VALUES (1, '2024-05-06 11:00:00', 1, '101000 Иванов Иван Иванович',
                '101000 ООО «Ромашка»', 2, 1, 'Срочно', 'Конверт C6');

            INSERT INTO parameters (id, name, value, updated_at) VALUES
                (1, 'LastOutgoingNumber', '42', '2024-01-01 09:00:00'),
                (2, 'DatabasePath', 'C:\prev\picket302.db', '2024-01-01 09:00:00'),
                (3, 'ServerHost', 'server.local', '2024-01-01 09:00:00'),
                (4, 'SmtpPassword', 'секрет', '2024-01-01 09:00:00');

            INSERT INTO schema_version (id, version, description, applied_at)
            VALUES (1, $version, 'Прежняя версия', '2024-01-01 09:00:00');
            """;

        команда.Parameters.AddWithValue("$version", ПоследняяВерсияСхемы());
        команда.ExecuteNonQuery();
    }

    /// <summary>
    /// Возвращает номер последней версии схемы, поддерживаемой программой.
    /// </summary>
    /// <returns>Номер версии схемы.</returns>
    private static string ПоследняяВерсияСхемы()
        => MigrationCatalog
            .Load(typeof(SqliteUnitOfWork).Assembly)
            .Max(миграция => миграция.Version) ?? "0";

    /// <summary>
    /// Создаёт временную папку, удаляемую по завершении теста.
    /// </summary>
    private sealed class ВременнаяПапка : IDisposable
    {
        /// <summary>Путь к временной папке.</summary>
        public string Путь { get; }

        /// <summary>Создаёт временную папку.</summary>
        public ВременнаяПапка()
        {
            Путь = Path.Combine(
                Path.GetTempPath(),
                "picket302_upgrade_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Путь);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Путь))
                {
                    Directory.Delete(Путь, recursive: true);
                }
            }
            catch (IOException)
            {
                // Временные файлы остаются: тест не должен падать из-за очистки.
            }
            catch (UnauthorizedAccessException)
            {
                // Временные файлы остаются: тест не должен падать из-за очистки.
            }
        }
    }
}
