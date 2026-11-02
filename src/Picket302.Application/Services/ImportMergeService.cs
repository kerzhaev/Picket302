using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Licensing;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;

namespace Picket302.Application.Services;

/// <summary>
/// Действие, выполненное над записью при тонком слиянии импортируемых данных
/// с существующей базой (ТЗ §8.12, функция 74).
/// </summary>
public enum ImportMergeAction
{
    /// <summary>Запись создана.</summary>
    Created = 0,

    /// <summary>Существующая запись обновлена.</summary>
    Updated = 1,

    /// <summary>Запись оставлена без изменений.</summary>
    Skipped = 2,
}

/// <summary>
/// Результат слияния по одной записи пакета (ТЗ §8.12, функция 74).
/// </summary>
/// <param name="Record">Запись пакета импорта.</param>
/// <param name="Action">Выполненное действие.</param>
/// <param name="HasConflict">
/// Признак того, что по ключу слияния найдена существующая запись.
/// </param>
/// <param name="ConflictDetails">Описание конфликтов: ключ, имя записи и расхождения значений.</param>
public sealed record ImportMergeOutcome(
    ImportRecord Record,
    ImportMergeAction Action,
    bool HasConflict,
    IReadOnlyList<string> ConflictDetails);

/// <summary>
/// Тонкое слияние импортируемых данных с существующей базой (ТЗ §8.12,
/// функция 74: «Тонкое слияние импортируемых данных с существующей базой»).
/// </summary>
/// <remarks>
/// <para>
/// Для каждой записи пакета ищется кандидат слияния по естественному ключу:
/// ИНН, а при его отсутствии — нормализованное наименование и почтовый индекс.
/// </para>
/// <para>
/// Политика слияния определяет действие: «пропустить существующую» — запись не
/// изменяется; «дополнить пустые» — копируются только пустые поля существующей
/// записи; «перезаписать» — непустые значения заменяются импортируемыми.
/// Конфликт непустых значений при политике «дополнить пустые» фиксируется в
/// отчёте, а значение не перезаписывается.
/// </para>
/// </remarks>
public sealed class ImportMergeService
{
    private readonly ICounterpartyRepository _репозиторий;
    private readonly ICounterpartyFieldRepository? _поля;
    private readonly ILicenseGate? _ворота;

    /// <summary>
    /// Создаёт службу слияния.
    /// </summary>
    /// <param name="repository">Репозиторий контрагентов.</param>
    public ImportMergeService(ICounterpartyRepository repository)
        : this(repository, null)
    {
    }

    /// <summary>
    /// Создаёт службу слияния с доступом к пользовательским полям.
    /// </summary>
    /// <param name="repository">Репозиторий контрагентов.</param>
    /// <param name="fieldRepository">
    /// Репозиторий пользовательских полей; используется при слиянии полей
    /// таблицы <c>counterparty_fields</c>.
    /// </param>
    /// <param name="ворота">
    /// Ворота лицензии; при наличии импорт в режиме trial с истёкшим сроком
    /// либо исчерпанным лимитом объёма не выполняется (ТЗ §8.10, функция 119).
    /// </param>
    public ImportMergeService(
        ICounterpartyRepository repository,
        ICounterpartyFieldRepository? fieldRepository,
        ILicenseGate? ворота = null)
    {
        _репозиторий = repository ?? throw new ArgumentNullException(nameof(repository));
        _поля = fieldRepository;
        _ворота = ворота;
    }

    /// <summary>
    /// Выполняет тонкое слияние пакета импорта с базой.
    /// </summary>
    /// <param name="batch">Пакет импорта.</param>
    /// <param name="policy">Политика слияния.</param>
    /// <returns>Отчёт о слиянии.</returns>
    public MergeReport MergeImported(ImportBatch batch, MergePolicy policy)
        => MergeImported(batch, policy, CancellationToken.None);

    /// <summary>
    /// Выполняет тонкое слияние пакета импорта с базой.
    /// </summary>
    /// <param name="batch">Пакет импорта.</param>
    /// <param name="policy">Политика слияния.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Отчёт о слиянии.</returns>
    public MergeReport MergeImported(ImportBatch batch, MergePolicy policy, CancellationToken cancellationToken)
    {
        var результаты = MergeImportedDetailed(batch, policy, cancellationToken).GetAwaiter().GetResult();

        return СобратьОтчёт(результаты);
    }

