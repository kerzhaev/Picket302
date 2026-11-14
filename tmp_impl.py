import io


def patch(path, edits):
    s = io.open(path, encoding="utf-8").read()
    for old, new in edits:
        if old not in s:
            raise SystemExit("NOT FOUND in %s:\n%s" % (path, old[:240]))
        s = s.replace(old, new, 1)
    io.open(path, "w", encoding="utf-8", newline="\n").write(s)
    print("patched", path)


# --- Local gateway: new members -------------------------------------------------------
patch("src/Picket302.App/Shared/LocalDataGateway.cs", [
    (
        """    /// <inheritdoc />
    public Task<IReadOnlyDictionary<long, int>> ПолучитьСчётчикиПоПапкамAsync(CancellationToken токен = default)
        => _данные.Письма.ПолучитьСчётчикиПоПапкамAsync(токен);""",
        """    /// <inheritdoc />
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
            токен);""",
    ),
    (
        """    /// <inheritdoc />
    public Task<Result<Distribution>> СохранитьРассылкуAsync(long идентификатор, CancellationToken токен = default)
        => _данные.СлужбаРассылок.SaveAsync(идентификатор, токен);

    /// <inheritdoc />
    public Task<Result<Distribution>> ОтметитьРассылкуОтправленнойAsync(
        long идентификатор,
        DateTime? момент = null,
        CancellationToken токен = default)
        => _данные.СлужбаРассылок.MarkDispatchedAsync(идентификатор, момент, токен);

    /// <inheritdoc />
    public Task<int> ПересчитатьКонвертыРассылкиAsync(long идентификатор, CancellationToken токен = default)
        => _данные.СлужбаРассылок.RecalculateEnvelopesRequiredAsync(идентификатор, токен);""",
        """    /// <inheritdoc />
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
        => _данные.СлужбаРассылок.RecalculateEnvelopesRequiredAsync(рассылка.Id, токен);""",
    ),
])

