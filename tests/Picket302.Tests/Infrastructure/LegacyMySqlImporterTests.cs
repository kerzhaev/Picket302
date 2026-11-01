using System.Globalization;
using System.Text;

using Dapper;
using Picket302.Infrastructure.Migration;

using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка импорта базы из исходной MySQL-схемы — 8 таблиц
/// (ТЗ §8.9, функция 117: «Импорт базы из исходной MySQL-схемы (8 таблиц)»).
/// </summary>
/// <remarks>
/// Перенос проверяется без сервера MySQL: источником служит набор данных,
/// прочитанный в память (TЗ §11.1: юнит-тест — изолированная проверка без БД;
/// §11.2: целевая база — SQLite в памяти).
/// </remarks>
public sealed class LegacyMySqlImporterTests
{
    /// <summary>
    /// Восемь таблиц исходной схемы переносятся в целевую базу: адресная книга,
    /// группы, отправления, история печати, кэш кодов, описи и параметры.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task ВосемьТаблицMySql_ПереносятсяВЦелевуюБазу()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyMySqlImporter(база.Session);
        var отчёт = await импортер.ImportAsync(СоздатьИсточник(), "Импорт MySQL от 2024-05-06 из print_konvert");

        // Адресная книга: отправитель и получатель.
        var контрагенты = (await база.Connection.QueryAsync<СтрокаКонтрагента>(
            "SELECT id, name, post_zip, post_str1, post_str5, is_vip, is_sender, group_id, source FROM counterparties ORDER BY id;"))
            .ToArray();

        Assert.Equal(2, контрагенты.Length);

        var отправитель = контрагенты.Single(запись => запись.IsSender == 1);
        Assert.Equal("ООО «Ромашка»", отправитель.Name);
        Assert.Equal("101000", отправитель.PostZip);
        Assert.Equal("Москва", отправитель.PostStr5);

        var получатель = контрагенты.Single(запись => запись.IsVip == 1);
        Assert.Equal("Иванов Иван Иванович", получатель.Name);
        Assert.Equal("101000", получатель.PostZip);
        Assert.Equal("Иванов Иван Иванович", получатель.PostStr1);
        Assert.NotNull(получатель.Source);
        Assert.Contains("Импорт MySQL", получатель.Source!, StringComparison.Ordinal);

        // Группа получателя переносится по наименованию.
        Assert.NotNull(получатель.GroupId);

        // Отправление: copyes, vip, prim, date_send и print_prim.
        var письма = (await база.Connection.QueryAsync<СтрокаПисьма>(
            "SELECT counterparty_id, sender_id, copy_count, is_vip, note, status, date_created, date_printed, to_zip, to_str1 FROM mails;"))
            .ToArray();

        var письмо = Assert.Single(письма);
        Assert.Equal(2, письмо.CopyCount);
        Assert.Equal(1, письмо.IsVip);
        Assert.Equal("Срочно", письмо.Note);
        Assert.Equal(1, письмо.Status);
        Assert.StartsWith("2024-05-06 10:00:00", письмо.DateCreated, StringComparison.Ordinal);
        Assert.StartsWith("2024-05-06 10:00:00", письмо.DatePrinted, StringComparison.Ordinal);
        Assert.Equal("101000", письмо.ToZip);
        Assert.Equal(получатель.Id, письмо.CounterpartyId);
        Assert.Equal(отправитель.Id, письмо.SenderId);

        // История печати: teplate_descr и снимок адреса отправителя.
        var история = Assert.Single(await база.Connection.QueryAsync<СтрокаИстории>(
            "SELECT printed_at, recipient_address_snapshot, sender_address_snapshot, copy_count, is_vip, note, template_description FROM print_history;"));

        Assert.Equal("Конверт C6", история.TemplateDescription);
        Assert.Equal("101000 ООО «Ромашка»", история.SenderAddressSnapshot);
        Assert.Contains("Иванов Иван Иванович", история.RecipientAddressSnapshot, StringComparison.Ordinal);
        Assert.Equal(2, история.CopyCount);
        Assert.Equal(1, история.IsVip);
        Assert.Equal("Срочно", история.Note);

