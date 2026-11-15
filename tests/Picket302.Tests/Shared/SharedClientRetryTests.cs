using System.Text.Json;
using Picket302.App.Shared;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Shared.Dtos;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Shared.Commands;
using Picket302.Shared.Protocol;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Повтор мутаций после неопределённого сетевого исхода.
/// </summary>
/// <remarks>
/// Сценарий проходит через настоящий клиентский шлюз и HTTP+TLS: служба
/// фиксирует команду, первый ответ теряется управляемо, клиент повторяет
/// действие — в базе остаётся ровно одна логическая запись и один номер.
/// </remarks>
public sealed class SharedClientRetryTests
{
    [Fact]
    public async Task Повтор_После_Потери_Ответа_Не_Создаёт_Дубль_Контрагента()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();
        var каталог = КаталогПроверки();

        try
        {
            using var транспорт = await ТранспортСПотерей(стенд, "ПК оператора 1", "commands/counterparties.save");
            var незавершённые = Хранилище(каталог);
            var шлюз = new RemoteDataGateway(транспорт, незавершённые);

            // Ответ потерян после отправки: сервер уже зафиксировал команду,
            // исход для клиента неизвестен.
            var ошибка = await Assert.ThrowsAsync<SharedOutcomeUnknownException>(
                () => шлюз.СохранитьКонтрагентаAsync(Домен("ООО «Повтор»")));

            Assert.Contains("неизвестен", ошибка.Message);
            Assert.Contains("могла быть выполнена", ошибка.Message);
            Assert.Single(шлюз.НезавершённыеОперации);

            // Команда дошла до сервера: запись видна шлюзу, и она одна.
            Assert.Single((await шлюз.ПолучитьКонтрагентовAsync(new PageRequest(0, 50))).Items);

            var идентификатор = шлюз.НезавершённыеОперации[0].Идентификатор;

            await шлюз.ПовторитьНезавершённуюAsync(идентификатор);

            Assert.Empty(шлюз.НезавершённыеОперации);
            Assert.Single((await шлюз.ПолучитьКонтрагентовAsync(new PageRequest(0, 50))).Items);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public async Task Повтор_Выдачи_Номера_Не_Пропускает_Номера()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();
        var каталог = КаталогПроверки();

        try
        {
            using var транспорт = await ТранспортСПотерей(стенд, "ПК оператора 1", "commands/mails.assign-number");
            var незавершённые = Хранилище(каталог);
            var шлюз = new RemoteDataGateway(транспорт, незавершённые);

            var отправитель = await шлюз.СоздатьОтправителяAsync(new AgentDto { Name = "ООО «Отправитель»" });
            Assert.True(отправитель.IsSuccess);

            var получатель = await шлюз.СохранитьКонтрагентаAsync(Домен("ООО «Получатель»"));
            Assert.True(получатель.IsSuccess);

            var первое = await шлюз.СоздатьПисьмоAsync(Отправление(
                получатель.Value!.Id,
                отправитель.Value!.Id));
            Assert.True(первое.IsSuccess, $"создание письма: {первое.Error}");

            try
            {
                var исход = await шлюз.ВыдатьНомерПисьмуAsync(первое.Value!);
                Assert.True(исход.IsSuccess, $"номер: {исход.Error}");
                Assert.Fail("ожидалась потеря ответа");
            }
            catch (SharedOutcomeUnknownException)
            {
                // Ожидаемое поведение: ответ потерян.
            }

            Assert.Single(шлюз.НезавершённыеОперации);

            Console.WriteLine("ЖУРНАЛ_СТЕНДА: " + string.Join(" || ", стенд.ЗаписиЖурнала.TakeLast(5)));

            // Повтор с прежним идентификатором возвращает прежний результат:
            // номер не выдаётся второй раз.
            await шлюз.ПовторитьНезавершённуюAsync(шлюз.НезавершённыеОперации[0].Идентификатор);
            Assert.Empty(шлюз.НезавершённыеОперации);

            var сНомером = await шлюз.ПолучитьПисьмоAsync(первое.Value!.Id);
            Assert.False(string.IsNullOrWhiteSpace(сНомером!.OutgoingNumber));

            // Следующее отправление получает номер, соседний с первым:
            // второго номера из-за повтора не выдавалось.
            var второе = await шлюз.СоздатьПисьмоAsync(Отправление(
                получатель.Value!.Id,
                отправитель.Value!.Id));
            Assert.True(второе.IsSuccess);

            var выданный = await шлюз.ВыдатьНомерПисьмуAsync(второе.Value!);
            Assert.True(выданный.IsSuccess);

            Assert.Equal(
                СчётчикНомера(сНомером.OutgoingNumber!) + 1,
                СчётчикНомера(выданный.Value!.OutgoingNumber!));
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public async Task Повтор_Формирования_Рассылки_Создаёт_Одну_Рассылку()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();
        var каталог = КаталогПроверки();

        try
        {
            using var транспорт = await ТранспортСПотерей(стенд, "ПК оператора 1", "commands/distributions.create-from-selection");
            var незавершённые = Хранилище(каталог);
            var шлюз = new RemoteDataGateway(транспорт, незавершённые);

            var отправитель = await шлюз.СоздатьОтправителяAsync(
                new AgentDto { Name = "ООО «Отправитель»" });
            Assert.True(отправитель.IsSuccess);

            var получатель = await шлюз.СохранитьКонтрагентаAsync(Домен("ООО «Получатель»"));
            Assert.True(получатель.IsSuccess);

            var ошибка = await Assert.ThrowsAsync<SharedOutcomeUnknownException>(
                () => шлюз.СоздатьРассылкуИзВыборкиAsync(
                    new[] { получатель.Value!.Id },
                    отправитель.Value!.Id,
                    "Рассылка после сбоя"));
            Assert.Single(шлюз.НезавершённыеОперации);

            await шлюз.ПовторитьНезавершённуюAsync(шлюз.НезавершённыеОперации[0].Идентификатор);
            Assert.Empty(шлюз.НезавершённыеОперации);

            var рассылки = await шлюз.ПолучитьРассылкиAsync();

            Assert.Single(рассылки);
            Assert.Equal("Рассылка после сбоя", рассылки[0].Name);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public async Task Повтор_Под_Тем_Же_Идентификатором_С_Другим_Телом_Отклоняется()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var реквизиты = Реквизиты(стенд, клиент);
        using var транспорт = new SharedTransport(реквизиты, клиент.Секрет);

        var идентификатор = Guid.NewGuid().ToString("D");

        var тело1 = JsonSerializer.Serialize(
            new SaveCounterpartyRequest(SharedServiceIntegrationTests.Контрагент("ООО «Первое»"), 0),
            SharedJson.Options);

        await транспорт.ВыполнитьКомандуJsonAsync<SaveCounterpartyResponse>(
            "counterparties.save",
            тело1,
            идентификатор);

        var тело2 = JsonSerializer.Serialize(
            new SaveCounterpartyRequest(SharedServiceIntegrationTests.Контрагент("ООО «Другое»"), 0),
            SharedJson.Options);

        var ошибка = await Assert.ThrowsAsync<SharedModeException>(
            () => транспорт.ВыполнитьКомандуJsonAsync<SaveCounterpartyResponse>(
                "counterparties.save",
                тело2,
                идентификатор));

        Assert.Equal(SharedErrorCodes.IdempotencyMismatch, ошибка.Код);

        // Иная команда под старым идентификатором тоже отклоняется.
        var тело3 = JsonSerializer.Serialize(
            new SeedDemoRequest(),
            SharedJson.Options);

        ошибка = await Assert.ThrowsAsync<SharedModeException>(
            () => транспорт.ВыполнитьКомандуJsonAsync<SeedDemoResponse>(
                "database.seed-demo",
                тело3,
                идентификатор));

        Assert.Equal(SharedErrorCodes.IdempotencyMismatch, ошибка.Код);
    }

    [Fact]
    public async Task Параллельные_Повторы_Создают_Одну_Запись()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();
        var каталог = КаталогПроверки();

        try
        {
            using var транспорт = await ТранспортСПотерей(стенд, "ПК оператора 1", "commands/counterparties.save");
            var незавершённые = Хранилище(каталог);
            var шлюз = new RemoteDataGateway(транспорт, незавершённые);

            await Assert.ThrowsAsync<SharedOutcomeUnknownException>(
                () => шлюз.СохранитьКонтрагентаAsync(Домен("ООО «Гонка»")));

            var идентификатор = шлюз.НезавершённыеОперации[0].Идентификатор;

            // Два одновременных повтора с одним идентификатором: оба получают
            // прежний результат зафиксированной операции, запись одна.
            await Task.WhenAll(
                шлюз.ПовторитьНезавершённуюAsync(идентификатор),
                шлюз.ПовторитьНезавершённуюAsync(идентификатор));

            Assert.Empty(шлюз.НезавершённыеОперации);
            Assert.Single((await шлюз.ПолучитьКонтрагентовAsync(new PageRequest(0, 50))).Items);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public async Task Недоступная_История_Блокирует_Мутации_Повтор_Восстанавливает()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();
        var каталог = КаталогПроверки();

        try
        {
            using var транспорт = await ТранспортСПотерей(стенд, "ПК оператора 1", "commands/counterparties.save");
            var незавершённые = Хранилище(каталог);
            var шлюз = new RemoteDataGateway(транспорт, незавершённые);
            var файлИстории = Path.Combine(каталог, "pending-operations.json");

            // Сервер зафиксировал команду, ответ потерян: запись в истории.
            await Assert.ThrowsAsync<SharedOutcomeUnknownException>(
                () => шлюз.СохранитьКонтрагентаAsync(Домен("ООО «Недоступная история»")));

            var идентификатор = шлюз.НезавершённыеОперации[0].Идентификатор;
            Assert.True(File.Exists(файлИстории));

            // Файл истории временно недоступен: новая мутация отклоняется
            // ДО отправки — она получила бы новый идентификатор.
            using (var держатель = File.Open(файлИстории, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var ошибка = await Assert.ThrowsAsync<SharedModeException>(
                    () => шлюз.СохранитьКонтрагентаAsync(Домен("ООО «Вторая запись»")));

                Assert.Equal(SharedErrorCodes.PendingHistoryUnavailable, ошибка.Код);

                // Сервер не получил вторую команду: запись одна.
                Assert.Single((await шлюз.ПолучитьКонтрагентовAsync(new PageRequest(0, 50))).Items);
            }

            // Файл вновь доступен: повтор с прежним идентификатором возвращает
            // один результат, запись остаётся одна.
            await шлюз.ПовторитьНезавершённуюAsync(идентификатор);

            Assert.Empty(шлюз.НезавершённыеОперации);

            var страница = await шлюз.ПолучитьКонтрагентовAsync(new PageRequest(0, 50));

            Assert.Single(страница.Items);
            Assert.Equal("ООО «Недоступная история»", страница.Items[0].Name);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public async Task Окно_Выполнения_На_Сервере_Сохраняет_Незавершённую_Операцию()    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();
        var каталог = КаталогПроверки();

        try
        {
            using var транспорт = await Транспорт(стенд, "ПК оператора 1");
            var незавершённые = Хранилище(каталог);
            var шлюз = new RemoteDataGateway(транспорт, незавершённые);

            var тело = JsonSerializer.Serialize(
                new SaveCounterpartyRequest(SharedServiceIntegrationTests.Контрагент("ООО «Долгая команда»"), 0),
                SharedJson.Options);

            // Операция зарегистрирована клиентом, ответ на первую попытку
            // потерян; на сервере команда ещё выполняется (медленная
            // команда, окно до завершения первой попытки).
            var идентификатор = незавершённые.Начать("counterparties.save", тело);

            ЗарегистрироватьВыполняющуюся(стенд, идентификатор, "counterparties.save", тело);

            var ошибка = await Assert.ThrowsAsync<SharedModeException>(
                () => шлюз.ПовторитьНезавершённуюAsync(идентификатор));

            // Сервер различает «ещё выполняется» и «другое содержимое»:
            // клиент обязан сохранить запись для повтора позже.
            Assert.Equal(SharedErrorCodes.OperationInProgress, ошибка.Код);
            Assert.Single(шлюз.НезавершённыеОперации);
            Assert.Equal(идентификатор, шлюз.НезавершённыеОперации[0].Идентификатор);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    /// <summary>
    /// Регистрирует на стенде запись «операция ещё выполняется»: тот же
    /// идентификатор и тело, что у незавершённой операции клиента.
    /// </summary>
    /// <param name="стенд">Стенд службы.</param>
    /// <param name="идентификатор">Идентификатор операции.</param>
    /// <param name="команда">Имя команды.</param>
    /// <param name="тело">Тело запроса в виде JSON.</param>
    private static void ЗарегистрироватьВыполняющуюся(
        SharedServiceTestHost стенд,
        string идентификатор,
        string команда,
        string тело)
    {
        var строкаПодключения = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(стенд.КаталогДанных, "picket302.db"),
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
            Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Default,
        }.ToString();

        using var соединение = new Microsoft.Data.Sqlite.SqliteConnection(строкаПодключения);
        соединение.Open();

        using var вставка = соединение.CreateCommand();
        вставка.CommandText =
            """
            INSERT INTO shared_operations
                (operation_id, device_id, command_name, request_hash, status, created_at)
            VALUES (@Id, @Device, @Command, @Hash, 'in-progress', @Now);
            """;
        вставка.Parameters.AddWithValue("@Id", идентификатор);
        вставка.Parameters.AddWithValue("@Device", "ПК оператора 1");
        вставка.Parameters.AddWithValue("@Command", команда);
        вставка.Parameters.AddWithValue(
            "@Hash",
            Picket302.Service.Data.SharedOperationStore.ВычислитьОтпечаток(тело));
        вставка.Parameters.AddWithValue(
            "@Now",
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));

        вставка.ExecuteNonQuery();
    }

    [Fact]
    public async Task Отмена_Повтора_Сохраняет_Незавершённую_Операцию()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();
        var каталог = КаталогПроверки();

        try
        {
            using var транспорт = await ТранспортСПотерей(стенд, "ПК оператора 1", "commands/counterparties.save");
            var незавершённые = Хранилище(каталог);
            var шлюз = new RemoteDataGateway(транспорт, незавершённые);

            await Assert.ThrowsAsync<SharedOutcomeUnknownException>(
                () => шлюз.СохранитьКонтрагентаAsync(Домен("ООО «Отмена»")));

            var идентификатор = шлюз.НезавершённыеОперации[0].Идентификатор;

            using var отменённый = new CancellationTokenSource();
            отменённый.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => шлюз.ПовторитьНезавершённуюAsync(идентификатор, отменённый.Token));

            // Отмена не теряет операцию: исход по-прежнему неизвестен.
            Assert.Single(шлюз.НезавершённыеОперации);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public async Task Незавершённая_Операция_Переживает_Перезапуск_Клиента()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();
        var каталог = КаталогПроверки();

        try
        {
            using var транспорт1 = await ТранспортСПотерей(стенд, "ПК оператора 1", "commands/counterparties.save");
            var файл = Path.Combine(каталог, "pending-operations.json");
            var незавершённые1 = new PendingOperationsStore(файл);
            var шлюз1 = new RemoteDataGateway(транспорт1, незавершённые1);

            await Assert.ThrowsAsync<SharedOutcomeUnknownException>(
                () => шлюз1.СохранитьКонтрагентаAsync(Домен("ООО «Перезапуск»")));

            транспорт1.Dispose();

            // Перезапуск клиента: новое хранилище над тем же файлом и новый
            // транспорт без потери ответа.
            using var транспорт2 = await Транспорт(стенд, "ПК оператора 1");
            var незавершённые2 = new PendingOperationsStore(файл);
            var шлюз2 = new RemoteDataGateway(транспорт2, незавершённые2);

            var запись = Assert.Single(незавершённые2.Незавершённые);

            await шлюз2.ПовторитьНезавершённуюAsync(запись.Идентификатор);

            Assert.Empty(незавершённые2.Незавершённые);
            Assert.Single((await шлюз2.ПолучитьКонтрагентовAsync(new PageRequest(0, 50))).Items);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public void Просроченные_Сведения_О_Незавершённой_Операции_Удаляются()
    {
        var каталог = КаталогПроверки();

        try
        {
            var файл = Path.Combine(каталог, "pending-operations.json");
            var момент = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            var незавершённые1 = new PendingOperationsStore(файл, часы: () => момент);

            незавершённые1.Начать("counterparties.save", "{}");

            // Неделя прошла: срок хранения истёк, исход остался неизвестным.
            var незавершённые2 = new PendingOperationsStore(
                файл,
                часы: () => момент.AddDays(7).AddMinutes(1));

            Assert.Empty(незавершённые2.Незавершённые);
        }
        finally
        {
            УдалитьКаталог(каталог);
        }
    }

    [Fact]
    public async Task Недоставленный_Запрос_Сообщает_Что_Данные_Не_Изменялись()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        // Порт без слушателя: TCP-соединение не установится, HTTP-запрос не
        // будет отправлен.
        using var слушатель = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        слушатель.Start();

        var мёртвыйПорт = ((System.Net.IPEndPoint)слушатель.LocalEndpoint).Port;
        слушатель.Stop();

        using var клиент = await стенд.СоздатьКлиентаAsync("ПК оператора 1");

        var реквизиты = new SharedClientSettings(
            "127.0.0.1",
            мёртвыйПорт,
            клиент.Устройство,
            string.Empty,
            SharedFingerprint.ВычислитьОтпечаток(стенд.Сертификат));

        using var транспорт = new SharedTransport(реквизиты, клиент.Секрет);

        var ошибка = await Assert.ThrowsAsync<SharedModeException>(
            () => транспорт.ВыполнитьКомандуJsonAsync<SaveCounterpartyResponse>(
                "counterparties.save",
                "{}",
                Guid.NewGuid().ToString("D")));

        // Соединение не установлено: утверждение «данные не изменялись»
        // здесь обосновано.
        Assert.Equal("network-unreachable", ошибка.Код);
        Assert.NotEqual(SharedErrorCodes.OutcomeUnknown, ошибка.Код);
        Assert.Contains("не был доставлен", ошибка.Message);
    }

    [Fact]
    public async Task Сообщение_Неизвестного_Исхода_Не_Утверждает_Что_Данные_Неизменны()
    {
        await using var стенд = await SharedServiceTestHost.ПоднятьAsync();

        using var транспорт = await ТранспортСПотерей(стенд, "ПК оператора 1", "commands/counterparties.save");

        var ошибка = await Assert.ThrowsAsync<SharedOutcomeUnknownException>(
            () => транспорт.ВыполнитьКомандуJsonAsync<SaveCounterpartyResponse>(
                "counterparties.save",
                JsonSerializer.Serialize(
                    new SaveCounterpartyRequest(SharedServiceIntegrationTests.Контрагент("ООО «Честность»"), 0),
                    SharedJson.Options),
                Guid.NewGuid().ToString("D")));

        Assert.DoesNotContain("не изменялись", ошибка.Message);
        Assert.Equal(SharedErrorCodes.OutcomeUnknown, ошибка.Код);
    }

    /// <summary>
    /// Доменная карточка контрагента с почтовым адресом.
    /// </summary>
    /// <param name="наименование">Наименование.</param>
    /// <returns>Готовый контрагент.</returns>
    private static Counterparty Домен(string наименование) => new()
    {
        Type = CounterpartyType.LegalEntity,
        Name = наименование,
        IsActive = true,
        PostalAddress = Address.Create(
            "101000",
            new[] { наименование, "ул. Тверская", "д. 1", string.Empty, "Москва", "Московская обл." }).Value,
    };

    /// <summary>
    /// Доменное отправление.
    /// </summary>
    /// <param name="получатель">Идентификатор получателя.</param>
    /// <param name="отправитель">Идентификатор отправителя.</param>
    /// <returns>Готовое отправление.</returns>
    private static Mail Отправление(long получатель, long отправитель) => new()
    {
        CounterpartyId = получатель,
        SenderId = отправитель,
        EnvelopeType = "C6",
        CopyCount = Mail.MinimumCopyCount,
        Status = MailStatus.Draft,
        DateCreated = DateTime.UtcNow,
        RecipientAddress = Address.Create(
            "101000",
            new[] { "ООО «Получатель»", "ул. Тверская", "д. 1", string.Empty, "Москва", "Московская обл." }).Value,
    };

    /// <summary>
    /// Транспорт клиента, сопряжённого со стендом.
    /// </summary>
    /// <param name="стенд">Стенд службы.</param>
    /// <param name="имя">Имя устройства (уже сопряжённого).</param>
    /// <returns>Готовый транспорт.</returns>
    private static async Task<SharedTransport> Транспорт(
        SharedServiceTestHost стенд,
        string имя)
    {
        using var клиент = await стенд.СоздатьКлиентаAsync(имя);

        return new SharedTransport(Реквизиты(стенд, клиент), клиент.Секрет);
    }

    /// <summary>
    /// Транспорт с управляемой потерей первого ответа на указанный маршрут
    /// команды.
    /// </summary>
    /// <param name="стенд">Стенд службы.</param>
    /// <param name="имя">Имя устройства.</param>
    /// <param name="маршрут">Фрагмент маршрута команды.</param>
    /// <returns>Готовый транспорт.</returns>
    private static async Task<SharedTransport> ТранспортСПотерей(
        SharedServiceTestHost стенд,
        string имя,
        string маршрут)
    {
        using var клиент = await стенд.СоздатьКлиентаAsync(имя);

        return new SharedTransport(
            Реквизиты(стенд, клиент),
            клиент.Секрет,
            new ПотеряОтветаОбработчик(
                SharedTransport.СоздатьОбработчикОтпечатка(
                    SharedFingerprint.ВычислитьОтпечаток(стенд.Сертификат)),
                маршрут));
    }

    /// <summary>Реквизиты подключения для сопряжённого клиента.</summary>
    /// <param name="стенд">Стенд службы.</param>
    /// <param name="клиент">Сопряжённый клиент.</param>
    /// <returns>Реквизиты.</returns>
    private static SharedClientSettings Реквизиты(
        SharedServiceTestHost стенд,
        SharedTestClient клиент)
        => new(
            "127.0.0.1",
            стенд.Порт,
            клиент.Устройство,
            string.Empty,
            SharedFingerprint.ВычислитьОтпечаток(стенд.Сертификат));

    /// <summary>Временный каталог проверки.</summary>
    /// <returns>Путь каталога.</returns>
    private static string КаталогПроверки()
    {
        var каталог = Path.Combine(
            Path.GetTempPath(),
            "Picket302Retry_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(каталог);

        return каталог;
    }

    /// <summary>Хранилище незавершённых операций во временном каталоге.</summary>
    /// <param name="каталог">Каталог.</param>
    /// <returns>Хранилище.</returns>
    private static PendingOperationsStore Хранилище(string каталог)
        => new(Path.Combine(каталог, "pending-operations.json"));

    /// <summary>Удаляет временный каталог, терпя оставшиеся файлы.</summary>
    /// <param name="каталог">Каталог.</param>
    private static void УдалитьКаталог(string каталог)
    {
        try
        {
            Directory.Delete(каталог, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог не блокирует работу проверок.
        }
    }

    /// <summary>
    /// Счётчик регистрационного номера: номер строится по маске
    /// «ИСХ-{YYYY}/{SEQ:5}», счётчик — сегмент после слэша.
    /// </summary>
    /// <param name="номер">Регистрационный номер.</param>
    /// <returns>Числовой счётчик.</returns>
    private static int СчётчикНомера(string номер)
    {
        var сегмент = номер[(номер.LastIndexOf('/') + 1)..];

        return int.Parse(
            сегмент,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Обработчик, теряющий первый ответ на указанные маршруты: сервер уже
    /// выполнил команду, ответ до клиента не дошёл.
    /// </summary>
    private sealed class ПотеряОтветаОбработчик : System.Net.Http.DelegatingHandler
    {
        private readonly string[] _маршруты;
        private readonly HashSet<string> _потерянные = new(StringComparer.Ordinal);

        /// <summary>
        /// Создаёт обработчик над штатным обработчиком с закреплением
        /// отпечатка.
        /// </summary>
        /// <param name="внутренний">Штатный обработчик.</param>
        /// <param name="маршруты">Фрагменты маршрутов, чьи первые ответы теряются.</param>
        public ПотеряОтветаОбработчик(HttpMessageHandler внутренний, params string[] маршруты)
            : base(внутренний)
        {
            _маршруты = маршруты;
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage запрос,
            CancellationToken токен)
        {
            var ответ = await base.SendAsync(запрос, токен).ConfigureAwait(false);

            var путь = запрос.RequestUri?.PathAndQuery ?? string.Empty;

            if (_маршруты.Any(маршрут => путь.Contains(маршрут, StringComparison.Ordinal))
                && _потерянные.Add(путь))
            {
                ответ.Dispose();

                throw new HttpRequestException(
                    "Соединение оборвано после отправки запроса",
                    inner: new IOException("Ответ не получен"));
            }

            return ответ;
        }
    }
}
