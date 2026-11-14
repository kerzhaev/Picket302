using Picket302.Application.Services;
using Picket302.Composition;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Infrastructure.Licensing;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Printing;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;

namespace Picket302.App.Shared;

/// <summary>
/// Доступ к данным одиночного режима: те же прикладные службы и репозитории,
/// что и до появления общего режима.
/// </summary>
/// <remarks>
/// Реализация не меняет поведение программы: она лишь переносит вызовы
/// оболочки за единый интерфейс <see cref="IDataGateway"/>. Правила
/// предметной области по-прежнему выполняют прикладные службы доменного слоя.
/// </remarks>
public sealed class LocalDataGateway : IDataGateway
{
    private readonly DataServices _данные;

    /// <summary>
    /// Создаёт доступ к данным одиночного режима.
    /// </summary>
    /// <param name="данные">Состав слоя данных над локальной сессией базы.</param>
    public LocalDataGateway(DataServices данные)
    {
        ArgumentNullException.ThrowIfNull(данные);

        _данные = данные;
    }

    /// <inheritdoc />
    public bool ОбщийРежим => false;

    /// <inheritdoc />
    public Task<Page<Counterparty>> ПолучитьКонтрагентовAsync(
        PageRequest запрос,
        CounterpartyFilter? фильтр = null,
        CancellationToken токен = default)
        => _данные.Контрагенты.GetPagedAsync(запрос, фильтр, токен);

    /// <inheritdoc />
    public Task<Counterparty?> ПолучитьКонтрагентаAsync(long идентификатор, CancellationToken токен = default)
        => _данные.Контрагенты.GetByIdAsync(идентификатор, токен);

    /// <inheritdoc />
    public Task<Result<Counterparty>> СохранитьКонтрагентаAsync(
        Counterparty контрагент,
        CancellationToken токен = default)
        => _данные.СлужбаКонтрагентов.SaveAsync(контрагент, токен);

    /// <inheritdoc />
    public Task<Result<Counterparty>> УдалитьКонтрагентаAsync(
        Counterparty контрагент,
        CancellationToken токен = default)
        => _данные.СлужбаКонтрагентов.SoftDeleteAsync(контрагент.Id, токен);

