using Picket302.Domain.Contracts;
using Picket302.Domain.Entities;
using Picket302.Domain.ValueObjects;
using Picket302.Shared.Dtos;
using Picket302.Shared.Mapping;
using Xunit;

namespace Picket302.Tests.Shared;

/// <summary>
/// Контрактные проверки преобразования доменной модели в контракт протокола
/// и обратно.
/// </summary>
/// <remarks>
/// <para>
/// Клиент строит контрактные значения из доменных сущностей, служба — обратно.
/// Если преобразование теряет или искажает реквизиты, общий режим расходится
/// с одиночным, поэтому каждый тип проверяется полным round-trip.
/// </para>
/// <para>
/// Проверки не зависят от базы данных: это проверки формы контракта, а не
/// поведения хранилища.
/// </para>
/// </remarks>
public sealed class SharedMappingTests
{
    [Fact]
    public void Адрес_Сохраняет_Все_Реквизиты()
    {
        var адрес = Адрес("101000", "ООО «Ромашка»", "ул. Тверская", "д. 1", "кв. 5", "Москва", "Московская обл.");

        var dto = SharedMapping.КАдресу(адрес);
        var обратно = SharedMapping.ИзАдреса(dto);

        Assert.NotNull(обратно);
        Assert.Equal("101000", обратно!.Zip);
        Assert.Equal(Address.LineCount, обратно.Lines.Count);
        Assert.Equal("ул. Тверская", обратно.Line2);
        Assert.Equal("Москва", обратно.Line5);
        Assert.Equal("Московская обл.", обратно.Line6);
    }

    [Fact]
    public void Адрес_С_Исходной_Строкой_Импорта_Сохраняет_Raw()
    {
        var результат = Address.Create(
            "101000",
            new[] { "а", "б", "в", "г", "д", "е" },
            "исходная-строка");

        Assert.True(результат.IsSuccess);

        var обратно = SharedMapping.ИзАдреса(SharedMapping.КАдресу(результат.Value));

        Assert.NotNull(обратно);
        Assert.Equal("исходная-строка", обратно!.Raw);
    }

    [Fact]
    public void Незаполненный_Индекс_Отклоняется_При_Обратном_Преобразовании()
    {
        var dto = new AddressDto("не-индекс", new[] { "а" }, null);

        Assert.Throws<SharedMappingException>(() => SharedMapping.ИзАдреса(dto));
    }

    [Fact]
    public void Короткий_Список_Строк_Дополняется_Пустыми()
    {
        var dto = new AddressDto("101000", new[] { "одна" }, null);

        var адрес = SharedMapping.ИзАдреса(dto);

        Assert.NotNull(адрес);
        Assert.Equal(Address.LineCount, адрес!.Lines.Count);
        Assert.Equal("одна", адрес.Line1);
        Assert.Equal(string.Empty, адрес.Line6);
    }

    [Fact]
    public void Набор_Срезов_Адресации_Сохраняет_Все_Три_Среза()
    {
        var контрагент = Контрагент();

        var dto = SharedMapping.КНаборуАдресов(контрагент.Addresses);
        var обратно = SharedMapping.ИзНабораАдресов(dto);

        Assert.Equal("101000", обратно.Legal!.Zip);
        Assert.Equal("192000", обратно.Actual!.Zip);
        Assert.Equal("193000", обратно.Postal!.Zip);
        Assert.Equal(AddressMainType.Actual, обратно.MainType);
    }

