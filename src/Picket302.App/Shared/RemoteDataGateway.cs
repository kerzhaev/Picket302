using System.Text.Json;
using Picket302.Application.Services;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Infrastructure.Licensing;
using Picket302.Printing;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.App.Shared;

/// <summary>
/// Доступ к общей базе через типизированный API серверной службы.
/// </summary>
/// <remarks>
/// <para>
/// Каждый составной сценарий выполняется <b>одной</b> серверной командой:
/// служба исполняет те же доменные правила, что и одиночный режим, в одной
/// короткой транзакции. Клиент не открывает файл базы и не выполняет
/// составные действия по частям.
/// </para>
/// <para>
/// Недоступность службы не подменяется местной базой: ошибка доходит до
/// оператора, а данные остаются в общей баче нетронутыми.
/// </para>
/// </remarks>
public sealed class RemoteDataGateway : IDataGateway
{
    private readonly SharedTransport _транспорт;
    private readonly PendingOperationsStore _незавершённые;

    /// <summary>
    /// Создаёт доступ к общей базе.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    /// <param name="незавершённые">
    /// Хранилище незавершённых операций: каждая мутация регистрируется с
    /// точным телом запроса; при потере ответа повтор выполняется с прежним
    /// идентификатором, и дубль не возникает.
    /// </param>
    public RemoteDataGateway(SharedTransport транспорт, PendingOperationsStore незавершённые)
    {
        ArgumentNullException.ThrowIfNull(транспорт);
        ArgumentNullException.ThrowIfNull(незавершённые);

        _транспорт = транспорт;
        _незавершённые = незавершённые;
    }

    /// <inheritdoc />
    public bool ОбщийРежим => true;

    /// <inheritdoc />
    public IReadOnlyList<PendingOperationsStore.НезавершённаяОперация> НезавершённыеОперации
        => _незавершённые.Незавершённые;

    /// <inheritdoc />
    public async Task<Page<Counterparty>> ПолучитьКонтрагентовAsync(
        PageRequest запрос,
        CounterpartyFilter? фильтр = null,
        CancellationToken токен = default)
    {
        var страница = await _транспорт
            .ВыполнитьЗапросAsync<PageDto<CounterpartyDto>>(
                "/api/v1/queries/counterparties?offset=" + запрос.Offset
                + "&limit=" + запрос.Limit
                + "&filter=" + Uri.EscapeDataString(JsonSerializer.Serialize(
                    фильтр is null
                        ? new CounterpartyFilterDto()
                        : SharedMapping.КФильтруКонтрагентовDto(фильтр),
                    SharedJson.Options)),
                токен)
            .ConfigureAwait(false);

        return new Page<Counterparty>(
            страница.Items.Select(ИзКонтрагента).ToArray(),
            страница.Offset,
            страница.Limit,
            страница.TotalCount);
    }

    /// <inheritdoc />
    public async Task<Counterparty?> ПолучитьКонтрагентаAsync(long идентификатор, CancellationToken токен = default)
    {
        var dto = await _транспорт
            .ВыполнитьЗапросAsync<CounterpartyDto?>(
                "/api/v1/queries/counterparties/" + идентификатор,
                токен)
            .ConfigureAwait(false);

        return dto is null ? null : ИзКонтрагента(dto);
    }

