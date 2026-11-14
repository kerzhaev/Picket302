using System.Net;
using System.Security.Cryptography.X509Certificates;
using Dapper;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Picket302.Service.Commands;
using Picket302.Service.Configuration;
using Picket302.Service.Data;
using Picket302.Service.Security;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Сборка серверной службы совместного режима.
/// </summary>
/// <remarks>
/// Сборка вынесена отдельно от точки входа, чтобы службу можно было
/// поднять в проверках на произвольном порту и в изолированном каталоге.
/// </remarks>
public static class SharedServiceFactory
{
    /// <summary>
    /// Создаёт веб-приложение службы.
    /// </summary>
    /// <param name="параметры">Настройки службы.</param>
    /// <param name="журнал">Приёмник записей журнала службы.</param>
    /// <param name="сертификат">
    /// Сертификат сервера для TLS. Пусто — служба поднимается без
    /// шифрования (используется только в проверках на локальном адресе).
    /// </param>
    /// <returns>Готовое веб-приложение.</returns>
    public static WebApplication Создать(
        ServiceOptions параметры,
        Action<string> журнал,
        X509Certificate2? сертификат = null)
    {
        ArgumentNullException.ThrowIfNull(параметры);
        ArgumentNullException.ThrowIfNull(журнал);

        var база = new ServerDatabase(параметры.DatabasePath);

        var реестр = СоздатьРеестр(база);

                var конвейер = new SharedCommandPipeline(база, реестр, журнал);
        var опознаватель = new SharedDeviceAuthenticator(база, журнал);
        var админТокен = new SharedAdminToken(параметры.DataDirectory);

        var построитель = WebApplication.CreateBuilder();

        построитель.Logging.ClearProviders();
        построитель.WebHost.ConfigureKestrel(настройки =>
        {
            настройки.ListenAnyIP(параметры.Port, узел =>
            {
                if (сертификат is not null)
                {
                    узел.UseHttps(сертификат);
                }
            });

            настройки.AddServerHeader = false;
        });

        построитель.Services.AddSingleton(база);
        построитель.Services.AddSingleton(реестр);
        построитель.Services.AddSingleton(конвейер);
        построитель.Services.AddSingleton(опознаватель);
        построитель.Services.AddSingleton(админТокен);

        var приложение = построитель.Build();

        приложение.Use(async (http, следующий) =>
        {
            try
            {
                await следующий(http).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Служба обязана пережить любую ошибку запроса: необработанное исключение остановило бы обслуживание клиентов.
            catch (Exception ошибка)
            {
                журнал($"Необработанная ошибка запроса {http.Request.Path}: {ошибка}");

                if (!http.Response.HasStarted)
                {
                    http.Response.StatusCode = StatusCodes.Status500InternalServerError;

                    await http.Response.WriteAsJsonAsync(
                        new SharedError(
                            SharedErrorCodes.Internal,
                            "Внутренняя ошибка службы."),
                        SharedJson.Options).ConfigureAwait(false);
                }
            }
        });

        // Идентичность сервера и отпечаток ключа вычисляются при сборке:
        // клиент закрепляет отпечаток и сверяет его при каждом подключении.
        var отпечаток = сертификат is null
            ? string.Empty
            : SharedSecrets.ВычислитьОтпечатокСервера(сертификат);

        var идентификатор = ПрочитатьИдентификаторСервера(база);

        приложение.Use(async (http, следующий) =>
        {
            http.Response.Headers[SharedProtocol.ProtocolVersionHeader] =
                SharedProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);

            await следующий(http).ConfigureAwait(false);
        });

        SharedEndpoints.Подключить(
            приложение,
            new SharedServiceContext(
                параметры,
                база,
                реестр,
                конвейер,
                опознаватель,
                админТокен,
                идентификатор,
                отпечаток,
                журнал));

        return приложение;
    }

