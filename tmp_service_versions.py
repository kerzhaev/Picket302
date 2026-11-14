import io

SVC = "src/Picket302.Service/Commands/"


def patch(path, edits):
    s = io.open(path, encoding="utf-8").read()
    for old, new in edits:
        if old not in s:
            raise SystemExit("NOT FOUND in %s:\n%s" % (path, old[:200]))
        s = s.replace(old, new, 1)
    io.open(path, "w", encoding="utf-8", newline="\n").write(s)
    print("patched", path)


# --- Custom fields: version check -------------------------------------------------
patch(SVC + "CounterpartyCommands.cs", [
    (
        """        var результат = await область.Данные.СлужбаПолей
            .SetCustomValueAsync(
                запрос.CounterpartyId,
                запрос.FieldName,
                запрос.Value,
                cancellationToken)
            .ConfigureAwait(false);""",
        """        // Поле может быть новым (версия 0) или уже существующим: при
        // существующем поле версия должна совпасть, иначе перезапись чужого
        // значения отклоняется.
        var существующее = await область.Данные.ПоляКонтрагентов
            .GetByCounterpartyAsync(запрос.CounterpartyId, cancellationToken)
            .ConfigureAwait(false);

        var прежнее = существующее
            .FirstOrDefault(поле => поле.FieldName == запрос.FieldName);

        if (прежнее is not null)
        {
            var увеличено = await область.Версии
                .УвеличитьВерсиюAsync(
                    SharedEntityKind.CounterpartyField,
                    прежнее.Id,
                    запрос.ExpectedVersion,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!увеличено)
            {
                throw new SharedConflictException(
                    "Значение поля изменено на другом компьютере. Обновите данные и повторите действие.",
                    $"Поле «{запрос.FieldName}» контрагента {запрос.CounterpartyId}.");
            }
        }
        else if (запрос.ExpectedVersion != 0)
        {
            throw new SharedConflictException(
                "Поле не найдено на сервере: возможно, его удалили на другом компьютере.",
                $"Поле «{запрос.FieldName}» контрагента {запрос.CounterpartyId}.");
        }

        var результат = await область.Данные.СлужбаПолей
            .SetCustomValueAsync(
                запрос.CounterpartyId,
                запрос.FieldName,
                запрос.Value,
                cancellationToken)
            .ConfigureAwait(false);""",
    ),
    (
        """        var удалено = await область.Данные.СлужбаПолей
            .RemoveCustomValueAsync(запрос.CounterpartyId, запрос.FieldName, cancellationToken)
            .ConfigureAwait(false);""",
        """        var поля = await область.Данные.ПоляКонтрагентов
            .GetByCounterpartyAsync(запрос.CounterpartyId, cancellationToken)
            .ConfigureAwait(false);

        var прежнее = поля.FirstOrDefault(поле => поле.FieldName == запрос.FieldName);

        if (прежнее is null)
        {
            throw new SharedNotFoundException("Значение пользовательского поля не найдено.");
        }

        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.CounterpartyField,
                прежнее.Id,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Значение поля изменено на другом компьютере. Обновите данные и повторите действие.",
                $"Поле «{запрос.FieldName}» контрагента {запрос.CounterpartyId}.");
        }

        var удалено = await область.Данные.СлужбаПолей
            .RemoveCustomValueAsync(запрос.CounterpartyId, запрос.FieldName, cancellationToken)
            .ConfigureAwait(false);""",
    ),
])

# --- Move counterparty: version check ----------------------------------------------
patch(SVC + "GroupFolderCommands.cs", [
    (
        """        var результат = await область.Данные.СлужбаКатегорий
            .MoveCounterpartyAsync(
                запрос.CounterpartyId,
                запрос.CategoryId,
                cancellationToken)
            .ConfigureAwait(false);""",
        """        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.Counterparty,
                запрос.CounterpartyId,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Контрагент изменён на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор контрагента: {запрос.CounterpartyId}.");
        }

        var результат = await область.Данные.СлужбаКатегорий
            .MoveCounterpartyAsync(
                запрос.CounterpartyId,
                запрос.CategoryId,
                cancellationToken)
            .ConfigureAwait(false);""",
    ),
])

# --- Draft address and distributions: version checks --------------------------------
patch(SVC + "MailDistributionCommands.cs", [
    (
        """        var адрес = SharedMapping.ИзАдреса(запрос.Address);

        if (адрес is null)
        {
            throw new SharedValidationException("Адрес получателя не задан.");
        }
""",
        """        var адрес = SharedMapping.ИзАдреса(запрос.Address);

        if (адрес is null)
        {
            throw new SharedValidationException("Адрес получателя не задан.");
        }

        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.Counterparty,
                запрос.CounterpartyId,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Карточка получателя изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор контрагента: {запрос.CounterpartyId}.");
        }
""",
    ),
    (
        """        var результат = await область.Данные.СлужбаРассылок
            .UpdateDistributionAsync(""",
        """        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.Distribution,
                запрос.DistributionId,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Рассылка изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор рассылки: {запрос.DistributionId}.");
        }

        var результат = await область.Данные.СлужбаРассылок
            .UpdateDistributionAsync(""",
    ),
    (
        """        var результат = await область.Данные.СлужбаРассылок
            .SaveAsync(запрос.DistributionId, cancellationToken)
            .ConfigureAwait(false);""",
        """        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.Distribution,
                запрос.DistributionId,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Рассылка изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор рассылки: {запрос.DistributionId}.");
        }

        var результат = await область.Данные.СлужбаРассылок
            .SaveAsync(запрос.DistributionId, cancellationToken)
            .ConfigureAwait(false);""",
    ),
    (
        """        var результат = await область.Данные.СлужбаРассылок
            .MarkDispatchedAsync(запрос.DistributionId, запрос.Moment, cancellationToken)
            .ConfigureAwait(false);""",
        """        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.Distribution,
                запрос.DistributionId,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Рассылка изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор рассылки: {запрос.DistributionId}.");
        }

        var результат = await область.Данные.СлужбаРассылок
            .MarkDispatchedAsync(запрос.DistributionId, запрос.Moment, cancellationToken)
            .ConfigureAwait(false);""",
    ),
    (
        """        await область.Данные.СлужбаРассылок
            .RecalculateEnvelopesRequiredAsync(запрос.DistributionId, cancellationToken)
            .ConfigureAwait(false);""",
        """        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.Distribution,
                запрос.DistributionId,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Рассылка изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор рассылки: {запрос.DistributionId}.");
        }

        await область.Данные.СлужбаРассылок
            .RecalculateEnvelopesRequiredAsync(запрос.DistributionId, cancellationToken)
            .ConfigureAwait(false);""",
    ),
])
print("done")
