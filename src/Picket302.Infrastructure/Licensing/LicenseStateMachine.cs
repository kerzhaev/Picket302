using System.Globalization;
using Picket302.Domain.Licensing;

namespace Picket302.Infrastructure.Licensing;

/// <summary>
/// Снимок объёма данных программы: число контрагентов и созданных документов.
/// </summary>
/// <param name="Контрагенты">Число контрагентов в адресной книге.</param>
/// <param name="Документы">Число созданных документов.</param>
public sealed record СнимокИспользования(int Контрагенты, int Документы);

/// <summary>
/// Автомат состояний лицензии (ТЗ §8.10, функция 119: «состояние лицензии —
/// один автомат, вычисляемый при старте и хранимый в одном месте:
/// Trial / Licensed / Expired»).
/// </summary>
/// <remarks>
/// <para>
/// Состояние вычисляется в конструкторе и обновляется вызовом
/// <see cref="EvaluateState"/>; проверки представления читают единую точку —
/// свойство <see cref="Status"/>.
/// </para>
/// <para>
/// Источники состояния: наличие лицензии в хранилище, момент истечения срока
/// и лимиты редакции (<see cref="Редакции"/>). Пробный период отсчитывается от
/// момента первого запуска, который записывается в ветку реестра лицензирования
/// в значение <c>TrialStartedAt</c>.
/// </para>
/// <para>
/// Деградация корректная: истечение срока и снятие ключа не удаляют данные;
/// чтение остаётся доступным, а создание записей и печать партий блокируются
/// с понятным сообщением и числом оставшихся дней.
/// </para>
/// </remarks>
public sealed class LicenseStateMachine
{
    /// <summary>Имя значения реестра с началом пробного периода.</summary>
    public const string ИмяНачалаПробногоПериода = "TrialStartedAt";

    private readonly LicenseStore _хранилище;
    private readonly LicensingRegistry _реестр;
    private readonly Func<DateTime> _часы;
    private readonly Func<СнимокИспользования>? _счётчики;

    /// <summary>
    /// Создаёт автомат состояний лицензии и вычисляет начальное состояние.
    /// </summary>
    /// <param name="хранилище">Хранилище данных лицензии.</param>
    /// <param name="реестр">
    /// Хранилище значений ветки лицензирования; по умолчанию используется
    /// хранилище, связанное с <paramref name="хранилище"/>.
    /// </param>
    /// <param name="часы">
    /// Источник текущего времени (UTC); по умолчанию — системные часы.
    /// </param>
    /// <param name="счётчики">
    /// Источник объёма данных программы для проверки лимита пробного периода;
    /// <see langword="null"/> — лимит объёма не проверяется.
    /// </param>
    /// <exception cref="ArgumentNullException">Хранилище не задано.</exception>
    public LicenseStateMachine(
        LicenseStore хранилище,
        LicensingRegistry? реестр = null,
        Func<DateTime>? часы = null,
        Func<СнимокИспользования>? счётчики = null)
    {
        ArgumentNullException.ThrowIfNull(хранилище);

        _хранилище = хранилище;
        _реестр = реестр ?? LicensingRegistry.Windows();
        _часы = часы ?? (() => DateTime.UtcNow);
        _счётчики = счётчики;

        EvaluateState();
    }

    /// <summary>
    /// Текущий статус лицензии — единая точка чтения для проверок интерфейса
    /// (ТЗ §8.10, функция 119).
    /// </summary>
    public LicenseStatus Status { get; private set; } = null!;

    /// <summary>
    /// Число оставшихся дней срока; <see cref="int.MaxValue"/> — срок не
    /// ограничен.
    /// </summary>
    public int DaysRemaining { get; private set; }

    /// <summary>
    /// Признак доступности создания записей: в состоянии лицензии доступно
    /// всегда, в пробном режиме — пока не исчерпан лимит.
    /// </summary>
    public bool CanCreate { get; private set; }

    /// <summary>
    /// Признак доступности печати партий: в состоянии лицензии доступна всегда,
    /// в пробном режиме — пока не исчерпан лимит.
    /// </summary>
    public bool CanPrintBatches { get; private set; }

    /// <summary>
    /// Редакция, действующая в текущем состоянии.
    /// </summary>
    public Edition ТекущаяРедакция => Status.Edition;

    /// <summary>
    /// Лимиты действующей редакции.
    /// </summary>
    public LicenseLimits ТекущиеЛимиты => Редакции.Описание(ТекущаяРедакция).Limits;

