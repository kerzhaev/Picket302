using Picket302.Domain.Common;
using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.Repositories;
using Picket302.Domain.ValueObjects;
using Picket302.Shared.Dtos;

namespace Picket302.Shared.Mapping;

/// <summary>
/// Ошибка преобразования данных между доменной моделью и контрактом.
/// </summary>
/// <remarks>
/// Ошибка означает, что присланные клиентом данные не образуют допустимую
/// доменную сущность (например, почтовый индекс не из шести цифр). Служба
/// переводит её в ответ «данные не прошли проверку», а во внутренние
/// подробности не выдаёт.
/// </remarks>
public sealed class SharedMappingException : Exception
{
    /// <summary>
    /// Создаёт ошибку преобразования.
    /// </summary>
    /// <param name="message">Описание расхождения.</param>
    public SharedMappingException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Преобразования между доменной моделью и контрактом совместного режима.
/// </summary>
/// <remarks>
/// <para>
/// Преобразования выполняются в одном месте, чтобы клиент и служба не
/// разошлись в трактовке одних и тех же данных: клиент строит DTO из
/// доменной сущности, служба — обратно.
/// </para>
/// <para>
/// Маркер версии в преобразования не участвует: он хранится отдельно от
/// строк сущностей и подставляется вызывающим кодом при чтении.
/// </para>
/// </remarks>
public static class SharedMapping
{
    /// <summary>
    /// Преобразует адрес в контрактную форму.
    /// </summary>
    /// <param name="адрес">Адрес.</param>
    /// <returns>DTO адреса либо <see langword="null"/>, если адрес не заведён.</returns>
    public static AddressDto? КАдресу(Address? адрес)
        => адрес is null
            ? null
            : new AddressDto(адрес.Zip, адрес.Lines.ToArray(), адрес.Raw);

    /// <summary>
    /// Преобразует контрактную форму адреса в доменную.
    /// </summary>
    /// <param name="dto">DTO адреса.</param>
    /// <returns>Адрес либо <see langword="null"/>, если адрес не задан.</returns>
    /// <exception cref="SharedMappingException">Данные адреса недопустимы.</exception>
    public static Address? ИзАдреса(AddressDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var строки = new string[Address.LineCount];

        for (var i = 0; i < Address.LineCount; i++)
        {
            строки[i] = dto.Lines is not null && i < dto.Lines.Count
                ? dto.Lines[i] ?? string.Empty
                : string.Empty;
        }

        var результат = Address.Create(dto.Zip ?? string.Empty, строки, dto.Raw);

        if (результат.IsFailure)
        {
            throw new SharedMappingException(результат.Error!);
        }

        return результат.Value;
    }

    /// <summary>
    /// Преобразует набор срезов адресации в контрактную форму.
    /// </summary>
    /// <param name="набор">Набор срезов.</param>
    /// <returns>DTO набора срезов.</returns>
    public static AddressSetDto КНаборуАдресов(AddressSet набор) => new(
        КАдресу(набор.Legal),
        КАдресу(набор.Actual),
        КАдресу(набор.Postal),
        набор.MainType);

    /// <summary>
    /// Преобразует контрактную форму набора срезов в доменную.
    /// </summary>
    /// <param name="dto">DTO набора срезов.</param>
    /// <returns>Набор срезов.</returns>
    /// <exception cref="SharedMappingException">Данные адресов недопустимы.</exception>
    public static AddressSet ИзНабораАдресов(AddressSetDto? dto)
    {
        var набор = new AddressSet();

        if (dto is null)
        {
            return набор;
        }

        набор.SetAddress(AddressMainType.Legal, ИзАдреса(dto.Legal), false);
        набор.SetAddress(AddressMainType.Actual, ИзАдреса(dto.Actual), false);
        набор.SetAddress(AddressMainType.Postal, ИзАдреса(dto.Postal), false);
        набор.SetMainType(dto.MainType);

        return набор;
    }

