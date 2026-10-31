using Picket302.Domain.Entities;

namespace Picket302.Domain.Contracts;

/// <summary>
/// Данные агента — отправителя (ТЗ §8.12, функция 157: «Отправители (агенты)»).
/// </summary>
public sealed class AgentDto
{
    /// <summary>Наименование или ФИО отправителя.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Тип контрагента.</summary>
    public CounterpartyType Type { get; set; } = CounterpartyType.LegalEntity;

    /// <summary>ИНН (10 знаков для ЮЛ, 12 для ФЛ) либо УНП.</summary>
    public string? Inn { get; set; }

    /// <summary>КПП юридического лица.</summary>
    public string? Kpp { get; set; }

    /// <summary>БИК банка.</summary>
    public string? Bik { get; set; }

    /// <summary>Номер счёта.</summary>
    public string? Account { get; set; }

    /// <summary>Наименование банка.</summary>
    public string? BankName { get; set; }

    /// <summary>Почтовый индекс основного адреса.</summary>
    public string? Zip { get; set; }

    /// <summary>Строки основного адреса 1..6.</summary>
    public IReadOnlyList<string> AddressLines { get; set; } = [];

    /// <summary>Подпись отправителя — выводится на конвертах.</summary>
    public string? Signature { get; set; }

    /// <summary>Признак отправителя (ТЗ §8.12, функция 139).</summary>
    public bool IsSender { get; set; } = true;
}