    /// <summary>
    /// Регистрирует все команды службы.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    /// <returns>Реестр команд.</returns>
    public static SharedCommandRegistry СоздатьРеестр(ServerDatabase база)
    {
        var реестр = new SharedCommandRegistry();

        реестр.Register(new SaveCounterpartyCommand(база));
        реестр.Register(new ChangeCounterpartyStateCommand(база));
        реестр.Register(new BatchChangeCounterpartyStateCommand(база));
        реестр.Register(new BatchUpdateCounterpartyFieldCommand(база));
        реестр.Register(new SetCounterpartyFieldCommand(база));
        реестр.Register(new RemoveCounterpartyFieldCommand(база));
        реестр.Register(new CreateAgentCommand(база));

        реестр.Register(new CreateGroupCommand(база));
        реестр.Register(new RenameGroupCommand(база));
        реестр.Register(new DeleteGroupCommand(база));
        реестр.Register(new SetDefaultGroupCommand(база));
        реестр.Register(new CreateCategoryCommand(база));
        реестр.Register(new RenameCategoryCommand(база));
        реестр.Register(new DeleteCategoryCommand(база));
        реестр.Register(new MoveCounterpartyCommand(база));

        реестр.Register(new CreateFolderCommand(база));
        реестр.Register(new RenameFolderCommand(база));
        реестр.Register(new MoveFolderCommand(база));
        реестр.Register(new DeleteFolderCommand(база));

        реестр.Register(new CreateMailCommand(база));
        реестр.Register(new TransitionMailStatusCommand(база));
        реестр.Register(new MoveMailToFolderCommand(база));
        реестр.Register(new UpdateDraftRecipientAddressCommand(база));
        реестр.Register(new AssignMailNumberCommand(база));

        реестр.Register(new GenerateDistributionCommand(база));
        реестр.Register(new CreateDistributionFromSelectionCommand(база));
        реестр.Register(new CloneDistributionCommand(база));
        реестр.Register(new UpdateDistributionCommand(база));
        реестр.Register(new SaveDistributionCommand(база));
        реестр.Register(new MarkDistributionDispatchedCommand(база));
        реестр.Register(new RecalculateEnvelopesCommand(база));

        реестр.Register(new SavePrintTemplateCommand(база));
        реестр.Register(new DeletePrintTemplateCommand(база));
        реестр.Register(new SaveBlankFieldCommand(база));
        реестр.Register(new DeleteBlankFieldCommand(база));

        реестр.Register(new RecordPrintEventCommand(база));
        реестр.Register(new WriteEmailLogCommand(база));
        реестр.Register(new ImportMergeCommand(база));
        реестр.Register(new ClearDatabaseCommand(база));
        реестр.Register(new SeedDemoCommand(база));

        return реестр;
    }

    /// <summary>
    /// Находит сертификат сервера в хранилище сертификатов компьютера.
    /// </summary>
    /// <param name="отпечаток">Отпечаток сертификата.</param>
    /// <returns>Сертификат либо <see langword="null"/>, если он не найден.</returns>
    public static X509Certificate2? НайтиСертификат(string? отпечаток)
    {
        if (string.IsNullOrWhiteSpace(отпечаток))
        {
            return null;
        }

        using var хранилище = new X509Store(StoreName.My, StoreLocation.LocalMachine);

        try
        {
            хранилище.Open(OpenFlags.ReadOnly);

            var найденный = хранилище.Certificates
                .Find(X509FindType.FindByThumbprint, отпечаток.Trim(), validOnly: false);

            return найденный.Count > 0 ? найденный[0] : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Читает или создаёт идентификатор сервера.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    /// <returns>Идентификатор сервера.</returns>
    private static string ПрочитатьИдентификаторСервера(ServerDatabase база)
    {
        const string ключ = "server-id";

        var сессия = база.СоздатьСессию();

        try
        {
            var значение = сессия.Connection.QueryFirstOrDefault<string>(
                "SELECT value FROM shared_service_meta WHERE name = @Name;",
                new { Name = ключ });

            if (!string.IsNullOrWhiteSpace(значение))
            {
                return значение;
            }

            var новый = Guid.NewGuid().ToString("D");

            сессия.Connection.Execute(
                """
                INSERT INTO shared_service_meta (name, value)
                VALUES (@Name, @Value)
                ON CONFLICT (name) DO NOTHING;
                """,
                new { Name = ключ, Value = новый });

            return новый;
        }
        finally
        {
            сессия.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
