using Picket302.Service.Data;
using Picket302.Shared.Protocol;

namespace Picket302.Service.Hosting;

/// <summary>
/// Основа серверной команды, работающей с общей базой.
/// </summary>
/// <remarks>
/// <para>
/// Команда исполняется в одной транзакции: смена версии записи, изменение
/// данных и отметка о выполнении в журнале идемпотентности фиксируются
/// вместе. Если команда не выполнилась, не изменилось ничего.
/// </para>
/// <para>
/// Повтор запроса с тем же идентификатором операции возвращает прежний
/// результат: письмо или рассылка не создаются дважды.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">Тип запроса команды.</typeparam>
/// <typeparam name="TResponse">Тип ответа команды.</typeparam>
public abstract class SharedDbCommand<TRequest, TResponse> : SharedCommand<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Создаёт команду над указанной фабрикой сессий базы данных.
    /// </summary>
    /// <param name="база">Фабрика сессий базы данных.</param>
    protected SharedDbCommand(ServerDatabase база)
    {
        ArgumentNullException.ThrowIfNull(база);

        База = база;
    }

    /// <summary>Фабрика сессий базы данных.</summary>
    protected ServerDatabase База { get; }

    /// <summary>
    /// Выполняет содержимое команды в открытой транзакции области.
    /// </summary>
    /// <param name="область">Область выполнения команды.</param>
    /// <param name="контекст">Контекст выполнения.</param>
    /// <param name="запрос">Запрос команды.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Результат команды.</returns>
    protected abstract Task<TResponse> ВыполнитьВнутриAsync(
        SharedDbScope область,
        SharedCommandContext контекст,
        TRequest запрос,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public override async Task<TResponse> ExecuteAsync(
        SharedCommandContext context,
        TRequest request,
        CancellationToken cancellationToken)
    {
        await using var область = await SharedDbScope
            .СоздатьAsync(База, cancellationToken)
            .ConfigureAwait(false);

        await область.ОткрытьТранзакциюAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var результат = await ВыполнитьВнутриAsync(
                область,
                context,
                request,
                cancellationToken).ConfigureAwait(false);

            await область
                .СохранитьРезультатAsync(
                    context.OperationId,
                    результат,
                    context.StartedAt,
                    cancellationToken)
                .ConfigureAwait(false);

            await область.ЗафиксироватьAsync(cancellationToken).ConfigureAwait(false);

            return результат;
        }
        catch
        {
            await область.ОткатитьAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
