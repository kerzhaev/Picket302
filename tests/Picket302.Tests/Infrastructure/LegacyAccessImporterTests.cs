using Dapper;
using Picket302.Domain.Rules;
using Picket302.Infrastructure.Migration;

using Xunit;

namespace Picket302.Tests.Infrastructure;

/// <summary>
/// Проверка импорта базы из исходной Access-схемы — 14 таблиц
/// (ТЗ §8.9, функция 118: «Импорт базы из исходной Access-схемы (14 таблиц)»).
/// </summary>
/// <remarks>
/// Сопоставление проверяется на наборе данных в памяти, а работа с файлом .mdb —
/// на отдельной проверке занятости файла (ТЗ §11.1, §11.2).
/// </remarks>
public sealed class LegacyAccessImporterTests
{
    /// <summary>
    /// Контрагент исходной схемы (53 столбца) переносится в адресную книгу без
    /// потерь: банковские и паспортные реквизиты сохраняются.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task КонтрагентAccess_ПереноситсяБезПотерь()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyAccessImporter(база.Session);
        var отчёт = await импортер.ImportAsync(СоздатьИсточник(), "Импорт Access от 2024-05-06 из database.mdb");

        var контрагент = Assert.Single(await база.Connection.QueryAsync<СтрокаКонтрагента>(
            "SELECT id, name, short_name, inn, kpp, ogrn, okfs, okpo, okved, bik, ks, rs, rkc, bank_name, "
            + "passport_series, passport_number, passport_issued_by, birth_date, birth_place, position, director_fio, "
            + "email, phone, special_notes, post_zip, post_str1, post_str2, post_str3, is_sender FROM counterparties "
            + "WHERE name = 'ООО «Ромашка»';"));

        Assert.Equal("Ромашка", контрагент.ShortName);
        Assert.Equal("7701234567", контрагент.Inn);
        Assert.Equal("770101001", контрагент.Kpp);
        Assert.Equal("1027700132195", контрагент.Ogrn);
        Assert.Equal("65", контрагент.Okfs);
        Assert.Equal("12345678", контрагент.Okpo);
        Assert.Equal("62.01", контрагент.Okved);

        // Банковские реквизиты.
        Assert.Equal("044525225", контрагент.Bik);
        Assert.Equal("30101810400000000225", контрагент.Ks);
        Assert.Equal("40702810100000012345", контрагент.Rs);
        Assert.Equal("ПАО Сбербанк", контрагент.BankName);

        // Паспортные и прочие реквизиты.
        Assert.Equal("45 12", контрагент.PassportSeries);
        Assert.Equal("123456", контрагент.PassportNumber);
        Assert.Equal("ОВД Тверского р-на", контрагент.PassportIssuedBy);
        Assert.Equal("Москва", контрагент.BirthPlace);
        Assert.Equal("Генеральный директор", контрагент.Position);
        Assert.Equal("Петров Иван Сергеевич", контрагент.DirectorFio);
        Assert.Equal("info@example.com", контрагент.Email);
        Assert.Equal("+7 495 000-00-00", контрагент.Phone);
        Assert.Equal("Постоянный клиент", контрагент.SpecialNotes);

        // Адресные срезы.
        Assert.Equal("101000", контрагент.PostZip);
        Assert.Equal("ул. Пушкина, д. 5", контрагент.PostStr1);
        Assert.Equal("Москва", контрагент.PostStr2);
        Assert.Equal("Московская обл.", контрагент.PostStr3);