    /// <summary>
    /// Преобразует контрагента в контрактную форму.
    /// </summary>
    /// <param name="контрагент">Контрагент.</param>
    /// <param name="версия">Маркер версии записи.</param>
    /// <returns>DTO контрагента.</returns>
    public static CounterpartyDto ККонтрагенту(Counterparty контрагент, long версия) => new(
        контрагент.Id,
        контрагент.Type,
        контрагент.Name,
        контрагент.ShortName,
        контрагент.Inn,
        контрагент.Kpp,
        контрагент.Ogrn,
        контрагент.Okfs,
        контрагент.Okpo,
        контрагент.Okved,
        контрагент.Bik,
        контрагент.Ks,
        контрагент.Rs,
        контрагент.Rkc,
        контрагент.BankName,
        контрагент.PassportSeries,
        контрагент.PassportNumber,
        контрагент.PassportIssuedBy,
        контрагент.PassportIssuedDate,
        контрагент.BirthDate,
        контрагент.BirthPlace,
        контрагент.Position,
        контрагент.DirectorFio,
        контрагент.Email,
        контрагент.Phone,
        контрагент.SpecialNotes,
        контрагент.Source,
        контрагент.Logo,
        КНаборуАдресов(контрагент.Addresses),
        контрагент.GroupId,
        контрагент.IsVip,
        контрагент.IsSender,
        контрагент.IsActive,
        контрагент.IsDeleted,
        контрагент.CreatedAt,
        контрагент.UpdatedAt,
        версия);

    /// <summary>
    /// Преобразует контрактную форму контрагента в доменную.
    /// </summary>
    /// <param name="dto">DTO контрагента.</param>
    /// <returns>Контрагент.</returns>
    /// <exception cref="SharedMappingException">Данные контрагента недопустимы.</exception>
    public static Counterparty ИзКонтрагента(CounterpartyDto dto)
    {
        var контрагент = new Counterparty
        {
            Id = dto.Id,
            Type = dto.Type,
            Name = dto.Name ?? string.Empty,
            ShortName = dto.ShortName,
            Inn = dto.Inn,
            Kpp = dto.Kpp,
            Ogrn = dto.Ogrn,
            Okfs = dto.Okfs,
            Okpo = dto.Okpo,
            Okved = dto.Okved,
            Bik = dto.Bik,
            Ks = dto.Ks,
            Rs = dto.Rs,
            Rkc = dto.Rkc,
            BankName = dto.BankName,
            PassportSeries = dto.PassportSeries,
            PassportNumber = dto.PassportNumber,
            PassportIssuedBy = dto.PassportIssuedBy,
            PassportIssuedDate = dto.PassportIssuedDate,
            BirthDate = dto.BirthDate,
            BirthPlace = dto.BirthPlace,
            Position = dto.Position,
            DirectorFio = dto.DirectorFio,
            Email = dto.Email,
            Phone = dto.Phone,
            SpecialNotes = dto.SpecialNotes,
            Source = dto.Source,
            Logo = dto.Logo,
            GroupId = dto.GroupId,
            IsVip = dto.IsVip,
            IsSender = dto.IsSender,
            IsActive = dto.IsActive,
            IsDeleted = dto.IsDeleted,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
        };

        var адреса = ИзНабораАдресов(dto.Addresses);

        контрагент.SetAddress(AddressMainType.Legal, адреса.Legal, false);
        контрагент.SetAddress(AddressMainType.Actual, адреса.Actual, false);
        контрагент.SetAddress(AddressMainType.Postal, адреса.Postal, false);
        контрагент.AddressMainType = dto.Addresses?.MainType ?? AddressMainType.Postal;

        return контрагент;
    }

    /// <summary>
    /// Преобразует пользовательское поле контрагента в контрактную форму.
    /// </summary>
    /// <param name="поле">Поле.</param>
    /// <param name="версия">Маркер версии поля.</param>
    /// <returns>DTO поля.</returns>
    public static CounterpartyFieldDto КПолюКонтрагента(CounterpartyField поле, long версия) => new(
        поле.Id,
        поле.CounterpartyId,
        поле.FieldName,
        поле.FieldValue,
        поле.FieldType,
        поле.UpdatedAt,
        версия);

    /// <summary>
    /// Преобразует контрактную форму пользовательского поля в доменную.
    /// </summary>
    /// <param name="dto">DTO поля.</param>
    /// <returns>Поле.</returns>
    public static CounterpartyField ИзПоляКонтрагента(CounterpartyFieldDto dto) => new()
    {
        Id = dto.Id,
        CounterpartyId = dto.CounterpartyId,
        FieldName = dto.FieldName ?? string.Empty,
        FieldValue = dto.FieldValue,
        FieldType = dto.FieldType,
        UpdatedAt = dto.UpdatedAt,
    };

