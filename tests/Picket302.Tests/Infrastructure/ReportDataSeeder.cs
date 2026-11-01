using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Сеяние демонстрационных данных для тестов слоя запросов и печатных форм
/// (ТЗ §11.2: SQLite in-memory с сеяными данными).
/// </summary>
public static class ReportDataSeeder
{
    /// <summary>Идентификатор отправителя в сеяных данных.</summary>
    public const long SenderId = 1;

    /// <summary>Идентификатор другого отправителя (для проверки смешения).</summary>
    public const long OtherSenderId = 2;

    /// <summary>Сеет группы, контрагентов, отправления, опись, журнал печати и историю.</summary>
    /// <param name="connection">Открытое соединение с тестовой базой.</param>
    /// <returns>Список идентификаторов писем отправителя №1.</returns>
    public static async Task<IReadOnlyList<long>> SeedAsync(SqliteConnection connection)
    {
        // Системная группа по умолчанию уже создана миграцией V002 —
        // переименуем её и привяжем сеяных контрагентов к группе 1.
        await ВыполнитьAsync(connection, "DELETE FROM groups;").ConfigureAwait(false);
        await ВыполнитьAsync(connection,
            "INSERT INTO groups (id, name, is_default) VALUES (1, 'Клиенты', 1);").ConfigureAwait(false);

        // Отправитель №1 (с почтовым адресом), отправитель №2, получатели.
        await ВыполнитьAsync(connection, """
            INSERT INTO counterparties (id, type, name, inn, group_id, is_active, is_sender,
                passport_series, passport_number, passport_issued_by, bik, rs, ks, bank_name,
                post_zip, post_str1, post_str2, post_str3, post_str4, post_str5, post_str6)
            VALUES
            (1, 0, 'ООО Отправитель', '7701000001', 1, 1, 1,
             '1234', '567890', 'ОВД Ленинского района', '044525225', '40702810000000001234', '30101810400000000225', 'Банк Перв',
             '101000', 'г. Москва', 'ул. Первая', 'д. 1', '', '', ''),
            (2, 0, 'ИП Второй Отправитель', '7702000002', 1, 1, 1,
             '9876', '543210', 'ОВД Второго района', '044525999', '40702810000000009999', '30101810900000000999', 'Банк Втор',
             '630000', 'г. Новосибирск', 'ул. Вторая', 'д. 2', '', '', ''),
            (3, 1, 'Иванов Иван Иванович', '7703000003', 1, 1, 0,
             '1111', '111111', 'ОВД Третьего района', '', '', '', '',
             '', '', '', '', '', '', ''),
            (4, 1, 'Петров Петр Петрович', '7704000004', 1, 1, 0,
             '2222', '222222', 'ОВД Четвертого района', '', '', '', '',
             '', '', '', '', '', '', '');
            """).ConfigureAwait(false);

        await ВыполнитьAsync(connection,
            "INSERT INTO inventories (id, name, counterparty_id, destination) VALUES (1, 'Опись документов', 3, 'г. Москва, ул. Тестовая');").ConfigureAwait(false);

        await ВыполнитьAsync(connection, """
            INSERT INTO inventory_items (id, inventory_id, name, quantity, cost) VALUES
            (1, 1, 'Договор поставки', 2, 150.50),
            (2, 1, 'Акт сверки', 1, 49.50);
            """).ConfigureAwait(false);

        // Письма отправителя №1: id 1, 2; письмо отправителя №2: id 3.
        // Дата создания — сегодня, чтобы попадать в диапазоны DateFrom/DateTo.
        var сегодня = DateTime.UtcNow.Date;

        await ВставитьПисьмоAsync(connection, 1, SenderId, 3, "100000", "ул. Тестовая, д. 10", "исх-1", сегодня, inventoryId: 1).ConfigureAwait(false);
        await ВставитьПисьмоAsync(connection, 2, SenderId, 4, "190000", "ул. Большая, д. 5", "исх-2", сегодня, inventoryId: null).ConfigureAwait(false);
        await ВставитьПисьмоAsync(connection, 3, OtherSenderId, 3, "410000", "ул. Саратовская, д. 7", "исх-3", сегодня, inventoryId: null).ConfigureAwait(false);

        // Журнал печати и история.
        await ВыполнитьAsync(connection, """
            INSERT INTO print_jobs (id, template_id, printer_name, paper_size_name, paper_width_mm, paper_height_mm, font_info, copies, print_mode, status)
            VALUES (1, NULL, 'HP LaserJet Pro M402dn', 'C5', 229.0, 162.0, 'Tahoma 9.0pt', 2, 'batch_direct', 'SUCCESS');
            """).ConfigureAwait(false);

        await ВыполнитьAsync(connection, """
            INSERT INTO print_history (id, printed_at, mail_id, recipient_address_snapshot, sender_address_snapshot, copy_count, is_vip, template_description, print_job_id)
            VALUES (1, strftime('%Y-%m-%d %H:%M:%f', 'now'), 1, '100000, г. Москва, ул. Тестовая, д. 10; Иванов Иван Иванович', '101000, г. Москва, ул. Первая, д. 1; ООО Отправитель', 2, 0, 'Конверт C5 (2012)', 1);
            """).ConfigureAwait(false);

        // Кэш QR-кодов.
        await ВыполнитьAsync(connection,
            "INSERT INTO qr_cache (payload_hash, image_blob) VALUES ('6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b', x'010203');").ConfigureAwait(false);

        return new List<long> { 1, 2 };
    }

    /// <summary>Вставляет отправление.</summary>
    private static async Task ВставитьПисьмоAsync(
        SqliteConnection connection,
        long id,
        long senderId,
        long counterpartyId,
        string индекс,
        string улица,
        string исходящий,
        DateTime дата,
        long? inventoryId)
    {
        await ВыполнитьAsync(connection, $"""
            INSERT INTO mails (id, counterparty_id, sender_id, inventory_id, outgoing_number, date_created, date_dispatched,
                copy_count, is_vip, note, declared_value, weight, fee, status, mail_category, to_zip, to_str1, to_str2)
            VALUES ({id}, {counterpartyId}, {senderId}, {(inventoryId is null ? "NULL" : inventoryId.Value.ToString(CultureInfo.InvariantCulture))}, '{исходящий}', '{дата:yyyy-MM-dd 10:00:00}', '{дата:yyyy-MM-dd 12:00:00}',
                1, 0, 'примечание {id}', 500.00, 0.100, 65.00, 1, 1, '{индекс}', 'Получатель {id}', '{улица}');
            """).ConfigureAwait(false);
    }

    private static async Task ВыполнитьAsync(SqliteConnection connection, string sql)
    {
        await using var команда = connection.CreateCommand();
        команда.CommandText = sql;
        await команда.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
