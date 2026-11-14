using Picket302.Application.Services;
using Picket302.Service.Data;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Service.Hosting;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Commands;

/// <summary>
/// Создание отправления в общей базе.
/// </summary>
public sealed class CreateMailCommand : SharedDbCommand<CreateMailRequest, MailDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "mails.create";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public CreateMailCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<MailDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        CreateMailRequest запрос,
        CancellationToken cancellationToken)
    {
        var письмо = SharedMapping.ИзОтправления(запрос.Mail);
        письмо.Id = 0;

        var результат = await область.Данные.СлужбаПисем
            .CreateAsync(письмо, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var созданное = результат.Value;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Mail, созданное.Id, cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Mail, созданное.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КОтправлению(созданное, версия);
    }
}

/// <summary>
/// Смена статуса отправления.
/// </summary>
public sealed class TransitionMailStatusCommand
    : SharedDbCommand<TransitionMailStatusRequest, MailDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "mails.transition-status";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public TransitionMailStatusCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<MailDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        TransitionMailStatusRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Mail, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Отправление изменено на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор отправления: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаПисем
            .TransitionStatusAsync(запрос.Id, запрос.NewStatus, запрос.Moment, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var письмо = результат.Value;
        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Mail, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КОтправлению(письмо, версия);
    }
}

/// <summary>
/// Перенос отправления в папку.
/// </summary>
public sealed class MoveMailToFolderCommand : SharedDbCommand<MoveMailToFolderRequest, MailDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "mails.move-to-folder";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public MoveMailToFolderCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<MailDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        MoveMailToFolderRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Mail, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Отправление изменено на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор отправления: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаПисем
            .MoveToFolderAsync(запрос.Id, запрос.FolderId, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var письмо = результат.Value;
        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Mail, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КОтправлению(письмо, версия);
    }
}

/// <summary>
/// Обновление адреса получателя в непечатанных письмах.
/// </summary>
public sealed class UpdateDraftRecipientAddressCommand
    : SharedDbCommand<UpdateDraftRecipientAddressRequest, UpdateDraftRecipientAddressResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "mails.update-draft-address";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public UpdateDraftRecipientAddressCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<UpdateDraftRecipientAddressResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        UpdateDraftRecipientAddressRequest запрос,
        CancellationToken cancellationToken)
    {
        var адрес = SharedMapping.ИзАдреса(запрос.Address);

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


        var обновлено = await область.Данные.СлужбаКаскада
            .UpdateUnprintedLettersAddressAsync(запрос.CounterpartyId, адрес, cancellationToken)
            .ConfigureAwait(false);

        // Ответ несёт новую версию контрагента, а не число обновлённых
        // писем: клиент предъявляет версию при следующем изменении, и число
        // писем в этом поле дало бы ложный конфликт.
        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Counterparty, запрос.CounterpartyId, cancellationToken)
            .ConfigureAwait(false);

        return new UpdateDraftRecipientAddressResponse(обновлено, версия);
    }
}

/// <summary>
/// Выдача очередного регистрационного номера отправлению.
/// </summary>
/// <remarks>
/// Номер выдаётся службой в той же транзакции, в которой он записывается в
/// отправление. Два параллельных клиента не могут получить один номер.
/// </remarks>
public sealed class AssignMailNumberCommand : SharedDbCommand<AssignMailNumberRequest, MailDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "mails.assign-number";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public AssignMailNumberCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<MailDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        AssignMailNumberRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Mail, запрос.MailId, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Отправление изменено на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор отправления: {запрос.MailId}.");
        }

        var письмо = await область.Данные.Письма
            .GetByIdAsync(запрос.MailId, cancellationToken)
            .ConfigureAwait(false);

        if (письмо is null)
        {
            throw new SharedNotFoundException("Отправление не найдено.");
        }

        var номер = await область.Данные.СлужбаНумерации
            .GenerateNextOutgoingNumberAsync(запрос.TemplateMask, запрос.Moment, cancellationToken)
            .ConfigureAwait(false);

        письмо.OutgoingNumber = номер;

        var затронуто = await область.Данные.Письма
            .UpdateAsync(письмо, cancellationToken)
            .ConfigureAwait(false);

        if (затронуто == 0)
        {
            throw new SharedNotFoundException("Отправление не найдено.");
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Mail, запрос.MailId, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КОтправлению(письмо, версия);
    }
}

