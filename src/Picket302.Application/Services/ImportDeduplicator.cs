using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;

namespace Picket302.Application.Services;

/// <summary>
/// Дедупликация импорта: отбрасывание повторяющихся записей внутри пакета и
/// записей, уже присутствующих в базе (ТЗ §8.12, функция 75: «Дедупликация
/// импорта и источник данных каждого контрагента»).
/// </summary>
/// <remarks>
/// <para>
/// Каждой принятой записи записывается признак источника данных вида
/// «Импорт &lt;формат&gt; от &lt;дата&gt; из &lt;файл&gt;»
/// (колонка <c>counterparties.source</c>).
/// </para>
/// <para>
/// Записи, уже присутствующие в базе, пропускаются, если политика слияния
/// не предполагает обновления («пропустить существующую»). При политиках
/// «дополнить пустые» и «перезаписать» такие записи принимаются и учитываются
/// как обновлённые.
/// </para>
/// </remarks>
public sealed class ImportDeduplicator
{
    private readonly ICounterpartyRepository _репозиторий;

    /// <summary>
    /// Создаёт дедупликатор.
    /// </summary>
    /// <param name="repository">Репозиторий контрагентов.</param>
    public ImportDeduplicator(ICounterpartyRepository repository)
        => _репозиторий = repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Отбрасывает дубликаты пакета и записи, уже присутствующие в базе.
    /// </summary>
    /// <param name="drafts">Черновики контрагентов пакета.</param>
    /// <returns>Результат дедупликации.</returns>
    public DeduplicationResult FilterDuplicates(IEnumerable<CounterpartyDraft> drafts)
        => FilterDuplicates(drafts, MergePolicy.SkipExisting, null, CancellationToken.None);

    /// <summary>
    /// Отбрасывает дубликаты пакета и записи, уже присутствующие в базе.
    /// </summary>
    /// <param name="drafts">Черновики контрагентов пакета.</param>
    /// <param name="policy">Политика слияния.</param>
    /// <returns>Результат дедупликации.</returns>
    public DeduplicationResult FilterDuplicates(IEnumerable<CounterpartyDraft> drafts, MergePolicy policy)
        => FilterDuplicates(drafts, policy, null, CancellationToken.None);

    /// <summary>
    /// Отбрасывает дубликаты пакета и записи, уже присутствующие в базе, и
    /// проставляет признак источника данных.
    /// </summary>
    /// <param name="drafts">Черновики контрагентов пакета.</param>
    /// <param name="policy">Политика слияния.</param>
    /// <param name="sourceDescription">
    /// Описание источника данных вида «Импорт &lt;формат&gt; от &lt;дата&gt; из
    /// &lt;файл&gt;». Если черновик уже содержит признак источника, он не
    /// заменяется.
    /// </param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="проверятьБазу">
    /// Признак проверки записей по базе. При значении <see langword="false"/>
    /// отбрасываются только повторы внутри пакета — так поступает мастер
    /// импорта, где существующие записи обрабатывает служба тонкого слияния.
    /// </param>
    /// <returns>Результат дедупликации.</returns>
    public DeduplicationResult FilterDuplicates(
        IEnumerable<CounterpartyDraft> drafts,
        MergePolicy policy,
        string? sourceDescription,
        CancellationToken cancellationToken = default,
        bool проверятьБазу = true)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        var принятые = new List<CounterpartyDraft>();
        var дубликаты = new List<CounterpartyDraft>();
        var встреченныеКлючи = new HashSet<string>(StringComparer.Ordinal);
        var обновлено = 0;

        foreach (var черновик in drafts)
        {
            if (черновик is null)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var ключ = ImportMergeService.BuildMergeKey(черновик);

            if (ключ.Length > 0 && !встреченныеКлючи.Add(ключ))
            {
                дубликаты.Add(черновик);
                continue;
            }

            if (проверятьБазу && ключ.Length > 0)
            {
                var существующий = НайтиСовпадение(черновик, cancellationToken).GetAwaiter().GetResult();

                if (существующий is not null)
                {
                    if (policy == MergePolicy.SkipExisting)
                    {
                        дубликаты.Add(черновик);
                        continue;
                    }

                    обновлено++;
                }
            }

            if (string.IsNullOrWhiteSpace(черновик.Source) && !string.IsNullOrWhiteSpace(sourceDescription))
            {
                черновик.Source = sourceDescription;
            }

            принятые.Add(черновик);
        }

        return new DeduplicationResult
        {
            Accepted = принятые,
            SkippedDuplicates = дубликаты,
            UpdatedCount = обновлено,
        };
    }

    /// <summary>
    /// Формирует описание источника данных вида
    /// «Импорт &lt;формат&gt; от &lt;дата&gt; из &lt;файл&gt;».
    /// </summary>
    /// <param name="format">Формат источника («CSV», «XLSX», «1С 8.x»).</param>
    /// <param name="importedAt">Момент импорта.</param>
    /// <param name="fileName">Имя исходного файла.</param>
    /// <returns>Описание источника данных.</returns>
    public static string BuildSourceDescription(string format, DateTime importedAt, string fileName)
    {
        var имяФайла = string.IsNullOrWhiteSpace(fileName) ? string.Empty : fileName.Trim();
        var имяФормата = string.IsNullOrWhiteSpace(format) ? string.Empty : format.Trim();

        return $"Импорт {имяФормата} от {importedAt:dd.MM.yyyy} из {имяФайла}";
    }

    /// <summary>
    /// Ищет существующую запись по ключу слияния.
    /// </summary>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Найденный контрагент либо <see langword="null"/>.</returns>
    private async Task<Counterparty?> НайтиСовпадение(
        CounterpartyDraft черновик,
        CancellationToken cancellationToken)
    {
        var инн = string.IsNullOrWhiteSpace(черновик.Inn) ? null : черновик.Inn.Trim();
        var имя = (черновик.Name ?? string.Empty).Trim();
        var индекс = string.IsNullOrWhiteSpace(черновик.PostalZip) ? null : черновик.PostalZip.Trim();

        var найденный = await _репозиторий
            .FindByMergeKeyAsync(инн, имя, индекс, cancellationToken)
            .ConfigureAwait(false);

        if (найденный is not null)
        {
            return найденный;
        }

        var нормализованное = ImportMergeService.NormalizeKeyPart(имя);

        if (нормализованное.Length == 0
            || string.Equals(нормализованное, имя, StringComparison.Ordinal))
        {
            return null;
        }

        return await _репозиторий
            .FindByMergeKeyAsync(инн, нормализованное, индекс, cancellationToken)
            .ConfigureAwait(false);
    }
}