        // Группы: fdefault и closed.
        var группа = Assert.Single(await база.Connection.QueryAsync<СтрокаГруппы>(
            "SELECT name, note, is_default, is_closed FROM groups WHERE name = 'Основные';"));

        Assert.Equal("Первая группа", группа.Note);
        Assert.Equal(1, группа.IsDefault);
        Assert.Equal(1, группа.IsClosed);

        // Кэш QR-кодов, описи вложений и параметры.
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM qr_cache;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM inventories;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM inventory_items;"));
        Assert.Equal("42", await база.Connection.QuerySingleAsync<string>("SELECT value FROM parameters WHERE name = 'last_outgoing';"));

        // Отчёт соответствия содержит ключевые соответствия полей.
        Assert.Contains(отчёт.FieldMappings, соответствие =>
            соответствие.LegacyTable == "addrto" && соответствие.LegacyColumn == "copyes"
            && соответствие.TargetField == "mails.copy_count");
        Assert.Contains(отчёт.FieldMappings, соответствие =>
            соответствие.LegacyTable == "addrto_history" && соответствие.LegacyColumn == "teplate_descr"
            && соответствие.TargetField == "template_description");
        Assert.Contains(отчёт.FieldMappings, соответствие =>
            соответствие.LegacyTable == "groups" && соответствие.LegacyColumn == "fdefault"
            && соответствие.TargetField == "is_default");
    }

    /// <summary>
    /// Отсутствующая таблица пропускается с предупреждением: импорт не прерывается.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task ОтсутствующаяТаблица_ПропускаетсяСПредупреждением()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var таблицы = СоздатьТаблицы();
        таблицы.Remove(LegacyTableNames.MySqlQrCache);

        var импортер = new LegacyMySqlImporter(база.Session);
        var отчёт = await импортер.ImportAsync(
            new InMemoryLegacyTableSource(таблицы),
            "Импорт MySQL от 2024-05-06 из print_konvert");

        Assert.Contains(отчёт.Warnings, предупреждение =>
            предупреждение.Contains(LegacyTableNames.MySqlQrCache, StringComparison.Ordinal));

        var счётчик = отчёт.Tables.Single(таблица => таблица.LegacyTable == LegacyTableNames.MySqlQrCache);
        Assert.Equal(0, счётчик.Read);
        Assert.Equal(0, счётчик.Transferred);

        // Остальные таблицы перенесены: импорт не прерван.
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM mails;"));
        Assert.Equal(2, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM counterparties;"));
    }

    /// <summary>
    /// Недопустимый почтовый индекс помечает запись как требующую проверки,
    /// но не приводит к её отбрасыванию.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task НекорректныйИндекс_ПомечаетЗаписьКакТребующуюПроверки()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var таблицы = СоздатьТаблицы();
        таблицы[LegacyTableNames.MySqlRecipients] =
        [
            InMemoryLegacyTableSource.Строка(
                ("id", "10"),
                ("ind_to", "12345"),
                ("group_id", "1"),
                ("str_to1", "Иванов Иван Иванович"),
                ("str_to2", "ул. Пушкина, д. 5"),
                ("copyes", 1),
                ("vip", 0),
                ("prim", null),
                ("date_send", "2024-05-06 10:00:00"),
                ("print_prim", 0)),
        ];

        var импортер = new LegacyMySqlImporter(база.Session);
        var отчёт = await импортер.ImportAsync(
            new InMemoryLegacyTableSource(таблицы),
            "Импорт MySQL от 2024-05-06 из print_konvert");

        var счётчик = отчёт.Tables.Single(таблица => таблица.LegacyTable == LegacyTableNames.MySqlRecipients);
        Assert.Equal(1, счётчик.NeedsVerification);
        Assert.Equal(1, счётчик.Transferred);
        Assert.Equal(0, счётчик.Errors);

        // Запись не отброшена: получатель перенесён.
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM counterparties WHERE name = 'Иванов Иван Иванович';"));

        Assert.Contains(отчёт.Warnings, предупреждение =>
            предупреждение.Contains("шести цифр", StringComparison.Ordinal));
    }

    /// <summary>
    /// Текст в кодировке cp1251 исходной базы преобразуется в UTF-8 целевой базы.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task Cp1251_ПреобразуетсяВUtf8ВЦелевойБазе()
    {
        var ожидаемыйТекст = "ООО «Ромашка»";
        var байтыCp1251 = MySqlTableSource.EncodeCp1251(ожидаемыйТекст);

        // Байты cp1251 отличаются от UTF-8: декодирование проверяется по кодировке.
        Assert.NotEqual(Encoding.UTF8.GetBytes(ожидаемыйТекст), байтыCp1251);
        Assert.Equal(ожидаемыйТекст, MySqlTableSource.ConvertCp1251ToText(байтыCp1251));

        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyMySqlImporter(база.Session);
        await импортер.ImportAsync(
            СоздатьИсточник(),
            "Импорт MySQL от 2024-05-06 из print_konvert");

        // В целевой базе текст хранится в UTF-8.
        var байтыВБазе = (await база.Connection.QueryAsync<byte[]>(
            "SELECT CAST(name AS BLOB) FROM counterparties WHERE is_sender = 1;"))
            .Single();

        Assert.Equal(Encoding.UTF8.GetBytes(ожидаемыйТекст), байтыВБазе);

        var имя = await база.Connection.QuerySingleAsync<string>(
            "SELECT name FROM counterparties WHERE is_sender = 1;");

        Assert.Equal(ожидаемыйТекст, имя);
    }

    /// <summary>
    /// Счётчики отчёта совпадают с числом перенесённых записей.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task СчётчикиОтчёта_СовпадаютСЧисломПеренесённыхЗаписей()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyMySqlImporter(база.Session);
        var отчёт = await импортер.ImportAsync(
            СоздатьИсточник(),
            "Импорт MySQL от 2024-05-06 из print_konvert");

        Assert.Equal(8, отчёт.Tables.Count);

        foreach (var счётчик in отчёт.Tables)
        {
            Assert.True(
                счётчик.Read == счётчик.Transferred + счётчик.Skipped + счётчик.Errors,
                $"Таблица «{счётчик.LegacyTable}»: прочитано {счётчик.Read}, перенесено {счётчик.Transferred}, "
                + $"пропущено {счётчик.Skipped}, ошибок {счётчик.Errors}.");
            Assert.True(
                счётчик.Transferred == 1,
                $"Таблица «{счётчик.LegacyTable}»: прочитано {счётчик.Read}, перенесено {счётчик.Transferred}, "
                + $"пропущено {счётчик.Skipped}, ошибок {счётчик.Errors}.");
        }

        Assert.Equal(8, отчёт.Summary.TotalTransferred);
        Assert.Equal(0, отчёт.Summary.TotalErrors);
        Assert.Equal(0, отчёт.Summary.TotalSkipped);

        Assert.Equal(2, отчёт.Summary.Targets.Single(цель => цель.TargetTable == "counterparties").Transferred);
        Assert.Equal(1, отчёт.Summary.Targets.Single(цель => цель.TargetTable == "mails").Transferred);
        Assert.Equal(1, отчёт.Summary.Targets.Single(цель => цель.TargetTable == "print_history").Transferred);
        Assert.Equal(1, отчёт.Summary.Targets.Single(цель => цель.TargetTable == "groups").Transferred);
        Assert.Equal(1, отчёт.Summary.Targets.Single(цель => цель.TargetTable == "qr_cache").Transferred);
        Assert.Equal(1, отчёт.Summary.Targets.Single(цель => цель.TargetTable == "inventories").Transferred);
        Assert.Equal(1, отчёт.Summary.Targets.Single(цель => цель.TargetTable == "inventory_items").Transferred);

        // Счётчики отчёта совпадают с фактическим содержимым целевой базы.
        Assert.Equal(2, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM counterparties;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM mails;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM print_history;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM groups WHERE name = 'Основные';"));

        // Отчёт сериализуется в текст и в JSON — его показывает мастер миграции.
        var текст = отчёт.ToText();
        Assert.Contains("Отчёт миграции: MySQL", текст, StringComparison.Ordinal);
        Assert.Contains("addrto → counterparties, mails", текст, StringComparison.Ordinal);

        var json = отчёт.ToJson();
        Assert.Contains("\"source\": \"MySQL\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tables\"", json, StringComparison.Ordinal);
        Assert.Contains("\"fieldMappings\"", json, StringComparison.Ordinal);
        Assert.Contains("\"summary\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Повторный импорт не увеличивает число записей адресной книги: записи
    /// сливаются по ключу функции 74 (ИНН, иначе наименование и индекс).
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task ПовторныйИмпорт_НеСоздаётДубликатовАдреснойКниги()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyMySqlImporter(база.Session);

        await импортер.ImportAsync(СоздатьИсточник(), "Импорт MySQL от 2024-05-06 из print_konvert");
        await импортер.ImportAsync(СоздатьИсточник(), "Импорт MySQL от 2024-05-06 из print_konvert");

        Assert.Equal(2, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM counterparties;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM groups WHERE name = 'Основные';"));
    }

    /// <summary>
    /// Создаёт набор исходных таблиц MySQL-схемы (8 таблиц).
    /// </summary>
    /// <returns>Набор таблиц.</returns>
    private static Dictionary<string, IReadOnlyList<LegacyRow>> СоздатьТаблицы()
        => new Dictionary<string, IReadOnlyList<LegacyRow>>(StringComparer.OrdinalIgnoreCase)
        {
            [LegacyTableNames.MySqlSenders] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("id", "1"),
                    ("ind_from", "101000"),
                    ("str_from1", "ООО «Ромашка»"),
                    ("str_from2", "ул. Ленина, д. 1"),
                    ("str_from3", null),
                    ("str_from4", null),
                    ("str_from5", "Москва"),
                    ("str_from6", "Московская обл."),
                    ("logo", new byte[] { 1, 2, 3, 4, 5 }),
                    ("logo_info", "Логотип 100x50"),
                    ("file_Ext", ".png")),
            ],
            [LegacyTableNames.MySqlRecipients] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("id", "10"),
                    ("ind_to", "101000"),
                    ("group_id", "1"),
                    ("str_to1", "Иванов Иван Иванович"),
                    ("str_to2", "ул. Пушкина, д. 5"),
                    ("str_to3", null),
                    ("str_to4", "кв. 7"),
                    ("str_to5", "Москва"),
                    ("str_to6", null),
                    ("copyes", 2),
                    ("vip", 1),
                    ("prim", "Срочно"),
                    ("date_send", "2024-05-06 10:00:00"),
                    ("print_prim", 1)),
            ],
            [LegacyTableNames.MySqlPrintHistory] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("id", "100"),
                    ("date_print", "2024-05-06 10:05:00"),
                    ("ind_to", "101000"),
                    ("str_to1", "Иванов Иван Иванович"),
                    ("str_to2", "ул. Пушкина, д. 5"),
                    ("str_to3", null),
                    ("str_to4", "кв. 7"),
                    ("str_to5", "Москва"),
                    ("str_to6", null),
                    ("vip", 1),
                    ("copyes", 2),
                    ("prim", "Срочно"),
                    ("teplate_descr", "Конверт C6"),
                    ("print_prim", 1),
                    ("id_env", "1"),
                    ("addrfrom", "101000 ООО «Ромашка»")),
            ],
            [LegacyTableNames.MySqlQrCache] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("id", "1"),
                    ("img_qrcode", new byte[] { 9, 8, 7, 6 })),
            ],
            [LegacyTableNames.MySqlGroups] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("id", "1"),
                    ("name", "Основные"),
                    ("prim", "Первая группа"),
                    ("fdefault", 1),
                    ("closed", 1)),
            ],
            [LegacyTableNames.MySqlInventory] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("id", "5"),
                    ("id_main_to", "10"),
                    ("name", "Опись № 5"),
                    ("komu", "Иванов Иван Иванович"),
                    ("kuda", "Москва"),
                    ("prim", "отправлено 06.05.2024"),
                    ("date_create", "2024-05-06 09:00:00")),
            ],
            [LegacyTableNames.MySqlInventoryDetail] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("id", "7"),
                    ("id_main", "5"),
                    ("name", "Письмо"),
                    ("kol", 2),
                    ("cost", 10.50),
                    ("prim", "вложение"),
                    ("date_create", "2024-05-06 09:00:00")),
            ],
            [LegacyTableNames.MySqlParams] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("id", "1"),
                    ("name", "last_outgoing"),
                    ("value", "42")),
            ],
        };

    /// <summary>Создаёт источник исходных данных из набора таблиц.</summary>
    private static InMemoryLegacyTableSource СоздатьИсточник()
        => new(СоздатьТаблицы());

    /// <summary>Строка контрагента целевой базы для проверок.</summary>
    private sealed class СтрокаКонтрагента
    {
        public long Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? PostZip { get; set; }

        public string? PostStr1 { get; set; }

        public string? PostStr5 { get; set; }

        public int IsVip { get; set; }

        public int IsSender { get; set; }

        public long? GroupId { get; set; }

        public string? Source { get; set; }
    }

    /// <summary>Строка отправления целевой базы для проверок.</summary>
    private sealed class СтрокаПисьма
    {
        public long CounterpartyId { get; set; }

        public long SenderId { get; set; }

        public int CopyCount { get; set; }

        public int IsVip { get; set; }

        public string? Note { get; set; }

        public int Status { get; set; }

        public string? DateCreated { get; set; }

        public string? DatePrinted { get; set; }

        public string? ToZip { get; set; }

        public string? ToStr1 { get; set; }
    }

    /// <summary>Строка журнала печати целевой базы для проверок.</summary>
    private sealed class СтрокаИстории
    {
        public string? PrintedAt { get; set; }

        public string? RecipientAddressSnapshot { get; set; }

        public string? SenderAddressSnapshot { get; set; }

        public int CopyCount { get; set; }

        public int IsVip { get; set; }

        public string? Note { get; set; }

        public string? TemplateDescription { get; set; }
    }

    /// <summary>Строка группы целевой базы для проверок.</summary>
    private sealed class СтрокаГруппы
    {
        public string Name { get; set; } = string.Empty;

        public string? Note { get; set; }

        public int IsDefault { get; set; }

        public int IsClosed { get; set; }
    }
}