    /// <summary>
    /// Преобразует группу в контрактную форму.
    /// </summary>
    /// <param name="группа">Группа.</param>
    /// <param name="версия">Маркер версии группы.</param>
    /// <returns>DTO группы.</returns>
    public static GroupDto КГруппе(Group группа, long версия) => new(
        группа.Id,
        группа.Name,
        группа.Note,
        группа.IsDefault,
        группа.IsClosed,
        группа.ParentId,
        группа.SortOrder,
        версия);

    /// <summary>
    /// Преобразует контрактную форму группы в доменную.
    /// </summary>
    /// <param name="dto">DTO группы.</param>
    /// <returns>Группа.</returns>
    public static Group ИзГруппы(GroupDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name ?? string.Empty,
        Note = dto.Note,
        IsDefault = dto.IsDefault,
        IsClosed = dto.IsClosed,
        ParentId = dto.ParentId,
        SortOrder = dto.SortOrder,
    };

    /// <summary>
    /// Преобразует папку в контрактную форму.
    /// </summary>
    /// <param name="папка">Папка.</param>
    /// <param name="версия">Маркер версии папки.</param>
    /// <returns>DTO папки.</returns>
    public static FolderDto КПапке(Folder папка, long версия) => new(
        папка.Id,
        папка.Name,
        папка.ParentId,
        папка.Note,
        папка.SortOrder,
        версия);

    /// <summary>
    /// Преобразует контрактную форму папки в доменную.
    /// </summary>
    /// <param name="dto">DTO папки.</param>
    /// <returns>Папка.</returns>
    public static Folder ИзПапки(FolderDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name ?? string.Empty,
        ParentId = dto.ParentId,
        Note = dto.Note,
        SortOrder = dto.SortOrder,
    };

    /// <summary>
    /// Преобразует отправление в контрактную форму.
    /// </summary>
    /// <param name="письмо">Отправление.</param>
    /// <param name="версия">Маркер версии отправления.</param>
    /// <returns>DTO отправления.</returns>
    public static MailDto КОтправлению(Mail письмо, long версия) => new(
        письмо.Id,
        письмо.CounterpartyId,
        письмо.SenderId,
        письмо.DistributionId,
        письмо.EnvelopeType,
        письмо.NoticeType,
        письмо.NotificationType,
        письмо.CopyCount,
        письмо.IsVip,
        письмо.Note,
        письмо.DeclaredValue,
        письмо.Weight,
        письмо.Fee,
        письмо.Status,
        письмо.NeedNotification,
        письмо.NeedReturnAddress,
        письмо.MailCategory,
        письмо.DateCreated,
        письмо.DatePrinted,
        письмо.DateDispatched,
        письмо.DateConfirmation,
        письмо.DateCanceled,
        письмо.IncomingNumber,
        письмо.OutgoingNumber,
        письмо.PostRegisterId,
        письмо.RussianPostId,
        письмо.RussianPostBatch,
        письмо.InventoryId,
        КАдресу(письмо.RecipientAddress),
        письмо.FolderId,
        версия);

    /// <summary>
    /// Преобразует контрактную форму отправления в доменную.
    /// </summary>
    /// <param name="dto">DTO отправления.</param>
    /// <returns>Отправление.</returns>
    /// <exception cref="SharedMappingException">Данные отправления недопустимы.</exception>
    public static Mail ИзОтправления(MailDto dto)
    {
        var письмо = new Mail
        {
            Id = dto.Id,
            CounterpartyId = dto.CounterpartyId,
            SenderId = dto.SenderId,
            DistributionId = dto.DistributionId,
            EnvelopeType = dto.EnvelopeType,
            CopyCount = dto.CopyCount,
            IsVip = dto.IsVip,
            Note = dto.Note,
            DeclaredValue = dto.DeclaredValue,
            Weight = dto.Weight,
            Fee = dto.Fee,
            Status = dto.Status,
            NeedNotification = dto.NeedNotification,
            NeedReturnAddress = dto.NeedReturnAddress,
            MailCategory = dto.MailCategory,
            DateCreated = dto.DateCreated,
            DatePrinted = dto.DatePrinted,
            DateDispatched = dto.DateDispatched,
            DateConfirmation = dto.DateConfirmation,
            DateCanceled = dto.DateCanceled,
            IncomingNumber = dto.IncomingNumber,
            OutgoingNumber = dto.OutgoingNumber,
            PostRegisterId = dto.PostRegisterId,
            RussianPostId = dto.RussianPostId,
            RussianPostBatch = dto.RussianPostBatch,
            InventoryId = dto.InventoryId,
            RecipientAddress = ИзАдреса(dto.RecipientAddress),
            FolderId = dto.FolderId,
        };

        // Вид уведомления и признак «нужно уведомление» хранятся в базе
        // независимо друг от друга: колонка notice_type задаёт вид, отдельный
        // столбец — признак. Вызов SelectNotificationForm выводил бы признак
        // из вида и затирал сохранённое значение, поэтому вид восстанавливается
        // напрямую, а признак переносится как есть.
        if (!string.IsNullOrWhiteSpace(dto.NoticeType))
        {
            письмо.NoticeType = dto.NoticeType;
        }
        else if (dto.NotificationType != NotificationType.None)
        {
            письмо.SelectNotificationForm(dto.NotificationType);
        }

        письмо.NeedNotification = dto.NeedNotification;

        return письмо;
    }

