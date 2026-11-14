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
/// Сохранение шаблона конверта или бланка.
/// </summary>
public sealed class SavePrintTemplateCommand : SharedDbCommand<SavePrintTemplateRequest, PrintTemplateDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "templates.save";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public SavePrintTemplateCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<PrintTemplateDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        SavePrintTemplateRequest запрос,
        CancellationToken cancellationToken)
    {
        var шаблон = SharedMapping.ИзШаблона(запрос.Template);
        var новый = шаблон.Id <= 0;

        if (!новый)
        {
            var увеличено = await область.Версии
                .УвеличитьВерсиюAsync(
                    SharedEntityKind.PrintTemplate,
                    шаблон.Id,
                    запрос.ExpectedVersion,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!увеличено)
            {
                throw new SharedConflictException(
                    "Шаблон изменён на другом компьютере. Обновите данные и повторите сохранение.",
                    $"Идентификатор шаблона: {шаблон.Id}.");
            }

            var затронуто = await область.Данные.ШаблоныПечати
                .UpdateAsync(шаблон, cancellationToken)
                .ConfigureAwait(false);

            if (затронуто == 0)
            {
                throw new SharedNotFoundException("Шаблон не найден.");
            }
        }
        else
        {
            шаблон.Id = await область.Данные.ШаблоныПечати
                .AddAsync(шаблон, cancellationToken)
                .ConfigureAwait(false);
        }

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.PrintTemplate, шаблон.Id, cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.PrintTemplate, шаблон.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КШаблону(шаблон, версия, null);
    }
}

/// <summary>
/// Удаление шаблона.
/// </summary>
public sealed class DeletePrintTemplateCommand : SharedDbCommand<DeletePrintTemplateRequest, VersionedResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "templates.delete";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public DeletePrintTemplateCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<VersionedResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        DeletePrintTemplateRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.PrintTemplate,
                запрос.Id,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Шаблон изменён на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор шаблона: {запрос.Id}.");
        }

        var удалён = await область.Данные.ШаблоныПечати
            .DeleteAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!удалён)
        {
            throw new SharedNotFoundException("Шаблон не найден.");
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.PrintTemplate, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return new VersionedResponse(запрос.Id, версия);
    }
}

/// <summary>
/// Сохранение поля бланка.
/// </summary>
public sealed class SaveBlankFieldCommand : SharedDbCommand<SaveBlankFieldRequest, BlankFieldDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "template-fields.save";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public SaveBlankFieldCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<BlankFieldDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        SaveBlankFieldRequest запрос,
        CancellationToken cancellationToken)
    {
        var поле = SharedMapping.ИзПоляБланка(запрос.Field);
        var новое = поле.Id <= 0;

        if (!новое)
        {
            var увеличено = await область.Версии
                .УвеличитьВерсиюAsync(
                    SharedEntityKind.BlankField,
                    поле.Id,
                    запрос.ExpectedVersion,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!увеличено)
            {
                throw new SharedConflictException(
                    "Поле бланка изменено на другом компьютере. Обновите данные и повторите сохранение.",
                    $"Идентификатор поля: {поле.Id}.");
            }

            var затронуто = await область.Данные.ПоляШаблонов
                .UpdateAsync(поле, cancellationToken)
                .ConfigureAwait(false);

            if (затронуто == 0)
            {
                throw new SharedNotFoundException("Поле бланка не найдено.");
            }
        }
        else
        {
            поле.Id = await область.Данные.ПоляШаблонов
                .AddAsync(поле, cancellationToken)
                .ConfigureAwait(false);
        }

        await область.Версии
            .СоздатьВерсиюAsync(SharedEntityKind.BlankField, поле.Id, cancellationToken)
            .ConfigureAwait(false);

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.BlankField, поле.Id, cancellationToken)
            .ConfigureAwait(false);

        return SharedMapping.КПолюБланка(поле, версия);
    }
}

/// <summary>
/// Удаление поля бланка.
/// </summary>
public sealed class DeleteBlankFieldCommand : SharedDbCommand<DeleteBlankFieldRequest, VersionedResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "template-fields.delete";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public DeleteBlankFieldCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<VersionedResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        DeleteBlankFieldRequest запрос,
        CancellationToken cancellationToken)
    {
        var увеличено = await область.Версии
            .УвеличитьВерсиюAsync(
                SharedEntityKind.BlankField,
                запрос.Id,
                запрос.ExpectedVersion,
                cancellationToken)
            .ConfigureAwait(false);

        if (!увеличено)
        {
            throw new SharedConflictException(
                "Поле бланка изменено на другом компьютере. Обновите данные и повторите действие.",
                $"Идентификатор поля: {запрос.Id}.");
        }

        var удалено = await область.Данные.ПоляШаблонов
            .DeleteAsync(запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!удалено)
        {
            throw new SharedNotFoundException("Поле бланка не найдено.");
        }

        var версия = await область.Версии
            .ПолучитьВерсиюAsync(SharedEntityKind.BlankField, запрос.Id, cancellationToken)
            .ConfigureAwait(false);

        return new VersionedResponse(запрос.Id, версия);
    }
}

