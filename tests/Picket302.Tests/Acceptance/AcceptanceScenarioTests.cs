using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Picket302.Application.Export;
using Picket302.Application.Import;
using Picket302.Application.Services;
using Picket302.Application.Setup;
using Picket302.ComAutomation;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Domain.Backup;
using Picket302.Infrastructure.Backup;
using Picket302.Infrastructure.Data;
using Picket302.Infrastructure.Fonts;
using Picket302.Infrastructure.Queries;
using Picket302.Infrastructure.Repositories;
using Picket302.Printing;
using Picket302.Printing.Audit;
using Picket302.Printing.Devices;
using Picket302.Printing.Elements;
using Picket302.Printing.Engine;
using Picket302.Printing.Metrics;
using Picket302.Printing.Reports;
using Picket302.Printing.Rendering;
using Picket302.Printing.Validation;
using Picket302.Tests.Infrastructure;
using Xunit;

namespace Picket302.Tests.Acceptance;

/// <summary>
/// Сквозной приёмочный сценарий (ТЗ §11.1, §11.4; §8.12, функции 152–163):
/// демо-база → импорт CSV с валидацией (политика «только без ошибок») →
/// создание категории → рассылка (CreateFromSelection — эквивалент
/// drag&amp;drop) → пробная печать (print_mode = test, записи mails не
/// создаются) → пакетная печать F7 на мок-принтере → проверка
/// PrintJob/PrintHistory (включая отклонённые) → реестр ф.103 (итоги
/// сходятся) → экспорт XLSX без Office (zip-структура валидна) → COM
/// PrintEnvelope (out-of-proc, код 0, запись mails) → BackupNow → Restore
/// на чистую базу (записи на месте).
/// </summary>
/// <remarks>
/// Тест работает с реальным каталогом профиля (<c>%AppData%\Picket302</c>):
/// пользовательская база переносится в резервную копию и возвращается,
/// сервер пишет аудит печати. Коллекция «ПрофильПриложения» выполняет его
/// последовательно с защитными проверками изоляции профиля.
/// </remarks>
[Collection("ПрофильПриложения")]
public sealed class AcceptanceScenarioTests : IDisposable
{
    private readonly string _каталог = Path.Combine(
        Path.GetTempPath(),
        "Picket302Acceptance_" + Guid.NewGuid().ToString("N"));

    private readonly string _путьБазы;
    private readonly string _папкаАрхивов;

    public AcceptanceScenarioTests()
    {
        Directory.CreateDirectory(_каталог);
        _путьБазы = Path.Combine(_каталог, "picket302.db");
        _папкаАрхивов = Path.Combine(_каталог, "архивы");
        Directory.CreateDirectory(_папкаАрхивов);
    }