    /// <summary>
    /// Выполняет тонкое слияние пакета импорта с базой и возвращает результат
    /// по каждой записи.
    /// </summary>
    /// <param name="batch">Пакет импорта.</param>
    /// <param name="policy">Политика слияния.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Список результатов по записям пакета.</returns>
    public async Task<IReadOnlyList<ImportMergeOutcome>> MergeImportedDetailed(
        ImportBatch batch,
        MergePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var разрешение = _ворота?.Проверить(LicenseOperation.СозданиеЗаписей);

        if (разрешение is { Разрешено: false })
        {
            throw new LicenseLimitExceededException(разрешение.Сообщение);
        }

        var результаты = new List<ImportMergeOutcome>();

        foreach (var запись in batch.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (запись.Status == ImportRecordStatus.Skipped)
            {
                результаты.Add(new ImportMergeOutcome(запись, ImportMergeAction.Skipped, false, Array.Empty<string>()));
                continue;
            }

            var черновик = запись.Draft ?? new CounterpartyDraft();
            var существующий = await НайтиСовпадение(черновик, cancellationToken).ConfigureAwait(false);

            if (существующий is null)
            {
                var новый = СоздатьКонтрагента(черновик, batch);
                await _репозиторий.AddAsync(новый, cancellationToken).ConfigureAwait(false);

                результаты.Add(new ImportMergeOutcome(запись, ImportMergeAction.Created, false, Array.Empty<string>()));
                continue;
            }

            var конфликты = new List<string>
            {
                $"Ключ слияния: {ОписатьКлюч(черновик)}; найдена существующая запись «{существующий.Name}».",
            };

            switch (policy)
            {
                case MergePolicy.SkipExisting:
                    результаты.Add(new ImportMergeOutcome(запись, ImportMergeAction.Skipped, true, конфликты));
                    break;

                case MergePolicy.FillEmpty:
                    var изменено = СлитьПустыми(существующий, черновик, batch, конфликты);

                    if (изменено)
                    {
                        await _репозиторий.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);
                    }

                    результаты.Add(new ImportMergeOutcome(
                        запись,
                        изменено ? ImportMergeAction.Updated : ImportMergeAction.Skipped,
                        true,
                        конфликты));
                    break;

                case MergePolicy.Overwrite:
                    СлитьПерезаписью(существующий, черновик, batch, конфликты);
                    await _репозиторий.UpdateAsync(существующий, cancellationToken).ConfigureAwait(false);

                    результаты.Add(new ImportMergeOutcome(запись, ImportMergeAction.Updated, true, конфликты));
                    break;

                default:
                    результаты.Add(new ImportMergeOutcome(запись, ImportMergeAction.Skipped, true, конфликты));
                    break;
            }
        }

