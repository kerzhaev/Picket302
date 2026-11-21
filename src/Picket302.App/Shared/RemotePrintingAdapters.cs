using System.Text.Json;
using Picket302.Domain.Common;
using Picket302.Domain.Dictionaries;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Printing;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Picket302.Shared.Protocol;

namespace Picket302.App.Shared;

/// <summary>
/// Шаблоны печати в общей базе.
/// </summary>
public sealed class RemotePrintTemplateRepository : IPrintTemplateRepository
{
    private readonly SharedTransport _транспорт;
    private readonly PendingOperationsStore _незавершённые;
    private readonly SharedTemplatePackageResolver? _разрешитель;

    /// <summary>
    /// Создаёт адаптер шаблонов печати.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    /// <param name="незавершённые">Хранилище незавершённых операций.</param>
    /// <param name="разрешитель">
    /// Получатель содержимого пакета шаблона; пусто — содержимое не
    /// запрашивается (проверки без сети).
    /// </param>
    public RemotePrintTemplateRepository(
        SharedTransport транспорт,
        PendingOperationsStore незавершённые,
        SharedTemplatePackageResolver? разрешитель = null)
    {
        ArgumentNullException.ThrowIfNull(транспорт);
        ArgumentNullException.ThrowIfNull(незавершённые);

        _транспорт = транспорт;
        _незавершённые = незавершённые;
        _разрешитель = разрешитель;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(PrintTemplate template, CancellationToken cancellationToken = default)
    {
        var ответ = await _незавершённые
            .ВыполнитьAsync<PrintTemplateDto>(
                _транспорт,
                "templates.save",
                JsonSerializer.Serialize(
                    new SavePrintTemplateRequest(
                        SharedMapping.КШаблону(template, template.Version, null),
                        template.Version),
                    SharedJson.Options),
                cancellationToken)
            .ConfigureAwait(false);

        return ответ.Id;
    }

    /// <inheritdoc />
    public async Task<PrintTemplate?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var шаблоны = await GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var шаблон = шаблоны.FirstOrDefault(запись => запись.Id == id);

        if (шаблон is null)
        {
            return null;
        }

        // Содержимое макета получается по привязке к пакету: путь к файлу на
        // сервере клиенту не передаётся. Версия, полученная от сервера,
        // проверяется по отпечатку — печать по чужой версии недопустима.
        if (_разрешитель is not null)
        {
            var путь = await _разрешитель
                .РазрешитьAsync(
                    шаблон,
                    (идентификатор, версия, токен) => _транспорт
                        .СкачатьПакетШаблонаAsync(идентификатор, версия, токен),
                    cancellationToken)
                .ConfigureAwait(false);

            if (путь is not null)
            {
                шаблон.FilePath = путь;
            }
        }

        return шаблон;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintTemplate>> GetAllAsync(
        string? kind = null,
        CancellationToken cancellationToken = default)
    {
        var шаблоны = await _транспорт
            .ВыполнитьЗапросAsync<PrintTemplateDto[]>("/api/v1/queries/templates", cancellationToken)
            .ConfigureAwait(false);

        return шаблоны
            .Where(шаблон => kind is null || string.Equals(шаблон.Kind, kind, StringComparison.Ordinal))
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
                PackageId = string.IsNullOrWhiteSpace(шаблон.PackageId) ? null : шаблон.PackageId,
                PackageVersion = шаблон.PackageVersion > 0 ? шаблон.PackageVersion : null,
                PackageHash = string.IsNullOrWhiteSpace(шаблон.ContentHash) ? null : шаблон.ContentHash,
            })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(PrintTemplate template, CancellationToken cancellationToken = default)
    {
        await _незавершённые
            .ВыполнитьAsync<PrintTemplateDto>(
                _транспорт,
                "templates.save",
                JsonSerializer.Serialize(
                    new SavePrintTemplateRequest(
                        SharedMapping.КШаблону(template, template.Version, null),
                        template.Version),
                    SharedJson.Options),
                cancellationToken)
            .ConfigureAwait(false);

        return 1;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var шаблоны = await GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var шаблон = шаблоны.FirstOrDefault(запись => запись.Id == id);

        if (шаблон is null)
        {
            return false;
        }

        await _незавершённые
            .ВыполнитьAsync<VersionedResponse>(
                _транспорт,
                "templates.delete",
                JsonSerializer.Serialize(
                    new DeletePrintTemplateRequest(id, шаблон.Version),
                    SharedJson.Options),
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}

/// <summary>
/// Поля бланка в общей базе.
/// </summary>
public sealed class RemoteBlankFieldRepository : IBlankFieldRepository
{
    private readonly SharedTransport _транспорт;
    private readonly PendingOperationsStore _незавершённые;

    /// <summary>
    /// Создаёт адаптер полей бланка.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    /// <param name="незавершённые">Хранилище незавершённых операций.</param>
    public RemoteBlankFieldRepository(SharedTransport транспорт, PendingOperationsStore незавершённые)
    {
        ArgumentNullException.ThrowIfNull(транспорт);
        ArgumentNullException.ThrowIfNull(незавершённые);

        _транспорт = транспорт;
        _незавершённые = незавершённые;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(BlankField field, CancellationToken cancellationToken = default)
    {
        var ответ = await _незавершённые
            .ВыполнитьAsync<BlankFieldDto>(
                _транспорт,
                "template-fields.save",
                JsonSerializer.Serialize(
                    new SaveBlankFieldRequest(
                        SharedMapping.КПолюБланка(field, field.Version),
                        field.Version),
                    SharedJson.Options),
                cancellationToken)
            .ConfigureAwait(false);

        return ответ.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlankField>> GetByTemplateAsync(
        long templateId,
        CancellationToken cancellationToken = default)
    {
        var поля = await _транспорт
            .ВыполнитьЗапросAsync<BlankFieldDto[]>(
                "/api/v1/queries/template-fields?templateId=" + templateId,
                cancellationToken)
            .ConfigureAwait(false);

        return поля
            .Select(поле => new BlankField
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
    public async Task<int> UpdateAsync(BlankField field, CancellationToken cancellationToken = default)
    {
        await _незавершённые
            .ВыполнитьAsync<BlankFieldDto>(
                _транспорт,
                "template-fields.save",
                JsonSerializer.Serialize(
                    new SaveBlankFieldRequest(
                        SharedMapping.КПолюБланка(field, field.Version),
                        field.Version),
                    SharedJson.Options),
                cancellationToken)
            .ConfigureAwait(false);

        return 1;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var поля = await _транспорт
            .ВыполнитьЗапросAsync<BlankFieldDto[]>("/api/v1/queries/template-fields?templateId=0", cancellationToken)
            .ConfigureAwait(false);

        var поле = поля.FirstOrDefault(запись => запись.Id == id);

        if (поле is null)
        {
            return false;
        }

        await _незавершённые
            .ВыполнитьAsync<VersionedResponse>(
                _транспорт,
                "template-fields.delete",
                JsonSerializer.Serialize(
                    new DeleteBlankFieldRequest(id, поле.Version),
                    SharedJson.Options),
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}

/// <summary>
/// Реестр печатных сессий в общей базе.
/// </summary>
public sealed class RemotePrintJobRepository : IPrintJobRepository
{
    private readonly SharedTransport _транспорт;
    private readonly PendingOperationsStore _незавершённые;

    /// <summary>
    /// Создаёт адаптер реестра печатных сессий.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    /// <param name="незавершённые">Хранилище незавершённых операций.</param>
    public RemotePrintJobRepository(SharedTransport транспорт, PendingOperationsStore незавершённые)
    {
        ArgumentNullException.ThrowIfNull(транспорт);
        ArgumentNullException.ThrowIfNull(незавершённые);

        _транспорт = транспорт;
        _незавершённые = незавершённые;
    }

    /// <inheritdoc />
    public async Task<long> AddAsync(PrintJob printJob, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printJob);

        // Сессия печати создаётся на сервере реестра без привязки к
        // отправлениям: журнал печати с исходами и устройством заполняет
        // отдельная команда print.record-event. Сессия — мутация: потерянный
        // ответ не приводит ко второй сессии, повтор идёт с прежним
        // идентификатором операции.
        try
        {
            var ответ = await _незавершённые
                .ВыполнитьAsync<CreatePrintSessionResponse>(
                    _транспорт,
                    "print.create-session",
                    JsonSerializer.Serialize(
                        new CreatePrintSessionRequest(
                            printJob.TemplateId,
                            printJob.PrinterName,
                            printJob.PaperSizeName,
                            printJob.PaperWidthMm,
                            printJob.PaperHeightMm,
                            printJob.FontInfo,
                            printJob.Copies,
                            printJob.PrintMode,
                            printJob.Status,
                            printJob.ErrorText),
                        SharedJson.Options),
                    cancellationToken)
                .ConfigureAwait(false);

            return ответ.PrintJobId;
        }
        catch (SharedOutcomeUnknownException исключение)
        {
            // Исход записи сессии неизвестен: сервер мог создать запись
            // реестра, а мог и не создать. Печатный конвейер различает этот
            // случай и отказ: неизвестный исход не должен выглядеть как
            // «конверты не напечатаны» и не должен приводить к повторной
            // физической печати.
            throw new OutcomeUnknownException(исключение.Message);
        }
    }

    /// <inheritdoc />
    public async Task<PrintJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var страница = await _транспорт
            .ВыполнитьЗапросAsync<PageDto<PrintJobDto>>(
                "/api/v1/queries/print-jobs?offset=0&limit=200",
                cancellationToken)
            .ConfigureAwait(false);

        var найденный = страница.Items.FirstOrDefault(сессия => сессия.Id == id);

        if (найденный is null)
        {
            return null;
        }

        return new PrintJob(
            найденный.Id,
            найденный.CreatedAt,
            найденный.TemplateId,
            найденный.PrinterName,
            найденный.PaperSizeName,
            найденный.PaperWidthMm,
            найденный.PaperHeightMm,
            найденный.FontInfo,
            найденный.Copies,
            найденный.PrintMode,
            найденный.Status,
            найденный.ErrorText,
            null);
    }

    /// <inheritdoc />
    public async Task<Page<PrintJob>> GetPagedAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        var нормализованный = request.Normalized();

        var страница = await _транспорт
            .ВыполнитьЗапросAsync<PageDto<PrintJobDto>>(
                "/api/v1/queries/print-jobs?offset=" + нормализованный.Offset
                + "&limit=" + нормализованный.Limit,
                cancellationToken)
            .ConfigureAwait(false);

        return new Page<PrintJob>(
            страница.Items
                .Select(сессия => new PrintJob(
                    сессия.Id,
                    сессия.CreatedAt,
                    сессия.TemplateId,
                    сессия.PrinterName,
                    сессия.PaperSizeName,
                    сессия.PaperWidthMm,
                    сессия.PaperHeightMm,
                    сессия.FontInfo,
                    сессия.Copies,
                    сессия.PrintMode,
                    сессия.Status,
                    сессия.ErrorText,
                    null))
                .ToArray(),
            страница.Offset,
            страница.Limit,
            страница.TotalCount);
    }
}

/// <summary>
/// Данные отправлений для печати из общей базы.
/// </summary>
public sealed class RemotePrintDataSource : IPrintDataSource
{
    private readonly SharedTransport _транспорт;

    /// <summary>
    /// Создаёт адаптер данных печати.
    /// </summary>
    /// <param name="транспорт">Транспорт клиента совместного режима.</param>
    public RemotePrintDataSource(SharedTransport транспорт)
    {
        ArgumentNullException.ThrowIfNull(транспорт);

        _транспорт = транспорт;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintableItem>> GetItemsAsync(
        IReadOnlyList<long> mailIds,
        CancellationToken cancellationToken = default)
    {
        // print.get-data — команда-чтение: исход не влияет на данные,
        // регистрация в незавершённых операциях не требуется.
        var ответ = await _транспорт
            .ВыполнитьКомандуJsonAsync<GetPrintDataResponse>(
                "print.get-data",
                JsonSerializer.Serialize(new GetPrintDataRequest(mailIds), SharedJson.Options),
                Guid.NewGuid().ToString("D"),
                cancellationToken)
            .ConfigureAwait(false);

        return ответ.Items
            .Select(элемент => new PrintableItem
            {
                Id = элемент.Id,
                Values = FieldDictionary.NormalizePrintValues(элемент.Values),
                LogoBytes = элемент.LogoBytes,
            })
            .ToArray();
    }
}
