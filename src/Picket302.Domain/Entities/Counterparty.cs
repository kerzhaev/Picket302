using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Rules;
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
    public Address? LegalAddress
    {
        get => Addresses.Legal;
        set => Addresses.SetAddress(AddressMainType.Legal, value, false);
    }

    /// <summary>Почтовый адрес.</summary>
    public Address? PostalAddress
    {
        get => Addresses.Postal;
        set => Addresses.SetAddress(AddressMainType.Postal, value, false);
    }

    /// <summary>Фактический адрес.</summary>
    public Address? ActualAddress
    {
        get => Addresses.Actual;
        set => Addresses.SetAddress(AddressMainType.Actual, value, false);
    }

    /// <summary>
    /// Три независимых среза адресации записи (ТЗ §8.1, функция 3).
    /// Единственный источник модели адресации контрагента.
    /// </summary>
    public AddressSet Addresses { get; } = new();

    /// <summary>Тип адреса, используемый по умолчанию.</summary>
    public AddressMainType AddressMainType
    {
        get => Addresses.MainType;
        set => Addresses.SetMainType(value);
    }

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
    public Address? GetAddress(AddressMainType type) => Addresses.GetAddress(type);

    /// <summary>Заменяет адрес заданного типа.</summary>
    /// <param name="type">Тип адреса.</param>
    /// <param name="address">Новый адрес; <see langword="null"/> — адрес не заведён.</param>
    public void SetAddress(AddressMainType type, Address? address)
        => Addresses.SetAddress(type, address, false);

    /// <summary>
    /// Заменяет адрес заданного среза и, при <paramref name="makeMain"/>,
    /// назначает этот срез основным (ТЗ §8.1, функция 3).
    /// </summary>
    /// <param name="type">Тип среза адреса.</param>
    /// <param name="address">Новый адрес среза.</param>
    /// <param name="makeMain">Сделать срез основным.</param>
    public void SetAddress(AddressMainType type, Address? address, bool makeMain)
        => Addresses.SetAddress(type, address, makeMain);

    /// <summary>
    /// Устанавливает тип контрагента: юридическое или физическое лицо
    /// (ТЗ §8.1, функция 2).
    /// </summary>
    /// <param name="type">Новый тип контрагента.</param>
    /// <remarks>
    /// Реквизиты юридического лица (КПП, ОГРН, БИК) при смене типа не
    /// уничтожаются: они скрываются в карточке, но остаются в записи, чтобы
    /// смена типа не приводила к потере введённых данных. Проверка «ЮЛ без КПП»
    /// выполняется при сохранении карточки.
    /// </remarks>
    public void SetCounterpartyType(CounterpartyType type)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный тип контрагента.");
        }

        Type = type;
    }

    /// <summary>
    /// Переключает признак «Избранный адрес» (ТЗ §8.1, функция 17).
    /// </summary>
    /// <param name="isVip">Новое значение признака.</param>
    public void ToggleVipStatus(bool isVip) => IsVip = isVip;

    /// <summary>
    /// Помечает запись как отправителя либо снимает признак
    /// (ТЗ §8.1, функция 139).
    /// </summary>
    /// <param name="value">
    /// <see langword="true"/> — запись доступна в справочнике отправителей,
    /// <see langword="false"/> — признак снимается.
    /// </param>
    public void MarkAsSender(bool value) => IsSender = value;

    /// <summary>
    /// Применяет реквизиты карточки к записи, проверяя их правилами
    /// целостности (ТЗ §8.1, функция 1).
    /// </summary>
    /// <param name="dto">Новые реквизиты карточки.</param>
    /// <returns>
    /// Успешный результат с обновлённой карточкой либо текст нарушения
    /// (некорректный ИНН, КПП или БИК).
    /// </returns>
    public Result<Counterparty> UpdateDetails(CounterpartyDetailsDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var проверка = ПроверитьРеквизиты(dto);

        if (проверка.IsFailure)
        {
            return Result<Counterparty>.Failure(проверка.Error!);
        }

        Type = dto.Type;
        Name = dto.Name;
        ShortName = dto.ShortName;
        Inn = dto.Inn;
        Kpp = dto.Kpp;
        Ogrn = dto.Ogrn;
        Okfs = dto.Okfs;
        Okpo = dto.Okpo;
        Okved = dto.Okved;
        Bik = dto.Bik;
        Ks = dto.Ks;
        Rs = dto.Rs;
        Rkc = dto.Rkc;
        BankName = dto.BankName;
        PassportSeries = dto.PassportSeries;
        PassportNumber = dto.PassportNumber;
        PassportIssuedBy = dto.PassportIssuedBy;
        PassportIssuedDate = dto.PassportIssuedDate;
        BirthDate = dto.BirthDate;
        BirthPlace = dto.BirthPlace;
        Position = dto.Position;
        DirectorFio = dto.DirectorFio;
        Email = dto.Email;
        Phone = dto.Phone;
        SpecialNotes = dto.SpecialNotes;
        GroupId = dto.GroupId;
        IsVip = dto.IsVip;
        IsSender = dto.IsSender;
        IsActive = dto.IsActive;

        Addresses.SetAddress(AddressMainType.Legal, dto.LegalAddress, false);
        Addresses.SetAddress(AddressMainType.Actual, dto.ActualAddress, false);
        Addresses.SetAddress(AddressMainType.Postal, dto.PostalAddress, false);
        Addresses.SetMainType(dto.AddressMainType);

        return Result<Counterparty>.Success(this);
    }

    /// <summary>
    /// Проверяет реквизиты карточки: ИНН по контрольным суммам, КПП (9 символов)
    /// и БИК (9 цифр) (ТЗ §8.1, функция 1).
    /// </summary>
    /// <param name="dto">Проверяемые реквизиты.</param>
    /// <returns>Успешный результат либо текст нарушения.</returns>
    private static Result<Counterparty> ПроверитьРеквизиты(CounterpartyDetailsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return Result<Counterparty>.Failure("Наименование контрагента не заполнено.");
        }

        if (!string.IsNullOrWhiteSpace(dto.Inn))
        {
            var проверкаИнн = InnValidator.Validate(dto.Inn, dto.Type);

            if (проверкаИнн.IsFailure)
            {
                return Result<Counterparty>.Failure(проверкаИнн.Error!);
            }
        }

        if (!string.IsNullOrWhiteSpace(dto.Kpp))
        {
            var проверкаКпп = BankRequisitesValidator.ValidateKpp(dto.Kpp);

            if (проверкаКпп.IsFailure)
            {
                return Result<Counterparty>.Failure(проверкаКпп.Error!);
            }
        }

        if (!string.IsNullOrWhiteSpace(dto.Bik))
        {
            var проверкаБик = BankRequisitesValidator.ValidateBik(dto.Bik);

            if (проверкаБик.IsFailure)
            {
                return Result<Counterparty>.Failure(проверкаБик.Error!);
            }
        }

        return Result<Counterparty>.Success(new Counterparty());
    }

    /// <summary>Помечает запись как удалённую (мягкое удаление).</summary>
    public void MarkDeleted() => IsDeleted = true;

    /// <summary>Возвращает запись из удалённых в рабочее состояние.</summary>
    public void Restore() => IsDeleted = false;
}