    /// <summary>
    /// Преобразует рассылку в контрактную форму.
    /// </summary>
    /// <param name="рассылка">Рассылка.</param>
    /// <param name="версия">Маркер версии рассылки.</param>
    /// <param name="получателей">Число получателей.</param>
    /// <returns>DTO рассылки.</returns>
    public static DistributionDto КРассылке(Distribution рассылка, long версия, int получателей) => new(
        рассылка.Id,
        рассылка.Name,
        рассылка.SenderId,
        рассылка.Kind,
        рассылка.Note,
        рассылка.State,
        рассылка.EnvelopesRequired,
        рассылка.CreatedAt,
        рассылка.DispatchedAt,
        получателей,
        версия);

    /// <summary>
    /// Преобразует представление рассылки в контрактную форму.
    /// </summary>
    /// <param name="рассылка">Представление рассылки.</param>
    /// <param name="версия">Маркер версии рассылки.</param>
    /// <returns>DTO рассылки.</returns>
    public static DistributionDto КРассылке(DistributionView рассылка, long версия) => new(
        рассылка.Id,
        рассылка.Name,
        рассылка.SenderId,
        рассылка.Kind,
        рассылка.Note,
        рассылка.State,
        рассылка.EnvelopesRequired,
        рассылка.CreatedAt,
        рассылка.DispatchedAt,
        рассылка.RecipientCount,
        версия);

    /// <summary>
    /// Преобразует контрактную форму рассылки в доменную.
    /// </summary>
    /// <param name="dto">DTO рассылки.</param>
    /// <returns>Рассылка.</returns>
    public static Distribution ИзРассылки(DistributionDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name ?? string.Empty,
        SenderId = dto.SenderId,
        Kind = dto.Kind,
        Note = dto.Note,
        State = dto.State,
        EnvelopesRequired = dto.EnvelopesRequired,
        CreatedAt = dto.CreatedAt,
        DispatchedAt = dto.DispatchedAt,
    };

    /// <summary>
    /// Преобразует шаблон в контрактную форму.
    /// </summary>
    /// <param name="шаблон">Шаблон.</param>
    /// <param name="версия">Маркер версии строки шаблона.</param>
    /// <param name="пакет">Метаданные пакета шаблона; пусто — пакет не связан.</param>
    /// <returns>DTO шаблона.</returns>
    public static PrintTemplateDto КШаблону(
        PrintTemplate шаблон,
        long версия,
        TemplatePackageDto? пакет) => new(
        шаблон.Id,
        шаблон.Name,
        шаблон.Kind,
        шаблон.Format,
        шаблон.WidthMm,
        шаблон.HeightMm,
        шаблон.Orientation,
        шаблон.IsBuiltin,
        шаблон.Country,
        пакет?.PackageId,
        пакет?.Version ?? 0,
        пакет?.ContentHash,
        версия);

    /// <summary>
    /// Преобразует контрактную форму шаблона в доменную.
    /// </summary>
    /// <param name="dto">DTO шаблона.</param>
    /// <returns>Шаблон.</returns>
    public static PrintTemplate ИзШаблона(PrintTemplateDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name ?? string.Empty,
        Kind = dto.Kind ?? PrintTemplateKind.Envelope,
        Format = dto.Format ?? string.Empty,
        WidthMm = dto.WidthMm,
        HeightMm = dto.HeightMm,
        Orientation = dto.Orientation,
        IsBuiltin = dto.IsBuiltin,
        Country = dto.Country ?? "RUS",
    };

