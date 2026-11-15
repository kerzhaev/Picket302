using Dapper;
using Picket302.Service.Data;
using Picket302.Service.Hosting;
using Picket302.Shared.Commands;
using Picket302.Shared.Dtos;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Commands;

/// <summary>
/// Загрузка пакета шаблона на сервер.
/// </summary>
/// <remarks>
/// Пакет хранится на сервере: клиент не передаёт путь к файлу, только
/// содержимое. Служба проверяет архив и записывает новую версию.
/// </remarks>
public sealed class UploadTemplatePackageCommand
    : SharedDbCommand<UploadTemplatePackageRequest, TemplatePackageDto>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "templates.upload-package";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    /// <param name="каталогПакетов">Каталог пакетов шаблонов.</param>
    public UploadTemplatePackageCommand(ServerDatabase база, string каталогПакетов)
        : base(база)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогПакетов);

        КаталогПакетов = каталогПакетов;
    }

    /// <summary>Каталог пакетов шаблонов.</summary>
    private string КаталогПакетов { get; }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<TemplatePackageDto> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        UploadTemplatePackageRequest запрос,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(запрос.Name)
            || запрос.Name.Trim().Length == 0
            || запрос.Name.Trim().Length > 200)
        {
            throw new SharedValidationException("Наименование пакета не задано или слишком длинное.");
        }

        byte[] содержимое;

        try
        {
            содержимое = Convert.FromBase64String(запрос.ContentBase64);
        }
        catch (FormatException)
        {
            throw new SharedValidationException("Содержимое пакета не прошло проверку формата.");
        }

        var хранилище = new SharedTemplatePackageStore(КаталогПакетов, область.Единица);

        var метаданные = await хранилище.СохранитьAsync(
            запрос.Name.Trim(),
            содержимое,
            isBuiltin: false,
            cancellationToken).ConfigureAwait(false);

        // Строка шаблона, связанная с пакетом, переводится на новую версию:
        // иначе рабочее место запросило бы прежнюю версию и напечатало по
        // устаревшему макету. Привязка хранит именно действующую версию.
        await область.Единица.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE print_templates
                SET package_version = @Version, package_hash = @Hash
                WHERE package_id = @Id;
                """,
                new
                {
                    Id = метаданные.PackageId,
                    Version = метаданные.Version,
                    Hash = метаданные.ContentHash,
                },
                область.Единица.Transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return метаданные;
    }
}

/// <summary>
/// Удаление управляемого пакета шаблона.
/// </summary>
public sealed class DeleteTemplatePackageCommand
    : SharedDbCommand<DeleteTemplatePackageRequest, DeleteTemplatePackageResponse>
{
    /// <summary>Имя команды в URL.</summary>
    public const string Имя = "templates.delete-package";

    /// <summary>
    /// Создаёт команду.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    /// <param name="каталогПакетов">Каталог пакетов шаблонов.</param>
    public DeleteTemplatePackageCommand(ServerDatabase база, string каталогПакетов)
        : base(база)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(каталогПакетов);

        КаталогПакетов = каталогПакетов;
    }

    /// <summary>Каталог пакетов шаблонов.</summary>
    private string КаталогПакетов { get; }

    /// <inheritdoc />
    public override string Name => Имя;

    /// <inheritdoc />
    protected override async Task<DeleteTemplatePackageResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        DeleteTemplatePackageRequest запрос,
        CancellationToken cancellationToken)
    {
        var хранилище = new SharedTemplatePackageStore(КаталогПакетов, область.Единица);

        var удалено = await хранилище
            .УдалитьAsync(запрос.PackageId, cancellationToken)
            .ConfigureAwait(false);

        if (!удалено)
        {
            throw new SharedNotFoundException("Пакет шаблона не найден.");
        }

        return new DeleteTemplatePackageResponse(запрос.PackageId);
    }
}