    /// <summary>
    /// Вычисляет состояние лицензии и сохраняет результат
    /// (ТЗ §8.10, функция 119: <c>public LicenseState EvaluateState()</c>).
    /// </summary>
    /// <returns>Вычисленное состояние лицензии.</returns>
    public LicenseState EvaluateState()
    {
        var сейчас = _часы().ToUniversalTime();

        LicenseState состояние;
        Edition редакция;
        DateTime? истечение = null;
        string? владелец = null;
        string? машина = null;
        string? сообщение = null;
        var дни = 0;
        var лимитИсчерпан = false;

        if (_хранилище.TryLoad(out var лицензия))
        {
            редакция = лицензия.Edition;
            истечение = лицензия.ExpiresAtUtc;
            владелец = лицензия.Owner;
            машина = string.IsNullOrWhiteSpace(лицензия.MachineId)
                ? _хранилище.MachineId
                : лицензия.MachineId;

            if (лицензия.ExpiresAtUtc is null || лицензия.ExpiresAtUtc > сейчас)
            {
                состояние = LicenseState.Licensed;
                дни = лицензия.ExpiresAtUtc is null ? int.MaxValue : ОсталосьДней(лицензия.ExpiresAtUtc.Value, сейчас);
            }
            else
            {
                состояние = LicenseState.Expired;
                дни = 0;
                сообщение = "Срок лицензии истёк.";
            }
        }
        else
        {
            редакция = Edition.Free;
            машина = _хранилище.MachineId;

            var лимиты = Редакции.Описание(редакция).Limits;
            var начало = НачалоПробногоПериода(сейчас);
            var срок = лимиты.TrialDays ?? Редакции.ПробныйСрокДнейПоУмолчанию;
            var конец = начало.AddDays(срок);

            if (ПревышенЛимитОбъёма(лимиты))
            {
                лимитИсчерпан = true;
                состояние = LicenseState.Expired;
                дни = 0;
                сообщение = "Исчерпан лимит пробного периода: создание новых записей и печать партий недоступны. Существующие данные сохранены.";
            }
            else if (сейчас < конец)
            {
                состояние = LicenseState.Trial;
                дни = ОсталосьДней(конец, сейчас);
            }
            else
            {
                состояние = LicenseState.Expired;
                дни = 0;
                сообщение = "Пробный период закончился.";
            }
        }

        Status = new LicenseStatus(состояние, редакция, истечение, владелец, машина, дни, сообщение);
        DaysRemaining = дни;

        CanCreate = состояние == LicenseState.Licensed
            || (состояние == LicenseState.Trial && !лимитИсчерпан);
        CanPrintBatches = CanCreate;

        return состояние;
    }

    /// <summary>
    /// Определяет, исчерпан ли лимит объёма пробного периода.
    /// </summary>
    /// <param name="counterpartyCount">Число контрагентов в адресной книге.</param>
    /// <param name="documentCount">Число созданных документов.</param>
    /// <returns>
    /// <see langword="true"/> — лимит исчерпан, создание и печать партий
    /// заблокированы; <see langword="false"/> — лимит не исчерпан.
    /// </returns>
    /// <remarks>
    /// Достижение предела считается исчерпанием: при числе контрагентов, равном
    /// пределу, создание новых записей недоступно. Существующие записи при этом
    /// не удаляются (ТЗ §8.10, функция 119: «уменьшение лимита не удаляет
    /// существующие записи»).
    /// </remarks>
    public bool IsVolumeLimitReached(int counterpartyCount, int documentCount)
        => ПревышенЛимит(ТекущиеЛимиты, counterpartyCount, documentCount);

    /// <summary>
    /// Возвращает начало пробного периода; при первом запуске момент
    /// фиксируется в ветке реестра лицензирования.
    /// </summary>
    /// <param name="сейчас">Текущий момент времени (UTC).</param>
    /// <returns>Начало пробного периода (UTC).</returns>
    private DateTime НачалоПробногоПериода(DateTime сейчас)
    {
        var сохранённое = _реестр.Прочитать(ИмяНачалаПробногоПериода);

        if (!string.IsNullOrWhiteSpace(сохранённое)
            && DateTime.TryParse(
                сохранённое,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var значение))
        {
            return значение.ToUniversalTime();
        }

        _реестр.Записать(ИмяНачалаПробногоПериода, сейчас.ToString("O", CultureInfo.InvariantCulture));

        return сейчас;
    }

    /// <summary>
    /// Определяет, превышен ли лимит объёма по данным счётчиков.
    /// </summary>
    /// <param name="лимиты">Лимиты действующей редакции.</param>
    /// <returns><see langword="true"/> — лимит исчерпан.</returns>
    private bool ПревышенЛимитОбъёма(LicenseLimits лимиты)
    {
        var счётчик = _счётчики;

        if (счётчик is null)
        {
            return false;
        }

        var снимок = счётчик();

        return ПревышенЛимит(лимиты, снимок.Контрагенты, снимок.Документы);
    }

    /// <summary>
    /// Сравнивает объём данных с лимитами редакции.
    /// </summary>
    /// <param name="лимиты">Лимиты редакции.</param>
    /// <param name="контрагенты">Число контрагентов.</param>
    /// <param name="документы">Число документов.</param>
    /// <returns><see langword="true"/> — лимит исчерпан.</returns>
    private static bool ПревышенЛимит(LicenseLimits лимиты, int контрагенты, int документы)
    {
        if (лимиты.MaxCounterparties is int пределКонтрагентов && контрагенты >= пределКонтрагентов)
        {
            return true;
        }

        if (лимиты.MaxDocuments is int пределДокументов && документы >= пределДокументов)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Возвращает число полных и неполных дней до окончания срока.
    /// </summary>
    /// <param name="конец">Момент окончания срока (UTC).</param>
    /// <param name="сейчас">Текущий момент времени (UTC).</param>
    /// <returns>Число оставшихся дней (не меньше нуля).</returns>
    private static int ОсталосьДней(DateTime конец, DateTime сейчас)
    {
        var разница = конец - сейчас;

        if (разница <= TimeSpan.Zero)
        {
            return 0;
        }

        return (int)Math.Ceiling(разница.TotalDays);
    }
}