    /// <summary>
    /// Преобразует поле бланка в контрактную форму.
    /// </summary>
    /// <param name="поле">Поле бланка.</param>
    /// <param name="версия">Маркер версии поля.</param>
    /// <returns>DTO поля бланка.</returns>
    public static BlankFieldDto КПолюБланка(BlankField поле, long версия) => new(
        поле.Id,
        поле.TemplateId,
        поле.Name,
        поле.Kind,
        поле.Page,
        поле.XMm,
        поле.YMm,
        поле.LengthMm,
        поле.HeightMm,
        поле.FontName,
        поле.FontHeightPt,
        поле.FontHeightMinPt,
        поле.FontHeightMaxPt,
        поле.IsBold,
        поле.IsItalic,
        поле.ColorRgb,
        поле.Align,
        поле.MaxChars,
        поле.IsVisible,
        поле.IsPrintable,
        поле.ZoneRole,
        поле.ClipartId,
        версия);

    /// <summary>
    /// Преобразует контрактную форму поля бланка в доменную.
    /// </summary>
    /// <param name="dto">DTO поля бланка.</param>
    /// <returns>Поле бланка.</returns>
    public static BlankField ИзПоляБланка(BlankFieldDto dto) => new()
    {
        Id = dto.Id,
        TemplateId = dto.TemplateId,
        Name = dto.Name ?? string.Empty,
        Kind = dto.Kind ?? BlankFieldKind.Text,
        Page = dto.Page,
        XMm = dto.XMm,
        YMm = dto.YMm,
        LengthMm = dto.LengthMm,
        HeightMm = dto.HeightMm,
        FontName = dto.FontName ?? "Tahoma",
        FontHeightPt = dto.FontHeightPt,
        FontHeightMinPt = dto.FontHeightMinPt,
        FontHeightMaxPt = dto.FontHeightMaxPt,
        IsBold = dto.IsBold,
        IsItalic = dto.IsItalic,
        ColorRgb = dto.ColorRgb,
        Align = dto.Align,
        MaxChars = dto.MaxChars,
        IsVisible = dto.IsVisible,
        IsPrintable = dto.IsPrintable,
        ZoneRole = dto.ZoneRole,
        ClipartId = dto.ClipartId,
    };

    /// <summary>
    /// Преобразует сессию печати в контрактную форму.
    /// </summary>
    /// <param name="сессия">Сессия печати.</param>
    /// <param name="устройство">Идентификатор устройства-клиента.</param>
    /// <returns>DTO сессии печати.</returns>
    public static PrintJobDto КСессииПечати(PrintJob сессия, string? устройство) => new(
        сессия.Id,
        сессия.CreatedAt,
        сессия.TemplateId,
        сессия.PrinterName,
        сессия.PaperSizeName,
        сессия.PaperWidthMm,
        сессия.PaperHeightMm,
        сессия.FontInfo,
        сессия.Copies,
        сессия.PrintMode,
        сессия.Status,
        сессия.ErrorText,
        устройство);

    /// <summary>
    /// Преобразует запись журнала печати в контрактную форму.
    /// </summary>
    /// <param name="запись">Запись журнала.</param>
    /// <param name="устройство">Идентификатор устройства-клиента.</param>
    /// <returns>DTO записи журнала.</returns>
    public static PrintHistoryDto КИсторииПечати(PrintHistory запись, string? устройство) => new(
        запись.Id,
        запись.PrintedAt,
        запись.MailId,
        запись.RecipientAddressSnapshot,
        запись.SenderAddressSnapshot,
        запись.CopyCount,
        запись.IsVip,
        запись.Note,
        запись.TemplateDescription,
        запись.PrintJobId,
        устройство,
        запись.Outcome);

    /// <summary>
    /// Преобразует фильтр выборки контрагентов в доменный.
    /// </summary>
    /// <param name="dto">DTO фильтра.</param>
    /// <returns>Доменный фильтр.</returns>
    public static CounterpartyFilter КФильтруКонтрагентов(CounterpartyFilterDto? dto)
    {
        if (dto is null)
        {
            return new CounterpartyFilter();
        }

        return new CounterpartyFilter(
            dto.NameFragment,
            dto.Inn,
            dto.GroupId,
            dto.IsVip,
            dto.IsSender,
            dto.IsActive,
            dto.IncludeDeleted,
            ККритериям(dto.Criteria),
            dto.SortColumn,
            dto.SortDescending);
    }