    /// <inheritdoc />
    public async Task<bool> ОкончательноУдалитьКонтрагентаAsync(
        long идентификатор,
        CancellationToken токен = default)
        => await _данные.Контрагенты.DeleteAsync(идентификатор, токен).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<int>> ИзменитьСостояниеКонтрагентовAsync(
        IReadOnlyList<Counterparty> контрагенты,
        bool восстановить,
        CancellationToken токен = default)
    {
        var идентификаторы = контрагенты.Select(з => з.Id).ToArray();

        return восстановить
            ? await _данные.СлужбаМенеджера.RestoreAsync(идентификаторы, токен).ConfigureAwait(false)
            : await _данные.СлужбаМенеджера.FastDeleteMarkAsync(идентификаторы, токен).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<int>> ИзменитьПолеКонтрагентовAsync(
        IReadOnlyList<Counterparty> контрагенты,
        string свойство,
        string? значение,
        CancellationToken токен = default)
        => _данные.СлужбаПакетногоРедактирования.BatchUpdateFieldAsync(
            контрагенты.Select(з => з.Id).ToArray(),
            свойство,
            значение,
            токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<Counterparty>> ПолучитьОтправителейAsync(CancellationToken токен = default)
        => _данные.Контрагенты.GetSendersAsync(токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<Counterparty>> ПолучитьКонтрагентовГруппыAsync(
        long идентификаторГруппы,
        CancellationToken токен = default)
        => _данные.Контрагенты.GetByGroupAsync(идентификаторГруппы, токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ПолучитьЗначенияПоляAsync(
        string поле,
        CancellationToken токен = default)
        => _данные.Контрагенты.GetDistinctFieldValuesAsync(поле, токен);

    /// <inheritdoc />
    public Task<Counterparty?> НайтиПоКлючуСлиянияAsync(
        string? инн,
        string наименование,
        string? индекс,
        CancellationToken токен = default)
        => _данные.Контрагенты.FindByMergeKeyAsync(инн, наименование, индекс, токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<CounterpartyField>> ПолучитьПоляКонтрагентаAsync(
        long идентификаторКонтрагента,
        CancellationToken токен = default)
        => _данные.ПоляКонтрагентов.GetByCounterpartyAsync(идентификаторКонтрагента, токен);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ПолучитьЗначенияПолейКонтрагентаAsync(
        long идентификаторКонтрагента,
        CancellationToken токен = default)
        => _данные.СлужбаПолей.GetCustomValuesAsync(идентификаторКонтрагента, токен);

    /// <inheritdoc />
    public Task<Result<CounterpartyField>> ЗаписатьПолеКонтрагентаAsync(
        long идентификаторКонтрагента,
        string поле,
        string значение,
        CancellationToken токен = default)
        => _данные.СлужбаПолей.SetCustomValueAsync(идентификаторКонтрагента, поле, значение, токен);

    /// <inheritdoc />
    public async Task<Result<bool>> УдалитьПолеКонтрагентаAsync(
        long идентификаторКонтрагента,
        string поле,
        CancellationToken токен = default)
    {
        var удалено = await _данные.СлужбаПолей
            .RemoveCustomValueAsync(идентификаторКонтрагента, поле, токен)
            .ConfigureAwait(false);

        return удалено
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("Значение пользовательского поля не найдено.");
    }

    /// <inheritdoc />
    public Task<Result<Counterparty>> СоздатьОтправителяAsync(AgentDto агент, CancellationToken токен = default)
        => _данные.СлужбаАгентов.CreateAgentAsync(агент, токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<Group>> ПолучитьГруппыAsync(CancellationToken токен = default)
        => _данные.Группы.GetAllAsync(токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<Category>> ПолучитьДеревоКатегорийAsync(CancellationToken токен = default)
        => _данные.СлужбаКатегорий.GetTreeAsync(токен);

    /// <inheritdoc />
    public Task<Result<Category>> СоздатьКатегориюAsync(
        string наименование,
        long? родитель = null,
        CancellationToken токен = default)
        => _данные.СлужбаКатегорий.CreateCategoryAsync(наименование, родитель, токен);

    /// <inheritdoc />
    public Task<Result<Category>> ПереименоватьКатегориюAsync(
        Category категория,
        string наименование,
        CancellationToken токен = default)
        => _данные.СлужбаКатегорий.RenameCategoryAsync(категория.Id, наименование, токен);

    /// <inheritdoc />
    public Task<Result<int>> УдалитьКатегориюAsync(Category категория, CancellationToken токен = default)
        => _данные.СлужбаКатегорий.DeleteCategoryAsync(категория.Id, токен);

    /// <inheritdoc />
    public Task<Result<Counterparty>> ПеренестиКонтрагентаAsync(
        long идентификаторКонтрагента,
        long идентификаторКатегории,
        CancellationToken токен = default)
        => _данные.СлужбаКатегорий.MoveCounterpartyAsync(
            идентификаторКонтрагента,
            идентификаторКатегории,
            токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<Folder>> ПолучитьПапкиAsync(CancellationToken токен = default)
        => _данные.Папки.GetAllAsync(токен);

    /// <inheritdoc />
    public Task<Result<Folder>> СоздатьПапкуAsync(
        string наименование,
        long? родитель = null,
        string? примечание = null,
        CancellationToken токен = default)
        => _данные.СлужбаПапок.CreateAsync(наименование, родитель, примечание, токен);

    /// <inheritdoc />
    public Task<Result<Folder>> ПереименоватьПапкуAsync(
        Folder папка,
        string наименование,
        CancellationToken токен = default)
        => _данные.СлужбаПапок.RenameAsync(папка.Id, наименование, токен);

    /// <inheritdoc />
    public Task<Result<Folder>> ПеренестиПапкуAsync(
        Folder папка,
        long? родитель,
        CancellationToken токен = default)
        => _данные.СлужбаПапок.MoveAsync(папка.Id, родитель, токен);

    /// <inheritdoc />
    public async Task<Result<bool>> УдалитьПапкуAsync(Folder папка, CancellationToken токен = default)
    {
        var удалена = await _данные.Папки.DeleteAsync(папка.Id, токен).ConfigureAwait(false);

        return удалена
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("Папка не найдена.");
    }

    /// <inheritdoc />
    public Task<int> ПолучитьЧислоПисемВПапкеAsync(
        long идентификаторПапки,
        CancellationToken токен = default)
        => _данные.СлужбаПапок.CountMailsAsync(идентификаторПапки, токен);

    /// <inheritdoc />
    public Task<Page<Mail>> ПолучитьПисьмаAsync(
        PageRequest запрос,
        MailFilter? фильтр = null,
        CancellationToken токен = default)
        => _данные.Письма.GetPagedAsync(запрос, фильтр, токен);

    /// <inheritdoc />
    public Task<Mail?> ПолучитьПисьмоAsync(long идентификатор, CancellationToken токен = default)
        => _данные.Письма.GetByIdAsync(идентификатор, токен);

    /// <inheritdoc />
    public Task<Result<Mail>> СоздатьПисьмоAsync(Mail письмо, CancellationToken токен = default)
        => _данные.СлужбаПисем.CreateAsync(письмо, токен);

    /// <inheritdoc />
    public Task<Result<Mail>> СменитьСтатусПисьмаAsync(
        Mail письмо,
        MailStatus статус,
        DateTime? момент = null,
        CancellationToken токен = default)
        => _данные.СлужбаПисем.TransitionStatusAsync(письмо.Id, статус, момент, токен);

    /// <inheritdoc />
    public Task<Result<Mail>> ПеренестиПисьмоAsync(
        Mail письмо,
        long идентификаторПапки,
        CancellationToken токен = default)
        => _данные.СлужбаПисем.MoveToFolderAsync(письмо.Id, идентификаторПапки, токен);

    /// <inheritdoc />
    public Task<int> ОбновитьАдресВЧерновикахAsync(
        long идентификаторКонтрагента,
        Address адрес,
        CancellationToken токен = default)
        => _данные.СлужбаКаскада.UpdateUnprintedLettersAddressAsync(
            идентификаторКонтрагента,
            адрес,
            токен);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<long, int>> ПолучитьСчётчикиПоПапкамAsync(CancellationToken токен = default)
        => _данные.Письма.ПолучитьСчётчикиПоПапкамAsync(токен);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Mail>> ПолучитьПисьмаРассылкиAsync(
        long идентификаторРассылки,
        CancellationToken токен = default)
        => await _данные.Письма
            .GetByDistributionAsync(идентификаторРассылки, токен)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<IEmailLogWriter> ПолучитьПисателяEmailAsync(CancellationToken токен = default)
        => Task.FromResult<IEmailLogWriter>(new LocalEmailLogWriter(_данные.Сессия));

    /// <inheritdoc />
    public Task<Result<int>> ИзменитьСостояниеПоИдентификаторамAsync(
        IReadOnlyList<long> идентификаторы,
        bool восстановить,
        CancellationToken токен = default)
        => восстановить
            ? _данные.СлужбаМенеджера.RestoreAsync(идентификаторы, токен)
            : _данные.СлужбаМенеджера.FastDeleteMarkAsync(идентификаторы, токен);

    /// <inheritdoc />
    public Task<Result<int>> ИзменитьПолеПоИдентификаторамAsync(
        IReadOnlyList<long> идентификаторы,
        string свойство,
        string? значение,
        CancellationToken токен = default)
        => _данные.СлужбаПакетногоРедактирования.BatchUpdateFieldAsync(
            идентификаторы,
            свойство,
            значение,
            токен);

    /// <inheritdoc />
    public async Task<Result<Mail>> ВыдатьНомерПисьмуAsync(
        Mail письмо,
        string? шаблон = null,
        DateTime? момент = null,
        CancellationToken токен = default)
    {
        var номер = await _данные.СлужбаНумерации
            .GenerateNextOutgoingNumberAsync(шаблон, момент, токен)
            .ConfigureAwait(false);

        письмо.OutgoingNumber = номер;

        var затронуто = await _данные.Письма.UpdateAsync(письмо, токен).ConfigureAwait(false);

        return затронуто > 0
            ? Result<Mail>.Success(письмо)
            : Result<Mail>.Failure("Отправление не найдено.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DistributionView>> ПолучитьРассылкиAsync(
        DistributionFilter? фильтр = null,
        CancellationToken токен = default)
        => _данные.СлужбаРассылок.ListDistributionsAsync(фильтр, токен);

    /// <inheritdoc />
    public Task<Distribution?> ПолучитьРассылкуAsync(long идентификатор, CancellationToken токен = default)
        => _данные.Рассылки.GetByIdAsync(идентификатор, токен);

    /// <inheritdoc />
    public Task<Result<Distribution>> СформироватьРассылкуAsync(
        DistributionPlan план,
        CancellationToken токен = default)
        => _данные.СлужбаРассылок.GenerateDistributionAsync(план, токен);

    /// <inheritdoc />
    public Task<Result<Distribution>> СоздатьРассылкуИзВыборкиAsync(
        IReadOnlyList<long> идентификаторы,
        long? отправитель = null,
        string? наименование = null,
        string? формат = null,
        CancellationToken токен = default)
        => _данные.СлужбаРассылок.CreateFromSelectionAsync(
            идентификаторы,
            отправитель,
            наименование,
            формат,
            токен);

    /// <inheritdoc />
    public async Task<Result<РассылкаКлон>> КлонироватьРассылкуAsync(
        long идентификатор,
        CancellationToken токен = default)
    {
        var результат = await _данные.СлужбаРассылок
            .CloneForResendAsync(идентификатор, токен)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            return Result<РассылкаКлон>.Failure(результат.Error!);
        }

        var клон = результат.Value;

        return Result<РассылкаКлон>.Success(
            new РассылкаКлон(клон.Distribution, клон.LettersCreated, клон.SkippedCounterparties));
    }

    /// <inheritdoc />
    public async Task<Result<РассылкаПравка>> ИзменитьРассылкуAsync(
        DistributionUpdateRequest запрос,
        long ожидаемаяВерсия,
        CancellationToken токен = default)
    {
        var результат = await _данные.СлужбаРассылок
            .UpdateDistributionAsync(запрос, токен)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            return Result<РассылкаПравка>.Failure(результат.Error!);
        }

        var правка = результат.Value;

        return Result<РассылкаПравка>.Success(
            new РассылкаПравка(
                правка.Distribution,
                правка.LettersAdded,
                правка.LettersRemoved,
                правка.SkippedCounterparties));
    }

    /// <inheritdoc />
    public Task<Result<Distribution>> СохранитьРассылкуAsync(Distribution рассылка, CancellationToken токен = default)
        => _данные.СлужбаРассылок.SaveAsync(рассылка.Id, токен);

    /// <inheritdoc />
    public Task<Result<Distribution>> ОтметитьРассылкуОтправленнойAsync(
        Distribution рассылка,
        DateTime? момент = null,
        CancellationToken токен = default)
        => _данные.СлужбаРассылок.MarkDispatchedAsync(рассылка.Id, момент, токен);

    /// <inheritdoc />
    public Task<int> ПересчитатьКонвертыРассылкиAsync(Distribution рассылка, CancellationToken токен = default)
        => _данные.СлужбаРассылок.RecalculateEnvelopesRequiredAsync(рассылка.Id, токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<PrintTemplate>> ПолучитьШаблоныAsync(
        string? вид = null,
        CancellationToken токен = default)
        => _данные.ШаблоныПечати.GetAllAsync(вид, токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<BlankField>> ПолучитьПоляШаблонаAsync(
        long идентификаторШаблона,
        CancellationToken токен = default)
        => _данные.ПоляШаблонов.GetByTemplateAsync(идентификаторШаблона, токен);

    /// <inheritdoc />
    public Task<IReadOnlyList<TemplatePackageDto>> ПолучитьПакетыШаблоновAsync(CancellationToken токен = default)
        => Task.FromResult<IReadOnlyList<TemplatePackageDto>>(Array.Empty<TemplatePackageDto>());

    /// <inheritdoc />
    public async Task<Result<PrintTemplate>> СохранитьШаблонAsync(
        PrintTemplate шаблон,
        CancellationToken токен = default)
    {
        if (шаблон.Id <= 0)
        {
            var идентификатор = await _данные.ШаблоныПечати.AddAsync(шаблон, токен).ConfigureAwait(false);
            шаблон.Id = идентификатор;

            return Result<PrintTemplate>.Success(шаблон);
        }

        var затронуто = await _данные.ШаблоныПечати.UpdateAsync(шаблон, токен).ConfigureAwait(false);

        return затронуто > 0
            ? Result<PrintTemplate>.Success(шаблон)
            : Result<PrintTemplate>.Failure("Шаблон не найден.");
    }

    /// <inheritdoc />
    public async Task<Result<bool>> УдалитьШаблонAsync(PrintTemplate шаблон, CancellationToken токен = default)
    {
        var удалён = await _данные.ШаблоныПечати.DeleteAsync(шаблон.Id, токен).ConfigureAwait(false);

        return удалён
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("Шаблон не найден.");
    }

    /// <inheritdoc />
    public async Task<Result<BlankField>> СохранитьПолеШаблонаAsync(
        BlankField поле,
        CancellationToken токен = default)
    {
        if (поле.Id <= 0)
        {
            var идентификатор = await _данные.ПоляШаблонов.AddAsync(поле, токен).ConfigureAwait(false);
            поле.Id = идентификатор;

            return Result<BlankField>.Success(поле);
        }

        var затронуто = await _данные.ПоляШаблонов.UpdateAsync(поле, токен).ConfigureAwait(false);

        return затронуто > 0
            ? Result<BlankField>.Success(поле)
            : Result<BlankField>.Failure("Поле бланка не найдено.");
    }

    /// <inheritdoc />
    public async Task<Result<bool>> УдалитьПолеШаблонаAsync(BlankField поле, CancellationToken токен = default)
    {
        var удалено = await _данные.ПоляШаблонов.DeleteAsync(поле.Id, токен).ConfigureAwait(false);

        return удалено
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("Поле бланка не найдено.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PrintableItem>> ПолучитьДанныеПечатиAsync(
        IReadOnlyList<long> идентификаторы,
        CancellationToken токен = default)
        => _данные.ДанныеПечати.GetItemsAsync(идентификаторы, токен);

    /// <inheritdoc />
    public async Task<Result<PrintHistory>> ЗаписатьПечатноеСобытиеAsync(
        IReadOnlyList<long> идентификаторы,
        string описаниеШаблона,
        PrintEventOutcome исход,
        CancellationToken токен = default)
    {
        if (идентификаторы.Count == 0)
        {
            return Result<PrintHistory>.Failure("Не выбрано ни одного отправления.");
        }

        Result<PrintHistory>? последний = null;

        foreach (var идентификатор in идентификаторы)
        {
            последний = await _данные.СлужбаПисем
                .RegisterPrintAsync(
                    идентификатор,
                    null,
                    описаниеШаблона,
                    токен,
                    исход.ToString(),
                    deviceId: null)
                .ConfigureAwait(false);
        }

        return последний ?? Result<PrintHistory>.Failure("Печатное событие не зафиксировано.");
    }

    /// <inheritdoc />
    public Task<Page<PrintHistory>> ПолучитьИсториюПечатиAsync(
        PageRequest запрос,
        CancellationToken токен = default)
        => _данные.ИсторииПечати.GetPagedAsync(запрос, токен);

    /// <inheritdoc />
    public async Task<СнимокИспользования> ПолучитьСнимокОбъёмаAsync(CancellationToken токен = default)
    {
        var контрагентов = await _данные.Обслуживание
            .CountScopeAsync(ClearScope.Counterparties, токен)
            .ConfigureAwait(false);

        var писем = await _данные.Обслуживание
            .CountScopeAsync(ClearScope.SentMails, токен)
            .ConfigureAwait(false);

        return new СнимокИспользования(контрагентов, писем);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<ClearScope, int>> ПолучитьСчётчикиОчисткиAsync(CancellationToken токен = default)
        => _данные.СлужбаСидов.GetScopeCountsAsync(токен);

    /// <inheritdoc />
    public Task<Result<int>> ОчиститьБазуAsync(ClearScope область, CancellationToken токен = default)
        => _данные.СлужбаСидов.ClearDatabaseAsync(область, токен);

    /// <inheritdoc />
    public Task<int> НаполнитьДемоДаннымиAsync(CancellationToken токен = default)
        => _данные.СлужбаСидов.SeedDemoDataAsync(токен);

    /// <inheritdoc />
    public async Task<ImportMergeReport> СлитьИмпортAsync(
        ImportBatch пакет,
        MergePolicy политика,
        CancellationToken токен = default)
    {
        var слияние = new ImportMergeService(
            _данные.Контрагенты,
            _данные.ПоляКонтрагентов);

        var исходы = await слияние
            .MergeImportedDetailed(пакет, политика, токен)
            .ConfigureAwait(false);

        var создано = 0;
        var обновлено = 0;
        var конфликтов = 0;

        foreach (var исход in исходы)
        {
            switch (исход.Action)
            {
                case ImportMergeAction.Created:
                    создано++;
                    break;

                case ImportMergeAction.Updated:
                    обновлено++;
                    break;

                default:
                    if (исход.HasConflict)
                    {
                        конфликтов++;
                    }

                    break;
            }
        }

        return new ImportMergeReport(
            создано,
            обновлено,
            исходы.Count - создано - обновлено,
            конфликтов);
    }
}
