using Picket302.Domain.Entities;

namespace Picket302.Domain.ValueObjects;

/// <summary>
/// Три независимых среза адресации контрагента (ТЗ §8.1, функция 3:
/// «Три независимых адреса контрагента»).
/// </summary>
/// <remarks>
/// <para>
/// Срез — это один из трёх адресов: юридический, фактический и почтовый.
/// Каждый срез хранится самостоятельно; целевой срез для подстановки в
/// конверты по умолчанию задаётся флагом <c>makeMain</c> и сохраняется в
/// колонке <c>counterparties.address_main_type</c>.
/// </para>
/// <para>
/// Единственный источник модели адресации — этот тип: сущность
/// <see cref="Counterparty"/> обращается к срезам только через него, поэтому
/// альтернативного способа задать адрес в системе нет.
/// </para>
/// </remarks>
public sealed class AddressSet
{
    /// <summary>Юридический адрес.</summary>
    public Address? Legal { get; private set; }

    /// <summary>Фактический адрес.</summary>
    public Address? Actual { get; private set; }

    /// <summary>Почтовый адрес.</summary>
    public Address? Postal { get; private set; }

    /// <summary>Срез адреса, используемый по умолчанию.</summary>
    public AddressMainType MainType { get; private set; } = Counterparty.DefaultAddressMainType;

    /// <summary>
    /// Создаёт пустой набор срезов с основным почтовым срезом.
    /// </summary>
    public AddressSet()
    {
    }

    /// <summary>
    /// Создаёт набор срезов из готовых адресов.
    /// </summary>
    /// <param name="legal">Юридический адрес.</param>
    /// <param name="actual">Фактический адрес.</param>
    /// <param name="postal">Почтовый адрес.</param>
    /// <param name="mainType">Основной срез.</param>
    public AddressSet(Address? legal, Address? actual, Address? postal, AddressMainType mainType)
    {
        Legal = legal;
        Actual = actual;
        Postal = postal;
        MainType = mainType;
    }

    /// <summary>
    /// Заменяет адрес заданного среза; при <paramref name="makeMain"/> этот срез
    /// становится основным (ТЗ §8.1, функция 3).
    /// </summary>
    /// <param name="type">Тип среза адреса.</param>
    /// <param name="address">Новый адрес среза; <see langword="null"/> — адрес не заведён.</param>
    /// <param name="makeMain">Сделать срез основным.</param>
    public void SetAddress(AddressMainType type, Address? address, bool makeMain)
    {
        switch (type)
        {
            case AddressMainType.Legal:
                Legal = address;
                break;
            case AddressMainType.Actual:
                Actual = address;
                break;
            case AddressMainType.Postal:
                Postal = address;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный тип адреса.");
        }

        if (makeMain)
        {
            MainType = type;
        }
    }

    /// <summary>
    /// Назначает основной срез адреса (ТЗ §3.1.1, колонка
    /// <c>counterparties.address_main_type</c>).
    /// </summary>
    /// <param name="type">Срез, используемый по умолчанию.</param>
    public void SetMainType(AddressMainType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный тип адреса.");
        }

        MainType = type;
    }

    /// <summary>Возвращает адрес заданного среза.</summary>
    /// <param name="type">Тип среза адреса.</param>
    /// <returns>Адрес среза либо <see langword="null"/>, если срез не заведён.</returns>
    public Address? GetAddress(AddressMainType type) => type switch
    {
        AddressMainType.Legal => Legal,
        AddressMainType.Actual => Actual,
        AddressMainType.Postal => Postal,
        _ => null,
    };

    /// <summary>
    /// Адрес основного среза — тот, что подставляется в конверты по умолчанию.
    /// </summary>
    public Address? Main => GetAddress(MainType);
}