        return результаты;
    }

    /// <summary>
    /// Сливает значения пользовательских полей существующей записи по политике
    /// слияния (таблица <c>counterparty_fields</c>).
    /// </summary>
    /// <param name="counterpartyId">Идентификатор контрагента.</param>
    /// <param name="values">Импортируемые значения полей.</param>
    /// <param name="policy">Политика слияния.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Число изменённых полей.</returns>
    public async Task<int> MergeCustomFieldsAsync(
        long counterpartyId,
        IReadOnlyDictionary<string, string> values,
        MergePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (_поля is null)
        {
            return 0;
        }

        var существующие = await _поля.GetByCounterpartyAsync(counterpartyId, cancellationToken).ConfigureAwait(false);
        var изменено = 0;

        foreach (var пара in values)
        {
            var имя = пара.Key?.Trim();

            if (string.IsNullOrWhiteSpace(имя))
            {
                continue;
            }

            var существующее = существующие.FirstOrDefault(поле =>
                string.Equals(поле.FieldName, имя, StringComparison.OrdinalIgnoreCase));

            if (существующее is null)
            {
                await _поля.AddAsync(
                    new CounterpartyField
                    {
                        CounterpartyId = counterpartyId,
                        FieldName = имя,
                        FieldValue = пара.Value,
                        FieldType = CounterpartyFieldType.Text,
                        UpdatedAt = DateTime.UtcNow,
                    },
                    cancellationToken).ConfigureAwait(false);

                изменено++;
                continue;
            }

            if (policy == MergePolicy.SkipExisting)
            {
                continue;
            }

            if (policy == MergePolicy.FillEmpty && !string.IsNullOrWhiteSpace(существующее.FieldValue))
            {
                continue;
            }

            if (string.Equals(существующее.FieldValue, пара.Value, StringComparison.Ordinal))
            {
                continue;
            }

            существующее.FieldValue = пара.Value;
            существующее.UpdatedAt = DateTime.UtcNow;
            await _поля.UpdateAsync(существующее, cancellationToken).ConfigureAwait(false);
            изменено++;
        }

        return изменено;
    }

    /// <summary>
    /// Нормализует часть ключа слияния: обрезка краёв, схлопывание пробелов,
    /// верхний регистр, удаление кавычек.
    /// </summary>
    /// <param name="value">Исходное значение.</param>
    /// <returns>Нормализованное значение ключа.</returns>
    public static string NormalizeKeyPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var символы = value
            .Where(символ => символ is not ('"' or '«' or '»' or '„'))
            .Select(char.ToUpperInvariant)
            .ToArray();

        var текст = new string(символы);
        var части = текст.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', части);
    }

    /// <summary>
    /// Строит естественный ключ слияния записи: ИНН, а при его отсутствии —
    /// нормализованное наименование и почтовый индекс.
    /// </summary>
    /// <param name="draft">Черновик контрагента.</param>
    /// <returns>Ключ слияния либо пустая строка, если ключ не определён.</returns>
    public static string BuildMergeKey(CounterpartyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var инн = NormalizeKeyPart(draft.Inn);

        if (инн.Length > 0)
        {
            return $"ИНН:{инн}";
        }

        var имя = NormalizeKeyPart(draft.Name);

        if (имя.Length == 0)
        {
            return string.Empty;
        }

        return $"НАИМЕНОВАНИЕ:{имя}|ИНДЕКС:{NormalizeKeyPart(draft.PostalZip)}";
    }

    /// <summary>
    /// Собирает отчёт о слиянии по результатам по каждой записи.
    /// </summary>
    /// <param name="результаты">Результаты по записям.</param>
    /// <returns>Отчёт о слиянии.</returns>
    private static MergeReport СобратьОтчёт(IReadOnlyList<ImportMergeOutcome> результаты)
    {
        var создано = 0;
        var обновлено = 0;
        var пропущено = 0;
        var конфликтов = 0;
        var детали = new List<string>();

        foreach (var результат in результаты)
        {
            switch (результат.Action)
            {
                case ImportMergeAction.Created:
                    создано++;
                    break;
                case ImportMergeAction.Updated:
                    обновлено++;
                    break;
                default:
                    пропущено++;
                    break;
            }

            if (!результат.HasConflict)
            {
                continue;
            }

            конфликтов++;
            детали.AddRange(результат.ConflictDetails);
        }

        return new MergeReport
        {
            Created = создано,
            Updated = обновлено,
            Skipped = пропущено,
            Conflicts = конфликтов,
            ConflictDetails = детали,
        };
    }

    /// <summary>
    /// Ищет кандидата слияния по естественному ключу.
    /// </summary>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Найденный контрагент либо <see langword="null"/>.</returns>
    private async Task<Counterparty?> НайтиСовпадение(CounterpartyDraft черновик, CancellationToken cancellationToken)
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

        var нормализованное = NormalizeKeyPart(имя);

        if (нормализованное.Length == 0
            || string.Equals(нормализованное, имя, StringComparison.Ordinal))
        {
            return null;
        }

        return await _репозиторий
            .FindByMergeKeyAsync(инн, нормализованное, индекс, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Описывает ключ слияния для отчёта.
    /// </summary>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <returns>Текстовое описание ключа.</returns>
    private static string ОписатьКлюч(CounterpartyDraft черновик)
    {
        if (!string.IsNullOrWhiteSpace(черновик.Inn))
        {
            return $"ИНН {черновик.Inn.Trim()}";
        }

        var индекс = string.IsNullOrWhiteSpace(черновик.PostalZip) ? string.Empty : черновик.PostalZip.Trim();

        return $"наименование «{(черновик.Name ?? string.Empty).Trim()}»{(индекс.Length > 0 ? $", индекс {индекс}" : string.Empty)}";
    }

    /// <summary>
    /// Создаёт контрагента по черновику и описанию пакета.
    /// </summary>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <param name="batch">Пакет импорта.</param>
    /// <returns>Новая запись контрагента.</returns>
    private static Counterparty СоздатьКонтрагента(CounterpartyDraft черновик, ImportBatch batch)
    {
        var контрагент = new Counterparty
        {
            Type = черновик.Type,
            Name = (черновик.Name ?? string.Empty).Trim(),
            ShortName = Нормализовать(черновик.ShortName),
            Inn = Нормализовать(черновик.Inn),
            Kpp = Нормализовать(черновик.Kpp),
            Ogrn = Нормализовать(черновик.Ogrn),
            Phone = Нормализовать(черновик.Phone),
            Email = Нормализовать(черновик.Email),
            SpecialNotes = Нормализовать(черновик.Note),
            GroupId = черновик.GroupId,
            IsVip = черновик.IsVip,
            IsActive = true,
            Source = batch.SourceDescription,
        };

        контрагент.PostalAddress = СоздатьАдрес(черновик.PostalZip, черновик.PostalLines);
        контрагент.LegalAddress = СоздатьАдрес(черновик.LegalZip, черновик.LegalLines);
        контрагент.ActualAddress = СоздатьАдрес(черновик.ActualZip, черновик.ActualLines);

        return контрагент;
    }

    /// <summary>
    /// Создаёт адрес по индексу и строкам; некорректный индекс адрес не создаёт.
    /// </summary>
    /// <param name="zip">Почтовый индекс.</param>
    /// <param name="lines">Адресные строки.</param>
    /// <returns>Адрес либо <see langword="null"/>.</returns>
    private static Address? СоздатьАдрес(string zip, string[] lines)
    {
        if (string.IsNullOrWhiteSpace(zip) || lines is null || lines.Length == 0)
        {
            return null;
        }

        if (lines.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        var адрес = Address.Create(zip.Trim(), lines, null);

        return адрес.IsSuccess ? адрес.Value : null;
    }

    /// <summary>
    /// Дополняет пустые поля существующей записи импортируемыми значениями.
    /// </summary>
    /// <param name="существующий">Существующая запись.</param>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <param name="batch">Пакет импорта.</param>
    /// <param name="конфликты">Накопитель описаний конфликтов.</param>
    /// <returns><see langword="true"/>, если запись изменена.</returns>
    private static bool СлитьПустыми(
        Counterparty существующий,
        CounterpartyDraft черновик,
        ImportBatch batch,
        List<string> конфликты)
    {
        var изменено = false;

        изменено |= СлитьЗначение(
            () => существующий.Name,
            значение => существующий.Name = значение,
            черновик.Name,
            "Наименование",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьЗначение(
            () => существующий.ShortName,
            значение => существующий.ShortName = значение,
            черновик.ShortName,
            "Сокращённое наименование",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьЗначение(
            () => существующий.Inn,
            значение => существующий.Inn = значение,
            черновик.Inn,
            "ИНН",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьЗначение(
            () => существующий.Kpp,
            значение => существующий.Kpp = значение,
            черновик.Kpp,
            "КПП",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьЗначение(
            () => существующий.Ogrn,
            значение => существующий.Ogrn = значение,
            черновик.Ogrn,
            "ОГРН",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьЗначение(
            () => существующий.Phone,
            значение => существующий.Phone = значение,
            черновик.Phone,
            "Телефон",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьЗначение(
            () => существующий.Email,
            значение => существующий.Email = значение,
            черновик.Email,
            "Электронная почта",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьЗначение(
            () => существующий.SpecialNotes,
            значение => существующий.SpecialNotes = значение,
            черновик.Note,
            "Примечание",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьЗначение(
            () => существующий.Source,
            значение => существующий.Source = значение,
            batch.SourceDescription,
            "Источник данных",
            конфликты,
            толькоПустые: true,
            сообщатьОКонфликте: false);

        if (существующий.GroupId is null && черновик.GroupId is not null)
        {
            существующий.GroupId = черновик.GroupId;
            изменено = true;
        }

        if (!существующий.IsVip && черновик.IsVip)
        {
            существующий.IsVip = true;
            изменено = true;
        }

        изменено |= СлитьАдрес(
            () => существующий.PostalAddress,
            значение => существующий.PostalAddress = значение,
            черновик.PostalZip,
            черновик.PostalLines,
            "Почтовый адрес",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьАдрес(
            () => существующий.LegalAddress,
            значение => существующий.LegalAddress = значение,
            черновик.LegalZip,
            черновик.LegalLines,
            "Юридический адрес",
            конфликты,
            толькоПустые: true);

        изменено |= СлитьАдрес(
            () => существующий.ActualAddress,
            значение => существующий.ActualAddress = значение,
            черновик.ActualZip,
            черновик.ActualLines,
            "Фактический адрес",
            конфликты,
            толькоПустые: true);

        return изменено;
    }

    /// <summary>
    /// Перезаписывает непустые значения существующей записи импортируемыми.
    /// </summary>
    /// <param name="существующий">Существующая запись.</param>
    /// <param name="черновик">Черновик контрагента.</param>
    /// <param name="batch">Пакет импорта.</param>
    /// <param name="конфликты">Накопитель описаний конфликтов.</param>
    private static void СлитьПерезаписью(
        Counterparty существующий,
        CounterpartyDraft черновик,
        ImportBatch batch,
        List<string> конфликты)
    {
        СлитьЗначение(
            () => существующий.Name,
            значение => существующий.Name = значение,
            черновик.Name,
            "Наименование",
            конфликты,
            толькоПустые: false);

        СлитьЗначение(
            () => существующий.ShortName,
            значение => существующий.ShortName = значение,
            черновик.ShortName,
            "Сокращённое наименование",
            конфликты,
            толькоПустые: false);

        СлитьЗначение(
            () => существующий.Inn,
            значение => существующий.Inn = значение,
            черновик.Inn,
            "ИНН",
            конфликты,
            толькоПустые: false);

        СлитьЗначение(
            () => существующий.Kpp,
            значение => существующий.Kpp = значение,
            черновик.Kpp,
            "КПП",
            конфликты,
            толькоПустые: false);

        СлитьЗначение(
            () => существующий.Ogrn,
            значение => существующий.Ogrn = значение,
            черновик.Ogrn,
            "ОГРН",
            конфликты,
            толькоПустые: false);

        СлитьЗначение(
            () => существующий.Phone,
            значение => существующий.Phone = значение,
            черновик.Phone,
            "Телефон",
            конфликты,
            толькоПустые: false);

        СлитьЗначение(
            () => существующий.Email,
            значение => существующий.Email = значение,
            черновик.Email,
            "Электронная почта",
            конфликты,
            толькоПустые: false);

        СлитьЗначение(
            () => существующий.SpecialNotes,
            значение => существующий.SpecialNotes = значение,
            черновик.Note,
            "Примечание",
            конфликты,
            толькоПустые: false);

        СлитьЗначение(
            () => существующий.Source,
            значение => существующий.Source = значение,
            batch.SourceDescription,
            "Источник данных",
            конфликты,
            толькоПустые: false);

        if (черновик.GroupId is not null && существующий.GroupId != черновик.GroupId)
        {
            существующий.GroupId = черновик.GroupId;
        }

        if (черновик.IsVip && !существующий.IsVip)
        {
            существующий.IsVip = true;
        }

        СлитьАдрес(
            () => существующий.PostalAddress,
            значение => существующий.PostalAddress = значение,
            черновик.PostalZip,
            черновик.PostalLines,
            "Почтовый адрес",
            конфликты,
            толькоПустые: false);

        СлитьАдрес(
            () => существующий.LegalAddress,
            значение => существующий.LegalAddress = значение,
            черновик.LegalZip,
            черновик.LegalLines,
            "Юридический адрес",
            конфликты,
            толькоПустые: false);

        СлитьАдрес(
            () => существующий.ActualAddress,
            значение => существующий.ActualAddress = значение,
            черновик.ActualZip,
            черновик.ActualLines,
            "Фактический адрес",
            конфликты,
            толькоПустые: false);
    }

    /// <summary>
    /// Сливает одно текстовое поле по политике слияния.
    /// </summary>
    /// <param name="получить">Чтение текущего значения поля.</param>
    /// <param name="установить">Запись нового значения поля.</param>
    /// <param name="входящее">Импортируемое значение.</param>
    /// <param name="названиеПоля">Название поля для отчёта.</param>
    /// <param name="конфликты">Накопитель описаний конфликтов.</param>
    /// <param name="толькоПустые">Признак политики «дополнить пустые».</param>
    /// <param name="сообщатьОКонфликте">
    /// Признак фиксации расхождения значений в отчёте. Для признака источника
    /// данных расхождение не фиксируется: смена источника при каждом импорте —
    /// обычное дело.
    /// </param>
    /// <returns><see langword="true"/>, если значение изменено.</returns>
    private static bool СлитьЗначение(
        Func<string?> получить,
        Action<string> установить,
        string? входящее,
        string названиеПоля,
        List<string> конфликты,
        bool толькоПустые,
        bool сообщатьОКонфликте = true)
    {
        if (string.IsNullOrWhiteSpace(входящее))
        {
            return false;
        }

        var текущее = получить();
        var новое = входящее.Trim();

        if (толькоПустые)
        {
            if (!string.IsNullOrWhiteSpace(текущее))
            {
                if (сообщатьОКонфликте
                    && !string.Equals(текущее.Trim(), новое, StringComparison.OrdinalIgnoreCase))
                {
                    конфликты.Add(
                        $"Поле «{названиеПоля}»: существующее значение «{текущее.Trim()}» отличается от импортируемого «{новое}» — значение не перезаписано.");
                }

                return false;
            }
        }
        else if (string.Equals(текущее?.Trim(), новое, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        установить(новое);

        return true;
    }

    /// <summary>
    /// Сливает адрес по политике слияния.
    /// </summary>
    /// <param name="получить">Чтение текущего адреса.</param>
    /// <param name="установить">Запись нового адреса.</param>
    /// <param name="zip">Почтовый индекс импорта.</param>
    /// <param name="lines">Адресные строки импорта.</param>
    /// <param name="названиеПоля">Название поля для отчёта.</param>
    /// <param name="конфликты">Накопитель описаний конфликтов.</param>
    /// <param name="толькоПустые">Признак политики «дополнить пустые».</param>
    /// <returns><see langword="true"/>, если адрес изменён.</returns>
    private static bool СлитьАдрес(
        Func<Address?> получить,
        Action<Address?> установить,
        string zip,
        string[] lines,
        string названиеПоля,
        List<string> конфликты,
        bool толькоПустые)
    {
        var новый = СоздатьАдрес(zip, lines);

        if (новый is null)
        {
            return false;
        }

        var текущий = получить();

        if (текущий is null)
        {
            установить(новый);
            return true;
        }

        var различия = СравнитьАдреса(текущий, новый);

        if (различия.Count == 0)
        {
            return false;
        }

        if (толькоПустые)
        {
            конфликты.Add(
                $"Поле «{названиеПоля}»: существующий адрес отличается от импортируемого ({string.Join("; ", различия)}) — адрес не перезаписан.");
            return false;
        }

        установить(новый);

        return true;
    }

    /// <summary>
    /// Сравнивает два адреса и возвращает список расхождений.
    /// </summary>
    /// <param name="первый">Первый адрес.</param>
    /// <param name="второй">Второй адрес.</param>
    /// <returns>Список расхождений; пустой список — адреса совпадают.</returns>
    private static List<string> СравнитьАдреса(Address первый, Address второй)
    {
        var различия = new List<string>();

        if (!string.Equals(первый.Zip, второй.Zip, StringComparison.Ordinal))
        {
            различия.Add($"индекс «{первый.Zip}» и «{второй.Zip}»");
        }

        for (var i = 0; i < Math.Min(первый.Lines.Count, второй.Lines.Count); i++)
        {
            var слева = первый.Lines[i] ?? string.Empty;
            var справа = второй.Lines[i] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(слева) || string.IsNullOrWhiteSpace(справа))
            {
                continue;
            }

            if (!string.Equals(слева.Trim(), справа.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                различия.Add($"строка {i + 1} «{слева.Trim()}» и «{справа.Trim()}»");
            }
        }

        return различия;
    }

    /// <summary>
    /// Нормализует необязательное текстовое значение.
    /// </summary>
    /// <param name="значение">Исходное значение.</param>
    /// <returns>Обрезанное значение либо <see langword="null"/>.</returns>
    private static string? Нормализовать(string? значение)
        => string.IsNullOrWhiteSpace(значение) ? null : значение.Trim();
}