    public void Dispose()
    {
        // Пул соединений может ещё держать файл базы в момент Dispose:
        // сбрасываем пул и повторяем удаление, чтобы временные каталоги
        // приёмки не накапливались в %TEMP% (ТЗ §11.5).
        SqliteConnection.ClearAllPools();

        for (var попытка = 0; попытка < 5; попытка++)
        {
            try
            {
                if (Directory.Exists(_каталог))
                {
                    Directory.Delete(_каталог, recursive: true);
                }

                break;
            }
            catch (IOException)
            {
                // Каталог ещё занят завершающимися соединениями — повторяем.
                Thread.Sleep(200);
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Сквозной_сценарий_приёмки_от_демо_базы_до_восстановления_архива()
    {
        // ==================================================================
        // Шаг 1. Демо-база при первом запуске (функции 137, 153).
        // ==================================================================
        var путьШаблона = Path.Combine(_каталог, "Конверт_C6_приёмка.lbk");
        await File.WriteAllTextAsync(путьШаблона, "LBK-заглушка приёмочного сценария");

        var идДемоПолучателей = new List<long>();

        await using (var база = await SqliteTestDatabase.CreateFileAsync(_путьБазы))
        {
            var контрагенты = new CounterpartyRepository(база.Session);
            var группы = new GroupRepository(база.Session);
            var сеялка = new DemoDatabaseSeeder(
                группы,
                контрагенты,
                new CounterpartyFieldRepository(база.Session),
                new DatabaseMaintenanceRepository(база.Session),
                база.UnitOfWork);

            var создано = await сеялка.SeedDemoDataAsync();

            Assert.Equal(6, создано);

            var категории = await new CategoryTreeService(группы, контрагенты, база.UnitOfWork)
                .GetTreeAsync();

            // Категории демо-базы: «Неразобранные» (по умолчанию), «Поставщики»,
            // «Получатели», «Архив».
            Assert.Contains(категории, к => к.IsDefault && к.Name == "Неразобранные");
            Assert.Contains(категории, к => к.Name == "Поставщики");
            Assert.Contains(категории, к => к.Name == "Получатели");

            // ==================================================================
            // Шаг 2. Создание категории (функции 138, 154): дерево, новая
            // категория для импортируемых записей.
            // ==================================================================
            var службаКатегорий = new CategoryTreeService(группы, контрагенты, база.UnitOfWork);
            var категория = await службаКатегорий.CreateCategoryAsync("Клиенты (импорт)");

            Assert.True(категория.IsSuccess, категория.Error);
            Assert.NotNull(категория.Value);

            // ==================================================================
            // Шаг 3. Импорт CSV с валидацией, политика «только без ошибок»
            // (функции 71–73, 148, 159): три записи, одна с неверным ИНН.
            // ==================================================================
            var путьCsv = Path.Combine(_каталог, "импорт.csv");
            await File.WriteAllTextAsync(
                путьCsv,
                "Название организации;ИНН;Почтовый адрес\r\n"
                + "ООО «Приёмочная Лаборатория»;7701234560;125009, г. Москва, ул. Приёмочная, д. 1\r\n"
                + "ООО «Второй Экспресс»;7722334453;630005, г. Новосибирск, ул. Доставочная, д. 5\r\n"
                + "ООО «Битый Индекс»;123;101000, г. Москва, ул. Ошибочная, д. 9\r\n",
                new System.Text.UTF8Encoding(false));

            var сессия = ImportSession.Start(путьCsv, new ImportStartOptions
            {
                FilePath = путьCsv,
                Delimiter = ';',
                EncodingName = "utf-8",
                Repository = контрагенты,
                GroupId = категория.Value!.Id,
            });

            Assert.True(сессия.IsSuccess, сессия.Error);

            var импорт = сессия.Value!;
            var сводка = импорт.ValidateAll();

            Assert.Equal(3, сводка.TotalCount);
            Assert.Equal(1, сводка.ErrorCount);

            // Индекс разобран из строки адреса (функция 5: parseZipIndexFromAddress).
            Assert.Equal("125009", импорт.Records[0].Draft.PostalZip);

            var итог = импорт.Commit(ImportPolicy.WithoutErrors);

            Assert.Equal(2, итог.Created);
            Assert.Equal(0, итог.Errors);
            Assert.Equal(1, итог.Skipped);

            var выборка = await контрагенты.GetPagedAsync(new PageRequest(0, 100));

            Assert.Equal(8, выборка.TotalCount);

            // Импортированные записи попали в новую категорию (функция 159).
            var импортированные = await контрагенты.GetByGroupAsync(категория.Value.Id);

            Assert.Equal(2, импортированные.Count);

            // ==================================================================
            // Шаг 4. Отправитель (функция 139: флаг «Это отправитель») и
            // рассылка CreateFromSelection — эквивалент drag&amp;drop и
            // «Добавить всех активных» (функции 140, 141, 155).
            // ==================================================================
            var все = (await контрагенты.GetPagedAsync(new PageRequest(0, 100))).Items;
            var отправитель = Assert.Single(все, к => к.Name == "ФГУП «Почта Тест»");

            отправитель.IsSender = true;
            Assert.Equal(1, await контрагенты.UpdateAsync(отправитель));

            var получатели = все
                .Where(к => к.Name is "ИП Смирнов А. П." or "ООО «Балтика-Логистик»" or "ООО «Приёмочная Лаборатория»")
                .Select(к => к.Id)
                .ToList();

            Assert.Equal(3, получатели.Count);

            идДемоПолучателей.AddRange(
                все.Where(к => к.Name is "ИП Смирнов А. П." or "ООО «Балтика-Логистик»").Select(к => к.Id));

            var рассылка = await new DistributionService(
                new DistributionRepository(база.Session),
                new MailRepository(база.Session),
                контрагенты,
                база.UnitOfWork).CreateFromSelectionAsync(
                получатели,
                отправитель.Id,
                "Приёмочная рассылка",
                "C6");

            Assert.True(рассылка.IsSuccess, рассылка.Error);

            // Шаги 5–7 выполняются в этом же блоке: печать работает
            // с подключённой базой.
            var письма = await new MailRepository(база.Session)
                .GetByDistributionAsync(рассылка.Value!.Id);

            Assert.Equal(3, письма.Count);

            // Ценность, плата и вес — данные реестра ф.103 (функция 27).
            var тарифы = new[] { (105.50m, 1500m, 0.250m), (86.00m, 0m, 0.115m), (132.75m, 3200m, 0.480m) };

            for (var i = 0; i < письма.Count; i++)
            {
                письма[i].Fee = тарифы[i].Item1;
                письма[i].DeclaredValue = тарифы[i].Item2;
                письма[i].Weight = тарифы[i].Item3;
                await new MailRepository(база.Session).UpdateAsync(письма[i]);
            }

            // Шаблон конверта с полями (функции 36, 39): структурированный
            // макет в базе, поля по тегам словаря.
            var шаблоны = new PrintTemplateRepository(база.Session);
            var идШаблона = await шаблоны.AddAsync(new PrintTemplate
            {
                Name = "Конверт C6 (приёмка)",
                Kind = PrintTemplateKind.Envelope,
                Format = "C6",
                WidthMm = 114m,
                HeightMm = 162m,
                Orientation = TemplateOrientation.Portrait,
                FilePath = путьШаблона,
                Country = "RUS",
            });

            var поляШаблона = new BlankFieldRepository(база.Session);

            foreach (var (имя, x, y) in new[]
                     {
                         ("[edt_str_to1]", 8m, 20m),
                         ("[edt_str_to2]", 8m, 30m),
                         ("[edtIndeks_to]", 12m, 8m),
                     })
            {
                await поляШаблона.AddAsync(new BlankField
                {
                    TemplateId = идШаблона,
                    Name = имя,
                    Kind = BlankFieldKind.Text,
                    XMm = x,
                    YMm = y,
                    LengthMm = 60m,
                    HeightMm = 8m,
                });
            }

            var идПисем = письма.Select(п => п.Id).ToList();
            var журналПечати = new PrintJobRepository(база.Session);
            var каналАудита = new PrintAuditWriter(_каталог);

            // ==================================================================
            // Шаг 5. Пробная печать (функция 143): print_mode = test,
            // записи mails не создаются.
            // ==================================================================
            var пробная = await СобратьКонтроллер(
                база,
                каналАудита,
                new СпулерПриёмки(),
                new ПринтерПриёмки()).PrintTestAsync(
                идПисем[0],
                new PrintRequest
                {
                    TemplateId = идШаблона,
                    PrinterName = ПринтерПриёмки.ИмяМокПринтера,
                });

            Assert.True(пробная.IsSuccess, пробная.ErrorMessage);

            // После пробной печати число отправлений не изменилось.
            Assert.Equal(3, (await new MailRepository(база.Session).GetByDistributionAsync(рассылка.Value.Id)).Count);

            // ==================================================================
            // Шаг 6. Пакетная печать всех выделенных без предпросмотра — F7
            // (функция 79) на мок-принтере; история печати (функция 64).
            // ==================================================================
            var партия = await СобратьКонтроллер(
                база,
                каналАудита,
                new СпулерПриёмки(),
                new ПринтерПриёмки()).PrintSelectionDirectAsync(
                идПисем,
                new PrintRequest
                {
                    TemplateId = идШаблона,
                    PrinterName = ПринтерПриёмки.ИмяМокПринтера,
                });

            Assert.True(партия.IsSuccess, партия.ErrorMessage);
            Assert.Equal(PrintJobStatus.Success, партия.Status);

            // История печати: снимок на каждый физический вывод конверта.
            // Снимки создаёт сама печатная сессия (функция 64), а не тест.
            var снимки = await new PrintHistoryRepository(база.Session)
                .GetPagedAsync(new PageRequest(0, 100));

            Assert.Equal(3, снимки.TotalCount);
            Assert.All(
                снимки.Items,
                снимок => Assert.Equal(партия.PrintJobId, снимок.PrintJobId));

            foreach (var письмо in письма)
            {
                var снимок = Assert.Single(
                    снимки.Items,
                    запись => запись.MailId == письмо.Id);

                Assert.Contains(письмо.RecipientAddress?.Zip ?? string.Empty, снимок.RecipientAddressSnapshot);
                Assert.Contains(
                    отправитель.GetAddress(отправитель.AddressMainType)?.Zip ?? string.Empty,
                    снимок.SenderAddressSnapshot);
                Assert.Equal(письмо.CopyCount, снимок.CopyCount);
                Assert.Equal(партия.PrintJobId, снимок.PrintJobId);
            }

            // Пробная печать (print_mode = test) учётной записи истории
            // печати не создаёт: в журнале только три конверта партии.
            Assert.Equal(3, снимки.TotalCount);

            // ==================================================================
            // Шаг 7. Отклонённая печать (функции 83, 85): печатная область
            // мок-принтера меньше бланка — задание блокируется и попадает
            // в журнал со статусом BLOCKED_SIZE (функция 88).
            // ==================================================================
            var отклонённая = await СобратьКонтроллер(
                база,
                каналАудита,
                new СпулерПриёмки(),
                new ПринтерПриёмки(маленький: true)).PrintSelectionDirectAsync(
                идПисем,
                new PrintRequest
                {
                    TemplateId = идШаблона,
                    PrinterName = ПринтерПриёмки.ИмяМокПринтера,
                });

            Assert.False(отклонённая.IsSuccess);
            Assert.Equal(PrintJobStatus.BlockedSize, отклонённая.Status);
            Assert.Contains(PaperSizeValidator.BlockedSizeMessage, отклонённая.ErrorMessage ?? string.Empty);

            // ==================================================================
            // Проверка PrintJob/PrintHistory (включая отклонённые).
            // ==================================================================
            var записиЖурнала = await журналПечати.GetPagedAsync(new PageRequest(0, 100));

            Assert.Equal(3, записиЖурнала.TotalCount);

            var пробнаяЗапись = Assert.Single(записиЖурнала.Items, з => з.PrintMode == PrintModes.Test);
            Assert.Equal(PrintJobStatus.Success, пробнаяЗапись.Status);

            // Успешная партийная печать: в журнале ровно одна запись со
            // статусом SUCCESS (отклонённая попытка — та же пакетная печать,
            // но со статусом BLOCKED_SIZE, поэтому она отбирается отдельно).
            var партийнаяЗапись = Assert.Single(
                записиЖурнала.Items,
                з => з.PrintMode == PrintModes.BatchDirect && з.Status == PrintJobStatus.Success);
            Assert.Equal(PrintJobStatus.Success, партийнаяЗапись.Status);
            Assert.Equal(ПринтерПриёмки.ИмяМокПринтера, партийнаяЗапись.PrinterName);

            var отклонённаяЗапись = Assert.Single(
                записиЖурнала.Items,
                з => з.Status == PrintJobStatus.BlockedSize);
            Assert.NotNull(отклонённаяЗапись.ErrorText);

            // Журнал аудита печати содержит запись каждого прогона,
            // включая отклонённый (§11.4).
            var текстАудита = await File.ReadAllTextAsync(Path.Combine(_каталог, PrintAuditWriter.FileName));

            Assert.Contains("Статус: УСПЕШНО", текстАудита);
            Assert.Contains("Статус: ЗАБЛОКИРОВАНО", текстАудита);

            // ==================================================================
            // Шаг 8. Реестр ф.103 (функции 27, 50): итоги сходятся с суммой
            // строк табличной части.
            // ==================================================================
            var форма103 = await new Form103Report(new SqliteReportDataProvider(база.Connection))
                .GenerateF103(идПисем.ToArray(), отправитель.Id);

            Assert.Equal(3, форма103.Main.Rows.Count);
            Assert.True(Form103Report.TotalsMatch(форма103), "Итоги ф.103 не сходятся с суммой строк.");

            Assert.Equal(тарифы.Sum(т => т.Item1), форма103.Main.TotalValues["Плата"]);
            Assert.Equal(тарифы.Sum(т => т.Item2), форма103.Main.TotalValues["Ценность"]);
            Assert.Equal(тарифы.Sum(т => т.Item3), форма103.Main.TotalValues["Вес"]);
            Assert.Equal(3L, форма103.Main.TotalValues["Отправлений"]);

            // ==================================================================
            // Шаг 9. Экспорт адресной книги в XLSX без Office (функции 70,
            // 71, 160): zip-структура книги валидна.
            // ==================================================================
            var путьXlsx = Path.Combine(_каталог, "адресная-книга.xlsx");

            using (var поток = File.Create(путьXlsx))
            {
                new UniversalAddressExchange().ExportData(
                    все,
                    ExchangeFormat.Xlsx,
                    поток);
            }

            using (var книга = System.IO.Compression.ZipFile.OpenRead(путьXlsx))
            {
                foreach (var часть in new[]
                         {
                             OpenXmlSpreadsheetExporter.ContentTypesPartName,
                             OpenXmlSpreadsheetExporter.PackageRelationshipsPartName,
                             OpenXmlSpreadsheetExporter.WorkbookPartName,
                             OpenXmlSpreadsheetExporter.WorkbookRelationshipsPartName,
                             OpenXmlSpreadsheetExporter.WorksheetPartName,
                             OpenXmlSpreadsheetExporter.SharedStringsPartName,
                             OpenXmlSpreadsheetExporter.StylesPartName,
                         })
                {
                    Assert.NotNull(книга.GetEntry(часть));
                }

                // Выгружены все 8 контрагентов: строка заголовков + 8 строк
                // данных (последняя строка листа имеет номер 9).
                var лист = new StreamReader(
                    книга.GetEntry(OpenXmlSpreadsheetExporter.WorksheetPartName)!.Open()).ReadToEnd();

                Assert.Contains("r=\"9\"", лист);

                var строки = new StreamReader(
                    книга.GetEntry(OpenXmlSpreadsheetExporter.SharedStringsPartName)!.Open()).ReadToEnd();

                Assert.Contains("ФГУП «Почта Тест»", строки);
                Assert.Contains("ООО «Приёмочная Лаборатория»", строки);
            }

            // Шаг 10 (COM out-of-proc) выполняется после закрытия базы;
            // шаг 11 — архивирование и восстановление.
        }

        // ==================================================================
        // Шаг 11. BackupNow → восстановление на чистую базу (функции 103,
        // 104, 129, 161): после восстановления записи на месте.
        // ==================================================================
        var известныеВерсии = new MigrationRunner().Migrations
            .Select(миграция => миграция.Version)
            .ToList();
        var службаАрхива = new BackupService(_путьБазы, известныеВерсии);

        var архив = службаАрхива.BackupNow(new BackupOptions(_папкаАрхивов));

        Assert.True(архив.IsSuccess, архив.Error);

        // База очищается до пустой («чистая база»): схема без данных.
        // Пул соединений Microsoft.Data.Sqlite удерживает файл базы —
        // освобождаем его перед удалением (как это делает BackupService).
        SqliteConnection.ClearAllPools();
        УдалитьФайлСПовтором(_путьБазы);

        Assert.False(File.Exists(_путьБазы), "Файл базы не удалён перед восстановлением.");

        await using (var чистая = await SqliteTestDatabase.CreateFileAsync(_путьБазы))
        {
            var пусто = await чистая.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM counterparties;");

            Assert.Equal(0, пусто);
        }

        var восстановление = службаАрхива.RestoreFromArchive(архив.ArchivePath!, string.Empty);

        Assert.True(восстановление.IsSuccess, восстановление.Error);

        await using (var база = await SqliteTestDatabase.CreateFileAsync(_путьБазы, сМиграциями: false))
        {
            Assert.Equal(8, await база.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM counterparties;"));
            Assert.Equal(3, await база.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM mails;"));
            Assert.Equal(3, await база.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM print_jobs;"));
            Assert.Equal(3, await база.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM print_history;"));

            var имяИмпортной = await база.Connection.ExecuteScalarAsync<string?>(
                "SELECT name FROM counterparties WHERE name LIKE 'ООО «Приёмочная%';");

            Assert.Equal("ООО «Приёмочная Лаборатория»", имяИмпортной);
        }
    }

    [Fact]
    public async Task Com_PrintEnvelope_OutOfProc_ВозвращаетКод0_И_СоздаётЗаписьMails()
    {
        // Предусловие приёмки: принтер по умолчанию установлен
        // (функция 78). Без него сценарий COM-печати невыполним.
        var принтер = new PrinterManager().GetDefaultPrinter();
        Assert.True(
            принтер is not null,
            "Принтер по умолчанию не установлен — COM-печать не может быть проверена (см. функцию 78).");

        // Изолируем базу профиля пользователя: сервер работает с
        // %AppData%\Picket302\picket302.db (ТЗ §9.2).
        var каталогПриложения = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ComAutomationEnvironment.ApplicationDirectoryName);
        Directory.CreateDirectory(каталогПриложения);

        var путьБазыСервера = Path.Combine(каталогПриложения, ComAutomationEnvironment.DatabaseFileName);
        var копияПользовательской = путьБазыСервера + ".acceptance-" + Guid.NewGuid().ToString("N") + ".bak";
        var сохранилиПользовательскую = false;

        var путьШаблона = Path.Combine(каталогПриложения, "Конверт_C6_COM.lbk");
        var телоВыполнено = false;

        // Локальный COM-сервер держит файл базы профиля открытым, поэтому
        // его нужно остановить до переноса базы (иначе файл останется
        // занят завершающимся процессом).
        ОстановитьСерверы();

        if (File.Exists(путьБазыСервера))
        {
            ПереместитьФайлСПовтором(путьБазыСервера, копияПользовательской);
            сохранилиПользовательскую = true;
        }

        try
        {
            await File.WriteAllTextAsync(путьШаблона, "LBK-заглушка COM-сценария");

            // Подготовка базы сервера: схема, отправитель, шаблон конверта
            // с полями. Отправлений нет — их создаёт PrintEnvelope.
            var строкаПодключения = new SqliteConnectionStringBuilder
            {
                DataSource = путьБазыСервера,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            await using (var база = new SqliteUnitOfWork(строкаПодключения))
            {
                await new MigrationRunner().ApplyAsync(база.Connection);

                var контрагенты = new CounterpartyRepository(база);
                await контрагенты.AddAsync(new Counterparty
                {
                    Name = "ООО «Отправитель Приёмки»",
                    IsSender = true,
                    IsActive = true,
                    PostalAddress = Address.Create(
                        "101000",
                        ["г. Москва", "ул. Приёмочная, д. 1", string.Empty, string.Empty, string.Empty, string.Empty]).Value!,
                    AddressMainType = AddressMainType.Postal,
                });

                var шаблоны = new PrintTemplateRepository(база);
                var идШаблона = await шаблоны.AddAsync(new PrintTemplate
                {
                    Name = "Конверт C6 (COM)",
                    Kind = PrintTemplateKind.Envelope,
                    Format = "C6",
                    WidthMm = 114m,
                    HeightMm = 162m,
                    Orientation = TemplateOrientation.Portrait,
                    FilePath = путьШаблона,
                    Country = "RUS",
                });

                var поля = new BlankFieldRepository(база);

                foreach (var (имя, x, y) in new[]
                         {
                             ("[edt_str_to1]", 8m, 20m),
                             ("[edt_str_to2]", 8m, 30m),
                             ("[edtIndeks_to]", 12m, 8m),
                         })
                {
                    await поля.AddAsync(new BlankField
                    {
                        TemplateId = идШаблона,
                        Name = имя,
                        Kind = BlankFieldKind.Text,
                        XMm = x,
                        YMm = y,
                        LengthMm = 60m,
                        HeightMm = 8m,
                    });
                }
            }

            // Регистрация COM-сервера в HKCU (функция 114): ProgID, CLSID,
            // LocalServer32, интерфейс и библиотека типов.
            var регистрация = new ComRegistration(
                null,
                Path.Combine(AppContext.BaseDirectory, ComRegistration.ServerFileName),
                Path.Combine(AppContext.BaseDirectory, ComRegistration.TypeLibraryFileName));

            регистрация.Register();

            try
            {
                // Регистрация проверяется по ветке классов пользователя:
                // ProgID, CLSID, LocalServer32, библиотека типов
                // (ТЗ §9.3, функция 114).
                Assert.Equal(
                    $"{{{ComAutomationIds.ClassId}}}",
                    ComRegistration.ReadValue(
                        $@"Software\Classes\{ComAutomationIds.ProgId}",
                        "CLSID"));

                Assert.True(
                    ComRegistration.KeyExists(
                        $@"Software\Classes\CLSID\{{{ComAutomationIds.ClassId}}}\LocalServer32"),
                    "В ветке классов отсутствует запись LocalServer32.");

                Assert.True(
                    ComRegistration.KeyExists(
                        $@"Software\Classes\TypeLib\{{{ComAutomationIds.TypeLibId}}}\{ComAutomationIds.TypeLibVersion}\0\win64"),
                    "В ветке классов отсутствует библиотека типов версии 2.2.");

                // Активация out-of-proc: LocalServer32 запускает отдельный
                // процесс сервера, фабрика классов создаёт PEProApplication.
                // ProgID используется, когда среда разрешает его разрешение
                // в CLSID; иначе (per-user регистрация не видна ole32)
                // активация выполняется по идентификатору класса.
                var тип = Type.GetTypeFromProgID(ComAutomationIds.ProgId, throwOnError: false)
                          ?? Type.GetTypeFromCLSID(new Guid(ComAutomationIds.ClassId));

                Assert.True(тип is not null, "COM-сервер PEPro.Application не зарегистрирован.");

                object? приложение = null;
                var последнийКод = 0;

                for (var попытка = 0; попытка < 40 && приложение is null; попытка++)
                {
                    try
                    {
                        приложение = Activator.CreateInstance(тип!);
                    }
                    catch (COMException ошибка)
                    {
                        // Сервер ещё не зарегистрировал фабрику классов: код
                        // сохраняется для диагностики причины (ТЗ §10.3).
                        последнийКод = ошибка.HResult;

                        await Task.Delay(500);
                    }
                }

                Assert.True(
                    приложение is not null,
                    "COM-сервер PEPro.Application не активирован. " + ОписатьСостояниеАктивации(последнийКод));

                try
                {
                    // SafeLoadDatabase: контракт требует вызова метода перед
                    // операциями с пользовательскими бланками (ТЗ §9.2).
                    // Значение параметра [out, retval] не переносится
                    // клиентом при позднем связывании через IDispatch, поэтому
                    // проверяется код HRESULT, а факт загрузки базы
                    // подтверждается записью отправления в таблице mails.
                    var кодЗагрузки = ВызватьСКодом(тип!, приложение, "SafeLoadDatabase", new object?[] { null });

                    Assert.Equal(0, кодЗагрузки);

                    // Печать конверта (функция 113, метод PrintEnvelope):
                    // код 0 — успех, отправление записано в mails.
                    var код = (int)тип.InvokeMember(
                        "PrintEnvelope",
                        BindingFlags.InvokeMethod,
                        null,
                        приложение,
                        new object?[]
                        {
                            "ООО «Отправитель Приёмки»",
                            "г. Москва, ул. Приёмочная, д. 1",
                            "101000",
                            "Получатель Из COM",
                            "г. Тула, ул. Комсомольская, д. 7",
                            "300000",
                        },
                        CultureInfo.InvariantCulture)!;

                    Assert.Equal(0, код);

                    // Запись mails создана (слепая печать: вызов не блокируется
                    // окнами — ТЗ §9.2).
                    await using (var проверка = new SqliteUnitOfWork(строкаПодключения))
                    {
                        Assert.Equal(
                            1,
                            await проверка.Connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM mails;"));

                        var получатель = await проверка.Connection.ExecuteScalarAsync<string?>(
                            "SELECT c.name FROM mails m JOIN counterparties c ON m.counterparty_id = c.id;");

                        Assert.Equal("Получатель Из COM", получатель);
                    }

                    телоВыполнено = true;
                }
                finally
                {
                    if (приложение is not null)
                    {
                        Marshal.FinalReleaseComObject(приложение);
                    }
                }
            }
            finally
            {
                регистрация.Unregister();

                // Сервер держит файл базы: останавливаем процесс и дожидаемся
                // его завершения, иначе файл остаётся заблокированным.
                ОстановитьСерверы();
            }
        }
        finally
        {
            // Пул соединений теста удерживает файл базы профиля: освобождаем
            // его перед заменой пользовательской базы.
            SqliteConnection.ClearAllPools();

            Exception? ошибкаОчистки = null;

            try
            {
                УдалитьФайлСПовтором(путьШаблона);
                УдалитьФайлСПовтором(путьБазыСервера);
            }
            catch (Exception ошибка) when (ошибка is IOException or UnauthorizedAccessException)
            {
                ошибкаОчистки = ошибка;
            }

            if (сохранилиПользовательскую)
            {
                try
                {
                    ПереместитьФайлСПовтором(копияПользовательской, путьБазыСервера);
                }
                catch (Exception ошибка) when (ошибка is IOException or UnauthorizedAccessException)
                {
                    ошибкаОчистки ??= ошибка;
                }
            }

            // Ошибка очистки не должна скрывать результат тела сценария:
            // она сообщается, только если сам сценарий прошёл успешно.
            if (ошибкаОчистки is not null && телоВыполнено)
            {
                throw new IOException(
                    "Не удалось вернуть базу данных профиля пользователя после COM-сценария.",
                    ошибкаОчистки);
            }
        }
    }

    /// <summary>
    /// Собирает диагностику чистой COM-активации и различает причины сбоя:
    /// ProgID не разрешился; CLSID не зарегистрирован; LocalServer32
    /// отсутствует или указывает на несуществующий файл; процесс сервера
    /// запустился, но фабрика не зарегистрирована; фабрика не вернула
    /// интерфейс.
    /// </summary>
    /// <param name="hresult">Последний код активации; 0 — исключений не было.</param>
    /// <returns>Однострочная диагностика для сообщения проверки.</returns>
    private static string ОписатьСостояниеАктивации(int hresult)
    {
        var части = new List<string>();

        if (hresult != 0)
        {
            части.Add($"последний HRESULT=0x{hresult:X8}");
        }

        var clsidПоПrogId = ComRegistration.ReadValue(
            $@"Software\Classes\{ComAutomationIds.ProgId}",
            "CLSID");

        части.Add(clsidПоПrogId is null
            ? "ProgID PEPro.Application не разрешился (ключ отсутствует)"
            : $"ProgID → {clsidПоПrogId}");

        var ветка = $@"Software\Classes\CLSID\{{{ComAutomationIds.ClassId}}}";

        части.Add(ComRegistration.KeyExists(ветка)
            ? "CLSID зарегистрирован"
            : "CLSID не зарегистрирован");

        var сервер = ComRegistration.ReadValue($@"{ветка}\LocalServer32")?.Trim('"');

        части.Add(сервер is null
            ? "LocalServer32 отсутствует"
            : File.Exists(сервер)
                ? $"LocalServer32 → {сервер} (файл существует)"
                : $"LocalServer32 указывает на отсутствующий файл: {сервер}");

        try
        {
            var процессы = Process.GetProcessesByName("Picket302.ComAutomation");

            части.Add($"процессов Picket302.ComAutomation: {процессы.Length}");

            foreach (var процесс in процессы)
            {
                процесс.Dispose();
            }
        }
        catch (Exception)
        {
            // Диагностика не должна маскировать исходную ошибку активации.
        }

        return "Диагностика: " + string.Join("; ", части) + ".";
    }

    /// <summary>
    /// Останавливает процессы локального COM-сервера и дожидается их
    /// завершения: файл базы освобождается не мгновенно после kill.
    /// </summary>
    private static void ОстановитьСерверы()
    {
        foreach (var процесс in Process.GetProcessesByName("Picket302.ComAutomation"))
        {
            try
            {
                процесс.Kill(entireProcessTree: true);
                процесс.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // Процесс уже завершён.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Процесс завершился между поиском и остановкой.
            }
            finally
            {
                процесс.Dispose();
            }
        }
    }

    /// <summary>
    /// Удаляет файл с повторами: файл может быть ещё занят завершающимся
    /// процессом сервера.
    /// </summary>
    private static void УдалитьФайлСПовтором(string путь)
    {
        for (var попытка = 0; попытка < 20; попытка++)
        {
            try
            {
                if (File.Exists(путь))
                {
                    File.Delete(путь);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }
    }

    /// <summary>
    /// Перемещает файл с повторами: файл может быть ещё занят завершающимся
    /// процессом COM-сервера.
    /// </summary>
    private static void ПереместитьФайлСПовтором(string источник, string приёмник)
    {
        for (var попытка = 0; попытка < 20; попытка++)
        {
            try
            {
                File.Move(источник, приёмник, overwrite: true);

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }

        File.Move(источник, приёмник, overwrite: true);
    }

    /// <summary>
    /// Вызывает метод COM-объекта с единственным выходным параметром
    /// и возвращает его значение.
    /// </summary>
    private static int ВызватьСКодом(Type тип, object экземпляр, string метод, object?[] аргументы)
    {
        var результат = тип.InvokeMember(
            метод,
            BindingFlags.InvokeMethod,
            null,
            экземпляр,
            аргументы,
            CultureInfo.InvariantCulture);

        return Convert.ToInt32(результат, CultureInfo.InvariantCulture);
    }

        private static PrintSessionController СобратьКонтроллер(
        SqliteTestDatabase база,
        PrintAuditWriter аудит,
        IPrintSpooler спулер,
        PrinterManager принтеры)
    {
        var письма = new MailRepository(база.Session);
        var контрагенты = new CounterpartyRepository(база.Session);

        // История печати (функция 64) подключена к конвейеру печати: снимок
        // адресов создаёт сама печатная сессия, а не тест вручную.
        var службаПисем = new MailService(
            письма,
            контрагенты,
            new PrintHistoryRepository(база.Session),
            база.UnitOfWork);

        return new PrintSessionController(
            new PrintTemplateRepository(база.Session),
            new BlankFieldRepository(база.Session),
            new PrintJobRepository(база.Session),
            new PrintDataSource(письма, контрагенты),
            аудит,
            спулер,
            new EnvelopeRenderer(new ИзмерительПриёмки()),
            new PaperSizeValidator(),
            new PostIndexRenderer(new ШрифтДоступен()),
            new QrCodeRenderer(),
            new LogoRenderer(),
            принтеры,
            printHistory: new PrintHistoryRecorder(службаПисем));
    }

    /// <summary>Мок-принтер приёмки (§11.2: DEVMODE-мок).</summary>
    private sealed class ПринтерПриёмки : PrinterManager
    {
        public const string ИмяМокПринтера = "Picket302 Мок-Принтер";

        private readonly bool _маленький;

        public ПринтерПриёмки(bool маленький = false) => _маленький = маленький;

        public override PrinterDeviceInfo GetDeviceInfo(
            string printerName,
            short paperSize = DevModeConstants.DMPAPER_USER) =>
            _маленький
                ? new()
                {
                    PrinterName = printerName,
                    PaperSizeName = "USER",
                    PaperWidthMm = 100m,
                    PaperHeightMm = 150m,
                    PrintableWidthMm = 96m,
                    PrintableHeightMm = 140m,
                    DmPaperSize = paperSize,
                    AvailablePaperSizes = new[] { "USER" },
                }
                : new()
                {
                    PrinterName = printerName,
                    PaperSizeName = "C5",
                    PaperWidthMm = 162m,
                    PaperHeightMm = 229m,
                    PrintableWidthMm = 150m,
                    PrintableHeightMm = 220m,
                    DmPaperSize = paperSize,
                    AvailablePaperSizes = new[] { "C5", "A4" },
                };
    }

    /// <summary>Мок-спулер приёмки: задание не выводится на устройство.</summary>
    private sealed class СпулерПриёмки : IPrintSpooler
    {
        public int Вызовов { get; private set; }

        public Task PrintAsync(
            PrintJobContext context,
            Action<IGraphicsContext> render,
            CancellationToken cancellationToken)
        {
            Вызовов++;

            return Task.CompletedTask;
        }
    }

    /// <summary>Детерминированный измеритель текста (§11.2).</summary>
    private sealed class ИзмерительПриёмки : ITextMeasurer
    {
        public double MeasureWidth(string text, string fontName, decimal fontSizePt) =>
            text.Length * (double)fontSizePt * 0.5;

        public double MeasureLineHeight(string fontName, decimal fontSizePt) => (double)fontSizePt * 1.2;
    }

    /// <summary>
    /// Мок-контроль шрифта ZIPcode (§11.2): на приёмочной стенде шрифт
    /// считается установленным. Отсутствие шрифта проверяется отдельным
    /// сценарием, который использует реальный <see cref="ZipcodeFontGuard"/>.
    /// </summary>
    private sealed class ШрифтДоступен : IZipcodeFontGuard
    {
        public bool IsZipcodeFontAvailable => true;

        public bool EnsureZipcodeFontInstalled() => true;
    }
}