    /// <inheritdoc />
    public Task<Result<Counterparty>> УдалитьКонтрагентаAsync(
        Counterparty контрагент,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            await ВыполнитьОперациюAsync<ChangeCounterpartyStateRequest, VersionedResponse>(
                "counterparties.change-state",
                new ChangeCounterpartyStateRequest(
                    контрагент.Id,
                    контрагент.Version,
                    false),
                токен)
            .ConfigureAwait(false);

            контрагент.IsDeleted = true;

            return контрагент;
        });

    /// <inheritdoc />
    public Task<Result<Counterparty>> СохранитьКонтрагентаAsync(
        Counterparty контрагент,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var dto = await ВыполнитьОперациюAsync<SaveCounterpartyRequest, SaveCounterpartyResponse>(
                "counterparties.save",
                new SaveCounterpartyRequest(
                    SharedMapping.ККонтрагенту(контрагент, контрагент.Version),
                    контрагент.Version),
                токен)
            .ConfigureAwait(false);

            // Идентификатор назначает сервер: без него последующие операции
            // над записью (письмо, рассылка) ссылались бы на несуществующего
            // контрагента.
            контрагент.Id = dto.Id;
            контрагент.Version = dto.Version;

            return контрагент;
        });

    /// <inheritdoc />
    public Task<bool> ОкончательноУдалитьКонтрагентаAsync(
        long идентификатор,
        CancellationToken токен = default)
    {
        // Окончательное удаление в общем режиме не выполняется: запись может
        // быть нужна другому рабочему месту, а общее действие доступно
        // администратору через очистку базы.
        throw new SharedModeException(
            SharedErrorCodes.ValidationFailed,
            "Окончательное удаление записи в общем режиме недоступно. "
            + "Используйте очистку базы или обратитесь к администратору.");
    }

    /// <inheritdoc />
    public Task<Result<int>> ИзменитьСостояниеКонтрагентовAsync(
        IReadOnlyList<Counterparty> контрагенты,
        bool восстановить,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<BatchChangeCounterpartyStateRequest, BatchOperationResponse>(
                    "counterparties.batch-change-state",
                    new BatchChangeCounterpartyStateRequest(
                        контрагенты.Select(з => new VersionedId(з.Id, з.Version)).ToArray(),
                        восстановить), токен)
                .ConfigureAwait(false);

            return ответ.Count;
        });

    /// <inheritdoc />
    public Task<Result<int>> ИзменитьПолеКонтрагентовAsync(
        IReadOnlyList<Counterparty> контрагенты,
        string свойство,
        string? значение,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<BatchUpdateCounterpartyFieldRequest, BatchOperationResponse>(
                    "counterparties.batch-update-field",
                    new BatchUpdateCounterpartyFieldRequest(
                        контрагенты.Select(з => new VersionedId(з.Id, з.Version)).ToArray(),
                        свойство,
                        значение), токен)
                .ConfigureAwait(false);

            return ответ.Count;
        });

    /// <inheritdoc />
    public async Task<IReadOnlyList<Counterparty>> ПолучитьОтправителейAsync(CancellationToken токен = default)
    {
        var отправители = await _транспорт
            .ВыполнитьЗапросAsync<CounterpartyDto[]>("/api/v1/queries/senders", токен)
            .ConfigureAwait(false);

        return отправители.Select(ИзКонтрагента).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Counterparty>> ПолучитьКонтрагентовГруппыAsync(
        long идентификаторГруппы,
        CancellationToken токен = default)
    {
        var контрагенты = await _транспорт
            .ВыполнитьЗапросAsync<CounterpartyDto[]>(
                "/api/v1/queries/counterparties/by-group/" + идентификаторГруппы,
                токен)
            .ConfigureAwait(false);

        return контрагенты.Select(ИзКонтрагента).ToArray();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ПолучитьЗначенияПоляAsync(
        string поле,
        CancellationToken токен = default)
        => _транспорт.ВыполнитьЗапросAsync<IReadOnlyList<string>>(
            "/api/v1/queries/distinct-values?field=" + Uri.EscapeDataString(поле),
            токен);

    /// <inheritdoc />
    public async Task<Counterparty?> НайтиПоКлючуСлиянияAsync(
        string? инн,
        string наименование,
        string? индекс,
        CancellationToken токен = default)
    {
        var dto = await _транспорт
            .ВыполнитьЗапросAsync<CounterpartyDto?>(
                "/api/v1/queries/merge-key?inn=" + Uri.EscapeDataString(инн ?? string.Empty)
                + "&name=" + Uri.EscapeDataString(наименование)
                + "&zip=" + Uri.EscapeDataString(индекс ?? string.Empty),
                токен)
            .ConfigureAwait(false);

        return dto is null || dto.Id <= 0 ? null : ИзКонтрагента(dto);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CounterpartyField>> ПолучитьПоляКонтрагентаAsync(
        long идентификаторКонтрагента,
        CancellationToken токен = default)
    {
        var поля = await _транспорт
            .ВыполнитьЗапросAsync<CounterpartyFieldDto[]>(
                "/api/v1/queries/custom-fields?counterpartyId=" + идентификаторКонтрагента,
                токен)
            .ConfigureAwait(false);

        return поля
            .Select(поле => new CounterpartyField
            {
                Id = поле.Id,
                CounterpartyId = поле.CounterpartyId,
                FieldName = поле.FieldName,
                FieldValue = поле.FieldValue,
                FieldType = поле.FieldType,
                UpdatedAt = поле.UpdatedAt,
                Version = поле.Version,
            })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ПолучитьЗначенияПолейКонтрагентаAsync(
        long идентификаторКонтрагента,
        CancellationToken токен = default)
    {
        var поля = await ПолучитьПоляКонтрагентаAsync(идентификаторКонтрагента, токен)
            .ConfigureAwait(false);

        var значения = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var поле in поля)
        {
            if (!string.IsNullOrEmpty(поле.FieldValue))
            {
                значения[поле.FieldName] = поле.FieldValue!;
            }
        }

        return значения;
    }

    /// <inheritdoc />
    public Task<Result<CounterpartyField>> ЗаписатьПолеКонтрагентаAsync(
        long идентификаторКонтрагента,
        string поле,
        string значение,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var существующее = (await ПолучитьПоляКонтрагентаAsync(идентификаторКонтрагента, токен)
                    .ConfigureAwait(false))
                .FirstOrDefault(запись => запись.FieldName == поле);

            var ответ = await ВыполнитьОперациюAsync<SetCounterpartyFieldRequest, CounterpartyFieldResponse>(
                    "counterparties.set-field",
                    new SetCounterpartyFieldRequest(
                        идентификаторКонтрагента,
                        поле,
                        значение,
                        существующее?.Version ?? 0), токен)
                .ConfigureAwait(false);

            return new CounterpartyField
            {
                Id = ответ.Id,
                CounterpartyId = ответ.CounterpartyId,
                FieldName = ответ.FieldName,
                FieldValue = ответ.FieldValue,
                FieldType = ответ.FieldType,
                UpdatedAt = ответ.UpdatedAt,
                Version = ответ.Version,
            };
        });

    /// <inheritdoc />
    public Task<Result<bool>> УдалитьПолеКонтрагентаAsync(
        long идентификаторКонтрагента,
        string поле,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var существующее = (await ПолучитьПоляКонтрагентаAsync(идентификаторКонтрагента, токен)
                    .ConfigureAwait(false))
                .FirstOrDefault(запись => запись.FieldName == поле);

            if (существующее is null)
            {
                return false;
            }

            await ВыполнитьОперациюAsync<RemoveCounterpartyFieldRequest, VersionedResponse>(
                    "counterparties.remove-field",
                    new RemoveCounterpartyFieldRequest(
                        идентификаторКонтрагента,
                        поле,
                        существующее.Version), токен)
                .ConfigureAwait(false);

            return true;
        });

    /// <inheritdoc />
    public Task<Result<Counterparty>> СоздатьОтправителяAsync(AgentDto агент, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<CreateAgentRequest, CounterpartyResponse>(
                    "counterparties.create-agent",
                    new CreateAgentRequest(
                        агент.Name,
                        агент.Inn,
                        агент.Kpp,
                        агент.BankName,
                        агент.Zip), токен)
                .ConfigureAwait(false);

            return new Counterparty
            {
                Id = ответ.Id,
                Type = ответ.Type,
                Name = ответ.Name,
                ShortName = ответ.ShortName,
                Inn = ответ.Inn,
                Kpp = ответ.Kpp,
                IsSender = ответ.IsSender,
                IsActive = ответ.IsActive,
                CreatedAt = ответ.CreatedAt,
                UpdatedAt = ответ.UpdatedAt,
                Version = ответ.Version,
            };
        });

    /// <inheritdoc />
    public async Task<IReadOnlyList<Group>> ПолучитьГруппыAsync(CancellationToken токен = default)
    {
        var группы = await _транспорт
            .ВыполнитьЗапросAsync<GroupDto[]>("/api/v1/queries/groups", токен)
            .ConfigureAwait(false);

        return группы
            .Select(группа => new Group
            {
                Id = группа.Id,
                Name = группа.Name,
                Note = группа.Note,
                IsDefault = группа.IsDefault,
                IsClosed = группа.IsClosed,
                ParentId = группа.ParentId,
                SortOrder = группа.SortOrder,
                Version = группа.Version,
            })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Category>> ПолучитьДеревоКатегорийAsync(CancellationToken токен = default)
    {
        var дерево = await _транспорт
            .ВыполнитьЗапросAsync<CategoryDto[]>("/api/v1/queries/category-tree", токен)
            .ConfigureAwait(false);

        return дерево
            .Select(категория => new Category(
                категория.Id,
                категория.Name,
                категория.ParentId,
                категория.IsDefault,
                категория.IsClosed,
                категория.SortOrder,
                категория.CounterpartyCount))
            .ToArray();
    }

    /// <inheritdoc />
    public Task<Result<Category>> СоздатьКатегориюAsync(
        string наименование,
        long? родитель = null,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<CreateCategoryRequest, GroupDto>(
                    "groups.create-category",
                    new CreateCategoryRequest(наименование, родитель), токен)
                .ConfigureAwait(false);

            return new Category(
                ответ.Id,
                ответ.Name,
                ответ.ParentId,
                ответ.IsDefault,
                ответ.IsClosed,
                ответ.SortOrder,
                0);
        });

    /// <inheritdoc />
    public Task<Result<Category>> ПереименоватьКатегориюAsync(
        Category категория,
        string наименование,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var версия = await ТекущаяВерсияГруппыAsync(категория.Id, токен).ConfigureAwait(false);

            var ответ = await ВыполнитьОперациюAsync<RenameCategoryRequest, GroupDto>(
                    "groups.rename-category",
                    new RenameCategoryRequest(категория.Id, наименование, версия), токен)
                .ConfigureAwait(false);

            return new Category(
                ответ.Id,
                ответ.Name,
                ответ.ParentId,
                ответ.IsDefault,
                ответ.IsClosed,
                ответ.SortOrder,
                0);
        });

    /// <inheritdoc />
    public Task<Result<int>> УдалитьКатегориюAsync(Category категория, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var версия = await ТекущаяВерсияГруппыAsync(категория.Id, токен).ConfigureAwait(false);

            await ВыполнитьОперациюAsync<DeleteCategoryRequest, VersionedResponse>(
                    "groups.delete-category",
                    new DeleteCategoryRequest(категория.Id, версия), токен)
                .ConfigureAwait(false);

            return 1;
        });

    /// <inheritdoc />
    public Task<Result<Counterparty>> ПеренестиКонтрагентаAsync(
        long идентификаторКонтрагента,
        long идентификаторКатегории,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var контрагент = await ПолучитьКонтрагентаAsync(идентификаторКонтрагента, токен)
                .ConfigureAwait(false);

            if (контрагент is null)
            {
                throw new SharedModeException(
                    SharedErrorCodes.NotFound,
                    "Контрагент не найден.");
            }

            await ВыполнитьОперациюAsync<MoveCounterpartyRequest, VersionedResponse>(
                    "groups.move-counterparty",
                    new MoveCounterpartyRequest(
                        идентификаторКонтрагента,
                        идентификаторКатегории,
                        контрагент.Version), токен)
                .ConfigureAwait(false);

            контрагент.GroupId = идентификаторКатегории;

            return контрагент;
        });

    /// <inheritdoc />
    public async Task<IReadOnlyList<Folder>> ПолучитьПапкиAsync(CancellationToken токен = default)
    {
        var папки = await _транспорт
            .ВыполнитьЗапросAsync<FolderDto[]>("/api/v1/queries/folders", токен)
            .ConfigureAwait(false);

        return папки.Select(ИзПапки).ToArray();
    }

    /// <inheritdoc />
    public Task<Result<Folder>> СоздатьПапкуAsync(
        string наименование,
        long? родитель = null,
        string? примечание = null,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<CreateFolderRequest, FolderDto>(
                    "folders.create",
                    new CreateFolderRequest(наименование, родитель, примечание), токен)
                .ConfigureAwait(false);

            return ИзПапки(ответ);
        });

    /// <inheritdoc />
    public Task<Result<Folder>> ПереименоватьПапкуAsync(
        Folder папка,
        string наименование,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<RenameFolderRequest, FolderDto>(
                    "folders.rename",
                    new RenameFolderRequest(папка.Id, наименование, папка.Version), токен)
                .ConfigureAwait(false);

            return ИзПапки(ответ);
        });

    /// <inheritdoc />
    public Task<Result<Folder>> ПеренестиПапкуAsync(
        Folder папка,
        long? родитель,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<MoveFolderRequest, FolderDto>(
                    "folders.move",
                    new MoveFolderRequest(папка.Id, родитель, папка.Version), токен)
                .ConfigureAwait(false);

            return ИзПапки(ответ);
        });

    /// <inheritdoc />
    public Task<Result<bool>> УдалитьПапкуAsync(Folder папка, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            await ВыполнитьОперациюAsync<DeleteFolderRequest, VersionedResponse>(
                    "folders.delete",
                    new DeleteFolderRequest(папка.Id, папка.Version), токен)
                .ConfigureAwait(false);

            return true;
        });

    /// <inheritdoc />
    public async Task<int> ПолучитьЧислоПисемВПапкеAsync(
        long идентификаторПапки,
        CancellationToken токен = default)
    {
        var ответ = await _транспорт
            .ВыполнитьЗапросAsync<FolderMailCountResponse>(
                "/api/v1/queries/folder-mail-counts?folderId=" + идентификаторПапки,
                токен)
            .ConfigureAwait(false);

        return ответ.Count;
    }

    /// <inheritdoc />
    public async Task<Page<Mail>> ПолучитьПисьмаAsync(
        PageRequest запрос,
        MailFilter? фильтр = null,
        CancellationToken токен = default)
    {
        var страница = await _транспорт
            .ВыполнитьЗапросAsync<PageDto<MailDto>>(
                "/api/v1/queries/mails?offset=" + запрос.Offset
                + "&limit=" + запрос.Limit
                + "&filter=" + Uri.EscapeDataString(JsonSerializer.Serialize(
                    фильтр is null ? new MailFilterDto() : SharedMapping.КФильтруОтправленийDto(фильтр),
                    SharedJson.Options)),
                токен)
            .ConfigureAwait(false);

        return new Page<Mail>(
            страница.Items.Select(ИзПисьма).ToArray(),
            страница.Offset,
            страница.Limit,
            страница.TotalCount);
    }

    /// <inheritdoc />
    public async Task<Mail?> ПолучитьПисьмоAsync(long идентификатор, CancellationToken токен = default)
    {
        var dto = await _транспорт
            .ВыполнитьЗапросAsync<MailDto?>(
                "/api/v1/queries/mails/" + идентификатор,
                токен)
            .ConfigureAwait(false);

        return dto is null ? null : ИзПисьма(dto);
    }

    /// <inheritdoc />
    public Task<Result<Mail>> СоздатьПисьмоAsync(Mail письмо, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<CreateMailRequest, MailDto>(
                    "mails.create",
                    new CreateMailRequest(SharedMapping.КОтправлению(письмо, письмо.Version)), токен)
                .ConfigureAwait(false);

            return ИзПисьма(ответ);
        });

    /// <inheritdoc />
    public Task<Result<Mail>> СменитьСтатусПисьмаAsync(
        Mail письмо,
        MailStatus статус,
        DateTime? момент = null,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<TransitionMailStatusRequest, MailDto>(
                    "mails.transition-status",
                    new TransitionMailStatusRequest(
                        письмо.Id,
                        статус,
                        момент,
                        письмо.Version), токен)
                .ConfigureAwait(false);

            return ИзПисьма(ответ);
        });

    /// <inheritdoc />
    public Task<Result<Mail>> ПеренестиПисьмоAsync(
        Mail письмо,
        long идентификаторПапки,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<MoveMailToFolderRequest, MailDto>(
                    "mails.move-to-folder",
                    new MoveMailToFolderRequest(письмо.Id, идентификаторПапки, письмо.Version), токен)
                .ConfigureAwait(false);

            return ИзПисьма(ответ);
        });

    /// <inheritdoc />
    public async Task<int> ОбновитьАдресВЧерновикахAsync(
        long идентификаторКонтрагента,
        Address адрес,
        CancellationToken токен = default)
    {
        var контрагент = await ПолучитьКонтрагентаAsync(идентификаторКонтрагента, токен)
            .ConfigureAwait(false);

        var версия = контрагент?.Version ?? 0;

        var ответ = await ВыполнитьОперациюAsync<UpdateDraftRecipientAddressRequest, UpdateDraftRecipientAddressResponse>(
                "mails.update-draft-address",
                new UpdateDraftRecipientAddressRequest(
                    идентификаторКонтрагента,
                    SharedMapping.КАдресу(адрес)!,
                    версия), токен)
            .ConfigureAwait(false);

        return ответ.Updated;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, int>> ПолучитьСчётчикиПоПапкамAsync(CancellationToken токен = default)
    {
        var счётчики = await _транспорт
            .ВыполнитьЗапросAsync<Dictionary<long, int>>("/api/v1/queries/folder-counts", токен)
            .ConfigureAwait(false);

        return счётчики;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Mail>> ПолучитьПисьмаРассылкиAsync(
        long идентификаторРассылки,
        CancellationToken токен = default)
    {
        var письма = await _транспорт
            .ВыполнитьЗапросAsync<MailDto[]>(
                "/api/v1/queries/distribution-mails?distributionId=" + идентификаторРассылки,
                токен)
            .ConfigureAwait(false);

        return письма.Select(ИзПисьма).ToArray();
    }

    /// <inheritdoc />
    public Task<IEmailLogWriter> ПолучитьПисателяEmailAsync(CancellationToken токен = default)
        => Task.FromResult<IEmailLogWriter>(new RemoteEmailLogWriter(_транспорт, _незавершённые));

    /// <inheritdoc />
    public async Task<Result<int>> ИзменитьСостояниеПоИдентификаторамAsync(
        IReadOnlyList<long> идентификаторы,
        bool восстановить,
        CancellationToken токен = default)
    {
        // Версии записей читаются при выполнении: после чтения запись могла
        // измениться, и служба отклонит устаревшую правку.
        var записи = new List<VersionedId>();

        foreach (var идентификатор in идентификаторы)
        {
            var контрагент = await ПолучитьКонтрагентаAsync(идентификатор, токен).ConfigureAwait(false);

            if (контрагент is null)
            {
                continue;
            }

            записи.Add(new VersionedId(контрагент.Id, контрагент.Version));
        }

        var ответ = await ВыполнитьОперациюAsync<BatchChangeCounterpartyStateRequest, BatchOperationResponse>(
                "counterparties.batch-change-state",
                new BatchChangeCounterpartyStateRequest(записи, восстановить), токен)
            .ConfigureAwait(false);

        return Result<int>.Success(ответ.Count);
    }

    /// <inheritdoc />
    public async Task<Result<int>> ИзменитьПолеПоИдентификаторамAsync(
        IReadOnlyList<long> идентификаторы,
        string свойство,
        string? значение,
        CancellationToken токен = default)
    {
        var записи = new List<VersionedId>();

        foreach (var идентификатор in идентификаторы)
        {
            var контрагент = await ПолучитьКонтрагентаAsync(идентификатор, токен).ConfigureAwait(false);

            if (контрагент is null)
            {
                continue;
            }

            записи.Add(new VersionedId(контрагент.Id, контрагент.Version));
        }

        var ответ = await ВыполнитьОперациюAsync<BatchUpdateCounterpartyFieldRequest, BatchOperationResponse>(
                "counterparties.batch-update-field",
                new BatchUpdateCounterpartyFieldRequest(записи, свойство, значение), токен)
            .ConfigureAwait(false);

        return Result<int>.Success(ответ.Count);
    }

    /// <inheritdoc />
    public Task<Result<Mail>> ВыдатьНомерПисьмуAsync(
        Mail письмо,
        string? шаблон = null,
        DateTime? момент = null,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<AssignMailNumberRequest, MailDto>(
                    "mails.assign-number",
                    new AssignMailNumberRequest(
                        письмо.Id,
                        шаблон,
                        момент,
                        письмо.Version), токен)
                .ConfigureAwait(false);

            return ИзПисьма(ответ);
        });

    /// <inheritdoc />
    public async Task<IReadOnlyList<DistributionView>> ПолучитьРассылкиAsync(
        DistributionFilter? фильтр = null,
        CancellationToken токен = default)
    {
        var рассылки = await _транспорт
            .ВыполнитьЗапросAsync<DistributionDto[]>(
                "/api/v1/queries/distributions?filter="
                + Uri.EscapeDataString(JsonSerializer.Serialize(
                    фильтр is null
                        ? new DistributionFilterDto()
                        : SharedMapping.КФильтруРассылокDto(фильтр),
                    SharedJson.Options)),
                токен)
            .ConfigureAwait(false);

        return рассылки
            .Select(рассылка => new DistributionView(
                рассылка.Id,
                рассылка.Name,
                рассылка.Kind,
                рассылка.State,
                рассылка.SenderId,
                рассылка.Note,
                рассылка.EnvelopesRequired,
                рассылка.CreatedAt,
                рассылка.DispatchedAt,
                рассылка.RecipientCount))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<Distribution?> ПолучитьРассылкуAsync(long идентификатор, CancellationToken токен = default)
    {
        var dto = await _транспорт
            .ВыполнитьЗапросAsync<DistributionDto?>(
                "/api/v1/queries/distributions/" + идентификатор,
                токен)
            .ConfigureAwait(false);

        if (dto is null)
        {
            return null;
        }

        return new Distribution
        {
            Id = dto.Id,
            Name = dto.Name,
            SenderId = dto.SenderId,
            Kind = dto.Kind,
            Note = dto.Note,
            State = dto.State,
            EnvelopesRequired = dto.EnvelopesRequired,
            CreatedAt = dto.CreatedAt,
            DispatchedAt = dto.DispatchedAt,
            Version = dto.Version,
        };
    }

    /// <inheritdoc />
    public Task<Result<Distribution>> СформироватьРассылкуAsync(
        DistributionPlan план,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<GenerateDistributionRequest, DistributionDto>(
                    "distributions.generate",
                    new GenerateDistributionRequest(план), токен)
                .ConfigureAwait(false);

            return ИзРассылки(ответ);
        });

    /// <inheritdoc />
    public Task<Result<Distribution>> СоздатьРассылкуИзВыборкиAsync(
        IReadOnlyList<long> идентификаторы,
        long? отправитель = null,
        string? наименование = null,
        string? формат = null,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<CreateDistributionFromSelectionRequest, DistributionDto>(
                    "distributions.create-from-selection",
                    new CreateDistributionFromSelectionRequest(
                        идентификаторы,
                        отправитель,
                        наименование,
                        формат), токен)
                .ConfigureAwait(false);

            return ИзРассылки(ответ);
        });

    /// <inheritdoc />
    public Task<Result<РассылкаКлон>> КлонироватьРассылкуAsync(
        long идентификатор,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<CloneDistributionRequest, CloneDistributionResponse>(
                    "distributions.clone-for-resend",
                    new CloneDistributionRequest(идентификатор), токен)
                .ConfigureAwait(false);

            return new РассылкаКлон(
                ИзРассылки(ответ.Distribution),
                ответ.LettersCreated,
                ответ.SkippedCounterparties);
        });

    /// <inheritdoc />
    public Task<Result<РассылкаПравка>> ИзменитьРассылкуAsync(
        DistributionUpdateRequest запрос,
        long ожидаемаяВерсия,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            // Версия — та, которую вызывающий прочитал: повторное чтение на
            // сервере означало бы «последний сохранивший победит».
            var ответ = await ВыполнитьОперациюAsync<UpdateDistributionRequest, UpdateDistributionResponse>(
                    "distributions.update",
                    new UpdateDistributionRequest(
                        запрос.DistributionId,
                        запрос.Name,
                        запрос.Kind,
                        запрос.SenderId,
                        запрос.CounterpartyIds,
                        запрос.EnvelopeType,
                        ожидаемаяВерсия), токен)
                .ConfigureAwait(false);

            return new РассылкаПравка(
                ИзРассылки(ответ.Distribution),
                ответ.LettersAdded,
                ответ.LettersRemoved,
                ответ.SkippedCounterparties);
        });

    /// <inheritdoc />
    public Task<Result<Distribution>> СохранитьРассылкуAsync(Distribution рассылка, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<SaveDistributionRequest, DistributionDto>(
                    "distributions.save",
                    new SaveDistributionRequest(рассылка.Id, рассылка.Version), токен)
                .ConfigureAwait(false);

            return ИзРассылки(ответ);
        });

    /// <inheritdoc />
    public Task<Result<Distribution>> ОтметитьРассылкуОтправленнойAsync(
        Distribution рассылка,
        DateTime? момент = null,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<MarkDistributionDispatchedRequest, DistributionDto>(
                    "distributions.mark-dispatched",
                    new MarkDistributionDispatchedRequest(рассылка.Id, момент, рассылка.Version), токен)
                .ConfigureAwait(false);

            return ИзРассылки(ответ);
        });

    /// <inheritdoc />
    public async Task<int> ПересчитатьКонвертыРассылкиAsync(Distribution рассылка, CancellationToken токен = default)
    {
        await ВыполнитьОперациюAsync<RecalculateEnvelopesRequest, DistributionDto>(
                "distributions.recalculate-envelopes",
                new RecalculateEnvelopesRequest(рассылка.Id, рассылка.Version), токен)
            .ConfigureAwait(false);

        var пересчитанная = await ПолучитьРассылкуAsync(рассылка.Id, токен).ConfigureAwait(false);

        return пересчитанная?.EnvelopesRequired ?? 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplatePackageDto>> ПолучитьПакетыШаблоновAsync(CancellationToken токен = default)
    {
        var ответ = await _транспорт
            .ВыполнитьЗапросAsync<TemplatePackageListResponse>(
                SharedProtocol.TemplatePackagesRoute,
                токен)
            .ConfigureAwait(false);

        return ответ.Packages;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintTemplate>> ПолучитьШаблоныAsync(
        string? вид = null,
        CancellationToken токен = default)
    {
        var шаблоны = await _транспорт
            .ВыполнитьЗапросAsync<PrintTemplateDto[]>("/api/v1/queries/templates", токен)
            .ConfigureAwait(false);

        return шаблоны
            .Select(шаблон => new PrintTemplate
            {
                Id = шаблон.Id,
                Name = шаблон.Name,
                Kind = шаблон.Kind,
                Format = шаблон.Format,
                WidthMm = шаблон.WidthMm,
                HeightMm = шаблон.HeightMm,
                Orientation = шаблон.Orientation,
                IsBuiltin = шаблон.IsBuiltin,
                Country = шаблон.Country,
                Version = шаблон.Version,
            })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlankField>> ПолучитьПоляШаблонаAsync(
        long идентификаторШаблона,
        CancellationToken токен = default)
    {
        var поля = await _транспорт
            .ВыполнитьЗапросAsync<BlankFieldDto[]>(
                "/api/v1/queries/template-fields?templateId=" + идентификаторШаблона,
                токен)
            .ConfigureAwait(false);

        return поля.Select(поле => new BlankField
        {
            Id = поле.Id,
            TemplateId = поле.TemplateId,
            Name = поле.Name,
            Kind = поле.Kind,
            Page = поле.Page,
            XMm = поле.XMm,
            YMm = поле.YMm,
            LengthMm = поле.LengthMm,
            HeightMm = поле.HeightMm,
            FontName = поле.FontName,
            FontHeightPt = поле.FontHeightPt,
            FontHeightMinPt = поле.FontHeightMinPt,
            FontHeightMaxPt = поле.FontHeightMaxPt,
            IsBold = поле.IsBold,
            IsItalic = поле.IsItalic,
            ColorRgb = поле.ColorRgb,
            Align = поле.Align,
            MaxChars = поле.MaxChars,
            IsVisible = поле.IsVisible,
            IsPrintable = поле.IsPrintable,
            ZoneRole = поле.ZoneRole,
            ClipartId = поле.ClipartId,
            Version = поле.Version,
        })
        .ToArray();
    }

    /// <inheritdoc />
    public Task<Result<PrintTemplate>> СохранитьШаблонAsync(
        PrintTemplate шаблон,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<SavePrintTemplateRequest, PrintTemplateDto>(
                    "templates.save",
                    new SavePrintTemplateRequest(
                        SharedMapping.КШаблону(шаблон, шаблон.Version, null),
                        шаблон.Version), токен)
                .ConfigureAwait(false);

            return new PrintTemplate
            {
                Id = ответ.Id,
                Name = ответ.Name,
                Kind = ответ.Kind,
                Format = ответ.Format,
                WidthMm = ответ.WidthMm,
                HeightMm = ответ.HeightMm,
                Orientation = ответ.Orientation,
                IsBuiltin = ответ.IsBuiltin,
                Country = ответ.Country,
                Version = ответ.Version,
            };
        });

    /// <inheritdoc />
    public Task<Result<bool>> УдалитьШаблонAsync(PrintTemplate шаблон, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            await ВыполнитьОперациюAsync<DeletePrintTemplateRequest, VersionedResponse>(
                    "templates.delete",
                    new DeletePrintTemplateRequest(шаблон.Id, шаблон.Version), токен)
                .ConfigureAwait(false);

            return true;
        });

    /// <inheritdoc />
    public Task<Result<BlankField>> СохранитьПолеШаблонаAsync(
        BlankField поле,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<SaveBlankFieldRequest, BlankFieldDto>(
                    "template-fields.save",
                    new SaveBlankFieldRequest(
                        SharedMapping.КПолюБланка(поле, поле.Version),
                        поле.Version), токен)
                .ConfigureAwait(false);

            return new BlankField
            {
                Id = ответ.Id,
                TemplateId = ответ.TemplateId,
                Name = ответ.Name,
                Kind = ответ.Kind,
                Page = ответ.Page,
                XMm = ответ.XMm,
                YMm = ответ.YMm,
                LengthMm = ответ.LengthMm,
                HeightMm = ответ.HeightMm,
                FontName = ответ.FontName,
                FontHeightPt = ответ.FontHeightPt,
                FontHeightMinPt = ответ.FontHeightMinPt,
                FontHeightMaxPt = ответ.FontHeightMaxPt,
                IsBold = ответ.IsBold,
                IsItalic = ответ.IsItalic,
                ColorRgb = ответ.ColorRgb,
                Align = ответ.Align,
                MaxChars = ответ.MaxChars,
                IsVisible = ответ.IsVisible,
                IsPrintable = ответ.IsPrintable,
                ZoneRole = ответ.ZoneRole,
                ClipartId = ответ.ClipartId,
                Version = ответ.Version,
            };
        });

    /// <inheritdoc />
    public Task<Result<bool>> УдалитьПолеШаблонаAsync(BlankField поле, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            await ВыполнитьОперациюAsync<DeleteBlankFieldRequest, VersionedResponse>(
                    "template-fields.delete",
                    new DeleteBlankFieldRequest(поле.Id, поле.Version), токен)
                .ConfigureAwait(false);

            return true;
        });

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintableItem>> ПолучитьДанныеПечатиAsync(
        IReadOnlyList<long> идентификаторы,
        CancellationToken токен = default)
    {
        // print.get-data — команда-чтение: исход не влияет на данные,
        // регистрация в незавершённых операциях не требуется.
        var ответ = await _транспорт
            .ВыполнитьКомандуJsonAsync<GetPrintDataResponse>(
                "print.get-data",
                JsonSerializer.Serialize(new GetPrintDataRequest(идентификаторы), SharedJson.Options),
                Guid.NewGuid().ToString("D"),
                токен)
            .ConfigureAwait(false);

        return ответ.Items
            .Select(элемент => new PrintableItem
            {
                Id = элемент.Id,
                Values = элемент.Values,
                LogoBytes = элемент.LogoBytes,
            })
            .ToArray();
    }

    /// <inheritdoc />
    public Task<Result<PrintHistory>> ЗаписатьПечатноеСобытиеAsync(
        IReadOnlyList<long> идентификаторы,
        string описаниеШаблона,
        PrintEventOutcome исход,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<RecordPrintEventRequest, PrintEventResponse>(
                    "print.record-event",
                    new RecordPrintEventRequest(
                        идентификаторы,
                        описаниеШаблона,
                        исход), токен)
                .ConfigureAwait(false);

            return new PrintHistory
            {
                Id = 0,
                PrintedAt = DateTime.UtcNow,
                MailId = идентификаторы.FirstOrDefault(),
                RecipientAddressSnapshot = string.Empty,
                SenderAddressSnapshot = string.Empty,
                CopyCount = 1,
                IsVip = false,
                Note = null,
                TemplateDescription = описаниеШаблона,
                PrintJobId = ответ.PrintJobId,
            };
        });

    /// <inheritdoc />
    public async Task<Page<PrintHistory>> ПолучитьИсториюПечатиAsync(
        PageRequest запрос,
        CancellationToken токен = default)
    {
        var страница = await _транспорт
            .ВыполнитьЗапросAsync<PageDto<PrintHistoryDto>>(
                "/api/v1/queries/print-history?offset=" + запрос.Offset
                + "&limit=" + запрос.Limit,
                токен)
            .ConfigureAwait(false);

        return new Page<PrintHistory>(
            страница.Items
                .Select(запись => new PrintHistory
                {
                    Id = запись.Id,
                    PrintedAt = запись.PrintedAt,
                    MailId = запись.MailId,
                    RecipientAddressSnapshot = запись.RecipientAddressSnapshot,
                    SenderAddressSnapshot = запись.SenderAddressSnapshot,
                    CopyCount = запись.CopyCount,
                    IsVip = запись.IsVip,
                    Note = запись.Note,
                    TemplateDescription = запись.TemplateDescription,
                    PrintJobId = запись.PrintJobId,
                    Outcome = запись.Outcome,
                    DeviceId = запись.DeviceId,
                })
                .ToArray(),
            страница.Offset,
            страница.Limit,
            страница.TotalCount);
    }

    /// <inheritdoc />
    public async Task<СнимокИспользования> ПолучитьСнимокОбъёмаAsync(CancellationToken токен = default)
    {
        var снимок = await _транспорт
            .ВыполнитьЗапросAsync<UsageSnapshotDto>("/api/v1/queries/usage", токен)
            .ConfigureAwait(false);

        return new СнимокИспользования(снимок.CounterpartyCount, снимок.MailCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ClearScope, int>> ПолучитьСчётчикиОчисткиAsync(
        CancellationToken токен = default)
    {
        var счётчики = await _транспорт
            .ВыполнитьЗапросAsync<ScopeCountsDto>("/api/v1/queries/scope-counts", токен)
            .ConfigureAwait(false);

        return new Dictionary<ClearScope, int>
        {
            [ClearScope.Counterparties] = счётчики.Counterparties,
            [ClearScope.Distributions] = счётчики.Distributions,
            [ClearScope.SentMails] = счётчики.Mails,
            [ClearScope.CounterpartyCategories] = счётчики.Counterparties,
        };
    }

    /// <inheritdoc />
    public Task<Result<int>> ОчиститьБазуAsync(ClearScope область, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await ВыполнитьОперациюAsync<ClearDatabaseRequest, ClearDatabaseResponse>(
                    "database.clear",
                    new ClearDatabaseRequest(область), токен)
                .ConfigureAwait(false);

            return ответ.Cleared;
        });

    /// <inheritdoc />
    public async Task<int> НаполнитьДемоДаннымиAsync(CancellationToken токен = default)
    {
        var ответ = await ВыполнитьОперациюAsync<SeedDemoRequest, SeedDemoResponse>(
                "database.seed-demo",
                new SeedDemoRequest(), токен)
            .ConfigureAwait(false);

        return ответ.Seeded;
    }

    /// <inheritdoc />
    public async Task<ImportMergeReport> СлитьИмпортAsync(
        ImportBatch пакет,
        MergePolicy политика,
        CancellationToken токен = default)
    {
        var ответ = await ВыполнитьОперациюAsync<ImportMergeRequest, ImportMergeResponse>(
                "import.merge",
                new ImportMergeRequest(
                    пакет.SourceFormat,
                    пакет.SourceFile,
                    пакет.ImportedAt,
                    пакет.Records.ToArray(),
                    политика), токен)
            .ConfigureAwait(false);

        return new ImportMergeReport(
            ответ.Created,
            ответ.Updated,
            ответ.Skipped,
            ответ.Conflicts);
    }

    /// <inheritdoc />
    public async Task ПовторитьНезавершённуюAsync(
        string идентификатор,
        CancellationToken токен = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(идентификатор);

        await _незавершённые
            .ПовторитьAsync(_транспорт, идентификатор, токен)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Выполняет мутацию через механизм незавершённых операций: тело
    /// запроса сериализуется один раз, регистрируется в хранилище вместе с
    /// идентификатором операции и передаётся службе; при потере ответа
    /// исход сообщается как неизвестный, повтор — с прежними
    /// идентификатором и телом.
    /// </summary>
    /// <typeparam name="TЗапрос">Тип запроса команды.</typeparam>
    /// <typeparam name="TОтвет">Тип ответа команды.</typeparam>
    /// <param name="имяКоманды">Имя команды службы.</param>
    /// <param name="запрос">Запрос команды.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Ответ команды.</returns>
    private async Task<TОтвет> ВыполнитьОперациюAsync<TЗапрос, TОтвет>(
        string имяКоманды,
        TЗапрос запрос,
        CancellationToken токен)
        where TЗапрос : notnull
    {
        var тело = JsonSerializer.Serialize(запрос, SharedJson.Options);

        return await _незавершённые
            .ВыполнитьAsync<TОтвет>(_транспорт, имяКоманды, тело, токен)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Читает текущую версию группы: категория в общей базе хранится среди
    /// групп, а её версия читается отдельным запросом.
    /// </summary>
    /// <param name="идентификатор">Идентификатор группы.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Версия группы.</returns>
    private async Task<long> ТекущаяВерсияГруппыAsync(long идентификатор, CancellationToken токен)
    {
        var группы = await _транспорт
            .ВыполнитьЗапросAsync<GroupDto[]>("/api/v1/queries/groups", токен)
            .ConfigureAwait(false);

        return группы.FirstOrDefault(группа => группа.Id == идентификатор)?.Version ?? 0;
    }

    /// <summary>
    /// Преобразует DTO контрагента в доменную сущность.
    /// </summary>
    /// <param name="dto">DTO контрагента.</param>
    /// <returns>Контрагент.</returns>
    private static Counterparty ИзКонтрагента(CounterpartyDto dto)
    {
        var контрагент = SharedMapping.ИзКонтрагента(dto);

        контрагент.Version = dto.Version;

        return контрагент;
    }

    /// <summary>
    /// Преобразует DTO отправления в доменную сущность.
    /// </summary>
    /// <param name="dto">DTO отправления.</param>
    /// <returns>Отправление.</returns>
    private static Mail ИзПисьма(MailDto dto)
    {
        var письмо = SharedMapping.ИзОтправления(dto);

        письмо.Version = dto.Version;

        return письмо;
    }

    /// <summary>
    /// Преобразует DTO рассылки в доменную сущность.
    /// </summary>
    /// <param name="dto">DTO рассылки.</param>
    /// <returns>Рассылка.</returns>
    private static Distribution ИзРассылки(DistributionDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        SenderId = dto.SenderId,
        Kind = dto.Kind,
        Note = dto.Note,
        State = dto.State,
        EnvelopesRequired = dto.EnvelopesRequired,
        CreatedAt = dto.CreatedAt,
        DispatchedAt = dto.DispatchedAt,
        Version = dto.Version,
    };

    /// <summary>
    /// Преобразует DTO папки в доменную сущность.
    /// </summary>
    /// <param name="dto">DTO папки.</param>
    /// <returns>Папка.</returns>
    private static Folder ИзПапки(FolderDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        ParentId = dto.ParentId,
        Note = dto.Note,
        SortOrder = dto.SortOrder,
        Version = dto.Version,
    };

    /// <summary>
    /// Выполняет действие и превращает отказ службы в результат с текстом
    /// ошибки: конфликт версий и проверка данных показываются оператору как
    /// отказ операции, а не как сбой программы.
    /// </summary>
    /// <typeparam name="T">Тип результата.</typeparam>
    /// <param name="действие">Выполняемое действие.</param>
    /// <returns>Результат действия.</returns>
    private static async Task<Result<T>> ВыполнитьAsync<T>(Func<Task<T>> действие)
        where T : notnull
    {
        try
        {
            return Result<T>.Success(await действие().ConfigureAwait(false));
        }
        catch (SharedModeException ошибка) when (
            ошибка.Код == SharedErrorCodes.Conflict
            || ошибка.Код == SharedErrorCodes.ValidationFailed
            || ошибка.Код == SharedErrorCodes.NotFound)
        {
            return Result<T>.Failure(ошибка.Message);
        }
    }
}