    /// <summary>
    /// Преобразует контрактные условия фильтра в доменные.
    /// </summary>
    /// <param name="dto">DTO условий.</param>
    /// <returns>Доменные условия.</returns>
    public static FilterCriteria? ККритериям(FilterCriteriaDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var критерии = new FilterCriteria
        {
            SortColumn = dto.SortColumn,
            SortDescending = dto.SortDescending,
            OnlyFavorites = dto.OnlyFavorites,
            GroupId = dto.GroupId,
        };

        foreach (var условие in dto.Conditions ?? Array.Empty<FilterConditionDto>())
        {
            критерии.Conditions.Add(new FilterCriteria.Condition
            {
                Field = условие.Field,
                Operator = условие.Operator,
                Value = условие.Value,
            });
        }

        return критерии;
    }

    /// <summary>
    /// Преобразует доменный фильтр выборки контрагентов в контрактную форму.
    /// </summary>
    /// <param name="фильтр">Доменный фильтр.</param>
    /// <returns>Фильтр контракта.</returns>
    public static CounterpartyFilterDto? КФильтруКонтрагентовDto(CounterpartyFilter? фильтр)
    {
        if (фильтр is null)
        {
            return null;
        }

        var условия = фильтр.Criteria?.Conditions
            .Select(условие => new FilterConditionDto(
                условие.Field,
                условие.Operator,
                условие.Value))
            .ToArray();

        return new CounterpartyFilterDto(
            фильтр.NameFragment,
            фильтр.Inn,
            фильтр.GroupId,
            фильтр.IsVip,
            фильтр.IsSender,
            фильтр.IsActive,
            фильтр.IncludeDeleted,
            условия is null
                ? null
                : new FilterCriteriaDto(
                    условия,
                    фильтр.Criteria?.SortColumn,
                    фильтр.Criteria?.SortDescending ?? false,
                    фильтр.Criteria?.OnlyFavorites ?? false,
                    фильтр.Criteria?.GroupId),
            фильтр.SortColumn,
            фильтр.SortDescending);
    }

    /// <summary>
    /// Преобразует доменный фильтр выборки отправлений в контрактную форму.
    /// </summary>
    /// <param name="фильтр">Доменный фильтр.</param>
    /// <returns>Фильтр контракта.</returns>
    public static MailFilterDto? КФильтруОтправленийDto(MailFilter? фильтр) => фильтр is null
        ? null
        : new MailFilterDto(
            фильтр.Status,
            фильтр.CounterpartyId,
            фильтр.SenderId,
            фильтр.DistributionId,
            фильтр.PostRegisterId,
            фильтр.DateFrom,
            фильтр.DateTo);

    /// <summary>
    /// Преобразует доменный фильтр выборки рассылок в контрактную форму.
    /// </summary>
    /// <param name="фильтр">Доменный фильтр.</param>
    /// <returns>Фильтр контракта.</returns>
    public static DistributionFilterDto? КФильтруРассылокDto(DistributionFilter? фильтр) => фильтр is null
        ? null
        : new DistributionFilterDto(
            фильтр.CreatedFrom,
            фильтр.State,
            фильтр.SenderId);

    /// <summary>
    /// Преобразует фильтр выборки отправлений в доменный.
    /// </summary>
    /// <param name="dto">DTO фильтра.</param>
    /// <returns>Доменный фильтр.</returns>
    public static MailFilter КФильтруОтправлений(MailFilterDto? dto) => dto is null
        ? new MailFilter()
        : new MailFilter(
            dto.Status,
            dto.CounterpartyId,
            dto.SenderId,
            dto.DistributionId,
            dto.PostRegisterId,
            dto.DateFrom,
            dto.DateTo);

    /// <summary>
    /// Преобразует фильтр выборки рассылок в доменный.
    /// </summary>
    /// <param name="dto">DTO фильтра.</param>
    /// <returns>Доменный фильтр.</returns>
    public static DistributionFilter КФильтруРассылок(DistributionFilterDto? dto) => dto is null
        ? new DistributionFilter()
        : new DistributionFilter(dto.CreatedFrom, dto.State, dto.SenderId);

    /// <summary>
    /// Приводит страницу доменных значений к контрактной форме.
    /// </summary>
    /// <typeparam name="T">Тип доменного значения.</typeparam>
    /// <typeparam name="TDto">Тип контрактного значения.</typeparam>
    /// <param name="страница">Страница доменных значений.</param>
    /// <param name="преобразователь">Преобразователь значений.</param>
    /// <returns>Страница контрактных значений.</returns>
    public static PageDto<TDto> КСтранице<T, TDto>(
        Page<T> страница,
        Func<T, TDto> преобразователь) => new(
        страница.Items.Select(преобразователь).ToArray(),
        страница.Offset,
        страница.Limit,
        страница.TotalCount);
}