/// <summary>
/// Формирование рассылки по плану.
/// </summary>
/// <remarks>
/// Рассылка и её письма создаются одной командой и одной транзакцией: клиент
/// не может получить «наполовину» рассылку.
/// </remarks>
public sealed class GenerateDistributionCommand : SharedDbCommand<GenerateDistributionRequest, DistributionDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "distributions.generate";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public GenerateDistributionCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<DistributionDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        GenerateDistributionRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаРассылок
            .GenerateDistributionAsync(запрос.Plan, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var рассылка = результат.Value;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        var письма = await область.Данные.Письма
            .GetByDistributionAsync(рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var письмо in письма)
        {
            await область.Версии
                .СоздатьВерсиюAsync(SharedEntityKind.Mail, письмо.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КРассылке(рассылка, версия, письма.Count);
    }
}

/// <summary>
/// Создание рассылки по выбранным контрагентам.
/// </summary>
public sealed class CreateDistributionFromSelectionCommand
    : SharedDbCommand<CreateDistributionFromSelectionRequest, DistributionDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "distributions.create-from-selection";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public CreateDistributionFromSelectionCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<DistributionDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        CreateDistributionFromSelectionRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаРассылок
            .CreateFromSelectionAsync(
                запрос.CounterpartyIds,
                запрос.SenderId,
                запрос.Name,
                запрос.EnvelopeType,
                cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var рассылка = результат.Value;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        var письма = await область.Данные.Письма
            .GetByDistributionAsync(рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var письмо in письма)
        {
            await область.Версии
                .СоздатьВерсиюAsync(SharedEntityKind.Mail, письмо.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КРассылке(рассылка, версия, письма.Count);
    }
}

/// <summary>
/// Клонирование рассылки для повторной отправки.
/// </summary>
public sealed class CloneDistributionCommand : SharedDbCommand<CloneDistributionRequest, CloneDistributionResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "distributions.clone-for-resend";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public CloneDistributionCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<CloneDistributionResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        CloneDistributionRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаРассылок
            .CloneForResendAsync(запрос.DistributionId, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var клон = результат.Value;
        var рассылка = клон.Distribution;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        var письма = await область.Данные.Письма
            .GetByDistributionAsync(рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var письмо in письма)
        {
            await область.Версии
                .СоздатьВерсиюAsync(SharedEntityKind.Mail, письмо.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        return new CloneDistributionResponse(
            SharedMapping.КРассылке(рассылка, версия, письма.Count),
            клон.LettersCreated,
            клон.SkippedCounterparties);
    }
}

/// <summary>
/// Правка сохранённой рассылки.
/// </summary>
public sealed class UpdateDistributionCommand : SharedDbCommand<UpdateDistributionRequest, UpdateDistributionResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "distributions.update";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public UpdateDistributionCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<UpdateDistributionResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        UpdateDistributionRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
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
            .UpdateDistributionAsync(
                new DistributionUpdateRequest(
                    запрос.DistributionId,
                    запрос.Name,
                    запрос.Kind,
                    запрос.SenderId,
                    запрос.CounterpartyIds,
                    запрос.EnvelopeType),
                cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var правка = результат.Value;
        var рассылка = правка.Distribution;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        var письма = await область.Данные.Письма
            .GetByDistributionAsync(рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        foreach (var письмо in письма)
        {
            await область.Версии
                .СоздатьВерсиюAsync(SharedEntityKind.Mail, письмо.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        return new UpdateDistributionResponse(
            SharedMapping.КРассылке(рассылка, версия, письма.Count),
            правка.LettersAdded,
            правка.LettersRemoved,
            правка.SkippedCounterparties);
    }
}

/// <summary>
/// Перевод рассылки из черновика в сохранённую.
/// </summary>
public sealed class SaveDistributionCommand : SharedDbCommand<SaveDistributionRequest, DistributionDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "distributions.save";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public SaveDistributionCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<DistributionDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        SaveDistributionRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
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
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var рассылка = результат.Value;
        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        var письма = await область.Данные.Письма
            .GetByDistributionAsync(рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КРассылке(рассылка, версия, письма.Count);
    }
}

/// <summary>
/// Отметка рассылки как отправленной.
/// </summary>
public sealed class MarkDistributionDispatchedCommand
    : SharedDbCommand<MarkDistributionDispatchedRequest, DistributionDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "distributions.mark-dispatched";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public MarkDistributionDispatchedCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<DistributionDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        MarkDistributionDispatchedRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
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
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var рассылка = результат.Value;
        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Distribution, рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        var письма = await область.Данные.Письма
            .GetByDistributionAsync(рассылка.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КРассылке(рассылка, версия, письма.Count);
    }
}

/// <summary>
/// Пересчёт требуемого числа конвертов рассылки.
/// </summary>
public sealed class RecalculateEnvelopesCommand
    : SharedDbCommand<RecalculateEnvelopesRequest, DistributionDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "distributions.recalculate-envelopes";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public RecalculateEnvelopesCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<DistributionDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        RecalculateEnvelopesRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
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
            .ConfigureAwait(false);

        var рассылка = await область.Данные.Рассылки
            .GetByIdAsync(запрос.DistributionId, cancellationToken)
            .ConfigureAwait(false);

        if (рассылка is null)
        {
            throw new SharedNotFoundException("Рассылка не найдена.");
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Distribution, запрос.DistributionId, cancellationToken)
            .ConfigureAwait(false);

        var письма = await область.Данные.Письма
            .GetByDistributionAsync(запрос.DistributionId, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КРассылке(рассылка, версия, письма.Count);
    }
}