/// <summary>
/// Источник данных исходной схемы, работающий на наборе в памяти: перенос
/// проверяется без сервера MySQL и без файла .mdb (ТЗ §11.1, §11.2).
/// </summary>
public sealed class InMemoryLegacyTableSource : ILegacyTableSource
{
    private readonly Dictionary<string, IReadOnlyList<LegacyRow>> _таблицы;

    /// <summary>Создаёт источник данных в памяти.</summary>
    /// <param name="таблицы">Таблицы исходной схемы: имя таблицы — строки.</param>
    public InMemoryLegacyTableSource(IDictionary<string, IReadOnlyList<LegacyRow>> таблицы)
    {
        ArgumentNullException.ThrowIfNull(таблицы);

        _таблицы = new Dictionary<string, IReadOnlyList<LegacyRow>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (имя, строки) in таблицы)
        {
            _таблицы[имя] = строки;
        }
    }

    /// <summary>
    /// Создаёт строку исходной таблицы из пар «столбец — значение».
    /// </summary>
    /// <param name="значения">Пары «столбец — значение».</param>
    /// <returns>Строка исходной таблицы.</returns>
    public static LegacyRow Строка(params (string Столбец, object? Значение)[] значения)
    {
        ArgumentNullException.ThrowIfNull(значения);

        var словарь = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (столбец, значение) in значения)
        {
            словарь[столбец] = значение switch
            {
                null => null,
                byte[] байты => Convert.ToBase64String(байты),
                _ => Convert.ToString(значение, CultureInfo.InvariantCulture),
            };
        }

        return new LegacyRow(словарь);
    }

    /// <inheritdoc />
    public Task<bool> TryOpenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<IReadOnlyList<LegacyTable>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        var таблицы = _таблицы
            .Select(пара => new LegacyTable(
                пара.Key,
                пара.Value.Count > 0
                    ? пара.Value[0].Values.Keys.ToArray()
                    : Array.Empty<string>(),
                пара.Value.Count))
            .ToArray();

        return Task.FromResult<IReadOnlyList<LegacyTable>>(таблицы);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LegacyRow>> GetRowsAsync(
        string table,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_таблицы.TryGetValue(table, out var строки) ? строки : Array.Empty<LegacyRow>());
}
