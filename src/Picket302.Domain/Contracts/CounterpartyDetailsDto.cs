using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;

namespace Picket302.Domain.Contracts;

/// <summary>
/// Реквизиты карточки контрагента, передаваемые на сохранение
/// (ТЗ §8.1, функция 1: «Карточка контрагента со всеми атрибутами»).
/// </summary>
/// <remarks>
/// DTO — единственный способ внести изменения в карточку: доменная сущность
/// принимает готовый набор значений, проверяет его правилами целостности и
/// применяет к себе одним действием.
/// </remarks>
public sealed class CounterpartyDetailsDto
{
    /// <summary>Тип контрагента: юридическое или физическое лицо.</summary>
    public CounterpartyType Type { get; set; }

    /// <summary>Полное официальное наименование или ФИО.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Сокращённое наименование.</summary>
    public string? ShortName { get; set; }

    /// <summary>ИНН (10 знаков для ЮЛ, 12 для ФЛ) либо УНП.</summary>
    public string? Inn { get; set; }

    /// <summary>КПП юридического лица.</summary>
    public string? Kpp { get; set; }

    /// <summary>ОГРН либо ОГРНИП.</summary>
    public string? Ogrn { get; set; }

    /// <summary>ОКФС.</summary>
    public string? Okfs { get; set; }

    /// <summary>ОКПО.</summary>
    public string? Okpo { get; set; }

    /// <summary>ОКВЭД.</summary>
    public string? Okved { get; set; }

    /// <summary>БИК банка.</summary>
    public string? Bik { get; set; }

    /// <summary>Корреспондентский счёт банка.</summary>
    public string? Ks { get; set; }

    /// <summary>Расчётный счёт контрагента.</summary>
    public string? Rs { get; set; }

    /// <summary>Наименование расчётно-кассового центра.</summary>
    public string? Rkc { get; set; }

    /// <summary>Наименование обслуживающего банка.</summary>
    public string? BankName { get; set; }

    /// <summary>Серия паспорта.</summary>
    public string? PassportSeries { get; set; }

    /// <summary>Номер паспорта.</summary>
    public string? PassportNumber { get; set; }

    /// <summary>Кем выдан паспорт.</summary>
    public string? PassportIssuedBy { get; set; }

    /// <summary>Дата выдачи паспорта.</summary>
    public DateTime? PassportIssuedDate { get; set; }

    /// <summary>Дата рождения.</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>Место рождения.</summary>
    public string? BirthPlace { get; set; }

    /// <summary>Должность руководителя или контактного лица.</summary>
    public string? Position { get; set; }

    /// <summary>ФИО первого лица.</summary>
    public string? DirectorFio { get; set; }

    /// <summary>Адрес электронной почты.</summary>
    public string? Email { get; set; }

    /// <summary>Телефон.</summary>
    public string? Phone { get; set; }

    /// <summary>Особые технологические отметки.</summary>
    public string? SpecialNotes { get; set; }

    /// <summary>Юридический адрес.</summary>
    public Address? LegalAddress { get; set; }

    /// <summary>Фактический адрес.</summary>
    public Address? ActualAddress { get; set; }

    /// <summary>Почтовый адрес.</summary>
    public Address? PostalAddress { get; set; }

    /// <summary>Срез адреса, используемый по умолчанию.</summary>
    public AddressMainType AddressMainType { get; set; } = Counterparty.DefaultAddressMainType;

    /// <summary>Группа (категория) адресной книги.</summary>
    public long? GroupId { get; set; }

    /// <summary>Признак «Избранный адрес».</summary>
    public bool IsVip { get; set; }

    /// <summary>Флаг доступности записи в качестве глобального отправителя.</summary>
    public bool IsSender { get; set; }

    /// <summary>Флаг активности записи.</summary>
    public bool IsActive { get; set; } = true;
}
