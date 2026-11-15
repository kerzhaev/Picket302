using Picket302.Application.Import;
using Picket302.Domain.Contracts;

namespace Picket302.App.Shared;

/// <summary>
/// Слияние пакета импорта через шлюз данных рабочего места.
/// </summary>
/// <remarks>
/// <para>
/// В одиночном режиме шлюз сливает пакет локальной службой, в совместном —
/// серверной командой <c>import.merge</c>. Оба пути пишут одни и те же
/// доменные правила слияния, поэтому поведение импорта от режима не
/// зависит.
/// </para>
/// <para>
/// Совместный режим даёт важное свойство: пакет применяется сервером одной
/// командой в одной транзакции. Прерванный ответ не оставляет частичного
/// переключения, а повтор команды с прежним идентификатором операции
/// возвращает прежний результат и не создаёт дублей.
/// </para>
/// </remarks>
public sealed class GatewayImportMerger : IImportMerger
{
    private readonly IDataGateway _шлюз;

    /// <summary>
    /// Создаёт слиятель поверх шлюза данных.
    /// </summary>
    /// <param name="шлюз">Шлюз данных рабочего места.</param>
    public GatewayImportMerger(IDataGateway шлюз)
    {
        ArgumentNullException.ThrowIfNull(шлюз);

        _шлюз = шлюз;
    }

    /// <inheritdoc />
    public async Task<ImportMergeOutcome> СлитьAsync(
        ImportBatch пакет,
        MergePolicy политика,
        CancellationToken токен = default)
    {
        ArgumentNullException.ThrowIfNull(пакет);

        var отчёт = await _шлюз
            .СлитьИмпортAsync(пакет, политика, токен)
            .ConfigureAwait(false);

        return new ImportMergeOutcome(
            отчёт.Создано,
            отчёт.Обновлено,
            отчёт.Пропущено,
            отчёт.Конфликты,
            отчёт.Ошибки,
            отчёт.Непроверенные);
    }
}
