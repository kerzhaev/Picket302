using Picket302.Domain.ValueObjects;

namespace Picket302.Domain.Entities;

/// <summary>
/// Контрагент — юридическое или физическое лицо (ТЗ §3.1.1, таблица <c>counterparties</c>).
/// </summary>
/// <remarks>
/// Запись хранит базовые реквизиты и три независимых типа адреса: юридический,
/// почтовый и фактический. Адрес никогда не хранится одной строкой — каждая
/// адресная составляющая лежит в собственной колонке (ТЗ §3.1.19).
/// Удаление контрагента — мягкое: <see cref="IsDeleted"/> исключает запись
/// из рабочих выборок, окончательное удаление выполняется отдельной командой.
/// </remarks>
public sealed class Counterparty
{
    /// <summary>Тип адреса по умолчанию при создании записи (ТЗ §3.1.1).</summary>
    public const AddressMainType DefaultAddressMainType = AddressMainType.Postal;

    /// <summary>Идентификатор записи (первичный ключ).</summary>
    public long Id { get; set; }

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

    /// <summary>
    /// Логотип компании — растровый файл (PNG/BMP/JPEG), накладываемый на
    /// конверт (ТЗ §8.1, функция 47: «Таблица counterparties (колонка logo)»).
    /// </summary>
    public byte[]? Logo { get; set; }

    /// <summary>Юридический адрес.</summary>
    public Address? LegalAddress { get; set; }

    /// <summary>Почтовый адрес.</summary>
    public Address? PostalAddress { get; set; }

    /// <summary>Фактический адрес.</summary>
    public Address? ActualAddress { get; set; }

    /// <summary>Тип адреса, используемый по умолчанию.</summary>
    public AddressMainType AddressMainType { get; set; } = DefaultAddressMainType;

    /// <summary>Группа (категория) адресной книги.</summary>
    public long? GroupId { get; set; }

    /// <summary>Признак «Избранный адрес».</summary>
    public bool IsVip { get; set; }

    /// <summary>Флаг доступности в качестве глобального отправителя.</summary>
    public bool IsSender { get; set; }

    /// <summary>Флаг активности записи.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Мягкое удаление: запись исключена из рабочих выборок.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Момент создания записи (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Момент последнего изменения записи (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Возвращает адрес заданного типа.</summary>
    /// <param name="type">Тип адреса.</param>
    /// <returns>Адрес либо <see langword="null"/>, если адрес этого типа не заведён.</returns>
    public Address? GetAddress(AddressMainType type) => type switch
    {
        AddressMainType.Legal => LegalAddress,
        AddressMainType.Postal => PostalAddress,
        AddressMainType.Actual => ActualAddress,
        _ => null,
    };

    /// <summary>Заменяет адрес заданного типа.</summary>
    /// <param name="type">Тип адреса.</param>
    /// <param name="address">Новый адрес; <see langword="null"/> — адрес не заведён.</param>
    public void SetAddress(AddressMainType type, Address? address)
    {
        switch (type)
        {
            case AddressMainType.Legal:
                LegalAddress = address;
                break;
            case AddressMainType.Postal:
                PostalAddress = address;
                break;
            case AddressMainType.Actual:
                ActualAddress = address;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный тип адреса.");
        }
    }

    /// <summary>Помечает запись как удалённую (мягкое удаление).</summary>
    public void MarkDeleted() => IsDeleted = true;

    /// <summary>Возвращает запись из удалённых в рабочее состояние.</summary>
    public void Restore() => IsDeleted = false;
}
