using Picket302.Domain.Contracts;

namespace Picket302.Application.Import;

/// <summary>
/// Результат импорта контрагентов из выгрузки 1С (ТЗ §8.5, функция 72:
/// «Импорт контрагентов из выгрузок 1С:Предприятие 7.7 и 8.x»).
/// </summary>
/// <remarks>
/// Результат содержит сформированный пакет импорта и предупреждения разбора.
/// Предупреждения пакета описывают пропущенные блоки и повреждённые структуры
/// файла; предупреждения отдельных записей (усечение значений, отсутствие
/// почтового индекса) хранятся в самих записях.
/// </remarks>
public sealed class ImportBatchResult
{
    /// <summary>
    /// Создаёт результат импорта.
    /// </summary>
    /// <param name="batch">Сформированный пакет импорта.</param>
    /// <param name="warnings">Предупреждения разбора файла.</param>
    public ImportBatchResult(ImportBatch batch, IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(batch);

        Batch = batch;
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>Сформированный пакет импорта.</summary>
    public ImportBatch Batch { get; }

    /// <summary>
    /// Предупреждения разбора файла: пропущенные блоки, повреждённые структуры.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Записи пакета импорта (удобный доступ к записям).</summary>
    public IReadOnlyList<ImportRecord> Records => Batch.Records;

    /// <summary>Число записей пакета импорта.</summary>
    public int RecordCount => Batch.Records.Count;

    /// <summary>
    /// Все предупреждения разбора: предупреждения пакета и предупреждения
    /// отдельных записей.
    /// </summary>
    public IReadOnlyList<string> AllWarnings
    {
        get
        {
            var все = new List<string>(Warnings);

            foreach (var запись in Batch.Records)
            {
                все.AddRange(запись.Warnings);
            }

            return все;
        }
    }
}