/// <summary>
/// Фиксация печатного события в общем журнале.
/// </summary>
/// <remarks>
/// Событие приходит от клиента, который напечатал на своём рабочем месте:
/// служба не печатает и не знает, что напечатано физически. Исходы
/// «подготовлено», «передано в локальный спулер», «ошибка» и «неизвестный
/// исход после обрыва» различаются и не смешиваются.
/// </remarks>
public sealed class RecordPrintEventCommand : SharedDbCommand<RecordPrintEventRequest, PrintEventResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "print.record-event";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public RecordPrintEventCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<PrintEventResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        RecordPrintEventRequest запрос,
        CancellationToken cancellationToken)
    {
        if (запрос.MailIds is null || запрос.MailIds.Count == 0)
        {
            throw new SharedValidationException("Не выбрано ни одного отправления.");
        }

        var исход = запрос.Outcome.ToString();

        foreach (var идентификатор in запрос.MailIds)
        {
            var результат = await область.Данные.СлужбаПисем
                .RegisterPrintAsync(
                    идентификатор,
                    null,
                    запрос.TemplateDescription,
                    cancellationToken,
                    исход,
                    контекст.DeviceId)
                .ConfigureAwait(false);

            if (результат.IsFailure)
            {
                throw new SharedValidationException(результат.Error!);
            }
        }

        return new PrintEventResponse(запрос.MailIds, null, запрос.Outcome);
    }
}

/// <summary>
/// Запись в журнал email-рассылки.
/// </summary>
public sealed class WriteEmailLogCommand : SharedDbCommand<WriteEmailLogRequest, WriteEmailLogResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "email-log.write";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public WriteEmailLogCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<WriteEmailLogResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        WriteEmailLogRequest запрос,
        CancellationToken cancellationToken)
    {
        var писатель = new Picket302.Infrastructure.Mail.SqliteEmailLogWriter(область.Единица);

        var записано = 0;

        foreach (var запись in запрос.Entries)
        {
            await писатель.WriteAsync(
                запись.Recipient,
                запись.Status,
                запись.SmtpCode,
                запись.ServerResponse,
                запись.Error,
                запись.SentAtUtc,
                cancellationToken).ConfigureAwait(false);

            записано++;
        }

        return new WriteEmailLogResponse(записано);
    }
}

/// <summary>
/// Слияние импорта адресной книги: записи пакета сливаются с общими данными
/// по правилам <see cref="MergePolicy"/>.
/// </summary>
/// <remarks>
/// Разбор файла остаётся на клиенте, а слияние выполняется службой теми же
/// правилами <c>ImportMergeService</c>, что и в одиночном режиме.
/// </remarks>
public sealed class ImportMergeCommand : SharedDbCommand<ImportMergeRequest, ImportMergeResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "import.merge";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public ImportMergeCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<ImportMergeResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        ImportMergeRequest запрос,
        CancellationToken cancellationToken)
    {
        var пакет = new ImportBatch
        {
            SourceFormat = запрос.SourceFormat,
            SourceFile = запрос.SourceFile,
            ImportedAt = запрос.ImportedAt,
            Records = запрос.Records.ToList(),
        };

        var слияние = new ImportMergeService(
            область.Данные.Контрагенты,
            область.Данные.ПоляКонтрагентов);

        var исходы = await слияние
            .MergeImportedDetailed(пакет, запрос.Policy, cancellationToken)
            .ConfigureAwait(false);

        var созданных = 0;
        var обновлённых = 0;
        var конфликтов = 0;

        foreach (var исход in исходы)
        {
            switch (исход.Action)
            {
                case ImportMergeAction.Created:
                    созданных++;
                    break;

                case ImportMergeAction.Updated:
                    обновлённых++;
                    break;

                case ImportMergeAction.Skipped:
                    break;

                default:
                    if (исход.HasConflict)
                    {
                        конфликтов++;
                    }

                    break;
            }
        }

        return new ImportMergeResponse(созданных, обновлённых, исходы.Count - созданных - обновлённых, конфликтов);
    }
}

/// <summary>
/// Выборочная очистка общей базы.
/// </summary>
public sealed class ClearDatabaseCommand : SharedDbCommand<ClearDatabaseRequest, ClearDatabaseResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "database.clear";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public ClearDatabaseCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<ClearDatabaseResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        ClearDatabaseRequest запрос,
        CancellationToken cancellationToken)
    {
        var результат = await область.Данные.СлужбаСидов
            .ClearDatabaseAsync(запрос.Scope, cancellationToken)
            .ConfigureAwait(false);

        if (результат.IsFailure)
        {
            throw new SharedValidationException(результат.Error!);
        }

        return new ClearDatabaseResponse(результат.Value);
    }
}

/// <summary>
/// Наполнение общей базы демонстрационными данными.
/// </summary>
public sealed class SeedDemoCommand : SharedDbCommand<SeedDemoRequest, SeedDemoResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "database.seed-demo";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    public SeedDemoCommand(ServerDatabase база)
        : base(база)
    {
    }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<SeedDemoResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        SeedDemoRequest запрос,
        CancellationToken cancellationToken)
    {
        var создано = await область.Данные.СлужбаСидов
            .SeedDemoDataAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SeedDemoResponse(создано);
    }
}
