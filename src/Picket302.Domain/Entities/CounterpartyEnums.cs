namespace Picket302.Domain.Entities;

/// <summary>
/// Тип контрагента (ТЗ §3.1.1, колонка <c>counterparties.type</c>).
/// </summary>
public enum CounterpartyType
{
    /// <summary>Юридическое лицо.</summary>
    LegalEntity = 0,

    /// <summary>Физическое лицо.</summary>
    NaturalPerson = 1,
}

/// <summary>
/// Тип адреса по умолчанию (ТЗ §3.1.1, колонка <c>counterparties.address_main_type</c>).
/// </summary>
public enum AddressMainType
{
    /// <summary>Юридический адрес.</summary>
    Legal = 0,

    /// <summary>Почтовый адрес.</summary>
    Postal = 1,

    /// <summary>Фактический адрес.</summary>
    Actual = 2,
}

/// <summary>
/// Тип пользовательского поля контрагента (ТЗ §3.1.9, колонка
/// <c>counterparty_fields.field_type</c>).
/// </summary>
public enum CounterpartyFieldType
{
    /// <summary>Строка.</summary>
    Text = 0,

    /// <summary>Число.</summary>
    Number = 1,

    /// <summary>Дата.</summary>
    Date = 2,
}