    [Fact]
    public void Контрагент_Сохраняет_Все_Реквизиты_При_Round_Trip()
    {
        var контрагент = Контрагент();

        var dto = SharedMapping.ККонтрагенту(контрагент, 7);
        var обратно = SharedMapping.ИзКонтрагента(dto);

        Assert.Equal(контрагент.Id, обратно.Id);
        Assert.Equal(контрагент.Type, обратно.Type);
        Assert.Equal(контрагент.Name, обратно.Name);
        Assert.Equal(контрагент.ShortName, обратно.ShortName);
        Assert.Equal(контрагент.Inn, обратно.Inn);
        Assert.Equal(контрагент.Kpp, обратно.Kpp);
        Assert.Equal(контрагент.Ogrn, обратно.Ogrn);
        Assert.Equal(контрагент.BankName, обратно.BankName);
        Assert.Equal(контрагент.PassportSeries, обратно.PassportSeries);
        Assert.Equal(контрагент.PassportNumber, обратно.PassportNumber);
        Assert.Equal(контрагент.BirthPlace, обратно.BirthPlace);
        Assert.Equal(контрагент.Position, обратно.Position);
        Assert.Equal(контрагент.DirectorFio, обратно.DirectorFio);
        Assert.Equal(контрагент.Email, обратно.Email);
        Assert.Equal(контрагент.Phone, обратно.Phone);
        Assert.Equal(контрагент.SpecialNotes, обратно.SpecialNotes);
        Assert.Equal(контрагент.Source, обратно.Source);
        Assert.Equal(контрагент.GroupId, обратно.GroupId);
        Assert.Equal(контрагент.IsVip, обратно.IsVip);
        Assert.Equal(контрагент.IsSender, обратно.IsSender);
        Assert.Equal(контрагент.IsActive, обратно.IsActive);
        Assert.Equal(контрагент.IsDeleted, обратно.IsDeleted);
        Assert.Equal(контрагент.CreatedAt, обратно.CreatedAt);
        Assert.Equal(контрагент.UpdatedAt, обратно.UpdatedAt);
        Assert.Equal(контрагент.Logo, обратно.Logo);
        Assert.Equal(контрагент.AddressMainType, обратно.AddressMainType);
        Assert.Equal("101000", обратно.GetAddress(AddressMainType.Legal)!.Zip);
        Assert.Equal("193000", обратно.GetAddress(AddressMainType.Postal)!.Zip);
    }

    [Fact]
    public void Версия_Контрагента_Не_Теряется_При_Преобразовании()
    {
        var контрагент = Контрагент();

        var dto = SharedMapping.ККонтрагенту(контрагент, 42);

        Assert.Equal(42, dto.Version);
    }

    [Fact]
    public void Группа_Сохраняет_Реквизиты_При_Round_Trip()
    {
        var группа = new Group
        {
            Id = 5,
            Name = "Поставщики",
            Note = "заметка",
            IsDefault = false,
            IsClosed = true,
            ParentId = 2,
            SortOrder = 3,
        };

        var dto = SharedMapping.КГруппе(группа, 4);
        var обратно = SharedMapping.ИзГруппы(dto);

        Assert.Equal(группа.Id, обратно.Id);
        Assert.Equal(группа.Name, обратно.Name);
        Assert.Equal(группа.Note, обратно.Note);
        Assert.Equal(группа.IsDefault, обратно.IsDefault);
        Assert.Equal(группа.IsClosed, обратно.IsClosed);
        Assert.Equal(группа.ParentId, обратно.ParentId);
        Assert.Equal(группа.SortOrder, обратно.SortOrder);
        Assert.Equal(4, dto.Version);
    }

    [Fact]
    public void Папка_Сохраняет_Реквизиты_При_Round_Trip()
    {
        var папка = new Folder
        {
            Id = 9,
            Name = "Отправленные",
            ParentId = 1,
            Note = "заметка",
            SortOrder = 2,
        };

        var dto = SharedMapping.КПапке(папка, 1);
        var обратно = SharedMapping.ИзПапки(dto);

        Assert.Equal(папка.Id, обратно.Id);
        Assert.Equal(папка.Name, обратно.Name);
        Assert.Equal(папка.ParentId, обратно.ParentId);
        Assert.Equal(папка.Note, обратно.Note);
        Assert.Equal(папка.SortOrder, обратно.SortOrder);
    }

    [Fact]
    public void Отправление_Сохраняет_Реквизиты_При_Round_Trip()
    {
        var письмо = new Mail
        {
            Id = 11,
            CounterpartyId = 3,
            SenderId = 2,
            DistributionId = 4,
            EnvelopeType = "C6",
            CopyCount = 2,
            IsVip = true,
            Note = "срочно",
            DeclaredValue = 150.50m,
            Weight = 0.250m,
            Fee = 12.75m,
            Status = MailStatus.Printed,
            NeedNotification = true,
            NeedReturnAddress = false,
            MailCategory = MailCategory.Simple,
            DateCreated = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc),
            DatePrinted = new DateTime(2026, 11, 14, 11, 0, 0, DateTimeKind.Utc),
            IncomingNumber = "ВХ-1",
            OutgoingNumber = "ИСХ-1",
            RussianPostId = "RP-1",
            RussianPostBatch = "B-1",
            FolderId = 6,
        };

