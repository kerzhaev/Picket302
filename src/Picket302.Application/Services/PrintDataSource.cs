using Picket302.Domain.Dictionaries;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Printing;

namespace Picket302.Application.Services;

/// <summary>
/// Источник данных для печати: подстановка значений полей бланка по данным
/// отправлений (ТЗ §6.1: «[Данные Mail] + [Бланк .lbk]»; §3.2 — единая матрица
/// «колонка БД → тег шаблона → псевдоним импорта»).
/// </summary>
public sealed class PrintDataSource : IPrintDataSource
{
    private readonly IMailRepository _письма;
    private readonly ICounterpartyRepository _контрагенты;

    /// <summary>
    /// Создаёт источник данных печати.
    /// </summary>
    /// <param name="mails">Хранилище отправлений.</param>
    /// <param name="counterparties">Хранилище контрагентов.</param>
    public PrintDataSource(IMailRepository mails, ICounterpartyRepository counterparties)
    {
        ArgumentNullException.ThrowIfNull(mails);
        ArgumentNullException.ThrowIfNull(counterparties);

        _письма = mails;
        _контрагенты = counterparties;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrintableItem>> GetItemsAsync(
        IReadOnlyList<long> mailIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mailIds);

        var элементы = new List<PrintableItem>(mailIds.Count);

        foreach (var идентификатор in mailIds)
        {
            var письмо = await _письма.GetByIdAsync(идентификатор, cancellationToken).ConfigureAwait(false);

            if (письмо is null)
            {
                continue;
            }

            элементы.Add(await СобратьЭлементAsync(письмо, cancellationToken).ConfigureAwait(false));
        }

        return элементы;
    }

    private async Task<PrintableItem> СобратьЭлементAsync(
        Mail письмо,
        CancellationToken cancellationToken)
    {
        var отправитель = письмо.SenderId > 0
            ? await _контрагенты.GetByIdAsync(письмо.SenderId, cancellationToken).ConfigureAwait(false)
            : null;

        // Логотип получателя накладывается на конверт при печати: растровое
        // изображение хранится в колонке counterparties.logo.
        var получатель = письмо.CounterpartyId > 0
            ? await _контрагенты.GetByIdAsync(письмо.CounterpartyId, cancellationToken).ConfigureAwait(false)
            : null;

        var адресОтправителя = отправитель?.GetAddress(отправитель.AddressMainType);

        var значения = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var поле in FieldDictionary.All)
        {
            var значение = ПолучитьЗначение(письмо, отправитель, адресОтправителя, поле.ColumnName);

            if (значение is not null)
            {
                значения[поле.TemplateTag] = значение;
            }
        }

        return new PrintableItem
        {
            Id = письмо.Id,
            Values = значения,
            LogoBytes = получатель?.Logo,
        };
    }

    private static string? ПолучитьЗначение(
        Mail письмо,
        Counterparty? отправитель,
        Address? адресОтправителя,
        string колонка)
    {
        switch (колонка)
        {
            case FieldDictionary.ColumnToZip:
                return письмо.RecipientAddress?.Zip;

            case FieldDictionary.ColumnToStr1:
            case FieldDictionary.ColumnToStr2:
            case FieldDictionary.ColumnToStr3:
            case FieldDictionary.ColumnToStr4:
            case FieldDictionary.ColumnToStr5:
            case FieldDictionary.ColumnToStr6:
                return СтрокаАдреса(письмо.RecipientAddress, колонка, FieldDictionary.ColumnToStr1);

            case FieldDictionary.ColumnFromZip:
                return адресОтправителя?.Zip;

            case FieldDictionary.ColumnFromStr1:
            case FieldDictionary.ColumnFromStr2:
            case FieldDictionary.ColumnFromStr3:
            case FieldDictionary.ColumnFromStr4:
            case FieldDictionary.ColumnFromStr5:
            case FieldDictionary.ColumnFromStr6:
                return СтрокаАдреса(адресОтправителя, колонка, FieldDictionary.ColumnFromStr1);

            case FieldDictionary.ColumnCopyCount:
                return письмо.CopyCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

            case FieldDictionary.ColumnIsVip:
                return письмо.IsVip ? "1" : "0";

            case FieldDictionary.ColumnNote:
                return письмо.Note;

            case FieldDictionary.ColumnDeclaredValue:
                return письмо.DeclaredValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

            case FieldDictionary.ColumnFee:
                return письмо.Fee.ToString(System.Globalization.CultureInfo.InvariantCulture);

            case FieldDictionary.ColumnWeight:
                return письмо.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture);

            case FieldDictionary.ColumnPostIndexMachine:
                return письмо.RecipientAddress?.Zip;

            case FieldDictionary.ColumnId:
                return отправитель is null
                    ? null
                    : отправитель.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

            default:
                return null;
        }
    }

    private static string? СтрокаАдреса(Address? адрес, string колонка, string перваяКолонка)
    {
        if (адрес is null)
        {
            return null;
        }

        var индекс = колонка.Length - перваяКолонка.Length;

        if (индекс < 0 || индекс >= Address.LineCount)
        {
            return null;
        }

        return адрес.Lines[индекс];
    }
}