# --- Remote gateway: new members ------------------------------------------------------
patch("src/Picket302.App/Shared/RemoteDataGateway.cs", [
    (
        """    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, int>> ПолучитьСчётчикиПоПапкамAsync(CancellationToken токен = default)
    {
        var счётчики = await _транспорт
            .ВыполнитьЗапросAsync<Dictionary<long, int>>("/api/v1/queries/folder-counts", токен)
            .ConfigureAwait(false);

        return счётчики;
    }""",
        """    /// <inheritdoc />
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
        => Task.FromResult<IEmailLogWriter>(new RemoteEmailLogWriter(_транспорт));

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

        var ответ = await _транспорт
            .ВыполнитьКомандуAsync<BatchChangeCounterpartyStateRequest, BatchOperationResponse>(
                "counterparties.batch-change-state",
                new BatchChangeCounterpartyStateRequest(записи, восстановить),
                cancellationToken: токен)
            .ConfigureAwait(false);

        return ответ.Count;
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

        var ответ = await _транспорт
            .ВыполнитьКомандуAsync<BatchUpdateCounterpartyFieldRequest, BatchOperationResponse>(
                "counterparties.batch-update-field",
                new BatchUpdateCounterpartyFieldRequest(записи, свойство, значение),
                cancellationToken: токен)
            .ConfigureAwait(false);

        return ответ.Count;
    }""",
    ),
    (
        """    /// <inheritdoc />
    public Task<Result<Distribution>> СохранитьРассылкуAsync(long идентификатор, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var версия = await ТекущаяВерсияРассылкиAsync(идентификатор, токен).ConfigureAwait(false);

            var ответ = await _транспорт
                .ВыполнитьКомандуAsync<SaveDistributionRequest, DistributionDto>(
                    "distributions.save",
                    new SaveDistributionRequest(идентификатор, версия),
                    operationId: null,
                    cancellationToken: токен)
                .ConfigureAwait(false);

            return ИзРассылки(ответ);
        });

    /// <inheritdoc />
    public Task<Result<Distribution>> ОтметитьРассылкуОтправленнойAsync(
        long идентификатор,
        DateTime? момент = null,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var версия = await ТекущаяВерсияРассылкиAsync(идентификатор, токен).ConfigureAwait(false);

            var ответ = await _транспорт
                .ВыполнитьКомандуAsync<MarkDistributionDispatchedRequest, DistributionDto>(
                    "distributions.mark-dispatched",
                    new MarkDistributionDispatchedRequest(идентификатор, момент, версия),
                    operationId: null,
                    cancellationToken: токен)
                .ConfigureAwait(false);

            return ИзРассылки(ответ);
        });

    /// <inheritdoc />
    public async Task<int> ПересчитатьКонвертыРассылкиAsync(long идентификатор, CancellationToken токен = default)
    {
        var версия = await ТекущаяВерсияРассылкиAsync(идентификатор, токен).ConfigureAwait(false);

        await _транспорт
            .ВыполнитьКомандуAsync<RecalculateEnvelopesRequest, DistributionDto>(
                "distributions.recalculate-envelopes",
                new RecalculateEnvelopesRequest(идентификатор, версия),
                operationId: null,
                токен)
            .ConfigureAwait(false);

        return 1;
    }""",
        """    /// <inheritdoc />
    public Task<Result<Distribution>> СохранитьРассылкуAsync(Distribution рассылка, CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await _транспорт
                .ВыполнитьКомандуAsync<SaveDistributionRequest, DistributionDto>(
                    "distributions.save",
                    new SaveDistributionRequest(рассылка.Id, рассылка.Version),
                    operationId: null,
                    cancellationToken: токен)
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
            var ответ = await _транспорт
                .ВыполнитьКомандуAsync<MarkDistributionDispatchedRequest, DistributionDto>(
                    "distributions.mark-dispatched",
                    new MarkDistributionDispatchedRequest(рассылка.Id, момент, рассылка.Version),
                    operationId: null,
                    cancellationToken: токен)
                .ConfigureAwait(false);

            return ИзРассылки(ответ);
        });

    /// <inheritdoc />
    public async Task<int> ПересчитатьКонвертыРассылкиAsync(Distribution рассылка, CancellationToken токен = default)
    {
        await _транспорт
            .ВыполнитьКомандуAsync<RecalculateEnvelopesRequest, DistributionDto>(
                "distributions.recalculate-envelopes",
                new RecalculateEnvelopesRequest(рассылка.Id, рассылка.Version),
                operationId: null,
                cancellationToken: токен)
            .ConfigureAwait(false);

        var пересчитанная = await ПолучитьРассылкуAsync(рассылка.Id, токен).ConfigureAwait(false);

        return пересчитанная?.EnvelopesRequired ?? 0;
    }""",
    ),
    # ИзменитьРассылкуAsync must present the version the caller read, not a
    # fresh server-side one: re-reading reintroduces last-writer-wins.
    (
        """    /// <inheritdoc />
    public Task<Result<РассылкаПравка>> ИзменитьРассылкуAsync(
        DistributionUpdateRequest запрос,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var версия = await ТекущаяВерсияРассылкиAsync(запрос.DistributionId, токен)
                .ConfigureAwait(false);

            var ответ = await _транспорт
                .ВыполнитьКомандуAsync<UpdateDistributionRequest, UpdateDistributionResponse>(
                    "distributions.update",
                    new ОбновитьРассылку(
                        запрос.DistributionId,
                        запрос.Name,
                        запрос.Kind,
                        запрос.SenderId,
                        запрос.CounterpartyIds,
                        запрос.EnvelopeType,
                        версия),
                    operationId: null,
                    cancellationToken: токен)
                .ConfigureAwait(false);""",
        """    /// <inheritdoc />
    public Task<Result<РассылкаПравка>> ИзменитьРассылкуAsync(
        DistributionUpdateRequest запрос,
        CancellationToken токен = default)
        => ВыполнитьAsync(async () =>
        {
            var ответ = await _транспорт
                .ВыполнитьКомандуAsync<UpdateDistributionRequest, UpdateDistributionResponse>(
                    "distributions.update",
                    запрос,
                    operationId: null,
                    cancellationToken: токен)
                .ConfigureAwait(false);""",
    ),
])

# The ad-hoc alias using can go now that the request is passed as is.
patch("src/Picket302.App/Shared/RemoteDataGateway.cs", [
    (
        """using Picket302.Shared.Commands;
using ОбновитьРассылку = Picket302.Shared.Commands.UpdateDistributionRequest;""",
        """using Picket302.Shared.Commands;""",
    ),
])

print("done")