        письмо.SelectNotificationForm(NotificationType.Form119);
        письмо.RecipientAddress = Адрес("193000", "Иванов", "ул. Мира", "1", "", "Тверь", "Тверская обл.");

        var dto = SharedMapping.КОтправлению(письмо, 3);
        var обратно = SharedMapping.ИзОтправления(dto);

        Assert.Equal(письмо.Id, обратно.Id);
        Assert.Equal(письмо.CounterpartyId, обратно.CounterpartyId);
        Assert.Equal(письмо.SenderId, обратно.SenderId);
        Assert.Equal(письмо.DistributionId, обратно.DistributionId);
        Assert.Equal(письмо.EnvelopeType, обратно.EnvelopeType);
        Assert.Equal(NotificationType.Form119, обратно.NotificationType);
        Assert.Equal(письмо.CopyCount, обратно.CopyCount);
        Assert.Equal(письмо.IsVip, обратно.IsVip);
        Assert.Equal(письмо.Note, обратно.Note);
        Assert.Equal(письмо.DeclaredValue, обратно.DeclaredValue);
        Assert.Equal(письмо.Weight, обратно.Weight);
        Assert.Equal(письмо.Fee, обратно.Fee);
        Assert.Equal(MailStatus.Printed, обратно.Status);
        Assert.Equal(письмо.NeedNotification, обратно.NeedNotification);
        Assert.Equal(письмо.NeedReturnAddress, обратно.NeedReturnAddress);
        Assert.Equal(письмо.MailCategory, обратно.MailCategory);
        Assert.Equal(письмо.DateCreated, обратно.DateCreated);
        Assert.Equal(письмо.DatePrinted, обратно.DatePrinted);
        Assert.Equal(письмо.OutgoingNumber, обратно.OutgoingNumber);
        Assert.Equal(письмо.FolderId, обратно.FolderId);
        Assert.Equal("193000", обратно.RecipientAddress!.Zip);
        Assert.Equal(3, dto.Version);
    }

    [Fact]
    public void Рассылка_Сохраняет_Реквизиты_При_Round_Trip()
    {
        var рассылка = new Distribution
        {
            Id = 8,
            Name = "Ноябрьская рассылка",
            SenderId = 2,
            Kind = DistributionKind.OneToMany,
            Note = "заметка",
            State = DistributionState.Saved,
            EnvelopesRequired = 12,
            CreatedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
            DispatchedAt = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc),
        };

        var dto = SharedMapping.КРассылке(рассылка, 9, 12);
        var обратно = SharedMapping.ИзРассылки(dto);

        Assert.Equal(рассылка.Id, обратно.Id);
        Assert.Equal(рассылка.Name, обратно.Name);
        Assert.Equal(рассылка.SenderId, обратно.SenderId);
        Assert.Equal(рассылка.Kind, обратно.Kind);
        Assert.Equal(рассылка.Note, обратно.Note);
        Assert.Equal(рассылка.State, обратно.State);
        Assert.Equal(рассылка.EnvelopesRequired, обратно.EnvelopesRequired);
        Assert.Equal(рассылка.CreatedAt, обратно.CreatedAt);
        Assert.Equal(рассылка.DispatchedAt, обратно.DispatchedAt);
        Assert.Equal(12, dto.RecipientCount);
    }

    [Fact]
    public void Шаблон_Не_Передаёт_Путь_К_Файлу_На_Сервере()
    {
        var шаблон = new PrintTemplate
        {
            Id = 3,
            Name = "Конверт C6",
            Kind = PrintTemplateKind.Envelope,
            Format = "C6",
            WidthMm = 162m,
            HeightMm = 114m,
            Orientation = TemplateOrientation.Landscape,
            FilePath = @"C:\ProgramData\Picket302\service\templates\packages\custom.lbk",
            IsBuiltin = false,
            Country = "RUS",
        };

        var dto = SharedMapping.КШаблону(шаблон, 1, null);

        // Путь к файлу пакета на сервере клиенту не передаётся: пакет
        // запрашивается по идентификатору и версии.
        Assert.DoesNotContain("ProgramData", System.Text.Json.JsonSerializer.Serialize(dto));
        Assert.Null(dto.PackageId);
        Assert.Equal(0, dto.PackageVersion);
    }

    [Fact]
    public void Шаблон_Сохраняет_Реквизиты_При_Round_Trip()
    {
        var шаблон = new PrintTemplate
        {
            Id = 3,
            Name = "Конверт C6",
            Kind = PrintTemplateKind.Envelope,
            Format = "C6",
            WidthMm = 162m,
            HeightMm = 114m,
            Orientation = TemplateOrientation.Landscape,
            IsBuiltin = true,
            Country = "RUS",
        };

        var dto = SharedMapping.КШаблону(шаблон, 5, null);
        var обратно = SharedMapping.ИзШаблона(dto);

        Assert.Equal(шаблон.Id, обратно.Id);
        Assert.Equal(шаблон.Name, обратно.Name);
        Assert.Equal(шаблон.Kind, обратно.Kind);
        Assert.Equal(шаблон.Format, обратно.Format);
        Assert.Equal(шаблон.WidthMm, обратно.WidthMm);
        Assert.Equal(шаблон.HeightMm, обратно.HeightMm);
        Assert.Equal(шаблон.Orientation, обратно.Orientation);
        Assert.Equal(шаблон.IsBuiltin, обратно.IsBuiltin);
        Assert.Equal(шаблон.Country, обратно.Country);
    }

    [Fact]
    public void Поле_Бланка_Сохраняет_Реквизиты_При_Round_Trip()
    {
        var поле = new BlankField
        {
            Id = 4,
            TemplateId = 3,
            Name = "Индекс",
            Kind = BlankFieldKind.PostIndex,
            Page = 1,
            XMm = 10m,
            YMm = 20m,
            LengthMm = 30m,
            HeightMm = 5m,
            FontName = "Tahoma",
            FontHeightPt = 10m,
            FontHeightMinPt = 6m,
            FontHeightMaxPt = 14m,
            IsBold = true,
            IsItalic = false,
            ColorRgb = 255,
            Align = FieldAlignment.Center,
            MaxChars = 6,
            IsVisible = true,
            IsPrintable = true,
            ZoneRole = "post-index",
            ClipartId = null,
        };

        var dto = SharedMapping.КПолюБланка(поле, 2);
        var обратно = SharedMapping.ИзПоляБланка(dto);

        Assert.Equal(поле.Id, обратно.Id);
        Assert.Equal(поле.TemplateId, обратно.TemplateId);
        Assert.Equal(поле.Name, обратно.Name);
        Assert.Equal(поле.Kind, обратно.Kind);
        Assert.Equal(поле.XMm, обратно.XMm);
        Assert.Equal(поле.YMm, обратно.YMm);
        Assert.Equal(поле.LengthMm, обратно.LengthMm);
        Assert.Equal(поле.HeightMm, обратно.HeightMm);
        Assert.Equal(поле.FontName, обратно.FontName);
        Assert.Equal(поле.FontHeightPt, обратно.FontHeightPt);
        Assert.Equal(поле.IsBold, обратно.IsBold);
        Assert.Equal(поле.ColorRgb, обратно.ColorRgb);
        Assert.Equal(поле.Align, обратно.Align);
        Assert.Equal(поле.MaxChars, обратно.MaxChars);
        Assert.Equal(поле.ZoneRole, обратно.ZoneRole);
        Assert.Equal(2, dto.Version);
    }

    [Fact]
    public void Пользовательское_Поле_Сохраняет_Реквизиты_При_Round_Trip()
    {
        var поле = new CounterpartyField
        {
            Id = 6,
            CounterpartyId = 2,
            FieldName = "Договор",
            FieldValue = "123/45",
            FieldType = CounterpartyFieldType.Text,
            UpdatedAt = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc),
        };

        var dto = SharedMapping.КПолюКонтрагента(поле, 1);
        var обратно = SharedMapping.ИзПоляКонтрагента(dto);

        Assert.Equal(поле.Id, обратно.Id);
        Assert.Equal(поле.CounterpartyId, обратно.CounterpartyId);
        Assert.Equal(поле.FieldName, обратно.FieldName);
        Assert.Equal(поле.FieldValue, обратно.FieldValue);
        Assert.Equal(поле.FieldType, обратно.FieldType);
        Assert.Equal(поле.UpdatedAt, обратно.UpdatedAt);
    }

    [Fact]
    public void Фильтр_Контрагентов_Сохраняет_Условия_При_Round_Trip()
    {
        var фильтр = new CounterpartyFilterDto(
            NameFragment: "Ромашка",
            Inn: "7701234567",
            GroupId: 3,
            IsVip: true,
            IsSender: false,
            IsActive: null,
            IncludeDeleted: true,
            Criteria: new FilterCriteriaDto(
                new[] { new FilterConditionDto("name", FilterOperator.Contains, "Рома") },
                SortColumn: "name",
                SortDescending: true,
                OnlyFavorites: true,
                GroupId: 3),
            SortColumn: "name",
            SortDescending: true);

        var доменный = SharedMapping.КФильтруКонтрагентов(фильтр);

        Assert.Equal("Ромашка", доменный.NameFragment);
        Assert.Equal("7701234567", доменный.Inn);
        Assert.Equal(3, доменный.GroupId);
        Assert.True(доменный.IsVip);
        Assert.False(доменный.IsSender);
        Assert.Null(доменный.IsActive);
        Assert.True(доменный.IncludeDeleted);
        Assert.NotNull(доменный.Criteria);
        Assert.Single(доменный.Criteria!.Conditions);
        Assert.Equal("name", доменный.Criteria.Conditions[0].Field);
        Assert.Equal(FilterOperator.Contains, доменный.Criteria.Conditions[0].Operator);
        Assert.Equal("Рома", доменный.Criteria.Conditions[0].Value);
        Assert.Equal("name", доменный.Criteria.SortColumn);
        Assert.True(доменный.Criteria.SortDescending);
        Assert.True(доменный.Criteria.OnlyFavorites);
    }

    [Fact]
    public void Пустой_Фильтр_Не_Создаёт_Условий()
    {
        var доменный = SharedMapping.КФильтруКонтрагентов(null);

        Assert.Null(доменный.Criteria);
        Assert.Null(доменный.NameFragment);
    }

    private static Counterparty Контрагент()
    {
        var контрагент = new Counterparty
        {
            Id = 2,
            Type = CounterpartyType.LegalEntity,
            Name = "ООО «Ромашка»",
            ShortName = "Ромашка",
            Inn = "7701234567",
            Kpp = "770001001",
            Ogrn = "1157746000000",
            Okfs = "16.10",
            Okpo = "12345678",
            Okved = "16.10",
            Bik = "044525225",
            Ks = "30101810400000000225",
            Rs = "40702810900000000001",
            Rkc = "044525225",
            BankName = "ПАО Сбербанк",
            PassportSeries = "45 06",
            PassportNumber = "123456",
            PassportIssuedBy = "ОВД",
            PassportIssuedDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            BirthDate = new DateTime(1980, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            BirthPlace = "Москва",
            Position = "Генеральный директор",
            DirectorFio = "Иванов Иван Иванович",
            Email = "info@romashka.example",
            Phone = "+7 495 000-00-00",
            SpecialNotes = "важный клиент",
            Source = "Импорт CSV от 01.11.2026 из client.csv",
            Logo = new byte[] { 1, 2, 3 },
            GroupId = 4,
            IsVip = true,
            IsSender = true,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        контрагент.SetAddress(AddressMainType.Legal, Адрес("101000", "ООО «Ромашка»", "ул. Тверская", "д. 1", "", "Москва", "Московская обл."));
        контрагент.SetAddress(AddressMainType.Actual, Адрес("192000", "ООО «Ромашка»", "пр. Мира", "д. 2", "", "Санкт-Петербург", "Ленинградская обл."));
        контрагент.SetAddress(AddressMainType.Postal, Адрес("193000", "ООО «Ромашка»", "ул. Кирова", "д. 3", "", "Тверь", "Тверская обл."));
        контрагент.AddressMainType = AddressMainType.Actual;

        return контрагент;
    }

    private static Address Адрес(string индекс, params string[] строки)
    {
        var полные = new string[Address.LineCount];

        for (var i = 0; i < Address.LineCount; i++)
        {
            полные[i] = i < строки.Length ? строки[i] : string.Empty;
        }

        var результат = Address.Create(индекс, полные);

        Assert.True(результат.IsSuccess);

        return результат.Value;
    }
}
