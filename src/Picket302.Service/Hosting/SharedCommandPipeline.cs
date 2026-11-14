using System.Text.Json;
using Picket302.Composition;
using Picket302.Service.Data;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Результат выполнения серверной команды.
/// </summary>
/// <param name="Успешно">Признак успешного исхода.</param>
/// <param name="КодСостояния">Код состояния HTTP.</param>
/// <param name="Результат">Тело успешного ответа в виде JSON.</param>
/// <param name="Ошибка">Ошибка протокола.</param>
public sealed record SharedPipelineResult(
    bool Успешно,
    int КодСостояния,
    string? Результат = null,
    SharedError? Ошибка = null);

/// <summary>
/// Исполнение серверных команд общего режима.
/// </summary>
/// <remarks>
/// <para>
/// Транзакционная граница команды: <b>одна</b> короткая транзакция на стороне
/// службы. Всё составное действие (рассылка и её письма, смена статуса и
/// запись в журнал печати) выполняется целиком, а составные транзакции
/// прикладных служб вкладываются в неё.
/// </para>
/// <para>
/// Идемпотентность обеспечивается журналом операций: повтор запроса с тем же
/// идентификатором возвращает прежний результат и не создаёт дубль. Фиксация
/// результата в журнале входит в ту же транзакцию, что и сама команда, —
/// поэтому прерванный ответ не приводит к повторному письму.
/// </para>
/// </remarks>
public sealed class SharedCommandPipeline
{
    private readonly ServerDatabase _база;
    private readonly SharedCommandRegistry _реестр;
    private readonly Action<string> _журнал;

    /// <summary>
    /// Создаёт конвейер выполнения команд.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    /// <param name="реестр">Реестр команд.</param>
    /// <param name="журнал">Приёмник записей журнала службы.</param>
    public SharedCommandPipeline(
        ServerDatabase база,
        SharedCommandRegistry реестр,
        Action<string> журнал)
    {
        ArgumentNullException.ThrowIfNull(база);
        ArgumentNullException.ThrowIfNull(реестр);
        ArgumentNullException.ThrowIfNull(журнал);

        _база = база;
        _реестр = реестр;
        _журнал = журнал;
    }

    /// <summary>
    /// Реестр команд, обслуживаемый конвейером.
    /// </summary>
    public SharedCommandRegistry Реестр => _реестр;

