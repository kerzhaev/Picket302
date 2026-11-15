using Picket302.Domain.Contracts;

namespace Picket302.Application.Import;

/// <summary>
/// Слияние пакета импорта с общими данными.
/// </summary>
/// <remarks>
/// <para>
/// Шаг слияния вынесен за интерфейс, потому что он единственный пишет данные
/// импорта: в одиночном режиме пакет сливается локальной службой, в
/// совместном — серверной командой <c>import.merge</c>, которая выполняется
/// в одной транзакции на сервере.
/// </para>
/// <para>
/// Разбор файла, сопоставление столбцов и проверка записей остаются на
/// клиенте в обоих режимах: файл целиком в общую базу не переносится, а
/// сливаются только принятые после проверки записи.
/// </para>
/// </remarks>
public interface IImportMerger
{
    /// <summary>
    /// Сливает пакет импорта с общими данными.
    /// </summary>
    /// <param name="пакет">Пакет импорта.</param>
    /// <param name="политика">Правило слияния с существующими данными.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Итог слияния.</returns>
    Task<ImportMergeOutcome> СлитьAsync(
        ImportBatch пакет,
        MergePolicy политика,
        CancellationToken токен = default);
}

/// <summary>
/// Итог слияния пакета импорта.
/// </summary>
/// <param name="Created">Число созданных записей.</param>
/// <param name="Updated">Число обновлённых записей.</param>
/// <param name="Skipped">Число пропущенных записей.</param>
/// <param name="Conflicts">Число записей с конфликтами слияния.</param>
/// <param name="Errors">Число записей, записанных с ошибками проверки.</param>
/// <param name="Unverified">Число записей, не подтверждённых оператором.</param>
public sealed record ImportMergeOutcome(
    int Created,
    int Updated,
    int Skipped,
    int Conflicts,
    int Errors,
    int Unverified);
