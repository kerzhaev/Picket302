using Picket302.Service.Data;
using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Service.Hosting;
using Picket302.Shared.Commands;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Commands;

/// <summary>
/// Сохранение контрагента: создание новой записи либо изменение существующей.
/// </summary>
/// <remarks>
/// Изменение выполняется по правилам <c>CounterpartyService</c> — теми же,
/// что и в одиночном режиме. Проверка версии выполняется до изменения: если
/// запись успели изменить с другого рабочего места, команда отклоняется и
/// ничего не пишет.
/// </remarks>
public sealed class SaveCounterpartyCommand
    : SharedDbCommand<SaveCounterpartyRequest, SaveCounterpartyResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "counterparties.save";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public SaveCounterpartyCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<SaveCounterpartyResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        SaveCounterpartyRequest запрос,
        CancellationToken cancellationToken)
    {
        var новая = запрос.Counterparty.Id <= 0;
        var контрагент = SharedMapping.ИзКонтрагента(запрос.Counterparty);

        if (!новая)
        {
            var увеличено = await область.Версии
                .УвеличитьВерсиюAsync(
                    SharedEntityKind.Counterparty,
                    контрагент.Id,
                    запрос.ExpectedVersion,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!увеличено)
            {
                throw new SharedConflictException(
                    "Запись изменена на другом компьютере. Обновите данные и повторите сохранение.",
                    $"Идентификатор записи: {контрагент.Id}.");
            }
        }

        var результат = await область.Данные.СлужбаКонтрагентов
            .SaveAsync(контрагент, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var сохранённый = результат.Value;

        if (новая)
        {
            await область.Версии
                .СоздатьВерсиюAsync(SharedEntityKind.Counterparty, сохранённый.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Counterparty, сохранённый.Id, cancellationToken)
            .ConfigureAwait(false);

        return new SaveCounterpartyResponse(сохранённый.Id, версия);
    }
}

/// <summary>
/// Мягкое удаление либо восстановление контрагента.
/// </summary>
public sealed class ChangeCounterpartyStateCommand
    : SharedDbCommand<ChangeCounterpartyStateRequest, VersionedResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "counterparties.change-state";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public ChangeCounterpartyStateCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<VersionedResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        ChangeCounterpartyStateRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.Counterparty,
                запрос.Id,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Запись изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор записи: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаКонтрагентов
            .SoftDeleteAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedNotFoundException(результат.Error!);
        }

        if (запрос.Restore)
        {
            await область.Данные.СлужбаМенеджера
                .RestoreAsync(new[] { запрос.Id }, cancellationToken)
                .ConfigureAwait(false);
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Counterparty, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return new VersionedResponse(запрос.Id, версия);
    }
}

/// <summary>
/// Пакетное удаление либо восстановление контрагентов.
/// </summary>
public sealed class BatchChangeCounterpartyStateCommand
    : SharedDbCommand<BatchChangeCounterpartyStateRequest, BatchOperationResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "counterparties.batch-change-state";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public BatchChangeCounterpartyStateCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<BatchOperationResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        BatchChangeCounterpartyStateRequest запрос,
        CancellationToken cancellationToken)
    {
        if (запрос.Items is null || запрос.Items.Count == 0)
        {
            throw new SharedValidationException("Не выбрано ни одной записи.");
        }

        if (запрос.Items.Count > SharedProtocol.MaxIdListLength)
        {
            throw new SharedValidationException("Слишком много записей в одном запросе.");
        }

        var затронуто = new List<long>();

        foreach (var запись in запрос.Items)
        {
            var увеличено = await область.Версии
                .УвеличитьВерсиюAsync(
                    SharedEntityKind.Counterparty,
                    запись.Id,
                    запись.Version,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!увеличено)
            {
                throw new SharedConflictException(
                    "Запись изменена на другом компьютере. Пакетное действие отменено, "
                    + "ни одна запись не изменена.",
                    $"Идентификатор записи: {запись.Id}.");
            }

            затронуто.Add(запись.Id);
        }

        var результат = запрос.Restore
            ? await область.Данные.СлужбаМенеджера
                .RestoreAsync(затронуто, cancellationToken)
                .ConfigureAwait(false)
            : await область.Данные.СлужбаМенеджера
                .FastDeleteMarkAsync(затронуто, cancellationToken)
                .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        return new BatchOperationResponse(затронуто, результат.Value);
    }
}

