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
    /// Сертификат сервера для TLS. Обязателен: без него служба не
    /// принимает подключения, потому что секрет устройства не должен
    /// передаваться по открытому каналу.
    /// </param>
    /// <returns>Готовое веб-приложение.</returns>
    public static WebApplication Создать(
        ServiceOptions параметры,
        Action<string> журнал,
        X509Certificate2 сертификат)
    {
        ArgumentNullException.ThrowIfNull(параметры);
        ArgumentNullException.ThrowIfNull(журнал);
        ArgumentNullException.ThrowIfNull(сертификат);

        var база = new ServerDatabase(параметры.DatabasePath);

        var реестр = СоздатьРеестр(база);

                var конвейер = new SharedCommandPipeline(база, реестр, журнал);
        var опознаватель = new SharedDeviceAuthenticator(база, журнал);
        var админТокен = new SharedAdminToken(параметры.DataDirectory);

        var построитель = WebApplication.CreateBuilder();

        построитель.Logging.ClearProviders();
        построитель.WebHost.ConfigureKestrel(настройки =>
        {
            настройки.ListenAnyIP(параметры.Port, узел => узел.UseHttps(сертификат));

            настройки.AddServerHeader = false;

            // Предел тела задаётся на уровне сервера: слишком большой запрос
            // отклоняется до того, как команда будет разобрана.
            настройки.Limits.MaxRequestBodySize = SharedProtocol.MaxRequestBodyBytes;
        });

        построитель.Services.AddSingleton(база);
        построитель.Services.AddSingleton(реестр);
        построитель.Services.AddSingleton(конвейер);
        построитель.Services.AddSingleton(опознаватель);
        построитель.Services.AddSingleton(админТокен);

        var приложение = построитель.Build();

        // Версия протокола объявляется клиентом в заголовке и проверяется на
        // маршрутах команд и чтения. Маршруты сопряжения и сведений о сервере
        // освобождены от проверки: клиент узнаёт версию службы на этапе
        // сопряжения, до того как договорился о версии.
        приложение.Use(async (http, следующий) =>
        {
            if (ТребуетсяВерсия(http.Request.Path)
                && !SharedEndpoints.ВерсияПоддерживается(http))
            {
                http.Response.StatusCode = StatusCodes.Status426UpgradeRequired;

                await http.Response.WriteAsJsonAsync(
                    new SharedError(
                        SharedErrorCodes.VersionIncompatible,
                        "Версия протокола клиента не поддерживается службой."),
                    SharedJson.Options);

                return;
            }

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
        реестр.Register(new GetPrintDataCommand(база));
        реестр.Register(new WriteEmailLogCommand(база));
        реестр.Register(new ImportMergeCommand(база));
        реестр.Register(new ClearDatabaseCommand(база));
        реестр.Register(new SeedDemoCommand(база));

        return реестр;
    }

    /// <summary>
    /// Определяет, обязателен ли заголовок версии протокола для маршрута.
    /// </summary>
    /// <param name="путь">Путь запроса.</param>
    /// <returns><see langword="true"/>, если версия обязательна.</returns>
    private static bool ТребуетсяВерсия(PathString путь)
    {
        if (!путь.StartsWithSegments(SharedProtocol.RoutePrefix))
        {
            return false;
        }

        if (путь.Equals(SharedProtocol.HealthRoute, StringComparison.OrdinalIgnoreCase)
            || путь.Equals(SharedProtocol.ServerInfoRoute, StringComparison.OrdinalIgnoreCase)
            || путь.Equals(SharedProtocol.PairRoute, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
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