/// <summary>
/// Опция «Показывать список шаблонов перед печатью» (ТЗ §8.6, функция 82:
/// <c>Application.Services.PrintOptions</c>).
/// </summary>
/// <remarks>
/// Опция хранится в профиле пользователя (таблица <c>parameters</c>) и
/// применяется ко всем режимам печати F5/F6/F7. Дополнительно хранится
/// идентификатор последнего использованного шаблона: при выключенной опции
/// печать выполняется последним использованным шаблоном без диалога выбора.
/// </remarks>
public sealed class PrintOptions
{
    /// <summary>Ключ параметра в таблице <c>parameters</c>.</summary>
    public const string ShowTemplateListKey = "ShowTemplateListBeforePrint";

    /// <summary>Ключ параметра с идентификатором последнего использованного шаблона.</summary>
    public const string LastTemplateKey = "LastUsedPrintTemplateId";

    private readonly IParameterRepository _параметры;

    /// <summary>
    /// Создаёт службу параметров печати.
    /// </summary>
    /// <param name="parameters">Хранилище параметров.</param>
    public PrintOptions(IParameterRepository parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        _параметры = parameters;
    }

    /// <summary>
    /// Признак показа списка шаблонов перед печатью (ТЗ §8.6, функция 82).
    /// </summary>
    /// <remarks>
    /// Значение хранится в профиле пользователя под ключом
    /// <see cref="ShowTemplateListKey"/>; свойство отражает последнее
    /// прочитанное или записанное значение. По умолчанию список шаблонов
    /// показывается.
    /// </remarks>
    public bool ПоказыватьСписокШаблонов { get; private set; } = true;

    /// <summary>
    /// Идентификатор последнего использованного шаблона (null — шаблон ещё
    /// не выбирался).
    /// </summary>
    public long? ПоследнийШаблонId { get; private set; }

    /// <summary>
    /// Возвращает признак показа списка шаблонов перед печатью.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><see langword="true"/>, если список шаблонов показывается.</returns>
    public async Task<bool> GetShowTemplateListAsync(CancellationToken cancellationToken = default)
    {
        var значение = await _параметры
            .GetValueAsync(ShowTemplateListKey, cancellationToken)
            .ConfigureAwait(false);

        ПоказыватьСписокШаблонов = значение is null || !string.Equals(значение, "0", StringComparison.Ordinal);

        return ПоказыватьСписокШаблонов;
    }

    /// <summary>
    /// Сохраняет признак показа списка шаблонов перед печатью.
    /// </summary>
    /// <param name="value">Новое значение параметра.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    public async Task SetShowTemplateListAsync(bool value, CancellationToken cancellationToken = default)
    {
        await СохранитьAsync(ShowTemplateListKey, value ? "1" : "0", cancellationToken).ConfigureAwait(false);

        ПоказыватьСписокШаблонов = value;
    }

    /// <summary>
    /// Возвращает идентификатор последнего использованного шаблона.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Идентификатор шаблона либо <see langword="null"/>.</returns>
    public async Task<long?> GetLastTemplateIdAsync(CancellationToken cancellationToken = default)
    {
        var значение = await _параметры
            .GetValueAsync(LastTemplateKey, cancellationToken)
            .ConfigureAwait(false);

        ПоследнийШаблонId = long.TryParse(значение, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var идентификатор)
            ? идентификатор
            : null;

        return ПоследнийШаблонId;
    }

    /// <summary>
    /// Сохраняет идентификатор последнего использованного шаблона.
    /// </summary>
    /// <param name="templateId">Идентификатор шаблона либо <see langword="null"/>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача сохранения.</returns>
    public async Task SetLastTemplateIdAsync(long? templateId, CancellationToken cancellationToken = default)
    {
        var значение = templateId is null
            ? string.Empty
            : templateId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await СохранитьAsync(LastTemplateKey, значение, cancellationToken).ConfigureAwait(false);

        ПоследнийШаблонId = templateId;
    }

    private async Task СохранитьAsync(string имя, string значение, CancellationToken cancellationToken)
    {
        var параметр = await _параметры
            .GetByNameAsync(имя, cancellationToken)
            .ConfigureAwait(false);

        if (параметр is null)
        {
            await _параметры
                .AddAsync(
                    new Parameter { Name = имя, Value = значение },
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        параметр.Value = значение;
        await _параметры.UpdateAsync(параметр, cancellationToken).ConfigureAwait(false);
    }
}
