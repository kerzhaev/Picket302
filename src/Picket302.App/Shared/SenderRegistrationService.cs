using Picket302.Domain.Common;
using Picket302.Domain.Entities;
using Picket302.Domain.Services;

namespace Picket302.App.Shared;

/// <summary>
/// Сохранение карточки и её пользовательских полей для регистрации отправителя.
/// </summary>
/// <remarks>
/// Узкий контракт нужен, чтобы сценарий регистрации можно было проверить без
/// поднятия всего шлюза: тест подменяет только эти две операции.
/// </remarks>
public interface ISenderPersistence
{
    /// <summary>Сохраняет карточку контрагента (создание или обновление).</summary>
    Task<Result<Counterparty>> СохранитьКарточкуAsync(
        Counterparty карточка,
        CancellationToken токен = default);

    /// <summary>Записывает одно пользовательское поле контрагента.</summary>
    Task<Result<CounterpartyField>> ЗаписатьПолеAsync(
        long идентификатор,
        string имя,
        string значение,
        CancellationToken токен = default);
}

/// <summary>
/// Адаптер узкого контракта поверх штатного шлюза данных.
/// </summary>
public sealed class GatewaySenderPersistence : ISenderPersistence
{
    private readonly IDataGateway _шлюз;

    /// <summary>Создаёт адаптер поверх шлюза.</summary>
    /// <param name="шлюз">Шлюз данных приложения.</param>
    public GatewaySenderPersistence(IDataGateway шлюз)
    {
        ArgumentNullException.ThrowIfNull(шлюз);
        _шлюз = шлюз;
    }

    /// <inheritdoc />
    public Task<Result<Counterparty>> СохранитьКарточкуAsync(
        Counterparty карточка,
        CancellationToken токен = default)
        => _шлюз.СохранитьКонтрагентаAsync(карточка, токен);

    /// <inheritdoc />
    public Task<Result<CounterpartyField>> ЗаписатьПолеAsync(
        long идентификатор,
        string имя,
        string значение,
        CancellationToken токен = default)
        => _шлюз.ЗаписатьПолеКонтрагентаAsync(идентификатор, имя, значение, токен);
}

/// <summary>Исход регистрации отправителя.</summary>
public enum SenderRegistrationStatus
{
    /// <summary>Карточка и все дополнительные поля сохранены.</summary>
    Создан = 0,

    /// <summary>Карточка не сохранена: состояние окна менять нельзя.</summary>
    НеСоздан = 1,

    /// <summary>
    /// Карточка сохранена, но часть пользовательских полей — нет. Запись уже
    /// существует, повтор должен обновлять её по идентификатору.
    /// </summary>
    СозданБезДополнительныхПолей = 2,
}

/// <summary>Результат регистрации отправителя.</summary>
/// <param name="Статус">Исход операции.</param>
/// <param name="Идентификатор">Идентификатор сохранённой карточки (0 при отказе).</param>
/// <param name="Ошибка">Текст ошибки сохранения карточки либо первого неудачного поля.</param>
/// <param name="НесохранённыеПоля">Имена пользовательских полей, которые не записались.</param>
public sealed record SenderRegistrationResult(
    SenderRegistrationStatus Статус,
    long Идентификатор,
    string? Ошибка,
    IReadOnlyList<string> НесохранённыеПоля);

/// <summary>
/// Регистрация отправителя: полная карточка, дополнительные поля и выбор
/// текущего отправителя.
/// </summary>
/// <remarks>
/// <para>
/// Карточка сохраняется целиком тем же путём, что и карточка получателя:
/// сокращённое наименование, телефон, почта, примечание и адресные срезы не
/// теряются.
/// </para>
/// <para>
/// Текущий отправитель назначается только при полном успехе. Если карточка
/// сохранена, а дополнительные поля — нет, возвращается отдельный исход:
/// запись уже существует, и повторное сохранение обязано обновлять её по
/// идентификатору, а не создавать дубль.
/// </para>
/// </remarks>
public sealed class SenderRegistrationService
{
    private readonly ISenderPersistence _хранилище;
    private readonly CurrentSenderContext _текущийОтправитель;

    /// <summary>Создаёт службу регистрации.</summary>
    /// <param name="хранилище">Сохранение карточки и полей.</param>
    /// <param name="текущийОтправитель">Контекст текущего отправителя.</param>
    public SenderRegistrationService(
        ISenderPersistence хранилище,
        CurrentSenderContext текущийОтправитель)
    {
        ArgumentNullException.ThrowIfNull(хранилище);
        ArgumentNullException.ThrowIfNull(текущийОтправитель);

        _хранилище = хранилище;
        _текущийОтправитель = текущийОтправитель;
    }

    /// <summary>
    /// Сохраняет карточку отправителя и его дополнительные поля.
    /// </summary>
    /// <param name="карточка">Карточка из формы.</param>
    /// <param name="пользовательскиеПоля">Дополнительные поля формы.</param>
    /// <param name="токен">Токен отмены.</param>
    /// <returns>Исход операции; текущий отправитель меняется только при полном успехе.</returns>
    public async Task<SenderRegistrationResult> СоздатьAsync(
        Counterparty карточка,
        IReadOnlyList<(string Имя, string Значение)> пользовательскиеПоля,
        CancellationToken токен = default)
    {
        ArgumentNullException.ThrowIfNull(карточка);

        карточка.IsSender = true;

        var сохранение = await _хранилище
            .СохранитьКарточкуAsync(карточка, токен)
            .ConfigureAwait(false);

        if (сохранение.IsFailure)
        {
            return new SenderRegistrationResult(
                SenderRegistrationStatus.НеСоздан,
                0,
                сохранение.Error,
                Array.Empty<string>());
        }

        var идентификатор = сохранение.Value.Id;
        var неудачные = new List<string>();
        string? перваяОшибка = null;

        foreach (var (имя, значение) in пользовательскиеПоля ?? [])
        {
            if (string.IsNullOrWhiteSpace(имя))
            {
                continue;
            }

            var запись = await _хранилище
                .ЗаписатьПолеAsync(идентификатор, имя, значение, токен)
                .ConfigureAwait(false);

            if (запись.IsFailure)
            {
                неудачные.Add(имя);
                перваяОшибка ??= запись.Error;
            }
        }

        if (неудачные.Count > 0)
        {
            // Карточка существует: текущего отправителя не трогаем, но и не
            // выдаём операцию за неудачную целиком — иначе оператор создаст дубль.
            return new SenderRegistrationResult(
                SenderRegistrationStatus.СозданБезДополнительныхПолей,
                идентификатор,
                перваяОшибка,
                неудачные);
        }

        _текущийОтправитель.SetGlobalSender(идентификатор);

        return new SenderRegistrationResult(
            SenderRegistrationStatus.Создан,
            идентификатор,
            null,
            Array.Empty<string>());
    }
}