    /// <summary>
    /// Выполняет команду.
    /// </summary>
    /// <param name="имяКоманды">Имя команды.</param>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="deviceId">Идентификатор устройства-клиента.</param>
    /// <param name="deviceName">Имя устройства.</param>
    /// <param name="телоЗапроса">Тело запроса в виде JSON.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат выполнения команды.</returns>
    public async Task<SharedPipelineResult> ВыполнитьAsync(
        string имяКоманды,
        string operationId,
        string deviceId,
        string deviceName,
        string телоЗапроса,
        CancellationToken cancellationToken = default)
    {
        if (!_реестр.TryGet(имяКоманды, out var описание))
        {
            return new SharedPipelineResult(
                false,
                StatusCodes.Status404NotFound,
                Ошибка: new SharedError(
                    SharedErrorCodes.NotFound,
                    "Запрошенная операция не поддерживается этой службой."));
        }

        object? запрос;

        try
        {
            запрос = JsonSerializer.Deserialize(
                телоЗапроса,
                описание.RequestType,
                SharedJson.Options);
        }
        catch (JsonException ошибка)
        {
            return new SharedPipelineResult(
                false,
                StatusCodes.Status400BadRequest,
                Ошибка: new SharedError(
                    SharedErrorCodes.ValidationFailed,
                    "Запрос не прошёл проверку формата.",
                    operationId,
                    ошибка.Message));
        }

        if (запрос is null)
        {
            return new SharedPipelineResult(
                false,
                StatusCodes.Status400BadRequest,
                Ошибка: new SharedError(
                    SharedErrorCodes.ValidationFailed,
                    "Запрос пуст.",
                    operationId));
        }

        var отпечаток = SharedOperationStore.ВычислитьОтпечаток(телоЗапроса);

        // Шаг 1. Журнал идемпотентности: отдельная короткая транзакция, чтобы
        // параллельный повтор запроса увидел занятый идентификатор.
        var начало = await НачатьОперациюAsync(
            operationId,
            deviceId,
            имяКоманды,
            отпечаток,
            cancellationToken).ConfigureAwait(false);

        switch (начало.Вид)
        {
            case SharedOperationOutcomeKind.ReplayResult:
                return new SharedPipelineResult(true, StatusCodes.Status200OK, начало.Результат);

            case SharedOperationOutcomeKind.ReplayError:
                return await ВосстановитьОшибкуAsync(
                    operationId,
                    начало.Ошибка,
                    cancellationToken).ConfigureAwait(false);

            case SharedOperationOutcomeKind.Conflict:
                return new SharedPipelineResult(
                    false,
                    StatusCodes.Status409Conflict,
                    Ошибка: new SharedError(
                        SharedErrorCodes.IdempotencyMismatch,
                        начало.Причина ?? "Операция с таким идентификатором уже выполняется.",
                        operationId));
        }

        // Шаг 2. Выполнение команды. Транзакцией команды и фиксацией её
        // результата в журнале идемпотентности занимается сама команда:
        // обе операции фиксируются вместе.
        try
        {
            var контекст = new SharedCommandContext(operationId, deviceId, deviceName);
            var результат = await описание
                .InvokeAsync(контекст, запрос, cancellationToken)
                .ConfigureAwait(false);

            var json = JsonSerializer.Serialize(результат, SharedJson.Options);

            return new SharedPipelineResult(true, StatusCodes.Status200OK, json);
        }
        catch (SharedCommandException ошибка)
        {
            var ошибкаПротокола = new SharedError(
                ошибка.Код,
                ошибка.Сообщение,
                operationId,
                ошибка.Подробности);

            await СохранитьОшибкуAsync(operationId, ошибкаПротокола, cancellationToken)
                .ConfigureAwait(false);

            return new SharedPipelineResult(false, КодСостояния(ошибкаПротокола), Ошибка: ошибкаПротокола);
        }
        catch (JsonException ошибка)
        {
            var ошибкаПротокола = new SharedError(
                SharedErrorCodes.ValidationFailed,
                "Данные команды не прошли проверку формата.",
                operationId,
                ошибка.Message);

            await СохранитьОшибкуAsync(operationId, ошибкаПротокола, cancellationToken)
                .ConfigureAwait(false);

            return new SharedPipelineResult(
                false,
                StatusCodes.Status400BadRequest,
                Ошибка: ошибкаПротокола);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Служба обязана пережить любую ошибку команды: необработанное исключение остановило бы обслуживание клиентов.
        catch (Exception ошибка)
        {
            _журнал($"Ошибка выполнения команды «{имяКоманды}» (операция {operationId}): {ошибка}");

            var ошибкаПротокола = new SharedError(
                SharedErrorCodes.Internal,
                "Внутренняя ошибка службы. Операция не выполнена.",
                operationId);

            await СохранитьОшибкуAsync(operationId, ошибкаПротокола, cancellationToken)
                .ConfigureAwait(false);

            return new SharedPipelineResult(
                false,
                StatusCodes.Status500InternalServerError,
                Ошибка: ошибкаПротокола);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Регистрирует начало операции в журнале идемпотентности.
    /// </summary>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="deviceId">Идентификатор устройства.</param>
    /// <param name="имяКоманды">Имя команды.</param>
    /// <param name="отпечаток">Отпечаток содержимого запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Исход попытки.</returns>
    private async Task<SharedOperationOutcome> НачатьОперациюAsync(
        string operationId,
        string deviceId,
        string имяКоманды,
        string отпечаток,
        CancellationToken cancellationToken)
    {
        var сессия = _база.СоздатьСессию();
        await using var освобождение = сессия.ConfigureAwait(false);

        await сессия.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var исход = await new SharedOperationStore(сессия, _журнал)
                .НачатьAsync(operationId, deviceId, имяКоманды, отпечаток, cancellationToken)
                .ConfigureAwait(false);

            await сессия.CommitAsync(cancellationToken).ConfigureAwait(false);

            return исход;
        }
        catch
        {
            await сессия.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Сохраняет ошибку операции в журнале идемпотентности отдельной
    /// транзакцией: транзакция команды к этому моменту уже откачена.
    /// </summary>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="ошибка">Ошибка протокола.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача завершения.</returns>
    private async Task СохранитьОшибкуAsync(
        string operationId,
        SharedError ошибка,
        CancellationToken cancellationToken)
    {
        try
        {
            var сессия = _база.СоздатьСессию();
            await using var освобождение = сессия.ConfigureAwait(false);

            await сессия.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await new SharedOperationStore(сессия)
                .СохранитьОшибкуAsync(
                    operationId,
                    JsonSerializer.Serialize(ошибка, SharedJson.Options),
                    cancellationToken)
                .ConfigureAwait(false);

            await сессия.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Сбой записи в журнал не должен прерывать выполнение команды: данные уже откачены.
        catch (Exception сбой)
        {
            // Ошибку не удалось сохранить: команда уже откачена, повтор
            // запроса выполнит её заново — данные не изменены.
            _журнал($"Не удалось сохранить ошибку операции {operationId} в журнале: {сбой}");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Восстанавливает сохранённую ошибку операции.
    /// </summary>
    /// <param name="operationId">Идентификатор операции.</param>
    /// <param name="ошибкаJson">Сохранённая ошибка в виде JSON.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат с ошибкой.</returns>
    private static Task<SharedPipelineResult> ВосстановитьОшибкуAsync(
        string operationId,
        string? ошибкаJson,
        CancellationToken cancellationToken)
    {
        SharedError? ошибка = null;

        if (!string.IsNullOrEmpty(ошибкаJson))
        {
            try
            {
                ошибка = JsonSerializer.Deserialize<SharedError>(ошибкаJson, SharedJson.Options);
            }
            catch (JsonException)
            {
                ошибка = null;
            }
        }

        ошибка ??= new SharedError(
            SharedErrorCodes.Internal,
            "Операция ранее завершилась ошибкой.",
            operationId);

        return Task.FromResult(
            new SharedPipelineResult(false, КодСостояния(ошибка), Ошибка: ошибка));
    }

    /// <summary>
    /// Сопоставляет код ошибки протокола с кодом состояния HTTP.
    /// </summary>
    /// <param name="ошибка">Ошибка протокола.</param>
    /// <returns>Код состояния HTTP.</returns>
    private static int КодСостояния(SharedError ошибка) => ошибка.Code switch
    {
        SharedErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
        SharedErrorCodes.VersionIncompatible => StatusCodes.Status426UpgradeRequired,
        SharedErrorCodes.Conflict => StatusCodes.Status409Conflict,
        SharedErrorCodes.IdempotencyMismatch => StatusCodes.Status409Conflict,
        SharedErrorCodes.OperationInProgress => StatusCodes.Status409Conflict,
        SharedErrorCodes.NotFound => StatusCodes.Status404NotFound,
        SharedErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        SharedErrorCodes.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        SharedErrorCodes.ArchiveRejected => StatusCodes.Status400BadRequest,
        SharedErrorCodes.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
        SharedErrorCodes.InsufficientSpace => StatusCodes.Status507InsufficientStorage,
        SharedErrorCodes.MigrationFailed => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
}