/// <summary>
/// Пакетное изменение одного поля контрагентов.
/// </summary>
public sealed class BatchUpdateCounterpartyFieldCommand
    : SharedDbCommand<BatchUpdateCounterpartyFieldRequest, BatchOperationResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "counterparties.batch-update-field";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public BatchUpdateCounterpartyFieldCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<BatchOperationResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        BatchUpdateCounterpartyFieldRequest запрос,
        CancellationToken cancellationToken)
    {
        if (запрос.Items is null || запрос.Items.Count == 0)
        {
            throw new SharedValidationException("Не выбрано ни одной записи.");
        }

        var идентификаторы = запрос.Items.Select(з => з.Id).ToArray();

        foreach (var запись in запрос.Items)
        {
            var увеличено = await область.Версии
                .УвеличитьВерсиюAsync(
                    SharedEntityKind.Counterparty,
                    запись.Id,
                    запись.Version,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!увеличено)
            {
                throw new SharedConflictException(
                    "Запись изменена на другом компьютере. Пакетное изменение отменено, "
                    + "ни одна запись не изменена.",
                    $"Идентификатор записи: {запись.Id}.");
            }
        }

        var результат = await область.Данные.СлужбаПакетногоРедактирования
            .BatchUpdateFieldAsync(идентификаторы, запрос.PropertyName, запрос.NewValue, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        return new BatchOperationResponse(идентификаторы, результат.Value);
    }
}

/// <summary>
/// Запись значения пользовательского поля контрагента.
/// </summary>
public sealed class SetCounterpartyFieldCommand
    : SharedDbCommand<SetCounterpartyFieldRequest, CounterpartyFieldResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "counterparties.set-field";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public SetCounterpartyFieldCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<CounterpartyFieldResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        SetCounterpartyFieldRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаПолей
            .SetCustomValueAsync(
                запрос.CounterpartyId,
                запрос.FieldName,
                запрос.Value,
                cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var поле = результат.Value;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.CounterpartyField, поле.Id, cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.CounterpartyField, поле.Id, cancellationToken)
            .ConfigureAwait(false);

        return new CounterpartyFieldResponse(
            поле.Id,
            поле.CounterpartyId,
            поле.FieldName,
            поле.FieldValue,
            поле.FieldType,
            поле.UpdatedAt,
            версия);
    }
}

/// <summary>
/// Удаление значения пользовательского поля контрагента.
/// </summary>
public sealed class RemoveCounterpartyFieldCommand
    : SharedDbCommand<RemoveCounterpartyFieldRequest, VersionedResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "counterparties.remove-field";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public RemoveCounterpartyFieldCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<VersionedResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        RemoveCounterpartyFieldRequest запрос,
        CancellationToken cancellationToken)
    {
        var удалено = await область.Данные.СлужбаПолей
            .RemoveCustomValueAsync(запрос.CounterpartyId, запрос.FieldName, cancellationToken)
            .ConfigureAwait(false);

        if (!удалено)
        {
            throw new SharedNotFoundException("Значение пользовательского поля не найдено.");
        }

        return new VersionedResponse(запрос.CounterpartyId, 0);
    }
}

/// <summary>
/// Создание отправителя.
/// </summary>
public sealed class CreateAgentCommand
    : SharedDbCommand<CreateAgentRequest, CounterpartyResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "counterparties.create-agent";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public CreateAgentCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<CounterpartyResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        CreateAgentRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаАгентов
            .CreateAgentAsync(
                new AgentDto
                {
                    Name = запрос.Name,
                    Type = CounterpartyType.LegalEntity,
                    Inn = запрос.Inn,
                    Kpp = запрос.Kpp,
                    BankName = запрос.BankName,
                    Zip = запрос.Zip,
                    IsSender = true,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var отправитель = результат.Value;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Counterparty, отправитель.Id, cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Counterparty, отправитель.Id, cancellationToken)
            .ConfigureAwait(false);

        return new CounterpartyResponse(
            отправитель.Id,
            отправитель.Type,
            отправитель.Name,
            отправитель.ShortName,
            отправитель.Inn,
            отправитель.Kpp,
            отправитель.IsSender,
            отправитель.IsActive,
            отправитель.CreatedAt,
            отправитель.UpdatedAt,
            версия);
    }
}
