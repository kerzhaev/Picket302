using Picket302.Service.Data;
using Picket302.Domain.Entities;
using Picket302.Service.Hosting;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Commands;

/// <summary>
/// Создание группы адресной книги.
/// </summary>
public sealed class CreateGroupCommand : SharedDbCommand<CreateGroupRequest, GroupDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "groups.create";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public CreateGroupCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<GroupDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        CreateGroupRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаМенеджераГрупп
            .CreateGroupAsync(запрос.Name, запрос.IsDefault, запрос.IsClosed, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var группа = результат.Value;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Group, группа.Id, cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Group, группа.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КГруппе(группа, версия);
    }
}

/// <summary>
/// Переименование группы.
/// </summary>
public sealed class RenameGroupCommand : SharedDbCommand<RenameGroupRequest, GroupDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "groups.rename";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public RenameGroupCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<GroupDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        RenameGroupRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Группа изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор группы: {запрос.Id}.");
        }

        var группа = await область.Данные.Группы
            .GetByIdAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (группа is null)
        {
            throw new SharedNotFoundException("Группа не найдена.");
        }

        группа.Name = запрос.Name;

        var затронуто = await область.Данные.Группы
            .UpdateAsync(группа, cancellationToken)
            .ConfigureAwait(false);

        if (затронуто == 0)
        {
            throw new SharedNotFoundException("Группа не найдена.");
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КГруппе(группа, версия);
    }
}

/// <summary>
/// Удаление группы.
/// </summary>
public sealed class DeleteGroupCommand : SharedDbCommand<DeleteGroupRequest, VersionedResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "groups.delete";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public DeleteGroupCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<VersionedResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        DeleteGroupRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Группа изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор группы: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаМенеджераГрупп
            .DeleteGroupAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return new VersionedResponse(запрос.Id, версия);
    }
}

/// <summary>
/// Назначение группы по умолчанию.
/// </summary>
public sealed class SetDefaultGroupCommand : SharedDbCommand<SetDefaultGroupRequest, GroupDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "groups.set-default";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public SetDefaultGroupCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<GroupDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        SetDefaultGroupRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Группа изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор группы: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаГрупп
            .SetDefaultAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var группа = результат.Value;
        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КГруппе(группа, версия);
    }
}

/// <summary>
/// Создание категории дерева адресной книги.
/// </summary>
public sealed class CreateCategoryCommand : SharedDbCommand<CreateCategoryRequest, GroupDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "groups.create-category";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public CreateCategoryCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<GroupDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        CreateCategoryRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаКатегорий
            .CreateCategoryAsync(запрос.Name, запрос.ParentId, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var категория = результат.Value;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Group, категория.Id, cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Group, категория.Id, cancellationToken)
            .ConfigureAwait(false);

        var группа = await область.Данные.Группы
            .GetByIdAsync(категория.Id, cancellationToken)
            .ConfigureAwait(false);

        if (группа is null)
        {
            throw new SharedNotFoundException("Категория не найдена.");
        }

        return SharedMapping.КГруппе(группа, версия);
    }
}

/// <summary>
/// Переименование категории.
/// </summary>
public sealed class RenameCategoryCommand : SharedDbCommand<RenameCategoryRequest, GroupDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "groups.rename-category";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public RenameCategoryCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<GroupDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        RenameCategoryRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Категория изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор категории: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаКатегорий
            .RenameCategoryAsync(запрос.Id, запрос.Name, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var группа = await область.Данные.Группы
            .GetByIdAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (группа is null)
        {
            throw new SharedNotFoundException("Категория не найдена.");
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КГруппе(группа, версия);
    }
}

/// <summary>
/// Удаление категории.
/// </summary>
public sealed class DeleteCategoryCommand : SharedDbCommand<DeleteCategoryRequest, VersionedResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "groups.delete-category";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public DeleteCategoryCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<VersionedResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        DeleteCategoryRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Категория изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор категории: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаКатегорий
            .DeleteCategoryAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Group, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return new VersionedResponse(запрос.Id, версия);
    }
}

/// <summary>
/// Перенос контрагента в другую категорию.
/// </summary>
public sealed class MoveCounterpartyCommand : SharedDbCommand<MoveCounterpartyRequest, VersionedResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "groups.move-counterparty";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public MoveCounterpartyCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<VersionedResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        MoveCounterpartyRequest запрос,
        CancellationToken cancellationToken)
    {
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
                "Контрагент изменён на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор контрагента: {запрос.CounterpartyId}.");
        }

        var результат = await область.Данные.СлужбаКатегорий
            .MoveCounterpartyAsync(запрос.CounterpartyId, запрос.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        await область.Версии
            .СоздатьВерсиюAsync(
                SharedEntityKind.Counterparty,
                запрос.CounterpartyId,
                cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(
                SharedEntityKind.Counterparty,
                запрос.CounterpartyId,
                cancellationToken)
            .ConfigureAwait(false);

        return new VersionedResponse(запрос.CounterpartyId, версия);
    }
}

/// <summary>
/// Создание папки отправлений.
/// </summary>
public sealed class CreateFolderCommand : SharedDbCommand<CreateFolderRequest, FolderDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "folders.create";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public CreateFolderCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<FolderDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        CreateFolderRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаПапок
            .CreateAsync(запрос.Name, запрос.ParentId, запрос.Note, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var папка = результат.Value;

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.Folder, папка.Id, cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Folder, папка.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КПапке(папка, версия);
    }
}

/// <summary>
/// Переименование папки.
/// </summary>
public sealed class RenameFolderCommand : SharedDbCommand<RenameFolderRequest, FolderDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "folders.rename";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public RenameFolderCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<FolderDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        RenameFolderRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Folder, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Папка изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор папки: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаПапок
            .RenameAsync(запрос.Id, запрос.Name, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var папка = результат.Value;
        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Folder, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КПапке(папка, версия);
    }
}

/// <summary>
/// Перенос папки.
/// </summary>
public sealed class MoveFolderCommand : SharedDbCommand<MoveFolderRequest, FolderDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "folders.move";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public MoveFolderCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<FolderDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        MoveFolderRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Folder, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Папка изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор папки: {запрос.Id}.");
        }

        var результат = await область.Данные.СлужбаПапок
            .MoveAsync(запрос.Id, запрос.ParentId, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        var папка = результат.Value;
        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Folder, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КПапке(папка, версия);
    }
}

/// <summary>
/// Удаление папки.
/// </summary>
public sealed class DeleteFolderCommand : SharedDbCommand<DeleteFolderRequest, VersionedResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "folders.delete";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public DeleteFolderCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<VersionedResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        DeleteFolderRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(SharedEntityKind.Folder, запрос.Id, запрос.ExpectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Папка изменена на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор папки: {запрос.Id}.");
        }

        var удалена = await область.Данные.Папки
            .DeleteAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!удалена)
        {
            throw new SharedNotFoundException("Папка не найдена.");
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.Folder, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return new VersionedResponse(запрос.Id, версия);
    }
}