        Assert.Equal(14, отчёт.Tables.Count);
    }

    /// <summary>
    /// Неканонические поля контрагента переносятся в пользовательские поля и
    /// доступны в макетах по тегу вида <c>[Custom_*]</c>.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task ПользовательскиеПоляAccess_ДоступныПоТегамCustom()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyAccessImporter(база.Session);
        await импортер.ImportAsync(СоздатьИсточник(), "Импорт Access от 2024-05-06 из database.mdb");

        var поля = (await база.Connection.QueryAsync<СтрокаПоля>(
            "SELECT field_name, field_value FROM counterparty_fields ORDER BY field_name;"))
            .ToArray();

        Assert.NotEmpty(поля);

        var блокер = поля.Single(поле => поле.FieldName == "Blocker");
        Assert.Equal("Нет", блокер.FieldValue);

        // Поле пользовательской системы предыдущей версии переносится отдельно.
        var кодКлиента = поля.Single(поле => поле.FieldName == "КодКлиента");
        Assert.Equal("КЛ-001", кодКлиента.FieldValue);

        foreach (var поле in поля)
        {
            var тег = CustomFieldTag.Format(поле.FieldName);

            Assert.True(CustomFieldTag.IsTag(тег), $"Поле «{поле.FieldName}» должно быть доступно по тегу {тег}.");
            Assert.StartsWith($"[{CustomFieldTag.Prefix}", тег, StringComparison.Ordinal);
        }

        Assert.Equal("[Custom_Blocker]", CustomFieldTag.Format("Blocker"));
    }

    /// <summary>
    /// Письмо исходной схемы (39 столбцов) переносится в отправления.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task ПисьмоAccess_ПереноситсяВОтправления()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyAccessImporter(база.Session);
        await импортер.ImportAsync(СоздатьИсточник(), "Импорт Access от 2024-05-06 из database.mdb");

        var письмо = Assert.Single(await база.Connection.QueryAsync<СтрокаПисьма>(
            "SELECT counterparty_id, sender_id, declared_value, weight, fee, note, need_notification, mail_category, "
            + "status, outgoing_number, russian_post_id, envelope_type, to_zip, to_str1 FROM mails WHERE outgoing_number = 'Исх-15';"));

        Assert.Equal(1000m, письмо.DeclaredValue);
        Assert.Equal(0.5m, письмо.Weight);
        Assert.Equal(50m, письмо.Fee);
        Assert.Equal("Срочно", письмо.Note);
        Assert.Equal(1, письмо.NeedNotification);
        Assert.Equal(1, письмо.MailCategory);
        Assert.Equal(2, письмо.Status);
        Assert.Equal("Исх-15", письмо.OutgoingNumber);
        Assert.Equal("RP123456", письмо.RussianPostId);
        Assert.Equal("C6", письмо.EnvelopeType);
        Assert.Equal("101000", письмо.ToZip);
        Assert.Equal("ул. Пушкина, д. 5", письмо.ToStr1);
        Assert.NotEqual(письмо.CounterpartyId, письмо.SenderId);

        // Входящий документ исходной схемы переносится отдельным отправлением.
        Assert.Equal(2, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM mails;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM mails WHERE incoming_number = 'Вх-9';"));
    }

    /// <summary>
    /// Бланк исходной схемы (6 столбцов) переносится в макеты печати.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task БланкAccess_ПереноситсяВМакетыПечати()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyAccessImporter(база.Session);
        await импортер.ImportAsync(СоздатьИсточник(), "Импорт Access от 2024-05-06 из database.mdb");

        var макет = Assert.Single(await база.Connection.QueryAsync<СтрокаМакета>(
            "SELECT name, kind, format, file_path FROM print_templates;"));

        Assert.Equal("Конверт C6", макет.Name);
        Assert.Equal("envelope", макет.Kind);
        Assert.Equal("C6", макет.Format);
        Assert.Equal("blanks\\c6.lbk", макет.FilePath);
    }

    /// <summary>
    /// Переносятся только переносимые ключи настроек: учётные данные и параметры
    /// внешних сервисов остаются в исходной базе.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task НастройкиAccess_ПереносятТолькоПереносимыеКлючи()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var импортер = new LegacyAccessImporter(база.Session);
        await импортер.ImportAsync(СоздатьИсточник(), "Импорт Access от 2024-05-06 из database.mdb");

        Assert.Equal("15", await база.Connection.QuerySingleAsync<string>(
            "SELECT value FROM parameters WHERE name = 'LastIncomingNumber';"));
        Assert.Equal("42", await база.Connection.QuerySingleAsync<string>(
            "SELECT value FROM parameters WHERE name = 'LastOutgoingNumber';"));
        Assert.Equal("1", await база.Connection.QuerySingleAsync<string>(
            "SELECT value FROM parameters WHERE name = 'UseShortname';"));
        Assert.Equal("C6", await база.Connection.QuerySingleAsync<string>(
            "SELECT value FROM parameters WHERE name = 'DefaultEnvelope';"));

        // Учётные данные и параметры сервисов не переносятся.
        Assert.Equal(0, await база.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM parameters WHERE name IN ('ProxyLogin', 'ProxyPassword', 'SmtpPassword', "
            + "'RussianPostPassword', 'RussianPostToken', 'ServiceLogin', 'Filters');"));

        // Текущий отправитель переносится отдельным параметром.
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM parameters WHERE name = 'CurrentSenderId';"));
    }

    /// <summary>
    /// Отсутствующая таблица пропускается с предупреждением: импорт не прерывается.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task ОтсутствующаяТаблицаAccess_ПропускаетсяСПредупреждением()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var таблицы = СоздатьТаблицы();
        таблицы.Remove(LegacyTableNames.AccessCustomFields);

        var импортер = new LegacyAccessImporter(база.Session);
        var отчёт = await импортер.ImportAsync(
            new InMemoryLegacyTableSource(таблицы),
            "Импорт Access от 2024-05-06 из database.mdb");

        Assert.Contains(отчёт.Warnings, предупреждение =>
            предупреждение.Contains(LegacyTableNames.AccessCustomFields, StringComparison.Ordinal));

        // Остальные данные перенесены: импорт не прерван.
        Assert.Equal(2, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM counterparties;"));
        Assert.Equal(2, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM mails;"));
        Assert.Equal(1, await база.Connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM print_templates;"));
    }

    /// <summary>
    /// Файл базы, занятый другим процессом, приводит к сообщению с указанием
    /// закрыть приложение-владелец.
    /// </summary>
    /// <returns>Задача теста.</returns>
    [Fact]
    public async Task ЗанятыйФайл_ДаётСообщениеОЗанятости()
    {
        await using var база = await SqliteTestDatabase.CreateAsync();

        var путь = Path.Combine(Path.GetTempPath(), $"picket302_миграция_{Guid.NewGuid():N}.mdb");
        await File.WriteAllBytesAsync(путь, new byte[1024]);

        try
        {
            var импортер = new LegacyAccessImporter(база.Session);

            using (var занятый = new FileStream(путь, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var ошибка = await Assert.ThrowsAsync<LegacyMigrationException>(
                    () => Task.Run(() => импортер.ImportFromMdb(путь)));

                Assert.Equal(
                    "Файл базы занят другим процессом. Закройте приложение-владелец и повторите миграцию.",
                    ошибка.Message);
            }
        }
        finally
        {
            File.Delete(путь);
        }
    }

    /// <summary>
    /// Создаёт набор исходных таблиц Access-схемы (14 таблиц).
    /// </summary>
    /// <returns>Набор таблиц.</returns>
    private static Dictionary<string, IReadOnlyList<LegacyRow>> СоздатьТаблицы()
        => new Dictionary<string, IReadOnlyList<LegacyRow>>(StringComparer.OrdinalIgnoreCase)
        {
            [LegacyTableNames.AccessAddresses] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("AddressId", "addr-1"),
                    ("AddressLine", "ул. Пушкина, д. 5"),
                    ("City", "Москва"),
                    ("Province", "Московская обл."),
                    ("ZipCode", "101000")),
            ],
            [LegacyTableNames.AccessCounterparties] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("EconomicMemberId", "em-1"),
                    ("AddressFact", "addr-1"),
                    ("AddressLegal", "addr-1"),
                    ("AddressNotify", "1"),
                    ("AddressPost", "addr-1"),
                    ("AddressText", "101000 Москва ул. Пушкина д. 5"),
                    ("BIK", "044525225"),
                    ("BirthPlace", "Москва"),
                    ("BirthDate", "1980-05-05"),
                    ("Blocker", "Нет"),
                    ("ChiefFirstName", "Иван"),
                    ("ChiefLastName", "Петров"),
                    ("ChiefMiddleName", "Сергеевич"),
                    ("Code", "К-01"),
                    ("DateLastEdit", "2024-04-01 12:00:00"),
                    ("EconomicMemberEmail", "info@example.com"),
                    ("EconomicMemberNote", "Постоянный клиент"),
                    ("em_type", "0"),
                    ("FirstName", "Иван"),
                    ("FullName", "Петров Иван Сергеевич"),
                    ("INN", "7701234567"),
                    ("KPP", "770101001"),
                    ("KS", "30101810400000000225"),
                    ("LastName", "Петров"),
                    ("LegalName", "ООО «Ромашка»"),
                    ("MainAddressType", "1"),
                    ("MiddleName", "Сергеевич"),
                    ("NameBank", "ПАО Сбербанк"),
                    ("NameTo", "ООО «Ромашка»"),
                    ("Phone", "+7 495 000-00-00"),
                    ("Sender", "0"),
                    ("ShortName", "Ромашка"),
                    ("TemplateIncoming", "Входящий"),
                    ("TemplateOriginal", "Исходящий"),
                    ("LastNumberIncoming", "12"),
                    ("LastNumberOriginal", "13"),
                    ("LastGenerationTimeIncoming", "2024-04-01 09:00:00"),
                    ("LastGenerationTimeOriginal", "2024-04-02 09:00:00"),
                    ("OGRN", "1027700132195"),
                    ("OKFS", "65"),
                    ("OKOPF", "65"),
                    ("OKPO", "12345678"),
                    ("OKVED", "62.01"),
                    ("Organization", "ООО «Ромашка»"),
                    ("PassportSeries", "45 12"),
                    ("PassportNumber", "123456"),
                    ("PassportGiven", "ОВД Тверского р-на"),
                    ("PassportDate", "2010-01-01 00:00:00"),
                    ("NPPosition", "Генеральный директор"),
                    ("RS", "40702810100000012345"),
                    ("RKC", "0000"),
                    ("Under", "Иванов"),
                    ("Performer", "Секретарь")),
                InMemoryLegacyTableSource.Строка(
                    ("EconomicMemberId", "em-2"),
                    ("LegalName", "ООО «Отправитель»"),
                    ("INN", "7709876543"),
                    ("em_type", "0"),
                    ("Sender", "1"),
                    ("MainAddressType", "1"),
                    ("AddressPost", "addr-1")),
            ],
            [LegacyTableNames.AccessCustomFields] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("EconomicMemberId", "em-1"),
                    ("Name", "КодКлиента"),
                    ("Meaning", "КЛ-001")),
            ],
            [LegacyTableNames.AccessDbScheme] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("LastChangeDate", "2024-04-01"),
                    ("LastChangedRevision", "7"),
                    ("Version", 1)),
            ],
            [LegacyTableNames.AccessDistributions] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("DistributionId", "dist-1"),
                    ("DistributionName", "Рассылка по абонентам"),
                    ("dFrmEconomicMember", "em-2"),
                    ("SpecialNote", "Ежемесячная"),
                    ("isMultiple", "1"),
                    ("DateLastEdit", "2024-04-01 12:00:00"),
                    ("DatePublication", "2024-04-02 12:00:00"),
                    ("Subject", "Абонентская рассылка")),
            ],
            [LegacyTableNames.AccessDocuments] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("DocumentId", "doc-1"),
                    ("DocumentNumber", "Д-12"),
                    ("DocumentType", "Договор"),
                    ("DocumentDate", "2024-04-01"),
                    ("DocumentPages", 3),
                    ("DocumentCopies", 1),
                    ("IncomingId", null),
                    ("OutcomingId", "let-1")),
            ],
            [LegacyTableNames.AccessDocumentMovements] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("DocumentMovementId", "dm-1"),
                    ("DocumentId", "doc-1"),
                    ("MovementId", "mov-1")),
            ],
            [LegacyTableNames.AccessDocumentTypes] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("DocumentTypeId", "dt-1"),
                    ("Name", "Договор")),
            ],
            [LegacyTableNames.AccessIncoming] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("IncomingId", "inc-1"),
                    ("IncomingNumber", "Вх-9"),
                    ("IncomingSubject", "Обращение клиента"),
                    ("IncomingDate", "2024-05-02 09:00:00"),
                    ("NameTo", "ООО «Ромашка»"),
                    ("ToEconomicMember", "em-1"),
                    ("NameFrom", "ООО «Отправитель»"),
                    ("FrmEconomicMember", "em-2"),
                    ("OriginalNumber", "Исх-14")),
            ],
            [LegacyTableNames.AccessLetters] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("LetterId", "let-1"),
                    ("AddressFrom", "addr-1"),
                    ("AddressNotify", "addr-1"),
                    ("AddressTo", "addr-1"),
                    ("AmountOfCOD", 1000),
                    ("Content", "Текст письма"),
                    ("DateCanceled", null),
                    ("DateConfirmation", null),
                    ("DateCreated", "2024-05-01 08:00:00"),
                    ("DateDispatched", "2024-05-02 08:00:00"),
                    ("DateLastEdit", "2024-05-02 08:00:00"),
                    ("DatePrinted", "2024-05-02 07:00:00"),
                    ("DatePublication", null),
                    ("DistributionId", null),
                    ("FeeForShipping", 50),
                    ("FrmEconomicMember", "em-2"),
                    ("InventoryXML", null),
                    ("isEmail", "0"),
                    ("isHtml", "0"),
                    ("LetterEmail", null),
                    ("LetterNumber", "Исх-15"),
                    ("NameFrm", "ООО «Отправитель»"),
                    ("NameTo", "ООО «Ромашка»"),
                    ("NeedNotification", "1"),
                    ("PostalNumber", "RP123456"),
                    ("PostRegisterId", null),
                    ("PostRegisterNumber", 7),
                    ("Price", 1000),
                    ("ReplyToEmail", null),
                    ("RNote", null),
                    ("SenderEmail", null),
                    ("SpecialNote", "Срочно"),
                    ("Subject", "Договор"),
                    ("ToEconomicMember", "em-1"),
                    ("Weight", 0.5),
                    ("Configuration", 1),
                    ("RussianPostId", null),
                    ("TypeEnvelope", "C6"),
                    ("DateAddedInPostRegister", null)),
            ],
            [LegacyTableNames.AccessMovements] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("MovementId", "mov-1"),
                    ("IncomingId", "inc-1"),
                    ("PerformerId", "em-1"),
                    ("MovementDate", "2024-05-03 10:00:00"),
                    ("MovementType", 1),
                    ("MovementTitle", "Отдел продаж"),
                    ("PutInCase", "Иванов")),
            ],
            [LegacyTableNames.AccessPostRegisters] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("PostRegisterId", "reg-1"),
                    ("NumberRegister", "7"),
                    ("RussianPostBatchName", "Партия-1"),
                    ("RussianPostShippingPoint", "Москва"),
                    ("RussianPostSendingDate", "2024-05-02 08:00:00"),
                    ("RussianPostLogin", "post"),
                    ("RussianPostPassword", "секрет"),
                    ("RussianPostToken", "токен")),
            ],
            [LegacyTableNames.AccessBlanks] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("Id", "bl-1"),
                    ("Name", "Конверт C6"),
                    ("Description", "Стандартный конверт"),
                    ("EnvSize", "C6"),
                    ("Source", "blanks\\c6.lbk"),
                    ("Type", 0)),
            ],
            [LegacyTableNames.AccessSettings] =
            [
                InMemoryLegacyTableSource.Строка(
                    ("SettId", "set-1"),
                    ("LastIncomingNumber", 15),
                    ("LastOutcomingNumber", 42),
                    ("CountLetterReg", 1),
                    ("UseShortname", "1"),
                    ("UseWordWrap", "1"),
                    ("RevertAddressLineOrder", "0"),
                    ("SortLetterRegAlphabet", "1"),
                    ("SaveXML", "1"),
                    ("FormRegister", "1"),
                    ("DefaultEnvelope", "C6"),
                    ("DefaultNotify", "Уведомление"),
                    ("DefaultSystemEnvelope", 1),
                    ("DefaultSystemNotify", 0),
                    ("DefaultInventory", "Опись"),
                    ("DefaultPostRegister", "Реестр"),
                    ("AddressWrapType", 1),
                    ("LastUsingExternalSearch", ""),
                    ("ContractNumber", "Д-12"),
                    ("SortOrder", "name"),
                    ("UserName", "Оператор"),
                    ("NeedName", "1"),
                    ("ProxyLogin", "proxy"),
                    ("ProxyPassword", "секрет"),
                    ("ProxyPort", "8080"),
                    ("ProxyServer", "proxy.local"),
                    ("SmtpPassword", "секрет"),
                    ("SmtpServer", "smtp.local"),
                    ("IsProxyAuthorization", "0"),
                    ("IsProxyUse", "0"),
                    ("NeedAuthorization", "0"),
                    ("NeedSSL", "0"),
                    ("ServiceLogin", "служба"),
                    ("ServiceMessage", "сообщение"),
                    ("ServiceMessageType", "sms"),
                    ("RussianPostLogin", "post"),
                    ("RussianPostPassword", "секрет"),
                    ("RussianPostToken", "токен"),
                    ("Filters", "отбор")),
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

        public string? ShortName { get; set; }

        public string? Inn { get; set; }

        public string? Kpp { get; set; }

        public string? Ogrn { get; set; }

        public string? Okfs { get; set; }

        public string? Okpo { get; set; }

        public string? Okved { get; set; }

        public string? Bik { get; set; }

        public string? Ks { get; set; }

        public string? Rs { get; set; }

        public string? Rkc { get; set; }

        public string? BankName { get; set; }

        public string? PassportSeries { get; set; }

        public string? PassportNumber { get; set; }

        public string? PassportIssuedBy { get; set; }

        public string? BirthDate { get; set; }

        public string? BirthPlace { get; set; }

        public string? Position { get; set; }

        public string? DirectorFio { get; set; }

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? SpecialNotes { get; set; }

        public string? PostZip { get; set; }

        public string? PostStr1 { get; set; }

        public string? PostStr2 { get; set; }

        public string? PostStr3 { get; set; }

        public int IsSender { get; set; }
    }

    /// <summary>Строка пользовательского поля контрагента для проверок.</summary>
    private sealed class СтрокаПоля
    {
        public string FieldName { get; set; } = string.Empty;

        public string? FieldValue { get; set; }
    }

    /// <summary>Строка отправления целевой базы для проверок.</summary>
    private sealed class СтрокаПисьма
    {
        public long CounterpartyId { get; set; }

        public long SenderId { get; set; }

        public decimal DeclaredValue { get; set; }

        public decimal Weight { get; set; }

        public decimal Fee { get; set; }

        public string? Note { get; set; }

        public int NeedNotification { get; set; }

        public int MailCategory { get; set; }

        public int Status { get; set; }

        public string? OutgoingNumber { get; set; }

        public string? RussianPostId { get; set; }

        public string? EnvelopeType { get; set; }

        public string? ToZip { get; set; }

        public string? ToStr1 { get; set; }
    }

    /// <summary>Строка макета печати целевой базы для проверок.</summary>
    private sealed class СтрокаМакета
    {
        public string Name { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public string Format { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;
    }
}
